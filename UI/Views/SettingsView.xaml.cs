using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HTPC.Services;
using System.Net.Http;
using System.Threading.Tasks;

namespace HTPC.UI.Views;

public partial class SettingsView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;
	public event EventHandler? OnMultiviewRequested;

    private readonly ServerManagerService _serverManager;
    private bool _isInitialized = false;
	private static readonly HttpClient _httpClient = new HttpClient();

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
        
        // Load UI Scale
        UiScaleSlider.Value = prefs.UiScaleMultiplier;
        UiScaleTextText.Text = $"{(int)(prefs.UiScaleMultiplier * 100)}%";

        // Load Video Processing
        EnableUpscalingCheck.IsChecked = prefs.EnableUpscaling;
        UpscalerPresetBox.IsEnabled = prefs.EnableUpscaling; // Gray out dropdown if disabled
        
        if (prefs.UpscalerPreset == "ArtCNN") UpscalerPresetBox.SelectedIndex = 1;
        else UpscalerPresetBox.SelectedIndex = 0; // Default to RAVU

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

        // Instantly update the text so the user sees the percentage changing
        double newScale = Math.Round(e.NewValue, 1);
        UiScaleTextText.Text = $"{(int)(newScale * 100)}%";

        // If the user is physically holding down the mouse to drag, DO NOT scale the UI yet!
        if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed) 
            return;

        // If they clicked the track or used the keyboard, apply it instantly
        ApplyUiScale(newScale);
    }

    private void UiScaleSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        // Once they let go of the mouse, apply the actual scale
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
    
    private void Upscaler_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        
        var prefs = PreferencesManager.Load();
        
        // Save the Checkbox state
        prefs.EnableUpscaling = EnableUpscalingCheck.IsChecked == true;
        
        // Enable or Disable the dropdown visually based on the checkbox
        UpscalerPresetBox.IsEnabled = prefs.EnableUpscaling;
        
        // Save the dropdown string
        if (UpscalerPresetBox.SelectedIndex == 1) prefs.UpscalerPreset = "ArtCNN";
        else prefs.UpscalerPreset = "RAVU";
        
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
        
        this.Focus(); // Prevent focus from getting trapped after clicking save
    }

    // --- NEW DELETE SERVER METHOD ---
    private void DeleteServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is HTPC.Core.Models.ServerConfig server)
        {
            var result = MessageBox.Show($"Are you sure you want to delete the connection to '{server.Name}'?", 
                                         "Confirm Delete", 
                                         MessageBoxButton.YesNo, 
                                         MessageBoxImage.Warning);
                                         
            if (result == MessageBoxResult.Yes)
            {
                _serverManager.DeleteServer(server.Id);
                LoadServers(); // Refresh the list
            }
        }
    }
	
	private void AdminMenu_Click(object sender, RoutedEventArgs e)
    {
        // Left-clicking the button opens the context menu 
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.IsOpen = true;
        }
    }

    private async void AdminAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string endpoint)
        {
            var activeServer = _serverManager.GetActiveServer();
            if (activeServer == null)
            {
                MessageBox.Show("No active server selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string url = $"http://{activeServer.IpAddress}:{activeServer.Port}{endpoint}";

            try
            {
                HttpResponseMessage response;

                // Explicitly route DELETE for the cache, and PUT for everything else
                if (endpoint == "/dvr/cache")
                {
                    response = await _httpClient.DeleteAsync(url);
                }
                else
                {
                    response = await _httpClient.PutAsync(url, new StringContent(""));
                }

                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show($"Command '{item.Header}' sent successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Read the error message from Channels if one exists to help with debugging
                    string errorText = await response.Content.ReadAsStringAsync();
                    MessageBox.Show($"Server returned {response.StatusCode}\n{errorText}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect to server: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    // --- NAVIGATION SIGNATURES ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
	private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
}