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
// PLUTO TV API MODELS
// ==========================================
public class PlutoApiResponse
{
    [JsonPropertyName("categories")] public List<PlutoCategory>? Categories { get; set; }
}

public class PlutoCategory
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("items")] public List<PlutoItem>? Items { get; set; }
}

public class PlutoItem
{
    [JsonPropertyName("_id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("genre")] public string? Genre { get; set; }
    
    // Perfectly mapped to the JSON sample
    [JsonPropertyName("covers")] public List<PlutoCover>? Covers { get; set; } 
    
    [JsonPropertyName("featuredImage")] public PlutoImage? FeaturedImage { get; set; }
    [JsonPropertyName("clip")] public PlutoClip? Clip { get; set; }
}

public class PlutoCover
{
    [JsonPropertyName("aspectRatio")] public string? AspectRatio { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public class PlutoImage
{
    [JsonPropertyName("path")] public string? Path { get; set; }
}

public class PlutoClip
{
    [JsonPropertyName("originalReleaseDate")] public string? OriginalReleaseDate { get; set; }
}

// ==========================================
// VOD SYNC SERVICE
// ==========================================
public class VodSyncService
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VodSyncService> _logger;
    private readonly string _logFilePath;

    public VodSyncService(HttpClient httpClient, IServiceScopeFactory scopeFactory, ILogger<VodSyncService> logger)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _logger = logger;
        
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _logFilePath = Path.Combine(desktopPath, "pluto_sync.log");
    }

    private void LogToFile(string message)
    {
        try
        {
            File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            _logger.LogInformation(message);
        }
        catch { } 
    }

    public async Task SyncPlutoTvCatalogAsync()
    {
        LogToFile("Starting direct Pluto TV VOD catalog sync...");

        string apiUrl = "https://api.pluto.tv/v3/vod/categories?includeItems=true&deviceType=web";

        try
        {
            LogToFile("Downloading Pluto TV JSON payload...");
            var apiResponse = await _httpClient.GetFromJsonAsync<PlutoApiResponse>(apiUrl);
            
            if (apiResponse?.Categories == null)
            {
                LogToFile("Failed: Pluto API returned null or missing categories array.");
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // --- AUTOMATIC UPGRADE PATCH ---
            // Safely injects the new table for existing users updating from older versions.
            // If the table already exists, SQLite simply ignores this command.
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

            foreach (var category in apiResponse.Categories)
            {
                if (category.Items == null) continue;

                foreach (var plutoItem in category.Items)
                {
                    // ADDED: plutoItem == null check to prevent crashes on empty catalog items
                    if (plutoItem == null || plutoItem.Type != "movie" || string.IsNullOrWhiteSpace(plutoItem.Slug) || string.IsNullOrWhiteSpace(plutoItem.Id)) continue;
                    if (!processedIds.Add(plutoItem.Id)) continue; 

                    string deepLink = $"https://pluto.tv/en/ondemand/movies/{plutoItem.Slug}/details";
                    string poster = ExtractBestPoster(plutoItem);
                    int releaseYear = ExtractReleaseYear(plutoItem);

                    string title = plutoItem.Name ?? "Unknown";
                    string overview = !string.IsNullOrWhiteSpace(plutoItem.Description) ? plutoItem.Description : (plutoItem.Summary ?? "");
                    string genre = plutoItem.Genre ?? category.Name ?? "Movies";

                    var cachedItem = existingItems.FirstOrDefault(v => v.NativeDeepLink == deepLink);

                    if (cachedItem != null)
                    {
                        cachedItem.Title = title;
                        cachedItem.Year = releaseYear;
                        cachedItem.PosterUrl = poster;
                        cachedItem.Overview = overview;
                        cachedItem.Genre = genre;
                        cachedItem.IsActive = true;
                        cachedItem.LastVerified = DateTime.UtcNow;
                    }
                    else
                    {
                        db.VodCatalog.Add(new VODCatalogItem
                        {
                            TmdbId = 0, 
                            Title = title,
                            Year = releaseYear,
                            PosterUrl = poster,
                            Overview = overview,
                            Genre = genre,
                            ProviderKey = "pluto",
                            NativeDeepLink = deepLink,
                            IsActive = true,
                            LastVerified = DateTime.UtcNow
                        });
                    }
                    successCount++;
                }
            }

            LogToFile($"Saving {successCount} movies to local SQLite database...");
            await db.SaveChangesAsync();
            
            LogToFile($"Pluto TV sync complete. Successfully processed {successCount} unique movies directly from Pluto.");
        }
        catch (Exception ex)
        {
            LogToFile($"FATAL EXCEPTION during Pluto sync: {ex.Message}");
            
            // Check if the UI has booted before trying to show a popup
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"The Pluto VOD Sync failed to download.\n\nError: {ex.Message}\n\nCheck the log file on your Desktop for details.", "VOD Sync Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

    private string ExtractBestPoster(PlutoItem item)
    {
        if (item.Covers != null)
        {
            // ADDED: c?.AspectRatio to safely skip null cover objects in the JSON array
            var verticalCover = item.Covers.FirstOrDefault(c => c?.AspectRatio == "347:500");
            if (verticalCover != null && !string.IsNullOrWhiteSpace(verticalCover.Url))
            {
                return verticalCover.Url;
            }
        }
        
        return item.FeaturedImage?.Path ?? "";
    }

    private int ExtractReleaseYear(PlutoItem item)
    {
        int year = DateTime.Now.Year; 
        if (!string.IsNullOrWhiteSpace(item.Clip?.OriginalReleaseDate) && item.Clip.OriginalReleaseDate.Length >= 4)
        {
            int.TryParse(item.Clip.OriginalReleaseDate.Substring(0, 4), out year);
        }
        return year;
    }
}