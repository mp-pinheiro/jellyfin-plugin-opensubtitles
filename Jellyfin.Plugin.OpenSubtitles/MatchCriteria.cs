namespace Jellyfin.Plugin.OpenSubtitles;

/// <summary>
/// Primitive view of the matching constraints used to filter OpenSubtitles results. Constructed
/// from a <c>SubtitleSearchRequest</c> for the auto path and <c>null</c> for the manual path.
/// </summary>
/// <param name="IsEpisode"><c>true</c> if the request is for an episode, otherwise movie.</param>
/// <param name="Season">The season number when the item is an episode.</param>
/// <param name="Episode">The episode number when the item is an episode.</param>
/// <param name="ImdbId">The IMDb id (numeric, without the leading <c>tt</c>), or 0 if unknown.</param>
/// <param name="IsAutomated">Whether the originating request was an automated task.</param>
/// <param name="IsPerfectMatch">Whether to restrict to hash-matched results.</param>
internal sealed record MatchCriteria(
    bool IsEpisode,
    int? Season,
    int? Episode,
    long ImdbId,
    bool IsAutomated,
    bool IsPerfectMatch);
