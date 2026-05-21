using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

        // Launcher specific hero header brushes
        var heroColor = isLight ? Color.FromRgb(0x1D, 0x26, 0x31) : Color.FromRgb(0x11, 0x18, 0x21);
        var heroMutedColor = isLight ? Color.FromRgb(0xAF, 0xC0, 0xCE) : Color.FromRgb(0x9F, 0xB2, 0xC3);
        var heroTextColor = isLight ? Color.FromRgb(0xF4, 0xF7, 0xFB) : Color.FromRgb(0xF4, 0xF7, 0xFB);
        var closeBtnColor = isLight ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);
        var closeBtnBorderColor = isLight ? Color.FromArgb(0x2F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x32, 0xFF, 0xFF, 0xFF);

        Resources["PanelBrush"] = new SolidColorBrush(panelColor);
        Resources["PanelBorderBrush"] = new SolidColorBrush(panelBorderColor);
        Resources["AccentBrush"] = new SolidColorBrush(accentColor);
        Resources["AccentStrongBrush"] = new SolidColorBrush(accentStrongColor);
        Resources["TextBrush"] = new SolidColorBrush(textColor);
        Resources["MutedTextBrush"] = new SolidColorBrush(mutedTextColor);
        Resources["AppBackgroundBrush"] = new SolidColorBrush(appBgColor);

        Resources["HeroBrush"] = new SolidColorBrush(heroColor);
        Resources["HeroMutedBrush"] = new SolidColorBrush(heroMutedColor);
        Resources["HeroTextBrush"] = new SolidColorBrush(heroTextColor);
        Resources["CloseButtonBrush"] = new SolidColorBrush(closeBtnColor);
        Resources["CloseButtonBorderBrush"] = new SolidColorBrush(closeBtnBorderColor);
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

        // Format and load Markdown changelog
        ReleaseNotesViewer.Document = FormatMarkdownToFlowDocument(releaseBody);
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

    #region Programmatic Markdown Formatter

    private FlowDocument FormatMarkdownToFlowDocument(string markdown)
    {
        var doc = new FlowDocument
        {
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Roboto"),
            FontSize = 13.0,
            PagePadding = new Thickness(0)
        };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "TextBrush");

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        List? activeList = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                activeList = null;
                continue;
            }

            // Headings
            if (trimmed.StartsWith("#"))
            {
                activeList = null;
                var level = trimmed.TakeWhile(c => c == '#').Count();
                var headingText = trimmed.Substring(level).Trim();
                
                var p = new Paragraph
                {
                    Margin = new Thickness(0, 10, 0, 4),
                    FontWeight = FontWeights.Bold
                };
                
                if (level == 1) p.FontSize = 17;
                else if (level == 2) p.FontSize = 15;
                else p.FontSize = 13.5;

                ParseInlineMarkdown(headingText, p.Inlines);
                doc.Blocks.Add(p);
                continue;
            }

            // Bullet lists
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                var itemText = trimmed.Substring(2).Trim();
                if (activeList == null)
                {
                    activeList = new List
                    {
                        MarkerStyle = TextMarkerStyle.Disc,
                        Margin = new Thickness(14, 4, 0, 4)
                    };
                    doc.Blocks.Add(activeList);
                }

                var listItem = new ListItem();
                var itemPara = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                ParseInlineMarkdown(itemText, itemPara.Inlines);
                listItem.Blocks.Add(itemPara);
                activeList.ListItems.Add(listItem);
                continue;
            }

            // Normal paragraphs
            activeList = null;
            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
            ParseInlineMarkdown(trimmed, para.Inlines);
            doc.Blocks.Add(para);
        }

        return doc;
    }

    private void ParseInlineMarkdown(string text, InlineCollection inlines)
    {
        int i = 0;
        while (i < text.Length)
        {
            // Bold: **
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                int end = text.IndexOf("**", i + 2);
                if (end != -1)
                {
                    var boldRun = new Run(text.Substring(i + 2, end - (i + 2))) { FontWeight = FontWeights.Bold };
                    inlines.Add(boldRun);
                    i = end + 2;
                    continue;
                }
            }

            // Inline Monospace Code: `
            if (text[i] == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end != -1)
                {
                    var codeRun = new Run(text.Substring(i + 1, end - (i + 1)))
                    {
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11.5
                    };
                    codeRun.SetResourceReference(Run.ForegroundProperty, "AccentStrongBrush");

                    var codeBorder = new Border
                    {
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(3, 1, 3, 1),
                        Child = new TextBlock(codeRun) { VerticalAlignment = VerticalAlignment.Center }
                    };
                    codeBorder.SetResourceReference(Border.BackgroundProperty, "AppBackgroundBrush");
                    codeBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");

                    inlines.Add(new InlineUIContainer(codeBorder));
                    i = end + 1;
                    continue;
                }
            }

            // Markdown Link: [title](url)
            if (text[i] == '[')
            {
                int endTitle = text.IndexOf(']', i + 1);
                if (endTitle != -1 && endTitle + 1 < text.Length && text[endTitle + 1] == '(')
                {
                    int endUrl = text.IndexOf(')', endTitle + 2);
                    if (endUrl != -1)
                      {
                        var title = text.Substring(i + 1, endTitle - (i + 1));
                        var url = text.Substring(endTitle + 2, endUrl - (endTitle + 2));
                        
                        var link = CreateHyperlink(title, url);
                        inlines.Add(link);
                        i = endUrl + 1;
                        continue;
                    }
                }
            }

            // Raw URL: http:// or https://
            if (i + 7 < text.Length && (text.Substring(i, 7) == "http://" || (i + 8 < text.Length && text.Substring(i, 8) == "https://")))
            {
                int end = i;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != ')' && text[end] != ']' && text[end] != '*' && text[end] != '`')
                {
                    end++;
                }
                var url = text.Substring(i, end - i);
                var link = CreateHyperlink(url, url);
                inlines.Add(link);
                i = end;
                continue;
            }

            // Normal text chunk
            int nextSpecial = text.Length;
            int boldIdx = text.IndexOf("**", i);
            int codeIdx = text.IndexOf('`', i);
            int linkIdx = text.IndexOf('[', i);
            int httpIdx = text.IndexOf("http://", i);
            int httpsIdx = text.IndexOf("https://", i);

            if (boldIdx != -1 && boldIdx < nextSpecial) nextSpecial = boldIdx;
            if (codeIdx != -1 && codeIdx < nextSpecial) nextSpecial = codeIdx;
            if (linkIdx != -1 && linkIdx < nextSpecial) nextSpecial = linkIdx;
            if (httpIdx != -1 && httpIdx < nextSpecial) nextSpecial = httpIdx;
            if (httpsIdx != -1 && httpsIdx < nextSpecial) nextSpecial = httpsIdx;

            var normalChunk = text.Substring(i, nextSpecial - i);
            inlines.Add(new Run(normalChunk));
            i = nextSpecial;
        }
    }

    private Hyperlink CreateHyperlink(string title, string url)
    {
        var run = new Run(title);
        var link = new Hyperlink(run)
        {
            NavigateUri = new Uri(url),
            ToolTip = url,
            TextDecorations = TextDecorations.Underline
        };
        link.SetResourceReference(Hyperlink.ForegroundProperty, "AccentBrush");
        link.Click += Hyperlink_Click;
        return link;
    }

    private void Hyperlink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink link && link.NavigateUri != null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = link.NavigateUri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore web navigation errors
            }
        }
    }

    #endregion
}
