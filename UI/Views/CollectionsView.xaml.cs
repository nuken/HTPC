using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using HTPC.Core.Input;
using HTPC.Core.Models;
using HTPC.UI.ViewModels;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class CollectionsView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMultiviewRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnRecordingsRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler? OnExitRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;
    public event EventHandler<(System.Collections.Generic.List<MediaItem> Queue, int StartIndex)>? OnPlayQueueRequested;
    
    private System.Collections.Generic.List<MediaItem> _allEpisodesForSelectedShow = new();
    
    private readonly CollectionsViewModel _viewModel;
    private IInputElement? _lastFocusedElement;
    
    private enum FilterMode { None, Sort, Order }
    private FilterMode _currentFilterMode = FilterMode.None;
    private string _currentSort = "Alphabetical";
    private string _currentOrder = "Forward";
    
    private System.Collections.Generic.List<MediaItem> _masterCollectionContents = new();
    private readonly DispatcherTimer _searchTimer;

    // --- LAZY LOADING VARIABLES ---
    private System.Collections.Generic.List<MediaItem> _activeCollectionContents = new();
    public ObservableCollection<MediaItem> ModalMediaItems { get; set; } = new();
    private int _modalOffset = 0;
    private const int _chunkSize = 50;

    public CollectionsView(CollectionsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        
        // Bind the modal ListBox to the lazy-loading collection
        CollectionContentList.ItemsSource = ModalMediaItems;
        
        // Initialize Search Timer
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _searchTimer.Tick += SearchTimer_Tick;
        
        Loaded += OnLoaded;
        this.PreviewKeyDown += CollectionsView_PreviewKeyDown;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeToggleBtn.Content = PreferencesManager.LoadTheme() == "Dark" ? "\xE708" : "\xE706";
        ContentModal.Visibility = Visibility.Collapsed;
        EpisodesOverlay.Visibility = Visibility.Collapsed;
        await _viewModel.LoadCollectionsAsync();

        _ = Dispatcher.InvokeAsync(() => 
        {
            var rowElement = MovieCollectionsList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
            rowElement?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }, DispatcherPriority.Loaded);
    }

    // --- TOP NAVIGATION HANDLERS ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Recordings_Click(object sender, RoutedEventArgs e) => OnRecordingsRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
    private void ExitApp_Click(object sender, RoutedEventArgs e) => OnExitRequested?.Invoke(this, EventArgs.Empty);
    
    // --- COLLECTION SELECTION ---
    private void CollectionCard_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            if (element.IsMouseOver || Mouse.LeftButton == MouseButtonState.Pressed) return;

            try
            {
                var transform = element.TransformToAncestor(MainScroll.Content as UIElement);
                Point position = transform.Transform(new Point(0, 0));
                double targetY = position.Y - 100;
                MainScroll.ScrollToVerticalOffset(targetY < 0 ? 0 : targetY);
            }
            catch { }
        }
    }

    private async void CollectionCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is CollectionItem collection)
        {
            await OpenCollectionModal(collection);
            e.Handled = true;
        }
    }

    private void ThemeToggleBtn_Click(object sender, RoutedEventArgs e)
{
    string currentTheme = PreferencesManager.LoadTheme();
    string newTheme = currentTheme == "Dark" ? "Light" : "Dark";

    // Save to preferences
    PreferencesManager.SaveTheme(newTheme);

    // Switch the application resources
    ((App)Application.Current).ApplyTheme(newTheme);

    // Toggle the button icon (Moon for Dark, Sun for Light)
    ThemeToggleBtn.Content = newTheme == "Dark" ? "\xE708" : "\xE706";
}

    private async System.Threading.Tasks.Task OpenCollectionModal(CollectionItem collection)
{
    _lastFocusedElement = Keyboard.FocusedElement;
    ModalTitle.Text = collection.Name;
    
    CollectionSearchBox.Text = string.Empty;
    
    // Load saved preferences instead of resetting to defaults
    _currentSort = PreferencesManager.LoadCollectionSort() ?? "Alphabetical";
    _currentOrder = PreferencesManager.LoadCollectionOrder() ?? "Forward";
    
    if (CollectionSortBtn != null) CollectionSortBtn.Content = $"{_currentSort} ▼";
    if (CollectionOrderBtn != null) CollectionOrderBtn.Content = $"{_currentOrder} ▼";

    _masterCollectionContents = await _viewModel.GetCollectionContentsAsync(collection.Id);
    
    // Force the initial sort using the loaded preferences
    ApplyCollectionSorting();
    
    ContentModal.Visibility = Visibility.Visible;

    _ = Dispatcher.InvokeAsync(() => 
    {
        if (CollectionContentList.Items.Count > 0)
        {
            var rowElement = CollectionContentList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
            rowElement?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
        else
        {
            CloseModalBtn.Focus();
        }
    }, DispatcherPriority.Loaded);
}
    
	private void CollectionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        ApplyCollectionSorting();
    }

    private void CollectionSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_masterCollectionContents == null || _masterCollectionContents.Count == 0) return;
        ApplyCollectionSorting();
    }

    private void ApplyCollectionSorting()
    {
        if (_masterCollectionContents == null || _masterCollectionContents.Count == 0) return;

        string query = CollectionSearchBox.Text.ToLower().Trim();
        var filtered = _masterCollectionContents.AsEnumerable();

        // Apply Search Filter First
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(m => 
                (m.Title != null && m.Title.ToLower().Contains(query)) ||
                (m.CurrentShowTitle != null && m.CurrentShowTitle.ToLower().Contains(query)) ||
                (m.Summary != null && m.Summary.ToLower().Contains(query)) ||
                (m.Cast != null && m.Cast.Any(c => c.ToLower().Contains(query))) ||
                (m.Directors != null && m.Directors.Any(d => d.ToLower().Contains(query))) ||
                (m.Genres != null && m.Genres.Any(g => g.ToLower().Contains(query)))
            );
        }

        // Determine Sort Type and Order from state variables
        string sortType = _currentSort;
        bool isReverse = _currentOrder == "Reverse";

        IOrderedEnumerable<MediaItem> sorted;

        string StripArticles(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";
            string lower = title.ToLower();
            if (lower.StartsWith("the ")) return title.Substring(4);
            if (lower.StartsWith("a ")) return title.Substring(2);
            if (lower.StartsWith("an ")) return title.Substring(3);
            return title;
        }

        switch (sortType)
        {
            case "Date Added":
                sorted = isReverse ? filtered.OrderBy(m => m.CreatedAt) : filtered.OrderByDescending(m => m.CreatedAt);
                break;
            case "Release Year":
                sorted = isReverse ? filtered.OrderBy(m => m.ReleaseYear) : filtered.OrderByDescending(m => m.ReleaseYear);
                break;
            case "Date Watched":
                sorted = isReverse ? filtered.OrderBy(m => m.LastWatchedAt) : filtered.OrderByDescending(m => m.LastWatchedAt);
                break;
            case "Date Updated":
                // Fallbacks to LastRecordedAt for TV shows
                sorted = isReverse ? filtered.OrderBy(m => Math.Max(m.UpdatedAt, m.LastRecordedAt)) : filtered.OrderByDescending(m => Math.Max(m.UpdatedAt, m.LastRecordedAt));
                break;
            case "Duration":
                sorted = isReverse ? filtered.OrderByDescending(m => m.Duration) : filtered.OrderBy(m => m.Duration);
                break;
            case "Rating":
                sorted = isReverse ? filtered.OrderByDescending(m => m.ContentRating) : filtered.OrderBy(m => m.ContentRating);
                break;
            case "Favorites":
                // Brings favorites to the top, then sorts by title
                sorted = isReverse ? filtered.OrderBy(m => m.IsFavorite).ThenByDescending(m => StripArticles(m.Title)) 
                                   : filtered.OrderByDescending(m => m.IsFavorite).ThenBy(m => StripArticles(m.Title));
                break;
            case "Alphabetical":
            default:
                sorted = isReverse ? filtered.OrderByDescending(m => StripArticles(m.Title)) : filtered.OrderBy(m => StripArticles(m.Title));
                break;
        }

        _activeCollectionContents = sorted.ToList();
        
        ModalMediaItems.Clear();
        _modalOffset = 0;
        LoadNextModalChunk();
    }
    
    private void CollectionSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        // Let Left/Right natively control the text caret inside the box. 
        // We only intercept Up/Down for UI escaping.
        
        if (command == HtpcCommand.Down)
        {
            // Bridge straight down into the media grid
            if (CollectionContentList.Items.Count > 0)
            {
                var rowElement = CollectionContentList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                rowElement?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                e.Handled = true;
            }
        }
        else if (command == HtpcCommand.Up)
        {
            // Prevent focus from falling off the top edge of the screen
            e.Handled = true; 
        }
    }

    private void LoadNextModalChunk()
    {
        if (_modalOffset >= _activeCollectionContents.Count) return;

        var chunk = _activeCollectionContents.Skip(_modalOffset).Take(_chunkSize).ToList();
        foreach (var item in chunk)
        {
            ModalMediaItems.Add(item);
        }
        
        _modalOffset += chunk.Count;
    }

    private void ModalScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 100)
            {
                LoadNextModalChunk();
            }
        }
    }

    // --- BINGE WATCH QUEUE BUILDERS ---

    private async void BingeMovies_Click(object sender, RoutedEventArgs e)
    {
        // Use the flat list we already retrieved instead of digging through the UI rows
        var queue = new System.Collections.Generic.List<MediaItem>(_activeCollectionContents);
        
        if (queue.Count == 0) return;

        bool isSeriesCollection = queue.Any(item => item.Categories != null && 
            (item.Categories.Contains("Show", StringComparer.OrdinalIgnoreCase) || 
             item.Categories.Contains("Series", StringComparer.OrdinalIgnoreCase)));

        if (isSeriesCollection)
        {
            string originalTitle = ModalTitle.Text;
            ModalTitle.Text = "Building Binge Queue..."; 
            
            var masterEpisodesQueue = new System.Collections.Generic.List<MediaItem>();
            
            foreach (var show in queue)
            {
                var episodes = await _viewModel.GetShowEpisodesAsync(show.Id);
                if (episodes != null)
                {
                    foreach (var ep in episodes)
                    {
                        if (!ep.IsWatched) masterEpisodesQueue.Add(ep);
                    }
                }
            }
            
            if (masterEpisodesQueue.Count == 0)
            {
                ModalTitle.Text = "No unwatched episodes found.";
                await System.Threading.Tasks.Task.Delay(2000);
                ModalTitle.Text = originalTitle;
                return;
            }
            
            ContentModal.Visibility = Visibility.Collapsed;
            ModalTitle.Text = originalTitle; 
            
            OnPlayQueueRequested?.Invoke(this, (masterEpisodesQueue, 0));
        }
        else
        {
            int startIndex = queue.FindIndex(m => !m.IsWatched);
            if (startIndex == -1) startIndex = 0;

            ContentModal.Visibility = Visibility.Collapsed;
            OnPlayQueueRequested?.Invoke(this, (queue, startIndex));
        }
    }

    // --- BINGE WATCH TV SHOW QUEUE ENGINE ---

    private void BingeShow_Click(object sender, RoutedEventArgs e)
    {
        if (_allEpisodesForSelectedShow == null || _allEpisodesForSelectedShow.Count == 0) return;

        int firstUnwatchedIndex = _allEpisodesForSelectedShow.FindIndex(ep => !ep.IsWatched);
        
        if (firstUnwatchedIndex > 0)
        {
            BingeChoiceOverlay.Visibility = Visibility.Visible;
            
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                BingeResumeBtn.Focus();
                Keyboard.Focus(BingeResumeBtn); 
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
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
        BingeChoiceOverlay.Visibility = Visibility.Collapsed;
        EpisodesOverlay.Visibility = Visibility.Collapsed;
        ContentModal.Visibility = Visibility.Collapsed;
        
        OnPlayQueueRequested?.Invoke(this, (_allEpisodesForSelectedShow, startIndex));
    }

    private void BingeCancel_Click(object sender, RoutedEventArgs e)
    {
        BingeChoiceOverlay.Visibility = Visibility.Collapsed;
        
        var bingeBtn = EpisodesOverlay.FindName("BingeShowBtn") as UIElement;
        bingeBtn?.Focus(); 
    }

    // --- 10-FOOT UI ROUTING FOR BINGE PROMPT ---
    private void BingeChoice_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = HTPC.Core.Input.InputMapper.GetCommand(e.Key);
        
        if (command == HTPC.Core.Input.HtpcCommand.Back || command == HTPC.Core.Input.HtpcCommand.Left)
        {
            BingeCancel_Click(null!, null!);
            e.Handled = true;
        }
        else if (command == HTPC.Core.Input.HtpcCommand.Up || command == HtpcCommand.Down)
        {
            var direction = command == HTPC.Core.Input.HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as Button)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
        else if (command == HTPC.Core.Input.HtpcCommand.Right)
        {
            e.Handled = true; 
        }
    }

    // --- MEDIA ITEM SELECTION (Inside Modal) ---

    private void MediaCard_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            if (element.IsMouseOver || Mouse.LeftButton == MouseButtonState.Pressed) return;
        }
    }

    private void MediaCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem media)
        {
            PlaySelectedMedia(media);
            e.Handled = true;
        }
    }

    // --- REMOTE CONTROL & D-PAD HARDWARE ROUTING ---

    private void TopNavPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = HTPC.Core.Input.InputMapper.GetCommand(e.Key);
        bool isDown = command == HTPC.Core.Input.HtpcCommand.Down || e.Key == Key.Down;

        if (isDown)
        {
            if (MovieCollectionsList.Items.Count > 0)
            {
                FocusItemInList(MovieCollectionsList);
                e.Handled = true;
            }
            else if (ShowCollectionsList.Items.Count > 0)
            {
                FocusItemInList(ShowCollectionsList);
                e.Handled = true;
            }
        }
    }
    
    // --- MASTER REMOTE BACK HANDLER ---
    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (BingeChoiceOverlay.Visibility == Visibility.Visible) return;
        
        if (command == HtpcCommand.Back || e.Key == Key.Escape || e.Key == Key.BrowserBack || e.Key == Key.Back)
        {
            if (EpisodesOverlay.Visibility == Visibility.Visible)
            {
                CloseEpisodesOverlay_Click(null!, null!);
                e.Handled = true; 
            }
            else if (ContentModal.Visibility == Visibility.Visible)
            {
                CloseModal_Click(null!, null!);
                e.Handled = true; 
            }
        }
    }

    private void CollectionCard_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = HTPC.Core.Input.InputMapper.GetCommand(e.Key);
        bool isSelect = command == HTPC.Core.Input.HtpcCommand.Select || e.Key == Key.Enter;
        bool isUp = command == HTPC.Core.Input.HtpcCommand.Up || e.Key == Key.Up;

        if (!(sender is ListBoxItem item) || !(item.DataContext is CollectionItem collection)) return;

        if (isSelect)
        {
            _ = OpenCollectionModal(collection);
            e.Handled = true;
            return;
        }

        if (isUp)
        {
            // Simply bounce focus up. Since WPF is handling the grid naturally now, 
            // checking coordinates will natively bump to the top menu if it's the top row.
            var index = MovieCollectionsList.ItemContainerGenerator.IndexFromContainer(item);
            if (index < 0) index = ShowCollectionsList.ItemContainerGenerator.IndexFromContainer(item);
            
            // If it's one of the first few items in the first wrapped row, bump to nav
            if (index >= 0 && index < 6) 
            {
                FocusTopNav();
                e.Handled = true;
            }
        }
    }

    private void MediaCard_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        bool isUp = command == HtpcCommand.Up || e.Key == Key.Up;

        if (!(sender is ListBoxItem item) || !(item.DataContext is MediaItem media)) return;

        if (command == HtpcCommand.Select)
        {
            PlaySelectedMedia(media);
            e.Handled = true;
        }
        else if (isUp)
        {
            var index = CollectionContentList.ItemContainerGenerator.IndexFromContainer(item);
            
            // If they push UP from the top row (first 6 items)
            if (index >= 0 && index < 6)
            {
                // If they are on the right half of the screen, bridge to the Search Box
                if (index >= 3) CollectionSearchBox.Focus();
                // Otherwise, bridge to the Back button
                else CloseModalBtn.Focus();
                
                e.Handled = true;
            }
        }
        else if (command == HtpcCommand.Back || e.Key == Key.Escape)
        {
            CloseModal_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void CloseModalBtn_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Down || e.Key == Key.Down)
        {
            if (CollectionContentList.Items.Count > 0)
            {
                var rowElement = CollectionContentList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                rowElement?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                e.Handled = true;
            }
        }
    }

    // --- FOCUS HELPERS ---

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

    private void FocusItemInList(ItemsControl list)
    {
        if (list.Items.Count > 0)
        {
            var rowElement = list.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
            if (rowElement != null)
            {
                rowElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
            else
            {
                list.UpdateLayout();
                rowElement = list.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                rowElement?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
        }
    }
    
    private void CloseModal_Click(object sender, RoutedEventArgs e)
    {
        ContentModal.Visibility = Visibility.Collapsed;
        if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
            Keyboard.Focus(_lastFocusedElement);
    }
    
    private void PlaySelectedMedia(MediaItem media)
    {
        bool isSeries = media.Categories != null && 
                       (media.Categories.Contains("Show", StringComparer.OrdinalIgnoreCase) || 
                        media.Categories.Contains("Series", StringComparer.OrdinalIgnoreCase));

        if (isSeries)
        {
            OpenShowOverlay(media);
        }
        else
        {
            ContentModal.Visibility = Visibility.Collapsed;
            EpisodesOverlay.Visibility = Visibility.Collapsed;
            OnPlayRequested?.Invoke(this, media);
            
            if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
                Keyboard.Focus(_lastFocusedElement);
        }
    }

    // --- SEASONS / EPISODES OVERLAY LOGIC ---

    private async void OpenShowOverlay(MediaItem show)
    {
        try
        {
            OverlayShowTitle.Text = show.Title;
            OverlayShowSummary.Text = string.IsNullOrEmpty(show.Summary) ? "No summary available." : show.Summary;
            
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

            SeasonsList.SelectedIndex = -1;
            _viewModel.CurrentEpisodes.Clear();
            _viewModel.Seasons.Clear();

            _allEpisodesForSelectedShow = await _viewModel.GetShowEpisodesAsync(show.Id) ?? new System.Collections.Generic.List<MediaItem>();

            if (_allEpisodesForSelectedShow.Count > 0)
            {
                var uniqueSeasons = System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(_allEpisodesForSelectedShow, ep => ep.SeasonNumber)), s => s).ToList();
                foreach (var s in uniqueSeasons) _viewModel.Seasons.Add(s);
            }

            EpisodesOverlay.Visibility = Visibility.Visible;
            if (_viewModel.Seasons.Count > 0) SeasonsList.SelectedIndex = 0;

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
            MessageBox.Show($"Error loading episodes: {ex.Message}");
        }
    }

    private void SeasonsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeasonsList.SelectedItem is int selectedSeason)
        {
            _viewModel.CurrentEpisodes.Clear();
            var episodesForSeason = System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Where(_allEpisodesForSelectedShow, ep => ep.SeasonNumber == selectedSeason), ep => ep.EpisodeNumber);
            foreach (var ep in episodesForSeason) _viewModel.CurrentEpisodes.Add(ep);
        }
    }

    private void CloseEpisodesOverlay_Click(object sender, RoutedEventArgs e)
    {
        EpisodesOverlay.Visibility = Visibility.Collapsed;
        
        if (CollectionContentList.Items.Count > 0)
        {
            var rowElement = CollectionContentList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
            rowElement?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
        else
        {
            CloseModalBtn.Focus();
        }
    }

   private void SeasonItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Right)
        {
            if (_viewModel.CurrentEpisodes.Count > 0)
            {
                EpisodesList.UpdateLayout();
                var firstEp = EpisodesList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                if (firstEp != null) firstEp.Focus();
                else EpisodesList.Focus();
            }
            e.Handled = true;
        }
        else if (command == HtpcCommand.Back)
        {
            CloseEpisodesOverlay_Click(null!, null!);
            e.Handled = true;
        }
        else if (command == HtpcCommand.Left) 
        {
            BingeShowBtn.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up)
        {
            if (SeasonsList.SelectedIndex <= 0)
            {
                CloseEpisodesBtn.Focus();
            }
            else
            {
                (sender as ListBoxItem)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
            }
            e.Handled = true;
        }
        else if (command == HtpcCommand.Down)
        {
            (sender as ListBoxItem)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
            e.Handled = true;
        }
    }

    private void EpisodesOverlayButtons_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Right)
        {
            if (SeasonsList.Items.Count > 0)
            {
                SeasonsList.UpdateLayout();
                int targetIndex = Math.Max(0, SeasonsList.SelectedIndex);
                var seasonItem = SeasonsList.ItemContainerGenerator.ContainerFromIndex(targetIndex) as UIElement;
                seasonItem?.Focus();
            }
            e.Handled = true;
        }
        else if (command == HtpcCommand.Back || command == HtpcCommand.Left)
        {
            CloseEpisodesOverlay_Click(null!, null!);
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up && sender == BingeShowBtn)
        {
            CloseEpisodesBtn.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Down && sender == CloseEpisodesBtn)
        {
            BingeShowBtn.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Down && sender == BingeShowBtn)
        {
            e.Handled = true; 
        }
        else if (command == HtpcCommand.Up && sender == CloseEpisodesBtn)
        {
            e.Handled = true; 
        }
    }

    private void EpisodeItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem episode)
        {
            PlaySelectedMedia(episode);
            e.Handled = true;
        }
        else if (command == HtpcCommand.Left)
        {
            SeasonsList.UpdateLayout(); 
            if (SeasonsList.SelectedItem != null)
            {
                var seasonItem = SeasonsList.ItemContainerGenerator.ContainerFromItem(SeasonsList.SelectedItem) as UIElement;
                seasonItem?.Focus();
            }
            else SeasonsList.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up || command == HtpcCommand.Down)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as ListBoxItem)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
        else if (command == HtpcCommand.Right) e.Handled = true; 
    }

    private void EpisodeCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem episode)
        {
            PlaySelectedMedia(episode);
        }
    }
    
    // --- MOUSE WHEEL SCROLLING HANDLERS ---

    private void MainListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };
        MainScroll.RaiseEvent(eventArg);
    }

    private void ModalListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };
        
        // Ensure ModalScroll isn't null before raising the event
        ModalScroll?.RaiseEvent(eventArg);
    }
    
    private void CollectionsView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (command == HtpcCommand.Back || e.Key == Key.Escape)
        {
            if (EpisodesOverlay.Visibility == Visibility.Visible)
            {
                CloseEpisodesOverlay_Click(null!, null!);
                e.Handled = true;
            }
            else if (ContentModal.Visibility == Visibility.Visible)
            {
                CloseModal_Click(null!, null!);
                e.Handled = true;
            }
        }
    }
    
    private MediaItem? _rightClickedItem;

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        // Capture the exact item that was right-clicked regardless of which list it is in
        if (sender is ContextMenu menu && menu.PlacementTarget is FrameworkElement target && target.DataContext is MediaItem item)
        {
            _rightClickedItem = item;
            
            if (CollectionContentList.Items.Contains(item))
                CollectionContentList.SelectedItem = item;
            else if (EpisodesList.Items.Contains(item))
                EpisodesList.SelectedItem = item;
        }
    }

    private async void AdminCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is string command && _rightClickedItem != null)
        {
            if (command == "delete")
            {
                var result = MessageBox.Show($"Are you sure you want to permanently delete '{_rightClickedItem.Title}' from the server?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    bool success = await _viewModel.DeleteMediaAsync(_rightClickedItem.Id);
                    if (success)
                    {
                        // Clean up UI lists immediately without requiring a refresh
                        ModalMediaItems.Remove(_rightClickedItem);
                        _activeCollectionContents.Remove(_rightClickedItem);
                        _masterCollectionContents.Remove(_rightClickedItem);
                        _viewModel.CurrentEpisodes.Remove(_rightClickedItem);
                        _allEpisodesForSelectedShow.Remove(_rightClickedItem);
                        
                        ShowToast("Item deleted successfully.");
                    }
                    else
                    {
                        ShowToast("Failed to delete item.");
                    }
                }
            }
            else
            {
                bool success = await _viewModel.SendAdminCommandAsync(_rightClickedItem.Id, command);
                if (success) ShowToast("Command sent successfully.");
                else ShowToast("Command failed.");
            }
        }
    }

    private void MediaInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedItem != null)
        {
            MediaInfoTitle.Text = _rightClickedItem.Title;
            MediaInfoDetails.Children.Clear();
            MediaInfoDetails.Children.Add(new TextBlock { Text = $"File ID: {_rightClickedItem.Id}", Foreground = System.Windows.Media.Brushes.White, FontSize = 16, Margin = new Thickness(0, 0, 0, 5) });
            MediaInfoDetails.Children.Add(new TextBlock { Text = $"Path: {_rightClickedItem.Path}", Foreground = System.Windows.Media.Brushes.White, FontSize = 16, TextWrapping = TextWrapping.Wrap });

            ContentModal.Visibility = Visibility.Collapsed;
            EpisodesOverlay.Visibility = Visibility.Collapsed;
            MediaInfoModal.Visibility = Visibility.Visible;
            
            _lastFocusedElement = Keyboard.FocusedElement;
        }
    }

    private void CloseMediaInfo_Click(object sender, RoutedEventArgs e)
    {
        MediaInfoModal.Visibility = Visibility.Collapsed;
        
        // Restore whichever modal was previously open
        if (_rightClickedItem != null && ModalMediaItems.Contains(_rightClickedItem))
            ContentModal.Visibility = Visibility.Visible;
        else
            EpisodesOverlay.Visibility = Visibility.Visible;
            
        if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
            Keyboard.Focus(_lastFocusedElement);
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastNotification.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, ev) =>
        {
            ToastNotification.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }

    private void CollectionSortBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.Sort;
        FilterOverlayTitle.Text = "Sort By";
        FilterSelectionList.ItemsSource = new[] 
        { 
            "Alphabetical", "Date Added", "Release Year", "Date Watched", "Date Updated", "Duration", "Rating", "Favorites" 
        };
        FilterSelectionList.SelectedItem = _currentSort;
        OpenFilterOverlay();
    }

    private void CollectionOrderBtn_Click(object sender, RoutedEventArgs e)
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

    private void ProcessFilterSelection(object selectedItem)
{
    if (selectedItem is string selection)
    {
        if (_currentFilterMode == FilterMode.Sort)
        {
            _currentSort = selection;
            CollectionSortBtn.Content = $"{selection} ▼";
            try { PreferencesManager.SaveCollectionSort(_currentSort); } catch { }
        }
        else if (_currentFilterMode == FilterMode.Order)
        {
            _currentOrder = selection;
            CollectionOrderBtn.Content = $"{selection} ▼";
            try { PreferencesManager.SaveCollectionOrder(_currentOrder); } catch { }
        }

        CloseFilterOverlay();
        ApplyCollectionSorting();
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
}
