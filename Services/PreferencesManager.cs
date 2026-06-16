using System;
using System.IO;
using System.Text.Json;

namespace HTPC.Services;

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
	public double UiScaleMultiplier { get; set; } = 1.0; // 1.0 = 100%, 1.2 = 120%, 1.5 = 150%
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