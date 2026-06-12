using System;
using System.Windows;
using System.Windows.Controls;
using HTPC.Core.Models;
using HTPC.Services;
using HTPC.UI.Controls;
using HTPC.UI.Windows;

namespace HTPC.UI.Views;

public partial class PlayerView : UserControl
{
    public event EventHandler? OnBackRequested;

    private readonly MpvPlaybackService _mpvService;
    private readonly MediaLibraryService _libraryService;     // NEW
    private readonly ServerManagerService _serverManager;     // NEW
    private readonly MpvVideoHost _videoHost;
    
    private PlayerOverlayWindow? _overlayWindow;
    private bool _isMpvAttached = false;

    // Pass the new services into the constructor
    public PlayerView(MpvPlaybackService mpvService, MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _mpvService = mpvService;
        _libraryService = libraryService;
        _serverManager = serverManager;

        _videoHost = new MpvVideoHost();
        VideoSurface.Children.Add(_videoHost);

        this.Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isMpvAttached)
        {
            _mpvService.AttachToWindow(_videoHost.Handle);
            _isMpvAttached = true;
        }
    }

   public void StartPlayback(MediaItem media)
    {
        _mpvService.PlayMedia(media);

        _overlayWindow = new PlayerOverlayWindow(_mpvService, _libraryService, _serverManager)
        {
            Owner = Application.Current.MainWindow 
        };

        _overlayWindow.OnBackRequested += (s, e) =>
        {
            StopPlayback();
            OnBackRequested?.Invoke(this, EventArgs.Empty);
        };

        _overlayWindow.InitializeMedia(media);
        _overlayWindow.Show();
        
        // THE FIX: Force the transparent overlay to steal keyboard focus back from MPV!
        _overlayWindow.Activate();
        _overlayWindow.Focus();
    }

    public void StopPlayback()
    {
        _mpvService.Stop();
        if (_overlayWindow != null)
        {
            _overlayWindow.Close();
            _overlayWindow = null;
        }
    }
}