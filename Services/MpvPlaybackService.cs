using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HTPC.Core.Interop;
using HTPC.Core.Models;
using HTPC.Core.Data;

namespace HTPC.Services;

public class MpvPlaybackService : IDisposable
{
    private readonly ILogger<MpvPlaybackService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private IntPtr _mpvContext;
    
    private Timer? _positionTimer;
    private MediaItem? _currentMedia;

    public MpvPlaybackService(ILogger<MpvPlaybackService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        InitializeMpv();
    }

    private void InitializeMpv()
    {
        _logger.LogInformation("Initializing native libmpv engine...");

        _mpvContext = Libmpv.mpv_create();
        if (_mpvContext == IntPtr.Zero) throw new Exception("Failed to create libmpv context.");

        Libmpv.mpv_set_option_string(_mpvContext, "terminal", "yes");
        Libmpv.mpv_set_option_string(_mpvContext, "msg-level", "all=info"); 
        Libmpv.mpv_set_option_string(_mpvContext, "vo", "gpu-next");
        Libmpv.mpv_set_option_string(_mpvContext, "gpu-api", "d3d11");
        Libmpv.mpv_set_option_string(_mpvContext, "hwdec", "auto-copy");
        
        // --- PRE-BUFFER CACHE SETTINGS (Eliminates Stutter) ---
        Libmpv.mpv_set_option_string(_mpvContext, "cache", "yes");
        Libmpv.mpv_set_option_string(_mpvContext, "demuxer-max-bytes", "150000000");
        Libmpv.mpv_set_option_string(_mpvContext, "demuxer-readahead-secs", "10");
        Libmpv.mpv_set_option_string(_mpvContext, "cache-pause", "yes");

        int result = Libmpv.mpv_initialize(_mpvContext);
        if (result < 0) throw new Exception($"Failed to initialize libmpv context. Error: {result}");

        _positionTimer = new Timer(SaveCurrentPosition, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void AttachToWindow(IntPtr hwnd)
    {
        string windowIdStr = hwnd.ToInt64().ToString();
        Libmpv.mpv_set_property_string(_mpvContext, "wid", windowIdStr);
    }

    public void PlayMedia(MediaItem media)
    {
        _currentMedia = media;
        _logger.LogInformation($"Loading media: {media.Title}");

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = db.PlaybackStates.FirstOrDefault(s => s.MediaId == media.Id);
            
            if (state != null && state.PositionTicks > 0)
            {
                double startSeconds = TimeSpan.FromTicks(state.PositionTicks).TotalSeconds;
                Libmpv.mpv_set_option_string(_mpvContext, "start", startSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _logger.LogInformation($"Resuming at {startSeconds} seconds.");
            }
            else
            {
                Libmpv.mpv_set_option_string(_mpvContext, "start", "0");
            }
        }

        Libmpv.mpv_command_string(_mpvContext, $"loadfile \"{media.StreamUrl}\"");
        _positionTimer?.Change(5000, 5000);
    }
	
	private bool _isAnimeModeActive = false;

    public void ApplyUpscalerSettings()
    {
        var prefs = PreferencesManager.Load();

        // 1. Clear any existing external shaders from the pipeline (crucial for hot-swapping)
        Libmpv.mpv_set_option_string(_mpvContext, "glsl-shaders", "");

        // 2. Set the high-quality native base (Tier 3)
        Libmpv.mpv_set_option_string(_mpvContext, "vo", "gpu-next");
        Libmpv.mpv_set_option_string(_mpvContext, "scale", "spline36");

        // 3. If upscaling is disabled globally, stop right here
        if (!prefs.EnableUpscaling) return;

        string shaderPath = string.Empty;
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 4. Determine which shader to inject
        if (_isAnimeModeActive)
        {
            shaderPath = System.IO.Path.Combine(baseDir, "Shaders", "Anime4K_Restore_CNN_M.glsl");
        }
        else if (prefs.UpscalerPreset == "RAVU")
        {
            shaderPath = System.IO.Path.Combine(baseDir, "Shaders", "ravu-zoom-r3.glsl");
        }
        else if (prefs.UpscalerPreset == "ArtCNN")
        {
            shaderPath = System.IO.Path.Combine(baseDir, "Shaders", "ArtCNN_C4F32.glsl");
        }

        // 5. Inject the shader directly into the active MPV renderer
        if (!string.IsNullOrEmpty(shaderPath) && System.IO.File.Exists(shaderPath))
        {
            Libmpv.mpv_set_option_string(_mpvContext, "glsl-shaders", shaderPath);
        }
    }

    public bool ToggleAnimeMode()
    {
        _isAnimeModeActive = !_isAnimeModeActive;
        ApplyUpscalerSettings(); // Instantly re-apply pipeline without stopping video
        return _isAnimeModeActive;
    }
    
    public void Pause()
    {
        _logger.LogInformation("Pausing playback...");
        Libmpv.mpv_set_property_string(_mpvContext, "pause", "yes");
    }

    public void Resume()
    {
        _logger.LogInformation("Resuming playback...");
        Libmpv.mpv_set_property_string(_mpvContext, "pause", "no");
    }

    public void Stop()
    {
        _positionTimer?.Change(Timeout.Infinite, Timeout.Infinite); 
        SaveCurrentPosition(null); 
        Libmpv.mpv_command_string(_mpvContext, "stop"); 
        _currentMedia = null;
    }
    
    public void SetVolume(int volume)
    {
        if (_mpvContext != IntPtr.Zero)
        {
            Core.Interop.Libmpv.mpv_set_property_string(_mpvContext, "volume", volume.ToString());
        }
    }
    
    public double GetDuration()
    {
        if (_mpvContext == IntPtr.Zero) return 0;
        Libmpv.mpv_get_property(_mpvContext, "duration", 5, out double duration);
        return duration;
    }

    public double GetPosition()
    {
        if (_mpvContext == IntPtr.Zero) return 0;
        Libmpv.mpv_get_property(_mpvContext, "time-pos", 5, out double pos);
        return pos;
    }

    public void SeekAbsolute(double seconds)
    {
        if (_mpvContext == IntPtr.Zero) return;
        Libmpv.mpv_command_string(_mpvContext, $"seek {seconds} absolute");
    }

    public void SeekRelative(double seconds)
    {
        if (_mpvContext == IntPtr.Zero) return;
        Libmpv.mpv_command_string(_mpvContext, $"seek {seconds} relative");
    }

    public void CycleSubtitles()
    {
        if (_mpvContext == IntPtr.Zero) return;
        Libmpv.mpv_command_string(_mpvContext, "cycle sub");
    }

    private void SaveCurrentPosition(object? state)
    {
        if (_currentMedia == null || _mpvContext == IntPtr.Zero) return;

        int result = Libmpv.mpv_get_property(_mpvContext, "time-pos", 5, out double timeInSeconds);
        if (result < 0) return; 

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var playbackState = db.PlaybackStates.FirstOrDefault(s => s.MediaId == _currentMedia.Id);
            if (playbackState == null)
            {
                playbackState = new PlaybackState { MediaId = _currentMedia.Id };
                db.PlaybackStates.Add(playbackState);
            }

            playbackState.PositionTicks = TimeSpan.FromSeconds(timeInSeconds).Ticks;
            playbackState.LastPlayedAt = DateTime.UtcNow;
            
            db.SaveChanges();
        }
    }

    public void Dispose()
    {
        _positionTimer?.Dispose();
        if (_mpvContext != IntPtr.Zero)
        {
            Libmpv.mpv_terminate_destroy(_mpvContext);
            _mpvContext = IntPtr.Zero;
        }
    }
}