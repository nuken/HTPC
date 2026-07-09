using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Linq;
using HTPC.Core.Models;
using HTPC.Services;

namespace HTPC.UI.ViewModels;

public class CollectionsViewModel : INotifyPropertyChanged
{
    private readonly MediaLibraryService _mediaLibraryService;
    
    public ObservableCollection<CollectionItem> MovieCollections { get; set; } = new();
    public ObservableCollection<CollectionItem> ShowCollections { get; set; } = new();
    public ObservableCollection<int> Seasons { get; set; } = new ObservableCollection<int>();
    public ObservableCollection<MediaItem> CurrentEpisodes { get; set; } = new ObservableCollection<MediaItem>();
    public CollectionsViewModel(MediaLibraryService mediaLibraryService)
    {
        _mediaLibraryService = mediaLibraryService;
    }
	
	public async Task<System.Collections.Generic.List<MediaItem>> GetShowEpisodesAsync(string showId)
    {
        return await _mediaLibraryService.GetShowEpisodesAsync(showId);
    }

    public async Task LoadCollectionsAsync()
    {
        var allCollections = await _mediaLibraryService.GetLibraryCollectionsAsync();

        MovieCollections.Clear();
        ShowCollections.Clear();

        foreach (var col in allCollections.Where(c => c.CollectionType == "movies"))
            MovieCollections.Add(col);

        foreach (var col in allCollections.Where(c => c.CollectionType == "shows"))
            ShowCollections.Add(col);
    }

    public async Task<System.Collections.Generic.List<MediaItem>> GetCollectionContentsAsync(string id)
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