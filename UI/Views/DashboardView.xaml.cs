using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using HTPC.Core.Models;
using HTPC.Services;
using System.Windows.Input;

namespace HTPC.UI.Views;

public partial class DashboardView : UserControl
{
    public event EventHandler<MediaItem>? OnPlayRequested;
    public event EventHandler? OnExitRequested;
    public event EventHandler? OnSettingsRequested;
	public event EventHandler? OnGuideRequested;
	public event EventHandler? OnMoviesRequested;

    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    private bool _isUpdatingDropdown = true;

    // ObservableCollection automatically notifies the UI when items are added/removed
    public ObservableCollection<MediaItem> FeaturedMovies { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> LiveChannels { get; set; } = new ObservableCollection<MediaItem>();
	public ObservableCollection<MediaItem> RecentEpisodes { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> RecentVideos { get; set; } = new ObservableCollection<MediaItem>();

    public DashboardView(MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _serverManager = serverManager;
        this.DataContext = this;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
		var activeServer = _serverManager.GetActiveServer();
        
        // Redirect to settings if the database is empty or missing a server IP
        if (activeServer == null || string.IsNullOrWhiteSpace(activeServer.IpAddress))
        {
            OnSettingsRequested?.Invoke(this, EventArgs.Empty);
            return; // Stop trying to load the dashboard!
        } 
		
        if (FeaturedMovies.Count > 0) return;

        // 1. Fetch Collections
        var collections = await _libraryService.GetCollectionsAsync();
        
        // Add a default "All Channels" option at the top
        var allChannels = new ChannelCollection { Id = "", Name = "All Channels" };
        collections.Insert(0, allChannels);
        CollectionDropdown.ItemsSource = collections;

        // 2. Select the saved collection from the database
        var savedCollection = collections.FirstOrDefault(c => c.Id == activeServer?.DefaultCollectionId);
        
        CollectionDropdown.SelectedItem = savedCollection ?? allChannels;
        _isUpdatingDropdown = false; // Allow the selection changed event to fire going forward

        // 3. Fetch Initial Data based on the selected collection
        await LoadLiveTvData(savedCollection ?? allChannels);
        
        var movies = await _libraryService.GetFeaturedMoviesAsync();
        foreach (var movie in movies) FeaturedMovies.Add(movie);
		
		// Load Recent Episodes
            var episodes = await _libraryService.GetRecentEpisodesAsync(15);
            RecentEpisodes.Clear();
            foreach (var ep in episodes) RecentEpisodes.Add(ep);

            // Load Recent Videos
            var videos = await _libraryService.GetRecentVideosAsync(15);
            RecentVideos.Clear();
            foreach (var vid in videos) RecentVideos.Add(vid);
    }

    private async void CollectionDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingDropdown) return;
        
        if (CollectionDropdown.SelectedItem is ChannelCollection selectedCollection)
        {
            // Save to database instantly
            _serverManager.SetDefaultCollection(selectedCollection.Id);
            await LoadLiveTvData(selectedCollection);
        }
    }

    private async Task LoadLiveTvData(ChannelCollection collection)
    {
        LiveChannels.Clear();
        var channels = await _libraryService.GetLiveChannelsAsync(collection);
        foreach (var channel in channels)
        {
            LiveChannels.Add(channel);
        }
    }
	
	private void Guide_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OnGuideRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitApp_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OnExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Settings_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OnSettingsRequested?.Invoke(this, EventArgs.Empty);
    }
	
	private void Movies_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    }

    // --- NATIVE NAVIGATION FIXES ---

    private void ListBoxItem_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && sender is ListBoxItem item && item.DataContext is MediaItem movie)
        {
            OnPlayRequested?.Invoke(this, movie);
            e.Handled = true; // Prevent the sound/double-fire
        }
    }

    private void ListBoxItem_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem movie)
        {
            OnPlayRequested?.Invoke(this, movie);
        }
    }

    // --- MOUSE SCROLLING LOGIC ---

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ListBox listBox)
        {
            var viewer = GetScrollViewer(listBox);
            if (viewer != null)
            {
                // Jump left by roughly 3 posters
                viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - 600);
            }
        }
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ListBox listBox)
        {
            var viewer = GetScrollViewer(listBox);
            if (viewer != null)
            {
                // Jump right by roughly 3 posters
                viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset + 600);
            }
        }
    }
	
	private void HorizontalList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // If the user holds Shift, scroll the horizontal row
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            if (sender is ListBox listBox)
            {
                var viewer = GetScrollViewer(listBox);
                if (viewer != null)
                {
                    viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
                    e.Handled = true;
                }
            }
        }
        else
        {
            // Otherwise, pass the scroll event UP to the Main ScrollViewer so the whole page moves naturally!
            e.Handled = true;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            MainScroll.RaiseEvent(eventArg);
        }
    }

    // Standard WPF VisualTree trick to find the hidden ScrollViewer inside a ListBox
    private ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer viewer) return viewer;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}