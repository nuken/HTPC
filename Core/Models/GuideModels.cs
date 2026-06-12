using System;
using System.Collections.Generic;

namespace HTPC.Core.Models;

public class Channel
{
    public string Id { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public bool Favorite { get; set; } = false;
    
    public List<Airing>? CurrentAirings { get; set; }
}

public class Airing
{
    public string? ChannelNumber { get; set; }
    public string? Title { get; set; }
    public string? EpisodeTitle { get; set; }
    public string? DisplaySummary { get; set; }
    public string? ImageUrl { get; set; }
    public string? Source { get; set; }
	
    public DateTime StartTime { get; set; }
    public double? Duration { get; set; }
    
    // UI Properties for the Timeline
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