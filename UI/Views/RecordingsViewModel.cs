using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Linq;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.ViewModels;

public class RecordingsViewModel : INotifyPropertyChanged
{
    private readonly MediaLibraryService _mediaLibraryService;
    
    // --- MY RECORDINGS COLLECTIONS ---
    public ObservableCollection<MediaItem> ActiveRecordings { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> ScheduledRecordings { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> RecentShows { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> RecentMovies { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> ImportedMedia { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<ChannelCollection> DiscoverCollections { get; set; } = new ObservableCollection<ChannelCollection>();
	
    // --- DISCOVER COLLECTIONS ---
    public ObservableCollection<MediaItem> DiscoverResults { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<Channel> DiscoverChannels { get; set; } = new ObservableCollection<Channel>();

    // Master Cache Lists
    private readonly List<MediaItem> _masterActive = new();
    private readonly List<MediaItem> _masterScheduled = new();
    private readonly List<MediaItem> _masterShows = new();
    private readonly List<MediaItem> _masterMovies = new();
    private readonly List<MediaItem> _masterImports = new();

    private const int ChunkSize = 25; 
    public const int DiscoverChunkSize = 50;

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
    }

    public RecordingsViewModel(MediaLibraryService mediaLibraryService)
    {
        _mediaLibraryService = mediaLibraryService;
    }

    public async Task LoadRecordingsAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            // --- FIX: Push ALL heavy JSON parsing to background CPU threads ---
            var results = await Task.Run(() => _mediaLibraryService.GetAllRecordingsAsync());
            var scheduled = await Task.Run(() => _mediaLibraryService.GetScheduledRecordingsAsync());
            var imports = await Task.Run(() => _mediaLibraryService.GetImportedMediaAsync()); 
            
            _masterActive.Clear();
            _masterShows.Clear();
            _masterMovies.Clear();
            _masterImports.Clear();
            _masterScheduled.Clear();

            // Populate Master Lists
            foreach (var item in results)
            {
                if (item.IsImported) continue; 

                if (item.IsRecording) 
                {
                    _masterActive.Add(item);
                }
                else 
                {
                    if (item.Categories != null && item.Categories.Any(c => c.Equals("Movie", StringComparison.OrdinalIgnoreCase)))
                    {
                        _masterMovies.Add(item);
                    }
                    else
                    {
                        _masterShows.Add(item); 
                    }
                }
            }

            _masterScheduled.AddRange(scheduled);
            _masterImports.AddRange(imports);
            _masterImports.AddRange(results.Where(i => i.IsImported)); 

            // Clear the heavy UI collections
            ActiveRecordings.Clear();
            ScheduledRecordings.Clear();
            RecentShows.Clear();
            RecentMovies.Clear();
            ImportedMedia.Clear();

            // Push initial light batches to the UI
            LoadMoreActive();
            LoadMoreScheduled();
            LoadMoreShows();
            LoadMoreMovies();
            LoadMoreImports();
        }
        finally
        {
            IsLoading = false;
        }
    } 
	
	public void LoadMoreActive() => LoadChunk(_masterActive, ActiveRecordings);
    public void LoadMoreScheduled() => LoadChunk(_masterScheduled, ScheduledRecordings);
    public void LoadMoreShows() => LoadChunk(_masterShows, RecentShows);
    public void LoadMoreMovies() => LoadChunk(_masterMovies, RecentMovies);
    public void LoadMoreImports() => LoadChunk(_masterImports, ImportedMedia);

    private void LoadChunk(List<MediaItem> masterList, ObservableCollection<MediaItem> displayCollection)
    {
        if (displayCollection.Count >= masterList.Count) return;

        var nextBatch = masterList.Skip(displayCollection.Count).Take(ChunkSize);
        foreach (var item in nextBatch)
        {
            displayCollection.Add(item);
        }
    }

    public async Task<bool> DeleteMediaAsync(MediaItem item)
    {
        if (item == null) return false;
        
        bool success = await _mediaLibraryService.DeleteRecordingAsync(item.Id);
        if (success)
        {
            _masterActive.Remove(item);
            _masterScheduled.Remove(item);
            _masterShows.Remove(item);
            _masterMovies.Remove(item);
            _masterImports.Remove(item);

            if (ActiveRecordings.Contains(item)) ActiveRecordings.Remove(item);
            else if (ScheduledRecordings.Contains(item)) ScheduledRecordings.Remove(item);
            else if (RecentShows.Contains(item)) RecentShows.Remove(item);
            else if (RecentMovies.Contains(item)) RecentMovies.Remove(item);
            else if (ImportedMedia.Contains(item)) ImportedMedia.Remove(item);
        }
        return success;
    }
	
	// --- DISCOVER LOGIC ---
    
    // 1. New Collection Loader
    public async Task LoadDiscoverCollectionsAsync()
    {
        var cols = await Task.Run(() => _mediaLibraryService.GetCollectionsAsync());
        
        // Ensure we update the UI collection on the main thread
        App.Current.Dispatcher.Invoke(() =>
        {
            DiscoverCollections.Clear();
            foreach (var c in cols)
            {
                DiscoverCollections.Add(c);
            }
        });
    }

    // 2. Updated Channel Loader (Now accepts a collection)
    public async Task LoadDiscoverChannelsAsync(ChannelCollection? activeCollection)
    {
        // Fetch data on background thread
        var channels = await Task.Run(() => _mediaLibraryService.GetGuideChannelsAsync(activeCollection, 1));
        
        // Update UI collection on the main thread
        App.Current.Dispatcher.Invoke(() =>
        {
            DiscoverChannels.Clear();
            DiscoverChannels.Add(new Channel { Id = "ALL", Name = "All Channels", Number = "ALL" });

            if (channels != null)
            {
                foreach (var ch in channels)
                {
                    DiscoverChannels.Add(ch);
                }
            }
        });
    }

    // 3. Updated Airings Loader (Now accepts a collection)
    public async Task<List<MediaItem>> GetDiscoverAiringsAsync(int offset, int limit, string query, string channelId, ChannelCollection? activeCollection)
    {
        string safeChannelId = channelId == "ALL" ? "" : channelId;
        return await _mediaLibraryService.SearchUpcomingAiringsAsync(offset, limit, query, safeChannelId, activeCollection);
    }

    public async Task<bool> RecordEventAsync(MediaItem item)
    {
        return await _mediaLibraryService.RecordEventAsync(item);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}