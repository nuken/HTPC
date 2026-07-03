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
    
    // UI Bound Collections
    public ObservableCollection<MediaItem> ActiveRecordings { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> ScheduledRecordings { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> RecentShows { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> RecentMovies { get; set; } = new ObservableCollection<MediaItem>();
    public ObservableCollection<MediaItem> ImportedMedia { get; set; } = new ObservableCollection<MediaItem>();

    // Master Cache Lists (Background Memory)
    private readonly List<MediaItem> _masterActive = new();
    private readonly List<MediaItem> _masterScheduled = new();
    private readonly List<MediaItem> _masterShows = new();
    private readonly List<MediaItem> _masterMovies = new();
    private readonly List<MediaItem> _masterImports = new();

    private const int ChunkSize = 25; // Optimize layout passes to 25 items at a time

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
            var results = await _mediaLibraryService.GetAllRecordingsAsync();
            var scheduled = await _mediaLibraryService.GetScheduledRecordingsAsync();
            var imports = await _mediaLibraryService.GetImportedMediaAsync(); 
            
            _masterActive.Clear();
            _masterShows.Clear();
            _masterMovies.Clear();
            _masterImports.Clear();
            _masterScheduled.Clear();

            // Populate Master Lists
            foreach (var item in results)
            {
                if (item.IsImported) continue; // Handled below

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
            _masterImports.AddRange(results.Where(i => i.IsImported)); // Merge standard endpoint imports

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

    // --- LAZY LOADING TRIGGERS ---
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
            // Remove from background memory
            _masterActive.Remove(item);
            _masterScheduled.Remove(item);
            _masterShows.Remove(item);
            _masterMovies.Remove(item);
            _masterImports.Remove(item);

            // Remove from UI
            if (ActiveRecordings.Contains(item)) ActiveRecordings.Remove(item);
            else if (ScheduledRecordings.Contains(item)) ScheduledRecordings.Remove(item);
            else if (RecentShows.Contains(item)) RecentShows.Remove(item);
            else if (RecentMovies.Contains(item)) RecentMovies.Remove(item);
            else if (ImportedMedia.Contains(item)) ImportedMedia.Remove(item);
        }
        return success;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}