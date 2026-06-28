using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HTPC.Services;

// A simple model to map the GitHub JSON response
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
        
        // GitHub's API STRICTLY requires a User-Agent header, otherwise it returns a 403 Forbidden.
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Nucleus-HTPC-Updater");
    }

    public async Task<(bool UpdateAvailable, string LatestVersion, string ReleaseUrl)> CheckForUpdatesAsync()
    {
        try
        {
            // 1. Fetch the latest release data from your public repository
            var url = "https://api.github.com/repos/nuken/htpc/releases/latest";
            var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(url);

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                return (false, string.Empty, string.Empty);

            // 2. Clean the GitHub version string (e.g., "v1.1.0" becomes "1.1.0")
            string cleanGitHubVersion = release.TagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) 
                ? release.TagName.Substring(1) 
                : release.TagName;

            if (!Version.TryParse(cleanGitHubVersion, out Version githubVersion))
                return (false, string.Empty, string.Empty);

            // 3. Get the actual version of the running Nucleus HTPC application
            var localVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

            // 4. Mathematically compare the versions
            if (githubVersion > localVersion)
            {
                return (true, release.TagName, release.HtmlUrl);
            }
        }
        catch
        {
            // If the user has no internet or GitHub is down, fail silently. 
            // We never want a background update check to crash the HTPC experience.
        }

        return (false, string.Empty, string.Empty);
    }
}