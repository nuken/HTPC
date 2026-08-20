using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Input;
using HTPC.Core.Models;
using HTPC.UI.ViewModels;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class RecordingsView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnShowsRequested;
	public event EventHandler? OnSportsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler? OnMultiviewRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;
    public event EventHandler? OnCollectionsRequested;

    private readonly RecordingsViewModel _viewModel;
    private MediaItem? _selectedMedia;
    private IInputElement? _lastFocusedElement;

    // --- DISCOVER STATE ---
    private readonly DispatcherTimer _typingTimer;
    private int _discoverOffset = 0;
    private bool _isDiscoverLoading = false;
    private bool _discoverReachedEnd = false;
    private bool _isDiscoverMode = false;
	private enum FilterMode { None, Collection, Channel }
    private FilterMode _currentFilterMode = FilterMode.None;
    private ChannelCollection? _activeCollection;
    private Channel? _activeChannel;

    public RecordingsView(RecordingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        
        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _typingTimer.Tick += TypingTimer_Tick;

        Loaded += OnLoaded;
        PreviewKeyDown += RecordingsView_PreviewKeyDown;
		IsVisibleChanged += RecordingsView_IsVisibleChanged;
    }
	
	// --- NEW: ZOMBIE OVERLAY CLEANUP ---
    private void RecordingsView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) 
        {
            // The view is hiding (user navigated away)
            if (ModalOverlay.Visibility == Visibility.Visible)
            {
                ModalOverlay.Visibility = Visibility.Collapsed;
            }
            
            // Also clean up the filter dropdown overlay just in case!
            if (FilterOverlay.Visibility == Visibility.Visible)
            {
                FilterOverlay.Visibility = Visibility.Collapsed;
                _currentFilterMode = FilterMode.None;
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeToggleBtn.Content = PreferencesManager.LoadTheme() == "Dark" ? "\xE708" : "\xE706";

        // 1. Focus the UI instantly so the user isn't stuck waiting
        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            RecordingsNavBtn.Focus();
            Keyboard.Focus(RecordingsNavBtn);
        }), DispatcherPriority.ApplicationIdle);

        // 2. Fire and forget the heavy network loads in the background
        _ = LoadDataAsync();
    }
	
	private async System.Threading.Tasks.Task LoadDataAsync()
    {
        await _viewModel.LoadRecordingsAsync();
        
        await _viewModel.LoadDiscoverCollectionsAsync();
        
        // Populate initial text and trigger load
        if (_viewModel.DiscoverCollections.Count > 0)
        {
            _activeCollection = _viewModel.DiscoverCollections[0];
            CollectionFilterBtn.Content = $"{_activeCollection.Name} ▼";
            await _viewModel.LoadDiscoverChannelsAsync(_activeCollection);
        }
        else
        {
            await _viewModel.LoadDiscoverChannelsAsync(null);
        }
    }

    // --- TAB SWITCHING LOGIC ---
    private void TabMyRecordings_Click(object sender, RoutedEventArgs e)
    {
        _isDiscoverMode = false;
        
        TabMyRecordings.Style = (Style)FindResource("TabButtonActiveStyle");
        TabDiscover.Style = (Style)FindResource("TabButtonStyle");
        
        MyRecordingsContainer.Visibility = Visibility.Visible;
        DiscoverContainer.Visibility = Visibility.Collapsed;
        
        FocusFirstAvailableContentRow();
    }

    private async void TabDiscover_Click(object sender, RoutedEventArgs e)
    {
        _isDiscoverMode = true;
        
        TabDiscover.Style = (Style)FindResource("TabButtonActiveStyle");
        TabMyRecordings.Style = (Style)FindResource("TabButtonStyle");
        
        MyRecordingsContainer.Visibility = Visibility.Collapsed;
        DiscoverContainer.Visibility = Visibility.Visible;
        
        if (_viewModel.DiscoverResults.Count == 0)
        {
            await ResetAndLoadDiscoverAsync();
        }
        
        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            DiscoverSearchBox.Focus();
        }), DispatcherPriority.Input);
    }

    // --- DISCOVERY SEARCH & FILTER ---
    private void DiscoverSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _typingTimer.Stop();
        _typingTimer.Start();
    }

    private async void TypingTimer_Tick(object? sender, EventArgs e)
    {
        _typingTimer.Stop();
        await ResetAndLoadDiscoverAsync();
    }

    private async Task ResetAndLoadDiscoverAsync()
    {
        _discoverOffset = 0;
        _discoverReachedEnd = false;
        _viewModel.DiscoverResults.Clear();
        DiscoverScroll.ScrollToTop();
        
        await LoadNextDiscoverChunkAsync();
    }

    private async System.Threading.Tasks.Task LoadNextDiscoverChunkAsync()
    {
        if (_isDiscoverLoading || _discoverReachedEnd) return;
        
        _isDiscoverLoading = true;
        DiscoverLoadingText.Visibility = Visibility.Visible;

        string query = DiscoverSearchBox.Text;
        string channelFilter = _activeChannel?.Number ?? "ALL";
        var activeCollection = _activeCollection;

        var newAirings = await _viewModel.GetDiscoverAiringsAsync(_discoverOffset, RecordingsViewModel.DiscoverChunkSize, query, channelFilter, activeCollection);
        
        if (newAirings == null || newAirings.Count == 0)
        {
            _discoverReachedEnd = true;
        }
        else
        {
            foreach (var airing in newAirings)
            {
                _viewModel.DiscoverResults.Add(airing);
            }
            _discoverOffset += newAirings.Count;
        }

        DiscoverLoadingText.Visibility = Visibility.Collapsed;
        _isDiscoverLoading = false;
    }

    private async void DiscoverScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DiscoverScroll.VerticalOffset >= DiscoverScroll.ScrollableHeight - 100)
            await LoadNextDiscoverChunkAsync();
    }

    private void DiscoverList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender };
        DiscoverScroll.RaiseEvent(eventArg);
    }

    // --- NAVIGATION BRIDGING FIXES ---
    private void TopNavPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Down)
        {
            e.Handled = true;
            
            // --- FIX: Bridge down to the currently active SubNav pill ---
            if (_isDiscoverMode)
            {
                TabDiscover.Focus();
            }
            else
            {
                TabMyRecordings.Focus(); 
            }
        }
    }

    private void SubNavPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (command == HtpcCommand.Up)
        {
            e.Handled = true;
            // Bridge up to TopNav
            foreach (UIElement child in TopNavPanel.Children)
            {
                if (child is Button btn && btn.Focusable) { btn.Focus(); return; }
            }
        }
        else if (command == HtpcCommand.Down)
        {
            e.Handled = true;
            if (_isDiscoverMode) DiscoverSearchBox.Focus();
            else FocusFirstAvailableContentRow();
        }
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

    private void DiscoverSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Down || command == HtpcCommand.Up)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as TextBox)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
        // --- FIX: Allow remote to jump RIGHT into the new filter buttons ---
        else if (command == HtpcCommand.Right)
        {
            CollectionFilterBtn.Focus();
            e.Handled = true;
        }
    }

    private void FocusFirstAvailableContentRow()
    {
        if (TryFocusFirstListBoxItem(ActiveList)) return;
        if (TryFocusFirstListBoxItem(ScheduledList)) return;
        if (TryFocusFirstListBoxItem(RecentShowsList)) return;
        if (TryFocusFirstListBoxItem(RecentMoviesList)) return;
        TryFocusFirstListBoxItem(ImportedMediaList);
    }
	
	// --- NEW TV-OVERLAY FILTER LOGIC ---
    private void CollectionFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.Collection;
        FilterOverlayTitle.Text = "Select Collection";
        FilterSelectionList.ItemsSource = _viewModel.DiscoverCollections;
        FilterSelectionList.SelectedItem = _activeCollection ?? (_viewModel.DiscoverCollections.Count > 0 ? _viewModel.DiscoverCollections[0] : null);
        OpenFilterOverlay();
    }

    private void ChannelFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentFilterMode = FilterMode.Channel;
        FilterOverlayTitle.Text = "Select Channel";
        FilterSelectionList.ItemsSource = _viewModel.DiscoverChannels;
        FilterSelectionList.SelectedItem = _activeChannel ?? (_viewModel.DiscoverChannels.Count > 0 ? _viewModel.DiscoverChannels[0] : null);
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
        _currentFilterMode = FilterMode.None;
        
        if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
        {
            Keyboard.Focus(uiElement);
        }
    }

    private async void ProcessFilterSelection(object selectedItem)
    {
        if (_currentFilterMode == FilterMode.Collection && selectedItem is ChannelCollection collection)
        {
            _activeCollection = collection;
            CollectionFilterBtn.Content = $"{collection.Name} ▼";
            CloseFilterOverlay();
            
            await _viewModel.LoadDiscoverChannelsAsync(collection);
            _activeChannel = null; 
            ChannelFilterBtn.Content = "All Channels ▼";

            if (_isDiscoverMode) await ResetAndLoadDiscoverAsync();
        }
        else if (_currentFilterMode == FilterMode.Channel && selectedItem is Channel channel)
        {
            _activeChannel = channel;
            ChannelFilterBtn.Content = $"{channel.Name} ▼";
            CloseFilterOverlay();

            if (_isDiscoverMode) await ResetAndLoadDiscoverAsync();
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
        if (command == HtpcCommand.Down || command == HtpcCommand.Up || command == HtpcCommand.Left || command == HtpcCommand.Right)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down :
                            command == HtpcCommand.Up ? FocusNavigationDirection.Up :
                            command == HtpcCommand.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right;

            (sender as FrameworkElement)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true; 
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

    // --- UNIFIED MODAL LOGIC ---
    private void ListBoxItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Down || command == HtpcCommand.Up)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            if (sender is UIElement uiElement)
            {
                uiElement.MoveFocus(new TraversalRequest(direction));
                e.Handled = true;
            }
            return;
        }

        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem media)
        {
            ShowModal(media, isDiscoverEvent: false);
            e.Handled = true; 
        }
    }

    private void DiscoverItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem media)
        {
            ShowModal(media, isDiscoverEvent: true);
            e.Handled = true; 
        }
        else if (command == HtpcCommand.Up && sender is UIElement ui)
        {
            // Bridge out of wrap panel back to the search bar or filter buttons
            var index = DiscoverGridList.ItemContainerGenerator.IndexFromContainer(ui);
            if (index >= 0 && index < 6) 
            {
                // --- FIX: Let WPF intelligently jump to the Search Box OR the Filter Buttons ---
                ui.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
                e.Handled = true;
            }
        }
    }

    private void RecordingCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem media)
        {
            ShowModal(media, isDiscoverEvent: false);
            e.Handled = true;
        }
    }

    private void DiscoverCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem media)
        {
            ShowModal(media, isDiscoverEvent: true);
            e.Handled = true;
        }
    }

    private void ShowModal(MediaItem media, bool isDiscoverEvent)
    {
        _lastFocusedElement = Keyboard.FocusedElement;
        _selectedMedia = media;
        
        ModalTitle.Text = string.IsNullOrWhiteSpace(media.CurrentShowTitle) ? media.Title : media.CurrentShowTitle;
        ModalSubTitle.Text = string.IsNullOrWhiteSpace(media.CurrentShowTitle) ? "" : media.Title;
        ModalSummary.Text = string.IsNullOrWhiteSpace(media.Summary) ? "No description available." : media.Summary;

        // Context-aware Modal Buttons
        if (isDiscoverEvent)
        {
            ModalPlayBtn.Visibility = Visibility.Collapsed;
            DeleteModalBtn.Visibility = Visibility.Collapsed;
            ModalRecordBtn.Visibility = Visibility.Visible;
            
            ModalTime.Text = $"Airs: {media.DisplayTime}";
            ModalTime.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 164, 239)); 
            ModalTime.Visibility = Visibility.Visible;
        }
        else
        {
            ModalRecordBtn.Visibility = Visibility.Collapsed;
            DeleteModalBtn.Visibility = Visibility.Visible;

            if (media.IsScheduled)
            {
                ModalTime.Text = $"Scheduled: {media.DisplayTime}";
                ModalTime.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 164, 239)); 
                ModalTime.Visibility = Visibility.Visible;
                ModalPlayBtn.Visibility = Visibility.Collapsed; 
            }
            else
            {
                ModalPlayBtn.Visibility = Visibility.Visible;
                
                if (media.IsRecording)
                {
                    ModalTime.Text = "● Currently Recording";
                    ModalTime.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(211, 47, 47)); 
                    ModalTime.Visibility = Visibility.Visible;
                }
                else
                {
                    ModalTime.Visibility = Visibility.Collapsed;
                }
            }
        }

        try 
        { 
            if (!string.IsNullOrWhiteSpace(media.PosterUrl))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(media.PosterUrl, UriKind.RelativeOrAbsolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; 
                bmp.EndInit();
                ModalImage.Source = bmp;
            }
            else ModalImage.Source = null;
        } 
        catch { ModalImage.Source = null; }

        ModalOverlay.Visibility = Visibility.Visible;
        
        _ = Dispatcher.InvokeAsync(() => 
        {
            if (isDiscoverEvent) ModalRecordBtn.Focus();
            else if (media.IsScheduled) CloseModalBtn.Focus();
            else ModalPlayBtn.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void ModalPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMedia != null)
        {
            OnPlayRequested?.Invoke(this, _selectedMedia);
            ModalOverlay.Visibility = Visibility.Collapsed;
            RestoreFocus();
        }
    }

    private async void ModalRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMedia != null)
        {
            ModalRecordBtn.IsEnabled = false;
            
            bool success = await _viewModel.RecordEventAsync(_selectedMedia);
            
            if (success)
            {
                MessageBox.Show($"Successfully scheduled recording for '{_selectedMedia.Title}'.", "Recording Set", MessageBoxButton.OK, MessageBoxImage.Information);
                CloseModal_Click(null!, null!);
            }
            else
            {
                MessageBox.Show("Failed to set recording. Please check your connection to the DVR server.", "Recording Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                ModalRecordBtn.IsEnabled = true;
            }
        }
    }

    private async void DeleteModal_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMedia != null)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to permanently delete '{_selectedMedia.Title}' from the server?", 
                "Confirm Delete", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                ModalPlayBtn.IsEnabled = false;
                DeleteModalBtn.IsEnabled = false;
                
                bool success = await _viewModel.DeleteMediaAsync(_selectedMedia);
                
                if (!success)
                {
                    MessageBox.Show("Failed to delete the media. Please check your connection to the DVR server.", "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    ModalOverlay.Visibility = Visibility.Collapsed;
                    FocusFirstAvailableContentRow();
                }
                
                ModalPlayBtn.IsEnabled = true;
                DeleteModalBtn.IsEnabled = true;
            }
        }
    }

    private void CloseModal_Click(object sender, RoutedEventArgs e)
    {
        ModalOverlay.Visibility = Visibility.Collapsed;
        RestoreFocus(); 
    }

    private void RecordingsView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (ModalOverlay.Visibility == Visibility.Visible && command == HtpcCommand.Back)
        {
            CloseModal_Click(sender, e);
            e.Handled = true;
        }
        else if (FilterOverlay.Visibility == Visibility.Visible && command == HtpcCommand.Back)
        {
            CloseFilterOverlay();
            e.Handled = true;
        }
    }

    private void RestoreFocus()
    {
        if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
        {
            Keyboard.Focus(_lastFocusedElement);
        }
        else
        {
            if (_isDiscoverMode) DiscoverSearchBox.Focus();
            else FocusFirstAvailableContentRow();
        }
    }

    // --- HORIZONTAL SCROLLING HELPERS ---
    private void HorizontalList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentWidth == 0) return;

        if (e.HorizontalOffset + e.ViewportWidth >= e.ExtentWidth - 5)
        {
            if (sender == ActiveList) _viewModel.LoadMoreActive();
            else if (sender == ScheduledList) _viewModel.LoadMoreScheduled();
            else if (sender == RecentShowsList) _viewModel.LoadMoreShows();
            else if (sender == RecentMoviesList) _viewModel.LoadMoreMovies();
            else if (sender == ImportedMediaList) _viewModel.LoadMoreImports();
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
                    double step = e.Delta > 0 ? -1 : 1;
                    viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset + step);
                    e.Handled = true;
                }
            }
        }
        else
        {
            e.Handled = true;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender };
            MyRecordingsContainer.RaiseEvent(eventArg);
        }
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ListBox listBox)
        {
            var viewer = GetScrollViewer(listBox);
            if (viewer != null) viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - 4);
        }
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ListBox listBox)
        {
            var viewer = GetScrollViewer(listBox);
            if (viewer != null) viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset + 4);
        }
    }
    
    // --- REMOTE CONTROL SCROLLING MAPPING ---
    private void RecordingCard_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            if (element.IsMouseOver || Mouse.LeftButton == MouseButtonState.Pressed) return;

            try
            {
                if (ItemsControl.ItemsControlFromItemContainer(element) is ListBox listBox)
                {
                    listBox.ScrollIntoView(element.DataContext);
                }

                ScrollToElement(MyRecordingsContainer, element);
            }
            catch { }
        }
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

    // --- TOP NAVIGATION HANDLERS ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
	private void Sports_Click(object sender, RoutedEventArgs e) => OnSportsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
    private void Collections_Click(object sender, RoutedEventArgs e) => OnCollectionsRequested?.Invoke(this, EventArgs.Empty);
}
