using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles.API;

/// <summary>
/// Request body for the manual subtitle search endpoint.
/// </summary>
public class ManualSearchRequest
{
    /// <summary>
    /// Gets or sets the free-text query (title, release name, etc.).
    /// </summary>
    [JsonPropertyName("query")]
    public string? Query { get; set; }

    /// <summary>
    /// Gets or sets the two-letter ISO language code.
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content type (<c>episode</c> or <c>movie</c>).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "movie";

    /// <summary>
    /// Gets or sets the season number for episode searches.
    /// </summary>
    [JsonPropertyName("season")]
    public int? Season { get; set; }

    /// <summary>
    /// Gets or sets the episode number for episode searches.
    /// </summary>
    [JsonPropertyName("episode")]
    public int? Episode { get; set; }

    /// <summary>
    /// Gets or sets the IMDb id (numeric, without the leading <c>tt</c>).
    /// </summary>
    [JsonPropertyName("imdbId")]
    public long? ImdbId { get; set; }

    /// <summary>
    /// Gets or sets the year of release.
    /// </summary>
    [JsonPropertyName("year")]
    public int? Year { get; set; }
}
