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

public partial class SportsView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMultiviewRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnCollectionsRequested;
    public event EventHandler? OnRecordingsRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;

    private readonly SportsViewModel _viewModel;
    private MediaItem? _selectedMedia;
    private IInputElement? _lastFocusedElement;

    public SportsView(SportsViewModel viewModel)
{
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = _viewModel;

    Loaded += OnLoaded;
    PreviewKeyDown += SportsView_PreviewKeyDown;
    
    // --- NEW: ZOMBIE OVERLAY CLEANUP ---
    IsVisibleChanged += SportsView_IsVisibleChanged;
}

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeToggleBtn.Content = PreferencesManager.LoadTheme() == "Dark" ? "\xE708" : "\xE706";
        
        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            TopNavPanel.Children[7].Focus(); // Focus the 'Sports' pill
        }), DispatcherPriority.ApplicationIdle);

        await _viewModel.LoadSportsAsync();
    }

    // --- TOP NAVIGATION ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Collections_Click(object sender, RoutedEventArgs e) => OnCollectionsRequested?.Invoke(this, EventArgs.Empty);
    private void Recordings_Click(object sender, RoutedEventArgs e) => OnRecordingsRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void ThemeToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        string currentTheme = PreferencesManager.LoadTheme();
        string newTheme = currentTheme == "Dark" ? "Light" : "Dark";
        PreferencesManager.SaveTheme(newTheme);
        ((App)Application.Current).ApplyTheme(newTheme);
        ThemeToggleBtn.Content = newTheme == "Dark" ? "\xE708" : "\xE706";
    }

    // --- FILTER BAR LOGIC ---
    private void AddSportBtn_Click(object sender, RoutedEventArgs e)
    {
        // NEW: Dynamically filter the list so it only shows sports that haven't been added yet
        var availableToAdd = _viewModel.AvailableGenres
            .Where(g => !_viewModel.ActiveGenreFilters.Contains(g))
            .OrderBy(g => g)
            .ToList();

        FilterSelectionList.ItemsSource = availableToAdd;
        FilterOverlay.Visibility = Visibility.Visible;
        _lastFocusedElement = Keyboard.FocusedElement;

        _ = Dispatcher.InvokeAsync(() =>
        {
            if (FilterSelectionList.Items.Count > 0)
            {
                var item = FilterSelectionList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                item?.Focus();
            }
            else
            {
                // If there are no sports left to add, focus the new Close button
                CloseFilterBtn.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    // NEW: Close Button Handler
    private void CloseFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        CloseFilterOverlay();
    }
    
	private void RemoveFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string genre)
        {
            _viewModel.ToggleGenreFilter(genre);
        }
    }

    private void ProcessFilterSelection(object selectedItem)
    {
        if (selectedItem is string genre)
        {
            _viewModel.ToggleGenreFilter(genre);
            FilterOverlay.Visibility = Visibility.Collapsed;
            
            if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
                Keyboard.Focus(uiElement);
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
            FilterOverlay.Visibility = Visibility.Collapsed;
            if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible) Keyboard.Focus(uiElement);
            e.Handled = true;
        }
    }
	
	private void SportsView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
{
    if (!(bool)e.NewValue) 
    {
        // The view is hiding (user navigated away)
        if (EventModalOverlay.Visibility == Visibility.Visible)
        {
            EventModalOverlay.Visibility = Visibility.Collapsed;
        }
        
        // Also clean up the Add Sport filter overlay just in case!
        if (FilterOverlay.Visibility == Visibility.Visible)
        {
            FilterOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
private void MainScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Detect if we are nearing the bottom of the scroll area
        if (MainScroll.VerticalOffset >= MainScroll.ScrollableHeight - 200)
        {
            _viewModel.LoadMoreLive(); // Just in case there's a massive NFL Sunday block
            _viewModel.LoadMoreUpcoming();
        }
    }

    // --- FOCUS BRIDGING ---
    private void AddSportBtn_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Up)
        {
            TopNavPanel.Children[7].Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Down)
        {
            TryFocusFirstListBoxItem(LiveEventsGrid);
            e.Handled = true;
        }
    }

    private void FilterPill_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Up)
        {
            TopNavPanel.Children[7].Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Down)
        {
            TryFocusFirstListBoxItem(LiveEventsGrid);
            e.Handled = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Up)
        {
            TopNavPanel.Children[7].Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Down)
        {
            TryFocusFirstListBoxItem(LiveEventsGrid);
            e.Handled = true;
        }
    }

    private void ListBoxItem_PreviewKeyDown(object sender, KeyEventArgs e)
{
    var command = InputMapper.GetCommand(e.Key);
    if (sender is ListBoxItem item && item.DataContext is MediaItem media)
    {
        if (command == HtpcCommand.Select)
        {
            OpenEventModal(media);
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up)
        {
            ItemsControl parentGrid = ItemsControl.ItemsControlFromItemContainer(item);
            int index = parentGrid.ItemContainerGenerator.IndexFromContainer(item);

            // If pressing UP from the top row (first 6 items), bridge upwards
            if (index >= 0 && index < 6) 
            {
                if (parentGrid == UpcomingEventsGrid && LiveEventsGrid.Items.Count > 0)
                {
                    TryFocusFirstListBoxItem(LiveEventsGrid);
                }
                else
                {
                    AddSportBtn.Focus();
                }
                e.Handled = true;
            }
            else
            {
                // Force standard UP movement within the grid
                (sender as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
                e.Handled = true;
            }
        }
        else if (command == HtpcCommand.Down)
        {
            ItemsControl parentGrid = ItemsControl.ItemsControlFromItemContainer(item);
            
            // Try to move down natively first
            bool moved = (sender as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down)) ?? false;
            
            // If focus didn't move, we are trapped at the bottom of the Live grid. 
            // Manually bridge the gap to the Upcoming grid.
            if (!moved && parentGrid == LiveEventsGrid && UpcomingEventsGrid.Items.Count > 0)
            {
                TryFocusFirstListBoxItem(UpcomingEventsGrid);
            }
            e.Handled = true;
        }
    }
}

    private bool TryFocusFirstListBoxItem(ListBox listBox)
    {
        if (listBox.Visibility != Visibility.Visible || listBox.Items.Count == 0) return false;
        listBox.UpdateLayout();
        var element = listBox.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
        return element?.Focus() ?? false;
    }

    // --- EVENT MODAL LOGIC ---
    private void EventCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem media)
        {
            OpenEventModal(media);
        }
    }

    private void OpenEventModal(MediaItem media)
    {
        _lastFocusedElement = Keyboard.FocusedElement;
        _selectedMedia = media;

        ModalTitle.Text = string.IsNullOrWhiteSpace(media.Title) ? "Unknown Event" : media.Title;
        ModalNetwork.Text = string.IsNullOrWhiteSpace(media.ChannelName) ? "Unknown Network" : media.ChannelName;
        ModalSummary.Text = string.IsNullOrWhiteSpace(media.Summary) ? "No description available." : media.Summary;
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

        // SAFTEY FIX: Use exact timestamps instead of strings to determine if the game is live
        bool isLive = media.StartTime <= DateTime.Now && DateTime.Now < media.EndTime;
        
        // Force the UI text to say "Airing Now" if it's live, overriding whatever the backend sent
        ModalTime.Text = isLive ? "Airing Now" : media.DisplayTime;

        // Show the Tune In button
        TuneInBtn.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;

        EventModalOverlay.Visibility = Visibility.Visible;

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (isLive) TuneInBtn.Focus();
            else RecordBtn.Focus();
        }), DispatcherPriority.ContextIdle);
    }

    private void TuneIn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMedia != null)
        {
            EventModalOverlay.Visibility = Visibility.Collapsed;
            OnPlayRequested?.Invoke(this, _selectedMedia);
        }
    }

    private async void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMedia != null)
        {
            RecordBtn.IsEnabled = false;
            bool success = await _viewModel.RecordEventAsync(_selectedMedia);

            if (success)
            {
                MessageBox.Show($"Successfully scheduled recording for '{_selectedMedia.Title}'.", "Recording Set", MessageBoxButton.OK, MessageBoxImage.Information);
                CloseModal_Click(null!, null!);
            }
            else
            {
                MessageBox.Show("Failed to set recording. Please check your connection to the DVR server.", "Recording Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            RecordBtn.IsEnabled = true;
        }
    }
	
	private void SportsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
{
    // Stop the ListBox from swallowing the scroll event
    e.Handled = true;
    
    // Re-raise the event on the parent ScrollViewer
    var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
    {
        RoutedEvent = UIElement.MouseWheelEvent,
        Source = sender
    };
    MainScroll.RaiseEvent(eventArg);
}

private void TopNavPanel_PreviewKeyDown(object sender, KeyEventArgs e)
{
    var command = InputMapper.GetCommand(e.Key);
    
    if (command == HtpcCommand.Down)
    {
        // Explicitly route focus down into the active content area
        AddSportBtn.Focus();
        e.Handled = true;
    }
    else if (command == HtpcCommand.Left || command == HtpcCommand.Right)
    {
        // Force strict left/right movement across the menu pills
        var direction = command == HtpcCommand.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right;
        (e.OriginalSource as FrameworkElement)?.MoveFocus(new TraversalRequest(direction));
        e.Handled = true;
    }
}

    private void CloseModal_Click(object sender, RoutedEventArgs e)
    {
        EventModalOverlay.Visibility = Visibility.Collapsed;
        if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
            Keyboard.Focus(uiElement);
    }
	
	private void EventModalButtons_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        // The XAML "Cycle" property natively handles Up/Down wrapping. 
        // We just need to trap horizontal movement so focus cannot escape the modal to the background grids.
        if (command == HtpcCommand.Left || command == HtpcCommand.Right)
        {
            e.Handled = true;
        }
    }

    // --- MASTER BACK HANDLER ---
    private void SportsView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        if (command == HtpcCommand.Back || e.Key == Key.Escape)
        {
            if (FilterOverlay.Visibility == Visibility.Visible)
            {
                CloseFilterOverlay();
                e.Handled = true;
            }
            else if (EventModalOverlay.Visibility == Visibility.Visible)
            {
                CloseModal_Click(sender, e);
                e.Handled = true;
            }
        }
    }

    private void CloseFilterOverlay()
    {
        FilterOverlay.Visibility = Visibility.Collapsed;
        if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
            Keyboard.Focus(uiElement);
    }
}