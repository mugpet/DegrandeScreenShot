using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using DegrandeScreenShot.App.Services;

namespace DegrandeScreenShot.App;

/// <summary>
/// Interaction logic for SplashWindow.xaml
/// </summary>
public partial class SplashWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly DateTime _startTime;
    private const int MinimumHoldTimeMs = 1800;

    public SplashWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _startTime = DateTime.UtcNow;
        Loaded += SplashWindow_Loaded;
    }

    private async void SplashWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Play FadeIn animation
        if (Resources["FadeInStoryboard"] is Storyboard fadeIn)
        {
            fadeIn.Begin(this);
        }

        // Run startup background tasks
        await InitializeAppAsync();
    }

    private async Task InitializeAppAsync()
    {
        try
        {
            StatusLabel.Text = "Initializing screen capture engines...";
            await Task.Delay(250); // Small delay to appreciate the step

            StatusLabel.Text = "Checking for software updates...";
            var checkTask = CheckForUpdatesAsync();

            // Run check and enforce minimum splash holding time concurrently
            var elapsedMs = (int)(DateTime.UtcNow - _startTime).TotalMilliseconds;
            var remainingMs = Math.Max(0, MinimumHoldTimeMs - elapsedMs);
            
            await Task.WhenAll(checkTask, Task.Delay(remainingMs));
            
            StatusLabel.Text = "Ready!";
            await Task.Delay(200);
        }
        catch
        {
            // Fail-safe: ensure splash window exits cleanly regardless of internal exceptions
        }
        finally
        {
            await ExitSplashAndShowLauncherAsync();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var release = await UpdateService.GetLatestReleaseAsync();
            if (release == null) return;

            var current = UpdateService.GetCurrentVersion();
            if (UpdateService.IsUpdateAvailable(release.TagName, current, out _))
            {
                var asset = UpdateService.FindInstallerAsset(release);
                if (asset != null)
                {
                    // Cache the update on the main window thread-safely
                    Dispatcher.Invoke(() => _mainWindow.SetCachedUpdate(release, asset));
                }
            }
        }
        catch
        {
            // Fail silently so startup is never blocked by API issues
        }
    }

    private async Task ExitSplashAndShowLauncherAsync()
    {
        // Play smooth fade out
        if (Resources["FadeOutStoryboard"] is Storyboard fadeOut)
        {
            fadeOut.Begin(this);
            // Wait for fade out animation to complete (duration is 0.3s)
            await Task.Delay(310);
        }

        // Show the main window and close splash
        _mainWindow.ShowLauncher();
        Close();
    }
}
