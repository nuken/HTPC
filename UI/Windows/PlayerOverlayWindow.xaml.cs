using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Models;
using HTPC.Services;
using HTPC.Core.Input;
using System.Windows.Media.Animation;

namespace HTPC.UI.Windows;

public partial class PlayerOverlayWindow : Window
{
    public event EventHandler? OnBackRequested;

    private readonly MpvPlaybackService _mpvService;
    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    
    private readonly DispatcherTimer _syncTimer; 
    private DispatcherTimer _idleTimer;
    
    private bool _isPlaying = true;
    private bool _isDragging = false;
    private bool _isLiveTv = false; 
    private MediaItem? _currentMedia;
    private bool _isControlsVisible = true;
	private Point _lastMousePosition;

    public PlayerOverlayWindow(MpvPlaybackService mpvService, MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _mpvService = mpvService;
        _libraryService = libraryService;
        _serverManager = serverManager;

        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _syncTimer.Tick += SyncTimer_Tick;
        
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _idleTimer.Tick += IdleTimer_Tick;
        _idleTimer.Start();
    }
    
    public void InitializeMedia(MediaItem media)
    {
        _currentMedia = media;
        
        ShowTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? "" : media.Title;
        MediaTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? media.Title : media.CurrentShowTitle;
        
        _isLiveTv = media.IsLiveTv; 
        
        // INSTANTLY SHOW THE TUNING SCREEN
        if (_isLiveTv) BufferingOverlay.Visibility = Visibility.Visible;
        else BufferingOverlay.Visibility = Visibility.Collapsed;

        TimelineGrid.Visibility = Visibility.Visible;
        
        if (_isLiveTv)
        {
            TimelineSlider.IsHitTestVisible = false; 
        }
        else
        {
            TimelineSlider.IsHitTestVisible = true;  
        }

        _isPlaying = true;
        PlayPauseButton.Content = "⏸";
        
        _syncTimer.Start(); 
        WakeUpUi();

        if (media.StartOffsetSeconds > 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(600); 
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _mpvService.SeekAbsolute(media.StartOffsetSeconds);
                });
            });
        }
    }
    
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        WakeUpUi(); 

        var command = InputMapper.GetCommand(e.Key);

        if (command == HtpcCommand.None) return;

        switch (command)
        {
            case HtpcCommand.Back:
                if (MiniGuideContainer.Visibility == Visibility.Visible) CloseMiniGuide();
                else Back_Click(null, null);
                break;

            case HtpcCommand.Up:
                if (_isLiveTv && MiniGuideContainer.Visibility == Visibility.Collapsed) 
                    await OpenMiniGuideAsync(); 
                break;

            case HtpcCommand.Down:
                if (MiniGuideContainer.Visibility == Visibility.Visible) 
                    CloseMiniGuide();
                break;

            case HtpcCommand.Select:
                if (MiniGuideContainer.Visibility == Visibility.Visible)
                {
                    if (MiniGuideList.SelectedItem is Channel selectedChannel) 
                        PlayChannelFromMiniGuide(selectedChannel);
                }
                else
                {
                    PlayPause_Click(null!, null!);
                }
                break;

            case HtpcCommand.Left:
            case HtpcCommand.SkipBackward: 
                if (MiniGuideContainer.Visibility == Visibility.Visible) return; 
                
                if (MiniGuideContainer.Visibility == Visibility.Collapsed && !_isLiveTv) 
                    SkipBackward_Click(null!, null!);
                break;

            case HtpcCommand.Right:
            case HtpcCommand.SkipForward:
                if (MiniGuideContainer.Visibility == Visibility.Visible) return; 
                
                if (MiniGuideContainer.Visibility == Visibility.Collapsed && !_isLiveTv) 
                    SkipForward_Click(null!, null!);
                break;

            case HtpcCommand.PlayPause:
                PlayPause_Click(null!, null!);
                break;
                
            case HtpcCommand.ToggleSubtitles:
                CC_Click(null!, null!);
                break;
        }

        e.Handled = true;
    }
    
    private void Back_Click(object? sender, RoutedEventArgs? e)
    {
        OnBackRequested?.Invoke(this, EventArgs.Empty);
    }

   private async Task OpenMiniGuideAsync()
    {
        BottomBar.Visibility = Visibility.Collapsed; 
        MiniGuideContainer.Visibility = Visibility.Visible;

        if (MiniGuideList.Items.Count == 0)
        {
            var activeServer = _serverManager.GetActiveServer();
            var collections = await _libraryService.GetCollectionsAsync();
            var savedCollection = collections.FirstOrDefault(c => c.Id == activeServer?.DefaultCollectionId) ?? collections.FirstOrDefault();

            var channels = await _libraryService.GetGuideChannelsAsync(savedCollection, 1);
            MiniGuideList.ItemsSource = channels;
        }

        if (MiniGuideList.Items.Count > 0)
        {
            MiniGuideList.SelectedIndex = 0;
            
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                MiniGuideList.UpdateLayout();
                var item = MiniGuideList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                item?.Focus();
            }), DispatcherPriority.Loaded);
        }
    }

    private void CloseMiniGuide()
    {
        MiniGuideContainer.Visibility = Visibility.Collapsed;
        BottomBar.Visibility = Visibility.Visible; 
        this.Focus(); 
    }

    private void PlayChannelFromMiniGuide(Channel channel)
    {
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return;
        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        
        var currentAiring = channel.CurrentAirings?.FirstOrDefault(a => a.IsAiringNow) ?? channel.CurrentAirings?.FirstOrDefault();
        
        var media = _libraryService.CreateLiveMediaItem(baseUrl, channel, currentAiring);

        _mpvService.Stop();
        _mpvService.PlayMedia(media);
        InitializeMedia(media); 
        
        CloseMiniGuide();
    }

    private void SyncTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isPlaying || _isDragging) return;

        // --- HIDE THE BUFFERING SCREEN ONCE VIDEO STARTS ---
        if (BufferingOverlay.Visibility == Visibility.Visible)
        {
            if (_mpvService.GetPosition() > 0)
            {
                BufferingOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                return; 
            }
        }

        if (!_isLiveTv)
        {
            double duration = _mpvService.GetDuration();
            double position = _mpvService.GetPosition();

            if (duration > 0)
            {
                TimelineSlider.Maximum = duration;
                TimelineSlider.Value = position;

                TimeSpan posTime = TimeSpan.FromSeconds(position);
                TimeSpan remTime = TimeSpan.FromSeconds(duration - position);

                CurrentTimeText.Text = posTime.ToString(posTime.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
                RemainingTimeText.Text = "-" + remTime.ToString(remTime.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
            }
        }
        else
        {
            if (_currentMedia != null && _currentMedia.EndTime > _currentMedia.StartTime)
            {
                double duration = (_currentMedia.EndTime - _currentMedia.StartTime).TotalSeconds;
                double position = (DateTime.Now - _currentMedia.StartTime).TotalSeconds;

                if (position < 0) position = 0;
                if (position > duration) position = duration;

                TimelineSlider.Maximum = duration;
                TimelineSlider.Value = position;

                TimeSpan posTime = TimeSpan.FromSeconds(position);
                TimeSpan remTime = TimeSpan.FromSeconds(duration - position);

                CurrentTimeText.Text = posTime.ToString(posTime.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
                RemainingTimeText.Text = "-" + remTime.ToString(remTime.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
            }
        }
    }
    
    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _mpvService?.SetVolume((int)e.NewValue);
        WakeUpUi();
    }

    private void Timeline_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) 
    {
        _isDragging = true;
        WakeUpUi();
    }

    private void Timeline_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _isDragging = false;
        _mpvService.SeekAbsolute(TimelineSlider.Value);
        WakeUpUi(); 
    }

    private void SkipBackward_Click(object sender, RoutedEventArgs e)
    {
        _mpvService.SeekRelative(-10); 
        WakeUpUi();
        SyncTimer_Tick(null, EventArgs.Empty); 
    }

    private void SkipForward_Click(object sender, RoutedEventArgs e)
    {
        _mpvService.SeekRelative(30); 
        WakeUpUi();
        SyncTimer_Tick(null, EventArgs.Empty); 
    }

    private void CC_Click(object sender, RoutedEventArgs e)
    {
        _mpvService.CycleSubtitles();
        WakeUpUi();
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
        WakeUpUi();
    }

    // --- IDLE TIMER & FADE LOGIC ---

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        Point currentPosition = e.GetPosition(this);

        // Check if the physical mouse actually moved more than 2 pixels
        if (Math.Abs(currentPosition.X - _lastMousePosition.X) > 2 || 
            Math.Abs(currentPosition.Y - _lastMousePosition.Y) > 2)
        {
            _lastMousePosition = currentPosition;
            WakeUpUi();
        }
    }

    private void WakeUpUi()
    {
        // 1. Instantly restore the mouse cursor
        Mouse.OverrideCursor = null;

        // 2. Smoothly fade the controls back in if they were hidden
        if (!_isControlsVisible)
        {
            _isControlsVisible = true;
            FadeControls(1.0); // 100% Opacity
        }

        // 3. Reset the 3-second countdown (Safe against WPF initialization events!)
        _idleTimer?.Stop();
        _idleTimer?.Start();
    }

    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        _idleTimer.Stop();

        // 1. Force the mouse cursor to completely vanish globally
        Mouse.OverrideCursor = Cursors.None;

        // 2. Smoothly fade the controls out
        if (_isControlsVisible)
        {
            _isControlsVisible = false;
            FadeControls(0.0); // 0% Opacity
        }
    }

    private void FadeControls(double targetOpacity)
    {
        // Create a 0.3-second cinematic fade
        var fadeAnimation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromSeconds(0.3),
            FillBehavior = FillBehavior.HoldEnd
        };

        // Apply the animation to the wrapper Grid
        ControlsContainer.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
        
        // Ensure invisible buttons don't accidentally intercept mouse clicks
        ControlsContainer.IsHitTestVisible = targetOpacity > 0;
    }
}