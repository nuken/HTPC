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
	public event EventHandler? OnMultiviewRequested;

    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    private bool _isUpdatingDropdown = true;

    // ObservableCollection automatically notifies the UI when items are added/removed
    public ObservableCollection<MediaItem> FeaturedMovies { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> LiveChannels { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> RecentEpisodes { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> RecentVideos { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> UpNextQueue { get; set; } = new ObservableCollection<MediaItem>();
	
    public DashboardView(MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _serverManager = serverManager;
        this.DataContext = this;
        Loaded += OnLoaded;
    }

    // 1. THIS IS THE NEW HELPER METHOD
    private void ApplyDashboardLayout()
    {
        var prefs = PreferencesManager.Load();
        
        // Remove all sections from the visual tree
        DashboardContentPanel.Children.Clear();

        // Sort the saved layout by the Order integer
        var sortedLayout = prefs.DashboardLayout.OrderBy(r => r.Order).ToList();

        foreach (var row in sortedLayout)
        {
            if (!row.IsVisible) continue; // Skip it entirely if the user disabled it

            switch (row.Id)
            {
                case "UpNext":
                    // Only add Up Next if there is actually content in the queue
                    if (UpNextQueue.Count > 0) DashboardContentPanel.Children.Add(SectionUpNext);
                    break;
                case "LiveTv":
                    DashboardContentPanel.Children.Add(SectionLiveTv);
                    break;
                case "Movies":
                    DashboardContentPanel.Children.Add(SectionMovies);
                    break;
                case "Shows":
                    DashboardContentPanel.Children.Add(SectionShows);
                    break;
                case "Videos":
                    DashboardContentPanel.Children.Add(SectionVideos);
                    break;
            }
        }
    }

    // 2. THIS IS YOUR UPDATED ONLOADED METHOD (Now 100% Null-Safe & Scope-Safe)
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var activeServer = _serverManager.GetActiveServer();
        
        // Redirect to settings if the database is empty or missing a server IP
        if (activeServer == null || string.IsNullOrWhiteSpace(activeServer.IpAddress))
        {
            OnSettingsRequested?.Invoke(this, EventArgs.Empty);
            return; 
        } 
        
        // --- 1. INSTANT UI LOAD ---
        if (FeaturedMovies.Count > 0) 
        {
            if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed;
            ApplyDashboardLayout();
            
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                HomeNavBtn?.Focus();          
                if (HomeNavBtn != null) Keyboard.Focus(HomeNavBtn);  
            }), DispatcherPriority.ApplicationIdle);
        }
        else
        {
            if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Visible;
        }

        // --- 2. BACKGROUND DATA REFRESH ---
        var collections = await _libraryService.GetCollectionsAsync();
        var allChannels = new ChannelCollection { Id = "", Name = "All Channels" };
        collections.Insert(0, allChannels);
        
        // FIX: Declare savedCollection OUTSIDE the if-block so it can be used later!
        var savedCollection = collections.FirstOrDefault(c => c.Id == activeServer?.DefaultCollectionId);

        // Safely update the dropdown
        if (CollectionDropdown != null)
        {
            CollectionDropdown.ItemsSource = collections;
            CollectionDropdown.SelectedItem = savedCollection ?? allChannels;
        }
        _isUpdatingDropdown = false; 

        await LoadLiveTvData(savedCollection ?? allChannels);
        
        var movies = await _libraryService.GetFeaturedMoviesAsync();
        FeaturedMovies.Clear(); 
        foreach (var movie in movies) FeaturedMovies.Add(movie);
        
        var episodes = await _libraryService.GetRecentEpisodesAsync(15);
        RecentEpisodes.Clear();
        foreach (var ep in episodes) RecentEpisodes.Add(ep);

        var videos = await _libraryService.GetRecentVideosAsync(15);
        RecentVideos.Clear();
        foreach (var vid in videos) RecentVideos.Add(vid);

        var upNextItems = await _libraryService.GetUpNextAsync();
        UpNextQueue.Clear();
        foreach (var item in upNextItems) UpNextQueue.Add(item);
        
        ApplyDashboardLayout();

        // --- 3. FINAL CLEANUP ---
        // Safely check if the overlay exists before touching its properties
        if (LoadingOverlay != null && LoadingOverlay.Visibility == Visibility.Visible)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                HomeNavBtn?.Focus();
                if (HomeNavBtn != null) Keyboard.Focus(HomeNavBtn); 
            }), DispatcherPriority.ApplicationIdle);
        }
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
	private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
	    
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
            var currentItem = sender as ListBoxItem;
            if (currentItem == null) return;

            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            
            // Predict where WPF wants to go natively BEFORE we actually move
            var predicted = currentItem.PredictFocus(direction) as FrameworkElement;

            // FIX 1: THE WRAP-AROUND BUG
            // If WPF's spatial cone misses the top nav, it wraps to the bottom of the page.
            if (command == HtpcCommand.Up)
            {
                bool isWrapAround = false;
                if (predicted != null)
                {
                    try 
                    {
                        Point currentPos = currentItem.PointToScreen(new Point(0, 0));
                        Point predictedPos = predicted.PointToScreen(new Point(0, 0));
                        
                        // If the "Up" target is physically lower on the screen, it wrapped around!
                        if (predictedPos.Y >= currentPos.Y) isWrapAround = true;
                    } 
                    catch { /* Ignore coordinate errors during rapid scrolling */ }
                }

                // If it missed entirely, or it wrapped around, snap directly to the Top Nav!
                if (predicted == null || isWrapAround)
                {
                    HomeNavBtn.Focus();
                    MainScroll.ScrollToTop(); // Instantly bring top menu into view
                    e.Handled = true;
                    return;
                }
            }

            // Execute the physical movement
            currentItem.MoveFocus(new TraversalRequest(direction));
            
            // FIX 2: THE DOUBLE-CLICK BUG
            // We force it to physically scroll into view by mathematically calculating its position.
            var newFocus = Keyboard.FocusedElement as FrameworkElement;
            if (newFocus != null)
            {
                ScrollToElement(MainScroll, newFocus);
            }

            e.Handled = true; // Tell WPF we handled the movement
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

        if (cb != null && !cb.IsDropDownOpen)
        {
            if (command == HtpcCommand.Up)
            {
                var predicted = cb.PredictFocus(FocusNavigationDirection.Up) as FrameworkElement;
                bool isWrapAround = false;
                try 
                {
                    if (predicted != null) 
                    {
                        Point currentPos = cb.PointToScreen(new Point(0, 0));
                        Point predictedPos = predicted.PointToScreen(new Point(0, 0));
                        if (predictedPos.Y >= currentPos.Y) isWrapAround = true;
                    }
                } 
                catch {}

                // Prevent the Dropdown from wrapping around to the bottom
                if (predicted == null || isWrapAround)
                {
                    HomeNavBtn.Focus();
                    MainScroll.ScrollToTop();
                    e.Handled = true;
                    return;
                }
            }

            if (command == HtpcCommand.Down || command == HtpcCommand.Up || command == HtpcCommand.Left || command == HtpcCommand.Right)
            {
                var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down :
                                command == HtpcCommand.Up ? FocusNavigationDirection.Up :
                                command == HtpcCommand.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right;

                cb.MoveFocus(new TraversalRequest(direction));
                
                var newFocus = Keyboard.FocusedElement as FrameworkElement;
                if (newFocus != null && (command == HtpcCommand.Up || command == HtpcCommand.Down))
                {
                    ScrollToElement(MainScroll, newFocus);
                }

                e.Handled = true; 
            }
        }
    }

    // --- NEW: Helper to calculate exact offset and force the ScrollViewer to move ---
    private void ScrollToElement(ScrollViewer scrollViewer, FrameworkElement element)
    {
        try
        {
            var content = scrollViewer.Content as UIElement;
            if (content == null) return;

            var transform = element.TransformToAncestor(content);
            Point position = transform.Transform(new Point(0, 0));
            
            // 100px padding so the focused row isn't hugging the absolute top edge of the screen
            double targetY = position.Y - 100; 
            
            if (targetY < 0) targetY = 0;
            if (targetY > scrollViewer.ScrollableHeight) targetY = scrollViewer.ScrollableHeight;

            scrollViewer.ScrollToVerticalOffset(targetY);
        }
        catch { }
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