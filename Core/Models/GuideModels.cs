using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace HTPC.Core.Models;

// --- NEW MODEL FOR THE PRIORITY ENDPOINT ---
public class DevicePriority
{
    [JsonPropertyName("DeviceID")]
    public string? DeviceId { get; set; }
    
    [JsonPropertyName("FriendlyName")]
    public string? FriendlyName { get; set; }
}

public class Channel : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;

    // Source Tracking for Stacking
    public string DeviceId { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty; // <-- NEW: Used to group duplicate channels

    public bool IsHD { get; set; } = false;	

    private bool _favorite = false;
    public bool Favorite 
    { 
        get => _favorite; 
        set 
        { 
            _favorite = value; 
            OnPropertyChanged(); 
        } 
    }
	
	private bool _hidden = false;
    public bool Hidden 
    { 
        get => _hidden; 
        set 
        { 
            _hidden = value; 
            OnPropertyChanged(); 
        } 
    }
    
    public List<Airing>? CurrentAirings { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public class Airing
{
    public string? ChannelNumber { get; set; }
    public string? Title { get; set; }
    public string? EpisodeTitle { get; set; }
    public string? DisplaySummary { get; set; }
    public string? ImageUrl { get; set; }
    public string? Source { get; set; }
    
    public string? SeriesId { get; set; }
    public string? ProgramId { get; set; }
	
	public List<string> Genres { get; set; } = new List<string>();
    public List<string> Categories { get; set; } = new List<string>();
    public List<string> Tags { get; set; } = new List<string>();
    
    public DateTime StartTime { get; set; }
    public double? Duration { get; set; }
    
    public double LeftOffset { get; set; } = 0;
    public System.Windows.Thickness DynamicMargin => new System.Windows.Thickness(LeftOffset, 0, 0, 0);
    public System.Windows.Thickness InnerContentMargin => new System.Windows.Thickness(LeftOffset < 0 ? Math.Abs(LeftOffset) : 0, 0, 0, 0);
    public double BlockWidth => ((Duration ?? 1800) / 60.0) * 8.0; 
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    public string DisplayTitle 
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(EpisodeTitle) && Title != EpisodeTitle)
                return $"{Title} - {EpisodeTitle}";
            if (!string.IsNullOrWhiteSpace(Title)) return Title;
            if (!string.IsNullOrWhiteSpace(EpisodeTitle)) return EpisodeTitle;
            return "Unknown Program";
        }
    }

    public bool IsAiringNow
    {
        get
        {
            if (StartTime == DateTime.MinValue || Duration == null) return false;
            DateTime endTime = StartTime.AddSeconds(Duration.Value);
            return DateTime.Now >= StartTime && DateTime.Now < endTime;
        }
    }

    public string CategoryColor { get; set; } = "Transparent"; 
}