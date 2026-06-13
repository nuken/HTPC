using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class SettingsView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;

    private readonly ServerManagerService _serverManager;
    private bool _isInitialized = false;

    public SettingsView(ServerManagerService serverManager)
    {
        InitializeComponent();
        _serverManager = serverManager;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) 
        {
            TxtName.Focus();
            return;
        }

        _isInitialized = true;
        LoadServers();

        // 1. POPULATE COMBO BOXES
        var prefs = PreferencesManager.Load();
    for (int i = 0; i <= 30; i++)
    {
        string label = i == 0 ? "None" : $"{i} Min";
        PaddingStartBox.Items.Add(label); // No more complex Tags needed!
        PaddingEndBox.Items.Add(label);
    }
    PaddingStartBox.SelectedIndex = prefs.PaddingStartMinutes <= 30 ? prefs.PaddingStartMinutes : 0;
    PaddingEndBox.SelectedIndex = prefs.PaddingEndMinutes <= 30 ? prefs.PaddingEndMinutes : 0;

        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            TxtName.Focus();
        }), DispatcherPriority.Input);
    }

    // 2. AUTO-SAVE ON CHANGE
    private void Padding_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        
        var prefs = PreferencesManager.Load();
        
        // Save the index directly as the minutes!
        prefs.PaddingStartMinutes = Math.Max(0, PaddingStartBox.SelectedIndex);
        prefs.PaddingEndMinutes = Math.Max(0, PaddingEndBox.SelectedIndex);
        
        PreferencesManager.Save(prefs);
    }

    private void LoadServers()
    {
        SavedServersList.ItemsSource = _serverManager.GetAllServers();
    }

    private void SaveServer_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtName.Text.Trim();
        string ip = TxtIp.Text.Trim();
        
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ip) || !int.TryParse(TxtPort.Text, out int port))
        {
            MessageBox.Show("Please enter a valid Name, IP, and Port.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _serverManager.AddServer(name, ip, port);
        
        TxtName.Clear();
        TxtIp.Clear();
        TxtPort.Text = "8089";
        LoadServers();

        // Return focus to the top box so they can add another
        TxtName.Focus();
    }
    
    // --- NAVIGATION SIGNATURES ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
}