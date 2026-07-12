using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Linq;
using System.Collections.Generic;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.ViewModels;

public class CollectionsViewModel : INotifyPropertyChanged
{
    private readonly MediaLibraryService _mediaLibraryService;
    
    // Flattened Collections
    public ObservableCollection<CollectionItem> MovieCollections { get; set; } = new();
    public ObservableCollection<CollectionItem> ShowCollections { get; set; } = new();
    
    public ObservableCollection<int> Seasons { get; set; } = new ObservableCollection<int>();
    public ObservableCollection<MediaItem> CurrentEpisodes { get; set; } = new ObservableCollection<MediaItem>();
    
    public CollectionsViewModel(MediaLibraryService mediaLibraryService)
    {
        _mediaLibraryService = mediaLibraryService;
    }
	
    public async Task<List<MediaItem>> GetShowEpisodesAsync(string showId)
    {
        return await _mediaLibraryService.GetShowEpisodesAsync(showId);
    }

    public async Task LoadCollectionsAsync()
    {
        var allCollections = await _mediaLibraryService.GetLibraryCollectionsAsync();

        MovieCollections.Clear();
        ShowCollections.Clear();

        // Feed the data sequentially; the WrapPanel in the UI will handle the layout
        var movies = allCollections.Where(c => c.CollectionType == "movies").ToList();
        foreach (var item in movies)
        {
            MovieCollections.Add(item);
        }

        var shows = allCollections.Where(c => c.CollectionType == "shows").ToList();
        foreach (var item in shows)
        {
            ShowCollections.Add(item);
        }
    }

    public async Task<List<MediaItem>> GetCollectionContentsAsync(string id)
    {
        return await _mediaLibraryService.GetCollectionMediaAsync(id);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
	
    public async Task<MediaItem?> GetNextUnwatchedEpisodeAsync(string showId)
    {
        return await _mediaLibraryService.GetNextUnwatchedEpisodeAsync(showId);
    }
}