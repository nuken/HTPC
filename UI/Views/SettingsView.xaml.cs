using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Services;
using System.Net.Http;
using System.Threading.Tasks;
using HTPC.Core.Input; // Required for remote control commands
using HTPC.Core.Models;

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
	public System.Collections.ObjectModel.ObservableCollection<DashboardRowConfig> DashboardRows { get; set; } = new System.Collections.ObjectModel.ObservableCollection<DashboardRowConfig>();

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
            // The Heavy Hammer Focus Fix for returning to the page
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                HomeNavBtn.Focus(); 
                Keyboard.Focus(HomeNavBtn);
            }), DispatcherPriority.ApplicationIdle);
            return;
        }

        var prefs = PreferencesManager.Load();
		
		// Load Commercial Skip Mode
        if (prefs.CommercialSkipMode >= 0 && prefs.CommercialSkipMode <= 2)
        {
            CommercialSkipBox.SelectedIndex = prefs.CommercialSkipMode;
        }
        else
        {
            CommercialSkipBox.SelectedIndex = 2; // Default to Auto
        }
        // Load Dashboard Layout
        DashboardRows.Clear();
        if (prefs.DashboardLayout != null)
        {
            foreach (var row in prefs.DashboardLayout.OrderBy(r => r.Order))
            {
                DashboardRows.Add(row);
            }
        }
        DashboardLayoutList.ItemsSource = DashboardRows;
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

       // The Heavy Hammer Focus Fix
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                SettingsNavBtn.Focus();          // <-- Changed
                Keyboard.Focus(SettingsNavBtn);  // <-- Changed
            }), DispatcherPriority.ApplicationIdle);
    }

    // --- 10-FOOT UI FOCUS TRAPS & ESCAPE HATCHES ---

    private void Dropdown_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var cb = sender as ComboBox;
        var command = InputMapper.GetCommand(e.Key);

        if (cb != null && !cb.IsDropDownOpen)
        {
            if (command == HtpcCommand.Down || command == HtpcCommand.Up || command == HtpcCommand.Left || command == HtpcCommand.Right)
            {
                var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down :
                                command == HtpcCommand.Up ? FocusNavigationDirection.Up :
                                command == HtpcCommand.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right;

                cb.MoveFocus(new TraversalRequest(direction));
                e.Handled = true; 
            }
        }
    }

    private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Down || command == HtpcCommand.Up)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as TextBox)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
    }

    private void Slider_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Down || command == HtpcCommand.Up)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as Slider)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
    }

    private void CheckBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Down || command == HtpcCommand.Up)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as CheckBox)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
    }

    private void DashboardListItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        // Explicit Focus Bridge: Jump RIGHT to the Action Buttons
        if (command == HtpcCommand.Right)
        {
            LayoutMoveUpBtn.Focus();
            e.Handled = true;
        }
        // Notice we removed the 'Select' override! Now hitting Enter just selects the row normally.
    }

    private void LayoutActionBtn_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        // Explicit Focus Bridge: Jump LEFT back to the Layout List
        if (command == HtpcCommand.Left)
        {
            if (DashboardLayoutList.SelectedItem != null)
            {
                var item = DashboardLayoutList.ItemContainerGenerator.ContainerFromItem(DashboardLayoutList.SelectedItem) as UIElement;
                item?.Focus();
            }
            else
            {
                DashboardLayoutList.Focus();
            }
            e.Handled = true;
        }
        // Explicit Focus Bridge: Jump RIGHT to the Saved Servers List
        else if (command == HtpcCommand.Right)
        {
            if (SavedServersList.Items.Count > 0)
            {
                var item = SavedServersList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                item?.Focus();
            }
            else
            {
                TxtName.Focus(); // Fallback if no servers exist
            }
            e.Handled = true;
        }
    }

    private void SavedServerItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        // Let user hit "Enter" on a saved server to make it active instantly
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is ServerConfig server)
        {
            _serverManager.SetActiveServer(server.Id);
            LoadServers();
            e.Handled = true;
        }
        // Explicit Focus Bridge: Jump LEFT back to the center column buttons
        else if (command == HtpcCommand.Left)
        {
            LayoutMoveUpBtn.Focus(); 
            e.Handled = true;
        }
    }

    // --- END FOCUS TRAPS ---

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
        
        HomeNavBtn.Focus(); // Re-focus navigation instead of getting stuck
    }

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
	
	private void MakeActive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is HTPC.Core.Models.ServerConfig server)
        {
            _serverManager.SetActiveServer(server.Id); 
            LoadServers();
        }
    }
	
	private void AdminMenu_Click(object sender, RoutedEventArgs e)
    {
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
	
	private void CommercialSkip_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        
        var prefs = PreferencesManager.Load();
        prefs.CommercialSkipMode = CommercialSkipBox.SelectedIndex;
        PreferencesManager.Save(prefs);
    }
	
	// --- DASHBOARD LAYOUT LOGIC ---

    private void MoveRowUp_Click(object sender, RoutedEventArgs e)
    {
        int index = DashboardLayoutList.SelectedIndex;
        if (index > 0)
        {
            var item = DashboardRows[index];
            DashboardRows.RemoveAt(index);
            DashboardRows.Insert(index - 1, item);
            DashboardLayoutList.SelectedIndex = index - 1; // Keep it selected
            SaveDashboardLayout();
        }
    }

    private void MoveRowDown_Click(object sender, RoutedEventArgs e)
    {
        int index = DashboardLayoutList.SelectedIndex;
        if (index >= 0 && index < DashboardRows.Count - 1)
        {
            var item = DashboardRows[index];
            DashboardRows.RemoveAt(index);
            DashboardRows.Insert(index + 1, item);
            DashboardLayoutList.SelectedIndex = index + 1; // Keep it selected
            SaveDashboardLayout();
        }
    }

    private void ToggleRowVisibility(DashboardRowConfig row)
    {
        row.IsVisible = !row.IsVisible;
        
        int index = DashboardRows.IndexOf(row);
        if(index >= 0)
        {
            DashboardRows.RemoveAt(index);
            DashboardRows.Insert(index, row);
            DashboardLayoutList.SelectedIndex = index;
            SaveDashboardLayout();
        }
    }

    private void ToggleRowVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (DashboardLayoutList.SelectedItem is DashboardRowConfig row)
        {
            ToggleRowVisibility(row);
        }
    }

    private void SaveDashboardLayout()
    {
        var prefs = PreferencesManager.Load();
        prefs.DashboardLayout = new System.Collections.Generic.List<DashboardRowConfig>();
        
        for (int i = 0; i < DashboardRows.Count; i++)
        {
            DashboardRows[i].Order = i; // Update the order integer based on physical list position
            prefs.DashboardLayout.Add(DashboardRows[i]);
        }
        
        PreferencesManager.Save(prefs);
    }
	
	private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
    
    // --- NAVIGATION SIGNATURES ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
	private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
}