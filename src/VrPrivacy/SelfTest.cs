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
