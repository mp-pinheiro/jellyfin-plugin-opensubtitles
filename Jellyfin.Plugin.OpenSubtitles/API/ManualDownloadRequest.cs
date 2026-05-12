using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles.API;

/// <summary>
/// Request body for the manual subtitle download endpoint.
/// </summary>
public class ManualDownloadRequest
{
    /// <summary>
    /// Gets or sets the subtitle id returned by the search endpoint.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of the media file the subtitle should be written next to.
    /// </summary>
    [JsonPropertyName("destinationPath")]
    public string DestinationPath { get; set; } = string.Empty;
}
