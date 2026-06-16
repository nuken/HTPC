using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Input; 
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class MoviesView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;

    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    private readonly DispatcherTimer _typingTimer;
    
    public ObservableCollection<MediaItem> MovieLibrary { get; set; } = new ObservableCollection<MediaItem>();

    private int _currentOffset = 0;
    private const int _chunkSize = 50;
    private bool _isLoading = false;
    private bool _hasReachedEnd = false;
    private bool _isInitialized = false;

    // Filter States
    private string _currentSearch = "";
    private string _currentGenre = "All";
    private string _currentSort = "Recently Added";
	private string _currentStatus = "All Movies";

    public MoviesView(MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _serverManager = serverManager;
        this.DataContext = this;

        // Setup the Debounce Timer for smooth typing
        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _typingTimer.Tick += TypingTimer_Tick;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) 
        {
            SearchBox.Focus();
            return;
        }

        // Load the saved sort preference
        _currentSort = PreferencesManager.LoadMovieSort();
        foreach (ComboBoxItem item in SortDropdown.Items)
        {
            if (item.Content.ToString() == _currentSort)
            {
                SortDropdown.SelectedItem = item;
                break;
            }
        }

        _isInitialized = true;
        await ResetAndLoadAsync();

        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            SearchBox.Focus();
        }), DispatcherPriority.Input);
    }

    private async Task ResetAndLoadAsync()
    {
        if (!_isInitialized) return;

        _currentOffset = 0;
        _hasReachedEnd = false;
        MovieLibrary.Clear();
        MainScroll.ScrollToTop();
        
        await LoadNextChunkAsync();
    }

    private async Task LoadNextChunkAsync()
    {
        if (_isLoading || _hasReachedEnd) return;
        
        _isLoading = true;
        LoadingText.Visibility = Visibility.Visible;

        var newMovies = await _libraryService.GetFilteredMoviesAsync(_currentOffset, _chunkSize, _currentSearch, _currentGenre, _currentSort, _currentStatus);
        
        if (newMovies.Count == 0)
        {
            _hasReachedEnd = true;
        }
        else
        {
            foreach (var movie in newMovies) MovieLibrary.Add(movie);
            _currentOffset += _chunkSize;
        }

        LoadingText.Visibility = Visibility.Collapsed;
        _isLoading = false;
    }

    private async void MainScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (MainScroll.VerticalOffset >= MainScroll.ScrollableHeight - 100)
            await LoadNextChunkAsync();
    }

    // --- FILTERS & SEARCH ---

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _typingTimer.Stop();
        _typingTimer.Start();
    }

    private async void TypingTimer_Tick(object? sender, EventArgs e)
    {
        _typingTimer.Stop();
        if (_currentSearch != SearchBox.Text)
        {
            _currentSearch = SearchBox.Text;
            await ResetAndLoadAsync();
        }
    }

    private async void SortDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (SortDropdown.SelectedItem is ComboBoxItem item)
        {
            _currentSort = item.Content.ToString() ?? "Recently Added";
            PreferencesManager.SaveMovieSort(_currentSort); 
            await ResetAndLoadAsync();
        }
    }
	
	private async void StatusDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (StatusDropdown.SelectedItem is ComboBoxItem item)
        {
            _currentStatus = item.Content.ToString() ?? "All Movies";
            await ResetAndLoadAsync();
        }
    }

    private async void Genre_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (sender is RadioButton rb)
        {
            _currentGenre = rb.Content.ToString() ?? "All";
            await ResetAndLoadAsync();
        }
    }

    // --- UX/UI INTERACTION ---

    private void MoviesGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };
        MainScroll.RaiseEvent(eventArg);
    }

    private void ListBoxItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem movie)
        {
            OnPlayRequested?.Invoke(this, movie);
            e.Handled = true; 
        }
    }

    private void MovieCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem movie)
            OnPlayRequested?.Invoke(this, movie);
    }
    
    // --- 10-FOOT UI FOCUS TRAP FIXES ---

    private void Dropdown_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var cb = sender as ComboBox;
        var command = InputMapper.GetCommand(e.Key);

        // If the dropdown is CLOSED, allow the D-Pad to escape instead of scrolling the hidden list!
        if (cb != null && !cb.IsDropDownOpen)
        {
            if (command == HtpcCommand.Down || command == HtpcCommand.Up || command == HtpcCommand.Left || command == HtpcCommand.Right)
            {
                var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down :
                                command == HtpcCommand.Up ? FocusNavigationDirection.Up :
                                command == HtpcCommand.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right;

                cb.MoveFocus(new TraversalRequest(direction));
                e.Handled = true; // Stop the ComboBox from stealing the input
            }
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        // TextBoxes naturally capture Left/Right for typing, but we want Up/Down to escape!
        if (command == HtpcCommand.Down || command == HtpcCommand.Up)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as TextBox)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
    }
    
    private void GenrePill_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        // If the user pushes Up or Down on a genre pill, force the focus to jump!
        if (command == HtpcCommand.Down || command == HtpcCommand.Up)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as RadioButton)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
    }
    
    // --- ADMIN COMMANDS & CONTEXT MENU ---

    private async void AdminCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is MediaItem movie)
        {
            string command = menuItem.Tag?.ToString() ?? "";
            if (string.IsNullOrEmpty(command)) return;

            var activeServer = _serverManager.GetActiveServer();
            if (activeServer == null) return;

            string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
            
            // Show working toast
            ShowToast($"Sending command: {menuItem.Header}...");

            bool success = await _libraryService.SendFileAdminCommandAsync(baseUrl, movie.Id, command);

            if (success) 
            {
                ShowToast($"Success: {menuItem.Header} triggered.");
                
                // --- INSTANTLY UPDATE MEMORY SO THE MENU FLIPS ---
                if (command == "watch") movie.IsWatched = true;
                else if (command == "unwatch") movie.IsWatched = false;
                else if (command == "favorite") movie.IsFavorite = true;
                else if (command == "unfavorite") movie.IsFavorite = false;
            }
            else 
            {
                ShowToast($"Error: Failed to trigger {menuItem.Header}.");
            }
        }
    }
    
    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
{
    if (sender is ContextMenu menu && menu.PlacementTarget is FrameworkElement target && target.DataContext is MediaItem movie)
    {
        foreach (var item in menu.Items)
        {
            if (item is MenuItem menuItem)
            {
                // Toggle "Watch" / "Unwatch"
                if (menuItem.Tag?.ToString() == "watch")
                    menuItem.Visibility = movie.IsWatched ? Visibility.Collapsed : Visibility.Visible;
                
                if (menuItem.Tag?.ToString() == "unwatch")
                    menuItem.Visibility = movie.IsWatched ? Visibility.Visible : Visibility.Collapsed;

                // Toggle "Favorite" / "Unfavorite"
                if (menuItem.Tag?.ToString() == "favorite")
                    menuItem.Visibility = movie.IsFavorite ? Visibility.Collapsed : Visibility.Visible;
                
                if (menuItem.Tag?.ToString() == "unfavorite")
                    menuItem.Visibility = movie.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastNotification.Visibility = Visibility.Visible;

        // Auto-hide the toast after 3 seconds
        _ = Task.Run(async () => 
        {
            await Task.Delay(3000);
            Application.Current.Dispatcher.Invoke(() => ToastNotification.Visibility = Visibility.Collapsed);
        });
    }

    // --- MEDIA INFO MODAL ---

    // --- MEDIA INFO MODAL ---

    private async void MediaInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is MediaItem movie)
        {
            var activeServer = _serverManager.GetActiveServer();
            if (activeServer == null) return;

            string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
            MediaInfoTitle.Text = $"Loading info for: {movie.Title}...";
            MediaInfoDetails.Children.Clear();
            MediaInfoModal.Visibility = Visibility.Visible;

            string json = await _libraryService.GetMediaInfoAsync(baseUrl, movie.Id);
            
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    MediaInfoTitle.Text = movie.Title;

                    if (root.TryGetProperty("format", out var format))
                    {
                        // 1. File Path
                        if (format.TryGetProperty("filename", out var fileProp))
                            AddMediaInfoRow("Path", fileProp.GetString() ?? "Unknown");

                        // 2. Duration
                        if (format.TryGetProperty("duration", out var durProp) && double.TryParse(durProp.GetString(), out double seconds))
                        {
                            var time = TimeSpan.FromSeconds(seconds);
                            string durationText = time.Hours > 0 ? $"{time.Hours} hrs {time.Minutes} min" : $"{time.Minutes} min";
                            AddMediaInfoRow("Duration", durationText);
                        }

                        // 3. Bit Rate
                        if (format.TryGetProperty("bit_rate", out var brProp) && long.TryParse(brProp.GetString(), out long bitRate))
                            AddMediaInfoRow("Bit Rate", $"{bitRate:N0} bits/sec");

                        // 4. File Size
                        if (format.TryGetProperty("size", out var sizeProp) && long.TryParse(sizeProp.GetString(), out long bytes))
                            AddMediaInfoRow("File Size", $"{bytes:N0} bytes");
                    }

                    // 5. File ID
                    AddMediaInfoRow("File ID", movie.Id);

                    // 6. Streaming Index
                    if (root.TryGetProperty("m3u8_up_to_date", out var m3u8Prop))
                        AddMediaInfoRow("Streaming Index", m3u8Prop.GetBoolean() ? "Up to date" : "Needs update");

                    // 7. Track Information
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
                                
                                // Calculate exact FPS from fraction (e.g. "30000/1001" or "30/1")
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
                            else if (type == "subtitle")
                            {
                                details = "Subtitle Track";
                            }

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
            else
            {
                AddMediaInfoRow("Error", "Failed to retrieve media info from server.");
            }
        }
    }

    // Standard row generator for key/value pairs
    private void AddMediaInfoRow(string label, string value)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        
        panel.Children.Add(new TextBlock 
        { 
            Text = label, 
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)), 
            FontSize = 15, 
            FontWeight = FontWeights.SemiBold, 
            Width = 140 
        });
        
        panel.Children.Add(new TextBlock 
        { 
            Text = value, 
            Foreground = System.Windows.Media.Brushes.White, 
            FontSize = 15, 
            TextWrapping = TextWrapping.Wrap, 
            MaxWidth = 500 
        });
        
        MediaInfoDetails.Children.Add(panel);
    }

    // Custom row generator for Track data
    private void AddTrackInfo(int trackIndex, string codec, string details)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 15, 0, 0) };
        
        panel.Children.Add(new TextBlock 
        { 
            Text = $"Track #{trackIndex}: {codec}", 
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 164, 239)), // Blue accent
            FontSize = 15, 
            FontWeight = FontWeights.Bold 
        });
        
        if (!string.IsNullOrWhiteSpace(details))
        {
            panel.Children.Add(new TextBlock 
            { 
                Text = details, 
                Foreground = System.Windows.Media.Brushes.White, 
                FontSize = 14, 
                Margin = new Thickness(0, 2, 0, 0) 
            });
        }
        
        MediaInfoDetails.Children.Add(panel);
    }

    private void CloseMediaInfo_Click(object sender, RoutedEventArgs e)
    {
        MediaInfoModal.Visibility = Visibility.Collapsed;
    }

    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
}