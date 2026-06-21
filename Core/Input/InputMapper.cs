using System;
using System.Collections.Generic;
using System.Windows.Input;
using HTPC.Services;

namespace HTPC.Core.Input;

public enum HtpcCommand
{
    None,
    Up,
    Down,
    Left,
    Right,
    Select,
    Back,
	Home,
    PlayPause,
    SkipForward,
    SkipBackward,
    ToggleSubtitles,
    Fullscreen
}

public static class InputMapper
{
    private static Dictionary<Key, HtpcCommand> _keyMap = new();
    private static bool _isLoaded = false;

    public static void ReloadMappings()
    {
        var prefs = PreferencesManager.Load();
        _keyMap.Clear();

        if (prefs.KeyBindings != null)
        {
            foreach (var kvp in prefs.KeyBindings)
            {
                // Try to safely parse the strings back into Enums
                if (Enum.TryParse<Key>(kvp.Key, true, out Key wpfKey) && 
                    Enum.TryParse<HtpcCommand>(kvp.Value, true, out HtpcCommand command))
                {
                    _keyMap[wpfKey] = command;
                }
            }
        }
        _isLoaded = true;
    }

    public static HtpcCommand GetCommand(Key key)
    {
        if (!_isLoaded) ReloadMappings(); // Lazy load on first launch

        if (_keyMap.TryGetValue(key, out var command))
        {
            return command;
        }
        return HtpcCommand.None;
    }
}