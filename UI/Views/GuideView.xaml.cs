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
    // Global Navigation Events
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

    // --- MODAL OVERLAY LOGIC ---

    private void AiringBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Airing airing)
        {
            _selectedAiring = airing;
            
            // Populate overlay metadata
            ModalTitle.Text = airing.DisplayTitle;
            ModalTime.Text = $"{airing.Start:h:mm tt} - {airing.End:h:mm tt}";
            ModalSummary.Text = string.IsNullOrWhiteSpace(airing.DisplaySummary) ? "No description available." : airing.DisplaySummary;
            
            // Load poster safely
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

            // Show the bottom guide
            ModalOverlay.Visibility = Visibility.Visible;
        }
    }

    private void WatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAiring != null)
        {
            var activeServer = _serverManager.GetActiveServer();
            if (activeServer == null) return;

            string baseUrl = $"http://{activeServer.IpAddress}:{activeServer.Port}";
            
            // Find the underlying channel to check for virtual properties
            var parentChannel = DisplayedChannels.FirstOrDefault(c => c.Number == _selectedAiring.ChannelNumber);
            if (parentChannel == null) return;

            // THE FIX: Dynamically generate the correct stream rules
            var media = _libraryService.CreateLiveMediaItem(baseUrl, parentChannel, _selectedAiring);
            
            OnPlayRequested?.Invoke(this, media);
            ModalOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void CloseModal_Click(object sender, RoutedEventArgs e)
    {
        ModalOverlay.Visibility = Visibility.Collapsed;
    }

    // --- NAVBAR ROUTING ---
    private void Home_Click(object sender, MouseButtonEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, MouseButtonEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, MouseButtonEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, MouseButtonEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, MouseButtonEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);

    // --- PAGINATION & SCROLLING ---
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