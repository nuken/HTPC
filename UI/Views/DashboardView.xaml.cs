using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Input; 
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
    public event EventHandler? OnRecordingsRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnMultiviewRequested;
    public event EventHandler? OnCollectionsRequested;

    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    private readonly UpdateService _updateService;
    
    private string _latestReleaseUrl = string.Empty;
    private string _latestReleaseVersion = string.Empty;

    // --- Overlay Filter Variables ---
    private ChannelCollection? _activeCollection;
    private System.Collections.Generic.List<ChannelCollection> _availableCollections = new();
    private IInputElement? _lastFocusedElement;

    public ObservableCollection<MediaItem> FeaturedMovies { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> LiveChannels { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> RecentEpisodes { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> RecentVideos { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> UpNextQueue { get; set; } = new ObservableCollection<MediaItem>();
    
    public DashboardView(MediaLibraryService libraryService, ServerManagerService serverManager, UpdateService updateService)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _serverManager = serverManager;
        _updateService = updateService; 
        this.DataContext = this;
        Loaded += OnLoaded;
    }

    private void ApplyDashboardLayout()
    {
        var prefs = PreferencesManager.Load();
        
        DashboardContentPanel.Children.Clear();
        var sortedLayout = prefs.DashboardLayout.OrderBy(r => r.Order).ToList();

        foreach (var row in sortedLayout)
        {
            if (!row.IsVisible) continue; 

            switch (row.Id)
            {
                case "UpNext":
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var activeServer = _serverManager.GetActiveServer();
        
        if (activeServer == null || string.IsNullOrWhiteSpace(activeServer.IpAddress))
        {
            OnSettingsRequested?.Invoke(this, EventArgs.Empty);
            return; 
        } 
        
        _ = CheckForUpdatesAsync();
        
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

        var collections = await _libraryService.GetCollectionsAsync();
        var allChannels = new ChannelCollection { Id = "", Name = "All Channels" };
        collections.Insert(0, allChannels);
        
        var savedCollection = collections.FirstOrDefault(c => c.Id == activeServer?.DefaultCollectionId);

        // Populate new Overlay Button logic
        _availableCollections = collections;
        _activeCollection = savedCollection ?? allChannels;
        if (CollectionFilterBtn != null) CollectionFilterBtn.Content = $"{_activeCollection.Name} ▼";

        await LoadLiveTvData(_activeCollection);
        
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

    private async Task LoadLiveTvData(ChannelCollection collection)
    {
        LiveChannels.Clear();
        var channels = await _libraryService.GetLiveChannelsAsync(collection);
        foreach (var channel in channels)
        {
            LiveChannels.Add(channel);
        }
    }

    // --- NEW TV-OVERLAY FILTER LOGIC ---

    private void CollectionFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        FilterSelectionList.ItemsSource = _availableCollections;
        FilterSelectionList.SelectedItem = _activeCollection;
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
        if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
        {
            Keyboard.Focus(uiElement);
        }
    }

    private async void ProcessFilterSelection(object selectedItem)
    {
        if (selectedItem is ChannelCollection collection)
        {
            _activeCollection = collection;
            CollectionFilterBtn.Content = $"{collection.Name} ▼";
            CloseFilterOverlay();
            
            _serverManager.SetDefaultCollection(collection.Id);
            await LoadLiveTvData(collection);
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

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (FilterOverlay.Visibility == Visibility.Visible && (command == HtpcCommand.Back || e.Key == Key.Escape))
        {
            CloseFilterOverlay();
            e.Handled = true;
        }
    }

    // --- NAVIGATION LOGIC ---
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void ExitApp_Click(object sender, RoutedEventArgs e) => OnExitRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Recordings_Click(object sender, RoutedEventArgs e) => OnRecordingsRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Collections_Click(object sender, RoutedEventArgs e) => OnCollectionsRequested?.Invoke(this, EventArgs.Empty);

    // --- NATIVE NAVIGATION FIXES ---
    private void ListBoxItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem movie)
        {
            OnPlayRequested?.Invoke(this, movie);
            e.Handled = true; 
        }
        else if (command == HtpcCommand.Up || command == HtpcCommand.Down)
        {
            var currentItem = sender as ListBoxItem;
            if (currentItem == null) return;

            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            var predicted = currentItem.PredictFocus(direction) as FrameworkElement;

            if (command == HtpcCommand.Up)
            {
                bool isWrapAround = false;
                if (predicted != null)
                {
                    try 
                    {
                        Point currentPos = currentItem.PointToScreen(new Point(0, 0));
                        Point predictedPos = predicted.PointToScreen(new Point(0, 0));
                        
                        if (predictedPos.Y >= currentPos.Y) isWrapAround = true;
                    } 
                    catch { }
                }

                if (predicted == null || isWrapAround)
                {
                    HomeNavBtn.Focus();
                    MainScroll.ScrollToTop(); 
                    e.Handled = true;
                    return;
                }
            }

            currentItem.MoveFocus(new TraversalRequest(direction));
            
            var newFocus = Keyboard.FocusedElement as FrameworkElement;
            if (newFocus != null)
            {
                ScrollToElement(MainScroll, newFocus);
            }

            e.Handled = true; 
        }
    }

    private Point? _touchDownPosition;

    private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _touchDownPosition = e.GetPosition(this);
    }

    private void ListBoxItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_touchDownPosition == null) return;

        var currentPosition = e.GetPosition(this);
        double distanceX = Math.Abs(currentPosition.X - _touchDownPosition.Value.X);
        double distanceY = Math.Abs(currentPosition.Y - _touchDownPosition.Value.Y);

        _touchDownPosition = null; 

        if (distanceX > 15 || distanceY > 15) return;

        if (sender is ListBoxItem item && item.DataContext is MediaItem movie)
        {
            OnPlayRequested?.Invoke(this, movie);
            e.Handled = true; 
        }
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ListBox listBox)
        {
            var viewer = GetScrollViewer(listBox);
            if (viewer != null) viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - 600);
        }
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ListBox listBox)
        {
            var viewer = GetScrollViewer(listBox);
            if (viewer != null) viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset + 600);
        }
    }
    
    private void HorizontalList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
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
            e.Handled = true;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            MainScroll.RaiseEvent(eventArg);
        }
    }
    
    private async Task CheckForUpdatesAsync()
    {
        var update = await _updateService.CheckForUpdatesAsync();
        
        if (update.UpdateAvailable)
        {
            var prefs = PreferencesManager.Load();
            
            if (prefs.LastIgnoredVersion != update.LatestVersion || DateTime.Now > prefs.IgnoreUntilDate)
            {
                _latestReleaseUrl = update.ReleaseUrl;
                _latestReleaseVersion = update.LatestVersion;
                
                Dispatcher.Invoke(() => 
                {
                    UpdateMessageText.Text = $"Nucleus HTPC {update.LatestVersion} is available!";
                    UpdateBanner.Visibility = Visibility.Visible;
                });
            }
        }
    }

    private async void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnDownloadUpdate.Content = "Downloading...";
        BtnDownloadUpdate.IsEnabled = false;

        var installerPath = await _updateService.DownloadInstallerAsync(_latestReleaseVersion);

        if (installerPath != null && System.IO.File.Exists(installerPath))
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true };
            System.Diagnostics.Process.Start(startInfo);

            await Task.Delay(500); 
            Application.Current.Shutdown();
        }
        else
        {
            BtnDownloadUpdate.Content = "Retry / Open Browser";
            BtnDownloadUpdate.IsEnabled = true;
            
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo 
            { 
                FileName = _latestReleaseUrl, 
                UseShellExecute = true 
            });
        }
    }

    private void BtnIgnoreUpdate_Click(object sender, RoutedEventArgs e)
    {
        var prefs = PreferencesManager.Load();
        prefs.LastIgnoredVersion = _latestReleaseVersion;
        prefs.IgnoreUntilDate = DateTime.Now.AddDays(7);
        PreferencesManager.Save(prefs);
        
        UpdateBanner.Visibility = Visibility.Collapsed;
        HomeNavBtn?.Focus();
    }

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
    
    private void ScrollToElement(ScrollViewer scrollViewer, FrameworkElement element)
    {
        try
        {
            var content = scrollViewer.Content as UIElement;
            if (content == null) return;

            var transform = element.TransformToAncestor(content);
            Point position = transform.Transform(new Point(0, 0));
            
            double targetY = position.Y - 100; 
            
            if (targetY < 0) targetY = 0;
            if (targetY > scrollViewer.ScrollableHeight) targetY = scrollViewer.ScrollableHeight;

            scrollViewer.ScrollToVerticalOffset(targetY);
        }
        catch { }
    }

    private void TopNavPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Down)
        {
            e.Handled = true;
            
            if (UpNextList.IsVisible)
            {
                UpNextList.Focus();
            }
            else if (LiveTvList.IsVisible)
            {
                CollectionFilterBtn.Focus();
            }
            else
            {
                FocusFirstAvailableContentRow(); 
            }
        }
    }
    
    private bool TryFocusFirstListBoxItem(ListBox listBox)
    {
        if (listBox.Visibility != Visibility.Visible || listBox.Items.Count == 0) return false;
        
        var element = listBox.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
        if (element != null) return element.Focus();
        
        listBox.UpdateLayout();
        element = listBox.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
        return element?.Focus() ?? false;
    }
    
    private void FocusFirstAvailableContentRow()
    {
        if (TryFocusFirstListBoxItem(UpNextList)) return;
        if (TryFocusFirstListBoxItem(LiveTvList)) return;
        if (TryFocusFirstListBoxItem(MoviesList)) return;
        if (TryFocusFirstListBoxItem(EpisodesList)) return;
        TryFocusFirstListBoxItem(VideosList);
    }
}