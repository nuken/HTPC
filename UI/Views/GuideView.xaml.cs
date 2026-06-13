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

namespace HTPC.UI.Views;

public partial class GuideView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler<MediaItem>? OnPlayRequested; 

    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    public ObservableCollection<Channel> DisplayedChannels { get; set; } = new ObservableCollection<Channel>();

    private Airing? _selectedAiring;
	private Button? _lastFocusedAiringButton;
    private DateTime _lastTimeFocus = DateTime.MinValue;

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
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DisplayedChannels.Count > 0) return; 
        
        var activeServer = _serverManager.GetActiveServer();
        var collections = await _libraryService.GetCollectionsAsync();
        var savedCollectionId = Services.PreferencesManager.LoadGuideCollection();
        
        var targetCollection = collections.FirstOrDefault(c => c.Id == savedCollectionId) ?? collections.FirstOrDefault();
        
        if (targetCollection != null)
        {
            var channels = await _libraryService.GetGuideChannelsAsync(targetCollection, 4);
            RenderGuideData(channels);
        }
    }


    private void GuideView_PreviewKeyDown(object sender, KeyEventArgs e)
{
    var command = InputMapper.GetCommand(e.Key);

    // 1. MODAL BACK BUTTON
    if (ModalOverlay.Visibility == Visibility.Visible)
    {
        if (command == HtpcCommand.Back)
        {
            CloseModal_Click(null!, null!);
            _lastFocusedAiringButton?.Focus();
            e.Handled = true;
        }
        return;
    }

    bool isArrowKey = command == HtpcCommand.Left || command == HtpcCommand.Right || command == HtpcCommand.Up || command == HtpcCommand.Down;
    if (!isArrowKey) return;

    // 2. MATHEMATICAL EPG ROUTING
    if (Keyboard.FocusedElement is Button btn && btn.Tag is Airing currentAiring)
    {
        e.Handled = true; // Stop native WPF from dumping focus into the void!

        // HORIZONTAL ROUTING
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
                    // Bounce to the top menu if they push left on the very first show
                    var request = new TraversalRequest(FocusNavigationDirection.Up);
                    btn.MoveFocus(request);
                }
            }
        }
        // VERTICAL ROUTING
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
                // Escape up to the Navigation Bar
                var request = new TraversalRequest(FocusNavigationDirection.Up);
                btn.MoveFocus(request);
            }
        }
        return; 
    }

    // 3. FAIL-SAFE THE VOID
    bool isFocusedOnTopMenu = Keyboard.FocusedElement is ComboBox || Keyboard.FocusedElement is TextBox || 
                              (Keyboard.FocusedElement is Button topBtn && topBtn.Tag == null) ||
                              (Keyboard.FocusedElement is RepeatButton);

    if (!isFocusedOnTopMenu)
    {
        if (Keyboard.FocusedElement == null || Keyboard.FocusedElement == this || Keyboard.FocusedElement == GuideItemsControl)
        {
            FocusFirstAiring();
            e.Handled = true;
        }
    }
}

    // THE FIX: Pierce through the UI Virtualization barrier smoothly
private void FocusAiringSafely(Channel channel, Airing airing)
{
    // 1. "Park" the focus safely on the background control so it isn't destroyed when the row scrolls out of view
    this.Focus(); 

    // 2. Force the ListBox to scroll the required channel into the view
    GuideItemsControl.ScrollIntoView(channel);
    
    // 3. Wait for WPF to finish drawing the new row, then hand the focus to the button
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

private void GuideView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
{
    // When the guide becomes visible again...
    if ((bool)e.NewValue && DisplayedChannels.Count > 0)
    {
        // Try to restore exactly where they left off!
        if (_lastFocusedAiringButton != null && _lastFocusedAiringButton.Tag is Airing lastAiring)
        {
            var channel = DisplayedChannels.FirstOrDefault(c => c.Number == lastAiring.ChannelNumber);
            if (channel != null) 
            {
                FocusAiringSafely(channel, lastAiring);
                return;
            }
        }
        
        // If we can't restore, default safely to the top
        FocusFirstAiring();
    }
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
	
	// THE FIX: Provide a manual bridge from the Top Buttons down into the nested items control
    private void GuideNav_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Down)
        {
            FocusFirstAiring();
            e.Handled = true;
        }
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

    // THE FIX: When the user tabs to a button with the D-pad, force the scroll viewer to pan to it!
    private void AiringButton_GotFocus(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is Airing airing)
    {
        // 1. Check if we are moving horizontally
        bool isHorizontalMove = _lastFocusedAiringButton == null || 
            ((Airing)_lastFocusedAiringButton.Tag).ChannelNumber == airing.ChannelNumber;

        // 2. If moving horizontally, lock in the new time to keep vertical jumps perfectly straight
        if (isHorizontalMove)
        {
            if (airing.IsAiringNow) _lastTimeFocus = DateTime.Now;
            else _lastTimeFocus = airing.StartTime.AddSeconds(1);
        }

        // 3. Clear the CS0649 warning by actually remembering the button!
        _lastFocusedAiringButton = btn;

        // 4. Safely bring the item into view
        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            btn.BringIntoView();
        }), DispatcherPriority.Render);
    }
}

    // A standard WPF trick to pierce through DataTemplates and find specific controls
    private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
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

            // --- THE NEW BUTTON LOGIC ---
            WatchLiveBtn.Visibility = Visibility.Visible;
            
            // Ensure you have added x:Name="RecordEpisodeButton" and x:Name="RecordSeriesButton" 
            // to the new buttons in your GuideView.xaml Modal layout!
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

        // Convert Minutes to Seconds for the API
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
        if (_selectedAiring != null) GuideItemsControl.Focus();
    }

    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);

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

    public void RenderGuideData(List<Channel> channels)
    {
        DisplayedChannels.Clear();
        foreach (var c in channels) DisplayedChannels.Add(c);
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

    private ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer viewer) return viewer;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            // THE FIX: Removed the "parent:" named parameter here!
            var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
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