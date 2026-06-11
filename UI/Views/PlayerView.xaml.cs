using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Models;

namespace HTPC.UI.Views;

public partial class PlayerView : UserControl
{
    public event EventHandler? OnBackRequested;

    private readonly DispatcherTimer _uiHideTimer;
    private MediaItem? _currentMedia;
    private bool _isPlaying = false;
    private bool _isDragging = false;

    public PlayerView()
    {
        InitializeComponent();

        // Initialize the 3-second auto-hide timer
        _uiHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _uiHideTimer.Tick += UiHideTimer_Tick;
    }

    // Matches your existing MainWindow call!
    public void StartPlayback(MediaItem media)
    {
        _currentMedia = media;
        
        // Update UI Titles
        ShowTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? "" : media.Title;
        MediaTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? media.Title : media.CurrentShowTitle;

        // TODO: Initialize MPV here using media.StreamUrl
        // _mpvPlayer.Load(media.StreamUrl);
        // _mpvPlayer.Play();

        _isPlaying = true;
        PlayPauseButton.Content = "⏸";

        ShowControls();
    }

    // Matches your existing MainWindow Escape Key logic!
    public void StopPlayback()
    {
        // TODO: Stop MPV playback and clear memory
        // _mpvPlayer.Stop();
        
        _uiHideTimer.Stop();
        this.Cursor = Cursors.Arrow;
    }

    // --- AUTO-HIDE LOGIC ---

    private void UserControl_MouseMove(object sender, MouseEventArgs e)
    {
        ShowControls();
    }

    private void ShowControls()
    {
        ControlsOverlay.Visibility = Visibility.Visible;
        this.Cursor = Cursors.Arrow;
        
        _uiHideTimer.Stop();
        _uiHideTimer.Start();
    }

    private void UiHideTimer_Tick(object? sender, EventArgs e)
    {
        _uiHideTimer.Stop();
        
        // Only hide if we aren't actively dragging the timeline
        if (!_isDragging)
        {
            ControlsOverlay.Visibility = Visibility.Collapsed;
            this.Cursor = Cursors.None;
        }
    }

    // --- TRANSPORT CONTROLS ---

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            // Pause MPV
            _isPlaying = false;
            PlayPauseButton.Content = "▶";
        }
        else
        {
            // Play MPV
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
        // Tell MPV to seek to TimelineSlider.Value
        
        ShowControls(); // Restart the hide timer
    }
}