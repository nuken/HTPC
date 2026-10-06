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

public class CastMember
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
}

public partial class MoviesView : UserControl
{
    private MediaItem? _activeMovieForDetails;
	private string _activeTrailerUrl = "";
    public ObservableCollection<CastMember> MovieCredits { get; set; } = new ObservableCollection<CastMember>();
	public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
	public event EventHandler? OnVodRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;
    public event EventHandler? OnRecordingsRequested;
    public event EventHandler? OnShowsRequested;
	public event EventHandler? OnSportsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnMultiviewRequested;
    public event EventHandler? OnCollectionsRequested;

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
    private enum FilterMode { None, Status, Sort, Order }
    private FilterMode _currentFilterMode = FilterMode.None;
    private IInputElement? _lastFocusedElement;

    private string _currentSearch = "";
    private string _currentGenre = "All";
    private string _currentStatus = "All Movies";
    private string _currentSort = "Date Added";
    private string _currentOrder = "Forward";

    public MoviesView(MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _serverManager = serverManager;
        this.DataContext = this;

        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _typingTimer.Tick += TypingTimer_Tick;

        Loaded += OnLoaded;
        this.PreviewKeyDown += MoviesView_PreviewKeyDown;
		CastItemsControl.ItemsSource = MovieCredits;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
		if (VodNavBtn != null)
{
    VodNavBtn.Visibility = PreferencesManager.Load().EnableAdbVodBridge ? Visibility.Visible : Visibility.Collapsed;
}
        ThemeToggleBtn.Content = PreferencesManager.LoadTheme() == "Dark" ? "\xE708" : "\xE706";

        if (_isInitialized) 
        {
            SearchBox.Focus();
            return;
        }

        // Load the saved sort preference (gracefully falling back)
        _currentSort = PreferencesManager.LoadMovieSort() ?? "Date Added";
        SortFilterBtn.Content = $"{_currentSort} ▼";

        _isInitialized = true;
        await ResetAndLoadAsync();

        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            SearchBox.Focus();
        }), DispatcherPriority.Input);
    }

    private void FocusTopNav()
    {
        if (TopNavPanel == null) return;
        foreach (UIElement child in TopNavPanel.Children)
        {
            if (child is Button btn && btn.Focusable && btn.Visibility == Visibility.Visible)
            {
                btn.Focus();
                return;
            }
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

        // Note: The service now receives _currentOrder alongside _currentSort
        var newMovies = await _libraryService.GetFilteredMoviesAsync(_currentOffset, _chunkSize, _currentSearch, _currentGenre, _currentSort, _currentOrder, _currentStatus);
        
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

    // --- OVERLAY FILTERS ---

    private void StatusFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.Status;
        FilterOverlayTitle.Text = "Filter Status";
        FilterSelectionList.ItemsSource = new[] { "All Movies", "Favorites", "Watched", "Unwatched" };
        FilterSelectionList.SelectedItem = _currentStatus;
        OpenFilterOverlay();
    }

    private void SortFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.Sort;
        FilterOverlayTitle.Text = "Sort By";
        FilterSelectionList.ItemsSource = new[] { "Alphabetically", "Date Released", "Date Added", "Date Watched", "Date Favorited", "Duration", "Rating" };
        FilterSelectionList.SelectedItem = _currentSort;
        OpenFilterOverlay();
    }

    private void OrderFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.Order;
        FilterOverlayTitle.Text = "Order";
        FilterSelectionList.ItemsSource = new[] { "Forward", "Reverse" };
        FilterSelectionList.SelectedItem = _currentOrder;
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

    private async void ProcessFilterSelection(object selectedItem)
    {
        if (selectedItem is string selection)
        {
            if (_currentFilterMode == FilterMode.Status)
            {
                _currentStatus = selection;
                StatusFilterBtn.Content = $"{selection} ▼";
            }
            else if (_currentFilterMode == FilterMode.Sort)
            {
                _currentSort = selection;
                SortFilterBtn.Content = $"{selection} ▼";
                try { PreferencesManager.SaveMovieSort(_currentSort); } catch { }
            }
            else if (_currentFilterMode == FilterMode.Order)
            {
                _currentOrder = selection;
                OrderFilterBtn.Content = $"{selection} ▼";
            }

            CloseFilterOverlay();
            await ResetAndLoadAsync();
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
            // Focus the first genre pill
            if (GenrePanel.Children.Count > 0)
            {
                (GenrePanel.Children[0] as UIElement)?.Focus();
                e.Handled = true;
            }
        }
        else if (command == HtpcCommand.Up)
        {
            FocusTopNav();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Left)
        {
            if (sender == OrderFilterBtn) { SortFilterBtn.Focus(); e.Handled = true; }
            else if (sender == SortFilterBtn) { StatusFilterBtn.Focus(); e.Handled = true; }
            else if (sender == StatusFilterBtn) 
            { 
                if (ClearSearchBtn.Visibility == Visibility.Visible)
                {
                    ClearSearchBtn.Focus();
                }
                else
                {
                    SearchBox.Focus();
                }
                e.Handled = true; 
            }
        }
        else if (command == HtpcCommand.Right)
        {
            if (sender == StatusFilterBtn) { SortFilterBtn.Focus(); e.Handled = true; }
            else if (sender == SortFilterBtn) { OrderFilterBtn.Focus(); e.Handled = true; }
            else if (sender == OrderFilterBtn) { e.Handled = true; } // Trap right
        }
    }
   
   // --- SEARCH ---

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

    private async void Genre_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (sender is RadioButton rb)
        {
            _currentGenre = rb.Content.ToString() ?? "All";
            await ResetAndLoadAsync();
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        var tb = sender as TextBox;

        // FOCUS BRIDGE: Jump to ClearSearchBtn if visible, otherwise jump to StatusFilterBtn
        if (command == HtpcCommand.Right)
        {
            if (tb != null && tb.CaretIndex >= tb.Text.Length)
            {
                if (ClearSearchBtn.Visibility == Visibility.Visible)
                {
                    ClearSearchBtn.Focus();
                }
                else
                {
                    StatusFilterBtn.Focus();
                }
                e.Handled = true;
                return;
            }
        }
        else if (command == HtpcCommand.Down)
        {
            if (MoviesGrid.Items.Count > 0)
            {
                var rowElement = MoviesGrid.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                rowElement?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                e.Handled = true;
            }
        }
        else if (command == HtpcCommand.Up)
        {
            FocusTopNav();
            e.Handled = true; 
        }
    }
	
	private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void ClearSearchBtn_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Left)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Right)
        {
            StatusFilterBtn.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up)
        {
            FocusTopNav();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Down)
        {
            if (GenrePanel.Children.Count > 0)
            {
                (GenrePanel.Children[0] as UIElement)?.Focus();
            }
            else if (MoviesGrid.Items.Count > 0)
            {
                var rowElement = MoviesGrid.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                rowElement?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
            e.Handled = true;
        }
    }
    
    private void GenrePill_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
		
		if (command == HtpcCommand.Select || e.Key == Key.Enter)
    {
        if (sender is RadioButton rb)
        {
            rb.IsChecked = true;
            e.Handled = true;
            return;
        }
    }
        
        if (command == HtpcCommand.Down || command == HtpcCommand.Up)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as RadioButton)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
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
            OpenMovieDetails(movie); 
            e.Handled = true;
        }
    }
    
    private void MoviesView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (command == HtpcCommand.Back || e.Key == Key.Escape || e.Key == Key.BrowserBack || e.Key == Key.Back)
        {
            if (FilterOverlay.Visibility == Visibility.Visible)
            {
                CloseFilterOverlay();
                e.Handled = true;
                return;
            }

            if (MovieDetailsOverlay != null && MovieDetailsOverlay.Visibility == Visibility.Visible)
            {
                CloseMovieDetails_Click(null!, null!);
                MoviesGrid.Focus(); 
                e.Handled = true;
                return;
            }

            if (MediaInfoModal != null && MediaInfoModal.Visibility == Visibility.Visible)
            {
                CloseMediaInfo_Click(null!, null!);
                e.Handled = true;
                return;
            }
        }
    }

    private void MovieCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem movie)
        {
            OpenMovieDetails(movie); 
        }
    }
    
    // --- MOVIE DETAILS ENGINE ---
    
    private async void OpenMovieDetails(MediaItem movie)
    {
        _activeMovieForDetails = movie;
    _activeTrailerUrl = "";
    DetailTrailerBtn.Visibility = Visibility.Collapsed;
    MovieCredits.Clear();

        DetailTitle.Text = movie.Title;
        DetailSummary.Text = !string.IsNullOrEmpty(movie.Summary) ? movie.Summary : "No summary available.";
        
        try
        {
            if (!string.IsNullOrWhiteSpace(movie.PosterUrl))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(movie.PosterUrl, UriKind.RelativeOrAbsolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                DetailPoster.Source = bmp;
            }
            else DetailPoster.Source = null;
        }
        catch { DetailPoster.Source = null; }

        DetailYear.Text = "----";
        DetailRating.Text = "NR";
        DetailDuration.Text = "--m";
        DetailGenres.Text = "";
        
        MovieDetailsOverlay.Visibility = Visibility.Visible;

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            DetailPlayBtn.Focus();
            Keyboard.Focus(DetailPlayBtn);
        }), DispatcherPriority.ContextIdle);

        try
        {
            var activeServer = _serverManager.GetActiveServer();
            if (activeServer != null)
            {
                string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
                using var client = new System.Net.Http.HttpClient();
                
                // --- FIX 1: Fetch from the raw DVR endpoint instead of the stripped API endpoint ---
                var response = await client.GetStringAsync($"{baseUrl}/dvr/files/{movie.Id}");

                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var root = doc.RootElement;
                
                // --- FIX 2: Map the PascalCase properties correctly ---
                if (root.TryGetProperty("Duration", out var dur) && dur.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    if (double.TryParse(dur.ToString(), out double seconds))
                    {
                        var t = TimeSpan.FromSeconds(seconds);
                        DetailDuration.Text = t.Hours > 0 ? $"{t.Hours}h {t.Minutes}m" : $"{t.Minutes}m";
                    }
                }

                // All rich metadata lives inside the "Airing" object
                if (root.TryGetProperty("Airing", out var airing))
                {
                    if (airing.TryGetProperty("ReleaseYear", out var yr) && yr.ValueKind != System.Text.Json.JsonValueKind.Null) 
                        DetailYear.Text = yr.ToString();
                    
                    if (airing.TryGetProperty("ContentRating", out var cr) && cr.ValueKind != System.Text.Json.JsonValueKind.Null) 
                        DetailRating.Text = cr.ToString();

                    if (airing.TryGetProperty("Genres", out var gen) && gen.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var genresList = new System.Collections.Generic.List<string>();
                        foreach (var g in gen.EnumerateArray()) genresList.Add(g.ToString());
                        DetailGenres.Text = string.Join(" • ", genresList);
                    }

                    if (airing.TryGetProperty("FullSummary", out var fSum) && fSum.ValueKind != System.Text.Json.JsonValueKind.Null && !string.IsNullOrWhiteSpace(fSum.ToString()))
                    {
                         DetailSummary.Text = fSum.ToString();
                    }
                    else if (airing.TryGetProperty("Summary", out var sum) && sum.ValueKind != System.Text.Json.JsonValueKind.Null && !string.IsNullOrWhiteSpace(sum.ToString()))
                    {
                         DetailSummary.Text = sum.ToString();
                    }

                    // Map the TMDB rich cast/crew and primary trailer
                    if (airing.TryGetProperty("MovieInfo", out var movieInfo) && movieInfo.TryGetProperty("TMDB", out var tmdb))
                    {
                        if (tmdb.TryGetProperty("TrailerURL", out var tUrl) && tUrl.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            _activeTrailerUrl = tUrl.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(_activeTrailerUrl)) DetailTrailerBtn.Visibility = Visibility.Visible;
                        }

                        if (tmdb.TryGetProperty("Credits", out var credits) && credits.ValueKind == System.Text.Json.JsonValueKind.Array)
{
    int castCount = 0; // Add a counter
    foreach (var credit in credits.EnumerateArray())
    {
        if (castCount >= 18) break; // Hard limit to 18 items for the UI grid

        string name = credit.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
        string character = credit.TryGetProperty("Character", out var c) ? c.GetString() ?? "" : "";
        string job = credit.TryGetProperty("Job", out var j) ? j.GetString() ?? "" : "";
        string image = credit.TryGetProperty("Image", out var img) ? img.GetString() ?? "" : "";
        
        if (!string.IsNullOrWhiteSpace(name))
        {
            MovieCredits.Add(new CastMember 
            { 
                Name = name, 
                Role = !string.IsNullOrWhiteSpace(character) ? character : job,
                ImageUrl = image 
            });
            castCount++; // Increment the counter
        }
    }
}
                    }

                    // Fallback basic text Credits (if TMDB is missing)
                    if (MovieCredits.Count == 0)
                    {
                        if (airing.TryGetProperty("Directors", out var dirs) && dirs.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var d in dirs.EnumerateArray()) MovieCredits.Add(new CastMember { Name = d.ToString(), Role = "Director" });
                        }
                        if (airing.TryGetProperty("Cast", out var cast) && cast.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var c in cast.EnumerateArray()) MovieCredits.Add(new CastMember { Name = c.ToString(), Role = "Actor" });
                        }
                    }
                }

                // Fallback Trailer Check (from Extras array at the root level)
                if (string.IsNullOrWhiteSpace(_activeTrailerUrl) && root.TryGetProperty("Extras", out var extras) && extras.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var extra in extras.EnumerateArray())
                    {
                        if (extra.TryGetProperty("Type", out var typeProp) && typeProp.GetString() == "trailer")
                        {
                            _activeTrailerUrl = extra.TryGetProperty("Link", out var linkProp) ? linkProp.GetString() ?? "" : "";
                            if (!string.IsNullOrWhiteSpace(_activeTrailerUrl)) DetailTrailerBtn.Visibility = Visibility.Visible;
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DetailSummary.Text += $"\n\n(API Error: {ex.Message})"; 
        }
}

    private void DetailPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_activeMovieForDetails != null)
        {
            OnPlayRequested?.Invoke(this, _activeMovieForDetails);
        }
    }

    private void CloseMovieDetails_Click(object sender, RoutedEventArgs e)
    {
        MovieDetailsOverlay.Visibility = Visibility.Collapsed;
    }
	
	private async void DetailTrailer_Click(object sender, RoutedEventArgs e)
{
    if (string.IsNullOrWhiteSpace(_activeTrailerUrl) || _activeMovieForDetails == null) return;

    string originalText = DetailTrailerBtn.Content.ToString() ?? "";
    DetailTrailerBtn.Content = "⏳ Resolving Trailer...";
    DetailTrailerBtn.IsEnabled = false;

    // Extract the raw direct MP4 stream URL using yt-dlp
    string rawStreamUrl = await GetRawTrailerUrlAsync(_activeTrailerUrl);

    DetailTrailerBtn.Content = originalText;
    DetailTrailerBtn.IsEnabled = true;

    // Check if we got a successful HTTP stream link back
    if (!string.IsNullOrWhiteSpace(rawStreamUrl) && rawStreamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
    {
        var trailerItem = new MediaItem
        {
            Id = $"trailer_{_activeMovieForDetails.Id}",
            Title = $"{_activeMovieForDetails.Title} Trailer",
            CurrentShowTitle = "YouTube",
            StreamUrl = rawStreamUrl,
            PosterUrl = _activeMovieForDetails.PosterUrl,
            IsLiveTv = false,
            Duration = 0
        };

        OnPlayRequested?.Invoke(this, trailerItem);
    }
    else
    {
        // If it failed, print the EXACT error returned by yt-dlp to the screen so we can debug it
        DetailSummary.Text += $"\n\n[Trailer Error]: {rawStreamUrl}";
    }
}

private void CastMember_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.DataContext is CastMember person)
    {
        // 1. Close the movie details overlay
        CloseMovieDetails_Click(null!, null!);

        // 2. Reset the view to top and clear any active genre filters
        MainScroll.ScrollToTop();
        if (GenrePanel.Children.Count > 0 && GenrePanel.Children[0] is RadioButton allBtn)
        {
            allBtn.IsChecked = true;
        }

        // 3. Inject the actor/director name into the search box
        // This will automatically trigger the _typingTimer and reload the grid!
        SearchBox.Text = person.Name;
        
        // 4. Move focus to the search box so the user knows what happened
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }
}

