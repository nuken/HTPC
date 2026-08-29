using System;
using System.Linq;
using System.Windows;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Services;
using System.Net.Http;
using System.Threading.Tasks;
using HTPC.Core.Input;
using HTPC.Core.Models;

namespace HTPC.UI.Views;

public partial class SettingsView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnRecordingsRequested;
    public event EventHandler? OnShowsRequested;
	public event EventHandler? OnSportsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnMultiviewRequested;
    public event EventHandler? OnCollectionsRequested;

    private readonly ServerManagerService _serverManager;
    private bool _isInitialized = false;
    private static readonly HttpClient _httpClient = new HttpClient();
    public System.Collections.ObjectModel.ObservableCollection<DashboardRowConfig> DashboardRows { get; set; } = new System.Collections.ObjectModel.ObservableCollection<DashboardRowConfig>();

    // Overlay State Variables
    private enum FilterMode { None, PaddingStart, PaddingEnd, CommercialSkip, UpscalerPreset, SkipForward, SkipBackward }
    private FilterMode _currentFilterMode = FilterMode.None;
    private IInputElement? _lastFocusedElement;
    private string[] _paddingOptions;

    public SettingsView(ServerManagerService serverManager)
    {
        InitializeComponent();
        LoadVersionNumber();
        _serverManager = serverManager;
        
        _paddingOptions = new string[31];
        for (int i = 0; i <= 30; i++) _paddingOptions[i] = i == 0 ? "None" : $"{i} Min";

        Loaded += OnLoaded;
    }
    
    private void LoadVersionNumber()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionString = version?.ToString(3) ?? "Unknown";
        VersionText.Text = $"Nucleus HTPC v{versionString}";
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeToggleBtn.Content = PreferencesManager.LoadTheme() == "Dark" ? "\xE708" : "\xE706";

        if (_isInitialized) 
        {
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                HomeNavBtn.Focus(); 
                Keyboard.Focus(HomeNavBtn);
            }), DispatcherPriority.ApplicationIdle);
            return;
        }

        var prefs = PreferencesManager.Load();
        
        // Load Commercial Skip Mode
        int skipMode = prefs.CommercialSkipMode >= 0 && prefs.CommercialSkipMode <= 2 ? prefs.CommercialSkipMode : 2;
        string[] skipModes = { "Disabled (Off)", "Prompt (Click to Skip)", "Automatic (Seamless)" };
        CommercialSkipBtn.Content = $"{skipModes[skipMode]} ▼";
		
		// Load Skip Amounts
        SkipForwardBtn.Content = $"{prefs.SkipForwardSeconds} Seconds ▼";
        SkipBackwardBtn.Content = $"{prefs.SkipBackwardSeconds} Seconds ▼";
        
        // Load Padding
        int pStart = prefs.PaddingStartMinutes <= 30 ? prefs.PaddingStartMinutes : 0;
        PaddingStartBtn.Content = $"{_paddingOptions[pStart]} ▼";
        
        int pEnd = prefs.PaddingEndMinutes <= 30 ? prefs.PaddingEndMinutes : 0;
        PaddingEndBtn.Content = $"{_paddingOptions[pEnd]} ▼";

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
		
		// Load Sports Score Preference
        HideScoresCheck.IsChecked = prefs.HideSportsScores;

        // Load Video Processing
        EnableUpscalingCheck.IsChecked = prefs.EnableUpscaling;
        UpscalerPresetBtn.IsEnabled = prefs.EnableUpscaling; 
        
        string preset = prefs.UpscalerPreset == "ArtCNN" ? "ArtCNN (High-End GPUs)" : "RAVU (Mid-Range GPUs)";
        UpscalerPresetBtn.Content = $"{preset} ▼";
		
		LoadReplayPreferences();

        _isInitialized = true;
        LoadServers();

        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            SettingsNavBtn.Focus();         
            Keyboard.Focus(SettingsNavBtn); 
        }), DispatcherPriority.ApplicationIdle);
    }

    // --- OVERLAY FILTERS ---

    private void PaddingStartBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.PaddingStart;
        FilterOverlayTitle.Text = "Padding Before (Start)";
        FilterSelectionList.ItemsSource = _paddingOptions;
        FilterSelectionList.SelectedItem = PaddingStartBtn.Content.ToString()?.Replace(" ▼", "");
        OpenFilterOverlay();
    }

    private void PaddingEndBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.PaddingEnd;
        FilterOverlayTitle.Text = "Padding After (End)";
        FilterSelectionList.ItemsSource = _paddingOptions;
        FilterSelectionList.SelectedItem = PaddingEndBtn.Content.ToString()?.Replace(" ▼", "");
        OpenFilterOverlay();
    }

    private void CommercialSkipBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.CommercialSkip;
        FilterOverlayTitle.Text = "Commercial Skip";
        FilterSelectionList.ItemsSource = new[] { "Disabled (Off)", "Prompt (Click to Skip)", "Automatic (Seamless)" };
        FilterSelectionList.SelectedItem = CommercialSkipBtn.Content.ToString()?.Replace(" ▼", "");
        OpenFilterOverlay();
    }
	
	private void SkipForwardBtn_Click(object sender, RoutedEventArgs e)
{
    _currentFilterMode = FilterMode.SkipForward;
    FilterOverlayTitle.Text = "Skip Forward";
    FilterSelectionList.ItemsSource = new[] { "10 Seconds", "15 Seconds", "30 Seconds", "60 Seconds" };
    FilterSelectionList.SelectedItem = SkipForwardBtn.Content.ToString()?.Replace(" ▼", "");
    OpenFilterOverlay();
}

