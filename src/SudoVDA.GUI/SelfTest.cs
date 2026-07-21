using Microsoft.Win32;
using System.Reflection;
using System.Runtime.InteropServices;
namespace SudoVDA.GUI;

internal static class SelfTest
{
    private static int _failures;

    internal static int Run()
    {
        Check(Assembly.GetExecutingAssembly().GetName().Name == "SudoVDA-GUI", "assembly name");
        Check(new DisplayMode(1920, 1080, 60).ToString() == "1920 x 1080 @ 60 Hz", "display mode formatting");
        CheckResolutionSettings();
        CheckResolutionForm();


        Check(SudoVdaClient.IoctlAdd == 0x00222000, "ADD IOCTL");
        Check(SudoVdaClient.IoctlRemove == 0x00222004, "REMOVE IOCTL");
        Check(SudoVdaClient.IoctlGetWatchdog == 0x0022200C, "watchdog IOCTL");
        Check(SudoVdaClient.IoctlPing == 0x00222220, "ping IOCTL");
        Check(SudoVdaClient.IoctlGetProtocol == 0x002223FC, "protocol IOCTL");
        Check(Marshal.SizeOf<SudoVdaClient.AddParams>() == 56, "ADD layout");
        Check(Marshal.SizeOf<SudoVdaClient.AddOut>() == 12, "ADD output layout");
        Check(Marshal.SizeOf<SudoVdaClient.ProtocolVersion>() == 4, "protocol layout");

        var modes = DisplayController.DistinctModes(
        [
            new DisplayMode(1920, 1080, 60),
            new DisplayMode(1920, 1080, 60),
            new DisplayMode(2560, 1440, 120)
        ]);
        Check(modes.Count == 2, "mode deduplication");
        Check(modes[0].Width == 1920 && modes[1].Width == 2560, "mode ordering");
        Check(DisplayController.IsSupported(new DisplayMode(640, 480, 60)), "minimum mode");
        Check(!DisplayController.IsSupported(new DisplayMode(639, 480, 60)), "below-minimum mode");

        var snapshot = DisplayController.Capture();
        Check(snapshot.Displays.Count > 0, "active display discovery");

        var placedDisplays = new DisplaySnapshot(
        [
            new DisplayState("physical", new Point(0, 0), new DisplayMode(1920, 1080, 60), true),
            new DisplayState("virtual", new Point(1920, 0), new DisplayMode(1920, 1080, 60), false)
        ]);
        Check(DisplayController.ChoosePosition(placedDisplays, "virtual") == new Point(1920, 0),
            "retain non-overlapping virtual position");

        var overlappingDisplays = new DisplaySnapshot(
        [
            new DisplayState("physical", new Point(0, 0), new DisplayMode(1920, 1080, 60), true),
            new DisplayState("virtual", new Point(0, 0), new DisplayMode(1920, 1080, 60), false)
        ]);
        Check(DisplayController.ChoosePosition(overlappingDisplays, "virtual") == new Point(1920, 0),
            "move overlapping virtual display right");
        Check(snapshot.Displays.Count(display => display.Primary) == 1, "single primary discovery");
        Check(DisplayController.GetModeChoices().Count > 0, "display mode discovery");

        Check(WindowRouter.IsEligible(new(true, false, false, false, 42), 7, 99), "normal window");
        Check(!WindowRouter.IsEligible(new(true, false, false, false, 7), 7, 99), "own process");
        Check(!WindowRouter.IsEligible(new(true, true, false, false, 42), 7, 99), "cloaked window");
        Check(!WindowRouter.IsEligible(new(true, false, true, false, 42), 7, 99), "tool window");
        Check(!WindowRouter.IsEligible(new(true, false, false, true, 42), 7, 99), "non-activating window");
        Check(!WindowRouter.IsEligible(new(true, false, false, false, 99), 7, 99), "shell process");
        Check(!WindowRouter.IsEligible(new(false, false, false, false, 42), 7, 99), "hidden window");

        using var driver = SudoVdaClient.Open();
        var protocol = driver.GetProtocolVersion();
        var watchdog = driver.GetWatchdog();
        Check(protocol.Major == 0 && protocol.Minor >= 2, "installed driver protocol");
        Check(watchdog.Countdown <= watchdog.Timeout, "installed driver watchdog");
        Console.WriteLine($"SudoVDA protocol {protocol.Major}.{protocol.Minor}.{protocol.Incremental}; watchdog {watchdog.Timeout}s.");

        if (_failures == 0)
        {
            Console.WriteLine("Self-test passed.");
            return 0;
        }

        Console.Error.WriteLine($"Self-test failed: {_failures} check(s).");
        return 1;
    }