private async Task<string> GetRawTrailerUrlAsync(string youtubeUrl)
{
    try
    {
        string cleanUrl = youtubeUrl.Replace("/embed/", "/watch?v=");
        if (cleanUrl.Contains("?si=")) cleanUrl = cleanUrl.Substring(0, cleanUrl.IndexOf("?si="));

        // Use the native .net extraction directory created by PublishSingleFile
        string appDirectory = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? AppDomain.CurrentDomain.BaseDirectory) ?? "";
        string ytDlpPath = System.IO.Path.Combine(appDirectory, "yt-dlp.exe");

        // If the exe hasn't been extracted to this temp folder yet, unpack it from the embedded resource!
        if (!System.IO.File.Exists(ytDlpPath))
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            string? resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("yt-dlp.exe", StringComparison.OrdinalIgnoreCase));
            
            if (!string.IsNullOrEmpty(resourceName))
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var fileStream = new System.IO.FileStream(ytDlpPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                    await stream.CopyToAsync(fileStream);
                }
            }
            else
            {
                return "yt-dlp.exe is missing from Embedded Resources.";
            }
        }

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = $"-g -f b --no-warnings --extractor-args \"youtube:player_client=android\" \"{cleanUrl}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true, 
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync(); 
        await process.WaitForExitAsync();

        string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0 && lines[0].StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return lines[0];
        }
        
        return string.IsNullOrWhiteSpace(error) ? "Unknown resolution failure." : error.Trim();
    }
    catch (Exception ex)
    {
        return ex.Message;
    }
}

