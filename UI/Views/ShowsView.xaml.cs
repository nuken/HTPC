using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;
        await ResetAndLoadAsync();
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

    // OVERLAY LOGIC: User clicks a Show Poster
    private async void ShowCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem show)
        {
            // Populate Column 0 details
            OverlayShowTitle.Text = show.Title;
            OverlayShowSummary.Text = string.IsNullOrEmpty(show.Summary) ? "No summary available." : show.Summary;
            
            // Try to set the poster URL (might require a string-to-ImageSource converter depending on setup, but WPF usually handles absolute URLs)
            try { OverlayShowPoster.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(show.PosterUrl)); } catch { }

            // Fetch every episode for this show
            _allEpisodesForSelectedShow = await _libraryService.GetEpisodesForShowAsync(show.Title);

            // Populate Column 1 (Unique Seasons)
            Seasons.Clear();
            var uniqueSeasons = _allEpisodesForSelectedShow.Select(ep => ep.SeasonNumber).Distinct().OrderBy(s => s).ToList();
            foreach (var s in uniqueSeasons) Seasons.Add(s);

            // Open the overlay and auto-select the first season
            EpisodesOverlay.Visibility = Visibility.Visible;
            if (Seasons.Count > 0) SeasonsList.SelectedIndex = 0;
        }
    }

    // OVERLAY LOGIC: User selects a Season Number
    private void SeasonsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeasonsList.SelectedItem is int selectedSeason)
        {
            CurrentEpisodes.Clear();
            var episodesForSeason = _allEpisodesForSelectedShow.Where(ep => ep.SeasonNumber == selectedSeason).OrderBy(ep => ep.EpisodeNumber);
            foreach (var ep in episodesForSeason) CurrentEpisodes.Add(ep);
        }
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        EpisodesOverlay.Visibility = Visibility.Collapsed;
    }

    private void EpisodeCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem episode)
        {
            OnPlayRequested?.Invoke(this, episode);
        }
    }

    private void Home_Click(object sender, MouseButtonEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, MouseButtonEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, MouseButtonEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, MouseButtonEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
}