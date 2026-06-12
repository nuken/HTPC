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

namespace HTPC.UI.Windows;

public partial class PlayerOverlayWindow : Window
{
    public event EventHandler? OnBackRequested;

    private readonly MpvPlaybackService _mpvService;
    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    
    private readonly DispatcherTimer _uiHideTimer;
    private readonly DispatcherTimer _syncTimer; 
    
    private bool _isPlaying = true;
    private bool _isDragging = false;
    private bool _isLiveTv = false; 
	private MediaItem? _currentMedia;

    public PlayerOverlayWindow(MpvPlaybackService mpvService, MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _mpvService = mpvService;
        _libraryService = libraryService;
        _serverManager = serverManager;

        _uiHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _uiHideTimer.Tick += UiHideTimer_Tick;

        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _syncTimer.Tick += SyncTimer_Tick;
    }

    public void InitializeMedia(MediaItem media)
    {
        _currentMedia = media; // Save the media item to access Start/End times
        
        ShowTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? "" : media.Title;
        MediaTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? media.Title : media.CurrentShowTitle;
        
        // THE FIX: Use the bulletproof boolean flag
        _isLiveTv = media.IsLiveTv; 

        // Always show the timeline now!
        TimelineGrid.Visibility = Visibility.Visible;
        
        if (_isLiveTv)
        {
            
            TimelineSlider.IsHitTestVisible = false; // Lock the slider so it acts purely as a visual progress bar
        }
        else
        {
            
            TimelineSlider.IsHitTestVisible = true;  // Unlock for movies/shows
        }

        _isPlaying = true;
        PlayPauseButton.Content = "⏸";
        
        _syncTimer.Start(); 
        ShowControls();

        if (media.StartOffsetSeconds > 0)
        {
            Task.Run(async () =>
            {
                await Task.Delay(600); 
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _mpvService.SeekAbsolute(media.StartOffsetSeconds);
                });
            });
        }
    }
	
	private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ShowControls(); 

        // Ask the centralized mapper what action this key represents
        var command = InputMapper.GetCommand(e.Key);

        // If the key isn't mapped to anything, let WPF handle it normally
        if (command == HtpcCommand.None) return;

        // --- Execute logic based on the COMMAND, not the KEY ---
        switch (command)
        {
            case HtpcCommand.Back:
                if (MiniGuideContainer.Visibility == Visibility.Visible) CloseMiniGuide();
                else Back_Click(null!, null!);
                break;

            case HtpcCommand.Up:
                if (_isLiveTv && MiniGuideContainer.Visibility == Visibility.Collapsed) 
                    _ = OpenMiniGuideAsync();
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
            case HtpcCommand.SkipBackward: // Maps both the Left Arrow AND the MediaPrev button!
                if (MiniGuideContainer.Visibility == Visibility.Collapsed && !_isLiveTv) 
                    SkipBackward_Click(null!, null!);
                break;

            case HtpcCommand.Right:
            case HtpcCommand.SkipForward:
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
            var item = MiniGuideList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
            item?.Focus();
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
        
        // THE FIX: Generate the proper virtual or standard parameters
        var media = _libraryService.CreateLiveMediaItem(baseUrl, channel, currentAiring);

        _mpvService.Stop();
        _mpvService.PlayMedia(media);
        InitializeMedia(media); 
        
        CloseMiniGuide();
    }

    private void SyncTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isPlaying || _isDragging) return;

        if (!_isLiveTv)
        {
            // --- STANDARD BEHAVIOR: Movies and Recorded TV ---
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
            // --- THE FERAL TRICK: Clock-based TV Guide Timeline ---
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
    }

    private void Timeline_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _isDragging = true;

    private void Timeline_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _isDragging = false;
        _mpvService.SeekAbsolute(TimelineSlider.Value);
        ShowControls(); 
    }

    private void SkipBackward_Click(object sender, RoutedEventArgs e)
    {
        _mpvService.SeekRelative(-10); 
        ShowControls();
        SyncTimer_Tick(null, EventArgs.Empty); 
    }

    private void SkipForward_Click(object sender, RoutedEventArgs e)
    {
        _mpvService.SeekRelative(30); 
        ShowControls();
        SyncTimer_Tick(null, EventArgs.Empty); 
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

    private void Window_MouseMove(object sender, MouseEventArgs e) => ShowControls();

    private void ShowControls()
    {
        TopBar.Visibility = Visibility.Visible;
        if (MiniGuideContainer.Visibility == Visibility.Collapsed) BottomBar.Visibility = Visibility.Visible;
        
        this.Cursor = Cursors.Arrow;
        _uiHideTimer.Stop();
        _uiHideTimer.Start();
    }

    private void UiHideTimer_Tick(object? sender, EventArgs e)
    {
        _uiHideTimer.Stop();
        if (!_isDragging && MiniGuideContainer.Visibility == Visibility.Collapsed)
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