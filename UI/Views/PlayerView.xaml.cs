using System.Windows;
using System.Windows.Controls;
using HTPC.Services;
using HTPC.Core.Models; // Fixes the missing MediaItem reference

namespace HTPC.UI.Views;

public partial class PlayerView : UserControl
{
    private readonly MpvPlaybackService _playbackService;
    private bool _isHwndBound = false;

    public PlayerView(MpvPlaybackService playbackService)
    {
        InitializeComponent();
        _playbackService = playbackService;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isHwndBound)
        {
            _playbackService.AttachToWindow(VideoSurface.Handle);
            _isHwndBound = true;
        }
    }

    public void StartPlayback(MediaItem media)
    {
        _playbackService.PlayMedia(media);
    }

    public void StopPlayback()
    {
        _playbackService.Stop();
    }
}