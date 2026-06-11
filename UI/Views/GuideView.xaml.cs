using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class GuideView : UserControl
{
    public event EventHandler? OnBackRequested;
    public event EventHandler<MediaItem>? OnPlayRequested; 

    private readonly MediaLibraryService _libraryService;
    private readonly ServerManagerService _serverManager;
    public ObservableCollection<Channel> DisplayedChannels { get; set; } = new ObservableCollection<Channel>();

    public GuideView(MediaLibraryService libraryService, ServerManagerService serverManager)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _serverManager = serverManager;
        
        ChannelItemsControl.ItemsSource = DisplayedChannels;
        GuideItemsControl.ItemsSource = DisplayedChannels;
        
        this.Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DisplayedChannels.Count > 0) return; // Prevent double-loading
        
        StatusText.Text = "Loading Guide Data...";
        
        // Fetch the active collection to filter by
        var activeServer = _serverManager.GetActiveServer();
        var collections = await _libraryService.GetCollectionsAsync();
        var savedCollection = collections.FirstOrDefault(c => c.Id == activeServer?.DefaultCollectionId);

        // Fetch the timeline blocks
        var channels = await _libraryService.GetGuideChannelsAsync(savedCollection, 4); // Load 4 hours of data
        RenderGuideData(channels);
        
        StatusText.Text = $"Loaded {channels.Count} channels.";
        
        // Auto-focus the timeline so arrow keys work immediately
        GuideItemsControl.Focus();
    }

    private ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer sv) return sv;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        OnBackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AiringBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Airing airing)
        {
            // The HTPC player needs a MediaItem, so we convert the Airing block into one!
            var media = new MediaItem
            {
                Id = airing.ChannelNumber ?? "0",
                Title = $"Channel {airing.ChannelNumber}",
                CurrentShowTitle = airing.DisplayTitle,
                StreamUrl = "" // The HTPC Player Service will build this URL!
            };

            OnPlayRequested?.Invoke(this, media);
        }
    }

    // --- Scrolling Logic Ported from Feral ---

    private void PageUp_Click(object sender, RoutedEventArgs e)
    {
        var sv = GetScrollViewer(GuideItemsControl);
        if (sv != null) sv.ScrollToVerticalOffset(Math.Max(0, sv.VerticalOffset - 250));
    }

    private void PageDown_Click(object sender, RoutedEventArgs e)
    {
        var sv = GetScrollViewer(GuideItemsControl);
        if (sv != null) sv.ScrollToVerticalOffset(Math.Min(sv.ScrollableHeight, sv.VerticalOffset + 250));
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        TimelineScroller.ScrollToHorizontalOffset(Math.Max(0, TimelineScroller.HorizontalOffset - 240));
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        TimelineScroller.ScrollToHorizontalOffset(TimelineScroller.HorizontalOffset + 240);
    }

    private void ChannelItemsControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender };
        TimelineScroller.RaiseEvent(eventArg);
    }

    private void TimelineScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            TimelineScroller.ScrollToHorizontalOffset(TimelineScroller.HorizontalOffset - e.Delta);
        }
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

    // --- Data Rendering ---

    public void RenderGuideData(List<Channel> channels)
    {
        DisplayedChannels.Clear();
        foreach (var c in channels) DisplayedChannels.Add(c);

        GenerateTimeHeaders();
    }

    private void GenerateTimeHeaders()
    {
        var headers = new List<string>();
        DateTime now = DateTime.Now;
        DateTime start = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute >= 30 ? 30 : 0, 0);

        for (int i = 0; i < 12; i++) // 6 hours worth of headers
        {
            headers.Add(start.AddMinutes(i * 30).ToString("h:mm tt"));
        }
        TimeHeadersControl.ItemsSource = headers;
    }
}