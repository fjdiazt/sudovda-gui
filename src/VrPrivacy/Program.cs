namespace VrPrivacy;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            return SelfTest.Run();

        if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
            return SmokeTest.Run();

        if (args.Contains("--smoke-window", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            Application.Run(SmokeTest.CreateTestWindow());
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
