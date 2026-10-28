using Microsoft.UI.Xaml;

namespace ExoCinema;

public partial class App : Application
{
    public static MainWindow? MainAppWindow { get; private set; }

    /// <summary>%LOCALAPPDATA%\ExoCinema\crash.log: what ended Exo Cinema, when something did.</summary>
    private static readonly string CrashLog = Path.Combine(AppInfo.Data, "crash.log");

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => WriteCrash("xaml", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrash("domain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => WriteCrash("task", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainAppWindow = new MainWindow(LaunchArgs.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray()));
        MainAppWindow.Show();
    }

    internal static void WriteCrash(string where, Exception? e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);
            File.AppendAllText(CrashLog, AppLog.Clean($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {where}: {e}\n\n"));
        }
        catch (IOException) { }
    }
}