    private static void CheckResolutionForm()
    {
        var primary = new DisplayMode(3440, 1440, 119);
        UserSettings? saved = null;
        using var form = new MainForm(
            primary,
            UserSettings.Defaults(primary),
            [primary, new DisplayMode(1920, 1080, 60)],
            value => saved = value);

        var preset = form.Controls.Find("presetCombo", true).OfType<ComboBox>().Single();
        var width = form.Controls.Find("widthText", true).OfType<TextBox>().Single();
        var height = form.Controls.Find("heightText", true).OfType<TextBox>().Single();
        var refresh = form.Controls.Find("refreshCombo", true).OfType<ComboBox>().Single();
        var primaryCheck = form.Controls.Find("primaryCheck", true).OfType<CheckBox>().Single();
        var routingCheck = form.Controls.Find("routingCheck", true).OfType<CheckBox>().Single();
        var start = form.Controls.Find("startStopButton", true).OfType<Button>().Single();

        Check(form.Text == "SudoVDA", "main window title");
        Check(preset.SelectedItem?.ToString() == "Copy primary", "copy-primary default");
        Check(width.Text == "3440" && height.Text == "1440", "copy-primary dimensions");
        Check((uint)refresh.SelectedItem! == 119, "copy-primary refresh");
        Check(primaryCheck.Checked, "make-primary default");
        Check(routingCheck.Checked, "routing default");
        Check(start.Text == "Start", "start button default");

        preset.SelectedItem = preset.Items.Cast<object>()
            .Single(item => item.ToString() == "1920 x 1080");
        Check(width.Text == "1920" && height.Text == "1080", "preset populates dimensions");
        width.Text = "2000";
        Check(preset.SelectedItem?.ToString() == "Custom", "manual edit selects custom");
        width.Text = "invalid";
        Check(!start.Enabled, "invalid width disables start");
        width.Text = "2000";
        form.SetUiState("Active", false, true);
        Check(!preset.Enabled && !width.Enabled && !height.Enabled && !refresh.Enabled,
            "active display locks resolution controls");
        form.SetUiState("Stopped", false, false);
        Check(preset.Enabled && width.Enabled && height.Enabled && refresh.Enabled,
            "stopped display unlocks resolution controls");

        Check(start.Enabled, "valid width enables start");

        primaryCheck.Checked = false;
        routingCheck.Checked = false;
        form.PersistSettings();
        Check(saved == new UserSettings("Custom", 2000, 1080, 119, false, false),
            "form settings persistence");
        width.Text = "2100";
        form.PersistSettings();
        Check(saved?.Width == 2100, "settings resave after later edit");
    }

    private static void CheckResolutionSettings()
    {
        var primary = new DisplayMode(3440, 1440, 119);
        var rates = ResolutionOptions.RefreshRates(primary.RefreshHz);
        Check(rates.Contains(24u) && rates.Contains(500u), "standard refresh rates");
        Check(rates.Contains(119u), "primary refresh inclusion");

        var sizes = ResolutionOptions.DistinctSizes(
        [
            new DisplayMode(1920, 1080, 60),
            new DisplayMode(1920, 1080, 120),
            new DisplayMode(2560, 1440, 120)
        ]);
        Check(sizes.Count == 2, "resolution size deduplication");
        Check(ResolutionOptions.TryParseMode("640", "480", 60, out var minimum, out _, out _) &&
              minimum == new DisplayMode(640, 480, 60), "minimum custom mode");
        Check(!ResolutionOptions.TryParseMode("639", "480", 60, out _, out var widthError, out _) &&
              widthError == "Width must be 640–7680.", "width lower bound");
        Check(!ResolutionOptions.TryParseMode("1920", "nope", 60, out _, out _, out var heightError) &&
              heightError == "Height must be 480–4320.", "nonnumeric height");

        var path = $@"Software\VRPrivacy\Tests\{Guid.NewGuid():N}";
        try
        {
            var expected = new UserSettings("Custom", 2000, 1000, 144, false, true);
            UserSettingsStore.Save(expected, path);
            Check(UserSettingsStore.Load(primary, path) == expected, "settings registry round-trip");
            UserSettingsStore.Save(expected with { Preset = "1920x1080" }, path);
            var mismatchedPreset = UserSettingsStore.Load(primary, path);
            Check(mismatchedPreset.Preset == UserSettings.CopyPrimary,
                "mismatched preset fallback");

            using (var key = Registry.CurrentUser.CreateSubKey(path))
                key.SetValue("Width", 639, RegistryValueKind.DWord);
            var fallback = UserSettingsStore.Load(primary, path);
            Check(fallback.Preset == UserSettings.CopyPrimary && fallback.Width == primary.Width,
                "invalid settings fallback");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
    }

    internal static void Check(bool condition, string name)
    {
        if (condition)
            return;

        _failures++;
        Console.Error.WriteLine($"FAIL: {name}");
    }
}
