using System.Drawing;

namespace VrPrivacy;

internal sealed class MainForm : Form
{
    private static readonly Guid MonitorGuid = new("8d6a8a70-67e9-4af0-9e57-0fcb401ca31b");

    private readonly ComboBox _modeCombo = new()
    {
        Name = "modeCombo",
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly CheckBox _primaryCheck = new()
    {
        Name = "primaryCheck",
        Text = "Make primary",
        Checked = true,
        AutoSize = true
    };
    private readonly CheckBox _routingCheck = new()
    {
        Name = "routingCheck",
        Text = "Route new windows",
        Checked = true,
        AutoSize = true
    };
    private readonly Button _startStopButton = new()
    {
        Name = "startStopButton",
        Text = "Start",
        AutoSize = true,
        Anchor = AnchorStyles.Right
    };
    private readonly Label _statusLabel = new()
    {
        Name = "statusLabel",
        Text = "Stopped",
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private MonitorSession? _session;
    private bool _transitioning;
    private bool _closing;
    private bool _allowClose;

    internal MainForm()
    {
        Text = "VR Privacy";
        ClientSize = new Size(460, 210);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = "Display mode",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 7, 12, 3)
        }, 0, 0);
        layout.Controls.Add(_modeCombo, 1, 0);
        layout.Controls.Add(_primaryCheck, 1, 1);
        layout.Controls.Add(_routingCheck, 1, 2);
        layout.Controls.Add(_statusLabel, 0, 3);
        layout.SetColumnSpan(_statusLabel, 2);
        layout.Controls.Add(_startStopButton, 1, 4);
        Controls.Add(layout);

