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
	
	// --- NEW: DATE PROPERTY FOR WPF GROUPING ---
    public DateTime CreatedAtDate => CreatedAt > 0 
        ? DateTimeOffset.FromUnixTimeSeconds(CreatedAt).LocalDateTime.Date 
        : DateTime.MinValue.Date;
    
    public int ReleaseYear { get; set; }
    public List<string> Genres { get; set; } = new List<string>();
	public List<string> Categories { get; set; } = new List<string>();
	public bool IsImported { get; set; }
    public List<string> Cast { get; set; } = new List<string>();
    public List<string> Directors { get; set; } = new List<string>();
	
	public double Duration { get; set; }
    public string ContentRating { get; set; } = string.Empty;
    public long LastWatchedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long LastRecordedAt { get; set; }
	
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

    // --- NEW: DVR DISCOVERY PROPERTIES ---
    public string ChannelName { get; set; } = string.Empty;
    public string SeriesId { get; set; } = string.Empty;
    public string ChannelNumber { get; set; } = string.Empty;

    // --- NEW: IN-PROGRESS RECORDING FLAG ---
    private bool _isCompleted = true;
    public bool IsCompleted 
    { 
        get => _isCompleted; 
        set { _isCompleted = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRecording)); } 
    }
    
    // --- FIX: WPF HELPER FOR BINDING ---
    public bool IsRecording => !IsCompleted;
    
    // --- NEW: SCHEDULED RECORDINGS FLAGS ---
    public bool IsScheduled { get; set; }
    public string DisplayTime { get; set; } = string.Empty;
	
	public string? LiveScore { get; set; }
    public string? GamePeriod { get; set; }

	public double StartOffset { get; set; }
    public int StartOffsetSeconds { get; set; }

    // --- REQUIRED IMPLEMENTATION ---
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}