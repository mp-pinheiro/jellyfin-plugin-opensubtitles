namespace Jellyfin.Plugin.OpenSubtitles.API;

/// <summary>
/// A language option returned by the languages endpoint.
/// </summary>
public class LanguageOption
{
    /// <summary>
    /// Gets or sets the language code (typically a two-letter ISO code).
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name for the language.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
