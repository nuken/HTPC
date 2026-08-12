using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HTPC.Core.Input; 
using HTPC.Core.Models;
using HTPC.Services;
using System.Windows.Threading;

namespace HTPC.UI.Views;

public partial class MultiviewSetupView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnRecordingsRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnSettingsRequested;
    public event EventHandler? OnCollectionsRequested;
   
    public event EventHandler<List<Channel>>? OnLaunchMultiviewRequested;
    
    private readonly MediaLibraryService _libraryService;
    public ObservableCollection<Channel> DisplayedChannels { get; set; } = new ObservableCollection<Channel>();
    
    private List<Channel> _allChannels = new List<Channel>();
    private List<ChannelCollection> _collections = new List<ChannelCollection>();
    private Channel?[] _selectedChannels = new Channel?[4];
    private readonly DispatcherTimer _autoRefreshTimer;
    
    // --- Overlay Filter Variables ---
    private string _activeCollectionName = "All Channels";
    private List<string> _availableCollections = new();
    private IInputElement? _lastFocusedElement;
    
    public MultiviewSetupView(MediaLibraryService libraryService)
    {
        InitializeComponent();
        _libraryService = libraryService;
        ChannelItemsControl.ItemsSource = DisplayedChannels;
        this.Loaded += OnLoaded;
        this.IsVisibleChanged += MultiviewSetupView_IsVisibleChanged; 

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

        Channel? focusedChannel = (Keyboard.FocusedElement as ListBoxItem)?.DataContext as Channel;

        ChannelCollection? targetCollection = null;
        if (selection != "All Channels" && selection != "Favorites" && selection != "HD Channels")
            targetCollection = _collections.FirstOrDefault(c => c.Name == selection);

        var channels = await _libraryService.GetGuideChannelsAsync(targetCollection, 4);

        if (selection == "Favorites") channels = channels.Where(c => c.Favorite).ToList();
        else if (selection == "HD Channels") channels = channels.Where(c => c.IsHD).ToList();

        _allChannels = channels.Where(c => !c.Hidden).ToList();
        
        DisplayedChannels.Clear();
        foreach (var c in _allChannels) DisplayedChannels.Add(c);

        if (focusedChannel != null)
        {
            var newTarget = DisplayedChannels.FirstOrDefault(c => c.Number == focusedChannel.Number);
            if (newTarget != null)
            {
                ChannelItemsControl.UpdateLayout();
                var row = ChannelItemsControl.ItemContainerGenerator.ContainerFromItem(newTarget) as ListBoxItem;
                row?.Focus();
            }
        }
    }

    private void MultiviewSetupView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                CollectionFilterBtn.Focus();
                Keyboard.Focus(CollectionFilterBtn);
            }), DispatcherPriority.ApplicationIdle);
        }
        else
        {
            if (FilterOverlay.Visibility == Visibility.Visible)
            {
                CloseFilterOverlay();
            }
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeToggleBtn.Content = PreferencesManager.LoadTheme() == "Dark" ? "\xE708" : "\xE706";
        var prefs = PreferencesManager.Load();

        if (_availableCollections.Count == 0)
        {
            _collections = await _libraryService.GetCollectionsAsync();
            
            _availableCollections.Add("All Channels");
            _availableCollections.Add("Favorites");
            _availableCollections.Add("HD Channels");
            foreach (var col in _collections) _availableCollections.Add(col.Name);
        }

        _activeCollectionName = prefs.LastMultiviewCollection ?? "All Channels";
        if (!_availableCollections.Contains(_activeCollectionName)) _activeCollectionName = "All Channels";

        CollectionFilterBtn.Content = $"{_activeCollectionName} ▼";

        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            CollectionFilterBtn.Focus();
            Keyboard.Focus(CollectionFilterBtn); 
        }), DispatcherPriority.ApplicationIdle);

        await LoadChannelDataAsync(_activeCollectionName);
    }
    
    private async Task LoadChannelDataAsync(string selection)
    {
        DisplayedChannels.Clear(); 
        ChannelCollection? targetCollection = null;
        
        if (selection != "All Channels" && selection != "Favorites" && selection != "HD Channels")
        {
            targetCollection = _collections.FirstOrDefault(c => c.Name == selection);
        }

        var channels = await _libraryService.GetGuideChannelsAsync(targetCollection, 4);

        if (selection == "Favorites") channels = channels.Where(c => c.Favorite).ToList();
        else if (selection == "HD Channels") channels = channels.Where(c => c.IsHD).ToList();

        _allChannels = channels.Where(c => !c.Hidden).ToList();
        
        foreach (var c in _allChannels)
        {
            DisplayedChannels.Add(c);
        }
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
            
            var prefs = PreferencesManager.Load();
            prefs.LastMultiviewCollection = selection;
            PreferencesManager.Save(prefs);

            await LoadChannelDataAsync(selection);
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

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);

        if (FilterOverlay.Visibility == Visibility.Visible && (command == HtpcCommand.Back || e.Key == Key.Escape))
        {
            CloseFilterOverlay();
            e.Handled = true;
        }
    }

    // --- REFACTORED SELECTION LOGIC ---
    private void AddChannelToSlot(Channel channel)
    {
        for (int i = 0; i < 4; i++)
        {
            if (_selectedChannels[i] == null)
            {
                _selectedChannels[i] = channel;
                UpdateSlotsUI();
                return;
            }
        }
        
        MessageBox.Show("All 4 slots are full. Please remove a channel first.", "Multiview Full", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ListBoxItem_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var command = InputMapper.GetCommand(e.Key);
        
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is Channel channel)
        {
            AddChannelToSlot(channel);
            e.Handled = true; 
        }
        else if (command == HtpcCommand.Up || command == HtpcCommand.Down || command == HtpcCommand.Right)
        {
            var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down : 
                            command == HtpcCommand.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Right;
            (sender as ListBoxItem)?.MoveFocus(new TraversalRequest(direction));
            e.Handled = true; 
        }
    }

    private void ListBoxItem_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is Channel channel)
        {
            AddChannelToSlot(channel);
        }
    }

    private void RemoveSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag.ToString(), out int slotIndex))
        {
            _selectedChannels[slotIndex] = null;
            UpdateSlotsUI();
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < 4; i++) _selectedChannels[i] = null;
        UpdateSlotsUI();
    }

    private void UpdateSlotsUI()
    {
        UpdateSlot(0, Slot1Text, RemoveSlot1);
        UpdateSlot(1, Slot2Text, RemoveSlot2);
        UpdateSlot(2, Slot3Text, RemoveSlot3);
        UpdateSlot(3, Slot4Text, RemoveSlot4);

        int activeCount = _selectedChannels.Count(c => c != null);
        LaunchButton.IsEnabled = activeCount >= 2; 
    }

    private void UpdateSlot(int index, TextBlock textBlock, Button removeBtn)
    {
        var channel = _selectedChannels[index];
        if (channel != null)
        {
            textBlock.Text = $"Slot {index + 1}: {channel.Number} - {channel.Name}";
            textBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
            removeBtn.Visibility = Visibility.Visible;
        }
        else
        {
            textBlock.Text = $"Slot {index + 1}: Empty";
            textBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)); // #AAAAAA
            removeBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        var channelsToLaunch = _selectedChannels.Where(c => c != null).Cast<Channel>().ToList();
        
        if (channelsToLaunch.Count >= 2)
        {
            OnLaunchMultiviewRequested?.Invoke(this, channelsToLaunch);
        }
    }
    
    // --- UPDATED NAVIGATION SIGNATURES (RoutedEventArgs) ---
    private void Home_Click(object sender, RoutedEventArgs e) => OnHomeRequested?.Invoke(this, EventArgs.Empty);
    private void Guide_Click(object sender, RoutedEventArgs e) => OnGuideRequested?.Invoke(this, EventArgs.Empty);
    private void Movies_Click(object sender, RoutedEventArgs e) => OnMoviesRequested?.Invoke(this, EventArgs.Empty);
    private void Recordings_Click(object sender, RoutedEventArgs e) => OnRecordingsRequested?.Invoke(this, EventArgs.Empty);
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
    private void Collections_Click(object sender, RoutedEventArgs e) => OnCollectionsRequested?.Invoke(this, EventArgs.Empty);
}
