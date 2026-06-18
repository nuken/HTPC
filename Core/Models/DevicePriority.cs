using System.Text.Json.Serialization;

namespace HTPC.Core.Models;

public class DevicePriority
{
    [JsonPropertyName("DeviceID")]
    public string? DeviceId { get; set; }
    
    [JsonPropertyName("FriendlyName")]
    public string? FriendlyName { get; set; }
}