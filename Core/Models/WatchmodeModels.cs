using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HTPC.Core.Models;

public class WatchmodeListResponse
{
    [JsonPropertyName("titles")]
    public List<WatchmodeBasicTitle>? Titles { get; set; }
}

public class WatchmodeBasicTitle
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("year")] public int Year { get; set; }
}

public class WatchmodeTitleDetails
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("year")] public int? Year { get; set; }
    [JsonPropertyName("plot_overview")] public string? Overview { get; set; }
    [JsonPropertyName("poster")] public string? Poster { get; set; }
    [JsonPropertyName("genre_names")] public List<string>? Genres { get; set; }
    [JsonPropertyName("tmdb_id")] public int? TmdbId { get; set; }
    [JsonPropertyName("sources")] public List<WatchmodeSource>? Sources { get; set; }
}

public class WatchmodeSource
{
    [JsonPropertyName("source_id")] public int SourceId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("android_url")] public string? AndroidUrl { get; set; }
    [JsonPropertyName("web_url")] public string? WebUrl { get; set; }
}