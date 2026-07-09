using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Input; // Required for the new InputMapper
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class ShowsView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler? OnMoviesRequested;
	public event EventHandler? OnRecordingsRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;
	public event EventHandler<(System.Collections.Generic.List<MediaItem> Queue, int StartIndex)>? OnPlayQueueRequested;
    public event EventHandler? OnVideosRequested;
	public event EventHandler? OnMultiviewRequested;
	public event EventHandler? OnCollectionsRequested;

    private readonly MediaLibraryService _libraryService;
	private readonly ServerManagerService _serverManager;
    private readonly DispatcherTimer _typingTimer;
    
    // Data Bindings
    public ObservableCollection<MediaItem> ShowLibrary { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<int> Seasons { get; set; } = new ObservableCollection<int>();
    public ObservableCollection<MediaItem> CurrentEpisodes { get; set; } = new ObservableCollection<MediaItem>();

    // Master list of episodes for the currently selected show
    private List<MediaItem> _allEpisodesForSelectedShow = new List<MediaItem>();

    // Pagination State
    private int _currentOffset = 0;
    private const int _chunkSize = 50;
    private bool _isLoading = false;
    private bool _hasReachedEnd = false;
    private bool _isInitialized = false;

    private string _currentSearch = "";
    private string _currentSort = "Recently Recorded";

    // THE FIX: Added 'ServerManagerService serverManager' to the parameter list
    public ShowsView(MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _serverManager = serverManager;
        this.DataContext = this;

        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _typingTimer.Tick += TypingTimer_Tick;

        Loaded += OnLoaded;
        this.PreviewKeyDown += ShowsView_PreviewKeyDown; // Master listener for the remote's Back button
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) 
        {
            SearchBox.Focus();
            return;
        }

        _isInitialized = true;
        await ResetAndLoadAsync();

        // THE FIX: Push the cursor to the Search Box so the remote D-Pad works instantly
        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            SearchBox.Focus();
        }), DispatcherPriority.Input);
    }
	
	// --- ADMIN COMMANDS & CONTEXT MENU ---

    private async void AdminCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is MediaItem episode)
        {
            string command = menuItem.Tag?.ToString() ?? "";
            if (string.IsNullOrEmpty(command)) return;

            var activeServer = _serverManager.GetActiveServer();
            if (activeServer == null) return;

            string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
            
            ShowToast($"Sending command: {menuItem.Header}...");

            bool success = await _libraryService.SendFileAdminCommandAsync(baseUrl, episode.Id, command);

            if (success) 
            {
                ShowToast($"Success: {menuItem.Header} triggered.");
                
                if (command == "watch") episode.IsWatched = true;
                else if (command == "unwatch") episode.IsWatched = false;
                else if (command == "favorite") episode.IsFavorite = true;
                else if (command == "unfavorite") episode.IsFavorite = false;
            }
            else ShowToast($"Error: Failed to trigger {menuItem.Header}.");
        }
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && menu.PlacementTarget is FrameworkElement target && target.DataContext is MediaItem episode)
        {
            foreach (var item in menu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    if (menuItem.Tag?.ToString() == "watch")
                        menuItem.Visibility = episode.IsWatched ? Visibility.Collapsed : Visibility.Visible;
                    if (menuItem.Tag?.ToString() == "unwatch")
                        menuItem.Visibility = episode.IsWatched ? Visibility.Visible : Visibility.Collapsed;
                    if (menuItem.Tag?.ToString() == "favorite")
                        menuItem.Visibility = episode.IsFavorite ? Visibility.Collapsed : Visibility.Visible;
                    if (menuItem.Tag?.ToString() == "unfavorite")
                        menuItem.Visibility = episode.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
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
        if (sender is MenuItem menuItem && menuItem.DataContext is MediaItem episode)
        {
            var activeServer = _serverManager.GetActiveServer();
            if (activeServer == null) return;

            string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
            MediaInfoTitle.Text = $"Loading info for: {episode.CurrentShowTitle}...";
            MediaInfoDetails.Children.Clear();
            MediaInfoModal.Visibility = Visibility.Visible;

            string json = await _libraryService.GetMediaInfoAsync(baseUrl, episode.Id);
            
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    // Shows use the Episode title for the popup
                    MediaInfoTitle.Text = string.IsNullOrEmpty(episode.CurrentShowTitle) ? episode.Title : episode.CurrentShowTitle;

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

                    AddMediaInfoRow("File ID", episode.Id);

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

    // --- MASTER REMOTE BACK HANDLER ---
    private void ShowsView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        // NEW: If the Binge Prompt is open, don't let this master handler interfere
        if (BingeChoiceOverlay.Visibility == Visibility.Visible) return;

        // If the Modal is open and the user presses Back on the remote, close the modal
        if (EpisodesOverlay.Visibility == Visibility.Visible && command == HtpcCommand.Back)
        {
            CloseOverlay_Click(null!, null!);
            
            // Return focus to the main grid so they can keep scrolling shows
            ShowsGrid.Focus();
            e.Handled = true;
        }
    }

    private async Task ResetAndLoadAsync()
    {
        _currentOffset = 0;
        _hasReachedEnd = false;
        ShowLibrary.Clear();
        MainScroll.ScrollToTop();
        await LoadNextChunkAsync();
    }

    private async Task LoadNextChunkAsync()
    {
        if (_isLoading || _hasReachedEnd) return;
        _isLoading = true;

        var newShows = await _libraryService.GetFilteredShowsAsync(_currentOffset, _chunkSize, _currentSearch, _currentSort);
        
        if (newShows.Count == 0) _hasReachedEnd = true;
        else
        {
            foreach (var show in newShows) ShowLibrary.Add(show);
            _currentOffset += _chunkSize;
        }
        _isLoading = false;
    }

    private async void MainScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Infinite scrolling trigger
        if (MainScroll.VerticalOffset >= MainScroll.ScrollableHeight - 100)
            await LoadNextChunkAsync();
    }

    // --- SEARCH & SORT ---
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
            _currentSort = item.Content.ToString() ?? "Recently Recorded";
            await ResetAndLoadAsync();
        }
    }

    // --- NAVIGATION & INTERACTION ---
    private void ShowsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender };
        MainScroll.RaiseEvent(eventArg);
    }

    // THE FIX: Listen for Enter/OK on the Show Posters
    private void ListBoxItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem show)
        {
            OpenShowOverlay(show);
            e.Handled = true;
        }
    }

    private void ShowCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem show)
        {
            OpenShowOverlay(show);
        }
    }

    // Unified logic for opening a show (used by both Mouse and Keyboard/Remote)
    private async void OpenShowOverlay(MediaItem show)
    {
        try
        {
            // Populate Column 0 details
            OverlayShowTitle.Text = show.Title;
            OverlayShowSummary.Text = string.IsNullOrEmpty(show.Summary) ? "No summary available." : show.Summary;
            
            // Safe, crash-proof image loading!
            try 
            { 
                if (!string.IsNullOrWhiteSpace(show.PosterUrl))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(show.PosterUrl, UriKind.RelativeOrAbsolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; 
                    bmp.EndInit();
                    OverlayShowPoster.Source = bmp;
                }
                else OverlayShowPoster.Source = null;
            } 
            catch { OverlayShowPoster.Source = null; }

            // Explicitly nuke the old UI state so WPF is forced to update!
            SeasonsList.SelectedIndex = -1;
            CurrentEpisodes.Clear();
            Seasons.Clear();

            // Fetch every episode for this show
            _allEpisodesForSelectedShow = await _libraryService.GetEpisodesForShowAsync(show.Title) ?? new List<MediaItem>();

            // Populate Column 1 (Unique Seasons)
            if (_allEpisodesForSelectedShow.Any())
            {
                var uniqueSeasons = _allEpisodesForSelectedShow.Select(ep => ep.SeasonNumber).Distinct().OrderBy(s => s).ToList();
                foreach (var s in uniqueSeasons) Seasons.Add(s);
            }

            // Open the overlay
            EpisodesOverlay.Visibility = Visibility.Visible;
            if (Seasons.Count > 0) SeasonsList.SelectedIndex = 0;

            // THE FIX: Push D-Pad focus into the Seasons list so the remote works instantly
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                if (SeasonsList.Items.Count > 0)
                {
                    var firstSeason = SeasonsList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                    firstSeason?.Focus();
                }
            }), DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Crash Prevented!\n\nError: {ex.Message}", "Debugging Info", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // OVERLAY LOGIC: User selects a Season Number
    private void SeasonsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (SeasonsList.SelectedItem is int selectedSeason)
            {
                CurrentEpisodes.Clear();
                var episodesForSeason = _allEpisodesForSelectedShow.Where(ep => ep.SeasonNumber == selectedSeason).OrderBy(ep => ep.EpisodeNumber);
                foreach (var ep in episodesForSeason) CurrentEpisodes.Add(ep);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading season: {ex.Message}", "Debugging Info", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        EpisodesOverlay.Visibility = Visibility.Collapsed;
    }
	
	private void SeasonItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Right)
        {
            // FOCUS BRIDGE: Jump Right into the Episodes List
            if (CurrentEpisodes.Count > 0)
            {
                EpisodesList.UpdateLayout(); // CRITICAL: Force UI to render the episodes instantly!
                var firstEp = EpisodesList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                
                if (firstEp != null) firstEp.Focus();
                else EpisodesList.Focus();
            }
            e.Handled = true;
        }
       else if (command == HtpcCommand.Left)
        {
            // FOCUS BRIDGE: Jump Left to the Action Buttons
            BingeShowBtn.Focus(); 
            e.Handled = true;
        }
        else if (command == HtpcCommand.Back)
        {
            // FOCUS BRIDGE: Escape back to the main grid
            CloseOverlay_Click(null!, null!);
            ShowsGrid.Focus();
            e.Handled = true;
        }
    }
	
	// --- 10-FOOT UI ROUTING FOR OVERLAY BUTTONS ---
    private void OverlayButtons_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Right)
        {
            // FOCUS BRIDGE: Jump Right into the Seasons List
            if (SeasonsList.Items.Count > 0)
            {
                SeasonsList.UpdateLayout();
                int targetIndex = SeasonsList.SelectedIndex >= 0 ? SeasonsList.SelectedIndex : 0;
                var seasonItem = SeasonsList.ItemContainerGenerator.ContainerFromIndex(targetIndex) as UIElement;
                seasonItem?.Focus();
            }
            e.Handled = true;
        }
        else if (command == HtpcCommand.Back || command == HtpcCommand.Left)
        {
            // Escape the overlay and go back to the library
            CloseOverlay_Click(null!, null!);
            ShowsGrid.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up && sender == BingeShowBtn)
        {
            // Explicitly force focus up to the Back button to bypass WPF spatial traps
            BackToLibraryBtn.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Down && sender == BackToLibraryBtn)
        {
            // Explicitly force focus down to the Binge button
            BingeShowBtn.Focus();
            e.Handled = true;
        }
        // --- NEW: FOCUS TRAPS ---
        else if (command == HtpcCommand.Down && sender == BingeShowBtn)
        {
            e.Handled = true; // Trap focus on the bottom button
        }
        else if (command == HtpcCommand.Up && sender == BackToLibraryBtn)
        {
            e.Handled = true; // Trap focus on the top button
        }
    }

    // THE FIX: Listen for Enter/OK on the Episode Items
    private void EpisodeItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem episode)
        {
            OnPlayRequested?.Invoke(this, episode);
            e.Handled = true;
        }
        else if (command == HtpcCommand.Left)
        {
            // FOCUS BRIDGE: Jump back to Seasons List
            SeasonsList.UpdateLayout(); 
            if (SeasonsList.SelectedItem != null)
            {
                var seasonItem = SeasonsList.ItemContainerGenerator.ContainerFromItem(SeasonsList.SelectedItem) as UIElement;
                seasonItem?.Focus();
            }
            else
            {
                SeasonsList.Focus();
            }
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up || command == HtpcCommand.Down)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as ListBoxItem)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
        else if (command == HtpcCommand.Right)
        {
            e.Handled = true; // Block escaping right into the void
        }
    }

    private void EpisodeCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem episode)
        {
            OnPlayRequested?.Invoke(this, episode);
        }
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
	
	// --- BINGE WATCH QUEUE ENGINE ---

    private void BingeShow_Click(object sender, RoutedEventArgs e)
    {
        if (_allEpisodesForSelectedShow == null || _allEpisodesForSelectedShow.Count == 0) return;

        int firstUnwatchedIndex = _allEpisodesForSelectedShow.FindIndex(ep => !ep.IsWatched);
        
        if (firstUnwatchedIndex > 0)
        {
            BingeChoiceOverlay.Visibility = Visibility.Visible;
            
            // FIX: Wait for WPF to completely finish drawing the popup before snatching focus
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                BingeResumeBtn.Focus();
                Keyboard.Focus(BingeResumeBtn); // Forcefully snatch hardware focus
            }), DispatcherPriority.ContextIdle);
        }
        else
        {
            LaunchBingeQueue(0);
        }
    }

    private void BingeResume_Click(object sender, RoutedEventArgs e)
    {
        int startIndex = _allEpisodesForSelectedShow.FindIndex(ep => !ep.IsWatched);
        LaunchBingeQueue(startIndex);
    }

    private void BingeBeginning_Click(object sender, RoutedEventArgs e)
    {
        LaunchBingeQueue(0);
    }

    private void LaunchBingeQueue(int startIndex)
    {
        // Hide all overlays
        BingeChoiceOverlay.Visibility = Visibility.Collapsed;
        EpisodesOverlay.Visibility = Visibility.Collapsed;
        
        // Send the queue to the player
        OnPlayQueueRequested?.Invoke(this, (_allEpisodesForSelectedShow, startIndex));
    }

    private void BingeCancel_Click(object sender, RoutedEventArgs e)
    {
        BingeChoiceOverlay.Visibility = Visibility.Collapsed;
        BingeShowBtn.Focus(); // Return focus back to the original Binge button
    }

    // --- 10-FOOT UI ROUTING FOR BINGE PROMPT ---
    private void BingeChoice_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Back || command == HtpcCommand.Left)
        {
            BingeCancel_Click(null!, null!);
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up || command == HtpcCommand.Down)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as Button)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
        else if (command == HtpcCommand.Right)
        {
            e.Handled = true; // Prevent focus from flying off to the right side of the screen
        }
    }

    // --- UPDATED NAVIGATION SIGNATURES (RoutedEventArgs) ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
	private void Recordings_Click(object sender, RoutedEventArgs e) => OnRecordingsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
	private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
	private void Collections_Click(object sender, RoutedEventArgs e) => OnCollectionsRequested?.Invoke(this, EventArgs.Empty);
}