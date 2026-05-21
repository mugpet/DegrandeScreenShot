using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DegrandeScreenShot.App.Services;
using Microsoft.Win32;

namespace DegrandeScreenShot.App;

/// <summary>
/// Interaction logic for UpdateWindow.xaml
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly GitHubRelease _release;
    private readonly GitHubAsset _asset;
    private CancellationTokenSource? _cts;

    public UpdateWindow(GitHubRelease release, GitHubAsset asset)
    {
        InitializeComponent();
        _release = release;
        _asset = asset;

        ApplyTheme();

        Loaded += UpdateWindow_Loaded;
    }

    private void ApplyTheme()
    {
        var isLight = IsSystemLightTheme();
        var panelColor = isLight ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x20, 0x26, 0x30);
        var panelBorderColor = isLight ? Color.FromRgb(0xD5, 0xE0, 0xEA) : Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF);
        var accentColor = isLight ? Color.FromRgb(0x0C, 0x8E, 0xC5) : Color.FromRgb(0x9F, 0xDD, 0xF4);
        var accentStrongColor = isLight ? Color.FromRgb(0x0A, 0x6F, 0x9A) : Color.FromRgb(0xC8, 0xB9, 0xFF);
        var textColor = isLight ? Color.FromRgb(0x1D, 0x26, 0x31) : Color.FromRgb(0xF4, 0xF7, 0xFB);
        var mutedTextColor = isLight ? Color.FromRgb(0x6C, 0x79, 0x87) : Color.FromRgb(0xA9, 0xB7, 0xC6);
        var appBgColor = isLight ? Color.FromRgb(0xF4, 0xF7, 0xFB) : Color.FromRgb(0x1B, 0x20, 0x28);

        Resources["PanelBrush"] = new SolidColorBrush(panelColor);
        Resources["PanelBorderBrush"] = new SolidColorBrush(panelBorderColor);
        Resources["AccentBrush"] = new SolidColorBrush(accentColor);
        Resources["AccentStrongBrush"] = new SolidColorBrush(accentStrongColor);
        Resources["TextBrush"] = new SolidColorBrush(textColor);
        Resources["MutedTextBrush"] = new SolidColorBrush(mutedTextColor);
        Resources["AppBackgroundBrush"] = new SolidColorBrush(appBgColor);
    }

    private static bool IsSystemLightTheme()
    {
        using var personalizeKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var themeValue = personalizeKey?.GetValue("AppsUseLightTheme");
        return themeValue is not int intValue || intValue != 0;
    }

    private void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Display version comparison text
        var currentVersion = UpdateService.GetCurrentVersion().ToString(3); // e.g. 0.2.16
        var cleanTagName = _release.TagName.Trim();
        if (cleanTagName.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            cleanTagName = cleanTagName.Substring(1);
        }
        
        VersionInfoTextBlock.Text = $"Version {cleanTagName} is now available (you have {currentVersion}).";
        
        // Display release description
        var releaseBody = _release.Body;
        if (string.IsNullOrWhiteSpace(releaseBody))
        {
            releaseBody = "No release details provided.";
        }
        else
        {
            // Normalize line endings
            releaseBody = releaseBody.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }

        ReleaseNotesTextBox.Text = releaseBody;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null)
        {
            try
            {
                _cts.Cancel();
            }
            catch
            {
                // Ignore cancellation failures
            }
        }

        Close();
    }

    private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        // Disable controls
        LaterButton.IsEnabled = false;
        UpdateNowButton.IsEnabled = false;
        
        // Show progress area
        ProgressArea.Visibility = Visibility.Visible;
        DownloadProgressBar.Value = 0;
        PercentageTextBlock.Text = "0%";
        StatusTextBlock.Text = "Starting download...";

        _cts = new CancellationTokenSource();

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "DegrandeScreenShotUpdates");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            var tempFilePath = Path.Combine(tempDir, _asset.Name);

            var progress = new Progress<double>(p =>
            {
                // Run on UI thread
                Dispatcher.Invoke(() =>
                {
                    var pct = (int)Math.Round(p * 100);
                    DownloadProgressBar.Value = pct;
                    PercentageTextBlock.Text = $"{pct}%";
                    StatusTextBlock.Text = $"Downloading update ({pct}%)...";
                });
            });

            await UpdateService.DownloadFileWithProgressAsync(
                _asset.BrowserDownloadUrl, 
                tempFilePath, 
                progress, 
                _cts.Token);

            StatusTextBlock.Text = "Download complete! Launching installer...";
            await Task.Delay(800); // Brief visual confirmation for user

            UpdateService.ExecuteInstallerAndExit(tempFilePath);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Download cancelled.";
            ProgressArea.Visibility = Visibility.Collapsed;
            LaterButton.IsEnabled = true;
            UpdateNowButton.IsEnabled = true;
            _cts = null;
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = "Error during download.";
            MessageBox.Show(
                this, 
                $"An error occurred while downloading the update:\n{exception.Message}", 
                "Update Error", 
                MessageBoxButton.OK, 
                MessageBoxImage.Error);

            ProgressArea.Visibility = Visibility.Collapsed;
            LaterButton.IsEnabled = true;
            UpdateNowButton.IsEnabled = true;
            _cts = null;
        }
    }
}
