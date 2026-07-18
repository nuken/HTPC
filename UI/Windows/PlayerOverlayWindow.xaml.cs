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
	
	public event EventHandler<MediaItem>? OnPlayNextInQueue;
    private bool _autoAdvanceTriggered = false;

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
    private DateTime _initTime = DateTime.MinValue;
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
		_mpvService.OnMediaLoaded += MpvService_OnMediaLoaded;

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
        _mpvService.OnMediaLoaded -= MpvService_OnMediaLoaded; // Unhook to prevent leaks
        
        _idleTimer?.Stop();
        _syncTimer?.Stop();
        _skipAdTimer?.Stop();
        _statsTimer?.Stop();
        Mouse.OverrideCursor = null; 
    }
	
	private void MpvService_OnMediaLoaded()
    {
        Dispatcher.Invoke(() => 
        {
            BufferingOverlay.Visibility = Visibility.Collapsed;

            // --- NEW: Reset the grace period when the video actually appears! ---
            Mouse.OverrideCursor = Cursors.None;
            _lastMousePosition = Mouse.GetPosition(this);
            _initTime = DateTime.UtcNow;
        });
    }

    public void InitializeMedia(MediaItem media, MediaItem? nextInQueue = null)
    {
        _currentMedia = media;
        _autoAdvanceTriggered = false; // Reset for new video
        
        ShowTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? "" : media.Title;
        MediaTitleText.Text = string.IsNullOrEmpty(media.CurrentShowTitle) ? media.Title : media.CurrentShowTitle;
        
        _isLiveTv = media.IsLiveTv; 
        
        _upNextPromptShown = false;
        UpNextPromptContainer.Visibility = Visibility.Collapsed;
        _nextEpisodeToPlay = null;

        // NEW: Override the API lookup if the Binge Queue passed us the next item explicitly
        if (nextInQueue != null)
        {
            _nextEpisodeToPlay = nextInQueue;
        }
        else if (!_isLiveTv)
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

        // --- NEW: Instantly hide cursor and prime the overlay's anti-jitter tracker ---
        Mouse.OverrideCursor = Cursors.None;
        _lastMousePosition = Mouse.GetPosition(this);
        _initTime = DateTime.UtcNow;

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
        Mouse.OverrideCursor = Cursors.None;
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
		
		if (e.Key == Key.Space)
        {
            // Don't trigger if they are actively navigating the guide
            if (MiniGuideContainer.Visibility == Visibility.Collapsed)
            {
                PlayPause_Click(null!, null!);
            }
            e.Handled = true; return;
        }

        if (e.Key == Key.C)
        {
            CC_Click(null!, null!);
            e.Handled = true; return;
        }

        if (e.Key == Key.A)
        {
            Anime_Click(null!, null!);
            e.Handled = true; return;
        }

        if (e.Key == Key.M)
        {
            _mpvService.ToggleMute();
            e.Handled = true; return;
        }
		
       if (e.Key == Key.OemPlus || e.Key == Key.Add || e.Key == Key.VolumeUp)
        {
            if (VolumeSlider.Value < 100) VolumeSlider.Value += 5;
            e.Handled = true; return;
        }

        // Handle Volume Down (-, _, or Hardware Remote Volume Down)
        if (e.Key == Key.OemMinus || e.Key == Key.Subtract || e.Key == Key.VolumeDown)
        {
            if (VolumeSlider.Value > 0) VolumeSlider.Value -= 5;
            e.Handled = true; return;
        }

        var command = InputMapper.GetCommand(e.Key);

        if (command == HtpcCommand.None) return;
		
		if (command == HtpcCommand.Up || command == HtpcCommand.Down || 
            command == HtpcCommand.Left || command == HtpcCommand.Right)
        {
            var currentFocus = Keyboard.FocusedElement as FrameworkElement;
            if (currentFocus == null || currentFocus == this || currentFocus is Grid || currentFocus is Border)
            {
                PlayPauseButton.Focus();
                e.Handled = true; 
                return;
            }
        }

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
            // --- FIX: Read the user's actual current selection from Preferences ---
            var prefs = PreferencesManager.Load();
            string savedSelection = string.IsNullOrEmpty(prefs.LastGuideCollection) ? "All Channels" : prefs.LastGuideCollection;

            var activeServer = _serverManager.GetActiveServer();
            var collections = await _libraryService.GetCollectionsAsync();
            
            // Find the collection that matches their saved dropdown selection
            var targetCollection = collections.FirstOrDefault(c => c.Name == savedSelection);

            // Fetch the channels based on that exact collection
            var channels = await _libraryService.GetGuideChannelsAsync(targetCollection, 1);
            
            // Apply Secondary Static Filters exactly like the GuideView does
            if (savedSelection == "Favorites") channels = channels.Where(c => c.Favorite).ToList();
            else if (savedSelection == "HD Channels") channels = channels.Where(c => c.IsHD).ToList();
            else if (savedSelection == "SD Channels") channels = channels.Where(c => !c.IsHD).ToList();

            MiniGuideList.ItemsSource = channels;
        }

        if (MiniGuideList.Items.Count > 0)
        {
            // STEP 1: Process the logical match and physical scroll immediately
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                MiniGuideList.UpdateLayout(); 
                int targetIndex = 0; 

                if (_isLiveTv && _currentMedia != null)
                {
                    var channels = MiniGuideList.ItemsSource as System.Collections.Generic.IEnumerable<Channel>;
                    if (channels != null)
                    {
                        // AGGRESSIVE MATCHING: Check ID, then Number, then Title
                        var currentChannel = channels.FirstOrDefault(c => 
                            c.Id == _currentMedia.Id || 
                            c.Number == _currentMedia.Id || 
                            c.Name == _currentMedia.Title);
                        
                        if (currentChannel != null)
                        {
                            targetIndex = MiniGuideList.Items.IndexOf(currentChannel);
                            MiniGuideList.SelectedItem = currentChannel;
                            
                            // Force the scroll
                            MiniGuideList.ScrollIntoView(currentChannel);
                        }
                    }
                }
                else
                {
                    MiniGuideList.SelectedIndex = -1; 
                }

                // STEP 2: Wait for WPF to finish drawing the scroll, THEN grab the focus.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var item = MiniGuideList.ItemContainerGenerator.ContainerFromIndex(targetIndex) as UIElement;
                    
                    if (item != null)
                    {
                        item.Focus();
                        Keyboard.Focus(item);
                    }
                    else
                    {
                        MiniGuideList.Focus();
                        Keyboard.Focus(MiniGuideList);
                    }
                }), DispatcherPriority.ContextIdle);

            }), DispatcherPriority.Input); 
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

        //_mpvService.Stop();
        _mpvService.PlayMedia(media);
        InitializeMedia(media); 
        
        CloseMiniGuide();
    }

    private void SyncTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isPlaying || _isDragging) return;

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
            
            if (_nextEpisodeToPlay != null && !_autoAdvanceTriggered)
            {
                if (duration > 0 && (duration - position <= 120 || position / duration >= 0.95))
                {
                    if (!_upNextPromptShown) ShowUpNextPrompt();
                }
                
                // NEW: Auto-advance automatically if the video hits the end (within 2 seconds)
                if (duration > 0 && (duration - position <= 2))
                {
                    _autoAdvanceTriggered = true;
                    if (OnPlayNextInQueue != null)
                        OnPlayNextInQueue.Invoke(this, _nextEpisodeToPlay);
                    else
                        UpNextButton_Click(this, new RoutedEventArgs());
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

        // If PlayerView is running a Binge Queue, let it handle the seamless transition
        if (OnPlayNextInQueue != null)
        {
            OnPlayNextInQueue.Invoke(this, _nextEpisodeToPlay);
        }
        else
        {
            //_mpvService.Stop();
            _mpvService.PlayMedia(_nextEpisodeToPlay);
            InitializeMedia(_nextEpisodeToPlay);
        }
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

    private async void CC_Click(object sender, RoutedEventArgs e)
    {
        _mpvService.CycleSubtitles();
        
        // Give MPV 50ms to officially change the subtitle track internally
        await Task.Delay(50);
        
        // Fetch the exact active track ID
        string currentSid = _mpvService.GetMpvProperty("sid");
        bool isCcActive = currentSid != "no" && currentSid != "N/A" && !string.IsNullOrWhiteSpace(currentSid);

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
            // CHANGED: 100% Opaque Gold for maximum contrast against the dark UI
            Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 215, 0)),
            Width = CommercialMarkersCanvas.ActualWidth * widthPct,
            Height = CommercialMarkersCanvas.ActualHeight
        };

        Canvas.SetLeft(rect, CommercialMarkersCanvas.ActualWidth * startPct);
        CommercialMarkersCanvas.Children.Add(rect);
    }
}

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        // Grace Period: Ignore all phantom WPF layout mouse moves for 1 second after opening
        if ((DateTime.UtcNow - _initTime).TotalMilliseconds < 1000) return;

        Point currentPosition = e.GetPosition(this);

        if (Math.Abs(currentPosition.X - _lastMousePosition.X) > 2 || 
            Math.Abs(currentPosition.Y - _lastMousePosition.Y) > 2)
        {
            _lastMousePosition = currentPosition;
            Mouse.OverrideCursor = null; 
            WakeUpUi();
        }
    }

   private void WakeUpUi()
    {
        //Mouse.OverrideCursor = null;

        if (!_isControlsVisible)
        {
            _isControlsVisible = true;
            FadeControls(1.0); 
        }

        // *** WE DELETED THE FOCUS TRAP FROM HERE ***

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