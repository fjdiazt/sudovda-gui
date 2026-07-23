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

        var window = new MainWindow();
        ShowWindow(
            window,
            eventArgs.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase) &&
            window.MinimizeToNotificationAreaEnabled);
    }

    private void ShowWindow(Window window, bool hidden = false)
    {
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        if (hidden && window is MainWindow mainWindow)
            mainWindow.HideToNotificationArea();
        else
            window.Show();
    }
}
