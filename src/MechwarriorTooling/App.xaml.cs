using System.Windows;

namespace MechwarriorTooling;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(); MainWindow = window;
        int argument = Array.IndexOf(e.Args, "--game-dir");
        int verification = Array.IndexOf(e.Args, "--verify-ui");
        if (verification >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                if (argument < 0 || argument + 1 >= e.Args.Length || verification + 1 >= e.Args.Length)
                    throw new ArgumentException("--verify-ui OUTPUT requires --game-dir PATH.");
                await window.VerifyUiAsync(e.Args[argument + 1], e.Args[verification + 1]); Shutdown(0);
            }
            catch (Exception error)
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MechwarriorTooling-ui-error.txt"), error.ToString()); Shutdown(1);
            }
            return;
        }
        window.Show();
        if (argument >= 0 && argument + 1 < e.Args.Length) window.OpenInstallation(e.Args[argument + 1]);
    }
}
