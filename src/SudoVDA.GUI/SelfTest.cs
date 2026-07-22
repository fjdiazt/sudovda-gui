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
        CheckAspectLockForm();


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
        var modeChoices = DisplayController.GetModeChoices();
        Check(modeChoices.Count > 0, "display mode discovery");
        Check(new[] { 1080u, 1440u, 2160u }.All(width =>
            modeChoices.Any(mode => mode.Width == width && mode.Height == width)), "square fallback modes");

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
            [
                primary,
                new DisplayMode(1920, 1080, 60),
                new DisplayMode(1920, 1200, 60),
                new DisplayMode(1024, 768, 60),
                new DisplayMode(1280, 1024, 60),
                new DisplayMode(1000, 1000, 60)
            ],
            value => saved = value);

        var preset = form.Controls.Find("presetCombo", true).OfType<ComboBox>().Single();
        var aspect = form.Controls.Find("aspectCombo", true).OfType<ComboBox>().Single();
        var width = form.Controls.Find("widthText", true).OfType<TextBox>().Single();
        var height = form.Controls.Find("heightText", true).OfType<TextBox>().Single();
        var refresh = form.Controls.Find("refreshCombo", true).OfType<ComboBox>().Single();
        var primaryCheck = form.Controls.Find("primaryCheck", true).OfType<CheckBox>().Single();
        var routingCheck = form.Controls.Find("routingCheck", true).OfType<CheckBox>().Single();
        var start = form.Controls.Find("startStopButton", true).OfType<Button>().Single();
        var displayGroup = form.Controls.Find("displayGroup", true).OfType<GroupBox>().SingleOrDefault();
        var behaviorGroup = form.Controls.Find("behaviorGroup", true).OfType<GroupBox>().SingleOrDefault();
        var resolutionLayout = form.Controls.Find("resolutionLayout", true).OfType<TableLayoutPanel>().SingleOrDefault();
        var widthLabel = form.Controls.Find("widthLabel", true).OfType<Label>().SingleOrDefault();
        var heightLabel = form.Controls.Find("heightLabel", true).OfType<Label>().SingleOrDefault();
        var refreshLabel = form.Controls.Find("refreshLabel", true).OfType<Label>().SingleOrDefault();
        var statusIndicator = form.Controls.Find("statusIndicator", true).OfType<Label>().SingleOrDefault();

        Check(form.Text == "SudoVDA", "main window title");
        Check(aspect.SelectedItem?.ToString() == "All aspect ratios", "all-aspects default");
        Check(preset.SelectedItem?.ToString() == "Match primary display", "match-primary default");
        Check(width.Text == "3440" && height.Text == "1440", "copy-primary dimensions");
        Check((uint)refresh.SelectedItem! == 119, "copy-primary refresh");
        Check(aspect.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(
        [
            "All aspect ratios",
            "1:1 (Square)",
            "4:3 (Standard)",
            "5:4 (Standard)",
            "16:9 (Wide)",
            "16:10 (Wide)",
            "21:9 (Ultrawide)"
        ]), "aspect dropdown contents");
        Check(preset.Items.Cast<object>().Any(item => item.ToString() ==
            "1920 x 1080 (16:9 Wide)"), "all-aspects preset annotation");

        aspect.SelectedItem = aspect.Items.Cast<object>()
            .Single(item => item.ToString() == "16:9 (Wide)");
        Check(preset.Items.Cast<object>().Select(item => item.ToString()).SequenceEqual(
        [
            "Match primary display",
            "1920 x 1080",
            "Custom"
        ]), "filtered preset contents");
        Check(preset.SelectedItem?.ToString() == "1920 x 1080",
            "filter selects first matching preset");
        aspect.SelectedIndex = 0;
        Check(preset.SelectedItem?.ToString() == "1920 x 1080 (16:9 Wide)",
            "all-aspects preserves selected preset");
        Check(primaryCheck.Checked, "make-primary default");
        Check(routingCheck.Checked, "routing default");
        Check(start.Text == "Start", "start button default");
        Check(displayGroup?.Text == "Display", "display group");
        Check(behaviorGroup?.Text == "Behavior", "behavior group");
        Check(resolutionLayout is not null, "resolution row layout");
        if (resolutionLayout is not null)
        {
            Check(resolutionLayout.GetPositionFromControl(width).Column == 0 &&
                  resolutionLayout.GetPositionFromControl(width).Row == 5 &&
                  resolutionLayout.GetPositionFromControl(height).Column == 1 &&
                  resolutionLayout.GetPositionFromControl(height).Row == 5 &&
                  resolutionLayout.GetPositionFromControl(refresh).Column == 3 &&
                  resolutionLayout.GetPositionFromControl(refresh).Row == 5,
                "dimension controls share one row");
            Check(widthLabel is not null && heightLabel is not null && refreshLabel is not null &&
                  resolutionLayout.GetPositionFromControl(widthLabel).Row == 4 &&
                  resolutionLayout.GetPositionFromControl(heightLabel).Row == 4 &&
                  resolutionLayout.GetPositionFromControl(refreshLabel).Row == 4 &&
                  resolutionLayout.GetPositionFromControl(refreshLabel).Column == 3,
                "dimension labels share row above controls");
        }
        Check(statusIndicator?.ForeColor == Color.Firebrick, "stopped status color");
        form.SetUiState("Starting...", true, false);
        Check(statusIndicator?.ForeColor == Color.DarkOrange, "busy status color");
        form.SetUiState("Active", false, true);
        Check(statusIndicator?.ForeColor == Color.ForestGreen, "active status color");
        form.SetUiState("Stop failed", false, true, true);
        Check(statusIndicator?.ForeColor == Color.Firebrick, "error status color");
        form.SetUiState("Stopped", false, false);

        preset.SelectedItem = preset.Items.Cast<object>()
            .Single(item => item.ToString() == "1920 x 1080 (16:9 Wide)");
        Check(width.Text == "1920" && height.Text == "1080", "preset populates dimensions");
        width.Text = "2000";
        Check(preset.SelectedItem?.ToString() == "Custom", "manual edit selects custom");
        width.Text = "invalid";
        Check(!start.Enabled, "invalid width disables start");
        width.Text = "2000";
        form.SetUiState("Active", false, true);
        Check(!aspect.Enabled && !preset.Enabled && !width.Enabled && !height.Enabled && !refresh.Enabled,
            "active display locks resolution controls");
        form.SetUiState("Stopped", false, false);
        Check(aspect.Enabled && preset.Enabled && width.Enabled && height.Enabled && refresh.Enabled,
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

    private static void CheckAspectLockForm()
    {
        var primary = new DisplayMode(1920, 1080, 60);
        using var form = new MainForm(
            primary,
            UserSettings.Defaults(primary),
            [primary, new DisplayMode(1920, 1200, 60)],
            _ => { });

        var preset = form.Controls.Find("presetCombo", true).OfType<ComboBox>().Single();
        var width = form.Controls.Find("widthText", true).OfType<TextBox>().Single();
        var height = form.Controls.Find("heightText", true).OfType<TextBox>().Single();
        var aspectLock = form.Controls.Find("aspectLockButton", true).OfType<CheckBox>().Single();
        var layout = form.Controls.Find("resolutionLayout", true).OfType<TableLayoutPanel>().Single();

        Check(!aspectLock.Checked && aspectLock.Text == "🔓" &&
              aspectLock.AccessibleName == "Lock aspect ratio",
            "aspect lock default");
        Check(layout.ColumnCount == 4 &&
              layout.GetPositionFromControl(width).Column == 0 &&
              layout.GetPositionFromControl(height).Column == 1 &&
              layout.GetPositionFromControl(aspectLock).Column == 2,
            "aspect lock layout");

        aspectLock.Checked = true;
        Check(aspectLock.Text == "🔒" &&
              aspectLock.AccessibleName == "Unlock aspect ratio",
            "aspect lock enabled");

        width.Text = "2000";
        Check(height.Text == "1125" && preset.SelectedItem?.ToString() == "Custom",
            "locked width updates height");
        height.Text = "1200";
        Check(width.Text == "2133", "locked height updates width");

        preset.SelectedItem = preset.Items.Cast<object>()
            .Single(item => item.ToString() == "1920 x 1200 (16:10 Wide)");
        width.Text = "2000";
        Check(height.Text == "1250", "preset refreshes locked ratio");

        aspectLock.Checked = false;
        width.Text = "invalid";
        aspectLock.Checked = true;
        Check(!aspectLock.Checked && aspectLock.Text == "🔓",
            "invalid dimensions refuse aspect lock");

        form.SetUiState("Active", false, true);
        Check(!aspectLock.Enabled, "active display locks aspect button");
        form.SetUiState("Stopped", false, false);
        Check(aspectLock.Enabled, "stopped display unlocks aspect button");
    }

    private static void CheckResolutionSettings()
    {
        var primary = new DisplayMode(3440, 1440, 119);
        var rates = ResolutionOptions.RefreshRates(primary.RefreshHz);
        Check(rates.Contains(24u) && rates.Contains(500u), "standard refresh rates");
        Check(rates.Contains(119u), "primary refresh inclusion");

        var square = ResolutionOptions.AspectRatio(new ResolutionSize(1000, 1000));
        Check(square == new ResolutionAspectRatio(1, 1, "Square"), "square aspect classification");
        var ultrawide = ResolutionOptions.AspectRatio(new ResolutionSize(3440, 1440));
        Check(ultrawide == new ResolutionAspectRatio(21, 9, "Ultrawide"),
            "ultrawide aspect classification");
        var unknown = ResolutionOptions.AspectRatio(new ResolutionSize(1000, 700));
        Check(unknown == new ResolutionAspectRatio(10, 7, null), "unknown exact aspect classification");

        var aspectSortedSizes = ResolutionOptions.DistinctSizes(
        [
            new DisplayMode(1920, 1080, 60),
            new DisplayMode(1920, 1200, 60),
            new DisplayMode(1280, 768, 60),
            new DisplayMode(1440, 960, 60),
            new DisplayMode(1024, 768, 60),
            new DisplayMode(1280, 1024, 60),
            new DisplayMode(1000, 1000, 60)
        ]);
        Check(aspectSortedSizes.SequenceEqual(
        [
            new ResolutionSize(1000, 1000),
            new ResolutionSize(1440, 960),
            new ResolutionSize(1024, 768),
            new ResolutionSize(1280, 768),
            new ResolutionSize(1280, 1024),
            new ResolutionSize(1920, 1080),
            new ResolutionSize(1920, 1200)
        ]), "resolution aspect-first ordering");
        var aspects = ResolutionOptions.AspectRatios(aspectSortedSizes);
        Check(aspects.Select(value => value.FilterLabel).SequenceEqual(
        [
            "1:1 (Square)",
            "3:2 (Classic)",
            "4:3 (Standard)",
            "5:3 (Wide)",
            "5:4 (Standard)",
            "16:9 (Wide)",
            "16:10 (Wide)"
        ]), "aspect filter ordering and labels");

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