private void MovieDetailsButtons_PreviewKeyDown(object sender, KeyEventArgs e)
{
    var command = InputMapper.GetCommand(e.Key);

    if (command == HtpcCommand.Right) { e.Handled = true; }
    else if (command == HtpcCommand.Left || command == HtpcCommand.Back)
    {
        CloseMovieDetails_Click(null!, null!);
        e.Handled = true;
    }
    else if (command == HtpcCommand.Up && sender == DetailBackBtn) { e.Handled = true; }
    else if (command == HtpcCommand.Down && sender == DetailTrailerBtn) { e.Handled = true; }
    else if (command == HtpcCommand.Down && sender == DetailPlayBtn)
    {
        if (DetailTrailerBtn.Visibility == Visibility.Visible) DetailTrailerBtn.Focus();
        e.Handled = true; 
    }
    else if (command == HtpcCommand.Up && sender == DetailTrailerBtn)
    {
        DetailPlayBtn.Focus();
        e.Handled = true;
    }
    else if (command == HtpcCommand.Up && sender == DetailPlayBtn)
    {
        DetailBackBtn.Focus();
        e.Handled = true;
    }
    else if (command == HtpcCommand.Down && sender == DetailBackBtn)
    {
        DetailPlayBtn.Focus();
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
            
            ShowToast($"Sending command: {menuItem.Header}...");

            bool success = await _libraryService.SendFileAdminCommandAsync(baseUrl, movie.Id, command);

            if (success) 
            {
                ShowToast($"Success: {menuItem.Header} triggered.");
                
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
                    if (menuItem.Tag?.ToString() == "watch")
                        menuItem.Visibility = movie.IsWatched ? Visibility.Collapsed : Visibility.Visible;
                    
                    if (menuItem.Tag?.ToString() == "unwatch")
                        menuItem.Visibility = movie.IsWatched ? Visibility.Visible : Visibility.Collapsed;

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

        _ = Task.Run(async () => 
        {
            await Task.Delay(3000);
            Application.Current.Dispatcher.Invoke(() => ToastNotification.Visibility = Visibility.Collapsed);
        });
    }

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

                    AddMediaInfoRow("File ID", movie.Id);

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

    private void AddTrackInfo(int trackIndex, string codec, string details)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 15, 0, 0) };
        
        panel.Children.Add(new TextBlock 
        { 
            Text = $"Track #{trackIndex}: {codec}", 
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 164, 239)),
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
	private void Vod_Click(object sender, RoutedEventArgs e) => OnVodRequested?.Invoke(this, EventArgs.Empty);
	private void Recordings_Click(object sender, RoutedEventArgs e) => OnRecordingsRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
	private void Sports_Click(object sender, RoutedEventArgs e) => OnSportsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
	private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
	private void Collections_Click(object sender, RoutedEventArgs e) => OnCollectionsRequested?.Invoke(this, EventArgs.Empty);
}
