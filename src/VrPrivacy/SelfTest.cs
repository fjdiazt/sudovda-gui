using System.Runtime.InteropServices;
namespace VrPrivacy;

internal static class SelfTest
{
    private static int _failures;

    internal static int Run()
    {
        Check(new DisplayMode(1920, 1080, 60).ToString() == "1920 x 1080 @ 60 Hz", "display mode formatting");

        using var form = new MainForm();
        Check(form.Text == "VR Privacy", "main window title");

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
        Check(snapshot.Displays.Count(display => display.Primary) == 1, "single primary discovery");
        Check(DisplayController.GetModeChoices().Count > 0, "display mode discovery");

        Check(WindowRouter.IsEligible(new(true, false, false, false, 42), 7, 99), "normal window");
        Check(!WindowRouter.IsEligible(new(true, false, false, false, 7), 7, 99), "own process");
        Check(!WindowRouter.IsEligible(new(true, true, false, false, 42), 7, 99), "cloaked window");
        Check(!WindowRouter.IsEligible(new(true, false, true, false, 42), 7, 99), "tool window");
        Check(!WindowRouter.IsEligible(new(true, false, false, true, 42), 7, 99), "non-activating window");
        Check(!WindowRouter.IsEligible(new(true, false, false, false, 99), 7, 99), "shell process");
        Check(!WindowRouter.IsEligible(new(false, false, false, false, 42), 7, 99), "hidden window");

        if (_failures == 0)
        {
            Console.WriteLine("Self-test passed.");
            return 0;
        }

        Console.Error.WriteLine($"Self-test failed: {_failures} check(s).");
        return 1;
    }

    internal static void Check(bool condition, string name)
    {
        if (condition)
            return;

        _failures++;
        Console.Error.WriteLine($"FAIL: {name}");
    }
}
