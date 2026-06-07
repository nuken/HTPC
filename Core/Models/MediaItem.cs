namespace HTPC.Core.Models;

public class MediaItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string PosterUrl { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;

    // NEW: Live TV Metadata placeholders
    public string CurrentShowTitle { get; set; } = string.Empty;
    public string CurrentShowPosterUrl { get; set; } = string.Empty;
}