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
	
	private readonly DispatcherTimer _holdTimer;
    private readonly DispatcherTimer _scrubTimer;
    private int _scrubDirection = 0;
    private bool _isScrubbing = false;
    private bool _wasPlayingBeforeScrub = false;
    
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
		
		_holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _holdTimer.Tick += HoldTimer_Tick;

        _scrubTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _scrubTimer.Tick += ScrubTimer_Tick;
        
        // Listen for when the user lets go of a button on the hardware remote
        this.PreviewKeyUp += Window_PreviewKeyUp;
		if (Application.Current.MainWindow != null)
        {
            Application.Current.MainWindow.StateChanged += MainWindow_StateChanged;
        }

        this.Closed += Window_Closed;
    }
	
	private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        // When fullscreen is toggled, the OS steals focus. 
        // This snatches it back with Input Priority to ensure the remote keeps working.
        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            this.Focus();
            WakeUpUi(); 
        }), DispatcherPriority.Input);
    }
    
    private void Window_Closed(object? sender, EventArgs e)
    {
        if (Application.Current.MainWindow != null)
        {
            Application.Current.MainWindow.StateChanged -= MainWindow_StateChanged;
        }
        
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
        VolumeSlider.Value = prefs.Volume;
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
                {
                    await OpenMiniGuideAsync();
                }
                else if (MiniGuideContainer.Visibility == Visibility.Collapsed)
                {
                    // FOCUS BRIDGE: Explicitly escape Volume Slider UP to the Timeline
                    if (Keyboard.FocusedElement is Slider s && s.Name == "VolumeSlider")
                        TimelineSlider.Focus();
                    else
                        (Keyboard.FocusedElement as FrameworkElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
                }
                break;

            case HtpcCommand.Down:
                if (MiniGuideContainer.Visibility == Visibility.Visible)
                {
                    CloseMiniGuide();
                }
                else if (MiniGuideContainer.Visibility == Visibility.Collapsed)
                {
                    // FOCUS BRIDGE: Explicitly escape Volume Slider DOWN back to the Button row
                    if (Keyboard.FocusedElement is Slider s && s.Name == "VolumeSlider")
                        StatsButton.Focus();
                    else
                        (Keyboard.FocusedElement as FrameworkElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
                }
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
                else if (Keyboard.FocusedElement is Button)
                {
                    // Let WPF natively click whichever button is currently highlighted (CC, Stats, Skip, etc.)
                    return; 
                }
                else
                {
                    PlayPause_Click(null!, null!);
                }
                break;

            case HtpcCommand.Left:
            case HtpcCommand.SkipBackward: 
                if (MiniGuideContainer.Visibility == Visibility.Visible) return; 

                if (command == HtpcCommand.Left)
                {
                    if (Keyboard.FocusedElement is Button)
                    {
                        (Keyboard.FocusedElement as FrameworkElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
                        break;
                    }
                    else if (Keyboard.FocusedElement is Slider s && s.Name == "VolumeSlider")
                    {
                        // EDGE ESCAPE: If volume is 0 and they press Left, jump back to Stats button
                        if (s.Value <= s.Minimum)
                        {
                            StatsButton.Focus();
                            break;
                        }
                        return; // Let WPF natively slide the volume thumb
                    }
                }

                // If focus is on the Timeline, scrub!
                if (MiniGuideContainer.Visibility == Visibility.Collapsed && !_isLiveTv) 
                {
                    if (!e.IsRepeat) BeginSkipAction(-1); 
                }
                break;

            case HtpcCommand.Right:
            case HtpcCommand.SkipForward:
                if (MiniGuideContainer.Visibility == Visibility.Visible) return; 

                if (command == HtpcCommand.Right)
                {
                    // If focus is in the control buttons, navigate UI instead of scrubbing
                    if (Keyboard.FocusedElement is Button)
                    {
                        (Keyboard.FocusedElement as FrameworkElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right));
                        break;
                    }
                    else if (Keyboard.FocusedElement is Slider s && s.Name == "VolumeSlider")
                    {
                        return; // Let WPF natively slide the volume thumb
                    }
                }

                // If focus is on the Timeline, scrub!
                if (MiniGuideContainer.Visibility == Visibility.Collapsed && !_isLiveTv) 
                {
                    if (!e.IsRepeat) BeginSkipAction(1); 
                }
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
	
	private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Left || command == HtpcCommand.Right || 
            command == HtpcCommand.SkipBackward || command == HtpcCommand.SkipForward)
        {
            EndSkipAction();
            e.Handled = true;
        }
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
            // 1. Break the scroll lock by clearing the old selection
            MiniGuideList.SelectedIndex = -1;
            
            // 2. The Focus Reclamation Hammer (Steal input back from MPV)
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                MiniGuideList.UpdateLayout(); // Force WPF to redraw the list immediately
                
                var item = MiniGuideList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                if (item != null)
                {
                    item.Focus();
                    Keyboard.Focus(item); // Force hardware remote to this item
                }
                else
                {
                    MiniGuideList.Focus();
                    Keyboard.Focus(MiniGuideList);
                }
            }), DispatcherPriority.Input); // Input priority ensures it beats MPV
        }
    }

    private void CloseMiniGuide()
    {
        MiniGuideContainer.Visibility = Visibility.Collapsed;
        BottomBar.Visibility = Visibility.Visible; 
        
        // Hand the remote control back to the main player window safely
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.Focus();
            Keyboard.Focus(window);
        }
        else
        {
            this.Focus();
            Keyboard.Focus(this);
        }
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
        if (_mpvService != null)
        {
            int newVolume = (int)e.NewValue;
            _mpvService.SetVolume(newVolume);
            WakeUpUi();

            // NEW: Save the volume so it survives a restart
            var prefs = PreferencesManager.Load();
            prefs.Volume = newVolume;
            // Assuming your manager has a save method, it usually looks like this:
            PreferencesManager.Save(prefs); 
        }
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
	
	// --- THE NEW SCRUB & SKIP ENGINE ---
    
    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        _mpvService.SeekAbsolute(0); 
        WakeUpUi();
        SyncTimer_Tick(null, EventArgs.Empty); 
    }

    private void BeginSkipAction(int direction)
    {
        if (_isLiveTv || _scrubDirection != 0) return; 
        
        _scrubDirection = direction;
        _isScrubbing = false;
        _holdTimer.Start();
    }

    private void EndSkipAction()
    {
        if (_scrubDirection == 0) return;

        _holdTimer.Stop();

        if (_isScrubbing)
        {
            // We are done scrubbing, turn the video back on!
            _scrubTimer.Stop();
            _isScrubbing = false;
            
            if (_wasPlayingBeforeScrub) 
            {
                _mpvService.Resume();
                _isPlaying = true;
            }
        }
        else
        {
            // The timer never fired. It was just a quick tap!
            if (_scrubDirection == 1) _mpvService.SeekRelative(30);
            else _mpvService.SeekRelative(-10);
        }

        _scrubDirection = 0;
        WakeUpUi();
        SyncTimer_Tick(null, EventArgs.Empty);
    }

    private void HoldTimer_Tick(object? sender, EventArgs e)
    {
        _holdTimer.Stop();
        _isScrubbing = true;
        
        // Pause the video while scanning so it doesn't aggressively stutter
        _wasPlayingBeforeScrub = _isPlaying;
        if (_isPlaying)
        {
            _mpvService.Pause();
            _isPlaying = false; 
        }

        _scrubTimer.Start();
    }

    private void ScrubTimer_Tick(object? sender, EventArgs e)
    {
        // Jump 5 seconds every 100ms (Roughly 50x scan speed)
        _mpvService.SeekRelative(5 * _scrubDirection);
        WakeUpUi();
        SyncTimer_Tick(null, EventArgs.Empty);
    }

    // --- MOUSE & TOUCH BINDINGS ---
    private void SkipBackward_MouseDown(object sender, MouseButtonEventArgs e) { BeginSkipAction(-1); e.Handled = true; }
    private void SkipForward_MouseDown(object sender, MouseButtonEventArgs e) { BeginSkipAction(1); e.Handled = true; }
    private void Skip_MouseUp(object sender, MouseEventArgs e) { EndSkipAction(); }

    private void CC_Click(object sender, RoutedEventArgs e)
    {
        // Toggle the visual layer on/off
        _mpvService.CycleSubtitles();
        
        // Fetch the exact visibility state instantly
        string currentVis = _mpvService.GetMpvProperty("sub-visibility");
        bool isCcActive = currentVis == "yes";

        // Turn the CC button Blue when ON, White when OFF
        CcButton.Foreground = new System.Windows.Media.SolidColorBrush(
            isCcActive ? System.Windows.Media.Color.FromRgb(0, 164, 239) : System.Windows.Media.Colors.White);

        WakeUpUi();
    }
    
    private void Anime_Click(object sender, RoutedEventArgs e)
    {
        bool isAnimeActive = _mpvService.ToggleAnimeMode();
        
        AnimeButton.Foreground = new System.Windows.Media.SolidColorBrush(
            isAnimeActive ? System.Windows.Media.Color.FromRgb(139, 0, 0) : System.Windows.Media.Colors.White);
            
        WakeUpUi();
    }
	
	private void StatsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleStatsForNerds();
        WakeUpUi(); // Keep the controls visible when clicking
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

        // AGGRESSIVE FOCUS FIX: Evaluate focus every time you touch the remote, 
        // not just when the UI fades in. If focus is lost in the void (Window/Grid), 
        // instantly snap it back to Play/Pause.
        var currentFocus = Keyboard.FocusedElement as FrameworkElement;
        if (currentFocus == null || currentFocus == this || currentFocus is Grid || currentFocus is Border)
        {
            PlayPauseButton.Focus();
            Keyboard.Focus(PlayPauseButton);
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