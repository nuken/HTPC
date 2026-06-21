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
    
    private DispatcherTimer _skipAdTimer;
    private double _skipTargetTime = 0;
    private bool _markersDrawn = false;
    
    private MediaItem? _nextEpisodeToPlay;
    private bool _upNextPromptShown = false;

    // --- NEW: Polling Timer ---
    private readonly DispatcherTimer _statsTimer;

    public PlayerOverlayWindow(MpvPlaybackService mpvService, MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        ApplyGlobalUiScale();
        _mpvService = mpvService;
        _libraryService = libraryService;
        _serverManager = serverManager;

        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _syncTimer.Tick += SyncTimer_Tick;
        
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _idleTimer.Tick += IdleTimer_Tick;
        _idleTimer.Start();
        
        _skipAdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _skipAdTimer.Tick += (s, e) => { SkipAdButton.Visibility = Visibility.Collapsed; _skipAdTimer.Stop(); };
        
        _mpvService.OnCommercialPrompt += ShowSkipAdPrompt;

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += StatsTimer_Tick;

        this.Closed += Window_Closed;
    }
    
    private void Window_Closed(object? sender, EventArgs e)
    {
        _mpvService.OnCommercialPrompt -= ShowSkipAdPrompt; 
        _idleTimer?.Stop();
        _syncTimer?.Stop();
        _skipAdTimer?.Stop();
        _statsTimer?.Stop();
        Mouse.OverrideCursor = null; 
    }

    public void InitializeMedia(MediaItem media)
    {
        _currentMedia = media;
        
        ShowTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? "" : media.Title;
        MediaTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? media.Title : media.CurrentShowTitle;
        
        _isLiveTv = media.IsLiveTv; 
        
        _upNextPromptShown = false;
        UpNextPromptContainer.Visibility = Visibility.Collapsed;
        _nextEpisodeToPlay = null;

        if (!_isLiveTv)
        {
            _ = Task.Run(async () =>
            {
                _nextEpisodeToPlay = await _libraryService.GetNextEpisodeAsync(media);
            });
        }
        
        if (_isLiveTv) BufferingOverlay.Visibility = Visibility.Visible;
        else BufferingOverlay.Visibility = Visibility.Collapsed;

        TimelineGrid.Visibility = Visibility.Visible;
        TimelineSlider.IsHitTestVisible = !_isLiveTv;
        
        _markersDrawn = false;
        CommercialMarkersCanvas.Children.Clear();
        SkipAdButton.Visibility = Visibility.Collapsed;

        _isPlaying = true;
        PlayPauseButton.Content = "⏸";
        
        var prefs = PreferencesManager.Load();
        AnimeButton.Visibility = prefs.EnableUpscaling ? Visibility.Visible : Visibility.Collapsed;
        
        AnimeButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
        
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

        if (e.Key == Key.F)
        {
            if (Application.Current.MainWindow is MainWindow main)
            {
                main.ToggleFullscreen();
            }
            e.Handled = true;
            return;
        }

        // --- NEW: Toggle Stats for Nerds ---
        if (e.Key == Key.S)
        {
            ToggleStatsForNerds();
            e.Handled = true;
            return;
        }

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
                else if (UpNextPromptContainer.Visibility == Visibility.Visible && UpNextButton.IsFocused)
                {
                    UpNextButton_Click(null!, null!);
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
                if (!_markersDrawn)
                {
                    DrawCommercialMarkers(duration);
                    _markersDrawn = true;
                }

                TimelineSlider.Maximum = duration;
                TimelineSlider.Value = position;

                TimeSpan posTime = TimeSpan.FromSeconds(position);
                TimeSpan remTime = TimeSpan.FromSeconds(duration - position);

                CurrentTimeText.Text = posTime.ToString(posTime.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
                RemainingTimeText.Text = "-" + remTime.ToString(remTime.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
            }
            
            if (_nextEpisodeToPlay != null && !_upNextPromptShown)
            {
                if (duration > 0 && (duration - position <= 120 || position / duration >= 0.95))
                {
                    ShowUpNextPrompt();
                }
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
    
    private void ShowUpNextPrompt()
    {
        _upNextPromptShown = true;
        UpNextTitleText.Text = _nextEpisodeToPlay?.CurrentShowTitle ?? "Next Episode";
        UpNextPromptContainer.Visibility = Visibility.Visible;
        
        WakeUpUi();
        UpNextButton.Focus(); 
    }

    private void UpNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_nextEpisodeToPlay == null) return;

        _mpvService.Stop();
        _mpvService.PlayMedia(_nextEpisodeToPlay);
        InitializeMedia(_nextEpisodeToPlay);
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

    private async void Timeline_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _mpvService.SeekAbsolute(TimelineSlider.Value);
        WakeUpUi(); 
        
        await Task.Delay(300);
        _isDragging = false;
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
    
    private void Anime_Click(object sender, RoutedEventArgs e)
    {
        bool isAnimeActive = _mpvService.ToggleAnimeMode();
        
        AnimeButton.Foreground = new System.Windows.Media.SolidColorBrush(
            isAnimeActive ? System.Windows.Media.Color.FromRgb(139, 0, 0) : System.Windows.Media.Colors.White);
            
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
    
    private void ShowSkipAdPrompt(double targetTime)
    {
        Dispatcher.Invoke(() => 
        {
            _skipTargetTime = targetTime;
            SkipAdButton.Visibility = Visibility.Visible;
            SkipAdButton.Focus(); 
            _skipAdTimer.Stop();
            _skipAdTimer.Start(); 
            WakeUpUi();
        });
    }

    private void SkipAdButton_Click(object sender, RoutedEventArgs e)
    {
        _mpvService.SeekAbsolute(_skipTargetTime);
        SkipAdButton.Visibility = Visibility.Collapsed;
        _skipAdTimer.Stop();
        WakeUpUi();
    }

    private void CommercialMarkersCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLiveTv && _mpvService.GetDuration() > 0)
        {
            DrawCommercialMarkers(_mpvService.GetDuration());
        }
    }

    private void DrawCommercialMarkers(double totalDuration)
    {
        CommercialMarkersCanvas.Children.Clear();
        
        if (_currentMedia?.Commercials == null || _currentMedia.Commercials.Count < 2 || totalDuration <= 0 || CommercialMarkersCanvas.ActualWidth <= 0) 
            return;

        var comms = _currentMedia.Commercials;
        
        for (int i = 0; i < comms.Count - 1; i += 2)
        {
            double start = comms[i];
            double end = comms[i + 1];

            double startPct = start / totalDuration;
            double widthPct = (end - start) / totalDuration;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 165, 0)),
                Width = CommercialMarkersCanvas.ActualWidth * widthPct,
                Height = CommercialMarkersCanvas.ActualHeight
            };

            Canvas.SetLeft(rect, CommercialMarkersCanvas.ActualWidth * startPct);
            CommercialMarkersCanvas.Children.Add(rect);
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        Point currentPosition = e.GetPosition(this);

        if (Math.Abs(currentPosition.X - _lastMousePosition.X) > 2 || 
            Math.Abs(currentPosition.Y - _lastMousePosition.Y) > 2)
        {
            _lastMousePosition = currentPosition;
            WakeUpUi();
        }
    }

    private void WakeUpUi()
    {
        Mouse.OverrideCursor = null;

        if (!_isControlsVisible)
        {
            _isControlsVisible = true;
            FadeControls(1.0); 
        }

        _idleTimer?.Stop();
        _idleTimer?.Start();
    }

    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        _idleTimer?.Stop();

        Mouse.OverrideCursor = Cursors.None;

        if (_isControlsVisible)
        {
            _isControlsVisible = false;
            FadeControls(0.0); 
        }
    }

    private void FadeControls(double targetOpacity)
    {
        var fadeAnimation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromSeconds(0.3),
            FillBehavior = FillBehavior.HoldEnd
        };

        ControlsContainer.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
        ControlsContainer.IsHitTestVisible = targetOpacity > 0;
    }
    
    private void ApplyGlobalUiScale()
    {
        var prefs = PreferencesManager.Load();
        double scale = prefs.UiScaleMultiplier;

        if (scale < 0.5) scale = 1.0;

        ControlsContainer.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
    }

    // --- NEW: STATS FOR NERDS METHODS ---
    private void ToggleStatsForNerds()
    {
        if (_statsTimer == null) return; 

        if (StatsForNerdsContainer.Visibility == Visibility.Visible)
        {
            StatsForNerdsContainer.Visibility = Visibility.Collapsed;
            _statsTimer.Stop(); 
        }
        else
        {
            StatsForNerdsContainer.Visibility = Visibility.Visible;
            UpdateNerdStats(); 
            _statsTimer.Start();
        }
    }

    private void StatsTimer_Tick(object? sender, EventArgs e)
    {
        UpdateNerdStats();
    }

    private void UpdateNerdStats()
    {
        try
        {
            StatVideoCodec.Text = _mpvService.GetMpvProperty("video-codec");
            
            string width = _mpvService.GetMpvProperty("width");
            string height = _mpvService.GetMpvProperty("height");
            StatResolution.Text = width != "N/A" && height != "N/A" ? $"{width}x{height}" : "N/A";

            string fps = _mpvService.GetMpvProperty("estimated-vf-fps");
            if (fps != "N/A" && double.TryParse(fps, out double fpsValue))
            {
                StatFps.Text = fpsValue.ToString("0.00");
            }
            else StatFps.Text = "N/A";

            StatAudioCodec.Text = _mpvService.GetMpvProperty("audio-codec");
            
            string avSync = _mpvService.GetMpvProperty("avsync");
            if (avSync != "N/A" && double.TryParse(avSync, out double syncValue))
            {
                StatAvSync.Text = $"{syncValue.ToString("0.000")} sec";
            }
            else StatAvSync.Text = "0.000 sec";

            string droppedDecoder = _mpvService.GetMpvProperty("drop-frame-count");
            string droppedVo = _mpvService.GetMpvProperty("vo-drop-frame-count");
            StatDropped.Text = $"{droppedDecoder} (Dec) / {droppedVo} (Out)";

            StatHwDec.Text = _mpvService.GetMpvProperty("hwdec-current");
        }
        catch { }
    }
}