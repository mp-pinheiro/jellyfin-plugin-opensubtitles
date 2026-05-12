using System;

namespace Jellyfin.Plugin.OpenSubtitles.API;

/// <summary>
/// A single subtitle result returned by the manual search endpoint.
/// </summary>
public class ManualSearchResult
{
    /// <summary>
    /// Gets or sets the subtitle id that should be passed back to the download endpoint.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the release name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the uploader name.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Gets or sets the uploader comment.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Gets or sets the community rating.
    /// </summary>
    public float? CommunityRating { get; set; }

    /// <summary>
    /// Gets or sets the download count.
    /// </summary>
    public int? DownloadCount { get; set; }

    /// <summary>
    /// Gets or sets the subtitle file format.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets the three-letter ISO language code.
    /// </summary>
    public string? ThreeLetterISOLanguageName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the result is a hash match.
    /// </summary>
    public bool? IsHashMatch { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subtitle is for hearing impaired viewers.
    /// </summary>
    public bool? HearingImpaired { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subtitle is machine translated.
    /// </summary>
    public bool? MachineTranslated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subtitle is AI translated.
    /// </summary>
    public bool? AiTranslated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subtitle is forced.
    /// </summary>
    public bool? Forced { get; set; }

    /// <summary>
    /// Gets or sets the frame rate the subtitle was timed for.
    /// </summary>
    public float? FrameRate { get; set; }

    /// <summary>
    /// Gets or sets the upload date.
    /// </summary>
    public DateTime? DateCreated { get; set; }
}
