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
    private readonly MpvVideoHost _videoHost;
    private PlayerOverlayWindow? _overlayWindow;
    private bool _isMpvAttached = false;

    public PlayerView(MpvPlaybackService mpvService)
    {
        InitializeComponent();
        _mpvService = mpvService;

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
        // 1. Start the native hardware video
        _mpvService.PlayMedia(media);

        // 2. Launch the completely separate transparent UI Window!
        _overlayWindow = new PlayerOverlayWindow(_mpvService)
        {
            // Lock the overlay to the Main Window so they minimize/close together
            Owner = Application.Current.MainWindow 
        };

        // If the user clicks back on the overlay, close it and alert MainWindow
        _overlayWindow.OnBackRequested += (s, e) =>
        {
            StopPlayback();
            OnBackRequested?.Invoke(this, EventArgs.Empty);
        };

        _overlayWindow.InitializeMedia(media);
        _overlayWindow.Show();
    }

    public void StopPlayback()
    {
        _mpvService.Stop();
        
        // Destroy the overlay window
        if (_overlayWindow != null)
        {
            _overlayWindow.Close();
            _overlayWindow = null;
        }
    }
}