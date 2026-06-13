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
    public event EventHandler<MediaItem>? OnPlayRequested;
    public event EventHandler? OnVideosRequested;

    private readonly MediaLibraryService _libraryService;
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

    public ShowsView(MediaLibraryService libraryService)
    {
        InitializeComponent();
        _libraryService = libraryService;
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

    // --- MASTER REMOTE BACK HANDLER ---
    private void ShowsView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

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

    // THE FIX: Listen for Enter/OK on the Episode Items
    private void EpisodeItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem episode)
        {
            OnPlayRequested?.Invoke(this, episode);
            e.Handled = true;
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

    // --- UPDATED NAVIGATION SIGNATURES (RoutedEventArgs) ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
}