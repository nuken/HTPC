using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.IO;
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
	private double _lastSyncedPosition = 0;
    private bool _hasMarkedWatched = false;
    private string _tempChapterFile = string.Empty;
	
	private CancellationTokenSource? _loadingWatchdogCts;
    private int _retryCount = 0;
    private const int MaxRetries = 2;
    private const int LoadingTimeoutSeconds = 5;
	
	// --- NEW: TUNER DIAGNOSTICS SWITCH ---
    private bool EnableTunerDiagnostics = false;

    private void LogTuner(string message)
    {
        if (EnableTunerDiagnostics)
        {
            // This will print directly to the command prompt terminal
            Console.WriteLine($"[TUNER] {DateTime.Now:HH:mm:ss.fff} [Thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}] {message}");
        }
    }
	private DateTime _lastPositionChangeTime = DateTime.UtcNow;
    private double _lastWatchdogPosition = -1;

    public double CurrentPosition { get; private set; }
    public double CurrentDuration { get; private set; }
    public event Action? OnMediaLoaded;
    private const int MPV_EVENT_END_FILE = 7;
    private const int MPV_EVENT_FILE_LOADED = 8;

    public event Action<double>? OnCommercialPrompt;

    public MpvPlaybackService(ILogger<MpvPlaybackService> logger, IServiceScopeFactory scopeFactory, ServerManagerService serverManager)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _serverManager = serverManager; 
        InitializeMpv();
    }
	
    private string GenerateCommercialChapters(List<double>? commercials)
    {
        if (commercials == null || commercials.Count == 0) return string.Empty;
        
        string tempPath = Path.Combine(Path.GetTempPath(), $"mpv_chapters_{Guid.NewGuid()}.txt");
        var sb = new StringBuilder();
        int chapterIndex = 1;
        
        if (commercials[0] > 0)
        {
            sb.AppendLine($"CHAPTER{chapterIndex:D2}=00:00:00.000");
            sb.AppendLine($"CHAPTER{chapterIndex:D2}NAME=Show");
            chapterIndex++;
        }

        for (int i = 0; i < commercials.Count; i++)
        {
            TimeSpan ts = TimeSpan.FromSeconds(commercials[i]);
            sb.AppendLine($"CHAPTER{chapterIndex:D2}={ts:hh\\:mm\\:ss\\.fff}");
            sb.AppendLine($"CHAPTER{chapterIndex:D2}NAME={(i % 2 == 0 ? "Commercial" : "Show")}");
            chapterIndex++;
        }

        File.WriteAllText(tempPath, sb.ToString());
        return tempPath;
    }

    private void InitializeMpv()
    {
        _logger.LogInformation("Initializing native libmpv engine...");

        _mpvContext = Libmpv.mpv_create();
        if (_mpvContext == IntPtr.Zero) throw new Exception("Failed to create libmpv context.");
        Libmpv.mpv_set_option_string(_mpvContext, "osd-bar", "no");
        
        Libmpv.mpv_set_option_string(_mpvContext, "osd-level", "0"); 
        Libmpv.mpv_set_option_string(_mpvContext, "terminal", "yes");
        Libmpv.mpv_set_option_string(_mpvContext, "msg-level", "all=info"); 
        Libmpv.mpv_set_option_string(_mpvContext, "vo", "gpu-next");
        Libmpv.mpv_set_option_string(_mpvContext, "gpu-api", "d3d11");
        Libmpv.mpv_set_option_string(_mpvContext, "hwdec", "auto-copy");
        
        Libmpv.mpv_set_option_string(_mpvContext, "cache", "yes");
        Libmpv.mpv_set_option_string(_mpvContext, "demuxer-max-bytes", "150000000"); 
        Libmpv.mpv_set_option_string(_mpvContext, "demuxer-readahead-secs", "10");
        
        Libmpv.mpv_set_option_string(_mpvContext, "cache-pause", "yes");
        Libmpv.mpv_set_option_string(_mpvContext, "cache-pause-initial", "yes");
        Libmpv.mpv_set_option_string(_mpvContext, "cache-secs", "1.5");
        Libmpv.mpv_set_option_string(_mpvContext, "cache-pause-wait", "1.5");

        // The lavf fastseek command ensures HLS playlists probe instantly without stalling
        Libmpv.mpv_set_option_string(_mpvContext, "demuxer-lavf-o", "fflags=+fastseek");

        // --- NEW: Aggressive Network Timeout Settings for HLS ---
        // Force the network connection to drop if the server takes longer than 5 seconds to reply
        Libmpv.mpv_set_option_string(_mpvContext, "network-timeout", "5"); 
        // Force HLS playlist parsing to timeout if the playlist stalls
        Libmpv.mpv_set_option_string(_mpvContext, "stream-lavf-o", "timeout=5000000"); 
        // Stop MPV from hanging infinitely if the demuxer gets confused by broken timestamps
        Libmpv.mpv_set_option_string(_mpvContext, "demuxer-max-back-bytes", "50M");

        Libmpv.mpv_set_option_string(_mpvContext, "video-sync", "display-resample");
        Libmpv.mpv_set_option_string(_mpvContext, "autosync", "30");
        Libmpv.mpv_set_option_string(_mpvContext, "deinterlace", "auto");

        Libmpv.mpv_set_option_string(_mpvContext, "slang", "eng,en,en-US");
        Libmpv.mpv_set_option_string(_mpvContext, "alang", "eng,en,en-US");
        Libmpv.mpv_set_option_string(_mpvContext, "sub-visibility", "yes"); 
        Libmpv.mpv_set_option_string(_mpvContext, "sid", "no"); 
        Libmpv.mpv_set_option_string(_mpvContext, "sub-font-size", "45");
        Libmpv.mpv_set_option_string(_mpvContext, "sub-color", "#FFFFFFFF"); 
        Libmpv.mpv_set_option_string(_mpvContext, "sub-border-color", "#FF000000"); 
        Libmpv.mpv_set_option_string(_mpvContext, "sub-border-size", "3");

        int result = Libmpv.mpv_initialize(_mpvContext);
        if (result < 0) throw new Exception($"Failed to initialize libmpv context. Error: {result}");

        _positionTimer = new Timer(SaveCurrentPosition, null, Timeout.Infinite, Timeout.Infinite);
        Libmpv.mpv_observe_property(_mpvContext, 1, "time-pos", 5);
		Libmpv.mpv_observe_property(_mpvContext, 2, "duration", 5);

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
        var ptr = HTPC.Core.Interop.Libmpv.mpv_get_property_string(_mpvContext, propertyName); 
        if (ptr != IntPtr.Zero)
        {
            string? result = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr);
            HTPC.Core.Interop.Libmpv.mpv_free(ptr);
            return result ?? "N/A";
        }
        return "N/A";
    }

    public void PlayMedia(MediaItem media, bool isRetry = false)
    {
        LogTuner($"PlayMedia requested for: {media.Title}. IsRetry: {isRetry}");

        if (!isRetry)
        {
            _retryCount = 0; 
        }

        if (_currentMedia != null && _currentMedia.Id != media.Id)
        {
            LogTuner($"Different media detected. Tearing down previous session: {_currentMedia.Id}");
            SaveCurrentPosition(null);
            _ = SyncProgressToServerAsync(_currentMedia.Id, CurrentDuration, CurrentPosition);
            _ = StopServerSessionAsync(_currentMedia); 
            
            CurrentPosition = 0;
            CurrentDuration = 0;
        }

        _currentMedia = media;
        _logger.LogInformation($"Loading media: {media.Title} (Attempt {_retryCount + 1})");

        _lastSyncedPosition = 0;
        _hasMarkedWatched = false;
        
        if (!string.IsNullOrEmpty(_tempChapterFile) && System.IO.File.Exists(_tempChapterFile))
        {
            try { System.IO.File.Delete(_tempChapterFile); } catch { }
        }

        _tempChapterFile = GenerateCommercialChapters(media.Commercials);
        if (!string.IsNullOrEmpty(_tempChapterFile))
        {
            Libmpv.mpv_set_property_string(_mpvContext, "chapter-file", _tempChapterFile);
        }

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = db.PlaybackStates.FirstOrDefault(s => s.MediaId == media.Id);
            
            if (media.StartOffset > 0)
            {
                Libmpv.mpv_set_option_string(_mpvContext, "start", media.StartOffset.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (state != null && state.PositionTicks > 0)
            {
                double startSeconds = TimeSpan.FromTicks(state.PositionTicks).TotalSeconds;
                Libmpv.mpv_set_option_string(_mpvContext, "start", startSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                Libmpv.mpv_set_option_string(_mpvContext, "start", "0");
            }
        }

        string streamUrl = media.StreamUrl ?? media.Path;
        
        if (streamUrl.Contains(".m3u8"))
        {
            var server = _serverManager.GetActiveServer();
            bool isLocal = server != null && (server.IpAddress.StartsWith("192.168.") || server.IpAddress.StartsWith("10.") || server.IpAddress.StartsWith("127."));
            
            if (isLocal) 
            {
                streamUrl += streamUrl.Contains("?") ? "&abr=false&vcodec=copy&acodec=copy" : "?abr=false&vcodec=copy&acodec=copy";
            }
            else
            {
                streamUrl += streamUrl.Contains("?") ? "&abr=false&vcodec=h264&acodec=aac&vbitrate=4000&resolution=720" : "?abr=false&vcodec=h264&acodec=aac&vbitrate=4000&resolution=720";
            }
        }

        if (_loadingWatchdogCts != null)
        {
            LogTuner("Cancelling previous loading watchdog.");
            _loadingWatchdogCts.Cancel();
            _loadingWatchdogCts.Dispose();
        }
        _loadingWatchdogCts = new CancellationTokenSource();

        LogTuner($"Offloading native MPV loadfile command to background task...");
        
        Task.Run(() =>
        {
            LogTuner($"Executing loadfile: {streamUrl}");
            Libmpv.mpv_command_string(_mpvContext, $"loadfile \"{streamUrl}\"");

            if (!string.IsNullOrWhiteSpace(media.SubtitleUrl))
            {
                Libmpv.mpv_command_string(_mpvContext, $"sub-add \"{media.SubtitleUrl}\"");
            }
            LogTuner($"Native loadfile commands dispatched to engine.");
        });

        _ = StartLoadingWatchdogAsync(_loadingWatchdogCts.Token, media);
        _positionTimer?.Change(5000, 5000);
    }
    
    private void EventLoop()
    {
        LogTuner("Native MPV Event Loop thread started.");
        
        while (!_isDisposed)
        {
            IntPtr eventPtr = Libmpv.mpv_wait_event(_mpvContext, 0.5);
            if (eventPtr == IntPtr.Zero) continue;

            var ev = Marshal.PtrToStructure<Libmpv.mpv_event>(eventPtr);
            
            // 1. Stream loaded successfully
            if (ev.event_id == MPV_EVENT_FILE_LOADED)
            {
                LogTuner("MPV_EVENT_FILE_LOADED triggered! Video is successfully buffering/playing.");
                _logger.LogInformation("Stream loaded successfully in MPV. Cancelling watchdog.");
                _loadingWatchdogCts?.Cancel();
                OnMediaLoaded?.Invoke(); 
            }

            // 2. Stream ended or failed to open
            if (ev.event_id == MPV_EVENT_END_FILE)
            {
                LogTuner("MPV_EVENT_END_FILE triggered. Stream closed natively by MPV.");
                _loadingWatchdogCts?.Cancel();
                
                var media = _currentMedia;
                if (media != null)
                {
                    if (_retryCount < MaxRetries && CurrentPosition <= 0) // Use CurrentPosition here
                    {
                        _logger.LogWarning("File ended immediately with 0 duration. Triggering failure recovery.");
                        _ = Task.Run(() => HandlePlaybackFailure(media));
                        continue;
                    }

                    double duration = CurrentDuration; // Use CurrentDuration here
                    _ = SyncProgressToServerAsync(media.Id, duration, duration);
                }
            }
            
            // 3. Property changes (Position & Duration tracking)
            if (ev.event_id == 22) 
            {
                var prop = Marshal.PtrToStructure<Libmpv.mpv_event_property>(ev.data);
                if (prop.data != IntPtr.Zero)
                {
                    if (prop.name == "time-pos")
                    {
                        double timePos = Marshal.PtrToStructure<double>(prop.data);
                        
                        // NEW: If the position moves forward, reset the stagnation clock!
                        if (timePos != _lastWatchdogPosition)
                        {
                            _lastWatchdogPosition = timePos;
                            _lastPositionChangeTime = DateTime.UtcNow;
                        }

                        CurrentPosition = timePos;
                        
                        EvaluateCommercialBoundaries(timePos);

                        double duration = CurrentDuration;
                        bool isWatchedThreshold = duration > 0 && (timePos >= duration - 180 || (timePos / duration) >= 0.9);
                        
                        if (Math.Abs(timePos - _lastSyncedPosition) >= 20 || (isWatchedThreshold && !_hasMarkedWatched))
                        {
                            _lastSyncedPosition = timePos;
                            var media = _currentMedia;
                            if (media != null)
                            {
                                _ = SyncProgressToServerAsync(media.Id, duration, timePos);
                            }
                        }
                    }
                    else if (prop.name == "duration")
                    {
                        CurrentDuration = Marshal.PtrToStructure<double>(prop.data); // Cache it instantly
                    }
                }
            }
        }
    }
		
    private void EvaluateCommercialBoundaries(double currentSeconds)
    {
        var media = _currentMedia;
        if (media?.Commercials == null || media.Commercials.Count < 2) return;

        var prefs = PreferencesManager.Load();
        if (prefs.CommercialSkipMode == 0) return;

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
	
	private async Task StartLoadingWatchdogAsync(CancellationToken token, MediaItem media)
    {
        try
        {
            // Initial Load Check (5 seconds)
            await Task.Delay(TimeSpan.FromSeconds(LoadingTimeoutSeconds), token);
            
            // If we get here, the initial load failed.
            _logger.LogWarning($"Initial stream load timeout for {media.Title}. Attempting reconnect...");
            HandlePlaybackFailure(media);
            return; // Exit out, the retry will start a new watchdog
        }
        catch (TaskCanceledException)
        {
            // Initial load succeeded! Fall through to start the continuous mid-stream watchdog.
        }

        // --- NEW: Mid-Stream Stagnation Watchdog ---
        // This loop runs continuously in the background while the video plays
        try
        {
            _lastPositionChangeTime = DateTime.UtcNow; // Reset clock before starting
            
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, token); // Check every 2 seconds

                string isPaused = GetMpvProperty("pause");
                if (isPaused == "yes")
                {
                    // If the user intentionally paused it, don't kill the stream!
                    _lastPositionChangeTime = DateTime.UtcNow; 
                    continue;
                }

                // If the video is supposed to be playing, but the position hasn't changed in 5 seconds
                if ((DateTime.UtcNow - _lastPositionChangeTime).TotalSeconds > 5)
                {
                    _logger.LogWarning($"Stream decoder freeze detected on {media.Title}. Forcing reconnect...");
                    
                    // We must fire this on a background thread so we don't lock up the watchdog itself
                    _ = Task.Run(() => HandlePlaybackFailure(media));
                    break; // Exit this watchdog loop
                }
            }
        }
        catch (TaskCanceledException)
        {
            // The video finished normally or the user hit stop. Watchdog shutting down cleanly.
        }
    }

private void HandlePlaybackFailure(MediaItem media)
{
    if (_retryCount < MaxRetries)
    {
        _retryCount++;
        _logger.LogInformation($"Executing retry {_retryCount} for channel: {media.Title}");
        
        // Stop current internal playback state and kill hanging transcode sessions
        Stop(); 
        
        // Re-route back to PlayMedia as a retry
        PlayMedia(media, isRetry: true);
    }
    else
    {
        _logger.LogError($"Failed to tune channel {media.Title} after {MaxRetries} retries.");
        Stop();
    }
}
    
    private async Task SyncProgressToServerAsync(string fileId, double duration, double position)
    {
        if (string.IsNullOrEmpty(fileId)) return;
        if (position < 60) return;

        var server = _serverManager.GetActiveServer();
        if (server == null) return;

        string baseUrl = $"http://{server.IpAddress}:{server.Port}";

        try
        {
            if (duration > 0 && (position >= duration - 180 || (position / duration) >= 0.9))
            {
                if (!_hasMarkedWatched)
                {
                    _logger.LogInformation($"Playback reached Watched threshold. Marking {fileId} as Watched.");
                    await _httpClient.PutAsync($"{baseUrl}/dvr/files/{fileId}/watch", new System.Net.Http.StringContent(""));
                    _hasMarkedWatched = true;
                }
            }
            else
            {
                _logger.LogInformation($"Saving progress for {fileId} at {position} seconds.");
                await _httpClient.PutAsync($"{baseUrl}/dvr/files/{fileId}/playback_time/{(int)position}", new System.Net.Http.StringContent(""));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to sync playback progress: {ex.Message}");
        }
    }
    
    private bool _isAnimeModeActive = false;

    public void ApplyUpscalerSettings()
    {
        var prefs = PreferencesManager.Load();

        Libmpv.mpv_set_property_string(_mpvContext, "glsl-shaders", "");
        Libmpv.mpv_set_option_string(_mpvContext, "vo", "gpu-next");
        Libmpv.mpv_set_option_string(_mpvContext, "scale", "spline36");

        if (!prefs.EnableUpscaling) return;

        string shaderPath = string.Empty;
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        if (_isAnimeModeActive)
        {
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
        LogTuner("Stop() called. Cleaning up timers and saving position.");
        _positionTimer?.Change(Timeout.Infinite, Timeout.Infinite); 
        SaveCurrentPosition(null); 

        if (_currentMedia != null)
        {
            double duration = CurrentDuration;
            double position = CurrentPosition;
            
            _ = SyncProgressToServerAsync(_currentMedia.Id, duration, position);
            _ = StopServerSessionAsync(_currentMedia); 
        }

        LogTuner("Dispatching native MPV stop command to background task...");
        Task.Run(() => 
        {
            Libmpv.mpv_command_string(_mpvContext, "stop");
            LogTuner("Native MPV stop command executed.");
        }); 

        _currentMedia = null;
        CurrentPosition = 0; 
        CurrentDuration = 0; 
    }
    
    private async Task StopServerSessionAsync(MediaItem media)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null || string.IsNullOrEmpty(media.Id)) return;

        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";

        try
        {
            string json = await _httpClient.GetStringAsync($"{baseUrl}/api/v1/sessions");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var session in doc.RootElement.EnumerateArray())
                {
                    if (session.TryGetProperty("ID", out var idProp))
                    {
                        string sessionId = idProp.GetString() ?? "";
                        
                        // Match VOD (file1234) OR Live TV (ch1234-)
                        if (sessionId.Contains($"file{media.Id}") || sessionId.StartsWith($"ch{media.Id}-"))
                        {
                            await _httpClient.DeleteAsync($"{baseUrl}/api/v1/sessions/{sessionId}");
                            _logger.LogInformation($"Killed hanging transcoder session: {sessionId}");
                        }
                    }
                }
            }
        }
        catch { }
    }
	
    public void SetVolume(int volume)
    {
        if (_mpvContext != IntPtr.Zero)
        {
            Core.Interop.Libmpv.mpv_set_property_string(_mpvContext, "volume", volume.ToString());
        }
    }
    
    public double GetDuration() => CurrentDuration;

    public double GetPosition() => CurrentPosition;

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
	
	public void ToggleMute()
    {
        if (_mpvContext == IntPtr.Zero) return;
        Libmpv.mpv_command_string(_mpvContext, "cycle mute");
    }
	
    private void SaveCurrentPosition(object? state)
    {
        var media = _currentMedia;
        if (media == null) return;

        double timeInSeconds = CurrentPosition;
        if (timeInSeconds <= 0) return; 

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
        _isDisposed = true; 

        _positionTimer?.Dispose();

        if (_mpvContext != IntPtr.Zero)
        {
            Libmpv.mpv_command_string(_mpvContext, "quit");

            if (_eventLoopThread != null && _eventLoopThread.IsAlive)
            {
                _eventLoopThread.Join(1000); 
            }

            Libmpv.mpv_terminate_destroy(_mpvContext);
            _mpvContext = IntPtr.Zero;
        }
    }
}