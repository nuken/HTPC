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