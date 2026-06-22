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

    public void StartPlayback(MediaItem media)
    {
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
        
        // CRITICAL FIX: Delay the math until the XAML Layout Engine is 100% idle.
        // This guarantees PointToScreen won't silently crash the app during screen transitions!
        Application.Current.Dispatcher.BeginInvoke(new Action(SyncOverlayBounds), System.Windows.Threading.DispatcherPriority.ContextIdle);

        _overlayWindow.Activate();
        _overlayWindow.Focus();
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