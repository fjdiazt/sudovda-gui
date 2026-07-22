using System.Drawing;

namespace SudoVDA.GUI;

internal sealed class MainForm : Form
{
    private static readonly Guid MonitorGuid = new("8d6a8a70-67e9-4af0-9e57-0fcb401ca31b");

    private readonly ComboBox _aspectCombo = new()
    {
        Name = "aspectCombo",
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };

    private readonly ComboBox _presetCombo = new()
    {
        Name = "presetCombo",
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly TextBox _widthText = new()
    {
        Name = "widthText",
        Dock = DockStyle.Fill
    };
    private readonly TextBox _heightText = new()
    {
        Name = "heightText",
        Dock = DockStyle.Fill
    };
    private readonly CheckBox _aspectLockButton = new()
    {
        Name = "aspectLockButton",
        Appearance = Appearance.Button,
        Text = "🔓",
        AccessibleName = "Lock aspect ratio",
        TextAlign = ContentAlignment.MiddleCenter,
        Dock = DockStyle.Fill,
        AutoSize = true
    };
    private readonly ToolTip _toolTip = new();
    private readonly ComboBox _refreshCombo = new()
    {
        Name = "refreshCombo",
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly ErrorProvider _resolutionErrors = new()
    {
        BlinkStyle = ErrorBlinkStyle.NeverBlink
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
    private readonly Label _statusIndicator = new()
    {
        Name = "statusIndicator",
        Text = "●",
        AutoSize = true,
        ForeColor = Color.Firebrick,
        Anchor = AnchorStyles.Left
    };
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly DisplayMode _primaryMode;
    private readonly Action<UserSettings> _saveSettings;

    private IReadOnlyList<ResolutionSize> _availableSizes = [];
    private ResolutionSize? _lockedAspectRatio;
    private UserSettings _lastValidSettings;
    private bool _suppressResolutionEvents;
    private bool _modeValid;
    private bool _busy;

    private MonitorSession? _session;
    private bool _transitioning;
    private bool _closing;
    private bool _allowClose;

    internal MainForm() : this(null, null, null, null)
    {
    }

    internal MainForm(
        DisplayMode? primaryMode,
        UserSettings? settings,
        IReadOnlyList<DisplayMode>? availableModes,
        Action<UserSettings>? saveSettings)
    {
        _primaryMode = primaryMode ?? DisplayController.GetPrimaryMode();
        _lastValidSettings = settings ?? UserSettingsStore.Load(_primaryMode);
        _saveSettings = saveSettings ?? (value => UserSettingsStore.Save(value));

        Text = "SudoVDA";
        ClientSize = new Size(500, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        _resolutionErrors.ContainerControl = this;

        var resolutionLayout = new TableLayoutPanel
        {
            Name = "resolutionLayout",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 6,
            Padding = new Padding(8)
        };
        resolutionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
        resolutionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
        resolutionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        resolutionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
        var aspectLabel = CreateFieldLabel("aspectLabel", "Aspect ratio");
        resolutionLayout.Controls.Add(aspectLabel, 0, 0);
        resolutionLayout.SetColumnSpan(aspectLabel, 4);
        resolutionLayout.Controls.Add(_aspectCombo, 0, 1);
        resolutionLayout.SetColumnSpan(_aspectCombo, 4);
        var presetLabel = CreateFieldLabel("presetLabel", "Resolution preset");
        resolutionLayout.Controls.Add(presetLabel, 0, 2);
        resolutionLayout.SetColumnSpan(presetLabel, 4);
        resolutionLayout.Controls.Add(_presetCombo, 0, 3);
        resolutionLayout.SetColumnSpan(_presetCombo, 4);
        resolutionLayout.Controls.Add(CreateFieldLabel("widthLabel", "Width"), 0, 4);
        resolutionLayout.Controls.Add(CreateFieldLabel("heightLabel", "Height"), 1, 4);
        resolutionLayout.Controls.Add(CreateFieldLabel("refreshLabel", "Refresh rate"), 3, 4);
        resolutionLayout.Controls.Add(_widthText, 0, 5);
        resolutionLayout.Controls.Add(_heightText, 1, 5);
        resolutionLayout.Controls.Add(_aspectLockButton, 2, 5);
        resolutionLayout.Controls.Add(_refreshCombo, 3, 5);

        var displayGroup = new GroupBox
        {
            Name = "displayGroup",
            Text = "Display",
            Dock = DockStyle.Fill,
            AutoSize = true
        };
        displayGroup.Controls.Add(resolutionLayout);

        var behaviorFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 4, 8, 4)
        };
        behaviorFlow.Controls.Add(_primaryCheck);
        behaviorFlow.Controls.Add(_routingCheck);
        var behaviorGroup = new GroupBox
        {
            Name = "behaviorGroup",
            Text = "Behavior",
            Dock = DockStyle.Fill,
            AutoSize = true
        };
        behaviorGroup.Controls.Add(behaviorFlow);

        var footer = new TableLayoutPanel
        {
            Name = "footerLayout",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_statusIndicator, 0, 0);
        footer.Controls.Add(_statusLabel, 1, 0);
        footer.Controls.Add(_startStopButton, 2, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(displayGroup, 0, 0);
        layout.Controls.Add(behaviorGroup, 0, 1);
        layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);

        LoadResolutionControls(availableModes ?? DisplayController.GetModeChoices(), _lastValidSettings);
        _aspectCombo.SelectedIndexChanged += (_, _) => OnAspectChanged();
        _presetCombo.SelectedIndexChanged += (_, _) => OnPresetChanged();
        _widthText.TextChanged += (_, _) => OnDimensionChanged(true);
        _heightText.TextChanged += (_, _) => OnDimensionChanged(false);
        _aspectLockButton.CheckedChanged += (_, _) => OnAspectLockChanged();
        _refreshCombo.SelectedIndexChanged += (_, _) => ValidateResolution();
        _primaryCheck.CheckedChanged += (_, _) => UpdatePersistedChecks();
        _routingCheck.CheckedChanged += (_, _) => UpdatePersistedChecks();
        _startStopButton.Click += async (_, _) => await ToggleAsync();
        FormClosing += OnFormClosing;
        UpdateAspectLockButton();
        ValidateResolution();
    }

    private static Label CreateFieldLabel(string name, string text) => new()
    {
        Name = name,
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left
    };

    private void LoadResolutionControls(IReadOnlyList<DisplayMode> modes, UserSettings settings)
    {
        _suppressResolutionEvents = true;
        _availableSizes = ResolutionOptions.DistinctSizes(modes);
        _aspectCombo.Items.Add(new AspectFilterChoice("All aspect ratios", null));
        foreach (var ratio in ResolutionOptions.AspectRatios(_availableSizes))
            _aspectCombo.Items.Add(new AspectFilterChoice(ratio.FilterLabel, ratio));
        _aspectCombo.SelectedIndex = 0;
        RebuildPresetChoices(settings.Preset, false);

        foreach (var rate in ResolutionOptions.RefreshRates(_primaryMode.RefreshHz))
            _refreshCombo.Items.Add(rate);

        var choice = _presetCombo.Items
            .Cast<PresetChoice>()
            .SingleOrDefault(item => item.Key == settings.Preset) ??
            _presetCombo.Items.Cast<PresetChoice>().Single(item => item.Key == UserSettings.Custom);
        _presetCombo.SelectedItem = choice;
        var initialMode = choice.Key == UserSettings.CopyPrimary
            ? _primaryMode
            : new DisplayMode(settings.Width, settings.Height, settings.RefreshHz);
        SetModeText(initialMode, true);
        _primaryCheck.Checked = settings.MakePrimary;
        _routingCheck.Checked = settings.RouteNewWindows;
        _suppressResolutionEvents = false;
    }

    private void OnAspectChanged()
    {
        if (_suppressResolutionEvents)
            return;

        var preferredKey = _presetCombo.SelectedItem is PresetChoice { Size: not null } choice
            ? choice.Key
            : null;
        RebuildPresetChoices(preferredKey, true);
        OnPresetChanged();
    }

    private void RebuildPresetChoices(string? preferredKey, bool selectFirstNormal)
    {
        var suppressed = _suppressResolutionEvents;
        _suppressResolutionEvents = true;
        var ratio = (_aspectCombo.SelectedItem as AspectFilterChoice)?.Ratio;
        var sizes = ratio is null
            ? _availableSizes
            : _availableSizes.Where(size => ResolutionOptions.AspectRatio(size) == ratio).ToArray();

        _presetCombo.BeginUpdate();
        _presetCombo.Items.Clear();
        _presetCombo.Items.Add(new PresetChoice(
            "Match primary display", UserSettings.CopyPrimary, null));
        foreach (var size in sizes)
        {
            var aspect = ResolutionOptions.AspectRatio(size);
            var label = ratio is null
                ? $"{size} ({aspect.ResolutionLabel})"
                : size.ToString();
            _presetCombo.Items.Add(new PresetChoice(label, size.Key, size));
        }
        _presetCombo.Items.Add(new PresetChoice("Custom", UserSettings.Custom, null));

        var choice = _presetCombo.Items.Cast<PresetChoice>()
            .SingleOrDefault(item => item.Key == preferredKey);
        if (selectFirstNormal && choice?.Size is null)
            choice = _presetCombo.Items.Cast<PresetChoice>().FirstOrDefault(item => item.Size is not null);
        choice ??= _presetCombo.Items.Cast<PresetChoice>()
            .FirstOrDefault(item => item.Size is not null);
        choice ??= _presetCombo.Items.Cast<PresetChoice>()
            .Single(item => item.Key == UserSettings.CopyPrimary);
        _presetCombo.SelectedItem = choice;
        _presetCombo.EndUpdate();
        _suppressResolutionEvents = suppressed;
    }

    private void OnPresetChanged()
    {
        if (_suppressResolutionEvents || _presetCombo.SelectedItem is not PresetChoice choice)
            return;

        if (choice.Key == UserSettings.CopyPrimary)
            SetModeText(_primaryMode, true);
        else if (choice.Size is { } size)
            SetModeText(new DisplayMode(size.Width, size.Height, SelectedRefresh()), false);
        ValidateResolution();
    }

    private void OnDimensionChanged(bool widthChanged)
    {
        if (_suppressResolutionEvents)
            return;

        if (_aspectLockButton.Checked &&
            _lockedAspectRatio is { } ratio &&
            TryScaleLockedDimension(
                widthChanged ? _widthText.Text : _heightText.Text,
                ratio,
                widthChanged,
                out var scaled))
        {
            var suppressed = _suppressResolutionEvents;
            _suppressResolutionEvents = true;
            if (widthChanged)
                _heightText.Text = scaled.ToString();
            else
                _widthText.Text = scaled.ToString();
            _suppressResolutionEvents = suppressed;
        }

        SelectPreset(UserSettings.Custom);
        ValidateResolution();
    }

    private void OnAspectLockChanged()
    {
        if (_suppressResolutionEvents)
            return;

        if (_aspectLockButton.Checked && TryReadMode(out var mode))
        {
            _lockedAspectRatio = new ResolutionSize(mode.Width, mode.Height);
        }
        else
        {
            _lockedAspectRatio = null;
            if (_aspectLockButton.Checked)
            {
                _suppressResolutionEvents = true;
                _aspectLockButton.Checked = false;
                _suppressResolutionEvents = false;
            }
        }

        UpdateAspectLockButton();
    }

    private static bool TryScaleLockedDimension(
        string text,
        ResolutionSize ratio,
        bool widthChanged,
        out uint scaled)
    {
        scaled = 0;
        if (!uint.TryParse(text.Trim(), out var input))
            return false;

        var value = widthChanged
            ? input * (double)ratio.Height / ratio.Width
            : input * (double)ratio.Width / ratio.Height;
        if (value < 1 || value > uint.MaxValue)
            return false;

        scaled = (uint)Math.Round(value, MidpointRounding.AwayFromZero);
        return true;
    }

    private void UpdateAspectLockButton()
    {
        var locked = _aspectLockButton.Checked && _lockedAspectRatio is not null;
        _aspectLockButton.Text = locked ? "🔒" : "🔓";
        _aspectLockButton.AccessibleName = locked
            ? "Unlock aspect ratio"
            : "Lock aspect ratio";
        _toolTip.SetToolTip(_aspectLockButton, _aspectLockButton.AccessibleName);
    }

    private void SetModeText(DisplayMode mode, bool copyRefresh)
    {
        var suppressed = _suppressResolutionEvents;
        _suppressResolutionEvents = true;
        _widthText.Text = mode.Width.ToString();
        _heightText.Text = mode.Height.ToString();
        if (copyRefresh)
            _refreshCombo.SelectedItem = mode.RefreshHz;
        if (_aspectLockButton.Checked)
            _lockedAspectRatio = new ResolutionSize(mode.Width, mode.Height);
        _suppressResolutionEvents = suppressed;
    }

    private void SelectPreset(string key)
    {
        var suppressed = _suppressResolutionEvents;
        _suppressResolutionEvents = true;
        _presetCombo.SelectedItem = _presetCombo.Items
            .Cast<PresetChoice>()
            .Single(item => item.Key == key);
        _suppressResolutionEvents = suppressed;
    }

    private uint SelectedRefresh() => _refreshCombo.SelectedItem is uint value ? value : 60;

    private bool TryReadMode(out DisplayMode mode)
    {
        var valid = ResolutionOptions.TryParseMode(
            _widthText.Text,
            _heightText.Text,
            SelectedRefresh(),
            out mode,
            out var widthError,
            out var heightError);
        _resolutionErrors.SetError(_widthText, widthError);
        _resolutionErrors.SetError(_heightText, heightError);
        return valid;
    }

    private void ValidateResolution()
    {
        _modeValid = TryReadMode(out var mode);
        if (_modeValid && _presetCombo.SelectedItem is PresetChoice choice)
        {
            _lastValidSettings = new(
                choice.Key,
                mode.Width,
                mode.Height,
                mode.RefreshHz,
                _primaryCheck.Checked,
                _routingCheck.Checked);
        }
        UpdateStartStopEnabled();
    }

    private void UpdatePersistedChecks()
    {
        _lastValidSettings = _lastValidSettings with
        {
            MakePrimary = _primaryCheck.Checked,
            RouteNewWindows = _routingCheck.Checked
        };
    }

    internal void PersistSettings()
    {
        try
        {
            _saveSettings(_lastValidSettings with
            {
                MakePrimary = _primaryCheck.Checked,
                RouteNewWindows = _routingCheck.Checked
            });
        }
        catch (Exception exception)
        {
            SetStatusError($"Settings save failed: {exception.Message}");
        }
    }

    private void UpdateStartStopEnabled() =>
        _startStopButton.Enabled = !_busy && (_session is not null || _modeValid);

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

            if (!TryReadMode(out var mode))
            {
                ValidateResolution();
                return;
            }

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
                SetUiState($"Stop failed: {string.Join(" | ", errors)}", false, true, true);
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

        SetStatusError(message);
    }

    internal void SetUiState(string status, bool busy, bool active, bool error = false)
    {
        _statusLabel.Text = status;
        _statusIndicator.ForeColor = error
            ? Color.Firebrick
            : busy
                ? Color.DarkOrange
                : active
                    ? Color.ForestGreen
                    : Color.Firebrick;
        _busy = busy;
        var resolutionEnabled = !busy && !active;
        _aspectCombo.Enabled = resolutionEnabled;
        _aspectLockButton.Enabled = resolutionEnabled;
        _presetCombo.Enabled = resolutionEnabled;
        _widthText.Enabled = resolutionEnabled;
        _heightText.Enabled = resolutionEnabled;
        _refreshCombo.Enabled = resolutionEnabled;
        _primaryCheck.Enabled = resolutionEnabled;
        _routingCheck.Enabled = resolutionEnabled;
        _startStopButton.Text = active ? "Stop" : "Start";
        UpdateStartStopEnabled();
    }

    private void SetStatusError(string status)
    {
        _statusLabel.Text = status;
        _statusIndicator.ForeColor = Color.Firebrick;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        PersistSettings();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _resolutionErrors.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record AspectFilterChoice(string Label, ResolutionAspectRatio? Ratio)
    {
        public override string ToString() => Label;
    }

    private sealed record PresetChoice(string Label, string Key, ResolutionSize? Size)
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
