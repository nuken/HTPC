using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Input; 
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class VideosView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnRecordingsRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;
    public event EventHandler? OnMultiviewRequested;
    public event EventHandler? OnCollectionsRequested;

    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    private bool _isInitialized = false;

    // Data Bindings
    public ObservableCollection<MediaItem> VideoGroups { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> CurrentVideos { get; set; } = new ObservableCollection<MediaItem>();

    // Sorting Caches
    private List<MediaItem> _allGroups = new List<MediaItem>();
    private List<MediaItem> _allVideos = new List<MediaItem>();

    // Overlay State Variables
    private enum FilterMode { None, GroupSort, GroupOrder, VideoSort, VideoOrder }
    private FilterMode _currentFilterMode = FilterMode.None;
    private IInputElement? _lastFocusedElement;

    private string _currentGroupSort = "Alphabetical";
    private string _currentGroupOrder = "Forward";
    
    private string _currentVideoSort = "Date Added";
    private string _currentVideoOrder = "Forward";

    public VideosView(MediaLibraryService libraryService, ServerManagerService serverManager) 
    {
        InitializeComponent();
        _libraryService = libraryService;
        _serverManager = serverManager;
        this.DataContext = this;
        Loaded += OnLoaded;
        this.PreviewKeyDown += VideosView_PreviewKeyDown; 
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeToggleBtn.Content = PreferencesManager.LoadTheme() == "Dark" ? "\xE708" : "\xE706";

        if (_isInitialized) 
        {
            GroupsGrid.Focus();
            return;
        }

        _isInitialized = true;
        
        _allGroups = await _libraryService.GetVideoGroupsAsync();
        ApplyGroupSorting();

        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            if (GroupsGrid.Items.Count > 0)
            {
                var firstItem = GroupsGrid.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                firstItem?.Focus();
            }
        }), DispatcherPriority.Input);
    }

    private void VideosView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (FilterOverlay.Visibility == Visibility.Visible && (command == HtpcCommand.Back || e.Key == Key.Escape))
        {
            CloseFilterOverlay();
            e.Handled = true;
            return;
        }

        if (VideosOverlay.Visibility == Visibility.Visible && (command == HtpcCommand.Back || e.Key == Key.Escape))
        {
            CloseOverlay_Click(null!, null!);
            GroupsGrid.Focus();
            e.Handled = true;
        }
    }

    // --- SORTING LOGIC ---

    private void ApplyGroupSorting()
    {
        if (_allGroups == null || _allGroups.Count == 0) return;

        bool isReverse = _currentGroupOrder == "Reverse";
        
        var sorted = _currentGroupSort switch
        {
            "Date Added" => isReverse ? _allGroups.OrderBy(g => g.CreatedAt) : _allGroups.OrderByDescending(g => g.CreatedAt),
            _ => isReverse ? _allGroups.OrderByDescending(g => g.Title) : _allGroups.OrderBy(g => g.Title)
        };

        VideoGroups.Clear();
        foreach (var g in sorted) VideoGroups.Add(g);
    }

    private void ApplyVideoSorting()
    {
        if (_allVideos == null || _allVideos.Count == 0) return;

        bool isReverse = _currentVideoOrder == "Reverse";
        
        var sorted = _currentVideoSort switch
        {
            "Alphabetical" => isReverse ? _allVideos.OrderByDescending(v => v.CurrentShowTitle) : _allVideos.OrderBy(v => v.CurrentShowTitle),
            "Date Updated" => isReverse ? _allVideos.OrderBy(v => v.UpdatedAt) : _allVideos.OrderByDescending(v => v.UpdatedAt),
            "Date Watched" => isReverse ? _allVideos.OrderBy(v => v.LastWatchedAt) : _allVideos.OrderByDescending(v => v.LastWatchedAt),
            "Date Favorited" => isReverse ? _allVideos.OrderBy(v => v.IsFavorite).ThenByDescending(v => v.CreatedAt) : _allVideos.OrderByDescending(v => v.IsFavorite).ThenByDescending(v => v.CreatedAt),
            "Duration" => isReverse ? _allVideos.OrderBy(v => v.Duration) : _allVideos.OrderByDescending(v => v.Duration),
            _ => isReverse ? _allVideos.OrderBy(v => v.CreatedAt) : _allVideos.OrderByDescending(v => v.CreatedAt) // Date Added Default
        };

        CurrentVideos.Clear();
        foreach (var v in sorted) CurrentVideos.Add(v);
    }

    // --- OVERLAY FILTERS ---

    private void GroupSortBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.GroupSort;
        FilterOverlayTitle.Text = "Sort Folders By";
        FilterSelectionList.ItemsSource = new[] { "Alphabetical", "Date Added" };
        FilterSelectionList.SelectedItem = _currentGroupSort;
        OpenFilterOverlay();
    }

    private void GroupOrderBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.GroupOrder;
        FilterOverlayTitle.Text = "Order";
        FilterSelectionList.ItemsSource = new[] { "Forward", "Reverse" };
        FilterSelectionList.SelectedItem = _currentGroupOrder;
        OpenFilterOverlay();
    }

    private void VideoSortBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.VideoSort;
        FilterOverlayTitle.Text = "Sort Videos By";
        FilterSelectionList.ItemsSource = new[] { "Alphabetical", "Date Added", "Date Updated", "Date Watched", "Date Favorited", "Duration" };
        FilterSelectionList.SelectedItem = _currentVideoSort;
        OpenFilterOverlay();
    }

    private void VideoOrderBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.VideoOrder;
        FilterOverlayTitle.Text = "Order";
        FilterSelectionList.ItemsSource = new[] { "Forward", "Reverse" };
        FilterSelectionList.SelectedItem = _currentVideoOrder;
        OpenFilterOverlay();
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
            if (_currentFilterMode == FilterMode.GroupSort)
            {
                _currentGroupSort = selection;
                GroupSortBtn.Content = $"{selection} ▼";
                ApplyGroupSorting();
            }
            else if (_currentFilterMode == FilterMode.GroupOrder)
            {
                _currentGroupOrder = selection;
                GroupOrderBtn.Content = $"{selection} ▼";
                ApplyGroupSorting();
            }
            else if (_currentFilterMode == FilterMode.VideoSort)
            {
                _currentVideoSort = selection;
                VideoSortBtn.Content = $"{selection} ▼";
                ApplyVideoSorting();
            }
            else if (_currentFilterMode == FilterMode.VideoOrder)
            {
                _currentVideoOrder = selection;
                VideoOrderBtn.Content = $"{selection} ▼";
                ApplyVideoSorting();
            }

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
    
    if (command == HtpcCommand.Down) 
    {
        if (sender == GroupSortBtn || sender == GroupOrderBtn)
        {
            if (GroupsGrid.Items.Count > 0)
            {
                var element = GroupsGrid.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                element?.Focus();
                e.Handled = true;
            }
        }
        else if (sender == VideoSortBtn || sender == VideoOrderBtn)
        {
            if (VideosList.Items.Count > 0)
            {
                var element = VideosList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                element?.Focus();
                e.Handled = true;
            }
        }
    }
    else if (command == HtpcCommand.Up)
    {
        if (sender == GroupSortBtn || sender == GroupOrderBtn)
        {
            FocusTopNav();
            e.Handled = true;
        }
        else if (sender == VideoSortBtn || sender == VideoOrderBtn)
        {
            e.Handled = true; // Trap at top of overlay
        }
    }
    else if (command == HtpcCommand.Left)
    {
        if (sender == GroupOrderBtn) { GroupSortBtn.Focus(); e.Handled = true; }
        else if (sender == VideoOrderBtn) { VideoSortBtn.Focus(); e.Handled = true; }
        else if (sender == VideoSortBtn) { BackToFoldersBtn.Focus(); e.Handled = true; }
        else { e.Handled = true; } // Trap at left edge
    }
    else if (command == HtpcCommand.Right)
    {
        if (sender == GroupSortBtn) { GroupOrderBtn.Focus(); e.Handled = true; }
        else if (sender == VideoSortBtn) { VideoOrderBtn.Focus(); e.Handled = true; }
        else { e.Handled = true; } // Trap at right edge
    }
}

	// --- OVERLAY DRILL-DOWN LOGIC ---

    private void TopNavPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Down || e.Key == Key.Down)
        {
            GroupSortBtn.Focus();
            e.Handled = true;
        }
    }

    private void GroupItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        bool isSelect = command == HtpcCommand.Select || e.Key == Key.Enter;
        bool isUp = command == HtpcCommand.Up || e.Key == Key.Up;

        if (isSelect && sender is ListBoxItem item && item.DataContext is MediaItem group)
        {
            OpenGroupOverlay(group);
            e.Handled = true;
            return;
        }

        if (isUp && sender is ListBoxItem currentItem)
        {
            Point position = currentItem.TranslatePoint(new Point(0, 0), GroupsGrid);
            if (position.Y < 50)
            {
                GroupSortBtn.Focus();
                e.Handled = true;
            }
        }
    }

    private void FocusTopNav()
    {
        foreach (UIElement child in TopNavPanel.Children)
        {
            if (child is Button btn && btn.Focusable)
            {
                btn.Focus();
                return;
            }
        }
    }

    private void BackToFoldersBtn_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Down || e.Key == Key.Down)
        {
            if (VideosList.Items.Count > 0)
            {
                var element = VideosList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                element?.Focus();
                e.Handled = true;
            }
        }
        else if (command == HtpcCommand.Right)
        {
            VideoSortBtn.Focus();
            e.Handled = true;
        }
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

    private void GroupCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem group)
        {
            OpenGroupOverlay(group);
        }
    }

    private async void OpenGroupOverlay(MediaItem group)
    {
        try
        {
            SelectedGroupName.Text = group.Title;
            
            VideosOverlay.Visibility = Visibility.Visible;
            _allVideos.Clear();
            CurrentVideos.Clear();
            OverlayScroll.ScrollToTop(); 

            // Fetch the videos and then sort them into the cache
            _allVideos = await _libraryService.GetVideosInGroupAsync(group.Id);
            ApplyVideoSorting();

            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                if (VideosList.Items.Count > 0)
                {
                    var firstVideo = VideosList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                    firstVideo?.Focus();
                }
                else
                {
                    BackToFoldersBtn.Focus();
                }
            }), DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading videos: {ex.Message}");
        }
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        VideosOverlay.Visibility = Visibility.Collapsed;
    }

    private void VideoItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        bool isSelect = command == HtpcCommand.Select || e.Key == Key.Enter;
        bool isUp = command == HtpcCommand.Up || e.Key == Key.Up;

        if (isSelect && sender is ListBoxItem item && item.DataContext is MediaItem video)
        {
            OnPlayRequested?.Invoke(this, video);
            e.Handled = true;
            return;
        }

        if (isUp && sender is ListBoxItem currentItem)
        {
            Point position = currentItem.TranslatePoint(new Point(0, 0), VideosList);
            
            if (position.Y < 50)
            {
                VideoSortBtn.Focus();
                e.Handled = true;
            }
        }
    }

    private void VideoCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem video)
        {
            OnPlayRequested?.Invoke(this, video);
        }
    }
	
	// --- ADMIN COMMANDS & CONTEXT MENU ---

    private async void AdminCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is MediaItem video)
        {
            string command = menuItem.Tag?.ToString() ?? "";
            if (string.IsNullOrEmpty(command)) return;

            var activeServer = _serverManager.GetActiveServer();
            if (activeServer == null) return;

            string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
            
            ShowToast($"Sending command: {menuItem.Header}...");

            bool success = await _libraryService.SendFileAdminCommandAsync(baseUrl, video.Id, command);

            if (success) 
            {
                ShowToast($"Success: {menuItem.Header} triggered.");
                if (command == "watch") video.IsWatched = true;
                else if (command == "unwatch") video.IsWatched = false;
                else if (command == "favorite") video.IsFavorite = true;
                else if (command == "unfavorite") video.IsFavorite = false;
            }
            else ShowToast($"Error: Failed to trigger {menuItem.Header}.");
        }
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && menu.PlacementTarget is FrameworkElement target && target.DataContext is MediaItem video)
        {
            foreach (var item in menu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    if (menuItem.Tag?.ToString() == "watch")
                        menuItem.Visibility = video.IsWatched ? Visibility.Collapsed : Visibility.Visible;
                    if (menuItem.Tag?.ToString() == "unwatch")
                        menuItem.Visibility = video.IsWatched ? Visibility.Visible : Visibility.Collapsed;
                    if (menuItem.Tag?.ToString() == "favorite")
                        menuItem.Visibility = video.IsFavorite ? Visibility.Collapsed : Visibility.Visible;
                    if (menuItem.Tag?.ToString() == "unfavorite")
                        menuItem.Visibility = video.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastNotification.Visibility = Visibility.Visible;

        _ = Task.Run(async () => 
        {
            await Task.Delay(3000);
            Application.Current.Dispatcher.Invoke(() => ToastNotification.Visibility = Visibility.Collapsed);
        });
    }

    // --- MEDIA INFO MODAL ---

    private async void MediaInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is MediaItem video)
        {
            var activeServer = _serverManager.GetActiveServer();
            if (activeServer == null) return;

            string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
            MediaInfoTitle.Text = $"Loading info for: {video.Title}...";
            MediaInfoDetails.Children.Clear();
            MediaInfoModal.Visibility = Visibility.Visible;

            string json = await _libraryService.GetMediaInfoAsync(baseUrl, video.Id);
            
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    MediaInfoTitle.Text = video.Title;

                    if (root.TryGetProperty("format", out var format))
                    {
                        if (format.TryGetProperty("filename", out var fileProp))
                            AddMediaInfoRow("Path", fileProp.GetString() ?? "Unknown");

                        if (format.TryGetProperty("duration", out var durProp) && double.TryParse(durProp.GetString(), out double seconds))
                        {
                            var time = TimeSpan.FromSeconds(seconds);
                            string durationText = time.Hours > 0 ? $"{time.Hours} hrs {time.Minutes} min" : $"{time.Minutes} min";
                            AddMediaInfoRow("Duration", durationText);
                        }

                        if (format.TryGetProperty("bit_rate", out var brProp) && long.TryParse(brProp.GetString(), out long bitRate))
                            AddMediaInfoRow("Bit Rate", $"{bitRate:N0} bits/sec");

                        if (format.TryGetProperty("size", out var sizeProp) && long.TryParse(sizeProp.GetString(), out long bytes))
                            AddMediaInfoRow("File Size", $"{bytes:N0} bytes");
                    }

                    AddMediaInfoRow("File ID", video.Id);

                    if (root.TryGetProperty("m3u8_up_to_date", out var m3u8Prop))
                        AddMediaInfoRow("Streaming Index", m3u8Prop.GetBoolean() ? "Up to date" : "Needs update");

                    if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        int trackIndex = 0;
                        foreach (var stream in streams.EnumerateArray())
                        {
                            string type = stream.TryGetProperty("codec_type", out var typeProp) ? typeProp.GetString() ?? "" : "";
                            string codecLong = stream.TryGetProperty("codec_long_name", out var clnProp) ? clnProp.GetString() ?? "" : "Unknown Codec";
                            string details = "";

                            if (type == "video")
                            {
                                string width = stream.TryGetProperty("width", out var wProp) ? wProp.ToString() : "0";
                                string height = stream.TryGetProperty("height", out var hProp) ? hProp.ToString() : "0";
                                string aspect = stream.TryGetProperty("display_aspect_ratio", out var arProp) ? arProp.GetString() ?? "" : "";
                                string pixFmt = stream.TryGetProperty("pix_fmt", out var pfProp) ? pfProp.GetString() ?? "" : "";
                                string fieldOrder = stream.TryGetProperty("field_order", out var foProp) ? foProp.GetString() ?? "" : "";
                                
                                string fpsText = "";
                                if (stream.TryGetProperty("avg_frame_rate", out var frProp))
                                {
                                    var parts = frProp.GetString()?.Split('/') ?? Array.Empty<string>();
                                    if (parts.Length == 2 && double.TryParse(parts[0], out double num) && double.TryParse(parts[1], out double den) && den != 0)
                                        fpsText = $"{Math.Round(num / den, 2):F2}fps";
                                }

                                details = $"{width}x{height}   {aspect}   {pixFmt}   {fieldOrder}   {fpsText}";
                            }
                            else if (type == "audio")
                            {
                                string layout = stream.TryGetProperty("channel_layout", out var clProp) ? clProp.GetString() ?? "" : "";
                                string audioBitRate = stream.TryGetProperty("bit_rate", out var abrProp) && double.TryParse(abrProp.GetString(), out double abr) 
                                    ? $"{abr / 1000.0:F3}kbps" : "";
                                
                                details = $"{layout}   {audioBitRate}";
                            }
                            else if (type == "subtitle") details = "Subtitle Track";

                            AddTrackInfo(trackIndex, codecLong, details);
                            trackIndex++;
                        }
                    }
                }
                catch
                {
                    AddMediaInfoRow("Error", "Could not parse media info data.");
                }
            }
            else AddMediaInfoRow("Error", "Failed to retrieve media info from server.");
        }
    }

    private void AddMediaInfoRow(string label, string value)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)), FontSize = 15, FontWeight = FontWeights.SemiBold, Width = 140 });
        panel.Children.Add(new TextBlock { Text = value, Foreground = System.Windows.Media.Brushes.White, FontSize = 15, TextWrapping = TextWrapping.Wrap, MaxWidth = 500 });
        MediaInfoDetails.Children.Add(panel);
    }

    private void AddTrackInfo(int trackIndex, string codec, string details)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 15, 0, 0) };
        panel.Children.Add(new TextBlock { Text = $"Track #{trackIndex}: {codec}", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 164, 239)), FontSize = 15, FontWeight = FontWeights.Bold });
        if (!string.IsNullOrWhiteSpace(details)) panel.Children.Add(new TextBlock { Text = details, Foreground = System.Windows.Media.Brushes.White, FontSize = 14, Margin = new Thickness(0, 2, 0, 0) });
        MediaInfoDetails.Children.Add(panel);
    }

    private void CloseMediaInfo_Click(object sender, RoutedEventArgs e)
    {
        MediaInfoModal.Visibility = Visibility.Collapsed;
    }

    // --- MOUSE SCROLLING FIX ---
    
    private void GroupsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };
        MainScroll.RaiseEvent(eventArg);
    }

    private void VideosList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };
        OverlayScroll.RaiseEvent(eventArg);
    }

    // --- NAVBAR ROUTING ---

    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
	private void Recordings_Click(object sender, RoutedEventArgs e) => OnRecordingsRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
	private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
	private void Collections_Click(object sender, RoutedEventArgs e) => OnCollectionsRequested?.Invoke(this, EventArgs.Empty);
}
