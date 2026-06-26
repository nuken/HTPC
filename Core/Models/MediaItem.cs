using System;
using System.Collections.Generic;
using System.ComponentModel; // Required
using System.Runtime.CompilerServices; // Required

namespace HTPC.Core.Models;

// Added : INotifyPropertyChanged here
public class MediaItem : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string PosterUrl { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
	public string? SubtitleUrl { get; set; }
    
    public bool IsLiveTv { get; set; } = false;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    public string CurrentShowTitle { get; set; } = string.Empty;
    public string CurrentShowPosterUrl { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    
    public int ReleaseYear { get; set; }
    public List<string> Genres { get; set; } = new List<string>();
    public List<string> Cast { get; set; } = new List<string>();
    public List<string> Directors { get; set; } = new List<string>();
	
	public List<double>? Commercials { get; set; } = new List<double>();
	
	public string Path { get; set; } = string.Empty;
    public bool RequiresBrowser { get; set; } = false;
    
    // --- DVR METADATA ---
    private bool _isWatched;
    public bool IsWatched 
    { 
        get => _isWatched; 
        set { _isWatched = value; OnPropertyChanged(); } 
    }

    private bool _isFavorite;
    public bool IsFavorite 
    { 
        get => _isFavorite; 
        set { _isFavorite = value; OnPropertyChanged(); } 
    }
    
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string Summary { get; set; } = string.Empty;
	public double StartOffset { get; set; }
    public int StartOffsetSeconds { get; set; }

    // --- REQUIRED IMPLEMENTATION ---
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}