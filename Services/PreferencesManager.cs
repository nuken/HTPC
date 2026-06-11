using System;
using System.IO;
using System.Text.Json;

namespace HTPC.Services;

public static class PreferencesManager
{
    private static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "htpc_prefs.json");

    public static string LoadMovieSort()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("MovieSort", out var prop))
                    return prop.GetString() ?? "Recently Added";
            }
        }
        catch { }
        return "Recently Added"; // Default
    }

    public static void SaveMovieSort(string sortValue)
    {
        try
        {
            string json = JsonSerializer.Serialize(new { MovieSort = sortValue });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }
}