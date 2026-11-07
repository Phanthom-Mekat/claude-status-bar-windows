using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace ClaudeStatusBar;

public class App : Application
{
    TrayController? _ctrl;

    public override void Initialize()
    {
        Name = "Claude Status Bar";
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try { _ctrl = new TrayController(desktop); }
            catch (Exception e)
            {
                // Avalonia would swallow this and leave an invisible process holding the instance lock: quit instead.
                Log.Init(true);
                Log.Write("startup failed: " + e);
                Console.Error.WriteLine("Claude Status Bar failed to start: " + e);
                desktop.Shutdown(1);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
