using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Models;
using HTPC.Services;
using HTPC.UI.Controls;

namespace HTPC.UI.Views;

public partial class PlayerView : UserControl
{
    public event EventHandler? OnBackRequested;

    private readonly MpvPlaybackService _mpvService;
    private readonly MpvVideoHost _videoHost;
    private readonly DispatcherTimer _uiHideTimer;
    
    private MediaItem? _currentMedia;
    private bool _isPlaying = false;
    private bool _isDragging = false;
    private bool _isMpvAttached = false;

    public PlayerView(MpvPlaybackService mpvService)
    {
        InitializeComponent();
        _mpvService = mpvService;

        _videoHost = new MpvVideoHost();
        VideoSurface.Children.Add(_videoHost);

        this.Loaded += OnLoaded;
        
        // THE FIX: Listen to every single layout update to guarantee positioning
        this.LayoutUpdated += PlayerView_LayoutUpdated;

        _uiHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _uiHideTimer.Tick += UiHideTimer_Tick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isMpvAttached)
        {
            _mpvService.AttachToWindow(_videoHost.Handle);
            _isMpvAttached = true;
        }
    }

    // THE MASTER SIZING LOGIC
    private void PlayerView_LayoutUpdated(object? sender, EventArgs e)
    {
        if (RootGrid.ActualHeight > 0 && BottomBar.ActualHeight > 0)
        {
            // 1. Force the invisible overlay to match the video surface EXACTLY
            ControlsOverlay.Width = RootGrid.ActualWidth;
            ControlsOverlay.Height = RootGrid.ActualHeight;

            // 2. UNBREAKABLE MATH: Push the bottom bar down from the TOP of the screen.
            // Screen Height - Bar Height - 40px Margin = Perfect Bottom Placement!
            double exactTopMargin = RootGrid.ActualHeight - BottomBar.ActualHeight - 40;
            
            if (exactTopMargin > 0)
            {
                BottomBar.Margin = new Thickness(40, exactTopMargin, 40, 0);
            }
        }
    }

    public void StartPlayback(MediaItem media)
    {
        _currentMedia = media;
        
        ShowTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? "" : media.Title;
        MediaTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? media.Title : media.CurrentShowTitle;

        _mpvService.PlayMedia(media);

        _isPlaying = true;
        PlayPauseButton.Content = "⏸";

        ControlsPopup.IsOpen = true;
        ShowControls();
    }

    public void StopPlayback()
    {
        _mpvService.Stop();
        
        ControlsPopup.IsOpen = false;
        _uiHideTimer.Stop();
        
        this.Cursor = Cursors.Arrow;
    }

    // --- AUTO-HIDE LOGIC ---

    private void ControlsOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        ShowControls();
    }

    private void ShowControls()
    {
        TopBar.Visibility = Visibility.Visible;
        BottomBar.Visibility = Visibility.Visible;
        ControlsOverlay.Cursor = Cursors.Arrow;
        
        _uiHideTimer.Stop();
        _uiHideTimer.Start();
    }

    private void UiHideTimer_Tick(object? sender, EventArgs e)
    {
        _uiHideTimer.Stop();
        
        if (!_isDragging)
        {
            TopBar.Visibility = Visibility.Collapsed;
            BottomBar.Visibility = Visibility.Collapsed;
            ControlsOverlay.Cursor = Cursors.None;
        }
    }

    // --- TRANSPORT CONTROLS ---

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            _mpvService.Pause();
            _isPlaying = false;
            PlayPauseButton.Content = "▶";
        }
        else
        {
            _mpvService.Resume();
            _isPlaying = true;
            PlayPauseButton.Content = "⏸";
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        OnBackRequested?.Invoke(this, EventArgs.Empty);
    }

    // --- TIMELINE LOGIC ---

    private void Timeline_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _isDragging = true;
    }

    private void Timeline_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _isDragging = false;
        ShowControls(); 
    }
}