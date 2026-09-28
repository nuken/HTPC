using System;
using System.ComponentModel.DataAnnotations;

namespace HTPC.Core.Models;

public class VODCatalogItem
{
    [Key]
    public int Id { get; set; }
    
    // Metadata
    public int TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string PosterUrl { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;

    // Playback Routing
    public string ProviderKey { get; set; } = string.Empty; // e.g., "pluto"
    public string NativeDeepLink { get; set; } = string.Empty;
    
    // Cache Management
    public DateTime LastVerified { get; set; } 
    public bool IsActive { get; set; } = true;
}