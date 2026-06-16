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
    private object? _previousView;
    
    private bool _isFullscreen = true;

    public MainWindow(DashboardView dashboardView, PlayerView playerView, SettingsView settingsView, GuideView guideView, MoviesView moviesView, ShowsView showsView, VideosView videosView)
    {
        InitializeComponent();
        
        _dashboardView = dashboardView;
        _playerView = playerView;
        _settingsView = settingsView;
        _guideView = guideView;
        _moviesView = moviesView; 
        _showsView = showsView;
        _videosView = videosView;
        
        _settingsView.OnHomeRequested += NavigateToDashboard;
        _settingsView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _settingsView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _settingsView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _settingsView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        
        _dashboardView.OnPlayRequested += PlayMedia;
        _dashboardView.OnExitRequested += Dashboard_ExitRequested;
        _dashboardView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView; 
        _dashboardView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _dashboardView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _dashboardView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _dashboardView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        
        _guideView.OnHomeRequested += NavigateToDashboard;
        _guideView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _guideView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _guideView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        _guideView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _guideView.OnPlayRequested += PlayMedia;
        
        _moviesView.OnHomeRequested += NavigateToDashboard;
        _moviesView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _moviesView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _moviesView.OnPlayRequested += PlayMedia;
        _moviesView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _moviesView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        
        _showsView.OnHomeRequested += NavigateToDashboard;
        _showsView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _showsView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _showsView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _showsView.OnPlayRequested += PlayMedia;
        _showsView.OnVideosRequested += (s, e) => MainShellContainer.Content = _videosView;
        
        _videosView.OnHomeRequested += NavigateToDashboard;
        _videosView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _videosView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _videosView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        _videosView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _videosView.OnPlayRequested += PlayMedia;
        
        _playerView.OnBackRequested += (s, e) => MainShellContainer.Content = _previousView ?? _dashboardView;

        MainShellContainer.Content = _dashboardView;
        
        // --- Restore window layout preferences on boot ---
        InitializeWindowState();
		ApplyGlobalUiScale();
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
            else if (MainShellContainer.Content == _settingsView || MainShellContainer.Content == _guideView) 
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
}