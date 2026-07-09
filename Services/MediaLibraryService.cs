using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json; 
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using HTPC.Core.Models;
using System.Linq;

namespace HTPC.Services;

public class MediaLibraryService
{
    private readonly ServerManagerService _serverManager;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MediaLibraryService> _logger;
    private List<MediaItem>? _masterMoviesCache = null;

    public MediaLibraryService(ServerManagerService serverManager, HttpClient httpClient, ILogger<MediaLibraryService> logger)
    {
        _serverManager = serverManager;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<string>> GetDevicePriorityListAsync()
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<string>();

        string url = $"http://{activeServer.IpAddress}:{activeServer.Port}/devices/priority";
        
        try
        {
            var devices = await _httpClient.GetFromJsonAsync<List<DevicePriority>>(url);
            return devices?.Where(d => !string.IsNullOrEmpty(d.DeviceId))
                           .Select(d => d.DeviceId!)
                           .ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to fetch device priority list: {ex.Message}");
            return new List<string>();
        }
    }

    public List<Channel> StackAndFilterChannels(List<Channel> allChannels, List<string> priorityList)
    {
        var stackedChannels = new List<Channel>();
        var groupedChannels = allChannels.GroupBy(c => !string.IsNullOrWhiteSpace(c.StationId) ? c.StationId : c.Name);

        foreach (var group in groupedChannels)
        {
            if (group.Count() == 1)
            {
                stackedChannels.Add(group.First());
                continue;
            }

            var highestPriorityChannel = group.OrderBy(c =>
            {
                if (string.IsNullOrWhiteSpace(c.DeviceId)) return int.MaxValue; 
                int priorityIndex = priorityList.IndexOf(c.DeviceId);
                return priorityIndex == -1 ? int.MaxValue : priorityIndex;
            }).First();

            stackedChannels.Add(highestPriorityChannel);
        }

        return stackedChannels;
    }
	
	// --- NEW: LIBRARY COLLECTIONS ENDPOINTS ---
    
    public async Task<List<CollectionItem>> GetLibraryCollectionsAsync()
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<CollectionItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/collections"; 

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            var collections = new List<CollectionItem>();
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string id = GetStringOrNumber(element, "id");
                    if (string.IsNullOrEmpty(id)) continue;

