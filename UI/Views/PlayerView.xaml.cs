using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Models;
using HTPC.Services;
using HTPC.UI.Controls;
using HTPC.UI.Windows;

namespace HTPC.UI.Views;

public partial class PlayerView : UserControl
{
    public event EventHandler? OnBackRequested;

    private readonly MpvPlaybackService _mpvService;
    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    private readonly MpvVideoHost _videoHost;
    
    private PlayerOverlayWindow? _overlayWindow;
    private bool _isMpvAttached = false;
	private Point _lastMousePosition;
	private DateTime _playbackStartTime = DateTime.MinValue;
	private DateTime _lastLeftPress = DateTime.MinValue;
    private DateTime _lastRightPress = DateTime.MinValue;
    private readonly TimeSpan _doubleTapThreshold = TimeSpan.FromMilliseconds(400);
	// --- BINGE WATCH QUEUE STATE ---
    private System.Collections.Generic.List<MediaItem> _playbackQueue = new();
    private int _currentQueueIndex = 0;
    private bool _isTransitioning = false;


    public PlayerView(MpvPlaybackService mpvService, MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _mpvService = mpvService;
		
		this.Loaded += PlayerView_Loaded;
		
        _libraryService = libraryService;
        _serverManager = serverManager;

        _videoHost = new MpvVideoHost();
        VideoSurface.Children.Add(_videoHost);

        this.Loaded += OnLoaded;
        
        // Sync overlay when the player changes size
        this.SizeChanged += (s, e) => SyncOverlayBounds();
		this.PreviewKeyDown += PlayerView_PreviewKeyDown;
        this.PreviewMouseMove += PlayerView_PreviewMouseMove;
    }
	
