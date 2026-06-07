using System;
using System.Windows;
using System.Windows.Input;
using HTPC.UI.Views;
using HTPC.Core.Models; // Fixes the missing MediaItem reference

namespace HTPC.UI.Windows;

public partial class MainWindow : Window
{
    private readonly DashboardView _dashboardView;
    private readonly PlayerView _playerView;
    private readonly SettingsView _settingsView; // NEW

    public MainWindow(DashboardView dashboardView, PlayerView playerView, SettingsView settingsView)
    {
        InitializeComponent();
        _dashboardView = dashboardView;
        _playerView = playerView;
        _settingsView = settingsView;
		_settingsView.OnHomeRequested += Settings_HomeRequested;
        _dashboardView.OnPlayRequested += Dashboard_PlayRequested;
        _dashboardView.OnExitRequested += Dashboard_ExitRequested;
        _dashboardView.OnSettingsRequested += Dashboard_SettingsRequested; // NEW

        MainShellContainer.Content = _dashboardView;
    }
	
	private void Settings_HomeRequested(object? sender, EventArgs e)
    {
        MainShellContainer.Content = _dashboardView;
    }

    private void Dashboard_PlayRequested(object? sender, MediaItem media)
    {
        MainShellContainer.Content = _playerView;
        _playerView.StartPlayback(media); 
    }

    private void Dashboard_ExitRequested(object? sender, EventArgs e)
    {
        Application.Current.Shutdown();
    }
	
	private void Dashboard_SettingsRequested(object? sender, EventArgs e)
    {
        // Swap to settings page
        MainShellContainer.Content = _settingsView;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (MainShellContainer.Content == _playerView)
            {
                _playerView.StopPlayback();
                MainShellContainer.Content = _dashboardView;
            }
            // NEW: If we are on Settings, ESC goes back to Dashboard
            else if (MainShellContainer.Content == _settingsView) 
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