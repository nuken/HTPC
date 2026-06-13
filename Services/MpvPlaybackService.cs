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
		
		// 1. Force the network cache to be active
        Libmpv.mpv_set_option_string(_mpvContext, "cache", "yes");
        
        // 2. Increase the RAM cache size to ~150MB (prevents high-bitrate starvation)
        Libmpv.mpv_set_option_string(_mpvContext, "demuxer-max-bytes", "150000000");
        
        // 3. Force MPV to buffer up to 10 seconds ahead into the future
        Libmpv.mpv_set_option_string(_mpvContext, "demuxer-readahead-secs", "10");
        
        // 4. Force MPV to pause and wait for the cache to initially fill BEFORE playing
        Libmpv.mpv_set_option_string(_mpvContext, "cache-pause", "yes");

        int result = Libmpv.mpv_initialize(_mpvContext);
        if (result < 0) throw new Exception($"Failed to initialize libmpv context. Error: {result}");

        // Setup the background timer (but don't start it yet)
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

        // 1. Check the database for a resume timestamp
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = db.PlaybackStates.FirstOrDefault(s => s.MediaId == media.Id);
            
            if (state != null && state.PositionTicks > 0)
            {
                // Convert .NET Ticks back to seconds for the mpv engine
                double startSeconds = TimeSpan.FromTicks(state.PositionTicks).TotalSeconds;
                Libmpv.mpv_set_option_string(_mpvContext, "start", startSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _logger.LogInformation($"Resuming at {startSeconds} seconds.");
            }
            else
            {
                Libmpv.mpv_set_option_string(_mpvContext, "start", "0");
            }
        }

        // 2. Start the video
        Libmpv.mpv_command_string(_mpvContext, $"loadfile \"{media.StreamUrl}\"");

        // 3. Start the background timer to save position every 5 seconds
        _positionTimer?.Change(5000, 5000);
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
            // Tell the MPV engine to change the volume property
            Core.Interop.Libmpv.mpv_set_property_string(_mpvContext, "volume", volume.ToString());
        }
    }
	
	// --- TIMELINE & SEEKING ---
    
    public double GetDuration()
    {
        if (_mpvContext == IntPtr.Zero) return 0;
        // format 5 = MPV_FORMAT_DOUBLE
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
        // Seek to an exact timestamp
        Libmpv.mpv_command_string(_mpvContext, $"seek {seconds} absolute");
    }

    public void SeekRelative(double seconds)
    {
        if (_mpvContext == IntPtr.Zero) return;
        // Skip forward or backward
        Libmpv.mpv_command_string(_mpvContext, $"seek {seconds} relative");
    }

    // --- CLOSED CAPTIONS ---

    public void CycleSubtitles()
    {
        if (_mpvContext == IntPtr.Zero) return;
        // Cycles through available subtitle tracks (and 'none')
        Libmpv.mpv_command_string(_mpvContext, "cycle sub");
    }

    private void SaveCurrentPosition(object? state)
    {
        if (_currentMedia == null || _mpvContext == IntPtr.Zero) return;

        // format 5 = MPV_FORMAT_DOUBLE
        int result = Libmpv.mpv_get_property(_mpvContext, "time-pos", 5, out double timeInSeconds);
        if (result < 0) return; // Video might be buffering or ending, skip saving

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