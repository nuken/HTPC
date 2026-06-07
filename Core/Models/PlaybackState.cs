using System;

namespace HTPC.Core.Models;

public class PlaybackState
{
    public int Id { get; set; }
    public string MediaId { get; set; } = string.Empty; // The ID from your Channel DVR
    public long PositionTicks { get; set; } // The exact timestamp they stopped watching
    public DateTime LastPlayedAt { get; set; } = DateTime.UtcNow;
}