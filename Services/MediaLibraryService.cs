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

    public MediaLibraryService(ServerManagerService serverManager, HttpClient httpClient, ILogger<MediaLibraryService> logger)
    {
        _serverManager = serverManager;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<MediaItem>> GetFeaturedMoviesAsync()
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null)
        {
            _logger.LogWarning("No active Channel DVR server found.");
            return new List<MediaItem>();
        }

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        
        // FIX 1: Use the correct API endpoint discovered in ChannelsApi.cs
        string apiUrl = $"{baseUrl}/api/v1/movies"; 

        try
        {
            _logger.LogInformation($"Fetching media from: {apiUrl}");
            
            var response = await _httpClient.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();
            
            string jsonResponse = await response.Content.ReadAsStringAsync();
            
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            var movies = new List<MediaItem>();
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    // FIX 2: Use exact lowercase keys from Feral-HTPC
                    string id = element.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    string title = element.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "Unknown" : "Unknown";
                    string rawImageUrl = element.TryGetProperty("image_url", out var imgProp) ? imgProp.GetString() ?? "" : "";
                    
                    if (string.IsNullOrEmpty(id)) continue;

                    // FIX 3: Replicated Feral-HTPC's exact relative-path logic for Posters
                    string finalPosterUrl = rawImageUrl;
                    if (!string.IsNullOrWhiteSpace(rawImageUrl) && !rawImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        finalPosterUrl = rawImageUrl.StartsWith("/") 
                            ? $"{baseUrl}{rawImageUrl}" 
                            : $"{baseUrl}/{rawImageUrl}";
                    }
                    
                    movies.Add(new MediaItem
                    {
                        Id = id,
                        Title = title,
                        PosterUrl = finalPosterUrl,
                        StreamUrl = $"{baseUrl}/dvr/files/{id}/stream.mpg?format=ts" 
                    });
                }
            }
            
            _logger.LogInformation($"Successfully loaded {movies.Count} items.");
            return movies;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch media: {ex.Message}");
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
                    var col = new ChannelCollection
                    {
                        Id = element.TryGetProperty("slug", out var idProp) ? idProp.GetString() ?? "" : "",
                        Name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : ""
                    };
                    
                    if (element.TryGetProperty("items", out var itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in itemsArray.EnumerateArray())
                        {
                            // FIX 1: Safely handle both String and Number channel IDs
                            if (item.ValueKind == JsonValueKind.String) 
                                col.Channels.Add(item.GetString() ?? "");
                            else if (item.ValueKind == JsonValueKind.Number) 
                                col.Channels.Add(item.ToString());
                        }
                    }
                    collections.Add(col);
                }
            }
            
            return collections.Where(c => c.Channels.Count > 0).ToList();
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
            var channels = new List<MediaItem>();
            var collectionSortOrder = new Dictionary<string, int>(); 
            
            // We now pull EVERYTHING from the Guide endpoint, as it has better Channel IDs!
            string guideJson = await _httpClient.GetStringAsync($"{baseUrl}/devices/ANY/guide/now");
            using JsonDocument guideDoc = JsonDocument.Parse(guideJson);
            
            if (guideDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var guideEntry in guideDoc.RootElement.EnumerateArray())
                {
                    if (!guideEntry.TryGetProperty("Channel", out var channelProp)) continue;

                    // 1. Check Hidden flag
                    if (channelProp.TryGetProperty("Hidden", out var hiddenProp) && 
                       (hiddenProp.ValueKind == JsonValueKind.True || (hiddenProp.ValueKind == JsonValueKind.Number && hiddenProp.GetInt32() == 1))) 
                        continue;

                    // 2. Extract every single identifier
                    string channelNumber = GetStringOrNumber(channelProp, "Number", "number", "GuideNumber");
                    string channelId = GetStringOrNumber(channelProp, "ChannelID", "channelId", "id", "ID");
                    string name = GetStringOrNumber(channelProp, "Name", "name", "GuideName");
                    string callSign = GetStringOrNumber(channelProp, "CallSign", "callSign");
                    string logoUrl = GetStringOrNumber(channelProp, "Image", "image", "Logo", "logo", "art");

                    if (string.IsNullOrWhiteSpace(channelNumber)) continue;

                    if (!string.IsNullOrEmpty(logoUrl) && !logoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        logoUrl = $"{baseUrl}/{logoUrl.TrimStart('/')}";

                    int sortIndex = 999999;

                    // 3. The Ultimate TVE / Collection Matcher
                    if (activeCollection != null && !string.IsNullOrEmpty(activeCollection.Id))
                    {
                        bool inCollection = false;
                        for (int i = 0; i < activeCollection.Channels.Count; i++)
                        {
                            string colChannel = activeCollection.Channels[i];
                            
                            // Check exact matches across all properties
                            if (colChannel.Equals(channelId, StringComparison.OrdinalIgnoreCase) ||
                                colChannel.Equals(channelNumber, StringComparison.OrdinalIgnoreCase) ||
                                colChannel.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                colChannel.Equals(callSign, StringComparison.OrdinalIgnoreCase) ||
                                (double.TryParse(colChannel, out double dc) && double.TryParse(channelNumber, out double did) && dc == did))
                            {
                                inCollection = true;
                                sortIndex = i; 
                                break;
                            }
                            
                            // TVE / Pluto Fuzzy Matcher (Allows "youtube_tv_cbs" to match "cbs")
                            if (!inCollection && !string.IsNullOrEmpty(channelId) && channelId.Length >= 2)
                            {
                                if (colChannel.EndsWith(channelId, StringComparison.OrdinalIgnoreCase) || 
                                    colChannel.Contains($"_{channelId}", StringComparison.OrdinalIgnoreCase) ||
                                    colChannel.Contains($"-{channelId}", StringComparison.OrdinalIgnoreCase))
                                {
                                    inCollection = true;
                                    sortIndex = i;
                                    break;
                                }
                            }
                        }
                        if (!inCollection) continue;
                    }

                    // 4. Extract Now Playing Data
                    string currentTitle = "Unknown Program";
                    string currentImage = logoUrl;

                    if (guideEntry.TryGetProperty("Airings", out var airingsProp) && airingsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var airing in airingsProp.EnumerateArray())
                        {
                            currentTitle = GetStringOrNumber(airing, "Title");
                            string aImg = GetStringOrNumber(airing, "Image");
                            
                            if (!string.IsNullOrEmpty(aImg))
                            {
                                currentImage = aImg.StartsWith("http", StringComparison.OrdinalIgnoreCase) 
                                    ? aImg 
                                    : $"{baseUrl}/{aImg.TrimStart('/')}";
                            }
                            break; 
                        }
                    }

                    channels.Add(new MediaItem
                    {
                        Id = channelNumber, // Must remain the Number for correct streaming
                        Title = name,
                        PosterUrl = logoUrl,
                        CurrentShowTitle = string.IsNullOrEmpty(currentTitle) ? "Unknown Program" : currentTitle,
                        CurrentShowPosterUrl = currentImage,
                        StreamUrl = $"{baseUrl}/devices/ANY/channels/{channelNumber}/stream.mpg?format=ts" 
                    });
                    
                    collectionSortOrder[channelNumber] = sortIndex;
                }
            }

            var uniqueChannels = channels.GroupBy(c => c.Id).Select(g => g.First());

            if (activeCollection != null && !string.IsNullOrEmpty(activeCollection.Id))
            {
                return uniqueChannels.OrderBy(c => collectionSortOrder.TryGetValue(c.Id, out int idx) ? idx : 999999).ToList();
            }
            else
            {
                return uniqueChannels.OrderBy(c => double.TryParse(c.Id, out double num) ? num : 999999).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch live channels: {ex.Message}");
            return new List<MediaItem>();
        }
    }

    // HELPER: Mimics Feral-HTPC's Channel.GetValue() to handle Channel DVR's messy JSON
    private string GetStringOrNumber(JsonElement element, params string[] propertyNames)
    {
        foreach (var propName in propertyNames)
        {
            // Try exact case, then lowercase
            if (element.TryGetProperty(propName, out var prop) || 
                element.TryGetProperty(propName.ToLower(), out prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return prop.GetString() ?? "";
                
                // If the API returns a number instead of a string, safely convert it
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.ToString() ?? "";
            }
        }
        return "";
    }
}	