using System;
using System.Diagnostics;
using System.IO;         
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HTPC.Services;

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;
}

public class UpdateService
{
    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Nucleus-HTPC-Updater");
    }

    public async Task<(bool UpdateAvailable, string LatestVersion, string ReleaseUrl)> CheckForUpdatesAsync()
    {
        try
        {
            var url = "https://api.github.com/repos/nuken/htpc/releases/latest";
            var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(url);

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                return (false, string.Empty, string.Empty);

            string cleanGitHubVersion = release.TagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) 
                ? release.TagName.Substring(1) 
                : release.TagName;

            if (!Version.TryParse(cleanGitHubVersion, out Version? githubVersion))
                return (false, string.Empty, string.Empty);

            var localVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

            if (githubVersion > localVersion)
            {
                return (true, release.TagName, release.HtmlUrl);
            }
        }
        catch
        {
            // Fail silently
        }

        return (false, string.Empty, string.Empty);
    }

    // --- THE MISSING METHOD ---
    public async Task<string?> DownloadInstallerAsync(string tagName)
    {
        try
        {
            // Construct the direct download URL using the predictable GitHub release structure
            string downloadUrl = $"https://github.com/nuken/htpc/releases/download/{tagName}/NucleusHTPC_Installer_{tagName}.exe";
            
            string tempPath = Path.Combine(Path.GetTempPath(), "NucleusHTPC_Update.exe");
            
            // Ensure any previous failed download is cleared out
            if (File.Exists(tempPath)) File.Delete(tempPath);
            
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs);
            
            return tempPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update Download Failed: {ex.Message}");
            return null;
        }
    }
}