using System;

namespace HTPC.Core.Models;

public class ServerConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 8089;
    public string AuthToken { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string DefaultCollectionId { get; set; } = string.Empty; 
}