                    collections.Add(new CollectionItem
                    {
                        Id = id,
                        Name = GetStringOrNumber(element, "name"),
                        CollectionType = GetStringOrNumber(element, "collection_type"),
                        ImageUrl = GetBestImageUrl(baseUrl, element, id),
                        ContentCount = element.TryGetProperty("content_count", out var cc) && cc.ValueKind == JsonValueKind.Number ? cc.GetInt32() : 0
                    });
                }
            }
            return collections;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch Library Collections: {ex.Message}");
            return new List<CollectionItem>();
        }
    }

    public async Task<List<MediaItem>> GetCollectionMediaAsync(string collectionId)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null || string.IsNullOrWhiteSpace(collectionId)) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        // The API sorts based on the user's custom preference by default
        string apiUrl = $"{baseUrl}/api/v1/collections/{collectionId}/content"; 

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            var items = new List<MediaItem>();
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
{
    string id = GetStringOrNumber(element, "id");
    if (string.IsNullOrEmpty(id)) continue;

    string title = GetStringOrNumber(element, "title", "name"); // Added "name" as fallback
    string videoUrl = GetStringOrNumber(element, "VideoURL", "video_url");
    
    // NEW: Extract the Categories array so the UI knows if it's a Show or Movie
    var categories = new List<string>();
    if (element.TryGetProperty("categories", out var catElement) && catElement.ValueKind == System.Text.Json.JsonValueKind.Array)
    {
        foreach (var cat in catElement.EnumerateArray())
        {
            if (cat.ValueKind == System.Text.Json.JsonValueKind.String)
                categories.Add(cat.GetString() ?? "");
        }
    }

    if (element.TryGetProperty("episode_count", out _))
    {
        if (!categories.Contains("Series")) 
        {
            categories.Add("Series");
        }
    }

    items.Add(new MediaItem
    {
        Id = id,
        Title = string.IsNullOrEmpty(title) ? "Unknown" : title,
        Categories = categories, // <--- Now guaranteed to include "Series" for all shows
        PosterUrl = GetBestImageUrl(baseUrl, element, id),
        StreamUrl = !string.IsNullOrEmpty(videoUrl) ? videoUrl : $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
        Path = GetStringOrNumber(element, "Path", "path"),
        Summary = GetStringOrNumber(element, "summary", "full_summary"),
        Commercials = ParseDoubleArray(element, "commercials")
    });
}
            }
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch content for collection {collectionId}: {ex.Message}");
            return new List<MediaItem>();
        }
    }
	
	public async Task<List<MediaItem>> GetShowEpisodesAsync(string showId)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null || string.IsNullOrWhiteSpace(showId)) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string encodedShowId = Uri.EscapeDataString(showId);
        string apiUrl = $"{baseUrl}/api/v1/shows/{encodedShowId}/episodes"; 

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
            var items = new List<MediaItem>();
            
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string id = GetStringOrNumber(element, "id");
                    string episodeTitle = GetStringOrNumber(element, "episode_title");
                    string showTitle = GetStringOrNumber(element, "title");
                    
                    int seasonNum = element.TryGetProperty("season_number", out var sn) && sn.ValueKind == System.Text.Json.JsonValueKind.Number ? sn.GetInt32() : 0;
                    int epNum = element.TryGetProperty("episode_number", out var en) && en.ValueKind == System.Text.Json.JsonValueKind.Number ? en.GetInt32() : 0;

                    items.Add(new MediaItem
                    {
                        Id = id,
                        Title = !string.IsNullOrEmpty(episodeTitle) ? episodeTitle : showTitle,
                        CurrentShowTitle = showTitle,
                        SeasonNumber = seasonNum,
                        EpisodeNumber = epNum,
                        PosterUrl = GetBestImageUrl(baseUrl, element, id),
                        StreamUrl = $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        Path = GetStringOrNumber(element, "path", "Path"),
                        Summary = GetStringOrNumber(element, "summary"),
                        IsWatched = element.TryGetProperty("watched", out var w) && w.GetBoolean(),
                        IsFavorite = element.TryGetProperty("favorited", out var f) && f.GetBoolean()
                    });
                }
            }
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch all episodes for show {showId}: {ex.Message}");
            return new List<MediaItem>();
        }
    }
	
	public async Task<MediaItem?> GetNextUnwatchedEpisodeAsync(string showId)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null || string.IsNullOrWhiteSpace(showId)) return null;

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string encodedShowId = Uri.EscapeDataString(showId);
        string apiUrl = $"{baseUrl}/api/v1/shows/{encodedShowId}/episodes";
        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
            
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var unwatchedEpisodes = new System.Collections.Generic.List<System.Text.Json.JsonElement>();

                // 1. Gather all unwatched episodes
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    bool isWatched = element.TryGetProperty("watched", out var w) && w.GetBoolean();
                    if (!isWatched)
                    {
                        unwatchedEpisodes.Add(element);
                    }
                }

                // 2. If we found unwatched episodes, play the oldest one first
                if (unwatchedEpisodes.Count > 0)
                {
                    // Sort by 'created_at' ascending (oldest first)
                    var nextEpisode = System.Linq.Enumerable.OrderBy(unwatchedEpisodes, e => 
                        e.TryGetProperty("created_at", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Number 
                        ? c.GetInt64() 
                        : long.MaxValue).First();

                    string id = GetStringOrNumber(nextEpisode, "id");
                    string episodeTitle = GetStringOrNumber(nextEpisode, "episode_title");
                    string showTitle = GetStringOrNumber(nextEpisode, "title");
                    
                    return new MediaItem
                    {
                        Id = id,
                        // Use episode_title if it exists, otherwise fallback to the show title
                        Title = !string.IsNullOrEmpty(episodeTitle) ? episodeTitle : showTitle,
                        CurrentShowTitle = showTitle,
                        PosterUrl = GetBestImageUrl(baseUrl, nextEpisode, id),
                        // Construct the native MPEG-TS stream URL
                        StreamUrl = $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        Path = GetStringOrNumber(nextEpisode, "path", "Path"),
                        Summary = GetStringOrNumber(nextEpisode, "summary") // Will safely be empty if not present
                    };
                }
            }
            return null; // All episodes are watched, or show has no episodes
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch episodes for show {showId}: {ex.Message}");
            return null;
        }
    }

    private async Task EnsureMoviesCacheAsync()
    {
        if (_masterMoviesCache != null) return;

        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return;
        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/movies"; 

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            _masterMoviesCache = new List<MediaItem>();
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string id = GetStringOrNumber(element, "id");
                    if (string.IsNullOrEmpty(id)) continue;

                    string title = GetStringOrNumber(element, "title");
                    
                    // --- ARCHITECTURE FIX: Centralized Image Selection ---
                    string posterUrl = GetBestImageUrl(baseUrl, element, id);

                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt64() : 0;
                    int year = element.TryGetProperty("release_year", out var yProp) && yProp.ValueKind == JsonValueKind.Number ? yProp.GetInt32() : 0;

                    bool isWatched = false;
                    if (element.TryGetProperty("watched", out var w1) || element.TryGetProperty("Watched", out w1))
                        isWatched = w1.ValueKind == JsonValueKind.True || (w1.ValueKind == JsonValueKind.Number && w1.GetInt32() == 1);

                    bool isFavorite = false;
                    if (element.TryGetProperty("favorited", out var f1) || element.TryGetProperty("Favorited", out f1))
                        isFavorite = f1.ValueKind == JsonValueKind.True || (f1.ValueKind == JsonValueKind.Number && f1.GetInt32() == 1);
                    else if (element.TryGetProperty("favorited_at", out var f2) || element.TryGetProperty("FavoritedAt", out f2))
                        isFavorite = f2.ValueKind == JsonValueKind.Number && f2.GetInt64() > 0;

                    string videoUrl = GetStringOrNumber(element, "VideoURL", "video_url");

                    _masterMoviesCache.Add(new MediaItem
                    {
						Path = GetStringOrNumber(element, "Path", "path"),
                        Id = id,
						SubtitleUrl = $"{baseUrl}/dvr/files/{id}/subtitles.vtt",
                        Title = string.IsNullOrEmpty(title) ? "Unknown Movie" : title,
                        PosterUrl = posterUrl,
                        StreamUrl = !string.IsNullOrEmpty(videoUrl) ? videoUrl : $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        CreatedAt = createdAt,
                        ReleaseYear = year,
                        Genres = ParseStringArray(element, "genres"),
                        Cast = ParseStringArray(element, "cast"),
                        Directors = ParseStringArray(element, "directors"),
                        IsWatched = isWatched,
                        IsFavorite = isFavorite,
						Commercials = ParseDoubleArray(element, "commercials")
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch master movies list: {ex.Message}");
            _masterMoviesCache = new List<MediaItem>();
        }
    }
	
	public async Task<MediaItem?> GetNextEpisodeAsync(MediaItem currentEpisode)
    {
        if (string.IsNullOrEmpty(currentEpisode.Title)) return null;

        var server = _serverManager.GetActiveServer();
        if (server == null) return null;

        string baseUrl = $"http://{server.IpAddress}:{server.Port}";
        string url = $"{baseUrl}/dvr/files"; 

        try
        {
            var response = await _httpClient.GetStringAsync(url);
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(response))
            {
                var episodes = new System.Collections.Generic.List<MediaItem>();
                
                foreach (System.Text.Json.JsonElement element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("Airing", out System.Text.Json.JsonElement airing))
                    {
                        string title = airing.TryGetProperty("Title", out var t) ? t.GetString() ?? "" : "";
                        if (title.Equals(currentEpisode.Title, StringComparison.OrdinalIgnoreCase))
                        {
                            int season = airing.TryGetProperty("SeasonNumber", out var sn) ? sn.GetInt32() : 0;
                            int epNum = airing.TryGetProperty("EpisodeNumber", out var en) ? en.GetInt32() : 0;
                            
                            string id = element.GetProperty("ID").GetString() ?? "";
                            string episodeTitle = airing.TryGetProperty("EpisodeTitle", out var et) ? et.GetString() ?? "" : "";
                            
                            string posterUrl = GetBestImageUrl(baseUrl, airing, id);
                            string videoUrl = element.TryGetProperty("VideoURL", out var vUrl) ? vUrl.GetString() ?? "" : "";

                            episodes.Add(new MediaItem 
                            { 
							    Path = GetStringOrNumber(element, "Path", "path"),
                                Id = id, 
								SubtitleUrl = $"{baseUrl}/dvr/files/{id}/subtitles.vtt",
                                Title = title, 
                                CurrentShowTitle = episodeTitle,
                                SeasonNumber = season,
                                EpisodeNumber = epNum,
                                StreamUrl = !string.IsNullOrEmpty(videoUrl) ? videoUrl : $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                                PosterUrl = posterUrl,
                                Commercials = ParseDoubleArray(element, "commercials")
                            });
                        }
                    }
                }

                var sorted = episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
                return sorted.FirstOrDefault(e => 
                    e.SeasonNumber > currentEpisode.SeasonNumber || 
                    (e.SeasonNumber == currentEpisode.SeasonNumber && e.EpisodeNumber > currentEpisode.EpisodeNumber));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error pre-fetching next episode: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ToggleChannelFavoriteAsync(string baseUrl, string deviceId, string guideNumber)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            string requestUri = $"{baseUrl}/devices/{deviceId}/channels/{guideNumber}/toggle_favorite";
            var response = await client.PutAsync(requestUri, null);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }
	
	public async Task<bool> ToggleChannelHiddenAsync(string baseUrl, string deviceId, string guideNumber)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            string requestUri = $"{baseUrl}/devices/{deviceId}/channels/{guideNumber}/toggle_hidden";
            var response = await client.PutAsync(requestUri, null);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> CreateRecordingJobAsync(string baseUrl, string channelNumber, Airing airing, int padStartSeconds = 0, int padEndSeconds = 0)
    {
        if (airing == null || string.IsNullOrWhiteSpace(channelNumber)) return false;

        try
        {
            string url = $"{baseUrl.TrimEnd('/')}/dvr/jobs/new";
            long startTimeEpoch = new DateTimeOffset(airing.StartTime).ToUnixTimeSeconds();
            int durationSeconds = (int)(airing.Duration ?? 3600);
            
            long jobStartTime = startTimeEpoch - padStartSeconds;
            int jobDuration = durationSeconds + padStartSeconds + padEndSeconds;
            
            var payload = new
            {
                Name = !string.IsNullOrWhiteSpace(airing.Title) ? airing.Title : "Unknown Program",
                Time = jobStartTime,
                Duration = jobDuration,
                Channels = new[] { channelNumber },
                Airing = new
                {
                    Source = "tms", 
                    Channel = channelNumber,
                    Time = startTimeEpoch,
                    Duration = durationSeconds,
                    Title = !string.IsNullOrWhiteSpace(airing.Title) ? airing.Title : "Unknown Program",
                    EpisodeTitle = airing.EpisodeTitle ?? "",
                    Summary = airing.DisplaySummary ?? "",
                    SeriesID = airing.SeriesId ?? "",
                    ProgramID = airing.ProgramId ?? "",
                    Image = airing.ImageUrl ?? ""
                }
            };

            var content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url) { Content = content };
            using var client = new System.Net.Http.HttpClient();
            var response = await client.SendAsync(request);
            
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> CreateSeriesPassAsync(string baseUrl, string seriesId, string title, string imageUrl, int padStartSeconds = 0, int padEndSeconds = 0)
    {
        if (string.IsNullOrWhiteSpace(seriesId)) return false;

        try
        {
            string url = $"{baseUrl.TrimEnd('/')}/dvr/rules/new";
            var rulePayload = new 
            {
                Name = title,
                Image = imageUrl,
                PaddingStart = padStartSeconds,
                PaddingEnd = padEndSeconds,
                EQ = new { SeriesID = seriesId, Tags = "New" }
            };

            var content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(rulePayload), System.Text.Encoding.UTF8, "application/json");
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url) { Content = content };
            using var client = new System.Net.Http.HttpClient();
            var response = await client.SendAsync(request);
            
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<Channel>> GetGuideChannelsAsync(ChannelCollection? activeCollection = null, int durationHours = 4)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<Channel>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        List<string> devicePriority = await GetDevicePriorityListAsync();
        
        try
        {
            var resultChannels = new List<Channel>();
            var collectionSortOrder = new Dictionary<string, int>(); 

            string channelsJson = await _httpClient.GetStringAsync($"{baseUrl}/devices?all=true");
            
            long unixTime = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
            long durationSeconds = durationHours * 3600; 
            string guideJson = await _httpClient.GetStringAsync($"{baseUrl}/devices/ANY/guide?time={unixTime}&duration={durationSeconds}");

            var hdDictionary = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string apiChannelsJson = await _httpClient.GetStringAsync($"{baseUrl}/api/v1/channels");
                using JsonDocument apiDoc = JsonDocument.Parse(apiChannelsJson);
                if (apiDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in apiDoc.RootElement.EnumerateArray())
                    {
                        string num = GetStringOrNumber(c, "number").Trim();
                        string name = GetStringOrNumber(c, "name").ToUpper();
                        bool isHd = false;

                        if (c.TryGetProperty("hd", out var hdProp) || c.TryGetProperty("HD", out hdProp))
                            isHd = hdProp.ValueKind == JsonValueKind.True || (hdProp.ValueKind == JsonValueKind.Number && hdProp.GetInt32() == 1);

                        if (!isHd && (!string.IsNullOrEmpty(name) && (name.Contains("-HD") || name.EndsWith(" HD"))))
                            isHd = true;

                        if (!string.IsNullOrEmpty(num)) hdDictionary[num] = isHd;
                    }
                }
            }
            catch { }

            var stationLogoDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string stationsJson = await _httpClient.GetStringAsync($"{baseUrl}/dvr/guide/stations");
                using JsonDocument stationsDoc = JsonDocument.Parse(stationsJson);
                if (stationsDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in stationsDoc.RootElement.EnumerateArray())
                    {
                        string sId = GetStringOrNumber(s, "id", "stationId");
                        string sLogo = GetStringOrNumber(s, "logo");
                        if (!string.IsNullOrEmpty(sId) && !string.IsNullOrEmpty(sLogo))
                            stationLogoDict[sId] = sLogo;
                    }
                }
            }
            catch { }

            using JsonDocument channelsDoc = JsonDocument.Parse(channelsJson);
            using JsonDocument guideDoc = JsonDocument.Parse(guideJson);
            
            var guideDict = new Dictionary<string, JsonElement>();
            if (guideDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in guideDoc.RootElement.EnumerateArray())
                {
                    if (g.TryGetProperty("Channel", out var cProp))
                    {
                        string chNum = GetStringOrNumber(cProp, "GuideNumber", "Number", "number");
                        if (!string.IsNullOrEmpty(chNum)) guideDict[chNum] = g;
                    }
                }
            }

            DateTime gridStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Minute >= 30 ? 30 : 0, 0);

            if (channelsDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var deviceBlock in channelsDoc.RootElement.EnumerateArray())
                {
                    string currentDeviceId = GetStringOrNumber(deviceBlock, "DeviceID");

                    bool isDisabled = false;
                    if (deviceBlock.TryGetProperty("Disabled", out var disabledNode))
                    {
                        isDisabled = disabledNode.ValueKind == JsonValueKind.True || 
                                     (disabledNode.ValueKind == JsonValueKind.Number && disabledNode.GetInt32() == 1);
                    }
                    
                    if (isDisabled) continue;

                    if (deviceBlock.TryGetProperty("Channels", out var channelsArray) && channelsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var channelProp in channelsArray.EnumerateArray())
                        {
                            string channelNumber = GetStringOrNumber(channelProp, "guidenumber", "number").Trim();
                            string channelId = GetStringOrNumber(channelProp, "id", "channelid");
                            string name = GetStringOrNumber(channelProp, "guidename", "name");
                            string callSign = GetStringOrNumber(channelProp, "callsign", "station", "tmsid");
                            string stationId = GetStringOrNumber(channelProp, "stationId", "station"); 
                            string logoUrl = GetStringOrNumber(channelProp, "image", "logo", "art", "thumbnail");
                            bool isFavorite = channelProp.TryGetProperty("Favorite", out var favRoot) && (favRoot.ValueKind == JsonValueKind.True || (favRoot.ValueKind == JsonValueKind.Number && favRoot.GetInt32() == 1));
                            bool isHidden = channelProp.TryGetProperty("Hidden", out var hidRoot) && (hidRoot.ValueKind == JsonValueKind.True || (hidRoot.ValueKind == JsonValueKind.Number && hidRoot.GetInt32() == 1));
                            
                            if (string.IsNullOrWhiteSpace(channelNumber)) continue;

                            string targetId = !string.IsNullOrWhiteSpace(stationId) ? stationId : callSign;
                            if (string.IsNullOrWhiteSpace(logoUrl) && !string.IsNullOrWhiteSpace(targetId))
                            {
                                if (stationLogoDict.TryGetValue(targetId, out string? mappedLogo)) logoUrl = mappedLogo;
                            }

                            if (string.IsNullOrWhiteSpace(logoUrl) && guideDict.TryGetValue(channelNumber, out var gDataFallback))
                            {
                                if (gDataFallback.TryGetProperty("Channel", out var gChan))
                                {
                                    string gImage = GetStringOrNumber(gChan, "image", "logo", "art");
                                    if (!string.IsNullOrWhiteSpace(gImage)) logoUrl = gImage;
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(logoUrl))
                            {
                                if (logoUrl.StartsWith("tmsimg://", StringComparison.OrdinalIgnoreCase))
                                    logoUrl = logoUrl.Replace("tmsimg://", $"{baseUrl}/tmsimg/", StringComparison.OrdinalIgnoreCase);
                                else if (!logoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                    logoUrl = $"{baseUrl}/{logoUrl.TrimStart('/')}";
                            }

                            int sortIndex = 999999;
                            var airings = new List<Airing>();

                            if (guideDict.TryGetValue(channelNumber, out var guideData))
                            {
                                if (guideData.TryGetProperty("Airings", out var airingsProp) && airingsProp.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var a in airingsProp.EnumerateArray())
                                    {
                                        long startUnix = a.TryGetProperty("Time", out var tProp) && tProp.ValueKind == JsonValueKind.Number ? tProp.GetInt64() : 0;
                                        double duration = a.TryGetProperty("Duration", out var dProp) && dProp.ValueKind == JsonValueKind.Number ? dProp.GetDouble() : 0;
                                        DateTime startTime = DateTimeOffset.FromUnixTimeSeconds(startUnix).LocalDateTime;

                                        if (startTime.AddSeconds(duration) > gridStart)
                                        {
                                            airings.Add(new Airing
                                            {
                                                ChannelNumber = channelNumber,
                                                Title = GetStringOrNumber(a, "Title"),
                                                EpisodeTitle = GetStringOrNumber(a, "EpisodeTitle"),
                                                DisplaySummary = GetStringOrNumber(a, "Summary"),
                                                ImageUrl = GetStringOrNumber(a, "Image"),
                                                StartTime = startTime,
                                                Duration = duration,
                                                Source = GetStringOrNumber(a, "Source", "source"), 
                                                CategoryColor = DetermineColor(ParseStringArray(a, "Categories", "Genres")),
                                                Genres = ParseStringArray(a, "Genres"),
                                                Categories = ParseStringArray(a, "Categories"),
                                                Tags = ParseStringArray(a, "Tags"),
                                                SeriesId = GetStringOrNumber(a, "SeriesID", "seriesid"),
                                                ProgramId = GetStringOrNumber(a, "ProgramID", "programid")
                                            });
                                        }
                                    }
                                }
                            }

                            if (airings.Count == 0) 
                            {
                                airings.Add(new Airing
                                {
                                    ChannelNumber = channelNumber,
                                    Title = "To Be Announced",
                                    StartTime = gridStart,
                                    Duration = durationHours * 3600,
                                    CategoryColor = "Transparent",
                                    ImageUrl = logoUrl
                                });
                            }

                            if (activeCollection != null && !string.IsNullOrEmpty(activeCollection.Id))
                            {
                                bool isExcluded = activeCollection.ExcludedSources.Any(ex => 
                                    (!string.IsNullOrEmpty(currentDeviceId) && currentDeviceId.Equals(ex, StringComparison.OrdinalIgnoreCase)) ||
                                    (!string.IsNullOrEmpty(channelId) && channelId.Contains(ex, StringComparison.OrdinalIgnoreCase)) || 
                                    (!string.IsNullOrEmpty(channelNumber) && channelNumber.Contains(ex, StringComparison.OrdinalIgnoreCase)));
                                
                                if (isExcluded) continue;

                                bool inCollection = false;
                                
                                for (int i = 0; i < activeCollection.Channels.Count; i++)
                                {
                                    string colChannel = activeCollection.Channels[i].Trim();
                                    if (string.Equals(colChannel, channelId, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(colChannel, channelNumber, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(colChannel, name, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(colChannel, callSign, StringComparison.OrdinalIgnoreCase) ||
                                        (double.TryParse(colChannel, out double dc) && double.TryParse(channelNumber, out double did) && dc == did))
                                    {
                                        inCollection = true;
                                        sortIndex = i; 
                                        break;
                                    }
                                }

                                if (!inCollection)
                                {
                                    var currentAiring = airings.FirstOrDefault(a => a.IsAiringNow) ?? airings[0];
                                    string searchBlock = $"{currentAiring.Title} {currentAiring.EpisodeTitle} {currentAiring.DisplaySummary}".ToLower();
                                    
                                    if (activeCollection.Keywords.Any(k => searchBlock.Contains(k.ToLower()))) 
                                    {
                                        inCollection = true;
                                    }
                                    else if (activeCollection.Genres.Any(g => currentAiring.Genres.Contains(g, StringComparer.OrdinalIgnoreCase))) 
                                    {
                                        inCollection = true;
                                    }
                                    else if (activeCollection.Categories.Any(c => currentAiring.Categories.Contains(c, StringComparer.OrdinalIgnoreCase))) 
                                    {
                                        inCollection = true;
                                    }
                                }

                                if (!inCollection) continue;
                            }

                            for (int i = 0; i < airings.Count; i++) 
                            {
                                airings[i].LeftOffset = (i == 0) ? (airings[i].StartTime - gridStart).TotalMinutes * 8.0 : 0;
                            }

                            bool hdStatus = hdDictionary.TryGetValue(channelNumber, out bool isHd) && isHd;

                            resultChannels.Add(new Channel
                            {
                                Id = channelId,
                                Number = channelNumber,
                                Name = name,
                                ImageUrl = logoUrl,
                                Favorite = isFavorite,
                                Hidden = isHidden,
                                DeviceId = currentDeviceId, 
                                StationId = stationId, 
                                CurrentAirings = airings,
                                IsHD = hdStatus 
                            });
                            
                            collectionSortOrder[channelNumber] = sortIndex;
                        }
                    }
                }
            }

            var stackedChannels = StackAndFilterChannels(resultChannels, devicePriority);
            var uniqueChannels = stackedChannels.GroupBy(c => c.Number).Select(g => g.First());
            return uniqueChannels.OrderBy(c => collectionSortOrder.TryGetValue(c.Number, out int idx) && idx != 999999 ? idx : (double.TryParse(c.Number, out double num) ? num : 999999)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch guide: {ex.Message}");
            return new List<Channel>();
        }
    }
	
    public async Task<bool> SendFileAdminCommandAsync(string baseUrl, string fileId, string command)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            string requestUri = $"{baseUrl}/dvr/files/{fileId}/{command}";
            var response = await client.PutAsync(requestUri, null);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string> GetMediaInfoAsync(string baseUrl, string fileId)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            string requestUri = $"{baseUrl}/dvr/files/{fileId}/mediainfo.json";
            return await client.GetStringAsync(requestUri);
        }
        catch { return string.Empty; }
    }
    
    public async Task<List<MediaItem>> GetFilteredMoviesAsync(int startIndex, int chunkSize, string searchQuery, string genreFilter, string sortOrder, string statusFilter = "All Movies")
    {
        await EnsureMoviesCacheAsync();
        if (_masterMoviesCache == null) return new List<MediaItem>();

        var query = _masterMoviesCache.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var searchLower = searchQuery.ToLower();
            query = query.Where(m => 
                m.Title.ToLower().Contains(searchLower) ||
                m.Cast.Any(c => c.ToLower().Contains(searchLower)) ||
                m.Directors.Any(d => d.ToLower().Contains(searchLower))
            );
        }

        if (!string.IsNullOrWhiteSpace(genreFilter) && genreFilter != "All")
            query = query.Where(m => m.Genres.Any(g => string.Equals(g, genreFilter, StringComparison.OrdinalIgnoreCase)));

        if (statusFilter == "Favorites") query = query.Where(m => m.IsFavorite);
        else if (statusFilter == "Watched") query = query.Where(m => m.IsWatched);
        else if (statusFilter == "Unwatched") query = query.Where(m => !m.IsWatched);

        query = sortOrder switch
        {
            "Alphabetical (A-Z)" => query.OrderBy(m => m.Title),
            "Alphabetical (Z-A)" => query.OrderByDescending(m => m.Title),
            "Release Year (Newest)" => query.OrderByDescending(m => m.ReleaseYear),
            "Release Year (Oldest)" => query.OrderBy(m => m.ReleaseYear),
            _ => query.OrderByDescending(m => m.CreatedAt) 
        };

        return query.Skip(startIndex).Take(chunkSize).ToList();
    }

    public void ClearMoviesCache() => _masterMoviesCache = null;
    
    private List<MediaItem>? _masterEpisodesCache = null;

    private async Task EnsureEpisodesCacheAsync()
    {
        if (_masterEpisodesCache != null) return;

        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return;
        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/episodes";

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            _masterEpisodesCache = new List<MediaItem>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string id = GetStringOrNumber(element, "id");
                    if (string.IsNullOrEmpty(id)) continue;

                    string showTitle = GetStringOrNumber(element, "title", "name", "show_title");
                    string episodeTitle = GetStringOrNumber(element, "episode_title");
                    string summary = GetStringOrNumber(element, "summary", "full_summary");
                    
                    string posterUrl = GetBestImageUrl(baseUrl, element, id);

                    int season = element.TryGetProperty("season_number", out var sProp) && sProp.ValueKind == JsonValueKind.Number ? sProp.GetInt32() : 0;
                    int episode = element.TryGetProperty("episode_number", out var eProp) && eProp.ValueKind == JsonValueKind.Number ? eProp.GetInt32() : 0;
                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt64() : 0;

                    bool isWatched = false;
                    if (element.TryGetProperty("watched", out var w1) || element.TryGetProperty("Watched", out w1))
                        isWatched = w1.ValueKind == JsonValueKind.True || (w1.ValueKind == JsonValueKind.Number && w1.GetInt32() == 1);

                    bool isFavorite = false;
                    if (element.TryGetProperty("favorited", out var f1) || element.TryGetProperty("Favorited", out f1))
                        isFavorite = f1.ValueKind == JsonValueKind.True || (f1.ValueKind == JsonValueKind.Number && f1.GetInt32() == 1);
                    else if (element.TryGetProperty("favorited_at", out var f2) || element.TryGetProperty("FavoritedAt", out f2))
                        isFavorite = f2.ValueKind == JsonValueKind.Number && f2.GetInt64() > 0;

                    string videoUrl = GetStringOrNumber(element, "VideoURL", "video_url");

                    _masterEpisodesCache.Add(new MediaItem
                    { 
					    Path = GetStringOrNumber(element, "Path", "path"),
                        Id = id,
						SubtitleUrl = $"{baseUrl}/dvr/files/{id}/subtitles.vtt",
                        Title = string.IsNullOrEmpty(showTitle) ? "Unknown Show" : showTitle,
                        CurrentShowTitle = episodeTitle,
                        PosterUrl = posterUrl,
                        StreamUrl = !string.IsNullOrEmpty(videoUrl) ? videoUrl : $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        Summary = summary,
                        SeasonNumber = season,
                        EpisodeNumber = episode,
                        CreatedAt = createdAt,
                        Genres = ParseStringArray(element, "genres"),
                        IsWatched = isWatched,
                        IsFavorite = isFavorite,
						Commercials = ParseDoubleArray(element, "commercials")
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch master episodes list: {ex.Message}");
            _masterEpisodesCache = new List<MediaItem>();
        }
    }

    public async Task<List<MediaItem>> GetFilteredShowsAsync(int startIndex, int chunkSize, string searchQuery, string sortOrder)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/shows?sort=date_added&dir=desc"; 
        
        var showsList = new List<MediaItem>();

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string id = GetStringOrNumber(element, "id");
                    string title = GetStringOrNumber(element, "title", "name");
                    string summary = GetStringOrNumber(element, "summary", "full_summary");
                    
                    string posterUrl = GetBestImageUrl(baseUrl, element);

                    long createdAt = 0;
                    if (element.TryGetProperty("last_recorded_at", out var lProp) && lProp.ValueKind == JsonValueKind.Number) 
                        createdAt = lProp.GetInt64();
                    else if (element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number) 
                        createdAt = cProp.GetInt64();

                    showsList.Add(new MediaItem
                    {   
                        Path = GetStringOrNumber(element, "Path", "path"),
                        Id = id,
                        Title = string.IsNullOrEmpty(title) ? "Unknown Show" : title,
                        Summary = summary,
                        PosterUrl = posterUrl,
                        CreatedAt = createdAt
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch shows from API: {ex.Message}");
        }

        var showsQuery = showsList.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var searchLower = searchQuery.ToLower();
            showsQuery = showsQuery.Where(s => s.Title.ToLower().Contains(searchLower));
        }

        string StripArticles(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";
            string lower = title.ToLower();
            if (lower.StartsWith("the ")) return title.Substring(4);
            if (lower.StartsWith("a ")) return title.Substring(2);
            if (lower.StartsWith("an ")) return title.Substring(3);
            return title;
        }

        showsQuery = sortOrder switch
        {
            "Alphabetical (A-Z)" => showsQuery.OrderBy(s => StripArticles(s.Title)),
            "Alphabetical (Z-A)" => showsQuery.OrderByDescending(s => StripArticles(s.Title)),
            _ => showsQuery.OrderByDescending(s => s.CreatedAt)
        };

        return showsQuery.Skip(startIndex).Take(chunkSize).ToList();
    }

    public async Task<List<MediaItem>> GetEpisodesForShowAsync(string showTitle)
    {
        await EnsureEpisodesCacheAsync();
        if (_masterEpisodesCache == null) return new List<MediaItem>();

        string Normalize(string input) => input.Replace("&", "and").Replace(":", "").Replace("-", "").ToLower().Trim();
        string normalizedTarget = Normalize(showTitle);

        return _masterEpisodesCache
            .Where(e => 
            {
                string normalizedEp = Normalize(e.Title);
                return normalizedEp == normalizedTarget || 
                       normalizedTarget.StartsWith(normalizedEp) || 
                       normalizedEp.StartsWith(normalizedTarget);
            })
            .OrderBy(e => e.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();
    }

    public async Task<List<MediaItem>> GetVideoGroupsAsync()
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();
        
        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/video_groups";
        var groups = new List<MediaItem>();

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string name = GetStringOrNumber(element, "name");
                    string id = GetStringOrNumber(element, "id");
                    
                    string posterUrl = GetBestImageUrl(baseUrl, element);

                    groups.Add(new MediaItem
                    {
                        Path = GetStringOrNumber(element, "Path", "path"),
						Id = id,
                        Title = string.IsNullOrEmpty(name) ? "Unknown Folder" : name,
                        PosterUrl = posterUrl
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch video groups: {ex.Message}");
        }
        return groups;
    }

    public async Task<List<MediaItem>> GetVideosInGroupAsync(string groupId)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/videos";
        var videos = new List<MediaItem>();

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string elementGroupId = GetStringOrNumber(element, "video_group_id", "group_id");
                    if (elementGroupId != groupId) continue;

                    string title = GetStringOrNumber(element, "title", "name");
                    string id = GetStringOrNumber(element, "id");
                    
                    string posterUrl = GetBestImageUrl(baseUrl, element, id);

                    bool isWatched = false;
                    if (element.TryGetProperty("watched", out var w1) || element.TryGetProperty("Watched", out w1))
                        isWatched = w1.ValueKind == JsonValueKind.True || (w1.ValueKind == JsonValueKind.Number && w1.GetInt32() == 1);

                    bool isFavorite = false;
                    if (element.TryGetProperty("favorited", out var f1) || element.TryGetProperty("Favorited", out f1))
                        isFavorite = f1.ValueKind == JsonValueKind.True || (f1.ValueKind == JsonValueKind.Number && f1.GetInt32() == 1);
                    else if (element.TryGetProperty("favorited_at", out var f2) || element.TryGetProperty("FavoritedAt", out f2))
                        isFavorite = f2.ValueKind == JsonValueKind.Number && f2.GetInt64() > 0;

                    string videoUrl = GetStringOrNumber(element, "VideoURL", "video_url");

                    videos.Add(new MediaItem
                    {
                        Path = GetStringOrNumber(element, "Path", "path"),
						Id = id,
						SubtitleUrl = $"{baseUrl}/dvr/files/{id}/subtitles.vtt",
                        Title = string.IsNullOrEmpty(title) ? "Unknown Video" : title,
                        PosterUrl = posterUrl,
                        StreamUrl = !string.IsNullOrEmpty(videoUrl) ? videoUrl : $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        IsWatched = isWatched,
                        IsFavorite = isFavorite,
						Commercials = ParseDoubleArray(element, "commercials")
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch videos in group {groupId}: {ex.Message}");
        }
        return videos;
    }
    
    private string DetermineColor(List<string> tags)
    {
        var combined = string.Join(" ", tags).ToLower();
        if (combined.Contains("sports") || combined.Contains("event") || combined.Contains("athletics")) return "#E87C00"; 
        if (combined.Contains("news") || combined.Contains("local")) return "#107C10"; 
        if (combined.Contains("movie") || combined.Contains("film") || combined.Contains("cinema")) return "#9300BA"; 
        if (combined.Contains("kids") || combined.Contains("children") || combined.Contains("animation")) return "#00A4EF"; 
        return "Transparent"; 
    }

    public async Task<IEnumerable<MediaItem>> GetFeaturedMoviesAsync(int limit = 10)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/movies?sort=createdAt&dir=desc"; 

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            var movies = new List<MediaItem>();
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string id = GetStringOrNumber(element, "id");
                    if (string.IsNullOrEmpty(id)) continue;

                    string title = GetStringOrNumber(element, "title");
                    string posterUrl = GetBestImageUrl(baseUrl, element, id);

                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt64() : 0;
                    string videoUrl = GetStringOrNumber(element, "VideoURL", "video_url");

                    movies.Add(new MediaItem
                    {
                        Path = GetStringOrNumber(element, "Path", "path"),
                        Id = id,
						SubtitleUrl = $"{baseUrl}/dvr/files/{id}/subtitles.vtt",
                        Title = string.IsNullOrEmpty(title) ? "Unknown" : title,
                        PosterUrl = posterUrl,
                        StreamUrl = !string.IsNullOrEmpty(videoUrl) ? videoUrl : $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        CreatedAt = createdAt,
                        Commercials = ParseDoubleArray(element, "commercials")
                    });
                }
            }
            
            return movies.OrderByDescending(m => m.CreatedAt).Take(limit).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch movies: {ex.Message}");
            return new List<MediaItem>();
        }
    }

    public async Task<IEnumerable<MediaItem>> GetRecentEpisodesAsync(int limit = 10)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/episodes?sort=createdAt&dir=desc"; 

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            var episodes = new List<MediaItem>();
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string id = GetStringOrNumber(element, "id");
                    if (string.IsNullOrEmpty(id)) continue;

                    string showTitle = GetStringOrNumber(element, "title");
                    string episodeTitle = GetStringOrNumber(element, "episode_title");
                    
                    string posterUrl = GetBestImageUrl(baseUrl, element, id);

                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt64() : 0;
                    string videoUrl = GetStringOrNumber(element, "VideoURL", "video_url");

                    episodes.Add(new MediaItem
                    {
                        Path = GetStringOrNumber(element, "Path", "path"),
                        Id = id,
						SubtitleUrl = $"{baseUrl}/dvr/files/{id}/subtitles.vtt",
                        Title = showTitle,
                        CurrentShowTitle = episodeTitle, 
                        PosterUrl = posterUrl,
                        StreamUrl = !string.IsNullOrEmpty(videoUrl) ? videoUrl : $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        CreatedAt = createdAt,
                        Commercials = ParseDoubleArray(element, "commercials")
                    });
                }
            }
            
            return episodes.OrderByDescending(e => e.CreatedAt).Take(limit).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch episodes: {ex.Message}");
            return new List<MediaItem>();
        }
    }
	
    public async Task<List<MediaItem>> GetAllRecordingsAsync()
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/all?source=recordings&sort=date_added&order=desc";
        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            var recordings = new List<MediaItem>();
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string id = GetStringOrNumber(element, "id");
                    if (string.IsNullOrEmpty(id)) continue;

                    string showTitle = GetStringOrNumber(element, "title");
                    string episodeTitle = GetStringOrNumber(element, "episode_title");
                    
                    string posterUrl = GetBestImageUrl(baseUrl, element, id);

					string summary = GetStringOrNumber(element, "summary");
                    if (string.IsNullOrEmpty(summary)) summary = GetStringOrNumber(element, "full_summary");
                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == System.Text.Json.JsonValueKind.Number ? cProp.GetInt64() : 0;
                    
                    bool isCompleted = true;
                    if (element.TryGetProperty("completed", out var compProp) || element.TryGetProperty("Completed", out compProp))
                    {
                        isCompleted = compProp.ValueKind == JsonValueKind.True || (compProp.ValueKind == JsonValueKind.Number && compProp.GetInt32() == 1);
                    }

                    string streamUrl = isCompleted 
                        ? $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts" 
                        : $"{baseUrl}/dvr/files/{id}/hls/master.m3u8";

                   double playbackTime = 0;
                    if (element.TryGetProperty("playback_time", out var pbProp) && pbProp.ValueKind == JsonValueKind.Number)
                    {
                        playbackTime = pbProp.GetDouble();
                    }

                    var categories = new List<string>(); 
                    if (element.TryGetProperty("categories", out var catProp) && catProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cat in catProp.EnumerateArray())
                        {
                            if (cat.ValueKind == JsonValueKind.String)
                                categories.Add(cat.GetString() ?? "");
                        }
                    }

                    string channelId = GetStringOrNumber(element, "channel");
                    bool isImported = string.IsNullOrEmpty(channelId);

                    recordings.Add(new MediaItem
                    {
                        Path = GetStringOrNumber(element, "Path", "path"),
                        Id = id,
                        SubtitleUrl = $"{baseUrl}/dvr/files/{id}/subtitles.vtt",
                        Title = showTitle,
                        CurrentShowTitle = episodeTitle, 
                        Summary = summary, 
                        PosterUrl = posterUrl,
                        StreamUrl = streamUrl,
                        CreatedAt = createdAt,
                        IsCompleted = isCompleted,
                        StartOffset = playbackTime,
                        Commercials = ParseDoubleArray(element, "commercials"),
                        Categories = categories,
                        IsImported = isImported
                    });
                }
            }
            
            return recordings;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch unified recordings: {ex.Message}");
            return new List<MediaItem>();
        }
    }
	
	public async Task<List<MediaItem>> GetScheduledRecordingsAsync()
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/dvr/jobs"; 

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            var jobs = new List<MediaItem>();
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string id = GetStringOrNumber(element, "ID", "id");
                    if (string.IsNullOrEmpty(id)) continue;

                    string showTitle = GetStringOrNumber(element, "Name");
                    string episodeTitle = "";
                    string summary = "";
                    string posterUrl = "";

                    if (element.TryGetProperty("Airing", out var airingProp))
                    {
                        episodeTitle = GetStringOrNumber(airingProp, "EpisodeTitle");
                        summary = GetStringOrNumber(airingProp, "Summary");
                        if (string.IsNullOrEmpty(showTitle)) showTitle = GetStringOrNumber(airingProp, "Title");
                        
                        posterUrl = GetBestImageUrl(baseUrl, airingProp);
                    }

                    long scheduledTime = element.TryGetProperty("Time", out var tProp) && tProp.ValueKind == JsonValueKind.Number ? tProp.GetInt64() : 0;
                    DateTime scheduledDate = DateTimeOffset.FromUnixTimeSeconds(scheduledTime).LocalDateTime;

                    jobs.Add(new MediaItem
                    {
                        Id = id,
                        Title = showTitle,
                        CurrentShowTitle = episodeTitle, 
                        Summary = summary,
                        PosterUrl = posterUrl,
                        CreatedAt = scheduledTime,
                        IsScheduled = true,
                        IsCompleted = false,
                        DisplayTime = scheduledDate.ToString("MMM d 'at' h:mm tt")
                    });
                }
            }
            return jobs;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch scheduled jobs: {ex.Message}");
            return new List<MediaItem>();
        }
    }
	
    public async Task<List<MediaItem>> GetImportedMediaAsync()
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/dvr/files"; 

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
            var imports = new List<MediaItem>();
            
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string source = "";
                    string channel = "";
                    string title = "";
                    string episodeTitle = "";
                    string posterUrl = "";
                    string summary = ""; 
                    var categories = new List<string>();

                    if (element.TryGetProperty("Airing", out var airing))
                    {
                        source = GetStringOrNumber(airing, "Source").ToLower();
                        channel = GetStringOrNumber(airing, "Channel");
                        title = GetStringOrNumber(airing, "Title");
                        episodeTitle = GetStringOrNumber(airing, "EpisodeTitle");
                        summary = GetStringOrNumber(airing, "Summary");
                        
                        posterUrl = GetBestImageUrl(baseUrl, airing);
                        
                        if (airing.TryGetProperty("Categories", out var catProp) && catProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var cat in catProp.EnumerateArray())
                            {
                                if (cat.ValueKind == System.Text.Json.JsonValueKind.String)
                                    categories.Add(cat.GetString() ?? "");
                            }
                        }
                    }

                   if (source.Contains("imported") || source.Contains("playon") || source.Contains("virtual") || source.Contains("strmlnk") || string.IsNullOrEmpty(channel))
                    {
                        if (element.TryGetProperty("Duration", out var durProp) && durProp.GetDouble() == 0) continue;

                        string id = GetStringOrNumber(element, "ID");
                        if (string.IsNullOrEmpty(id)) continue;
                        
                        // Fallback to root element if Airing node lacked a valid image
                        if (string.IsNullOrEmpty(posterUrl)) posterUrl = GetBestImageUrl(baseUrl, element, id);
                        
                        if (string.IsNullOrEmpty(title)) title = GetStringOrNumber(element, "Title");
                        if (string.IsNullOrEmpty(summary)) summary = GetStringOrNumber(element, "summary");

                        long createdAt = 0;
                        if (element.TryGetProperty("CreatedAt", out var cProp) && cProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                        {
                            createdAt = cProp.GetInt64();
                            if (createdAt > 9999999999) createdAt /= 1000; 
                        }

                        bool isCompleted = true;
                        if (element.TryGetProperty("Completed", out var compProp))
                        {
                            isCompleted = compProp.ValueKind == System.Text.Json.JsonValueKind.True || (compProp.ValueKind == System.Text.Json.JsonValueKind.Number && compProp.GetInt32() == 1);
                        }

                        imports.Add(new MediaItem
                        {
                            Path = GetStringOrNumber(element, "Path"),
                            Id = id,
                            SubtitleUrl = $"{baseUrl}/dvr/files/{id}/subtitles.vtt",
                            Title = title,
                            CurrentShowTitle = episodeTitle, 
                            Summary = summary, 
                            PosterUrl = posterUrl,
                            StreamUrl = $"{baseUrl}/dvr/files/{id}/hls/master.m3u8",
                            IsCompleted = isCompleted,
                            Categories = categories,
                            CreatedAt = createdAt,
                            IsImported = true
                        });
                    }
                }
            }
            
            return imports.OrderByDescending(i => i.CreatedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch imported media: {ex.Message}");
            return new List<MediaItem>();
        }
    }
	
	public async Task<bool> DeleteRecordingAsync(string fileId)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null || string.IsNullOrEmpty(fileId)) return false;

        string apiUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}/dvr/files/{fileId}"; 

        try
        {
            var response = await _httpClient.DeleteAsync(apiUrl);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete recording {fileId}: {ex.Message}");
            return false;
        }
    }
	
	public async Task<List<MediaItem>> GetUpNextAsync()
    {
        var items = new List<MediaItem>();
        var server = _serverManager.GetActiveServer();
        if (server == null) return items;

        string baseUrl = $"http://{server.IpAddress}:{server.Port}";
        string url = $"{baseUrl}/dvr/recordings/upnext";

        try
        {
            var response = await _httpClient.GetStringAsync(url);
            using (JsonDocument doc = JsonDocument.Parse(response))
            {
                foreach (JsonElement element in doc.RootElement.EnumerateArray())
                {
                    string id = element.GetProperty("ID").GetString() ?? "";
                    string title = "Unknown";
                    string posterUrl = "";
                    
                    if (element.TryGetProperty("Airing", out JsonElement airing))
                    {
                        title = airing.TryGetProperty("Title", out var t) ? t.GetString() ?? "" : "";
                        if (airing.TryGetProperty("EpisodeTitle", out var epTitle))
                        {
                            title += $" - {epTitle.GetString()}"; 
                        }
                        
                        posterUrl = GetBestImageUrl(baseUrl, airing, id);
                    }

                    double playbackTime = 0;
                    if (element.TryGetProperty("PlaybackTime", out JsonElement pbElement))
                    {
                        playbackTime = pbElement.GetDouble();
                    }
                    
                    string videoUrl = element.TryGetProperty("VideoURL", out var vUrl) ? vUrl.GetString() ?? "" : "";

                    items.Add(new MediaItem
                    {
                        Path = GetStringOrNumber(element, "Path", "path"),
						Id = id,
						SubtitleUrl = $"{baseUrl}/dvr/files/{id}/subtitles.vtt",
                        Title = title,
                        PosterUrl = posterUrl,
                        StreamUrl = !string.IsNullOrEmpty(videoUrl) ? videoUrl : $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        Commercials = ParseDoubleArray(element, "commercials"),
                        StartOffset = playbackTime 
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fetching Up Next: {ex.Message}");
        }

        return items;
    }

    public async Task<IEnumerable<MediaItem>> GetRecentVideosAsync(int limit = 10)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/videos?sort=createdAt&dir=desc"; 

        try
        {
            string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            var videos = new List<MediaItem>();
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string id = GetStringOrNumber(element, "id");
                    if (string.IsNullOrEmpty(id)) continue;

                    string groupTitle = GetStringOrNumber(element, "title");
                    string videoTitle = GetStringOrNumber(element, "video_title");
                    
                    string posterUrl = GetBestImageUrl(baseUrl, element, id);

                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt64() : 0;
                    
                    bool isWatched = false;
                    if (element.TryGetProperty("watched", out var w1) || element.TryGetProperty("Watched", out w1))
                        isWatched = w1.ValueKind == JsonValueKind.True || (w1.ValueKind == JsonValueKind.Number && w1.GetInt32() == 1);

                    bool isFavorite = false;
                    if (element.TryGetProperty("favorited", out var f1) || element.TryGetProperty("Favorited", out f1))
                        isFavorite = f1.ValueKind == JsonValueKind.True || (f1.ValueKind == JsonValueKind.Number && f1.GetInt32() == 1);
                    else if (element.TryGetProperty("favorited_at", out var f2) || element.TryGetProperty("FavoritedAt", out f2))
                        isFavorite = f2.ValueKind == JsonValueKind.Number && f2.GetInt64() > 0;

                    string videoUrl = GetStringOrNumber(element, "VideoURL", "video_url");

                    videos.Add(new MediaItem
                    {
                        Path = GetStringOrNumber(element, "Path", "path"),
                        Id = id,
						SubtitleUrl = $"{baseUrl}/dvr/files/{id}/subtitles.vtt",
                        Title = groupTitle,
                        CurrentShowTitle = videoTitle,
                        PosterUrl = posterUrl,
                        StreamUrl = !string.IsNullOrEmpty(videoUrl) ? videoUrl : $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        CreatedAt = createdAt,
                        IsWatched = isWatched,
                        IsFavorite = isFavorite,
                        Commercials = ParseDoubleArray(element, "commercials")
                    });
                }
            }
            
            return videos.OrderByDescending(v => v.CreatedAt).Take(limit).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch videos: {ex.Message}");
            return new List<MediaItem>();
        }
    }
    
    public async Task<List<ChannelCollection>> GetCollectionsAsync()
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<ChannelCollection>();

        string url = $"http://{activeServer.IpAddress}:{activeServer.Port}/dvr/collections/channels";
        try
        {
            string stringJson = await _httpClient.GetStringAsync(url);
            using JsonDocument doc = JsonDocument.Parse(stringJson);
            var collections = new List<ChannelCollection>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string name = GetStringOrNumber(element, "name");
                    
                    var col = new ChannelCollection
                    {
                        Id = GetStringOrNumber(element, "slug"),
                        Name = string.IsNullOrEmpty(name) ? "Unknown Collection" : name,
                        Channels = ParseStringArray(element, "items"),
                        Genres = ParseStringArray(element, "genres"),
                        Categories = ParseStringArray(element, "categories"),
                        Tags = ParseStringArray(element, "tags"),
                        Keywords = ParseStringArray(element, "keywords"),
                        ExcludedSources = ParseStringArray(element, "excluded_sources")
                    };
                    
                    collections.Add(col);
                }
            }
            return collections;
        }
        catch { return new List<ChannelCollection>(); }
    }

    public async Task<IEnumerable<MediaItem>> GetLiveChannelsAsync(ChannelCollection? activeCollection = null)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        
        try
        {
            var guideChannels = await GetGuideChannelsAsync(activeCollection, 4);
            var mediaItems = new List<MediaItem>();

            foreach (var c in guideChannels.Where(ch => ch.Favorite))
            {
                var currentAiring = c.CurrentAirings?.FirstOrDefault(a => a.IsAiringNow) ?? c.CurrentAirings?.FirstOrDefault();
                var mediaItem = CreateLiveMediaItem(baseUrl, c, currentAiring);
                mediaItems.Add(mediaItem);
            }

            return mediaItems;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to map live channels for dashboard: {ex.Message}");
            return new List<MediaItem>();
        }
    }
    
    // --- ARCHITECTURE FIX: CENTRALIZED IMAGE PARSING WITH PRIORITY AND FALLBACK ---
    private string GetBestImageUrl(string baseUrl, JsonElement element, string fallbackId = "")
    {
        // Prioritize full-quality original artwork over compressed square thumbnails
        string[] priorities = { "image_url", "image", "art", "cover_url", "thumbnail_url", "thumbnail", "Image" };
        
        foreach (var key in priorities)
        {
            string raw = GetStringOrNumber(element, key);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                // FormatImageUrl will mathematically return "" if the URL is just an empty root IP
                string formatted = FormatImageUrl(baseUrl, raw);
                if (!string.IsNullOrWhiteSpace(formatted)) return formatted;
            }
        }
        
        // Ultimate Safe Fallback: If all endpoints fail or return empty IPs, build the preview.jpg
        if (!string.IsNullOrWhiteSpace(fallbackId))
            return $"{baseUrl.TrimEnd('/')}/dvr/files/{fallbackId}/preview.jpg";
            
        return "";
    }
    
    private string GetStringOrNumber(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object) return "";
        
        foreach (var name in propertyNames)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.String) 
                    {
                        string val = prop.Value.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                    if (prop.Value.ValueKind == JsonValueKind.Number) 
                    {
                        return prop.Value.ToString() ?? "";
                    }
                }
            }
        }
        return "";
    }
    
    private List<string> ParseStringArray(JsonElement root, params string[] propertyNames)
    {
        var list = new List<string>();
        if (root.ValueKind != JsonValueKind.Object) return list;
        
        foreach (var prop in root.EnumerateObject())
        {
            foreach (var name in propertyNames)
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var val = item.GetString();
                            if (!string.IsNullOrWhiteSpace(val)) list.Add(val.Trim());
                        }
                        else if (item.ValueKind == JsonValueKind.Number)
                        {
                            list.Add(item.ToString() ?? "");
                        }
                    }
                    return list; 
                }
            }
        }
        return list;
    }
	
	private List<double> ParseDoubleArray(JsonElement root, params string[] propertyNames)
    {
        var list = new List<double>();
        if (root.ValueKind != JsonValueKind.Object) return list;
        
        foreach (var prop in root.EnumerateObject())
        {
            foreach (var name in propertyNames)
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Number)
                        {
                            list.Add(item.GetDouble());
                        }
                    }
                    return list; 
                }
            }
        }
        return list;
    }
    
    private string FormatImageUrl(string baseUrl, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return "";

        string cleanPath = imagePath.Trim();

        if (Uri.TryCreate(cleanPath, UriKind.Absolute, out Uri? uriResult))
        {
            if (uriResult.AbsolutePath == "/") return ""; // Traps empty root IPs like http://127.0.0.1:8089
            
            if (uriResult.Host == "127.0.0.1" || uriResult.Host == "localhost")
            {
                return baseUrl.TrimEnd('/') + uriResult.AbsolutePath + uriResult.Query;
            }

            return cleanPath;
        }

        return cleanPath.StartsWith("/") ? baseUrl.TrimEnd('/') + cleanPath : $"{baseUrl.TrimEnd('/')}/{cleanPath}";
    }
    
    public MediaItem CreateLiveMediaItem(string baseUrl, Channel channel, Airing? airing)
    {
        var media = new MediaItem
        {
            Id = channel.Number ?? "0",
            Title = string.IsNullOrEmpty(channel.Name) ? $"Channel {channel.Number}" : channel.Name,
            CurrentShowTitle = airing?.DisplayTitle ?? "Live TV",
            PosterUrl = channel.ImageUrl ?? "",
            CurrentShowPosterUrl = airing?.ImageUrl ?? channel.ImageUrl ?? "",
            
            IsLiveTv = true,
            StartTime = airing?.StartTime ?? DateTime.Now,
            EndTime = (airing != null && airing.Duration.HasValue) 
                        ? airing.StartTime.AddSeconds(Convert.ToDouble(airing.Duration.Value)) 
                        : DateTime.Now.AddHours(1)
        };

        bool isVirtualChannel = channel.Id != null && channel.Id.StartsWith("virtual", StringComparison.OrdinalIgnoreCase);

        if (isVirtualChannel && airing != null && !string.IsNullOrWhiteSpace(airing.Source))
        {
            string fileId = airing.Source.Split('/').Last();
            media.StreamUrl = $"{baseUrl.TrimEnd('/')}/dvr/files/{fileId}/stream.mpg?format=ts";
            
            var airStart = airing.StartTime; 
            if (airStart != DateTime.MinValue)
            {
                int offset = (int)(DateTime.Now - airStart).TotalSeconds;
                media.StartOffset = offset > 0 ? offset : 0;
            }
        }
        else
        {
            media.StreamUrl = $"{baseUrl.TrimEnd('/')}/devices/ANY/channels/{channel.Number}/hls/master.m3u8";
        }

        return media;
    }
	
	public async Task<MediaItem> ResolveStreamLinkAsync(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Path)) return item;
        if (!item.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase) && 
            !item.Path.EndsWith(".strmlnk", StringComparison.OrdinalIgnoreCase)) return item;

        var server = _serverManager.GetActiveServer();
        if (server == null) return item;

        try 
        {
            string url = $"http://{server.IpAddress}:{server.Port}/dvr/files/{item.Id}";
            string json = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            
            var streamLinks = ParseStringArray(doc.RootElement, "StreamLinks", "stream_links");
            string videoUrl = GetStringOrNumber(doc.RootElement, "VideoURL", "video_url");

            if (streamLinks.Count > 0) 
            {
                item.StreamUrl = streamLinks[0];
                item.RequiresBrowser = true;
            } 
            else if (!string.IsNullOrWhiteSpace(videoUrl)) 
            {
                item.StreamUrl = videoUrl;
                item.RequiresBrowser = false;
            }
        } 
        catch { }
        
        return item;
    }
}