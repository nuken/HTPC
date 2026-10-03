using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HTPC.Core.Data;
using HTPC.Core.Input;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class VodView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMultiviewRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnCollectionsRequested;
    public event EventHandler? OnRecordingsRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnSportsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DispatcherTimer _typingTimer;

    public ObservableCollection<VODCatalogItem> VodLibrary { get; set; } = new();

    private int _currentOffset = 0;
    private const int _chunkSize = 50;
    private bool _isLoading = false;
    private bool _hasReachedEnd = false;
    private bool _isInitialized = false;

    private enum FilterMode { None, Sort, Order }
    private FilterMode _currentFilterMode = FilterMode.None;
    private IInputElement? _lastFocusedElement;
    private VODCatalogItem? _selectedItem;

    private string _currentSearch = "";
    private string _currentGenre = "All";
    private string _currentSort = "Release Year";
    private string _currentOrder = "Reverse";

    public VodView(IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();
        _scopeFactory = scopeFactory;
        DataContext = this;

        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _typingTimer.Tick += TypingTimer_Tick;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeToggleBtn.Content = PreferencesManager.LoadTheme() == "Dark" ? "\xE708" : "\xE706";

        // FIX: If the background sync was still running last time we checked, try again!
        if (_isInitialized && VodLibrary.Count > 0)
        {
            SearchBox.Focus();
            return;
        }

        _isInitialized = true;
        await ResetAndLoadAsync();
        
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            SearchBox.Focus();
        }), DispatcherPriority.Input);
    }

    // ==========================================
    // DATA LOADING & CHUNKING (LAZY LOAD)
    // ==========================================
    private async Task ResetAndLoadAsync()
    {
        if (!_isInitialized) return;
        _currentOffset = 0;
        _hasReachedEnd = false;
        VodLibrary.Clear();
        MainScroll.ScrollToTop();

        await LoadNextChunkAsync();
    }

    private async Task LoadNextChunkAsync()
    {
        if (_isLoading || _hasReachedEnd) return;

        _isLoading = true;
        LoadingText.Visibility = Visibility.Visible;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.VodCatalog.Where(v => v.IsActive).AsQueryable();

        // 1. Text Search Filter
        if (!string.IsNullOrWhiteSpace(_currentSearch))
        {
            string lowerSearch = _currentSearch.ToLower();
            query = query.Where(v => v.Title.ToLower().Contains(lowerSearch) || v.Genre.ToLower().Contains(lowerSearch));
        }

        // 2. Genre Filter
        if (!string.IsNullOrWhiteSpace(_currentGenre) && _currentGenre != "All")
        {
            query = query.Where(v => v.Genre.ToLower().Contains(_currentGenre.ToLower()));
        }

        // 3. Sorting & Ordering
        bool isReverse = _currentOrder == "Reverse";
        query = _currentSort switch
        {
            "Alphabetical" => isReverse ? query.OrderByDescending(v => v.Title) : query.OrderBy(v => v.Title),
            "Release Year" => isReverse ? query.OrderByDescending(v => v.Year) : query.OrderBy(v => v.Year),
            _ or "Date Added" => isReverse ? query.OrderByDescending(v => v.Id) : query.OrderBy(v => v.Id)
        };

        var batch = await query.Skip(_currentOffset).Take(_chunkSize).ToListAsync();

        if (batch.Count == 0)
        {
            _hasReachedEnd = true;
        }
        else
        {
            foreach (var item in batch)
            {
                VodLibrary.Add(item);
            }
            _currentOffset += batch.Count;
        }

        LoadingText.Visibility = Visibility.Collapsed;
        _isLoading = false;
    }

    private async void MainScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (MainScroll.VerticalOffset >= MainScroll.ScrollableHeight - 150)
        {
            await LoadNextChunkAsync();
        }
    }

    // ==========================================
    // TOP NAVIGATION & THEME
    // ==========================================
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Collections_Click(object sender, RoutedEventArgs e) => OnCollectionsRequested?.Invoke(this, EventArgs.Empty);
    private void Recordings_Click(object sender, RoutedEventArgs e) => OnRecordingsRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Sports_Click(object sender, RoutedEventArgs e) => OnSportsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void ThemeToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        string currentTheme = PreferencesManager.LoadTheme();
        string newTheme = currentTheme == "Dark" ? "Light" : "Dark";
        PreferencesManager.SaveTheme(newTheme);
        ((App)Application.Current).ApplyTheme(newTheme);
        ThemeToggleBtn.Content = newTheme == "Dark" ? "\xE708" : "\xE706";
    }

    // ==========================================
    // SEARCH & FILTER LOGIC
    // ==========================================
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

    private void SortFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.Sort;
        FilterOverlayTitle.Text = "Sort By";
        FilterSelectionList.ItemsSource = new[] { "Release Year", "Alphabetical", "Date Added" };
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
            if (_currentFilterMode == FilterMode.Sort)
            {
                _currentSort = selection;
                SortFilterBtn.Content = $"{selection} ▼";
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

    // ==========================================
    // FOCUS BRIDGES & 10-FOOT KEYBOARD/REMOTE
    // ==========================================
    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (command == HtpcCommand.Back || e.Key == Key.Escape || e.Key == Key.BrowserBack || e.Key == Key.Back)
        {
            if (FilterOverlay.Visibility == Visibility.Visible)
            {
                CloseFilterOverlay();
                e.Handled = true;
            }
            else if (VodDetailsOverlay.Visibility == Visibility.Visible)
            {
                CloseDetails_Click(null!, null!);
                e.Handled = true;
            }
        }
    }

    private void TopNavPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Down)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        var tb = sender as TextBox;

        if (command == HtpcCommand.Right)
        {
            if (tb != null && tb.CaretIndex >= tb.Text.Length)
            {
                SortFilterBtn.Focus();
                e.Handled = true;
            }
        }
        else if (command == HtpcCommand.Down)
        {
            if (GenrePanel.Children.Count > 0)
            {
                (GenrePanel.Children[0] as UIElement)?.Focus();
            }
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up)
        {
            FocusTopNav();
            e.Handled = true;
        }
    }

    private void FilterBtn_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (command == HtpcCommand.Down)
        {
            if (GenrePanel.Children.Count > 0)
            {
                (GenrePanel.Children[0] as UIElement)?.Focus();
            }
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up)
        {
            FocusTopNav();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Left)
        {
            if (sender == OrderFilterBtn) { SortFilterBtn.Focus(); e.Handled = true; }
            else if (sender == SortFilterBtn) { SearchBox.Focus(); e.Handled = true; }
        }
        else if (command == HtpcCommand.Right)
        {
            if (sender == SortFilterBtn) { OrderFilterBtn.Focus(); e.Handled = true; }
            else if (sender == OrderFilterBtn) { e.Handled = true; }
        }
    }

    private void GenrePill_PreviewKeyDown(object sender, KeyEventArgs e)
{
    var command = InputMapper.GetCommand(e.Key);

    // 1. Allow the OK / Select button on the remote to check the pill
    if (command == HtpcCommand.Select || e.Key == Key.Enter)
    {
        if (sender is RadioButton rb)
        {
            rb.IsChecked = true;
            e.Handled = true;
            return;
        }
    }

    // 2. Vertical navigation between SearchBox and the Poster Grid
    if (command == HtpcCommand.Down)
    {
        if (VodGrid.Items.Count > 0)
        {
            var rowElement = VodGrid.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
            rowElement?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
        e.Handled = true;
    }
    else if (command == HtpcCommand.Up)
    {
        SearchBox.Focus();
        e.Handled = true;
    }
}

    private void ListBoxItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (!(sender is ListBoxItem item) || !(item.DataContext is VODCatalogItem vodItem)) return;

        if (command == HtpcCommand.Select)
        {
            OpenMovieDetails(vodItem);
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up)
        {
            int index = VodGrid.ItemContainerGenerator.IndexFromContainer(item);
            if (index >= 0 && index < 6)
            {
                if (GenrePanel.Children.Count > 0)
                {
                    (GenrePanel.Children[0] as UIElement)?.Focus();
                }
                else
                {
                    SearchBox.Focus();
                }
                e.Handled = true;
            }
        }
    }

    private void MovieCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is VODCatalogItem vodItem)
        {
            OpenMovieDetails(vodItem);
            e.Handled = true;
        }
    }

    private void FocusTopNav()
    {
        foreach (UIElement child in TopNavPanel.Children)
        {
            if (child is Button btn && btn.Focusable && btn.Visibility == Visibility.Visible)
            {
                btn.Focus();
                return;
            }
        }
    }

    // ==========================================
    // MOVIE DETAILS OVERLAY & PLAYBACK ROUTING
    // ==========================================
    private void OpenMovieDetails(VODCatalogItem movie)
    {
        _selectedItem = movie;
        _lastFocusedElement = Keyboard.FocusedElement;

        DetailTitle.Text = movie.Title;
        DetailYear.Text = movie.Year.ToString();
        DetailGenre.Text = string.IsNullOrWhiteSpace(movie.Genre) ? "VOD Feature" : movie.Genre;
        DetailSummary.Text = string.IsNullOrWhiteSpace(movie.Overview) ? "No summary available." : movie.Overview;
        DetailProvider.Text = movie.ProviderKey.ToUpper();

        try
        {
            if (!string.IsNullOrWhiteSpace(movie.PosterUrl))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(movie.PosterUrl, UriKind.RelativeOrAbsolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 300;
                bmp.EndInit();
                DetailPoster.Source = bmp;
            }
            else
            {
                DetailPoster.Source = null;
            }
        }
        catch
        {
            DetailPoster.Source = null;
        }

        VodDetailsOverlay.Visibility = Visibility.Visible;

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            DetailPlayBtn.Focus();
        }), DispatcherPriority.Input);
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e)
    {
        VodDetailsOverlay.Visibility = Visibility.Collapsed;

        if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
        {
            Keyboard.Focus(uiElement);
        }
    }

    private void DetailButtons_PreviewKeyDown(object sender, KeyEventArgs e)
{
    var command = InputMapper.GetCommand(e.Key);

    // 1. Trap Left and Right arrows completely so they do nothing
    if (command == HtpcCommand.Left || command == HtpcCommand.Right)
    {
        e.Handled = true;
        return;
    }

    // 2. Cycle vertically between the two buttons
    if (command == HtpcCommand.Up)
    {
        if (sender == DetailPlayBtn)
        {
            DetailBackBtn.Focus();
        }
        else if (sender == DetailBackBtn)
        {
            DetailPlayBtn.Focus(); // Wrap around
        }
        e.Handled = true;
    }
    else if (command == HtpcCommand.Down)
    {
        if (sender == DetailBackBtn)
        {
            DetailPlayBtn.Focus();
        }
        else if (sender == DetailPlayBtn)
        {
            DetailBackBtn.Focus(); // Wrap around
        }
        e.Handled = true;
    }
    // 3. Back / Escape button closes the overlay
    else if (command == HtpcCommand.Back || e.Key == Key.Escape)
    {
        CloseDetails_Click(null!, null!);
        e.Handled = true;
    }
}

    private void DetailPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        VodDetailsOverlay.Visibility = Visibility.Collapsed;

        var prefs = PreferencesManager.Load();

        // 1. Build the Go ADB Tuner route
        string encodedLink = Uri.EscapeDataString(_selectedItem.NativeDeepLink);
        string tunerStreamUrl = $"{prefs.AdbTunerUrl.TrimEnd('/')}/vod?provider={_selectedItem.ProviderKey}&link={encodedLink}";

        // 2. Map into a clean MediaItem for MpvPlaybackService
        var mediaItem = new MediaItem
        {
            Id = $"vod_{_selectedItem.Id}",
            Title = _selectedItem.Title,
            PosterUrl = _selectedItem.PosterUrl,
            StreamUrl = tunerStreamUrl,
            Summary = _selectedItem.Overview,
            ReleaseYear = _selectedItem.Year,
            Genres = !string.IsNullOrEmpty(_selectedItem.Genre) ? new() { _selectedItem.Genre } : new()
        };

        // 3. Hand off directly to MainWindow's playback engine
        OnPlayRequested?.Invoke(this, mediaItem);
    }

    private void VodGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender
        };
        MainScroll.RaiseEvent(eventArg);
    }
}