using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.OpenSubtitles.Configuration;

/// <summary>
/// The plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the credentials are invalid.
    /// </summary>
    public bool CredentialsInvalid { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether automatic subtitle searches should require strict
    /// season/episode (or IMDb) equality between the OpenSubtitles result and the local item.
    /// When <c>true</c> (default) behavior matches upstream: episode results must report the same
    /// season and episode numbers and movie results must report the same IMDb id. When <c>false</c>
    /// only the feature type (Episode/Movie) is required; results are still ordered with hash
    /// matches first.
    /// </summary>
    public bool StrictMatching { get; set; } = true;
}
