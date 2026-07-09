using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Input;
using HTPC.Core.Models;
using HTPC.UI.ViewModels;

namespace HTPC.UI.Views;

public partial class RecordingsView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler? OnMultiviewRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;
	public event EventHandler? OnCollectionsRequested;

    private readonly RecordingsViewModel _viewModel;
    private MediaItem? _selectedMedia;
    private IInputElement? _lastFocusedElement;

    public RecordingsView(RecordingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += OnLoaded;
        PreviewKeyDown += RecordingsView_PreviewKeyDown;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadRecordingsAsync();

        // 10-foot UI Best Practice: Force focus directly into the first available content row.
        // This prevents the user from starting trapped on the Window root or Top Nav.
        _ = Dispatcher.InvokeAsync(() => 
        {
            FocusFirstAvailableContentRow();
        }, DispatcherPriority.Loaded);
    }

    // --- NAVIGATION BRIDGING FIXES ---
    
    private void TopNavPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Down)
        {
            // Explicitly route focus down into the lists, bypassing the spatial layout gap
            e.Handled = true;
            FocusFirstAvailableContentRow();
        }
        else if (command == HtpcCommand.Up)
        {
            // Block the focus engine from wrapping around to the bottom of the page
            e.Handled = true; 
        }
    }

    private void FocusFirstAvailableContentRow()
    {
        // Waterfall through the lists. The first one with visible content gets focus.
        if (TryFocusFirstListBoxItem(ActiveList)) return;
        if (TryFocusFirstListBoxItem(ScheduledList)) return;
        if (TryFocusFirstListBoxItem(RecentShowsList)) return;
        if (TryFocusFirstListBoxItem(RecentMoviesList)) return;
        TryFocusFirstListBoxItem(ImportedMediaList);
    }

    private bool TryFocusFirstListBoxItem(ListBox listBox)
    {
        // If the row is hidden (due to our empty state triggers) or empty, skip it
        if (listBox.Visibility != Visibility.Visible || listBox.Items.Count == 0) return false;
        
        // Attempt to get the container if it's already generated
        var element = listBox.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
        if (element != null)
        {
            return element.Focus();
        }
        
        // If virtualized and not realized in the visual tree yet, force a layout pass
        listBox.UpdateLayout();
        element = listBox.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
        
        return element?.Focus() ?? false;
    }

    // --- MODAL LOGIC ---
    
    private void ListBoxItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        // Explicitly escape the horizontal ListBox on Up/Down commands so focus can jump rows
        if (command == HtpcCommand.Down || command == HtpcCommand.Up)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            if (sender is UIElement uiElement)
            {
                // Here we CAN use MoveFocus because we are moving spatially between stacked vertical rows
                uiElement.MoveFocus(new TraversalRequest(direction));
                e.Handled = true;
            }
            return;
        }

        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem media)
        {
            ShowModal(media);
            e.Handled = true; 
        }
    }

    private void RecordingCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem media)
        {
            ShowModal(media);
            e.Handled = true;
        }
    }

    private void ShowModal(MediaItem media)
    {
        _lastFocusedElement = Keyboard.FocusedElement;
        _selectedMedia = media;
        
        ModalTitle.Text = string.IsNullOrWhiteSpace(media.CurrentShowTitle) ? media.Title : media.CurrentShowTitle;
        ModalSubTitle.Text = string.IsNullOrWhiteSpace(media.CurrentShowTitle) ? "" : media.Title;
        ModalSummary.Text = string.IsNullOrWhiteSpace(media.Summary) ? "No description available." : media.Summary;

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
            if (media.IsScheduled) CloseModalBtn.Focus();
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
                    // Fallback to auto-focus logic since the previous element is now deleted
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
    }

    private void RestoreFocus()
    {
        if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
        {
            Keyboard.Focus(_lastFocusedElement);
        }
        else
        {
            FocusFirstAvailableContentRow();
        }
    }

    // --- HORIZONTAL SCROLLING HELPERS ---
    
    private void HorizontalList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentWidth == 0) return;

        // In Logical Scrolling:
        // e.ExtentWidth = Total number of loaded items in the list (e.g., 25)
        // e.ViewportWidth = Number of items visible on screen (e.g., 5)
        // e.HorizontalOffset = The index of the first visible item

        // Trigger the next chunk load when the user is within 5 items of the right edge.
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
                    // Scroll by 1 item per mouse wheel tick for smooth logical navigation
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
            MainScroll.RaiseEvent(eventArg);
        }
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ListBox listBox)
        {
            var viewer = GetScrollViewer(listBox);
            // Move left by 4 items (Logical Scrolling)
            if (viewer != null) viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - 4);
        }
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ListBox listBox)
        {
            var viewer = GetScrollViewer(listBox);
            // Move right by 4 items (Logical Scrolling)
            if (viewer != null) viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset + 4);
        }
    }
	
	// --- REMOTE CONTROL SCROLLING MAPPING ---
    
   private void RecordingCard_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
{
    if (sender is FrameworkElement element)
    {
        // PREVENT MOUSE CLICK RACE CONDITION:
        // If the mouse is hovering over the element or actively clicking it, 
        // do NOT auto-scroll. Leave the physical layout alone so the click can complete.
        if (element.IsMouseOver || Mouse.LeftButton == MouseButtonState.Pressed) 
        {
            return;
        }

        try
        {
            if (ItemsControl.ItemsControlFromItemContainer(element) is ListBox listBox)
            {
                listBox.ScrollIntoView(element.DataContext);
            }

            ScrollToElement(MainScroll, element);
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
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
	private void Collections_Click(object sender, RoutedEventArgs e) => OnCollectionsRequested?.Invoke(this, EventArgs.Empty);
}