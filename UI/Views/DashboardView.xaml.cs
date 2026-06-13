using System;
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

public partial class DashboardView : UserControl
{
    public event EventHandler<MediaItem>? OnPlayRequested;
    public event EventHandler? OnExitRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;

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
        
        if (FeaturedMovies.Count > 0) 
        {
            // If already loaded, just return focus to the dropdown for the remote
            CollectionDropdown.Focus();
            return;
        }

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

        // THE FIX: Push the cursor to the Dropdown so the remote D-Pad works instantly
        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            CollectionDropdown.Focus();
        }), DispatcherPriority.Input);
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
    
    // --- UPDATED NAVIGATION SIGNATURES (RoutedEventArgs) ---
    private void Guide_Click(object sender, RoutedEventArgs e)
    {
        OnGuideRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        OnExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        OnSettingsRequested?.Invoke(this, EventArgs.Empty);
    }
    
    private void Movies_Click(object sender, RoutedEventArgs e)
    {
        OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    }
    
    private void Shows_Click(object sender, RoutedEventArgs e)
    {
        OnShowsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Videos_Click(object sender, RoutedEventArgs e)
    {
        OnVideosRequested?.Invoke(this, EventArgs.Empty);
    }

    // --- NATIVE NAVIGATION FIXES ---

    private void ListBoxItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        // 1. Handle OK/Enter to play the movie
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem movie)
        {
            OnPlayRequested?.Invoke(this, movie);
            e.Handled = true; 
        }
        // 2. THE ESCAPE HATCH: Handle Up/Down to jump between rows instantly
        else if (command == HtpcCommand.Up || command == HtpcCommand.Down)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as ListBoxItem)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true; // Tell WPF we handled the movement, don't bounce around!
        }
    }

    private void ListBoxItem_MouseUp(object sender, MouseButtonEventArgs e)
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
}