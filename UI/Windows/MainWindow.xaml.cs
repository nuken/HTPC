using System;
using System.Windows;
using System.Windows.Input;
using HTPC.UI.Views;
using HTPC.Core.Models;

namespace HTPC.UI.Windows;

public partial class MainWindow : Window
{
    private readonly DashboardView _dashboardView;
    private readonly PlayerView _playerView;
    private readonly SettingsView _settingsView;
    private readonly GuideView _guideView;
	private readonly MoviesView _moviesView;
	private readonly ShowsView _showsView;

    public MainWindow(DashboardView dashboardView, PlayerView playerView, SettingsView settingsView, GuideView guideView, MoviesView moviesView)
    {
        InitializeComponent();
        _dashboardView = dashboardView;
        _playerView = playerView;
        _settingsView = settingsView;
        _guideView = guideView;
		_moviesView = moviesView;
        
        _settingsView.OnHomeRequested += NavigateToDashboard;
        
        _dashboardView.OnPlayRequested += PlayMedia;
        _dashboardView.OnExitRequested += Dashboard_ExitRequested;
        _dashboardView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView; 
		_dashboardView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _dashboardView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _dashboardView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
        
		
		// NEW: Wire up the Guide events
        _guideView.OnBackRequested += NavigateToDashboard;
        _guideView.OnPlayRequested += PlayMedia;
		
		_moviesView.OnHomeRequested += NavigateToDashboard;
        _moviesView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _moviesView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _moviesView.OnPlayRequested += PlayMedia;
		_moviesView.OnShowsRequested += (s, e) => MainShellContainer.Content = _showsView;
		
		_showsView.OnHomeRequested += NavigateToDashboard;
        _showsView.OnGuideRequested += (s, e) => MainShellContainer.Content = _guideView;
        _showsView.OnMoviesRequested += (s, e) => MainShellContainer.Content = _moviesView;
        _showsView.OnSettingsRequested += (s, e) => MainShellContainer.Content = _settingsView;
        _showsView.OnPlayRequested += PlayMedia;
		
		_playerView.OnBackRequested += NavigateToDashboard;

        MainShellContainer.Content = _dashboardView;
    }
    
    private void NavigateToDashboard(object? sender, EventArgs e)
    {
        MainShellContainer.Content = _dashboardView;
    }

    private void PlayMedia(object? sender, MediaItem media)
    {
        MainShellContainer.Content = _playerView;
        _playerView.StartPlayback(media); 
    }

    private void Dashboard_ExitRequested(object? sender, EventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // OPEN GUIDE HOTKEY
        if (e.Key == Key.G && MainShellContainer.Content == _dashboardView)
        {
            MainShellContainer.Content = _guideView;
            e.Handled = true;
            return;
        }

        // UNIVERSAL BACK BUTTON
        if (e.Key == Key.Escape)
        {
            if (MainShellContainer.Content == _playerView)
            {
                _playerView.StopPlayback();
                MainShellContainer.Content = _dashboardView;
            }
            else if (MainShellContainer.Content == _settingsView || MainShellContainer.Content == _guideView) 
            {
                MainShellContainer.Content = _dashboardView;
            }
            else if (MainShellContainer.Content == _dashboardView)
            {
                Application.Current.Shutdown();
            }
        }
    }
}