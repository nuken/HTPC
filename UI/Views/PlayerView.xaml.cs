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

        // Track when the user drags or resizes the Main Window!
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
        
        // Force the overlay to map exactly to the video size on boot
        Application.Current.Dispatcher.BeginInvoke(new Action(SyncOverlayBounds), System.Windows.Threading.DispatcherPriority.Loaded);

        _overlayWindow.Activate();
        _overlayWindow.Focus();
    }

    private void MainWindow_BoundsChanged(object? sender, EventArgs e)
    {
        SyncOverlayBounds();
    }

    private void SyncOverlayBounds()
    {
        if (_overlayWindow == null || PresentationSource.FromVisual(this) == null) return;
        
        try 
        {
            // 1. Get the raw physical screen coordinates of the video player
            Point physicalScreenPos = this.PointToScreen(new Point(0, 0));

            // 2. Get the current monitor's DPI scaling matrix
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                // 3. Convert physical pixels back into WPF Device-Independent Pixels (DIPs)
                System.Windows.Media.Matrix transform = source.CompositionTarget.TransformFromDevice;
                Point dipScreenPos = transform.Transform(physicalScreenPos);

                // 4. Apply the perfectly scaled coordinates
                _overlayWindow.Left = dipScreenPos.X;
                _overlayWindow.Top = dipScreenPos.Y;
                _overlayWindow.Width = this.ActualWidth;
                _overlayWindow.Height = this.ActualHeight;
            }
        }
        catch { /* Ignore if WPF visual tree isn't fully ready */ }
    }

    public void StopPlayback()
    {
        // Unhook the tracking events
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

        // CRITICAL FIX: Restore the global mouse cursor so it isn't trapped invisible!
        Mouse.OverrideCursor = null; 
    }
}