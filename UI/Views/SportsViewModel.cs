using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.ViewModels;

public class SportsViewModel : INotifyPropertyChanged
{
    private readonly MediaLibraryService _mediaLibraryService;

    // Master Cache
    private readonly List<MediaItem> _masterEvents = new();
	private List<LiveScoreData> _liveScoresCache = new();
    
    // UI Observable Collections
    public ObservableCollection<MediaItem> LiveEvents { get; } = new();
    public ObservableCollection<MediaItem> UpcomingEvents { get; } = new();
    public ObservableCollection<string> AvailableGenres { get; } = new();
    public ObservableCollection<string> ActiveGenreFilters { get; } = new();
	private List<MediaItem> _filteredLiveList = new();
    private List<MediaItem> _filteredUpcomingList = new();
    private int _liveOffset = 0;
    private int _upcomingOffset = 0;
    private const int ChunkSize = 30;

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
    }

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value;
                OnPropertyChanged(nameof(SearchQuery));
                ApplyFilters();
            }
        }
    }

    public SportsViewModel(MediaLibraryService mediaLibraryService)
    {
        _mediaLibraryService = mediaLibraryService;
    }

   public async Task LoadSportsAsync()
{
    if (IsLoading) return;
    IsLoading = true;

    try
    {
        // 1. Load preferences FIRST to see if we need to fetch scores
        var prefs = PreferencesManager.Load();
        
        var getEventsTask = Task.Run(() => _mediaLibraryService.GetSportsEventsAsync(24));
        Task<List<LiveScoreData>>? getScoresTask = null;

        // 2. Only hit the ESPN API if the user actually wants scores
        if (!prefs.HideSportsScores)
        {
            getScoresTask = _mediaLibraryService.GetLiveScoresAsync();
            await Task.WhenAll(getEventsTask, getScoresTask);
            _liveScoresCache = getScoresTask.Result;
        }
        else
        {
            // If scores are hidden, just wait for the DVR guide and clear the cache
            await getEventsTask;
            _liveScoresCache.Clear();
        }

        // 3. Extract the results
        var (events, genres) = getEventsTask.Result;

        _masterEvents.Clear();
        _masterEvents.AddRange(events);

        AvailableGenres.Clear();
        foreach (var genre in genres)
        {
            AvailableGenres.Add(genre);
        }

        // --- Load saved filters from preferences ---
        ActiveGenreFilters.Clear();
        if (prefs.ActiveSportFilters != null)
        {
            foreach (var filter in prefs.ActiveSportFilters)
            {
                // Only restore the pill if that sport is actually airing today
                if (AvailableGenres.Contains(filter, StringComparer.OrdinalIgnoreCase))
                {
                    ActiveGenreFilters.Add(filter);
                }
            }
        }

        ApplyFilters();
    }
    finally
    {
        IsLoading = false;
    }
}