private void SkipBackwardBtn_Click(object sender, RoutedEventArgs e)
{
    _currentFilterMode = FilterMode.SkipBackward;
    FilterOverlayTitle.Text = "Skip Backward";
    FilterSelectionList.ItemsSource = new[] { "10 Seconds", "15 Seconds", "30 Seconds", "60 Seconds" };
    FilterSelectionList.SelectedItem = SkipBackwardBtn.Content.ToString()?.Replace(" ▼", "");
    OpenFilterOverlay();
}

    private void UpscalerPresetBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.UpscalerPreset;
        FilterOverlayTitle.Text = "Upscaler Quality";
        FilterSelectionList.ItemsSource = new[] { "RAVU (Mid-Range GPUs)", "ArtCNN (High-End GPUs)" };
        FilterSelectionList.SelectedItem = UpscalerPresetBtn.Content.ToString()?.Replace(" ▼", "");
        OpenFilterOverlay();
    }
	
	private void HideScoresCheck_Changed(object sender, RoutedEventArgs e)
{
    if (!_isInitialized) return;
    
    var prefs = PreferencesManager.Load();
    prefs.HideSportsScores = HideScoresCheck.IsChecked == true;
    PreferencesManager.Save(prefs);
}

    private void OpenFilterOverlay()
    {
        FilterOverlay.Visibility = Visibility.Visible;
        _lastFocusedElement = Keyboard.FocusedElement;

        _ = Dispatcher.InvokeAsync(() =>
        {
            if (FilterSelectionList.SelectedItem != null)
            {
                FilterSelectionList.ScrollIntoView(FilterSelectionList.SelectedItem);
                var item = FilterSelectionList.ItemContainerGenerator.ContainerFromItem(FilterSelectionList.SelectedItem) as UIElement;
                item?.Focus();
            }
            else if (FilterSelectionList.Items.Count > 0)
            {
                var item = FilterSelectionList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                item?.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    private void CloseFilterOverlay()
    {
        FilterOverlay.Visibility = Visibility.Collapsed;
        _currentFilterMode = FilterMode.None;
        
        if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
        {
            Keyboard.Focus(uiElement);
        }
    }

    private void ProcessFilterSelection(object selectedItem)
    {
        if (selectedItem is string selection)
        {
            var prefs = PreferencesManager.Load();

            if (_currentFilterMode == FilterMode.PaddingStart)
            {
                PaddingStartBtn.Content = $"{selection} ▼";
                prefs.PaddingStartMinutes = Array.IndexOf(_paddingOptions, selection);
            }
            else if (_currentFilterMode == FilterMode.PaddingEnd)
            {
                PaddingEndBtn.Content = $"{selection} ▼";
                prefs.PaddingEndMinutes = Array.IndexOf(_paddingOptions, selection);
            }
            else if (_currentFilterMode == FilterMode.CommercialSkip)
            {
                CommercialSkipBtn.Content = $"{selection} ▼";
                prefs.CommercialSkipMode = selection.StartsWith("Disabled") ? 0 : selection.StartsWith("Prompt") ? 1 : 2;
            }
            else if (_currentFilterMode == FilterMode.UpscalerPreset)
            {
                UpscalerPresetBtn.Content = $"{selection} ▼";
                prefs.UpscalerPreset = selection.StartsWith("ArtCNN") ? "ArtCNN" : "RAVU";
            }
			else if (_currentFilterMode == FilterMode.SkipForward)
{
    SkipForwardBtn.Content = $"{selection} ▼";
    prefs.SkipForwardSeconds = int.Parse(selection.Split(' ')[0]);
}
else if (_currentFilterMode == FilterMode.SkipBackward)
{
    SkipBackwardBtn.Content = $"{selection} ▼";
    prefs.SkipBackwardSeconds = int.Parse(selection.Split(' ')[0]);
}

            PreferencesManager.Save(prefs);
            CloseFilterOverlay();
        }
    }

    private void FilterSelectionList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (FilterSelectionList.SelectedItem != null) 
            ProcessFilterSelection(FilterSelectionList.SelectedItem);
    }

    private void FilterSelectionList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Select && FilterSelectionList.SelectedItem != null)
        {
            ProcessFilterSelection(FilterSelectionList.SelectedItem);
            e.Handled = true;
        }
        else if (command == HtpcCommand.Back)
        {
            CloseFilterOverlay();
            e.Handled = true;
        }
    }

    private void FilterBtn_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Down || command == HtpcCommand.Up || command == HtpcCommand.Left || command == HtpcCommand.Right)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down :
                            command == HtpcCommand.Up ? FocusNavigationDirection.Up :
                            command == HtpcCommand.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right;

            (sender as FrameworkElement)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true; 
        }
    }

    // --- 10-FOOT UI FOCUS TRAPS & ESCAPE HATCHES ---

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
        
        if (command == HtpcCommand.Up || command == HtpcCommand.Down)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as Slider)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
    }

    private void CheckBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        var chk = sender as CheckBox;

        if (command == HtpcCommand.Select && chk != null)
        {
            chk.IsChecked = !chk.IsChecked;
            e.Handled = true;
            return;
        }

        if (command == HtpcCommand.Down || command == HtpcCommand.Up)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            chk?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
    }
    
    private void MainScroll_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed)
            return;

        if (e.NewFocus is FrameworkElement element)
        {
            ScrollToElement(MainScroll, element);
        }
    }

    private void ScrollToElement(ScrollViewer scrollViewer, FrameworkElement element)
    {
        try
        {
            var content = scrollViewer.Content as UIElement;
            if (content == null) return;

            var transform = element.TransformToAncestor(content);
            Point position = transform.Transform(new Point(0, 0));
            
            double targetY = position.Y - 50; 
            
            if (targetY < 0) targetY = 0;
            if (targetY > scrollViewer.ScrollableHeight) targetY = scrollViewer.ScrollableHeight;

            scrollViewer.ScrollToVerticalOffset(targetY);
        }
        catch { }
    }

    private void DashboardListItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        var item = sender as ListBoxItem;
        if (item == null) return;

        if (command == HtpcCommand.Right)
        {
            LayoutMoveUpBtn.Focus();
            e.Handled = true;
            return;
        }

        if (command == HtpcCommand.Up || command == HtpcCommand.Down)
        {
            int index = DashboardLayoutList.ItemContainerGenerator.IndexFromContainer(item);

            if (command == HtpcCommand.Up && index == 0)
            {
                CommercialSkipBtn.Focus();
                e.Handled = true;
            }
            else if (command == HtpcCommand.Down && index == DashboardLayoutList.Items.Count - 1)
            {
                UiScaleSlider.Focus();
                e.Handled = true;
            }
            else
            {
                var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
                item.MoveFocus(new TraversalRequest(direction));
                
                var newFocus = Keyboard.FocusedElement as FrameworkElement;
                if (newFocus != null && newFocus.DataContext != null)
                {
                    DashboardLayoutList.ScrollIntoView(newFocus.DataContext);
                }
                
                e.Handled = true;
            }
        }
    }

    private void LayoutActionBtn_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (command == HtpcCommand.Left)
        {
            var targetData = DashboardLayoutList.SelectedItem ?? (DashboardLayoutList.Items.Count > 0 ? DashboardLayoutList.Items[0] : null);
            
            if (targetData != null)
            {
                var container = DashboardLayoutList.ItemContainerGenerator.ContainerFromItem(targetData) as UIElement;
                container?.Focus();
            }
            e.Handled = true;
        }
        else if (command == HtpcCommand.Right)
        {
            if (SavedServersList.Items.Count > 0)
            {
                var item = SavedServersList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                item?.Focus();
            }
            else
            {
                TxtName.Focus(); 
            }
            e.Handled = true;
        }
    }

    private void SavedServerItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is ServerConfig server)
        {
            _serverManager.SetActiveServer(server.Id);
            LoadServers();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Left)
        {
            LayoutMoveUpBtn.Focus(); 
            e.Handled = true;
        }
    }

    private void UiScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (UiScaleTextText == null) return;

        double newScale = Math.Round(e.NewValue, 1);
        UiScaleTextText.Text = $"{(int)(newScale * 100)}%";

        if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed) 
            return;

        ApplyUiScale(newScale);
    }

    private void ThemeToggleBtn_Click(object sender, RoutedEventArgs e)
{
    // Toggle the state
    string currentTheme = PreferencesManager.LoadTheme();
    string newTheme = currentTheme == "Dark" ? "Light" : "Dark";

    // Save state to JSON
    PreferencesManager.SaveTheme(newTheme);

    // Tell App.xaml.cs to load the new dictionary
    ((App)Application.Current).ApplyTheme(newTheme);

    // Update the icon
    ThemeToggleBtn.Content = newTheme == "Dark" ? "\xE708" : "\xE706";
}

    private void UiScaleSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
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
    
    private void UpscalerCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        
        var prefs = PreferencesManager.Load();
        prefs.EnableUpscaling = EnableUpscalingCheck.IsChecked == true;
        UpscalerPresetBtn.IsEnabled = prefs.EnableUpscaling;
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
        
        HomeNavBtn.Focus(); 
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
                LoadServers(); 
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
    
    // --- DASHBOARD LAYOUT LOGIC ---

    private void MoveRowUp_Click(object sender, RoutedEventArgs e)
    {
        int index = DashboardLayoutList.SelectedIndex;
        if (index > 0)
        {
            var item = DashboardRows[index];
            DashboardRows.RemoveAt(index);
            DashboardRows.Insert(index - 1, item);
            DashboardLayoutList.SelectedIndex = index - 1; 
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
            DashboardLayoutList.SelectedIndex = index + 1; 
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
            DashboardRows[i].Order = i; 
            prefs.DashboardLayout.Add(DashboardRows[i]);
        }
        
        PreferencesManager.Save(prefs);
    }
    
    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
	
	private readonly int[] _replayOptions = new[] { 10, 15, 20, 30, 45, 60 };