	public void TriggerInstantReplay(int? seconds = null) 
{
    // Ensure this command only goes through if the video is actually playing
    if (this.Visibility != Visibility.Visible) return;
    
    _mpvService.TriggerInstantReplay(seconds);
}

public void JumpToLiveEdge()
{
    if (this.Visibility != Visibility.Visible) return;

    _mpvService.JumpToLiveEdge();
}

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isMpvAttached)
        {
            _mpvService.AttachToWindow(_videoHost.Handle);
            _isMpvAttached = true;
        }
        SyncOverlayBounds();
    }

    // Standard entry point for single files
    public void StartPlayback(MediaItem media)
    {
        StartPlaybackQueue(new System.Collections.Generic.List<MediaItem> { media }, 0);
    }

    // Dynamic entry point for serial binge-watching
    public void StartPlaybackQueue(System.Collections.Generic.List<MediaItem> queue, int startIndex)
    {
        _playbackQueue = queue ?? new System.Collections.Generic.List<MediaItem>();
        _currentQueueIndex = startIndex;
        _isTransitioning = false;

        if (_playbackQueue.Count == 0 || _currentQueueIndex >= _playbackQueue.Count) return;

        MediaItem currentItem = _playbackQueue[_currentQueueIndex];

        // --- NEW: TUNER LOGGING ---
        System.Diagnostics.Debug.WriteLine($"\n[TUNER] {DateTime.Now:HH:mm:ss.fff} ======================================");
        System.Diagnostics.Debug.WriteLine($"[TUNER] {DateTime.Now:HH:mm:ss.fff} UI requested tune for: {currentItem.Title}");

        _mpvService.PlayMedia(currentItem);
        
        System.Diagnostics.Debug.WriteLine($"[TUNER] {DateTime.Now:HH:mm:ss.fff} Handoff to MPV Service complete.");

        if (_overlayWindow != null)
        {
            _overlayWindow.Close();
        }

        _overlayWindow = new PlayerOverlayWindow(_mpvService, _libraryService, _serverManager)
        {
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        _overlayWindow.OnBackRequested += (s, e) =>
        {
            StopPlayback();
            OnBackRequested?.Invoke(this, EventArgs.Empty);
        };
        
        // NEW: Listen for the overlay telling us it's time to play the next item
        _overlayWindow.OnPlayNextInQueue += OverlayWindow_OnPlayNextInQueue;

        // Determine if there is a next item in our queue
        MediaItem? nextItem = (_currentQueueIndex + 1 < _playbackQueue.Count) 
            ? _playbackQueue[_currentQueueIndex + 1] 
            : null;

        // Pass the current AND next item to the overlay
        _overlayWindow.InitializeMedia(currentItem, nextItem);
        _overlayWindow.Show();
        
        // --- NEW: Instantly hide cursor and prime the anti-jitter tracker ---
        Mouse.OverrideCursor = Cursors.None;
        _lastMousePosition = Mouse.GetPosition(this);
        _playbackStartTime = DateTime.UtcNow;
        
        Application.Current.Dispatcher.BeginInvoke(new Action(SyncOverlayBounds), DispatcherPriority.ContextIdle);

        _overlayWindow.Activate();
        _overlayWindow.Focus();
    }

    private void OverlayWindow_OnPlayNextInQueue(object? sender, MediaItem nextItem)
    {
        // Prevent double-firing if the user clicks "Play Next" right as the video ends
        if (_isTransitioning) return;
        _isTransitioning = true;
        
        // Advance the queue and fire up the next video seamlessly!
        _currentQueueIndex++;
        StartPlaybackQueue(_playbackQueue, _currentQueueIndex);
    }
	
	private void PlayerView_PreviewKeyDown(object sender, KeyEventArgs e)
{
    Mouse.OverrideCursor = Cursors.None;
    var command = HTPC.Core.Input.InputMapper.GetCommand(e.Key);
    var now = DateTime.UtcNow;

    if (e.Key == Key.I)
    {
        TriggerInstantReplay(20); 
        e.Handled = true;
        return;
    }

    if (command == HTPC.Core.Input.HtpcCommand.Left || command == HTPC.Core.Input.HtpcCommand.SkipBackward)
    {
        if ((now - _lastLeftPress) < _doubleTapThreshold)
        {
            TriggerInstantReplay(20);
            _lastLeftPress = DateTime.MinValue; 
        }
        else
        {
            _mpvService.SeekRelative(-15); 
            _lastLeftPress = now;
        }
        e.Handled = true;
        return;
    }
    else if (command == HTPC.Core.Input.HtpcCommand.Right || command == HTPC.Core.Input.HtpcCommand.SkipForward)
    {
        if ((now - _lastRightPress) < _doubleTapThreshold)
        {
            JumpToLiveEdge();
            _lastRightPress = DateTime.MinValue;
        }
        else
        {
            _mpvService.SeekRelative(15);
            _lastRightPress = now;
        }
        e.Handled = true;
        return;
    }
    else if (command == HTPC.Core.Input.HtpcCommand.PlayPause)
    {
        JumpToLiveEdge();
    }
}

    private void PlayerView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // Grace Period: Ignore all phantom WPF layout mouse moves for 1 second after opening
        if ((DateTime.UtcNow - _playbackStartTime).TotalMilliseconds < 1000) return;

        Point currentPosition = e.GetPosition(this);

        // Only restore the cursor if the mouse physically moved more than 2 pixels
        if (Math.Abs(currentPosition.X - _lastMousePosition.X) > 2 || 
            Math.Abs(currentPosition.Y - _lastMousePosition.Y) > 2)
        {
            _lastMousePosition = currentPosition;
            Mouse.OverrideCursor = null; 
        }
    }

    // --- NEW: Feral Browser Launcher ---
    private void LaunchExternalBrowser(string url)
    {
        try
        {
            if (url.Contains("netflix.com") || url.Contains("disneyplus.com") || url.Contains("youtube.com"))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { 
                    FileName = "msedge", 
                    Arguments = $"--app=\"{url}\" --start-fullscreen", 
                    UseShellExecute = true 
                });
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch { }
    }

    private void SyncOverlayBounds()
    {
        // Don't calculate if the window isn't fully ready yet
        if (_overlayWindow == null || !this.IsLoaded) return;
        
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null) return; 

        try 
        {
            // 1. Get the physical pixel location of BOTH the Top-Left and Bottom-Right corners
            Point physicalTopLeft = this.PointToScreen(new Point(0, 0));
            Point physicalBottomRight = this.PointToScreen(new Point(this.ActualWidth, this.ActualHeight));

            // 2. Convert physical pixels back to WPF Device-Independent Pixels (DIPs)
            var transform = source.CompositionTarget.TransformFromDevice;
            Point dipTopLeft = transform.Transform(physicalTopLeft);
            Point dipBottomRight = transform.Transform(physicalBottomRight);

            // 3. Calculate the true scaled width and height on the monitor
            double trueScaledWidth = dipBottomRight.X - dipTopLeft.X;
            double trueScaledHeight = dipBottomRight.Y - dipTopLeft.Y;

            // 4. Apply exact coordinates to the overlay, perfectly matching the scaled video!
            _overlayWindow.Left = dipTopLeft.X;
            _overlayWindow.Top = dipTopLeft.Y;
            _overlayWindow.Width = trueScaledWidth;
            _overlayWindow.Height = trueScaledHeight;
        }
        catch 
        { 
            // Failsafe catch block just in case of rapid screen swapping
        }
    }

    public void StopPlayback()
    {
        var mainWindow = Application.Current.MainWindow;
        if (mainWindow != null)
        {
            mainWindow.LocationChanged -= MainWindow_BoundsChanged;
            mainWindow.SizeChanged -= MainWindow_BoundsChanged;
        }

        _mpvService.Stop();
        
        if (_overlayWindow != null)
        {
            _overlayWindow.Close();
            _overlayWindow = null;
        }

        Mouse.OverrideCursor = null; 
    }
	
	// --- NEW: OVERLAY MOVEMENT TRACKING ---
    private void PlayerView_Loaded(object sender, RoutedEventArgs e)
    {
        Window mainWindow = Window.GetWindow(this);
        if (mainWindow != null)
        {
            // Unsubscribe first to prevent duplicate fires if the view is reloaded
            mainWindow.LocationChanged -= MainWindow_BoundsChanged;
            mainWindow.SizeChanged -= MainWindow_BoundsChanged;
            
            // Subscribe to dragging and resizing
            mainWindow.LocationChanged += MainWindow_BoundsChanged;
            mainWindow.SizeChanged += MainWindow_BoundsChanged;
        }
    }

    private void MainWindow_BoundsChanged(object? sender, EventArgs e)
    {
        // If the video is playing and the overlay exists, instantly realign it!
        if (_overlayWindow != null && _overlayWindow.IsVisible)
        {
            SyncOverlayBounds();
        }
    }
}