using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Input; 
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class VideosView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;

    private readonly MediaLibraryService _libraryService;
    private bool _isInitialized = false;

    public ObservableCollection<MediaItem> VideoGroups { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> CurrentVideos { get; set; } = new ObservableCollection<MediaItem>();

    public VideosView(MediaLibraryService libraryService)
    {
        InitializeComponent();
        _libraryService = libraryService;
        this.DataContext = this;
        Loaded += OnLoaded;
        this.PreviewKeyDown += VideosView_PreviewKeyDown; 
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) 
        {
            GroupsGrid.Focus();
            return;
        }

        _isInitialized = true;
        
        var groups = await _libraryService.GetVideoGroupsAsync();
        VideoGroups.Clear();
        foreach (var group in groups) VideoGroups.Add(group);

        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            if (GroupsGrid.Items.Count > 0)
            {
                var firstItem = GroupsGrid.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                firstItem?.Focus();
            }
        }), DispatcherPriority.Input);
    }

    private void VideosView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (VideosOverlay.Visibility == Visibility.Visible && command == HtpcCommand.Back)
        {
            CloseOverlay_Click(null!, null!);
            GroupsGrid.Focus();
            e.Handled = true;
        }
    }

    // --- OVERLAY DRILL-DOWN LOGIC ---

    private void GroupItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem group)
        {
            OpenGroupOverlay(group);
            e.Handled = true;
        }
    }

    private void GroupCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem group)
        {
            OpenGroupOverlay(group);
        }
    }

    private async void OpenGroupOverlay(MediaItem group)
    {
        try
        {
            SelectedGroupName.Text = group.Title;
            
            VideosOverlay.Visibility = Visibility.Visible;
            CurrentVideos.Clear();
            OverlayScroll.ScrollToTop(); // Ensures the overlay always opens at the top

            var videos = await _libraryService.GetVideosInGroupAsync(group.Id);
            foreach (var vid in videos) CurrentVideos.Add(vid);

            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                if (VideosList.Items.Count > 0)
                {
                    var firstVideo = VideosList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                    firstVideo?.Focus();
                }
            }), DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading videos: {ex.Message}");
        }
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        VideosOverlay.Visibility = Visibility.Collapsed;
    }

    private void VideoItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem video)
        {
            OnPlayRequested?.Invoke(this, video);
            e.Handled = true;
        }
    }

    private void VideoCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem video)
        {
            OnPlayRequested?.Invoke(this, video);
        }
    }

    // --- MOUSE SCROLLING FIX ---
    
    private void GroupsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };
        MainScroll.RaiseEvent(eventArg);
    }

    private void VideosList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };
        OverlayScroll.RaiseEvent(eventArg);
    }

    // --- NAVBAR ROUTING ---

    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
}