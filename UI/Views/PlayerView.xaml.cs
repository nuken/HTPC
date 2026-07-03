using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input; // <-- NEEDED FOR THE MOUSE FIX
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

    public PlayerView(MpvPlaybackService mpvService, MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _mpvService = mpvService;
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isMpvAttached)
        {
            _mpvService.AttachToWindow(_videoHost.Handle);
            _isMpvAttached = true;
        }
        SyncOverlayBounds();
    }

    public async void StartPlayback(MediaItem media)
    {
        // --- NEW FERAL INTERCEPT LOGIC ---
        // Dynamically fetch stream links if this is a .strm or .strmlnk file
        media = await _libraryService.ResolveStreamLinkAsync(media);

        if (media.RequiresBrowser)
        {
            LaunchExternalBrowser(media.StreamUrl);
            
            // Auto-trigger the back button so the blank video player closes
            OnBackRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        // ---------------------------------

        _mpvService.PlayMedia(media);

        _overlayWindow = new PlayerOverlayWindow(_mpvService, _libraryService, _serverManager)
        {
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        // Track when the user drags or resizes the Main Window
        var mainWindow = Application.Current.MainWindow;
        if (mainWindow != null)
        {
            mainWindow.LocationChanged += MainWindow_BoundsChanged;
            mainWindow.SizeChanged += MainWindow_BoundsChanged;
        }

        _overlayWindow.OnBackRequested += (s, e) =>
        {
            StopPlayback();
            OnBackRequested?.Invoke(this, EventArgs.Empty);
        };

        _overlayWindow.InitializeMedia(media);
        _overlayWindow.Show();
        
        Application.Current.Dispatcher.BeginInvoke(new Action(SyncOverlayBounds), System.Windows.Threading.DispatcherPriority.ContextIdle);

        _overlayWindow.Activate();
        _overlayWindow.Focus();
    }
	
	private void PlayerView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Instantly hide the cursor on any remote/keyboard input
        Mouse.OverrideCursor = Cursors.None;
    }

    private void PlayerView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
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

    private void MainWindow_BoundsChanged(object? sender, EventArgs e)
    {
        SyncOverlayBounds();
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
}