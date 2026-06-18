using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.Views;

public partial class MultiviewSetupView : UserControl
{
    public event EventHandler? OnHomeRequested;
    public event EventHandler? OnGuideRequested;
    public event EventHandler? OnMoviesRequested;
    public event EventHandler? OnShowsRequested;
    public event EventHandler? OnVideosRequested;
    public event EventHandler? OnSettingsRequested;
   
	
	// Events to communicate with MainWindow
    public event EventHandler<List<Channel>>? OnLaunchMultiviewRequested;
    
    private readonly MediaLibraryService _libraryService;
    public ObservableCollection<Channel> DisplayedChannels { get; set; } = new ObservableCollection<Channel>();
    
    private List<Channel> _allChannels = new List<Channel>();
    private List<ChannelCollection> _collections = new List<ChannelCollection>();
    private Channel?[] _selectedChannels = new Channel?[4];
	
    public MultiviewSetupView(MediaLibraryService libraryService)
    {
        InitializeComponent();
        _libraryService = libraryService;
        ChannelItemsControl.ItemsSource = DisplayedChannels;
        this.Loaded += OnLoaded;
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

    private void Channel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Channel channel)
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
    private void Videos_Click(object sender, RoutedEventArgs e) => OnVideosRequested?.Invoke(this, EventArgs.Empty);
    private void Shows_Click(object sender, RoutedEventArgs e) => OnShowsRequested?.Invoke(this, EventArgs.Empty);
	private void Settings_Click(object sender, RoutedEventArgs e) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);
}