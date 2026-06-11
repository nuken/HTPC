using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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

    public MoviesView(MediaLibraryService libraryService)
    {
        InitializeComponent();
        _libraryService = libraryService;
        this.DataContext = this;

        // Setup the Debounce Timer for smooth typing
        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _typingTimer.Tick += TypingTimer_Tick;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;

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

        var newMovies = await _libraryService.GetFilteredMoviesAsync(_currentOffset, _chunkSize, _currentSearch, _currentGenre, _currentSort);
        
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
        // Reset the timer on every keystroke
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
            PreferencesManager.SaveMovieSort(_currentSort); // Save preference!
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

    private void MovieCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem movie)
            OnPlayRequested?.Invoke(this, movie);
    }

    private void Home_Click(object sender, MouseButtonEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, MouseButtonEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, MouseButtonEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
	private void Videos_Click(object sender, MouseButtonEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
	private void Settings_Click(object sender, MouseButtonEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
}