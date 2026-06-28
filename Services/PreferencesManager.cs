using System;
using System.IO;
using System.Text.Json;

namespace HTPC.Services;

public class DashboardRowConfig
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public int Order { get; set; } = 0;
}

public class AppPreferences
{
    public string MovieSort { get; set; } = "Recently Added";
    public string GuideCollection { get; set; } = "All";
    public int PaddingStartMinutes { get; set; } = 0;
    public int PaddingEndMinutes { get; set; } = 0;
    public string LastGuideCollection { get; set; } = "All Channels";
    public Dictionary<string, List<string>> CustomChannelOrders { get; set; } = new Dictionary<string, List<string>>();
    public bool IsFullscreen { get; set; } = true;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 720;
    public double WindowTop { get; set; } = 100;
    public double WindowLeft { get; set; } = 100;
    public double UiScaleMultiplier { get; set; } = 1.0; 
	public string LastMultiviewCollection { get; set; } = "All Channels";
	public int CommercialSkipMode { get; set; } = 2;
	public int Volume { get; set; } = 100;
	public string LastIgnoredVersion { get; set; } = string.Empty;
    public DateTime IgnoreUntilDate { get; set; } = DateTime.MinValue;
	// --- NEW: MPV ENGINE SETTINGS ---
    public string HardwareDecoding { get; set; } = "d3d11va";
    public string VideoSync { get; set; } = "audio";

    // --- NEW: VIDEO PROCESSING ---
    public bool EnableUpscaling { get; set; } = false;
    public string UpscalerPreset { get; set; } = "RAVU"; 
	
	// --- NEW: DASHBOARD LAYOUT ---
    public List<DashboardRowConfig> DashboardLayout { get; set; } = new List<DashboardRowConfig>
    {
        new DashboardRowConfig { Id = "UpNext", DisplayName = "Up Next", Order = 0, IsVisible = true },
        new DashboardRowConfig { Id = "LiveTv", DisplayName = "Live TV", Order = 1, IsVisible = true },
        new DashboardRowConfig { Id = "Movies", DisplayName = "Recent Movies", Order = 2, IsVisible = true },
        new DashboardRowConfig { Id = "Shows", DisplayName = "Recent Episodes", Order = 3, IsVisible = true },
        new DashboardRowConfig { Id = "Videos", DisplayName = "Recent Videos", Order = 4, IsVisible = true }
    };
	
	// --- REMOTE CONTROL MAPPINGS ---
    // Maps a WPF Key string to an HtpcCommand string
    public System.Collections.Generic.Dictionary<string, string> KeyBindings { get; set; } = new()
    {
        { "Up", "Up" },
        { "Down", "Down" },
        { "Left", "Left" },
        { "Right", "Right" },
        { "Enter", "Select" },
        { "Return", "Select" },
        { "Escape", "Back" },
        { "Back", "Back" },           // Standard Backspace
        { "BrowserBack", "Back" },    // Dedicated remote "Back" button
        { "MediaPlayPause", "PlayPause" },
        { "MediaPreviousTrack", "SkipBackward" },
        { "MediaNextTrack", "SkipForward" },
        { "P", "PlayPause" },
        { "C", "ToggleSubtitles" },
        { "F", "Fullscreen" }
    };
}

public static class PreferencesManager
{
    private static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "htpc_prefs.json");

    public static AppPreferences Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
            }
        }
        catch { }
        return new AppPreferences(); // Defaults
    }

    public static void Save(AppPreferences prefs)
    {
        try
        {
            string json = JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }

    // Keeping your original method signatures so we don't break existing code
    public static string LoadMovieSort() => Load().MovieSort;
    public static string LoadGuideCollection() => Load().GuideCollection;
    public static void SaveMovieSort(string sortValue) 
    {
        var prefs = Load();
        prefs.MovieSort = sortValue;
        Save(prefs);
    }
}