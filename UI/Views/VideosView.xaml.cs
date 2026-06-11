using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class VideosView : UserControl
{
    // Global Navigation Events
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;

    private readonly MediaLibraryService _libraryService;
    private bool _isInitialized = false;

    // Bindings
    public ObservableCollection<MediaItem> VideoGroups { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> CurrentVideos { get; set; } = new ObservableCollection<MediaItem>();

    public VideosView(MediaLibraryService libraryService)
    {
        InitializeComponent();
        _libraryService = libraryService;
        this.DataContext = this;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;
        
        var groups = await _libraryService.GetVideoGroupsAsync();
        VideoGroups.Clear();
        foreach (var group in groups) VideoGroups.Add(group);
    }

    // --- OVERLAY DRILL-DOWN LOGIC ---

    private async void GroupCard_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is ListBoxItem item && item.DataContext is MediaItem group)
            {
                SelectedGroupName.Text = group.Title;
                
                // Show overlay instantly so it feels responsive
                VideosOverlay.Visibility = Visibility.Visible;
                CurrentVideos.Clear();

                var videos = await _libraryService.GetVideosInGroupAsync(group.Id);
                foreach (var vid in videos) CurrentVideos.Add(vid);
            }
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

    private void VideoCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem video)
        {
            OnPlayRequested?.Invoke(this, video);
        }
    }

    // --- NAVBAR ROUTING ---

    private void Home_Click(object sender, MouseButtonEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, MouseButtonEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, MouseButtonEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, MouseButtonEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, MouseButtonEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
}