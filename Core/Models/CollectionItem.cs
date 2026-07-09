namespace HTPC.Core.Models;

public class CollectionItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CollectionType { get; set; } = string.Empty; // "movies" or "shows"
    public string ImageUrl { get; set; } = string.Empty;
    public int ContentCount { get; set; }
}