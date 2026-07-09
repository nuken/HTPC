using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HTPC.Core.Input;
using HTPC.Core.Models;
using HTPC.UI.ViewModels;

namespace HTPC.UI.Views;

public partial class CollectionsView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMultiviewRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnRecordingsRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler? OnExitRequested;
    public event EventHandler<MediaItem>? OnPlayRequested;
	public event EventHandler<(System.Collections.Generic.List<MediaItem> Queue, int StartIndex)>? OnPlayQueueRequested;
	private System.Collections.Generic.List<MediaItem> _allEpisodesForSelectedShow = new();
	
    private readonly CollectionsViewModel _viewModel;
    private IInputElement? _lastFocusedElement;

    public CollectionsView(CollectionsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += OnLoaded;
		this.PreviewKeyDown += CollectionsView_PreviewKeyDown;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ContentModal.Visibility = Visibility.Collapsed;
        EpisodesOverlay.Visibility = Visibility.Collapsed;
		await _viewModel.LoadCollectionsAsync();

        _ = Dispatcher.InvokeAsync(() => 
        {
            var element = MovieCollectionsList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
            element?.Focus();
        }, DispatcherPriority.Loaded);
    }

// --- TOP NAVIGATION HANDLERS ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void NavMultiview_Click(object sender, RoutedEventArgs e) => OnMultiviewRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Recordings_Click(object sender, RoutedEventArgs e) => OnRecordingsRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
    private void ExitApp_Click(object sender, RoutedEventArgs e) => OnExitRequested?.Invoke(this, EventArgs.Empty);
    // --- COLLECTION SELECTION (Opens Modal) ---
    
    private void CollectionCard_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            if (element.IsMouseOver || Mouse.LeftButton == MouseButtonState.Pressed) return;

            try
            {
                if (ItemsControl.ItemsControlFromItemContainer(element) is ListBox listBox)
                    listBox.ScrollIntoView(element.DataContext);

                // Auto scroll main window
                var transform = element.TransformToAncestor(MainScroll.Content as UIElement);
                Point position = transform.Transform(new Point(0, 0));
                double targetY = position.Y - 100;
                MainScroll.ScrollToVerticalOffset(targetY < 0 ? 0 : targetY);
            }
            catch { }
        }
    }

    private async void CollectionCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is CollectionItem collection)
        {
            await OpenCollectionModal(collection);
            e.Handled = true;
        }
    }
	
	// --- BINGE WATCH QUEUE BUILDERS ---

    private async void BingeMovies_Click(object sender, RoutedEventArgs e)
    {
        var queue = new System.Collections.Generic.List<MediaItem>();
        foreach (var item in CollectionContentList.Items)
        {
            if (item is MediaItem media) 
            {
                queue.Add(media);
            }
        }
        
        if (queue.Count == 0) return;

        // 1. Detect if this collection contains TV Shows instead of Movies
        bool isSeriesCollection = false;
        foreach (var item in queue)
        {
            if (item.Categories != null && (item.Categories.Contains("Show", StringComparer.OrdinalIgnoreCase) || item.Categories.Contains("Series", StringComparer.OrdinalIgnoreCase)))
            {
                isSeriesCollection = true;
                break;
            }
        }

        if (isSeriesCollection)
        {
            // We need to fetch the actual episodes for every show in this collection!
            string originalTitle = ModalTitle.Text;
            
            // Give the user visual feedback since fetching multiple shows takes a second
            ModalTitle.Text = "Building Binge Queue..."; 
            
            var masterEpisodesQueue = new System.Collections.Generic.List<MediaItem>();
            
            foreach (var show in queue)
            {
                // Fetch episodes using the ViewModel method we created earlier
                var episodes = await _viewModel.GetShowEpisodesAsync(show.Id);
                if (episodes != null)
                {
                    // Extract only the unwatched episodes and add them to the master queue
                    foreach (var ep in episodes)
                    {
                        if (!ep.IsWatched) masterEpisodesQueue.Add(ep);
                    }
                }
            }
            
            if (masterEpisodesQueue.Count == 0)
            {
                ModalTitle.Text = "No unwatched episodes found.";
                await System.Threading.Tasks.Task.Delay(2000);
                ModalTitle.Text = originalTitle;
                return;
            }
            
            ContentModal.Visibility = Visibility.Collapsed;
            ModalTitle.Text = originalTitle; // Reset the title for next time
            
            // Send the massive cross-show queue to the player
            OnPlayQueueRequested?.Invoke(this, (masterEpisodesQueue, 0));
        }
        else
        {
            // 2. Standard Movie Collection Logic
            int startIndex = queue.FindIndex(m => !m.IsWatched);
            if (startIndex == -1) startIndex = 0;

            ContentModal.Visibility = Visibility.Collapsed;
            
            // Send the movie queue to the player
            OnPlayQueueRequested?.Invoke(this, (queue, startIndex));
        }
    }

    // --- BINGE WATCH TV SHOW QUEUE ENGINE ---

    private void BingeShow_Click(object sender, RoutedEventArgs e)
    {
        if (_allEpisodesForSelectedShow == null || _allEpisodesForSelectedShow.Count == 0) return;

        int firstUnwatchedIndex = _allEpisodesForSelectedShow.FindIndex(ep => !ep.IsWatched);
        
        // SMART PROMPT LOGIC: 
        // If index is > 0, they are in the middle of a show. Ask what they want to do.
        if (firstUnwatchedIndex > 0)
        {
            BingeChoiceOverlay.Visibility = Visibility.Visible;
            
            // Wait for WPF to completely finish drawing the popup before snatching focus
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                BingeResumeBtn.Focus();
                Keyboard.Focus(BingeResumeBtn); // Forcefully snatch hardware focus
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
        else
        {
            // If index is 0 (never watched) or -1 (completely finished), skip prompt and start at episode 1
            LaunchBingeQueue(0);
        }
    }

    private void BingeResume_Click(object sender, RoutedEventArgs e)
    {
        int startIndex = _allEpisodesForSelectedShow.FindIndex(ep => !ep.IsWatched);
        LaunchBingeQueue(startIndex);
    }

    private void BingeBeginning_Click(object sender, RoutedEventArgs e)
    {
        LaunchBingeQueue(0);
    }

    private void LaunchBingeQueue(int startIndex)
    {
        // Hide all layers of overlays
        BingeChoiceOverlay.Visibility = Visibility.Collapsed;
        EpisodesOverlay.Visibility = Visibility.Collapsed;
        ContentModal.Visibility = Visibility.Collapsed;
        
        // Send the queue to the player
        OnPlayQueueRequested?.Invoke(this, (_allEpisodesForSelectedShow, startIndex));
    }

    private void BingeCancel_Click(object sender, RoutedEventArgs e)
    {
        BingeChoiceOverlay.Visibility = Visibility.Collapsed;
        
        // Return focus back to the original Binge button if it exists
        var bingeBtn = EpisodesOverlay.FindName("BingeShowBtn") as UIElement;
        bingeBtn?.Focus(); 
    }

    // --- 10-FOOT UI ROUTING FOR BINGE PROMPT ---
    private void BingeChoice_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = HTPC.Core.Input.InputMapper.GetCommand(e.Key);
        
        if (command == HTPC.Core.Input.HtpcCommand.Back || command == HTPC.Core.Input.HtpcCommand.Left)
        {
            BingeCancel_Click(null!, null!);
            e.Handled = true;
        }
        else if (command == HTPC.Core.Input.HtpcCommand.Up || command == HtpcCommand.Down)
        {
            var direction = command == HTPC.Core.Input.HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as Button)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
        else if (command == HTPC.Core.Input.HtpcCommand.Right)
        {
            e.Handled = true; // Prevent focus from flying off to the right side of the screen
        }
    }

    private async System.Threading.Tasks.Task OpenCollectionModal(CollectionItem collection)
    {
        _lastFocusedElement = Keyboard.FocusedElement;
        ModalTitle.Text = collection.Name;
        
        // Fetch contents
        var mediaItems = await _viewModel.GetCollectionContentsAsync(collection.Id);
        CollectionContentList.ItemsSource = mediaItems;
        
        ContentModal.Visibility = Visibility.Visible;

        _ = Dispatcher.InvokeAsync(() => 
        {
            if (CollectionContentList.Items.Count > 0)
            {
                var element = CollectionContentList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                element?.Focus();
            }
            else
            {
                CloseModalBtn.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    // --- MEDIA ITEM SELECTION (Inside Modal) ---

    private void MediaCard_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            if (element.IsMouseOver || Mouse.LeftButton == MouseButtonState.Pressed) return;
            if (ItemsControl.ItemsControlFromItemContainer(element) is ListBox listBox)
                listBox.ScrollIntoView(element.DataContext);
        }
    }

    private void MediaCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem media)
        {
            PlaySelectedMedia(media);
            e.Handled = true;
        }
    }

    // --- REMOTE CONTROL & D-PAD HARDWARE ROUTING ---

    private void TopNavPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = HTPC.Core.Input.InputMapper.GetCommand(e.Key);
        bool isDown = command == HTPC.Core.Input.HtpcCommand.Down || e.Key == Key.Down;

        // If the user is on the Top Nav and presses DOWN, force focus into the grid
        if (isDown)
        {
            if (MovieCollectionsList.Items.Count > 0)
            {
                FocusItemInList(MovieCollectionsList);
                e.Handled = true;
            }
            else if (ShowCollectionsList.Items.Count > 0)
            {
                FocusItemInList(ShowCollectionsList);
                e.Handled = true;
            }
        }
    }
	
	// --- MASTER REMOTE BACK HANDLER ---
    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
		
        if (BingeChoiceOverlay.Visibility == Visibility.Visible) return;
        
        if (command == HtpcCommand.Back || e.Key == Key.Escape || e.Key == Key.BrowserBack || e.Key == Key.Back)
        {
            // Layer 1: We are inside a Show's Season/Episode list
            if (EpisodesOverlay.Visibility == Visibility.Visible)
            {
                CloseEpisodesOverlay_Click(null!, null!);
                
                // CRITICAL: Stop MainWindow from sending you to the Dashboard
                e.Handled = true; 
            }
            // Layer 2: We are inside the Collection Content list
            else if (ContentModal.Visibility == Visibility.Visible)
            {
                CloseModal_Click(null!, null!);
                
                // CRITICAL: Stop MainWindow from sending you to the Dashboard
                e.Handled = true; 
            }
            
        }
    }

    private void CollectionCard_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = HTPC.Core.Input.InputMapper.GetCommand(e.Key);
        bool isSelect = command == HTPC.Core.Input.HtpcCommand.Select || e.Key == Key.Enter;
        bool isUp = command == HTPC.Core.Input.HtpcCommand.Up || e.Key == Key.Up;
        bool isDown = command == HTPC.Core.Input.HtpcCommand.Down || e.Key == Key.Down;

        if (!(sender is ListBoxItem item) || !(item.DataContext is CollectionItem collection)) return;

        // 1. Handle "Select" / Enter to open the Collection Modal
        if (isSelect)
        {
            _ = OpenCollectionModal(collection);
            e.Handled = true;
            return;
        }

        // Determine exactly which row we are on by checking the ViewModel data directly
        bool isMovie = _viewModel.MovieCollections.Contains(collection);
        bool isShow = _viewModel.ShowCollections.Contains(collection);

        // 2. Handle UP
        if (isUp)
        {
            if (isMovie) 
            {
                // We are on the top row, escape to Top Nav
                FocusTopNav();
                e.Handled = true;
            }
            else if (isShow)
            {
                // We are on the bottom row, jump to Movie row if it has items, otherwise Top Nav
                if (MovieCollectionsList.Items.Count > 0)
                {
                    FocusItemInList(MovieCollectionsList);
                    e.Handled = true;
                }
                else
                {
                    FocusTopNav();
                    e.Handled = true;
                }
            }
        }
        // 3. Handle DOWN
        else if (isDown)
        {
            if (isMovie && ShowCollectionsList.Items.Count > 0)
            {
                // Jump from Movie row to Show row
                FocusItemInList(ShowCollectionsList);
                e.Handled = true;
            }
        }
    }

    private void MediaCard_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        bool isUp = command == HtpcCommand.Up || e.Key == Key.Up;

        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem media)
        {
            PlaySelectedMedia(media);
            e.Handled = true;
        }
        // FIX: Force focus to the Back button if pressing Up
        else if (isUp)
        {
            CloseModalBtn.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Back || e.Key == Key.Escape)
        {
            CloseModal_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void CloseModalBtn_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        // Push focus DOWN from the back button into the list
        if (command == HtpcCommand.Down || e.Key == Key.Down)
        {
            if (CollectionContentList.Items.Count > 0)
            {
                var element = CollectionContentList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                element?.Focus();
                e.Handled = true;
            }
        }
    }

    // --- FOCUS HELPERS ---

    private void FocusTopNav()
    {
        foreach (UIElement child in TopNavPanel.Children)
        {
            if (child is Button btn && btn.Focusable)
            {
                btn.Focus();
                return;
            }
        }
    }

    private void FocusItemInList(ListBox list)
    {
        // Try to keep horizontal position, otherwise default to first item
        int index = Math.Max(0, list.SelectedIndex);
        
        if (list.ItemContainerGenerator.ContainerFromIndex(index) as UIElement is UIElement target)
        {
            target.Focus();
        }
        else
        {
            // Failsafe if virtualized item isn't ready
            list.UpdateLayout();
            if (list.ItemContainerGenerator.ContainerFromIndex(index) as UIElement is UIElement fallbackTarget)
            {
                fallbackTarget.Focus();
            }
        }
    }
    
    
    private void CloseModal_Click(object sender, RoutedEventArgs e)
    {
        ContentModal.Visibility = Visibility.Collapsed;
        if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
            Keyboard.Focus(_lastFocusedElement);
    }
	
	// --- UPDATED MEDIA CLICK HANDLER ---

    private void PlaySelectedMedia(MediaItem media)
    {
        bool isSeries = media.Categories != null && 
                       (media.Categories.Contains("Show", StringComparer.OrdinalIgnoreCase) || 
                        media.Categories.Contains("Series", StringComparer.OrdinalIgnoreCase));

        if (isSeries)
        {
            // Open the new Seasons/Episodes view
            OpenShowOverlay(media);
        }
        else
        {
            // It's a Movie, play it directly
            ContentModal.Visibility = Visibility.Collapsed;
            EpisodesOverlay.Visibility = Visibility.Collapsed;
            OnPlayRequested?.Invoke(this, media);
            
            if (_lastFocusedElement is UIElement uiElement && uiElement.IsVisible)
                Keyboard.Focus(_lastFocusedElement);
        }
    }

    // --- SEASONS / EPISODES OVERLAY LOGIC ---

    private async void OpenShowOverlay(MediaItem show)
    {
        try
        {
            OverlayShowTitle.Text = show.Title;
            OverlayShowSummary.Text = string.IsNullOrEmpty(show.Summary) ? "No summary available." : show.Summary;
            
            try 
            { 
                if (!string.IsNullOrWhiteSpace(show.PosterUrl))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(show.PosterUrl, UriKind.RelativeOrAbsolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; 
                    bmp.EndInit();
                    OverlayShowPoster.Source = bmp;
                }
                else OverlayShowPoster.Source = null;
            } 
            catch { OverlayShowPoster.Source = null; }

            SeasonsList.SelectedIndex = -1;
            _viewModel.CurrentEpisodes.Clear();
            _viewModel.Seasons.Clear();

            // Fetch episodes using the newly created API method
            _allEpisodesForSelectedShow = await _viewModel.GetShowEpisodesAsync(show.Id) ?? new System.Collections.Generic.List<MediaItem>();

            if (_allEpisodesForSelectedShow.Count > 0)
            {
                var uniqueSeasons = System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(_allEpisodesForSelectedShow, ep => ep.SeasonNumber)), s => s).ToList();
                foreach (var s in uniqueSeasons) _viewModel.Seasons.Add(s);
            }

            EpisodesOverlay.Visibility = Visibility.Visible;
            if (_viewModel.Seasons.Count > 0) SeasonsList.SelectedIndex = 0;

            // Push focus to the Seasons List
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                if (SeasonsList.Items.Count > 0)
                {
                    var firstSeason = SeasonsList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                    firstSeason?.Focus();
                }
            }), DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading episodes: {ex.Message}");
        }
    }

    private void SeasonsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeasonsList.SelectedItem is int selectedSeason)
        {
            _viewModel.CurrentEpisodes.Clear();
            var episodesForSeason = System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Where(_allEpisodesForSelectedShow, ep => ep.SeasonNumber == selectedSeason), ep => ep.EpisodeNumber);
            foreach (var ep in episodesForSeason) _viewModel.CurrentEpisodes.Add(ep);
        }
    }

    private void CloseEpisodesOverlay_Click(object sender, RoutedEventArgs e)
    {
        EpisodesOverlay.Visibility = Visibility.Collapsed;
        
        // Return focus back to the first item in the collection content list
        if (CollectionContentList.Items.Count > 0)
        {
            var firstItem = CollectionContentList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
            firstItem?.Focus();
        }
        else
        {
            CloseModalBtn.Focus();
        }
    }

    // --- HARDWARE ROUTING FOR EPISODES OVERLAY ---

   private void SeasonItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Right)
        {
            if (_viewModel.CurrentEpisodes.Count > 0)
            {
                EpisodesList.UpdateLayout();
                var firstEp = EpisodesList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                if (firstEp != null) firstEp.Focus();
                else EpisodesList.Focus();
            }
            e.Handled = true;
        }
        else if (command == HtpcCommand.Back)
        {
            CloseEpisodesOverlay_Click(null!, null!);
            e.Handled = true;
        }
        else if (command == HtpcCommand.Left) 
        {
            // FOCUS BRIDGE: Jump Left to the Action Buttons
            BingeShowBtn.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up)
        {
            if (SeasonsList.SelectedIndex <= 0)
            {
                CloseEpisodesBtn.Focus();
            }
            else
            {
                (sender as ListBoxItem)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
            }
            e.Handled = true;
        }
        else if (command == HtpcCommand.Down)
        {
            (sender as ListBoxItem)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
            e.Handled = true;
        }
    }

    // --- 10-FOOT UI ROUTING FOR EPISODE OVERLAY BUTTONS ---
    private void EpisodesOverlayButtons_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Right)
        {
            // FOCUS BRIDGE: Jump Right into the Seasons List
            if (SeasonsList.Items.Count > 0)
            {
                SeasonsList.UpdateLayout();
                int targetIndex = Math.Max(0, SeasonsList.SelectedIndex);
                var seasonItem = SeasonsList.ItemContainerGenerator.ContainerFromIndex(targetIndex) as UIElement;
                seasonItem?.Focus();
            }
            e.Handled = true;
        }
        else if (command == HtpcCommand.Back || command == HtpcCommand.Left)
        {
            // Escape the overlay and go back to the collection content list
            CloseEpisodesOverlay_Click(null!, null!);
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up && sender == BingeShowBtn)
        {
            // Explicitly force focus up to the Back button
            CloseEpisodesBtn.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Down && sender == CloseEpisodesBtn)
        {
            // Explicitly force focus down to the Binge button
            BingeShowBtn.Focus();
            e.Handled = true;
        }
        // --- NEW: FOCUS TRAPS ---
        else if (command == HtpcCommand.Down && sender == BingeShowBtn)
        {
            // Block WPF from throwing focus into the empty space below the poster
            e.Handled = true; 
        }
        else if (command == HtpcCommand.Up && sender == CloseEpisodesBtn)
        {
            // Block WPF from throwing focus into the empty space above the buttons
            e.Handled = true; 
        }
    }

    private void EpisodeItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is MediaItem episode)
        {
            PlaySelectedMedia(episode);
            e.Handled = true;
        }
        else if (command == HtpcCommand.Left)
        {
            SeasonsList.UpdateLayout(); 
            if (SeasonsList.SelectedItem != null)
            {
                var seasonItem = SeasonsList.ItemContainerGenerator.ContainerFromItem(SeasonsList.SelectedItem) as UIElement;
                seasonItem?.Focus();
            }
            else SeasonsList.Focus();
            e.Handled = true;
        }
        else if (command == HtpcCommand.Up || command == HtpcCommand.Down)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : FocusNavigationDirection.Up;
            (sender as ListBoxItem)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true;
        }
        else if (command == HtpcCommand.Right) e.Handled = true; 
    }

    private void EpisodeCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is MediaItem episode)
        {
            PlaySelectedMedia(episode);
        }
    }
	
	// --- MASTER REMOTE BACK HANDLER ---
    private void CollectionsView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (command == HtpcCommand.Back || e.Key == Key.Escape)
        {
            // 1. If we are deep inside a Show's Season/Episode list
            if (EpisodesOverlay.Visibility == Visibility.Visible)
            {
                CloseEpisodesOverlay_Click(null!, null!);
                e.Handled = true;
            }
            // 2. If we are inside the Collection Content list
            else if (ContentModal.Visibility == Visibility.Visible)
            {
                CloseModal_Click(null!, null!);
                e.Handled = true;
            }
            // 3. Otherwise, let it pass through to go back to the Dashboard
        }
    }
}