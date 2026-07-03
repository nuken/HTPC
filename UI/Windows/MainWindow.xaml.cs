using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using HTPC.UI.Views;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Windows;

public partial class MainWindow : Window
{
    private readonly DashboardView _dashboardView;
    private readonly PlayerView _playerView;
    private readonly SettingsView _settingsView;
    private readonly GuideView _guideView;
    private readonly MoviesView _moviesView;
    private readonly ShowsView _showsView;
    private readonly VideosView _videosView;
	private readonly RecordingsView _recordingsView;
    
    // --- THE MULTIVIEW VARIABLES ---
    private readonly MultiviewSetupView _multiviewSetupView;
    private readonly ServerManagerService _serverManager;
    
    private object? _previousView;
    private bool _isFullscreen = true;
	private Point _lastMousePosition;

    public MainWindow(DashboardView dashboardView, PlayerView playerView, SettingsView settingsView, GuideView guideView, MoviesView moviesView, ShowsView showsView, VideosView videosView, RecordingsView recordingsView, MultiviewSetupView multiviewSetupView, ServerManagerService serverManager)
    {
        InitializeComponent();
        
        _dashboardView = dashboardView;
        _playerView = playerView;
        _settingsView = settingsView;
        _guideView = guideView;
        _moviesView = moviesView; 
        _showsView = showsView;
        _videosView = videosView;
        _recordingsView = recordingsView; // Assign the injected view
        _multiviewSetupView = multiviewSetupView;
        _serverManager = serverManager;

        // --- MULTIVIEW WIRING ---
        _multiviewSetupView.OnLaunchMultiviewRequested += LaunchMultiviewPlayer;
        
        // (Temporary Route: If you press 'M' on the Dashboard, it opens the Setup screen)
        _dashboardView.PreviewKeyDown += (s, e) => 
        {
            if (e.Key == Key.M) MainShellContainer.Content = _multiviewSetupView;
        };
        
        // --- RESTORED NAVIGATION WIRING ---
        _settingsView.OnHomeRequested += NavigateToDashboard;
        _settingsView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _settingsView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _settingsView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _settingsView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        _settingsView.OnRecordingsRequested += (s, e) => MainShellContainer.Content = _recordingsView;
        
        _dashboardView.OnPlayRequested += PlayMedia;
        _dashboardView.OnExitRequested += Dashboard_ExitRequested;
        _dashboardView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView; 
        _dashboardView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _dashboardView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _dashboardView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _dashboardView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        _dashboardView.OnRecordingsRequested += (s, e) => MainShellContainer.Content = _recordingsView;
        
        _guideView.OnHomeRequested += NavigateToDashboard;
        _guideView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _guideView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _guideView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        _guideView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _guideView.OnRecordingsRequested += (s, e) => MainShellContainer.Content = _recordingsView;
        _guideView.OnPlayRequested += PlayMedia;
        
        _moviesView.OnHomeRequested += NavigateToDashboard;
        _moviesView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _moviesView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _moviesView.OnPlayRequested += PlayMedia;
        _moviesView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _moviesView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        _moviesView.OnRecordingsRequested += (s, e) => MainShellContainer.Content = _recordingsView;
        
        _showsView.OnHomeRequested += NavigateToDashboard;
        _showsView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _showsView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _showsView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _showsView.OnPlayRequested += PlayMedia;
        _showsView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        _showsView.OnRecordingsRequested += (s, e) => MainShellContainer.Content = _recordingsView;
        
        _videosView.OnHomeRequested += NavigateToDashboard;
        _videosView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _videosView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _videosView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _videosView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _videosView.OnRecordingsRequested += (s, e) => MainShellContainer.Content = _recordingsView;
        _videosView.OnPlayRequested += PlayMedia;
        
        // --- NEW: RECORDINGS VIEW OUTBOUND NAVIGATION ---
        _recordingsView.OnHomeRequested += NavigateToDashboard;
        _recordingsView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _recordingsView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _recordingsView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _recordingsView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        _recordingsView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _recordingsView.OnPlayRequested += PlayMedia;
        _recordingsView.OnMultiviewRequested += (s, e) => MainShellContainer.Content = _multiviewSetupView;
		
		_dashboardView.OnMultiviewRequested += (s, e) => MainShellContainer.Content = _multiviewSetupView;
        _settingsView.OnMultiviewRequested += (s, e) => MainShellContainer.Content = _multiviewSetupView;
        _guideView.OnMultiviewRequested += (s, e) => MainShellContainer.Content = _multiviewSetupView;
        _moviesView.OnMultiviewRequested += (s, e) => MainShellContainer.Content = _multiviewSetupView;
        _showsView.OnMultiviewRequested += (s, e) => MainShellContainer.Content = _multiviewSetupView;
        _videosView.OnMultiviewRequested += (s, e) => MainShellContainer.Content = _multiviewSetupView;
        
		_multiviewSetupView.OnHomeRequested += NavigateToDashboard;
        _multiviewSetupView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _multiviewSetupView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _multiviewSetupView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _multiviewSetupView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        _multiviewSetupView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _multiviewSetupView.OnRecordingsRequested += (s, e) => MainShellContainer.Content = _recordingsView;
        
        _playerView.OnBackRequested += (s, e) => MainShellContainer.Content = _previousView ?? _dashboardView;


        MainShellContainer.Content = _dashboardView;
        
        // --- Restore window layout preferences on boot ---
        InitializeWindowState();
		ApplyGlobalUiScale();
		this.PreviewMouseMove += Window_PreviewMouseMove;
    }
    
