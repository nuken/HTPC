using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using HTPC.Core.Input; 
using HTPC.Core.Models;
using HTPC.Services;
using System.Threading.Tasks;

namespace HTPC.UI.Views;

public partial class GuideView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnRecordingsRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler<MediaItem>? OnPlayRequested; 
    public event EventHandler? OnMultiviewRequested;
    public event EventHandler? OnCollectionsRequested;

    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    public ObservableCollection<Channel> DisplayedChannels { get; set; } = new ObservableCollection<Channel>();
    private List<Channel> _allChannels = new List<Channel>();
    private List<ChannelCollection> _collections = new List<ChannelCollection>();

    private Airing? _selectedAiring;
    private Button? _lastFocusedAiringButton;
    private DateTime _lastTimeFocus = DateTime.MinValue;
    private readonly DispatcherTimer _autoRefreshTimer;
    
    // --- Overlay Filter Variables ---
    private string _activeCollectionName = "All Channels";
    private string _currentCollectionId = "All";
    private List<string> _availableCollections = new();
    private IInputElement? _lastFocusedElement;

    public GuideView(MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _serverManager = serverManager;
        
        ChannelItemsControl.ItemsSource = DisplayedChannels;
        GuideItemsControl.ItemsSource = DisplayedChannels;
        
        this.Loaded += OnLoaded;
        this.PreviewKeyDown += GuideView_PreviewKeyDown; 
        this.IsVisibleChanged += GuideView_IsVisibleChanged;

        DateTime now = DateTime.Now;
        int minutesUntilNextHalfHour = 30 - (now.Minute % 30);
        int secondsUntilNextHalfHour = (minutesUntilNextHalfHour * 60) - now.Second;

        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(secondsUntilNextHalfHour) };
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        _autoRefreshTimer.Start();
    }
    
    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (sender is DispatcherTimer timer && timer.Interval.TotalMinutes != 30)
        {
            timer.Interval = TimeSpan.FromMinutes(30);
        }

        string selection = _activeCollectionName;

        Airing? focusedAiring = (Keyboard.FocusedElement as Button)?.Tag as Airing;

        ChannelCollection? targetCollection = null;
        if (selection != "All Channels" && selection != "Favorites" && selection != "HD Channels" && selection != "SD Channels")
        {
            targetCollection = _collections.FirstOrDefault(c => c.Name == selection);
        }

        var channels = await _libraryService.GetGuideChannelsAsync(targetCollection, 4);

        if (selection == "Favorites") channels = channels.Where(c => c.Favorite).ToList();
        else if (selection == "HD Channels") channels = channels.Where(c => c.IsHD).ToList();
        else if (selection == "SD Channels") channels = channels.Where(c => !c.IsHD).ToList();

        RenderGuideData(channels, selection);

        if (focusedAiring != null)
        {
            var channel = DisplayedChannels.FirstOrDefault(c => c.Number == focusedAiring.ChannelNumber);
            if (channel != null)
            {
                var newAiring = channel.CurrentAirings?.FirstOrDefault(a => a.StartTime == focusedAiring.StartTime);
                if (newAiring != null) FocusAiringSafely(channel, newAiring);
            }
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeToggleBtn.Content = PreferencesManager.LoadTheme() == "Dark" ? "\xE708" : "\xE706";
        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            if (_lastFocusedAiringButton == null)
            {
                GuideNavBtn.Focus();
                Keyboard.Focus(GuideNavBtn);
            }
        }), DispatcherPriority.ApplicationIdle);

        if (DisplayedChannels.Count > 0) return; 
        
        if (_availableCollections.Count == 0)
        {
            _collections = await _libraryService.GetCollectionsAsync();
            
            _availableCollections.Add("All Channels");
            _availableCollections.Add("Favorites");
            _availableCollections.Add("HD Channels");
            _availableCollections.Add("SD Channels");
            
            foreach (var col in _collections)
            {
                _availableCollections.Add(col.Name);
            }
        }

        var prefs = PreferencesManager.Load();
        _activeCollectionName = string.IsNullOrEmpty(prefs.LastGuideCollection) ? "All Channels" : prefs.LastGuideCollection;
        
        if (!_availableCollections.Contains(_activeCollectionName))
            _activeCollectionName = "All Channels";
            
        CollectionFilterBtn.Content = $"{_activeCollectionName} ▼";

        await LoadGuideDataAsync(_activeCollectionName);
    }
    
    private async Task LoadGuideDataAsync(string selection)
    {
        DisplayedChannels.Clear(); 
        ChannelItemsControl.ItemsSource = null;
        GuideItemsControl.ItemsSource = null;

        ChannelCollection? targetCollection = null;
        
        if (selection != "All Channels" && selection != "Favorites" && selection != "HD Channels" && selection != "SD Channels")
        {
            targetCollection = _collections.FirstOrDefault(c => c.Name == selection);
        }

        var channels = await _libraryService.GetGuideChannelsAsync(targetCollection, 4);

        if (selection == "Favorites") channels = channels.Where(c => c.Favorite).ToList();
        else if (selection == "HD Channels") channels = channels.Where(c => c.IsHD).ToList();
        else if (selection == "SD Channels") channels = channels.Where(c => !c.IsHD).ToList();

        ChannelItemsControl.ItemsSource = DisplayedChannels;
        GuideItemsControl.ItemsSource = DisplayedChannels;
        RenderGuideData(channels, selection);
    }

    // --- NEW TV-OVERLAY FILTER LOGIC ---
    
    private void CollectionFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        FilterSelectionList.ItemsSource = _availableCollections;
        FilterSelectionList.SelectedItem = _activeCollectionName;
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
        if (selectedItem is string selection)
        {
            _activeCollectionName = selection;
            CollectionFilterBtn.Content = $"{selection} ▼";
            CloseFilterOverlay();
            
            try 
            {
                var prefs = PreferencesManager.Load();
                prefs.LastGuideCollection = selection;
                PreferencesManager.Save(prefs);
            }
            catch { }

            await LoadGuideDataAsync(selection);
        }
    }

    private void FilterSelectionList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (FilterSelectionList.SelectedItem != null) ProcessFilterSelection(FilterSelectionList.SelectedItem);
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
        
        if (command == HtpcCommand.Up) 
        {
            GuideNavBtn.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Down)
        {
            FocusFirstAiring();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Left || command == HtpcCommand.Right)
        {
            var direction = command == HtpcCommand.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right;
            (sender as FrameworkElement)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true; 
        }
    }

    // --- 10-FOOT UI FOCUS BRIDGES ---

    private void GuideNav_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Down)
        {
            FocusFirstAiring();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up)
        {
            GuideNavBtn.Focus();
            e.Handled = true;
        }
    }

    private void ShowHidden_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Down)
        {
            CollectionFilterBtn.Focus();
            e.Handled = true;
        }
    }

    // --- MAIN GRID NAVIGATION ---

    private void GuideView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (ModalOverlay.Visibility == Visibility.Visible)
        {
            if (command == HtpcCommand.Back || e.Key == Key.Escape)
            {
                CloseModal_Click(null!, null!);
                _lastFocusedAiringButton?.Focus();
                e.Handled = true;
            }
            return;
        }
        
        if (FilterOverlay.Visibility == Visibility.Visible)
        {
            if (command == HtpcCommand.Back || e.Key == Key.Escape)
            {
                CloseFilterOverlay();
                e.Handled = true;
            }
            return;
        }

        bool isArrowKey = command == HtpcCommand.Left || command == HtpcCommand.Right || command == HtpcCommand.Up || command == HtpcCommand.Down;
        if (!isArrowKey) return;

        if (command == HtpcCommand.Down && Keyboard.FocusedElement is Button topBtn && topBtn.Tag == null && topBtn != CollectionFilterBtn)
        {
            string? btnText = topBtn.Content?.ToString();
            if (btnText == "Home" || btnText == "Guide" || btnText == "Multiview" || btnText == "Movies" || btnText == "Shows" || btnText == "Videos" || btnText == "Settings")
            {
                CollectionFilterBtn.Focus();
                e.Handled = true;
                return;
            }
        }

        if (Keyboard.FocusedElement is Button btn && btn.Tag is Airing currentAiring)
        {
            e.Handled = true;

            if (command == HtpcCommand.Left || command == HtpcCommand.Right)
            {
                var channel = DisplayedChannels.FirstOrDefault(c => c.Number == currentAiring.ChannelNumber);
                if (channel != null)
                {
                    var airings = channel.CurrentAirings ?? new List<Airing>();
                    int currentIndex = airings.IndexOf(currentAiring);
                    int nextIndex = command == HtpcCommand.Right ? currentIndex + 1 : currentIndex - 1;

                    if (nextIndex >= 0 && nextIndex < airings.Count)
                    {
                        var targetAiring = airings[nextIndex];
                        FocusAiringSafely(channel, targetAiring);
                    }
                    else if (nextIndex < 0 && command == HtpcCommand.Left)
                    {
                        CollectionFilterBtn.Focus();
                    }
                }
            }
            else if (command == HtpcCommand.Up || command == HtpcCommand.Down)
            {
                int currentChannelIndex = DisplayedChannels.IndexOf(DisplayedChannels.First(c => c.Number == currentAiring.ChannelNumber));
                int nextIndex = command == HtpcCommand.Down ? currentChannelIndex + 1 : currentChannelIndex - 1;

                if (nextIndex >= 0 && nextIndex < DisplayedChannels.Count)
                {
                    var nextChannel = DisplayedChannels[nextIndex];
                    var safeAirings = nextChannel.CurrentAirings ?? new List<Airing>();

                    var targetAiring = safeAirings.FirstOrDefault(a => 
                        a.StartTime <= _lastTimeFocus && 
                        a.StartTime.AddSeconds(a.Duration ?? 0) > _lastTimeFocus) ?? safeAirings.FirstOrDefault();

                    if (targetAiring != null)
                    {
                        FocusAiringSafely(nextChannel, targetAiring);
                    }
                }
                else if (nextIndex < 0 && command == HtpcCommand.Up)
                {
                    CollectionFilterBtn.Focus();
                }
            }
            return; 
        }

        bool isFocusedOnTopMenu = Keyboard.FocusedElement == CollectionFilterBtn || Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is CheckBox ||
                                  (Keyboard.FocusedElement is Button tb && tb.Tag == null) ||
                                  (Keyboard.FocusedElement is RepeatButton);

        if (!isFocusedOnTopMenu)
        {
            if (Keyboard.FocusedElement == null || Keyboard.FocusedElement == this || Keyboard.FocusedElement == GuideItemsControl)
            {
                GuideNavBtn.Focus(); 
                e.Handled = true;
            }
        }
    }
    
    private void GuideView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            if (DisplayedChannels.Count > 0 && _lastFocusedAiringButton != null && _lastFocusedAiringButton.Tag is Airing lastAiring)
            {
                var channel = DisplayedChannels.FirstOrDefault(c => c.Number == lastAiring.ChannelNumber);
                if (channel != null) 
                {
                    FocusAiringSafely(channel, lastAiring);
                    return;
                }
            }
            
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                GuideNavBtn.Focus();
            }), DispatcherPriority.ApplicationIdle);
        }
        else
        {
            if (FilterOverlay.Visibility == Visibility.Visible) CloseFilterOverlay();
        }
    }

    private void FocusAiringSafely(Channel channel, Airing airing)
    {
        this.Focus(); 

        var sv = GetScrollViewer(GuideItemsControl);
        if (sv != null)
        {
            int index = DisplayedChannels.IndexOf(channel);
            double rowHeight = 80.0; 
            
            double desiredTopOffset = (index - 3) * rowHeight; 
            double desiredBottomOffset = index * rowHeight;    
            
            if (desiredTopOffset > sv.VerticalOffset)
            {
                sv.ScrollToVerticalOffset(desiredTopOffset);
            }
            else if (desiredBottomOffset < sv.VerticalOffset)
            {
                sv.ScrollToVerticalOffset(desiredBottomOffset);
            }
        }

        GuideItemsControl.ScrollIntoView(channel);
        
        Dispatcher.BeginInvoke(new Action(() => 
        {
            GuideItemsControl.UpdateLayout();
            var row = GuideItemsControl.ItemContainerGenerator.ContainerFromItem(channel) as DependencyObject;
            
            if (row != null)
            {
                var targetBtn = FindButtonForAiring(row, airing);
                targetBtn?.Focus();
            }
        }), DispatcherPriority.Loaded);
    }

    private Button? FindButtonForAiring(DependencyObject parent, Airing targetAiring)
    {
        Queue<DependencyObject> queue = new Queue<DependencyObject>();
        queue.Enqueue(parent);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is Button b && b.Tag == targetAiring) return b;

            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < childCount; i++) queue.Enqueue(System.Windows.Media.VisualTreeHelper.GetChild(current, i));
        }
        return null;
    }
    
    private void FocusFirstAiring()
    {
        if (DisplayedChannels.Count > 0)
        {
            var firstChannel = DisplayedChannels[0];
            var firstAiring = firstChannel.CurrentAirings?.FirstOrDefault();
            if (firstAiring != null) FocusAiringSafely(firstChannel, firstAiring);
        }
    }

    private void AiringButton_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Airing airing)
        {
            bool isHorizontalMove = _lastFocusedAiringButton == null || 
                ((Airing)_lastFocusedAiringButton.Tag).ChannelNumber == airing.ChannelNumber;

            if (isHorizontalMove)
            {
                if (airing.IsAiringNow) _lastTimeFocus = DateTime.Now;
                else _lastTimeFocus = airing.StartTime.AddSeconds(1);
            }

            _lastFocusedAiringButton = btn;

            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                btn.BringIntoView();
            }), DispatcherPriority.Render);
        }
    }

    // --- OTHER UI LOGIC ---
    
    private async void FavoriteToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Channel channel)
        {
            channel.Favorite = !channel.Favorite;

            var activeServer = _serverManager.GetActiveServer();
            if (activeServer != null)
            {
                string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
                
                bool success = await _libraryService.ToggleChannelFavoriteAsync(baseUrl, channel.DeviceId, channel.Number);
                
                if (!success)
                {
                    channel.Favorite = !channel.Favorite;
                    MessageBox.Show("Failed to sync favorite with the server.", "Sync Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
    
    private void FilterChannels()
    {
        DisplayedChannels.Clear();
        bool showHidden = ShowHiddenCheckBox.IsChecked == true;
        
        string currentFilter = _activeCollectionName;

        foreach (var channel in _allChannels)
        {
            if (channel.Hidden && !showHidden) continue;
            
            if (currentFilter == "Favorites" && !channel.Favorite) continue;
            if (currentFilter == "HD Channels" && !channel.IsHD) continue;
            if (currentFilter == "SD Channels" && channel.IsHD) continue;

            DisplayedChannels.Add(channel);
        }
    }

    private void ShowHidden_Click(object sender, RoutedEventArgs e)
    {
        FilterChannels();
    }

    private async void HideChannel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is Channel channel)
        {
            channel.Hidden = !channel.Hidden;
            FilterChannels(); 

            var activeServer = _serverManager.GetActiveServer();
            if (activeServer != null)
            {
                string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
                
                bool success = await _libraryService.ToggleChannelHiddenAsync(baseUrl, channel.DeviceId, channel.Number);
                
                if (!success)
                {
                    channel.Hidden = !channel.Hidden;
                    FilterChannels();
                    MessageBox.Show("Failed to sync hidden status with the server.", "Sync Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }

    private void AiringBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Airing airing)
        {
            _selectedAiring = airing;
            
            ModalTitle.Text = airing.DisplayTitle;
            ModalTime.Text = $"{airing.Start:h:mm tt} - {airing.End:h:mm tt}";
            ModalSummary.Text = string.IsNullOrWhiteSpace(airing.DisplaySummary) ? "No description available." : airing.DisplaySummary;
            
            try 
            { 
                if (!string.IsNullOrWhiteSpace(airing.ImageUrl))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(airing.ImageUrl, UriKind.RelativeOrAbsolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; 
                    bmp.EndInit();
                    ModalImage.Source = bmp;
                }
                else ModalImage.Source = null;
            } 
            catch { ModalImage.Source = null; }

            WatchLiveBtn.Visibility = Visibility.Visible;
            
            RecordEpisodeButton.Visibility = Visibility.Visible;
            RecordSeriesButton.Visibility = string.IsNullOrWhiteSpace(airing.SeriesId) ? Visibility.Collapsed : Visibility.Visible;

            ModalOverlay.Visibility = Visibility.Visible;
            
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                WatchLiveBtn.Focus();
            }), DispatcherPriority.Input);
        }
    }
    
    private async void RecordEpisode_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAiring == null) return;
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return;

        var prefs = PreferencesManager.Load();
        int padStart = prefs.PaddingStartMinutes * 60;
        int padEnd = prefs.PaddingEndMinutes * 60;
        
        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        
        RecordEpisodeButton.Content = "⏳ Working...";
        bool success = await _libraryService.CreateRecordingJobAsync(baseUrl, _selectedAiring.ChannelNumber ?? "", _selectedAiring, padStart, padEnd);
        
        if (success) MessageBox.Show("Recording Scheduled successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show("Failed to schedule recording.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        
        RecordEpisodeButton.Content = "⏺ Record";
        CloseModal_Click(null!, null!);
    }

    private async void RecordSeries_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAiring == null || string.IsNullOrWhiteSpace(_selectedAiring.SeriesId)) return;
        var activeServer = _serverManager.GetActiveServer();
        if (activeServer == null) return;

        var prefs = PreferencesManager.Load();
        int padStart = prefs.PaddingStartMinutes * 60;
        int padEnd = prefs.PaddingEndMinutes * 60;
        
        string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
        
        RecordSeriesButton.Content = "⏳ Working...";
        bool success = await _libraryService.CreateSeriesPassAsync(baseUrl, _selectedAiring.SeriesId, _selectedAiring.Title ?? "Unknown", _selectedAiring.ImageUrl ?? "", padStart, padEnd);
        
        if (success) MessageBox.Show("Series Pass created successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show("Failed to create Series Pass.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        
        RecordSeriesButton.Content = "⏺ Series Pass";
        CloseModal_Click(null!, null!);
    }

    private void WatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAiring != null)
        {
            var activeServer = _serverManager.GetActiveServer();
            if (activeServer == null) return;

            string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
            var parentChannel = DisplayedChannels.FirstOrDefault(c => c.Number == _selectedAiring.ChannelNumber);
            if (parentChannel == null) return;

            var media = _libraryService.CreateLiveMediaItem(baseUrl, parentChannel, _selectedAiring);
            OnPlayRequested?.Invoke(this, media);
            ModalOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void CloseModal_Click(object sender, RoutedEventArgs e)
    {
        ModalOverlay.Visibility = Visibility.Collapsed;
        
        if (_lastFocusedAiringButton != null)
        {
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                _lastFocusedAiringButton.Focus();
            }), DispatcherPriority.Input);
        }
    }

    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Recordings_Click(object sender, RoutedEventArgs e) => OnRecordingsRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
    private void Collections_Click(object sender, RoutedEventArgs e) => OnCollectionsRequested?.Invoke(this, EventArgs.Empty);
    
    private void ChannelItemsControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = GetScrollViewer(GuideItemsControl);
        if (sv != null) sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void TimelineScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Shift)
            TimelineScroller.ScrollToHorizontalOffset(TimelineScroller.HorizontalOffset - e.Delta);
        else 
        {
            var sv = GetScrollViewer(GuideItemsControl);
            if (sv != null) sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        }
        e.Handled = true;
    }

    private void GuideItemsControl_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0) return;
        var channelScroll = GetScrollViewer(ChannelItemsControl);
        if (channelScroll != null) channelScroll.ScrollToVerticalOffset(e.VerticalOffset);
    }

    public void RenderGuideData(List<Channel> channels, string collectionId)
    {
        _currentCollectionId = string.IsNullOrEmpty(collectionId) ? "All" : collectionId;

        var prefs = PreferencesManager.Load();
        if (prefs.CustomChannelOrders != null && prefs.CustomChannelOrders.TryGetValue(_currentCollectionId, out var savedOrder))
        {
            channels = channels.OrderBy(c => 
            {
                int idx = savedOrder.IndexOf(c.Number);
                return idx != -1 ? idx : 999999; 
            }).ToList();
        }

        _allChannels = channels;
        FilterChannels();
        GenerateTimeHeaders();
        FocusFirstAiring();
    }

    private void GenerateTimeHeaders()
    {
        var headers = new List<string>();
        DateTime now = DateTime.Now;
        DateTime start = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute >= 30 ? 30 : 0, 0);

        for (int i = 0; i < 10; i++)
        {
            headers.Add(start.ToString("h:mm tt"));
            start = start.AddMinutes(30);
        }
        TimeHeadersControl.ItemsSource = headers;
    }
    
    // --- CUSTOM SORTING LOGIC ---

    private void MoveChannelUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is Channel channel)
        {
            int index = _allChannels.IndexOf(channel);
            if (index > 0)
            {
                _allChannels.RemoveAt(index);
                _allChannels.Insert(index - 1, channel);
                FilterChannels(); 
                SaveCurrentSortOrder();
            }
        }
    }

    private void MoveChannelDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is Channel channel)
        {
            int index = _allChannels.IndexOf(channel);
            if (index < _allChannels.Count - 1)
            {
                _allChannels.RemoveAt(index);
                _allChannels.Insert(index + 1, channel);
                FilterChannels();
                SaveCurrentSortOrder();
            }
        }
    }

    private void MoveChannelTop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is Channel channel)
        {
            _allChannels.Remove(channel);
            _allChannels.Insert(0, channel);
            FilterChannels();
            SaveCurrentSortOrder();
        }
    }

    private void MoveChannelBottom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is Channel channel)
        {
            _allChannels.Remove(channel);
            _allChannels.Add(channel);
            FilterChannels();
            SaveCurrentSortOrder();
        }
    }

    private void SaveCurrentSortOrder()
    {
        var prefs = PreferencesManager.Load();
        if (prefs.CustomChannelOrders == null) 
            prefs.CustomChannelOrders = new Dictionary<string, List<string>>();
        
        var newOrder = _allChannels.Select(c => c.Number).ToList();
        
        prefs.CustomChannelOrders[_currentCollectionId] = newOrder;
        PreferencesManager.Save(prefs);
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

    private void ThemeToggleBtn_Click(object sender, RoutedEventArgs e)
{
    // Toggle the state
    string currentTheme = PreferencesManager.LoadTheme();
    string newTheme = currentTheme == "Dark" ? "Light" : "Dark";

    // Save state to JSON
    PreferencesManager.SaveTheme(newTheme);

    // Tell App.xaml.cs to load the new dictionary
    ((App)Application.Current).ApplyTheme(newTheme);

    // Update the icon
    ThemeToggleBtn.Content = newTheme == "Dark" ? "\xE708" : "\xE706";
}

    private void ScrollLeft_Click(object sender, RoutedEventArgs e) => TimelineScroller.ScrollToHorizontalOffset(TimelineScroller.HorizontalOffset - 300);
    private void ScrollRight_Click(object sender, RoutedEventArgs e) => TimelineScroller.ScrollToHorizontalOffset(TimelineScroller.HorizontalOffset + 300);
    
    private void PageUp_Click(object sender, RoutedEventArgs e)
    {
        var sv = GetScrollViewer(GuideItemsControl);
        if (sv != null) sv.ScrollToVerticalOffset(sv.VerticalOffset - 210);
    }

    private void PageDown_Click(object sender, RoutedEventArgs e)
    {
        var sv = GetScrollViewer(GuideItemsControl);
        if (sv != null) sv.ScrollToVerticalOffset(sv.VerticalOffset + 210);
    }
}
