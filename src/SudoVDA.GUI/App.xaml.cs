using System.Windows;

namespace SudoVDA.GUI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        if (eventArgs.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(SelfTest.Run());
            return;
        }

        if (eventArgs.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(SmokeTest.Run());
            return;
        }

        if (eventArgs.Args.Contains("--smoke-window", StringComparer.OrdinalIgnoreCase))
        {
            ShowWindow(SmokeTest.CreateTestWindow());
            return;
        }

        ShowWindow(new MainWindow());
    }

    private void ShowWindow(Window window)
    {
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }
}
