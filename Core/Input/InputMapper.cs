using System.Collections.Generic;
using System.Windows.Input;

namespace HTPC.Core.Input;

// 1. The semantic actions your app understands
public enum HtpcCommand
{
    None,
	Home,
	Stop,
	VolumeUp,
	VolumeDown,
    Up,
    Down,
    Left,
    Right,
    Select,
    Back,
    PlayPause,
    SkipForward,
    SkipBackward,
    ToggleSubtitles,
    ToggleAudio
}

// 2. The engine that translates physical keys to semantic actions
public static class InputMapper
{
    // Right now these are hardcoded defaults. 
    // Later, you can easily load this dictionary from a JSON settings file to allow user-customization!
    private static readonly Dictionary<Key, HtpcCommand> _keyMap = new()
    {
        // D-Pad Navigation
        { Key.Up, HtpcCommand.Up },
        { Key.Down, HtpcCommand.Down },
        { Key.Left, HtpcCommand.Left },
        { Key.Right, HtpcCommand.Right },
        
        // OK / Select
        { Key.Enter, HtpcCommand.Select },
        { Key.Space, HtpcCommand.Select },
        
        // Return / Back (Catches the Backspace and remote 'Return' buttons)
        { Key.Escape, HtpcCommand.Back },
        { Key.Back, HtpcCommand.Back },
        { Key.BrowserBack, HtpcCommand.Back },

        // Home / Dashboard (Catches the physical 'Home' or 'Windows' button on remotes)
        { Key.BrowserHome, HtpcCommand.Home },
        { Key.LWin, HtpcCommand.Home },
        { Key.RWin, HtpcCommand.Home },

        // Dedicated Media Control Buttons
        { Key.MediaPlayPause, HtpcCommand.PlayPause },
        { Key.MediaNextTrack, HtpcCommand.SkipForward },
        { Key.MediaPreviousTrack, HtpcCommand.SkipBackward },
        { Key.MediaStop, HtpcCommand.Stop },
        
        // Volume Controls
        { Key.VolumeUp, HtpcCommand.VolumeUp },
        { Key.VolumeDown, HtpcCommand.VolumeDown },
        { Key.VolumeMute, HtpcCommand.ToggleAudio }, // Or map to a dedicated Mute command
        
        // Keyboard Shortcuts (Optional fallback)
        { Key.C, HtpcCommand.ToggleSubtitles },
        { Key.A, HtpcCommand.ToggleAudio }
    };
	
	// The method every Window will call to decipher the key press
    public static HtpcCommand GetCommand(Key key)
    {
        return _keyMap.TryGetValue(key, out var command) ? command : HtpcCommand.None;
    }
}