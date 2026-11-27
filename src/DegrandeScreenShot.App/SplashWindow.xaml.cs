using System.Windows;
using System.Windows.Media.Animation;

namespace DegrandeScreenShot.App;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        if (SystemParameters.ClientAreaAnimation && Resources["FadeInStoryboard"] is Storyboard fadeIn)
        {
            Loaded += (_, _) => fadeIn.Begin(this);
        }
        ContentRendered += OpenLauncher;
    }

    private void OpenLauncher(object? sender, EventArgs e)
    {
        ContentRendered -= OpenLauncher;
        var launcher = new MainWindow();
        Application.Current.MainWindow = launcher;
        launcher.ShowLauncher();
        Close();
    }
}
