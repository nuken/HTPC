using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Windows;

public partial class PlayerOverlayWindow : Window
{
    public event EventHandler? OnBackRequested;

    private readonly MpvPlaybackService _mpvService;
    private readonly DispatcherTimer _uiHideTimer;
    private readonly DispatcherTimer _syncTimer; // NEW: Timeline Sync Timer
    
    private bool _isPlaying = true;
    private bool _isDragging = false;

    public PlayerOverlayWindow(MpvPlaybackService mpvService)
    {
        InitializeComponent();
        _mpvService = mpvService;

        // Handles auto-hiding the UI
        _uiHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _uiHideTimer.Tick += UiHideTimer_Tick;

        // NEW: Handles querying MPV for the timeline position every 500ms
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _syncTimer.Tick += SyncTimer_Tick;
    }

    public void InitializeMedia(MediaItem media)
    {
        ShowTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? "" : media.Title;
        MediaTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? media.Title : media.CurrentShowTitle;
        _isPlaying = true;
        PlayPauseButton.Content = "⏸";
        
        _syncTimer.Start(); // Start syncing immediately
        ShowControls();
    }

    // --- TIMELINE SYNCING ---

    private void SyncTimer_Tick(object? sender, EventArgs e)
    {
        // Don't update the slider if the user is actively dragging it!
        if (!_isPlaying || _isDragging) return;

        double duration = _mpvService.GetDuration();
        double position = _mpvService.GetPosition();

        // Duration might be 0 for a fraction of a second while the video loads
        if (duration > 0)
        {
            TimelineSlider.Maximum = duration;
            TimelineSlider.Value = position;

            // Format standard 00:00:00 timestamps
            TimeSpan posTime = TimeSpan.FromSeconds(position);
            TimeSpan remTime = TimeSpan.FromSeconds(duration - position);

            // Use string format to hide hours if the video is short (optional)
            CurrentTimeText.Text = posTime.ToString(posTime.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
            RemainingTimeText.Text = "-" + remTime.ToString(remTime.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
        }
    }

    private void Timeline_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _isDragging = true;
    }

    private void Timeline_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _isDragging = false;
        
        // Command MPV to jump to the exact pixel/second the user dropped the slider on
        _mpvService.SeekAbsolute(TimelineSlider.Value);
        ShowControls(); 
    }

    // --- TRANSPORT CONTROLS ---

    private void SkipBackward_Click(object sender, RoutedEventArgs e)
    {
        _mpvService.SeekRelative(-10); // Rewind 10 seconds
        ShowControls();
        SyncTimer_Tick(null, EventArgs.Empty); // Force immediate UI refresh
    }

    private void SkipForward_Click(object sender, RoutedEventArgs e)
    {
        _mpvService.SeekRelative(30); // Fast Forward 30 seconds (Standard TV Commercial Skip)
        ShowControls();
        SyncTimer_Tick(null, EventArgs.Empty); // Force immediate UI refresh
    }

    private void CC_Click(object sender, RoutedEventArgs e)
    {
        _mpvService.CycleSubtitles();
        ShowControls();
    }

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

    // --- OVERLAY LOGIC ---

    private void Window_MouseMove(object sender, MouseEventArgs e) => ShowControls();

    private void ShowControls()
    {
        TopBar.Visibility = Visibility.Visible;
        BottomBar.Visibility = Visibility.Visible;
        this.Cursor = Cursors.Arrow;
        
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
            this.Cursor = Cursors.None;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _syncTimer.Stop();
        OnBackRequested?.Invoke(this, EventArgs.Empty);
    }
}