private void LoadReplayPreferences()
{
    var prefs = PreferencesManager.Load();
    ReplayDurationBtn.Content = $"{prefs.InstantReplaySeconds} Seconds";
    ReplaySlowMoBtn.Content = prefs.InstantReplaySlowMotion ? "Enabled (0.5x)" : "Disabled (1.0x)";
}

private void ReplayDurationBtn_Click(object sender, RoutedEventArgs e)
{
    var prefs = PreferencesManager.Load();
    int currentIndex = Array.IndexOf(_replayOptions, prefs.InstantReplaySeconds);
    int nextIndex = (currentIndex + 1) % _replayOptions.Length;

    prefs.InstantReplaySeconds = _replayOptions[nextIndex];
    PreferencesManager.Save(prefs);

    ReplayDurationBtn.Content = $"{prefs.InstantReplaySeconds} Seconds";
}

private void ReplaySlowMoBtn_Click(object sender, RoutedEventArgs e)
{
    var prefs = PreferencesManager.Load();
    prefs.InstantReplaySlowMotion = !prefs.InstantReplaySlowMotion;
    PreferencesManager.Save(prefs);

    ReplaySlowMoBtn.Content = prefs.InstantReplaySlowMotion ? "Enabled (0.5x)" : "Disabled (1.0x)";
}
    
    // --- NAVIGATION SIGNATURES ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Recordings_Click(object sender, RoutedEventArgs e) => OnRecordingsRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
	private void Sports_Click(object sender, RoutedEventArgs e) => OnSportsRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Collections_Click(object sender, RoutedEventArgs e) => OnCollectionsRequested?.Invoke(this, EventArgs.Empty);
}
