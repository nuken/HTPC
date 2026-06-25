using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
    
    private readonly ServerManagerService _serverManager;
    private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();
    
    private IntPtr _mpvContext;    
    private Timer? _positionTimer;
    private MediaItem? _currentMedia;
    private Thread? _eventLoopThread;
    private bool _isDisposed = false;
    private HashSet<int> _disabledCommercialBlocks = new HashSet<int>();

    // This event lets the UI know it should show the "Click to Skip" button overlay
    // It passes the 'targetTime' so the UI knows where to jump if clicked.
    public event Action<double>? OnCommercialPrompt;

    public MpvPlaybackService(ILogger<MpvPlaybackService> logger, IServiceScopeFactory scopeFactory, ServerManagerService serverManager)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _serverManager = serverManager; 
        InitializeMpv();
    }

    private void InitializeMpv()
    {
        _logger.LogInformation("Initializing native libmpv engine...");

        _mpvContext = Libmpv.mpv_create();
        if (_mpvContext == IntPtr.Zero) throw new Exception("Failed to create libmpv context.");
        
        // --- FIX: Prevent Subtitles from Auto-Playing ---
        Libmpv.mpv_set_option_string(_mpvContext, "sub-visibility", "no");

        Libmpv.mpv_set_option_string(_mpvContext, "osd-bar", "no");
        Libmpv.mpv_set_option_string(_mpvContext, "terminal", "yes");
        Libmpv.mpv_set_option_string(_mpvContext, "msg-level", "all=info"); 
        Libmpv.mpv_set_option_string(_mpvContext, "vo", "gpu-next");
        Libmpv.mpv_set_option_string(_mpvContext, "gpu-api", "d3d11");
        Libmpv.mpv_set_option_string(_mpvContext, "hwdec", "auto-copy"); // Keeping your lighter hardware setting
        
        // --- PRE-BUFFER CACHE SETTINGS ---
        Libmpv.mpv_set_option_string(_mpvContext, "cache", "yes");
        Libmpv.mpv_set_option_string(_mpvContext, "demuxer-max-bytes", "150000000");
        Libmpv.mpv_set_option_string(_mpvContext, "demuxer-readahead-secs", "8");
        
        // --- FIX: Prevent Hard Freezes on Network Dips ---
        Libmpv.mpv_set_option_string(_mpvContext, "cache-pause", "no");
		Libmpv.mpv_set_option_string(_mpvContext, "pause", "no");

        int result = Libmpv.mpv_initialize(_mpvContext);
        if (result < 0) throw new Exception($"Failed to initialize libmpv context. Error: {result}");

        _positionTimer = new Timer(SaveCurrentPosition, null, Timeout.Infinite, Timeout.Infinite);
        
        // Format 5 is MPV_FORMAT_DOUBLE. We tell MPV to notify us whenever time-pos changes.
        Libmpv.mpv_observe_property(_mpvContext, 1, "time-pos", 5);

        _eventLoopThread = new Thread(EventLoop);
        _eventLoopThread.IsBackground = true;
        _eventLoopThread.Name = "MpvEventLoop";
        _eventLoopThread.Start();
    }

    public void AttachToWindow(IntPtr hwnd)
    {
        string windowIdStr = hwnd.ToInt64().ToString();
        Libmpv.mpv_set_property_string(_mpvContext, "wid", windowIdStr);
    }
    
    public string GetMpvProperty(string propertyName)
    {
        // Safely fetch the property string from MPV
        var ptr = HTPC.Core.Interop.Libmpv.mpv_get_property_string(_mpvContext, propertyName); 
        if (ptr != IntPtr.Zero)
        {
            string? result = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr);
            HTPC.Core.Interop.Libmpv.mpv_free(ptr);
            return result ?? "N/A";
        }
        return "N/A";
    }

    public void PlayMedia(MediaItem media)
    {
        _currentMedia = media;
        _logger.LogInformation($"Loading media: {media.Title}");

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = db.PlaybackStates.FirstOrDefault(s => s.MediaId == media.Id);
            
            // Priority 1: Channels API explicitly gave us a StartOffset (from the Up Next queue)
            if (media.StartOffset > 0)
            {
                Libmpv.mpv_set_option_string(_mpvContext, "start", media.StartOffset.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _logger.LogInformation($"Up Next: Resuming at {media.StartOffset} seconds.");
            }
            // Priority 2: Otherwise, check the local database like normal
            else if (state != null && state.PositionTicks > 0)
            {
                double startSeconds = TimeSpan.FromTicks(state.PositionTicks).TotalSeconds;
                Libmpv.mpv_set_option_string(_mpvContext, "start", startSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _logger.LogInformation($"Resuming at {startSeconds} seconds.");
            }
            // Fallback: Start from the beginning
            else
            {
                Libmpv.mpv_set_option_string(_mpvContext, "start", "0");
            }
        }

        Libmpv.mpv_command_string(_mpvContext, $"loadfile \"{media.StreamUrl}\"");
        _positionTimer?.Change(5000, 5000);
    }
    
    private void EventLoop()
    {
        while (!_isDisposed)
        {
            IntPtr eventPtr = Libmpv.mpv_wait_event(_mpvContext, 0.5);
            if (eventPtr == IntPtr.Zero) continue;

            var ev = Marshal.PtrToStructure<Libmpv.mpv_event>(eventPtr);
            
            // event_id 7 == MPV_EVENT_END_FILE (Video finished naturally)
            if (ev.event_id == 7)
            {
                var media = _currentMedia;
                if (media != null)
                {
                    double duration = GetDuration();
                    _ = SyncProgressToServerAsync(media.Id, duration, duration);
                }
            }
            
            // event_id 22 == MPV_EVENT_PROPERTY_CHANGE
            if (ev.event_id == 22) 
            {
                var prop = Marshal.PtrToStructure<Libmpv.mpv_event_property>(ev.data);
                if (prop.name == "time-pos" && prop.data != IntPtr.Zero)
                {
                    double timePos = Marshal.PtrToStructure<double>(prop.data);
                    EvaluateCommercialBoundaries(timePos);
                }
            }
        }
    }

    private void EvaluateCommercialBoundaries(double currentSeconds)
    {
        var media = _currentMedia;
        if (media?.Commercials == null || media.Commercials.Count < 2) return;

        var prefs = PreferencesManager.Load();
        if (prefs.CommercialSkipMode == 0) return; // Mode 0 = Off

        var comms = media.Commercials;
        
        for (int i = 0; i < comms.Count - 1; i += 2)
        {
            double start = comms[i];
            double end = comms[i + 1];

            if (currentSeconds < start - 5)
            {
                _disabledCommercialBlocks.Remove(i);
            }

            if (currentSeconds >= start && currentSeconds < end)
            {
                if (_disabledCommercialBlocks.Contains(i)) continue; 

                if (prefs.CommercialSkipMode == 2) 
                {
                    _logger.LogInformation($"Auto-skipping commercial block: {start}s to {end}s");
                    _disabledCommercialBlocks.Add(i); 
                    SeekAbsolute(end); 
                }
                else if (prefs.CommercialSkipMode == 1) 
                {
                    _disabledCommercialBlocks.Add(i); 
                    OnCommercialPrompt?.Invoke(end);  
                }
            }
        }
    }
    
    private async Task SyncProgressToServerAsync(string fileId, double duration, double position)
    {
        if (string.IsNullOrEmpty(fileId)) return;

        // Guard 1: The "Accidental Click" (Under 60 seconds)
        if (position < 60)
        {
            _logger.LogInformation("Playback under 60 seconds. Ignoring progress sync.");
            return;
        }

        var server = _serverManager.GetActiveServer();
        if (server == null) return;

        string baseUrl = $"http://{server.IpAddress}:{server.Port}";

        try
        {
            // Guard 2: The "Credits" Threshold (Within 3 minutes of the end)
            if (duration > 0 && position >= duration - 180)
            {
                _logger.LogInformation($"Playback within 3 minutes of end. Marking {fileId} as Watched.");
                await _httpClient.PutAsync($"{baseUrl}/dvr/files/{fileId}/watch", new System.Net.Http.StringContent(""));
            }
            else
            {
                // Standard progress save
                _logger.LogInformation($"Saving progress for {fileId} at {position} seconds.");
                await _httpClient.PutAsync($"{baseUrl}/dvr/files/{fileId}/playback_time/{position}", new System.Net.Http.StringContent(""));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to sync playback progress to Channels DVR: {ex.Message}");
        }
    }
    
    private bool _isAnimeModeActive = false;

    public void ApplyUpscalerSettings()
    {
        var prefs = PreferencesManager.Load();

        // --- FIX: Use PROPERTY to allow hot-swapping active shaders ---
        Libmpv.mpv_set_property_string(_mpvContext, "glsl-shaders", "");

        Libmpv.mpv_set_option_string(_mpvContext, "vo", "gpu-next");
        Libmpv.mpv_set_option_string(_mpvContext, "scale", "spline36");

        if (!prefs.EnableUpscaling) return;

        string shaderPath = string.Empty;
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        if (_isAnimeModeActive)
        {
            // --- FIX: Properly chained Anime4K algorithm ---
            string restoreShader = System.IO.Path.Combine(baseDir, "Shaders", "Anime4K_Restore_CNN_M.glsl");
            string upscaleShader = System.IO.Path.Combine(baseDir, "Shaders", "Anime4K_Upscale_CNN_x2_M.glsl");
            
            if (System.IO.File.Exists(restoreShader) && System.IO.File.Exists(upscaleShader))
            {
                shaderPath = $"{restoreShader};{upscaleShader}"; 
            }
        }
        else if (prefs.UpscalerPreset == "RAVU")
        {
            shaderPath = System.IO.Path.Combine(baseDir, "Shaders", "ravu-zoom-r3.glsl");
        }
        else if (prefs.UpscalerPreset == "ArtCNN")
        {
            shaderPath = System.IO.Path.Combine(baseDir, "Shaders", "ArtCNN_C4F32.glsl");
        }

        if (!string.IsNullOrEmpty(shaderPath))
        {
            // --- FIX: Use PROPERTY to inject into the active video stream ---
            Libmpv.mpv_set_property_string(_mpvContext, "glsl-shaders", shaderPath);
        }
    }

    public bool ToggleAnimeMode()
    {
        _isAnimeModeActive = !_isAnimeModeActive;
        ApplyUpscalerSettings(); 
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

        if (_currentMedia != null)
        {
            double duration = GetDuration();
            double position = GetPosition();
            
            _ = SyncProgressToServerAsync(_currentMedia.Id, duration, position);
        }

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
        var media = _currentMedia;
        if (media == null || _mpvContext == IntPtr.Zero) return;

        int result = Libmpv.mpv_get_property(_mpvContext, "time-pos", 5, out double timeInSeconds);
        if (result < 0) return; 

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var playbackState = db.PlaybackStates.FirstOrDefault(s => s.MediaId == media.Id);
            if (playbackState == null)
            {
                playbackState = new PlaybackState { MediaId = media.Id };
                db.PlaybackStates.Add(playbackState);
            }

            playbackState.PositionTicks = TimeSpan.FromSeconds(timeInSeconds).Ticks;
            playbackState.LastPlayedAt = DateTime.UtcNow;
            
            db.SaveChanges();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true; // Signals the while-loop to stop

        _positionTimer?.Dispose();

        if (_mpvContext != IntPtr.Zero)
        {
            // --- FIX: Safe thread teardown ---
            // Send an explicit quit command to unblock mpv_wait_event gracefully
            Libmpv.mpv_command_string(_mpvContext, "quit");

            // Wait up to 1 second for the event loop thread to finish processing and exit
            if (_eventLoopThread != null && _eventLoopThread.IsAlive)
            {
                _eventLoopThread.Join(1000); 
            }

            // Safely destroy the context
            Libmpv.mpv_terminate_destroy(_mpvContext);
            _mpvContext = IntPtr.Zero;
        }
    }
}