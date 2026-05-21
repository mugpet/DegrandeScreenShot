using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DegrandeScreenShot.App.Services;

public record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl
);

public record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("assets")] List<GitHubAsset> Assets
);

public static class UpdateService
{
    private const string RepoOwner = "mugpet";
    private const string RepoName = "DegrandeScreenShot";

    public static Version GetCurrentVersion()
    {
        return typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    public static async Task<GitHubRelease?> GetLatestReleaseAsync()
    {
        using var client = new HttpClient();
        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "DegrandeScreenShot-App");

        try
        {
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<GitHubRelease>(json, options);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsUpdateAvailable(string latestTagName, Version currentVersion, out Version? parsedVersion)
    {
        parsedVersion = null;
        if (string.IsNullOrWhiteSpace(latestTagName))
        {
            return false;
        }

        var cleanTag = latestTagName.Trim();
        if (cleanTag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            cleanTag = cleanTag.Substring(1);
        }

        if (Version.TryParse(cleanTag, out var latestVersion))
        {
            parsedVersion = latestVersion;
            
            // Standard version comparison
            // Note: assembly version from build props is e.g. 0.2.16.0
            // and tag is 0.2.17.
            // Under standard Version comparison, 0.2.17 (which has Build = -1)
            // is compared. C# Version.CompareTo handles different component lengths:
            // if a component is undefined (-1), it's treated as 0 for major/minor but can cause subtle checks.
            // Let's normalize both to 4-component versions for robust comparison:
            var normLatest = NormalizeVersion(latestVersion);
            var normCurrent = NormalizeVersion(currentVersion);

            return normLatest > normCurrent;
        }

        return false;
    }

    private static Version NormalizeVersion(Version v)
    {
        return new Version(
            v.Major >= 0 ? v.Major : 0,
            v.Minor >= 0 ? v.Minor : 0,
            v.Build >= 0 ? v.Build : 0,
            v.Revision >= 0 ? v.Revision : 0
        );
    }

    public static GitHubAsset? FindInstallerAsset(GitHubRelease release)
    {
        if (release.Assets == null || release.Assets.Count == 0)
        {
            return null;
        }

        // 1. Look for .exe ending asset containing "Setup" (case-insensitive)
        var setupAsset = release.Assets.FirstOrDefault(a => 
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && 
            a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase));

        if (setupAsset != null)
        {
            return setupAsset;
        }

        // 2. Fall back to any .exe asset
        var exeAsset = release.Assets.FirstOrDefault(a => 
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (exeAsset != null)
        {
            return exeAsset;
        }

        // 3. Fall back to the first asset available
        return release.Assets.FirstOrDefault();
    }

    public static async Task DownloadFileWithProgressAsync(
        string downloadUrl, 
        string destinationPath, 
        IProgress<double> progress, 
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "DegrandeScreenShot-App");

        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        
        // Ensure folder exists
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        var totalRead = 0L;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            
            totalRead += bytesRead;
            if (totalBytes > 0)
            {
                var percentage = (double)totalRead / totalBytes;
                progress.Report(percentage);
            }
        }
        
        progress.Report(1.0); // Make sure it hits 100% explicitly
    }

    public static void ExecuteInstallerAndExit(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("Installer file not found.", installerPath);
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        });

        System.Windows.Application.Current.Shutdown();
    }
}
