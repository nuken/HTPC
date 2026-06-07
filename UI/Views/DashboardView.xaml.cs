using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class DashboardView : UserControl
{
    public event EventHandler<MediaItem>? OnPlayRequested;
    public event EventHandler? OnExitRequested;
    public event EventHandler? OnSettingsRequested;

    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    private bool _isUpdatingDropdown = true;

    // ObservableCollection automatically notifies the UI when items are added/removed
    public ObservableCollection<MediaItem> FeaturedMovies { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> LiveChannels { get; set; } = new ObservableCollection<MediaItem>();

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
        if (FeaturedMovies.Count > 0) return;

        // 1. Fetch Collections
        var collections = await _libraryService.GetCollectionsAsync();
        
        // Add a default "All Channels" option at the top
        var allChannels = new ChannelCollection { Id = "", Name = "All Channels" };
        collections.Insert(0, allChannels);
        CollectionDropdown.ItemsSource = collections;

        // 2. Select the saved collection from the database
        var activeServer = _serverManager.GetActiveServer();
        var savedCollection = collections.FirstOrDefault(c => c.Id == activeServer?.DefaultCollectionId);
        
        CollectionDropdown.SelectedItem = savedCollection ?? allChannels;
        _isUpdatingDropdown = false; // Allow the selection changed event to fire going forward

        // 3. Fetch Initial Data based on the selected collection
        await LoadLiveTvData(savedCollection ?? allChannels);
        
        var movies = await _libraryService.GetFeaturedMoviesAsync();
        foreach (var movie in movies) FeaturedMovies.Add(movie);
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

    private void ExitApp_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OnExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Settings_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OnSettingsRequested?.Invoke(this, EventArgs.Empty);
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