    // ==========================================
    // TITLE BAR & WINDOW STATE LOGIC
    // ==========================================

    private void InitializeWindowState()
    {
        var prefs = PreferencesManager.Load();
        _isFullscreen = prefs.IsFullscreen;

        if (_isFullscreen)
        {
            WindowState = WindowState.Maximized;
            TitleBarRow.Height = new GridLength(0); // Hide title bar
        }
        else
        {
            WindowState = WindowState.Normal;
            TitleBarRow.Height = new GridLength(32); // Show title bar
            
            // Reapply saved coordinates
            if (prefs.WindowWidth > 0)
            {
                this.Width = prefs.WindowWidth;
                this.Height = prefs.WindowHeight;
                this.Top = prefs.WindowTop;
                this.Left = prefs.WindowLeft;
            }
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) 
        {
            ToggleFullscreen();
        }
        else if (e.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Normal) 
        {
            DragMove(); 
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    public void ToggleFullscreen()
    {
        var prefs = PreferencesManager.Load();

        if (_isFullscreen)
        {
            _isFullscreen = false;
            WindowState = WindowState.Normal;
            TitleBarRow.Height = new GridLength(32);
            MaximizeRestoreBtn.Content = "🗖";

            if (prefs.WindowWidth > 0)
            {
                this.Width = prefs.WindowWidth;
                this.Height = prefs.WindowHeight;
                this.Top = prefs.WindowTop;
                this.Left = prefs.WindowLeft;
            }
        }
        else
        {
            // Save the exact floating coordinates before blowing it up to Fullscreen
            prefs.WindowWidth = this.ActualWidth;
            prefs.WindowHeight = this.ActualHeight;
            prefs.WindowTop = this.Top;
            prefs.WindowLeft = this.Left;

            _isFullscreen = true;
            WindowState = WindowState.Maximized;
            TitleBarRow.Height = new GridLength(0);
            MaximizeRestoreBtn.Content = "🗗";
        }

        prefs.IsFullscreen = _isFullscreen;
        PreferencesManager.Save(prefs);
    }
	
	private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        Point currentPosition = e.GetPosition(this);

        // Only restore the cursor if the mouse physically moved more than 2 pixels
        if (Math.Abs(currentPosition.X - _lastMousePosition.X) > 2 || 
            Math.Abs(currentPosition.Y - _lastMousePosition.Y) > 2)
        {
            if (Mouse.OverrideCursor == Cursors.None)
            {
                Mouse.OverrideCursor = null; // Unhide the cursor
            }
            
            _lastMousePosition = currentPosition;
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        var prefs = PreferencesManager.Load();
        prefs.IsFullscreen = _isFullscreen;

        // If closing in windowed mode, save the current dimensions so it boots back perfectly
        if (!_isFullscreen)
        {
            prefs.WindowWidth = this.ActualWidth;
            prefs.WindowHeight = this.ActualHeight;
            prefs.WindowTop = this.Top;
            prefs.WindowLeft = this.Left;
        }

        PreferencesManager.Save(prefs);
    }

    // ==========================================
    // GLOBAL KEYBOARD SHORTCUTS
    // ==========================================

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Mouse.OverrideCursor = Cursors.None;
		// 1. THE 'F' KEY HANDLER
        if (e.Key == Key.F)
        {
            // CRITICAL: Ignore if the user is typing into a search box or IP address field
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is PasswordBox) return;

            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        // 2. UNIVERSAL BACK / ESCAPE HANDLER
        if (e.Key == Key.Escape)
        {
            if (MainShellContainer.Content == _playerView)
            {
                _playerView.StopPlayback();
                MainShellContainer.Content = _previousView ?? _dashboardView;
                e.Handled = true;
            }
            else if (MainShellContainer.Content == _settingsView || MainShellContainer.Content == _guideView || MainShellContainer.Content == _multiviewSetupView) 
        {
            MainShellContainer.Content = _dashboardView;
            e.Handled = true;
        }
            else if (MainShellContainer.Content == _dashboardView)
            {
                Application.Current.Shutdown();
            }
            return;
        }

        // 3. IGNORE OTHER HOTKEYS IF WATCHING VIDEO
        if (MainShellContainer.Content is PlayerView) return;

        // 4. GUIDE HOTKEY
        if (e.Key == Key.G && MainShellContainer.Content == _dashboardView)
        {
            MainShellContainer.Content = _guideView;
            e.Handled = true;
            return;
        }

        // 5. STANDARD NAVIGATION (REMOTE BACK/HOME)
        var command = Core.Input.InputMapper.GetCommand(e.Key);

        if (command == Core.Input.HtpcCommand.Home)
        {
            NavigateToDashboard(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (command == Core.Input.HtpcCommand.Back)
        {
            if (MainShellContainer.Content != _dashboardView)
            {
                NavigateToDashboard(this, EventArgs.Empty);
                e.Handled = true;
            }
        }
    }
	
    // ==========================================
    // GLOBAL UI SCALING
    // ==========================================
    public void ApplyGlobalUiScale()
    {
        var prefs = PreferencesManager.Load();
        double scale = prefs.UiScaleMultiplier;

        // Ensure scale doesn't accidentally get set to 0 or something invisible
        if (scale < 0.5) scale = 1.0;

        // Apply a vector scale to the entire application shell
        MainShellContainer.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
    }

    // ==========================================
    // NAVIGATION ROUTING
    // ==========================================
    
    private void NavigateToDashboard(object? sender, EventArgs e)
    {
        MainShellContainer.Content = _dashboardView;
    }

    private void PlayMedia(object? sender, MediaItem media)
    {
        _previousView = MainShellContainer.Content;
        MainShellContainer.Content = _playerView;
        _playerView.StartPlayback(media); 
    }

    private void Dashboard_ExitRequested(object? sender, EventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void LaunchMultiviewPlayer(object? sender, System.Collections.Generic.List<Channel> channels)
    {
        var multiWindow = new MultiviewPlayerWindow(channels, _serverManager)
        {
            Owner = this // Ties the player strictly to the main window
        };
        
        // Hide the background app entirely so no double-clicks can occur
        this.Hide(); 
        
        // ShowDialog blocks the thread until the user explicitly closes the player
        multiWindow.ShowDialog(); 
        
        // Bring the app back once the player is closed
        this.Show(); 
        MainShellContainer.Focus(); 
    }
	
	// --- LOW LEVEL HARDWARE REMOTE CONTROL HOOKS ---
    private const int WM_APPCOMMAND = 0x0319;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Hook into the Windows message loop
        var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Catch hardware media buttons that bypass WPF's KeyDown events
        if (msg == WM_APPCOMMAND)
        {
            int cmd = (int)((uint)lParam >> 16) & ~0xf000;
            
            HTPC.Core.Input.HtpcCommand resolvedCommand = HTPC.Core.Input.HtpcCommand.None;

            switch (cmd)
            {
                case 8:  // APPCOMMAND_BROWSER_BACKWARD
                    resolvedCommand = HTPC.Core.Input.HtpcCommand.Back;
                    break;
                case 14: // APPCOMMAND_MEDIA_PLAY_PAUSE
                    resolvedCommand = HTPC.Core.Input.HtpcCommand.PlayPause;
                    break;
                case 11: // APPCOMMAND_MEDIA_NEXTTRACK
                    resolvedCommand = HTPC.Core.Input.HtpcCommand.SkipForward;
                    break;
                case 12: // APPCOMMAND_MEDIA_PREVIOUSTRACK
                    resolvedCommand = HTPC.Core.Input.HtpcCommand.SkipBackward;
                    break;
            }

            if (resolvedCommand != HTPC.Core.Input.HtpcCommand.None)
            {
                // Simulate a key press event so the active UserControl can handle it naturally
                var keyEvent = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, Key.None)
                {
                    RoutedEvent = UIElement.PreviewKeyDownEvent
                };
                
                // FIX: Changed MainContent to MainShellContainer to match your UI
                if (MainShellContainer.Content is UIElement activeView)
                {
                    // This forces standard handling as if they pressed a normal key
                    if (resolvedCommand == HTPC.Core.Input.HtpcCommand.Back)
                        activeView.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, Key.BrowserBack) { RoutedEvent = UIElement.PreviewKeyDownEvent });
                    else if (resolvedCommand == HTPC.Core.Input.HtpcCommand.PlayPause)
                        activeView.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, Key.MediaPlayPause) { RoutedEvent = UIElement.PreviewKeyDownEvent });
                    else if (resolvedCommand == HTPC.Core.Input.HtpcCommand.SkipForward)
                        activeView.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, Key.MediaNextTrack) { RoutedEvent = UIElement.PreviewKeyDownEvent });
                    else if (resolvedCommand == HTPC.Core.Input.HtpcCommand.SkipBackward)
                        activeView.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, Key.MediaPreviousTrack) { RoutedEvent = UIElement.PreviewKeyDownEvent });
                }
                handled = true;
            }
        }
        return IntPtr.Zero;
    }
}