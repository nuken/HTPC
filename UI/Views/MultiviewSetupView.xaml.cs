using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HTPC.Core.Input; // Required for remote control commands
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
   
    // Events to communicate with MainWindow
    public event EventHandler<List<Channel>>? OnLaunchMultiviewRequested;
    
    private readonly MediaLibraryService _libraryService;
    public ObservableCollection<Channel> DisplayedChannels { get; set; } = new ObservableCollection<Channel>();
    
    private List<Channel> _allChannels = new List<Channel>();
    private List<ChannelCollection> _collections = new List<ChannelCollection>();
    private Channel?[] _selectedChannels = new Channel?[4];
	private readonly DispatcherTimer _autoRefreshTimer;
    
    public MultiviewSetupView(MediaLibraryService libraryService)
    {
        InitializeComponent();
        _libraryService = libraryService;
        ChannelItemsControl.ItemsSource = DisplayedChannels;
        this.Loaded += OnLoaded;
        this.IsVisibleChanged += MultiviewSetupView_IsVisibleChanged; 

        // --- NEW: Start the Smart Sync EPG auto-refresh timer ---
        DateTime now = DateTime.Now;
        int minutesUntilNextHalfHour = 30 - (now.Minute % 30);
        int secondsUntilNextHalfHour = (minutesUntilNextHalfHour * 60) - now.Second;

        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(secondsUntilNextHalfHour) };
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        _autoRefreshTimer.Start();
    }
	
	private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        // --- NEW: Lock the timer to exactly 30 minutes going forward ---
        if (sender is DispatcherTimer timer && timer.Interval.TotalMinutes != 30)
        {
            timer.Interval = TimeSpan.FromMinutes(30);
        }

        if (CollectionDropdown.SelectedItem is string selection)
        {
            // Remember focus...
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

            // Restore focus
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
    }

    // --- NEW: Snap focus back when returning from the Multiview Player! ---
    private void MultiviewSetupView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            _ = Dispatcher.BeginInvoke(new Action(() => 
            {
                CollectionDropdown.Focus();
                Keyboard.Focus(CollectionDropdown);
            }), DispatcherPriority.ApplicationIdle);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var prefs = PreferencesManager.Load(); // Read from disk

        if (CollectionDropdown.Items.Count == 0)
        {
            _collections = await _libraryService.GetCollectionsAsync();
            
            CollectionDropdown.Items.Add("All Channels");
            CollectionDropdown.Items.Add("Favorites");
            CollectionDropdown.Items.Add("HD Channels");
            foreach (var col in _collections) CollectionDropdown.Items.Add(col.Name);
        }

        // Restore from disk memory
        CollectionDropdown.SelectedItem = prefs.LastMultiviewCollection ?? "All Channels";
        if (CollectionDropdown.SelectedIndex == -1) CollectionDropdown.SelectedIndex = 0;

        // --- NEW: THE HEAVY HAMMER FOCUS FIX ---
        // Snap the hardware remote control focus directly to the Dropdown
        _ = Dispatcher.BeginInvoke(new Action(() => 
        {
            CollectionDropdown.Focus();
            Keyboard.Focus(CollectionDropdown); 
        }), DispatcherPriority.ApplicationIdle);
    }
    private async void CollectionDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CollectionDropdown.SelectedItem is string selection)
        {
            // Save to disk immediately
            var prefs = PreferencesManager.Load();
            prefs.LastMultiviewCollection = selection;
            PreferencesManager.Save(prefs);

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
    }

    // --- REFACTORED SELECTION LOGIC ---
    private void AddChannelToSlot(Channel channel)
    {
        // Find first empty slot
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
        
        // 1. Handle OK/Enter to add the channel
        if (command == HtpcCommand.Select && sender is ListBoxItem item && item.DataContext is Channel channel)
        {
            AddChannelToSlot(channel);
            e.Handled = true; 
        }
        // 2. Escape the ListBox naturally using the D-Pad
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

    private void Dropdown_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var cb = sender as ComboBox;
        var command = InputMapper.GetCommand(e.Key);

        // If the dropdown is CLOSED, allow the D-Pad to escape!
        if (cb != null && !cb.IsDropDownOpen)
        {
            if (command == HtpcCommand.Down || command == HtpcCommand.Up || command == HtpcCommand.Left || command == HtpcCommand.Right)
            {
                var direction = command == HtpcCommand.Down ? FocusNavigationDirection.Down :
                                command == HtpcCommand.Up ? FocusNavigationDirection.Up :
                                command == HtpcCommand.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right;

                cb.MoveFocus(new TraversalRequest(direction));
                e.Handled = true; 
            }
        }
    }

    // --- END SELECTION LOGIC ---

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
        LaunchButton.IsEnabled = activeCount >= 2; // Need at least 2 channels to multiview
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
        // Extract only the populated channels
        var channelsToLaunch = _selectedChannels.Where(c => c != null).Cast<Channel>().ToList();
        
        if (channelsToLaunch.Count >= 2)
        {
            // Pass the list back to MainWindow to open the Multiview Player
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