        LoadModes();
        _startStopButton.Click += async (_, _) => await ToggleAsync();
        FormClosing += OnFormClosing;
    }

    private void LoadModes()
    {
        _modeCombo.Items.Add(new ModeChoice("Copy primary", null));
        try
        {
            foreach (var mode in DisplayController.GetModeChoices())
                _modeCombo.Items.Add(new ModeChoice(mode.ToString(), mode));
        }
        catch (Exception exception)
        {
            _statusLabel.Text = $"Mode discovery failed: {exception.Message}";
        }

        _modeCombo.SelectedIndex = 0;
    }

    private async Task ToggleAsync()
    {
        if (_session is null)
            await StartAsync();
        else
            await StopAsync();
    }

    private async Task StartAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_session is not null)
                return;

            _transitioning = true;
            SetUiState("Starting...", true, false);

            DisplaySnapshot? snapshot = null;
            SudoVdaClient? driver = null;
            CancellationTokenSource? watchdogCancellation = null;
            Task watchdogTask = Task.CompletedTask;
            WindowRouter? router = null;
            var added = false;

            try
            {
                snapshot = DisplayController.Capture();
                var selected = (ModeChoice)_modeCombo.SelectedItem!;
                var mode = selected.Mode ?? snapshot.Displays.Single(display => display.Primary).Mode;
                if (!DisplayController.IsSupported(mode))
                    throw new InvalidOperationException($"Unsupported display mode: {mode}.");

                driver = SudoVdaClient.Open();
                var watchdog = driver.GetWatchdog();
                var addedDisplay = driver.Add(mode, MonitorGuid);
                added = true;

                watchdogCancellation = new CancellationTokenSource();
                watchdogTask = RunWatchdogAsync(driver, watchdog.Timeout, watchdogCancellation.Token);

                var deviceName = await DisplayController.WaitForDisplayAsync(
                    addedDisplay,
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None);
                var bounds = DisplayController.PlaceAndSetPrimary(deviceName, mode, _primaryCheck.Checked);

                if (_routingCheck.Checked)
                    router = WindowRouter.Start(bounds, ReportBackgroundError);

                _session = new MonitorSession(
                    MonitorGuid,
                    driver,
                    snapshot,
                    deviceName,
                    watchdogCancellation,
                    watchdogTask,
                    router);

                driver = null;
                watchdogCancellation = null;
                router = null;
                SetUiState($"Active: {deviceName} — {mode}", false, true);
            }
            catch (Exception exception)
            {
                var cleanupErrors = await CleanupPartialStartAsync(
                    snapshot,
                    driver,
                    added,
                    watchdogCancellation,
                    watchdogTask,
                    router);
                var suffix = cleanupErrors.Count == 0
                    ? string.Empty
                    : $" Cleanup: {string.Join(" | ", cleanupErrors)}";
                SetUiState($"Start failed: {exception.Message}{suffix}", false, false);
            }
        }
        finally
        {
            _transitioning = false;
            _lifecycleGate.Release();
        }
    }

    private async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            var session = _session;
            if (session is null)
                return;

            _transitioning = true;
            SetUiState("Stopping...", true, false);
            var errors = new List<string>();
            var removed = false;

            await TryCleanupAsync(async () =>
            {
                if (session.Router is not null)
                    await session.Router.DisposeAsync();
            }, "stop window routing", errors);
            await TryCleanupAsync(() =>
            {
                DisplayController.Restore(session.Snapshot);
                return Task.CompletedTask;
            }, "restore display topology", errors);

            session.WatchdogCancellation.Cancel();
            await TryCleanupAsync(
                () => session.WatchdogTask,
                "stop watchdog",
                errors);
            try
            {
                session.Driver.Remove(session.MonitorGuid);
                removed = true;
            }
            catch (Exception exception)
            {
                errors.Add($"remove virtual display: {exception.Message}");
            }

            if (removed)
            {
                session.WatchdogCancellation.Dispose();
                session.Driver.Dispose();
                _session = null;
                SetUiState(
                    errors.Count == 0 ? "Stopped" : $"Stopped with errors: {string.Join(" | ", errors)}",
                    false,
                    false);
            }
            else
            {
                SetUiState($"Stop failed: {string.Join(" | ", errors)}", false, true);
            }
        }
        finally
        {
            _transitioning = false;
            _lifecycleGate.Release();
        }
    }

    private static async Task<List<string>> CleanupPartialStartAsync(
        DisplaySnapshot? snapshot,
        SudoVdaClient? driver,
        bool added,
        CancellationTokenSource? watchdogCancellation,
        Task watchdogTask,
        WindowRouter? router)
    {
        var errors = new List<string>();

        await TryCleanupAsync(async () =>
        {
            if (router is not null)
                await router.DisposeAsync();
        }, "stop window routing", errors);

        if (snapshot is not null)
        {
            await TryCleanupAsync(() =>
            {
                DisplayController.Restore(snapshot);
                return Task.CompletedTask;
            }, "restore display topology", errors);
        }

        watchdogCancellation?.Cancel();
        await TryCleanupAsync(() => watchdogTask, "stop watchdog", errors);

        if (added && driver is not null)
        {
            await TryCleanupAsync(() =>
            {
                driver.Remove(MonitorGuid);
                return Task.CompletedTask;
            }, "remove virtual display", errors);
        }

        watchdogCancellation?.Dispose();
        driver?.Dispose();
        return errors;
    }

    private async Task RunWatchdogAsync(
        SudoVdaClient driver,
        uint timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (timeoutSeconds == 0)
            return;

        var interval = TimeSpan.FromMilliseconds(Math.Max(250, timeoutSeconds * 1000d / 3d));
        try
        {
            while (true)
            {
                await Task.Delay(interval, cancellationToken);
                driver.Ping();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportBackgroundError($"SudoVDA watchdog failed: {exception.Message}");
        }
    }

    private static async Task TryCleanupAsync(
        Func<Task> action,
        string operation,
        ICollection<string> errors)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            errors.Add($"{operation}: {exception.Message}");
        }
    }

    private void ReportBackgroundError(string message)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(() => ReportBackgroundError(message));
            return;
        }

        _statusLabel.Text = message;
    }

    private void SetUiState(string status, bool busy, bool active)
    {
        _statusLabel.Text = status;
        _modeCombo.Enabled = !busy && !active;
        _primaryCheck.Enabled = !busy && !active;
        _routingCheck.Enabled = !busy && !active;
        _startStopButton.Enabled = !busy;
        _startStopButton.Text = active ? "Stop" : "Start";
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose || (_session is null && !_transitioning))
            return;

        eventArgs.Cancel = true;
        if (_closing)
            return;

        _closing = true;
        BeginInvoke(async () =>
        {
            await StopAsync();
            if (_session is null)
            {
                _allowClose = true;
                Close();
            }
            else
            {
                _closing = false;
            }
        });
    }

    private sealed record ModeChoice(string Label, DisplayMode? Mode)
    {
        public override string ToString() => Label;
    }

    private sealed record MonitorSession(
        Guid MonitorGuid,
        SudoVdaClient Driver,
        DisplaySnapshot Snapshot,
        string DeviceName,
        CancellationTokenSource WatchdogCancellation,
        Task WatchdogTask,
        WindowRouter? Router);
}
