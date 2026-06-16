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
            this.Focus(); // Return focus to the page generally, NOT a textbox
            return;
        }

        var prefs = PreferencesManager.Load();
        
        UiScaleSlider.Value = prefs.UiScaleMultiplier;
        UiScaleTextText.Text = $"{(int)(prefs.UiScaleMultiplier * 100)}%";

        _isInitialized = true;
        LoadServers();

        for (int i = 0; i <= 30; i++)
        {
            string label = i == 0 ? "None" : $"{i} Min";
            PaddingStartBox.Items.Add(label); 
            PaddingEndBox.Items.Add(label);
        }
        PaddingStartBox.SelectedIndex = prefs.PaddingStartMinutes <= 30 ? prefs.PaddingStartMinutes : 0;
        PaddingEndBox.SelectedIndex = prefs.PaddingEndMinutes <= 30 ? prefs.PaddingEndMinutes : 0;

        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            this.Focus(); // Ensure focus doesn't lock into the textbox on load
        }), DispatcherPriority.Input);
    }

    private void Padding_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        
        var prefs = PreferencesManager.Load();
        
        prefs.PaddingStartMinutes = Math.Max(0, PaddingStartBox.SelectedIndex);
        prefs.PaddingEndMinutes = Math.Max(0, PaddingEndBox.SelectedIndex);
        
        PreferencesManager.Save(prefs);
    }
    
    private void UiScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (UiScaleTextText == null) return;

        // 1. Instantly update the text so the user sees the percentage changing
        double newScale = Math.Round(e.NewValue, 1);
        UiScaleTextText.Text = $"{(int)(newScale * 100)}%";

        // 2. If the user is physically holding down the mouse to drag, DO NOT scale the UI yet!
        // This prevents the slider from jumping out from under their cursor.
        if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed) 
            return;

        // 3. If they clicked the track or used the keyboard, apply it instantly
        ApplyUiScale(newScale);
    }

    private void UiScaleSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        // 4. Once they let go of the mouse, apply the actual scale
        double newScale = Math.Round(UiScaleSlider.Value, 1);
        ApplyUiScale(newScale);
    }

    private void ApplyUiScale(double newScale)
    {
        var prefs = PreferencesManager.Load();
        prefs.UiScaleMultiplier = newScale;
        PreferencesManager.Save(prefs);

        if (Application.Current.MainWindow is HTPC.UI.Windows.MainWindow mainWindow)
        {
            mainWindow.ApplyGlobalUiScale();
        }
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
        
        this.Focus(); // Prevent focus from getting trapped after clicking save
    }
    
    // --- NAVIGATION SIGNATURES ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
}