using System.IO;

namespace DegrandeScreenShot.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
	private static readonly string LogFilePath = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"DegrandeScreenShot",
		"app.log");

	protected override void OnStartup(System.Windows.StartupEventArgs e)
	{
		base.OnStartup(e);
		DispatcherUnhandledException += App_DispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
		TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
		WriteLog("startup");

		var mainWindow = new MainWindow();
		MainWindow = mainWindow;
		mainWindow.InitializeHiddenTrayMode();
	}

	protected override void OnExit(System.Windows.ExitEventArgs e)
	{
		WriteLog($"exit code={e.ApplicationExitCode}");
		DispatcherUnhandledException -= App_DispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
		TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
		base.OnExit(e);
	}

	private static void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
	{
		WriteLog("dispatcher unhandled", e.Exception);
		e.Handled = true;
	}

	private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		WriteLog($"domain unhandled terminating={e.IsTerminating}", e.ExceptionObject as Exception);
	}

	private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		WriteLog("task unobserved", e.Exception);
	}

	private static void WriteLog(string message, Exception? exception = null)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
			File.AppendAllText(
				LogFilePath,
				$"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}{exception}{Environment.NewLine}");
		}
		catch
		{
			// Logging must never cause or mask app startup failures.
		}
	}
}