public void ToggleGenreFilter(string genre)
{
    if (ActiveGenreFilters.Contains(genre))
    {
        ActiveGenreFilters.Remove(genre);
    }
    else
    {
        ActiveGenreFilters.Add(genre);
    }

    // --- NEW: Save the updated filters to preferences ---
    var prefs = PreferencesManager.Load();
    prefs.ActiveSportFilters = ActiveGenreFilters.ToList();
    PreferencesManager.Save(prefs);

    ApplyFilters();
}
   
   public void ClearGenreFilters()
    {
        ActiveGenreFilters.Clear();
        ApplyFilters();
    }

    public void ApplyFilters()
    {
        var now = DateTime.Now;
        var query = _masterEvents.AsEnumerable();

        // 1. Filter by Active Sport/Genre Pills
        if (ActiveGenreFilters.Count > 0)
        {
            query = query.Where(e => e.Genres.Any(g => ActiveGenreFilters.Contains(g, StringComparer.OrdinalIgnoreCase)));
        }

        // 2. Filter by Search Query
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string lowerQuery = SearchQuery.ToLower();
            query = query.Where(e =>
                (e.Title != null && e.Title.ToLower().Contains(lowerQuery)) ||
                (e.CurrentShowTitle != null && e.CurrentShowTitle.ToLower().Contains(lowerQuery)) ||
                (e.Summary != null && e.Summary.ToLower().Contains(lowerQuery)) ||
                (e.ChannelName != null && e.ChannelName.ToLower().Contains(lowerQuery)));
        }

        var filteredList = query.ToList();

        // 3. Partition into internal background lists
        _filteredLiveList = filteredList.Where(e => now >= e.StartTime && now < e.EndTime).OrderBy(e => e.StartTime).ToList();
        _filteredUpcomingList = filteredList.Where(e => e.StartTime > now).OrderBy(e => e.StartTime).ToList();
		
		

        var prefs = PreferencesManager.Load();
		
		// --- NEW: FUZZY SCORE MATCHER ---
        if (!prefs.HideSportsScores)
        {
            foreach (var media in _filteredLiveList)
            {
                // Create a giant block of lowercase text to search against
                string rawText = $"{media.Title} {media.CurrentShowTitle} {media.Summary}".ToLower();
                
                // Break the TV guide data into an array of distinct words (removing punctuation)
                string[] words = rawText.Split(new[] { ' ', ',', '.', '@', '-', ':', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var score in _liveScoresCache)
                {
                    string hMascot = score.HomeMascot.ToLower();
                    string hSchool = score.HomeSchool.ToLower();
                    string hAbbr = score.HomeAbbreviation.ToLower();
                    string hDisp = score.HomeDisplayName.ToLower();
                    
                    string aMascot = score.AwayMascot.ToLower();
                    string aSchool = score.AwaySchool.ToLower();
                    string aAbbr = score.AwayAbbreviation.ToLower();
                    string aDisp = score.AwayDisplayName.ToLower();

                    // Smart Match: Check Name, Location, Full Display Name, and Abbreviations!
                    bool hasHome = (!string.IsNullOrEmpty(hMascot) && rawText.Contains(hMascot)) || 
                                   (!string.IsNullOrEmpty(hSchool) && rawText.Contains(hSchool)) || 
                                   (!string.IsNullOrEmpty(hDisp) && rawText.Contains(hDisp)) || 
                                   (!string.IsNullOrEmpty(hAbbr) && words.Contains(hAbbr));
                                   
                    bool hasAway = (!string.IsNullOrEmpty(aMascot) && rawText.Contains(aMascot)) || 
                                   (!string.IsNullOrEmpty(aSchool) && rawText.Contains(aSchool)) || 
                                   (!string.IsNullOrEmpty(aDisp) && rawText.Contains(aDisp)) || 
                                   (!string.IsNullOrEmpty(aAbbr) && words.Contains(aAbbr));

                    if (hasHome && hasAway)
                    {
                        media.LiveScore = score.Score;
                        media.GamePeriod = score.Period;
                        break; // Stop searching ESPN scores once we find the match
                    }
                }
            }
        }

        // 4. Clear the UI and reset offsets
        LiveEvents.Clear();
        UpcomingEvents.Clear();
        _liveOffset = 0;
        _upcomingOffset = 0;

        // 5. Load the first chunk
        LoadMoreLive();
        LoadMoreUpcoming();
    }

    // --- NEW: Chunk Loading Methods ---
    public void LoadMoreLive()
    {
        if (_liveOffset >= _filteredLiveList.Count) return;
        
        var chunk = _filteredLiveList.Skip(_liveOffset).Take(ChunkSize).ToList();
        foreach (var item in chunk) LiveEvents.Add(item);
        
        _liveOffset += chunk.Count;
    }

    public void LoadMoreUpcoming()
    {
        if (_upcomingOffset >= _filteredUpcomingList.Count) return;
        
        var chunk = _filteredUpcomingList.Skip(_upcomingOffset).Take(ChunkSize).ToList();
        foreach (var item in chunk) UpcomingEvents.Add(item);
        
        _upcomingOffset += chunk.Count;
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