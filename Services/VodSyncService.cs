using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using HTPC.Core.Data;
using HTPC.Core.Models;

namespace HTPC.Services;

// ==========================================
// PLUTO FOR CHANNELS - VOD PROXY MODEL
// ==========================================
public class ProxyVodItem
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("genre")] public string? Genre { get; set; }
    [JsonPropertyName("release_year")] public int ReleaseYear { get; set; }
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    [JsonPropertyName("video_url")] public string? VideoUrl { get; set; }
}

public class VodSyncService
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VodSyncService> _logger;
    

    public VodSyncService(HttpClient httpClient, IServiceScopeFactory scopeFactory, ILogger<VodSyncService> logger)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _logger = logger;
        
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        
    }

    private void LogToFile(string message)
{
    _logger.LogInformation(message);
}

    public async Task SyncPlutoTvCatalogAsync()
    {
        LogToFile("Starting direct PlutoForChannels VOD proxy sync...");

        var prefs = PreferencesManager.Load();
        string apiUrl = prefs.AdbTunerUrl; // We are repurposing this setting to hold the vod.json link

        if (string.IsNullOrWhiteSpace(apiUrl) || !apiUrl.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            LogToFile("Failed: The AdbTunerUrl setting must point to the vod.json file.");
            return;
        }

        try
        {
            LogToFile($"Downloading VOD catalog from {apiUrl}...");
            var proxyItems = await _httpClient.GetFromJsonAsync<List<ProxyVodItem>>(apiUrl);
            
            if (proxyItems == null || proxyItems.Count == 0)
            {
                LogToFile("Failed: Proxy API returned null or empty array.");
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ""VodCatalog"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_VodCatalog"" PRIMARY KEY AUTOINCREMENT,
                    ""TmdbId"" INTEGER NOT NULL,
                    ""Title"" TEXT NULL,
                    ""Year"" INTEGER NOT NULL,
                    ""PosterUrl"" TEXT NULL,
                    ""Overview"" TEXT NULL,
                    ""Genre"" TEXT NULL,
                    ""ProviderKey"" TEXT NULL,
                    ""NativeDeepLink"" TEXT NULL,
                    ""IsActive"" INTEGER NOT NULL,
                    ""LastVerified"" TEXT NOT NULL
                );
            ");

            var existingItems = db.VodCatalog.Where(v => v.ProviderKey == "pluto").ToList();
            foreach (var item in existingItems) 
            {
                item.IsActive = false;
            }

            int successCount = 0;
            var processedIds = new HashSet<string>();

            foreach (var item in proxyItems)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.VideoUrl)) continue;
                if (!processedIds.Add(item.Id)) continue; 

                var cachedItem = existingItems.FirstOrDefault(v => v.NativeDeepLink == item.VideoUrl);

                if (cachedItem != null)
                {
                    cachedItem.Title = item.Title ?? "Unknown";
                    cachedItem.Year = item.ReleaseYear;
                    cachedItem.PosterUrl = item.ImageUrl ?? "";
                    cachedItem.Overview = item.Summary ?? "";
                    cachedItem.Genre = item.Genre ?? "Movies";
                    cachedItem.IsActive = true;
                    cachedItem.LastVerified = DateTime.UtcNow;
                }
                else
                {
                    db.VodCatalog.Add(new VODCatalogItem
                    {
                        TmdbId = 0, 
                        Title = item.Title ?? "Unknown",
                        Year = item.ReleaseYear,
                        PosterUrl = item.ImageUrl ?? "",
                        Overview = item.Summary ?? "",
                        Genre = item.Genre ?? "Movies",
                        ProviderKey = "pluto",
                        NativeDeepLink = item.VideoUrl, // Store the direct proxy HLS URL here!
                        IsActive = true,
                        LastVerified = DateTime.UtcNow
                    });
                }
                successCount++;
            }

            LogToFile($"Saving {successCount} movies to local SQLite database...");
            await db.SaveChangesAsync();
            
            LogToFile($"VOD Proxy sync complete. Successfully processed {successCount} unique movies.");
        }
        catch (Exception ex)
        {
            LogToFile($"FATAL EXCEPTION during proxy sync: {ex.Message}");
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"The VOD Sync failed to download.\n\nError: {ex.Message}", "VOD Sync Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
    }   
}