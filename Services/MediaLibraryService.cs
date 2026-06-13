using System;
using System.Collections.Generic;
using System.Net.Http;
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

    // 1. Ensures the master list is downloaded into memory ONCE
    private async Task EnsureMoviesCacheAsync()
    {
        if (_masterMoviesCache != null) return;

        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return;
        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/movies"; // Grab everything

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
                    string rawImageUrl = GetStringOrNumber(element, "image_url", "thumbnail_url");
                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt64() : 0;
                    int year = element.TryGetProperty("release_year", out var yProp) && yProp.ValueKind == JsonValueKind.Number ? yProp.GetInt32() : 0;

                    _masterMoviesCache.Add(new MediaItem
                    {
                        Id = id,
                        Title = string.IsNullOrEmpty(title) ? "Unknown Movie" : title,
                        PosterUrl = FormatImageUrl(baseUrl, rawImageUrl),
                        StreamUrl = $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        CreatedAt = createdAt,
                        ReleaseYear = year,
                        Genres = ParseStringArray(element, "genres"),
                        Cast = ParseStringArray(element, "cast"),
                        Directors = ParseStringArray(element, "directors")
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
	
	public async Task<bool> CreateRecordingJobAsync(string baseUrl, string channelNumber, Airing airing, int padStartSeconds = 0, int padEndSeconds = 0)
    {
        if (airing == null || string.IsNullOrWhiteSpace(channelNumber)) return false;

        try
        {
            string url = $"{baseUrl.TrimEnd('/')}/dvr/jobs/new";
            long startTimeEpoch = new DateTimeOffset(airing.StartTime).ToUnixTimeSeconds();
            int durationSeconds = (int)(airing.Duration ?? 3600);
            
            // Adjust the actual Job boundaries based on padding offsets
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

    public MediaLibraryService(ServerManagerService serverManager, HttpClient httpClient, ILogger<MediaLibraryService> logger)
    {
        _serverManager = serverManager;
        _httpClient = httpClient;
        _logger = logger;
    }
	
	public async Task<List<Channel>> GetGuideChannelsAsync(ChannelCollection? activeCollection = null, int durationHours = 4)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<Channel>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        
        try
        {
            var resultChannels = new List<Channel>();
            var collectionSortOrder = new Dictionary<string, int>(); 

            // 1. Fetch Master Channels & EPG
            string channelsJson = await _httpClient.GetStringAsync($"{baseUrl}/devices/ANY/channels");
            long unixTime = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
            long durationSeconds = durationHours * 3600; 
            string guideJson = await _httpClient.GetStringAsync($"{baseUrl}/devices/ANY/guide?time={unixTime}&duration={durationSeconds}");

            // --- NEW: Fetch Stations for Logo Fallbacks (Feral Logic) ---
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
            catch { /* Ignore station fetch failures */ }

            using JsonDocument channelsDoc = JsonDocument.Parse(channelsJson);
            using JsonDocument guideDoc = JsonDocument.Parse(guideJson);
            
            // 2. Parse EPG into a Dictionary
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

            // 3. Build and Filter Channels
            if (channelsDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var channelProp in channelsDoc.RootElement.EnumerateArray())
                {
                    string channelNumber = GetStringOrNumber(channelProp, "guidenumber", "number");
                    string channelId = GetStringOrNumber(channelProp, "id", "channelid");
                    string name = GetStringOrNumber(channelProp, "guidename", "name");
                    string callSign = GetStringOrNumber(channelProp, "callsign", "station", "tmsid");
                    string stationId = GetStringOrNumber(channelProp, "stationId", "station"); 
                    string logoUrl = GetStringOrNumber(channelProp, "image", "logo", "art", "thumbnail");

                    if (string.IsNullOrWhiteSpace(channelNumber)) continue;

                    // --- NEW: Feral Fallback 1 - Stations Dictionary ---
                    string targetId = !string.IsNullOrWhiteSpace(stationId) ? stationId : callSign;
                    if (string.IsNullOrWhiteSpace(logoUrl) && !string.IsNullOrWhiteSpace(targetId))
                    {
                        if (stationLogoDict.TryGetValue(targetId, out string? mappedLogo)) logoUrl = mappedLogo;
                    }

                    // --- NEW: Feral Fallback 2 - Guide EPG Data ---
                    if (string.IsNullOrWhiteSpace(logoUrl) && guideDict.TryGetValue(channelNumber, out var gDataFallback))
                    {
                        if (gDataFallback.TryGetProperty("Channel", out var gChan))
                        {
                            string gImage = GetStringOrNumber(gChan, "image", "logo", "art");
                            if (!string.IsNullOrWhiteSpace(gImage)) logoUrl = gImage;
                        }
                    }

                    // --- NEW: Feral Fallback 3 - The Gracenote / Local Normalizer ---
                    if (!string.IsNullOrWhiteSpace(logoUrl))
                    {
                        if (logoUrl.StartsWith("tmsimg://", StringComparison.OrdinalIgnoreCase))
                            logoUrl = logoUrl.Replace("tmsimg://", $"{baseUrl}/tmsimg/", StringComparison.OrdinalIgnoreCase);
                        else if (!logoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            logoUrl = $"{baseUrl}/{logoUrl.TrimStart('/')}";
                    }
                    int sortIndex = 999999;
                    bool isFavorite = false;
                    var airings = new List<Airing>();

                    // Map EPG blocks to the channel
                    if (guideDict.TryGetValue(channelNumber, out var guideData))
                    {
                        if (guideData.TryGetProperty("Channel", out var gChan) && gChan.TryGetProperty("Favorite", out var favProp))
                            isFavorite = (favProp.ValueKind == JsonValueKind.True || (favProp.ValueKind == JsonValueKind.Number && favProp.GetInt32() == 1));

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
                                        // THE FIX: Capture the raw file source link for virtual timelines
                                        Source = GetStringOrNumber(a, "Source", "source"), 
                                        CategoryColor = DetermineColor(ParseStringArray(a, "Categories", "Genres")),
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
                            Duration = durationHours * 3600, // Fill the timeline block so the Guide doesn't crash
                            CategoryColor = "Transparent",
                            ImageUrl = logoUrl
                        });
                    }

                    // Collection Matcher
                    if (activeCollection != null && !string.IsNullOrEmpty(activeCollection.Id))
                    {
                        bool isExcluded = activeCollection.ExcludedSources.Any(ex => 
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
                            
                            if (activeCollection.Keywords.Any(k => searchBlock.Contains(k.ToLower()))) inCollection = true;
                        }

                        if (!inCollection) continue;
                    }

                    // Calculate UI Offsets for Timeline
                    for (int i = 0; i < airings.Count; i++) 
                    {
                        airings[i].LeftOffset = (i == 0) ? (airings[i].StartTime - gridStart).TotalMinutes * 8.0 : 0;
                    }

                    resultChannels.Add(new Channel
                    {
                        Id = channelId,
                        Number = channelNumber,
                        Name = name,
                        ImageUrl = logoUrl,
                        Favorite = isFavorite,
                        CurrentAirings = airings
                    });
                    
                    collectionSortOrder[channelNumber] = sortIndex;
                }
            }

            var uniqueChannels = resultChannels.GroupBy(c => c.Number).Select(g => g.First());
            return uniqueChannels.OrderBy(c => collectionSortOrder.TryGetValue(c.Number, out int idx) && idx != 999999 ? idx : (double.TryParse(c.Number, out double num) ? num : 999999)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch guide: {ex.Message}");
            return new List<Channel>();
        }
    }
	
	public async Task<List<MediaItem>> GetFilteredMoviesAsync(int startIndex, int chunkSize, string searchQuery, string genreFilter, string sortOrder)
    {
        await EnsureMoviesCacheAsync();
        if (_masterMoviesCache == null) return new List<MediaItem>();

        var query = _masterMoviesCache.AsEnumerable();

        // APPLY SEARCH (Title, Cast, OR Director)
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var searchLower = searchQuery.ToLower();
            query = query.Where(m => 
                m.Title.ToLower().Contains(searchLower) ||
                m.Cast.Any(c => c.ToLower().Contains(searchLower)) ||
                m.Directors.Any(d => d.ToLower().Contains(searchLower))
            );
        }

        // APPLY GENRE
        if (!string.IsNullOrWhiteSpace(genreFilter) && genreFilter != "All")
        {
            query = query.Where(m => m.Genres.Any(g => string.Equals(g, genreFilter, StringComparison.OrdinalIgnoreCase)));
        }

        // APPLY SORT
        query = sortOrder switch
        {
            "Alphabetical (A-Z)" => query.OrderBy(m => m.Title),
            "Alphabetical (Z-A)" => query.OrderByDescending(m => m.Title),
            "Release Year (Newest)" => query.OrderByDescending(m => m.ReleaseYear),
            "Release Year (Oldest)" => query.OrderBy(m => m.ReleaseYear),
            _ => query.OrderByDescending(m => m.CreatedAt) // Default to Recently Added
        };

        return query.Skip(startIndex).Take(chunkSize).ToList();
    }

    public void ClearMoviesCache() => _masterMoviesCache = null;
	
	// --- TV SHOWS ---

    private List<MediaItem>? _masterEpisodesCache = null;

    // 1. Download and parse every episode into memory (Used for the Episodes Overlay)
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

                    // THE FIX: Check multiple keys just in case Channels moves the series title around
                    string showTitle = GetStringOrNumber(element, "title", "name", "show_title");
                    string episodeTitle = GetStringOrNumber(element, "episode_title");
                    string summary = GetStringOrNumber(element, "summary", "full_summary");
                    
                    string imagePath = GetStringOrNumber(element, "image_url", "thumbnail_url", "image", "thumbnail", "art", "cover_url");
                    if (Uri.TryCreate(imagePath, UriKind.Absolute, out Uri? uri) && uri.AbsolutePath == "/")
                    {
                        imagePath = ""; 
                    }

                    int season = element.TryGetProperty("season_number", out var sProp) && sProp.ValueKind == JsonValueKind.Number ? sProp.GetInt32() : 0;
                    int episode = element.TryGetProperty("episode_number", out var eProp) && eProp.ValueKind == JsonValueKind.Number ? eProp.GetInt32() : 0;
                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt64() : 0;

                    _masterEpisodesCache.Add(new MediaItem
                    {
                        Id = id,
                        Title = string.IsNullOrEmpty(showTitle) ? "Unknown Show" : showTitle,
                        CurrentShowTitle = episodeTitle,
                        PosterUrl = FormatImageUrl(baseUrl, imagePath),
                        StreamUrl = $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        Summary = summary,
                        SeasonNumber = season,
                        EpisodeNumber = episode,
                        CreatedAt = createdAt,
                        Genres = ParseStringArray(element, "genres")
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

    // 2. THE FIX: Fetch directly from the native /api/v1/shows endpoint!
    public async Task<List<MediaItem>> GetFilteredShowsAsync(int startIndex, int chunkSize, string searchQuery, string sortOrder)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        string apiUrl = $"{baseUrl}/api/v1/shows"; // We now use the canonical API
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
                    
                    // Apply our IP-Agnostic image cleanup fix
                    string imagePath = GetStringOrNumber(element, "image_url", "thumbnail_url", "image", "thumbnail", "art", "cover_url");
                    if (Uri.TryCreate(imagePath, UriKind.Absolute, out Uri? uri) && uri.AbsolutePath == "/")
                    {
                        imagePath = ""; 
                    }

                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt64() : 0;

                    showsList.Add(new MediaItem
                    {
                        Id = id,
                        Title = string.IsNullOrEmpty(title) ? "Unknown Show" : title,
                        Summary = summary,
                        PosterUrl = FormatImageUrl(baseUrl, imagePath),
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

    // 3. Grab all episodes for a specific show
    public async Task<List<MediaItem>> GetEpisodesForShowAsync(string showTitle)
    {
        await EnsureEpisodesCacheAsync();
        if (_masterEpisodesCache == null) return new List<MediaItem>();

        // THE FUZZY MATCHER: Strip punctuation, casing, and "&" signs to bridge the API gap
        string Normalize(string input) => input.Replace("&", "and").Replace(":", "").Replace("-", "").ToLower().Trim();
        string normalizedTarget = Normalize(showTitle);

        return _masterEpisodesCache
            .Where(e => 
            {
                string normalizedEp = Normalize(e.Title);
                // Matches if they are exactly the same, OR if one is a subtitle of the other ("Gold Rush: Alaska" vs "Gold Rush")
                return normalizedEp == normalizedTarget || 
                       normalizedTarget.StartsWith(normalizedEp) || 
                       normalizedEp.StartsWith(normalizedTarget);
            })
            .OrderBy(e => e.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();
    }
	
    // --- PERSONAL VIDEOS ---

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
                    
                    string imagePath = GetStringOrNumber(element, "thumbnail_url", "image_url", "image", "thumbnail", "art", "cover_url");

                    // THE IP-AGNOSTIC FIX: If the API returns any server address without a file path, clear it!
                    if (Uri.TryCreate(imagePath, UriKind.Absolute, out Uri? uri) && uri.AbsolutePath == "/")
                    {
                        imagePath = ""; 
                    }

                    groups.Add(new MediaItem
                    {
                        Id = id,
                        Title = string.IsNullOrEmpty(name) ? "Unknown Folder" : name,
                        PosterUrl = FormatImageUrl(baseUrl, imagePath)
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
                    
                    string imagePath = GetStringOrNumber(element, "thumbnail_url", "image_url", "image", "thumbnail", "art", "cover_url");
                    
                    // THE IP-AGNOSTIC FIX: If the API returns any server address without a file path, clear it!
                    if (Uri.TryCreate(imagePath, UriKind.Absolute, out Uri? uri) && uri.AbsolutePath == "/")
                    {
                        imagePath = ""; 
                    }

                    // The Ultimate Fallback: Auto-extracted screenshot frame
                    if (string.IsNullOrEmpty(imagePath) && !string.IsNullOrEmpty(id))
                    {
                        imagePath = $"/dvr/files/{id}/preview"; 
                    }

                    videos.Add(new MediaItem
                    {
                        Id = id,
                        Title = string.IsNullOrEmpty(title) ? "Unknown Video" : title,
                        PosterUrl = FormatImageUrl(baseUrl, imagePath),
                        StreamUrl = $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts"
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
        
        // Changed to use server-side descending order
        string apiUrl = $"{baseUrl}/api/v1/movies?order=desc"; 

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
                    string rawImageUrl = GetStringOrNumber(element, "image_url");
                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt64() : 0;
                    
                    movies.Add(new MediaItem
                    {
                        Id = id,
                        Title = string.IsNullOrEmpty(title) ? "Unknown" : title,
                        PosterUrl = FormatImageUrl(baseUrl, rawImageUrl),
                        StreamUrl = $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        CreatedAt = createdAt
                    });

                    // SHORT CIRCUIT: Stop parsing once we hit our limit!
                    if (movies.Count >= limit) break;
                }
            }
            
            // No need to sort in memory anymore!
            return movies;
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
        
        // Changed to use server-side descending order
        string apiUrl = $"{baseUrl}/api/v1/episodes?order=desc"; 

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
                    string rawImageUrl = GetStringOrNumber(element, "thumbnail_url", "image_url");
                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt64() : 0;
                    
                    episodes.Add(new MediaItem
                    {
                        Id = id,
                        Title = showTitle,
                        CurrentShowTitle = episodeTitle, 
                        PosterUrl = FormatImageUrl(baseUrl, rawImageUrl),
                        StreamUrl = $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        CreatedAt = createdAt
                    });

                    // SHORT CIRCUIT: Stop parsing once we hit our limit!
                    if (episodes.Count >= limit) break;
                }
            }
            
            return episodes;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch episodes: {ex.Message}");
            return new List<MediaItem>();
        }
    }

    public async Task<IEnumerable<MediaItem>> GetRecentVideosAsync(int limit = 10)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return new List<MediaItem>();

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        
        // Changed to use server-side descending order
        string apiUrl = $"{baseUrl}/api/v1/videos?order=desc"; 

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
                    string rawImageUrl = GetStringOrNumber(element, "thumbnail_url", "image_url");
                    long createdAt = element.TryGetProperty("created_at", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt64() : 0;
                    
                    videos.Add(new MediaItem
                    {
                        Id = id,
                        Title = groupTitle,
                        CurrentShowTitle = videoTitle,
                        PosterUrl = FormatImageUrl(baseUrl, rawImageUrl),
                        StreamUrl = $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts",
                        CreatedAt = createdAt
                    });

                    // SHORT CIRCUIT: Stop parsing once we hit our limit!
                    if (videos.Count >= limit) break;
                }
            }
            
            return videos;
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
            string json = await _httpClient.GetStringAsync(url);
            using JsonDocument doc = JsonDocument.Parse(json);
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

            // Only map channels that have the Favorite flag set to true!
            foreach (var c in guideChannels.Where(ch => ch.Favorite))
            {
                var currentAiring = c.CurrentAirings?.FirstOrDefault(a => a.IsAiringNow) ?? c.CurrentAirings?.FirstOrDefault();

                // THE FIX: Uses the live media engine builder to safely format virtual favorites
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
	
   // HELPER: 100% Case-Insensitive JSON Extractor (Priority Ordered)
    private string GetStringOrNumber(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object) return "";
        
        // Loop through the requested names IN ORDER OF PRIORITY first!
        foreach (var name in propertyNames)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.String) 
                    {
                        string val = prop.Value.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(val)) return val; // Only return if it's not empty
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
	
	// HELPER: 100% Case-Insensitive JSON Array Extractor
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
	
	// HELPER: Keeps the image logic clean and out of the main loops
    private string FormatImageUrl(string baseUrl, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return "";

        string cleanPath = imagePath.Trim();

        // Let C#'s native Uri parser do the heavy lifting
        if (Uri.TryCreate(cleanPath, UriKind.Absolute, out Uri? uriResult))
        {
            // 1. The Bogus URL Fix: If it's any IP/Domain but has no file route attached
            if (uriResult.AbsolutePath == "/") return ""; 
            
            // 2. The Loopback Fix (Ms Rachel): If the server hardcoded 127.0.0.1 but has a valid file path
            if (uriResult.Host == "127.0.0.1" || uriResult.Host == "localhost")
            {
                return baseUrl.TrimEnd('/') + uriResult.AbsolutePath + uriResult.Query;
            }

            // 3. Valid Internet URL (e.g., tmsimg.fancybits.co)
            return cleanPath;
        }

        // 4. Standard Relative Path Fix (e.g., "/dvr/files/123/image")
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
            // THE FIX: Map to Feral's 'StartTime' and 'Duration' properties!
            StartTime = airing?.StartTime ?? DateTime.Now,
            EndTime = (airing != null && airing.Duration.HasValue) 
                        ? airing.StartTime.AddSeconds(Convert.ToDouble(airing.Duration.Value)) 
                        : DateTime.Now.AddHours(1)
        };

        // Determine if this is a virtual loop timeline channel
        bool isVirtualChannel = channel.Id != null && channel.Id.StartsWith("virtual", StringComparison.OrdinalIgnoreCase);

        if (isVirtualChannel && airing != null && !string.IsNullOrWhiteSpace(airing.Source))
        {
            // Extract file ID out of the raw source path (e.g., "dvr/files/1234")
            string fileId = airing.Source.Split('/').Last();
            media.StreamUrl = $"{baseUrl.TrimEnd('/')}/dvr/files/{fileId}/hls/stream.m3u8";
            
            // THE FIX: Ensure the offset calculator also uses Feral's StartTime
            var airStart = airing.StartTime; 
            if (airStart != DateTime.MinValue)
            {
                int offset = (int)(DateTime.Now - airStart).TotalSeconds;
                media.StartOffsetSeconds = offset > 0 ? offset : 0;
            }
        }
        else
        {
            // Standard Live TV Channel HLS delivery
            media.StreamUrl = $"{baseUrl.TrimEnd('/')}/devices/ANY/channels/{channel.Number}/hls/master.m3u8";
        }

        return media;
    }
}