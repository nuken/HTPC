using System;
using System.IO;
using System.Collections.Generic;
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
    public string AppTheme { get; set; } = "Dark";
	public List<string> ActiveSportFilters { get; set; } = new List<string>();
	public int SkipForwardSeconds { get; set; } = 30;
    public int SkipBackwardSeconds { get; set; } = 15;
	public bool HideSportsScores { get; set; } = false;
	public int InstantReplaySeconds { get; set; } = 20;
	public bool InstantReplaySlowMotion { get; set; } = true;
	
	// --- NEW: VOD BRIDGE SETTINGS ---
    public bool EnableAdbVodBridge { get; set; } = false;
    public string AdbTunerUrl { get; set; } = "";
   	public DateTime LastVodSync { get; set; } = DateTime.MinValue;
    
	// --- NEW: COLLECTION SORTING ---
    public string CollectionSort { get; set; } = "Alphabetical";
    public string CollectionOrder { get; set; } = "Forward";

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
    public Dictionary<string, string> KeyBindings { get; set; } = new()
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
    private static readonly object _lock = new object();
    private static AppPreferences? _cachedPrefs = null;

    private static readonly string SettingsFile = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory, 
        "htpc_prefs.json");

    public static AppPreferences Load()
    {
        lock (_lock)
        {
            // 1. Serve immediately from memory if already loaded
            if (_cachedPrefs != null)
            {
                return _cachedPrefs;
            }

            // 2. Initial load from disk
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    _cachedPrefs = JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
                    return _cachedPrefs;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PREFS] Failed to read settings file: {ex.Message}");
            }

            // 3. Fallback to default if file does not exist or failed initial parse
            _cachedPrefs = new AppPreferences();
            return _cachedPrefs;
        }
    }

    public static void Save(AppPreferences prefs)
    {
        lock (_lock)
        {
            // 1. Keep memory state updated immediately
            _cachedPrefs = prefs;

            // 2. Persist safely to disk with an OS lock retry loop
            int retries = 3;
            while (retries > 0)
            {
                try
                {
                    string json = JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(SettingsFile, json);
                    break; // Success, exit the retry loop
                }
                catch (IOException)
                {
                    retries--;
                    if (retries == 0) Console.WriteLine("[PREFS] File is locked by OS. Save failed.");
                    System.Threading.Thread.Sleep(100); // Wait 100ms for the OS/Antivirus to release the file
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PREFS] Error writing settings to disk: {ex.Message}");
                    break;
                }
            }
        }
    }

    public static string NormalizeAdbUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        
        string trimmed = input.Trim().TrimEnd('/');
        
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "http://" + trimmed;
        }

        return trimmed;
    }

    // Helper methods now operate safely against the synchronized memory cache
    public static string LoadMovieSort() => Load().MovieSort;
    public static string LoadGuideCollection() => Load().GuideCollection;
    
    public static void SaveMovieSort(string sortValue) 
    {
        lock (_lock)
        {
            var prefs = Load();
            prefs.MovieSort = sortValue;
            Save(prefs);
        }
    }

    public static string LoadTheme() => Load().AppTheme;

    public static void SaveTheme(string themeValue)
    {
        lock (_lock)
        {
            var prefs = Load();
            prefs.AppTheme = themeValue;
            Save(prefs);
        }
    }

    public static string LoadCollectionSort() => Load().CollectionSort;
    
    public static void SaveCollectionSort(string sortValue) 
    {
        lock (_lock)
        {
            var prefs = Load();
            prefs.CollectionSort = sortValue;
            Save(prefs);
        }
    }

    public static string LoadCollectionOrder() => Load().CollectionOrder;
    
    public static void SaveCollectionOrder(string orderValue) 
    {
        lock (_lock)
        {
            var prefs = Load();
            prefs.CollectionOrder = orderValue;
            Save(prefs);
        }
    }
}