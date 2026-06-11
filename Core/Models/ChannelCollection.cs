using System.Collections.Generic;

namespace HTPC.Core.Models;

public class ChannelCollection
{
    public string Id { get; set; } = string.Empty; // The "slug"
    public string Name { get; set; } = "All Channels";
    
    // Explicit Inclusions
    public List<string> Channels { get; set; } = new List<string>();

    // Smart Rules
    public List<string> Genres { get; set; } = new List<string>();
    public List<string> Categories { get; set; } = new List<string>();
    public List<string> Tags { get; set; } = new List<string>();
    public List<string> Keywords { get; set; } = new List<string>();
    public List<string> ExcludedSources { get; set; } = new List<string>();

    // FIX: Tells WPF exactly what text to display when it renders this object
    public override string ToString() => Name;
}