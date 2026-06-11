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
	public long CreatedAt { get; set; }
	
	// --- NEW PROPERTIES FOR MOVIE FILTERING ---
    public int ReleaseYear { get; set; }
    public List<string> Genres { get; set; } = new List<string>();
    public List<string> Cast { get; set; } = new List<string>();
    public List<string> Directors { get; set; } = new List<string>();
}