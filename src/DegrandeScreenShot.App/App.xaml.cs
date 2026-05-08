namespace DegrandeScreenShot.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
	protected override void OnStartup(System.Windows.StartupEventArgs e)
	{
		base.OnStartup(e);

		var mainWindow = new MainWindow(startHiddenInTray: true);
		MainWindow = mainWindow;
		mainWindow.Show();
	}
}

