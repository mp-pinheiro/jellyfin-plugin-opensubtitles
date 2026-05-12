using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OpenSubtitles.Configuration;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OpenSubtitles;

/// <summary>
/// The open subtitle downloader.
/// </summary>
public class OpenSubtitleDownloader : ISubtitleProvider
{
    private readonly ILogger<OpenSubtitleDownloader> _logger;
    private readonly ConcurrentBag<int> _badSubtitleIds = new ();
    private LoginInfo? _login;
    private DateTime? _limitReset;
    private DateTime? _lastRatelimitLog;
    private List<string>? _languages;
    private PluginConfiguration? _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenSubtitleDownloader"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{OpenSubtitleDownloader}"/> interface.</param>
    /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/> for creating Http Clients.</param>
    public OpenSubtitleDownloader(ILogger<OpenSubtitleDownloader> logger, IHttpClientFactory httpClientFactory)
    {
        Instance = this;
        _logger = logger;
        OpenSubtitlesRequestHelper.Instance = new OpenSubtitlesRequestHelper(httpClientFactory);
    }

    /// <summary>
    /// Gets the downloader instance.
    /// </summary>
    public static OpenSubtitleDownloader? Instance { get; private set; }

    /// <inheritdoc />
    public string Name => "Open Subtitles";

    /// <inheritdoc />
    public IEnumerable<VideoContentType> SupportedMediaTypes
        => new[] { VideoContentType.Episode, VideoContentType.Movie };

    /// <inheritdoc />
    public Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        => GetSubtitlesInternal(id, cancellationToken);

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await Login(cancellationToken).ConfigureAwait(false);

        if (request.IsAutomated && _login is null)
        {
            // Login attempt failed, since this is a task to download subtitles there's no point in continuing
            _logger.LogDebug("Returning empty results because login failed");
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        if (request.IsAutomated && _login?.User?.RemainingDownloads <= 0)
        {
            if (_lastRatelimitLog is null || DateTime.UtcNow.Subtract(_lastRatelimitLog.Value).TotalSeconds > 60)
            {
                _logger.LogInformation("Daily download limit reached, returning no results for automated task");
                _lastRatelimitLog = DateTime.UtcNow;
            }

            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        long.TryParse(request.GetProviderId(MetadataProvider.Imdb)?.TrimStart('t') ?? string.Empty, NumberStyles.Any, CultureInfo.InvariantCulture, out var imdbId);

        if (request.ContentType == VideoContentType.Episode && (!request.IndexNumber.HasValue || !request.ParentIndexNumber.HasValue || string.IsNullOrEmpty(request.SeriesName)))
        {
            _logger.LogDebug("Episode information missing");
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        if (string.IsNullOrEmpty(request.MediaPath))
        {
            _logger.LogDebug("Path Missing");
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        var language = await GetLanguage(request.TwoLetterISOLanguageName, request.MediaPath, cancellationToken).ConfigureAwait(false);

        string? hash = null;
        if (!Path.GetExtension(request.MediaPath).Equals(".strm", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
#pragma warning disable CA2007
                await using var fileStream = File.OpenRead(request.MediaPath);
#pragma warning restore CA2007

                hash = OpenSubtitlesRequestHelper.ComputeHash(fileStream);
            }
            catch (IOException ex)
            {
                throw new IOException(string.Format(CultureInfo.InvariantCulture, "IOException while computing hash for {0}", request.MediaPath), ex);
            }
        }

        var options = new Dictionary<string, string>
        {
            { "languages", language },
            { "type", request.ContentType == VideoContentType.Episode ? "episode" : "movie" }
        };

        if (!string.IsNullOrEmpty(hash))
        {
            options.Add("moviehash", hash);

            if (request.IsPerfectMatch)
            {
                options.Add("moviehash_match", "only");
            }
        }

        // If we have the IMDb ID we use that, otherwise query with the details
        if (imdbId != 0)
        {
            options.Add("imdb_id", imdbId.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            options.Add("query", BuildSearchQuery(request));

            if (request.ContentType == VideoContentType.Episode)
            {
                if (request.ParentIndexNumber.HasValue)
                {
                    options.Add("season_number", request.ParentIndexNumber.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (request.IndexNumber.HasValue)
                {
                    options.Add("episode_number", request.IndexNumber.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        return await SearchAndMapAsync(options, request, request.Language, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs a manual subtitle search using a user-supplied query and parameters. Intended for
    /// the controller-backed manual search UI; bypasses hash computation and S/E or IMDb equality
    /// post-filters. Login and daily-limit checks are still enforced.
    /// </summary>
    /// <param name="query">Free-form text query (e.g. show title or release name). Optional if <paramref name="imdbId"/> is provided.</param>
    /// <param name="twoLetterIsoLanguageName">The two-letter ISO language code, as accepted by <see cref="GetLanguage"/>.</param>
    /// <param name="type">Either <c>"episode"</c> or <c>"movie"</c>.</param>
    /// <param name="season">Optional season number.</param>
    /// <param name="episode">Optional episode number.</param>
    /// <param name="imdbId">Optional IMDb id (numeric, without the leading <c>tt</c>).</param>
    /// <param name="year">Optional year.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The mapped list of <see cref="RemoteSubtitleInfo"/>.</returns>
    public async Task<IEnumerable<RemoteSubtitleInfo>> ManualSearchAsync(
        string? query,
        string twoLetterIsoLanguageName,
        string type,
        int? season,
        int? episode,
        long? imdbId,
        int? year,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(twoLetterIsoLanguageName);
        ArgumentException.ThrowIfNullOrEmpty(type);

        await Login(cancellationToken).ConfigureAwait(false);

        if (_login is null)
        {
            throw new AuthenticationException("Unable to login");
        }

        if (_login.User?.RemainingDownloads <= 0)
        {
            throw new RateLimitExceededException("OpenSubtitles download limit reached");
        }

        // Mirror the path-based GetLanguage signature without requiring a media path.
        var language = await GetLanguage(twoLetterIsoLanguageName, "manual-search", cancellationToken).ConfigureAwait(false);

        var normalizedType = string.Equals(type, "episode", StringComparison.OrdinalIgnoreCase) ? "episode" : "movie";
        var options = new Dictionary<string, string>
        {
            { "languages", language },
            { "type", normalizedType }
        };

        if (imdbId.HasValue && imdbId.Value > 0)
        {
            options.Add("imdb_id", imdbId.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            options.Add("query", query.Trim());
        }

        if (normalizedType == "episode")
        {
            if (season.HasValue)
            {
                options.Add("season_number", season.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (episode.HasValue)
            {
                options.Add("episode_number", episode.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (year.HasValue)
        {
            options.Add("year", year.Value.ToString(CultureInfo.InvariantCulture));
        }

        return await SearchAndMapAsync(options, null, twoLetterIsoLanguageName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads a subtitle by id and writes it to disk next to the supplied media file using
    /// the <c>&lt;basename&gt;.&lt;language&gt;.srt</c> convention.
    /// </summary>
    /// <param name="id">The subtitle id returned by <see cref="Search"/> or <see cref="ManualSearchAsync"/>.</param>
    /// <param name="mediaPath">Absolute path to the media file the subtitle belongs next to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The absolute path of the written subtitle file.</returns>
    public async Task<string> DownloadToFileAsync(string id, string mediaPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mediaPath);

        var response = await GetSubtitlesInternal(id, cancellationToken).ConfigureAwait(false);

        var dir = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrEmpty(dir))
        {
            throw new ArgumentException("Could not determine destination directory", nameof(mediaPath));
        }

        var baseName = Path.GetFileNameWithoutExtension(mediaPath);
        var ext = string.IsNullOrEmpty(response.Format) ? "srt" : response.Format;
        var language = response.Language ?? "und";
        var suffix = string.Empty;
        if (response.IsHearingImpaired)
        {
            suffix += ".sdh";
        }

        if (response.IsForced)
        {
            suffix += ".forced";
        }

        var outPath = Path.Combine(dir, string.Format(CultureInfo.InvariantCulture, "{0}.{1}{2}.{3}", baseName, language, suffix, ext));

#pragma warning disable CA2007
        await using var output = File.Create(outPath);
        await response.Stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007

        return outPath;
    }

    /// <summary>
    /// Builds the free-text <c>query</c> field for an OpenSubtitles search. For episodes with a
    /// known series name this returns <c>"{SeriesName} S{Season:D2}E{Episode:D2}"</c>; for movies
    /// with a known item name it returns <see cref="SubtitleSearchRequest.Name"/>. In all other
    /// cases it falls back to the file name of <see cref="SubtitleSearchRequest.MediaPath"/>.
    /// </summary>
    /// <param name="request">The subtitle search request.</param>
    /// <returns>The search query string.</returns>
    internal static string BuildSearchQuery(SubtitleSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ContentType == VideoContentType.Episode
            && !string.IsNullOrWhiteSpace(request.SeriesName)
            && request.ParentIndexNumber.HasValue
            && request.IndexNumber.HasValue)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} S{1:D2}E{2:D2}",
                request.SeriesName,
                request.ParentIndexNumber.Value,
                request.IndexNumber.Value);
        }

        if (request.ContentType == VideoContentType.Movie && !string.IsNullOrWhiteSpace(request.Name))
        {
            return request.Name;
        }

        return Path.GetFileName(request.MediaPath ?? string.Empty);
    }

    /// <summary>
    /// Determines whether a single OpenSubtitles result should be kept after the API query has
    /// run. Hash-match-only and bad-subtitle pruning still apply; the season/episode and IMDb
    /// equality checks are toggled by <paramref name="strict"/>.
    /// </summary>
    /// <param name="data">The result entry to evaluate.</param>
    /// <param name="request">The originating request, or <c>null</c> for a manual search.</param>
    /// <param name="strict">Whether to enforce strict S/E or IMDb equality.</param>
    /// <param name="badSubtitleIds">Known-bad file ids to filter out for automated requests.</param>
    /// <returns><c>true</c> if the entry should be kept.</returns>
    internal static bool ShouldKeepResult(ResponseData data, SubtitleSearchRequest? request, bool strict, IReadOnlyCollection<int> badSubtitleIds)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(badSubtitleIds);

        if (data.Attributes?.Files is null || data.Attributes.Files.Count == 0)
        {
            return false;
        }

        var fileId = data.Attributes.Files[0].FileId;
        if (!fileId.HasValue)
        {
            return false;
        }

        if (request is { IsAutomated: true } && badSubtitleIds.Contains(fileId.Value))
        {
            return false;
        }

        if (request is not null)
        {
            var wantedType = request.ContentType == VideoContentType.Episode ? "Episode" : "Movie";
            if (data.Attributes.FeatureDetails?.FeatureType != wantedType)
            {
                return false;
            }

            if (strict)
            {
                if (request.ContentType == VideoContentType.Episode)
                {
                    if (data.Attributes.FeatureDetails?.SeasonNumber != request.ParentIndexNumber
                        || data.Attributes.FeatureDetails?.EpisodeNumber != request.IndexNumber)
                    {
                        return false;
                    }
                }
                else
                {
                    long.TryParse(
                        request.GetProviderId(MetadataProvider.Imdb)?.TrimStart('t') ?? string.Empty,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var imdbId);

                    if (imdbId != 0 && data.Attributes.FeatureDetails?.ImdbId != imdbId)
                    {
                        return false;
                    }
                }
            }

            if (request.IsPerfectMatch && !(data.Attributes.MovieHashMatch ?? false))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<IEnumerable<RemoteSubtitleInfo>> SearchAndMapAsync(
        Dictionary<string, string> options,
        SubtitleSearchRequest? request,
        string responseLanguage,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Search query: {Query}", options);

        var searchResponse = await OpenSubtitlesApi.SearchSubtitlesAsync(options, cancellationToken).ConfigureAwait(false);

        if (!searchResponse.Ok)
        {
            _logger.LogError("Invalid response: {Code} - {Body}", searchResponse.Code, searchResponse.Body);
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        if (searchResponse.Data is null)
        {
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        var strict = _configuration?.StrictMatching ?? true;

        return searchResponse.Data
            .Where(x => ShouldKeepResult(x, request, strict, _badSubtitleIds))
            .OrderByDescending(x => x.Attributes!.MovieHashMatch ?? false)
            .ThenByDescending(x => x.Attributes!.DownloadCount)
            .ThenByDescending(x => x.Attributes!.Ratings)
            .ThenByDescending(x => x.Attributes!.FromTrusted)
            .Select(i => new RemoteSubtitleInfo
            {
                Author = i.Attributes!.Uploader?.Name,
                Comment = i.Attributes.Comments,
                CommunityRating = i.Attributes.Ratings,
                DownloadCount = i.Attributes.DownloadCount,
                Format = "srt",
                ProviderName = Name,
                ThreeLetterISOLanguageName = responseLanguage,
                Id = BuildSubtitleId(responseLanguage, i),
                Name = i.Attributes.Release,
                DateCreated = i.Attributes.UploadDate,
                IsHashMatch = i.Attributes.MovieHashMatch,
                HearingImpaired = i.Attributes.HearingImpaired,
                MachineTranslated = i.Attributes.MachineTranslated,
                AiTranslated = i.Attributes.AiTranslated,
                FrameRate = i.Attributes.Fps,
                Forced = i.Attributes.ForeignPartsOnly
            })
            .ToList();
    }

    private string BuildSubtitleId(string language, ResponseData res)
    {
        var id = $"srt-{language}-{res.Attributes!.Files[0].FileId}";
        if (res.Attributes.HearingImpaired ?? false)
        {
            id += "-sdh";
        }

        if (res.Attributes.ForeignPartsOnly ?? false)
        {
            id += "-forced";
        }

        return id;
    }

    private async Task<SubtitleResponse> GetSubtitlesInternal(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Missing param", nameof(id));
        }

        if (_login?.User?.RemainingDownloads <= 0)
        {
            if (_limitReset < DateTime.UtcNow)
            {
                _logger.LogDebug("Reset time passed, updating user info");

                await UpdateUserInfo(cancellationToken).ConfigureAwait(false);

                // this shouldn't happen?
                if (_login.User.RemainingDownloads <= 0)
                {
                    _logger.LogError("OpenSubtitles download limit reached");
                    throw new RateLimitExceededException("OpenSubtitles download limit reached");
                }
            }
            else
            {
                _logger.LogError("OpenSubtitles download limit reached");
                throw new RateLimitExceededException("OpenSubtitles download limit reached");
            }
        }

        await Login(cancellationToken).ConfigureAwait(false);
        if (_login is null)
        {
            throw new AuthenticationException("Unable to login");
        }

        var idParts = id.Split('-');
        if (idParts.Length < 3)
        {
            throw new FormatException(string.Format(CultureInfo.InvariantCulture, "Invalid subtitle id format: {0}", id));
        }

        var format = idParts[0];
        var language = idParts[1];
        var fileId = int.Parse(idParts[2], CultureInfo.InvariantCulture);
        var isHearingImpaired = id.Contains("-sdh", StringComparison.OrdinalIgnoreCase);
        var isForced = id.Contains("-forced", StringComparison.OrdinalIgnoreCase);

        var info = await OpenSubtitlesApi
            .GetSubtitleLinkAsync(fileId, format, _login, cancellationToken)
            .ConfigureAwait(false);

        if (info.Data?.ResetTime is DateTime time)
        {
            UpdateResetTime(time);
        }

        if (!info.Ok)
        {
            switch (info.Code)
            {
                case HttpStatusCode.NotAcceptable when info.Data?.Remaining <= 0:
                {
                    if (_login.User is not null)
                    {
                        _login.User.RemainingDownloads = 0;
                    }

                    _logger.LogError("OpenSubtitles download limit reached");
                    throw new RateLimitExceededException("OpenSubtitles download limit reached");
                }

                case HttpStatusCode.Unauthorized:
                    _logger.LogDebug("Received Unauthorized while downloading subtitle {FileId}, resetting login", fileId);
                    // JWT token expired, obtain a new one and try again?
                    _login = null;
                    return await GetSubtitlesInternal(id, cancellationToken).ConfigureAwait(false);
            }

            var msg = info.Body.Contains("<html", StringComparison.OrdinalIgnoreCase) ? "[html]" : info.Body;

            msg = string.Format(
                CultureInfo.InvariantCulture,
                "Invalid response for file {0}: {1}\n\n{2}",
                fileId,
                info.Code,
                msg);

            throw new HttpRequestException(msg);
        }

        if (_login.User is not null)
        {
            _login.User.RemainingDownloads = info.Data?.Remaining;
            _logger.LogInformation("Remaining subtitle downloads: {RemainingDownloads}", _login.User.RemainingDownloads);
        }

        if (string.IsNullOrWhiteSpace(info.Data?.Link))
        {
            var msg = string.Format(
                CultureInfo.InvariantCulture,
                "Failed to obtain download link for file {0}: {1} (empty response)",
                fileId,
                info.Code);

            throw new HttpRequestException(msg);
        }

        var res = await OpenSubtitlesApi.DownloadSubtitleAsync(info.Data.Link, cancellationToken).ConfigureAwait(false);

        if (res.Code != HttpStatusCode.OK || string.IsNullOrWhiteSpace(res.Body))
        {
            var additionalMsg = string.Empty;
            if (res.Code == HttpStatusCode.OK && string.IsNullOrWhiteSpace(res.Body))
            {
                additionalMsg = " - this is most likely a broken subtitle, report at opensubtitles.com/contact and make sure to include the id";
                if (!_badSubtitleIds.Contains(fileId))
                {
                    _badSubtitleIds.Add(fileId);
                }
            }

            var msg = string.Format(
                CultureInfo.InvariantCulture,
                "Subtitle with Id {0} could not be downloaded: {1}{2}",
                fileId,
                res.Code,
                additionalMsg);

            throw new HttpRequestException(msg);
        }

        return new SubtitleResponse
        {
            Format = format,
            Language = language,
            Stream = new MemoryStream(Encoding.UTF8.GetBytes(res.Body)),
            IsForced = isForced,
            IsHearingImpaired = isHearingImpaired
        };
    }

    private async Task Login(CancellationToken cancellationToken)
    {
        if (_configuration is null || (_login is not null && DateTime.UtcNow < _login.ExpirationDate))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_configuration.Username) || string.IsNullOrWhiteSpace(_configuration.Password))
        {
            throw new AuthenticationException("Account username and/or password are not set up");
        }

        if (_configuration.CredentialsInvalid)
        {
            _logger.LogDebug("Skipping login due to credentials being invalid");
            return;
        }

        var loginResponse = await OpenSubtitlesApi.LogInAsync(
            _configuration.Username,
            _configuration.Password,
            cancellationToken).ConfigureAwait(false);

        if (!loginResponse.Ok)
        {
            // 400 = Using email, 401 = invalid credentials
            if ((loginResponse.Code == HttpStatusCode.BadRequest && _configuration.Username.Contains('@', StringComparison.OrdinalIgnoreCase))
                || loginResponse.Code == HttpStatusCode.Unauthorized)
            {
                _logger.LogError("Login failed due to invalid credentials, invalidating them ({Code} - {Body})", loginResponse.Code, loginResponse.Body);
                _configuration.CredentialsInvalid = true;
                OpenSubtitlesPlugin.Instance!.SaveConfiguration(_configuration);
            }
            else
            {
                _logger.LogError("Login failed: {Code} - {Body}", loginResponse.Code, loginResponse.Body);
            }

            throw new AuthenticationException("Authentication to OpenSubtitles failed.");
        }

        _login = loginResponse.Data;

        await UpdateUserInfo(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Logged in, download limit reset at {ResetTime}, token expiration at {ExpirationDate}", _limitReset, _login?.ExpirationDate);
    }

    private async Task UpdateUserInfo(CancellationToken cancellationToken)
    {
        if (_login is null)
        {
            return;
        }

        var infoResponse = await OpenSubtitlesApi.GetUserInfo(_login, cancellationToken).ConfigureAwait(false);
        if (infoResponse.Ok)
        {
            _login.User = infoResponse.Data?.Data;
            _limitReset = _login.User?.ResetTime;
        }
    }

    private async Task<string> GetLanguage(string language, string mediaPath, CancellationToken cancellationToken)
    {
        if (language == "zh")
        {
            language = "zh-CN";
        }
        else if (language == "pt")
        {
            language = "pt-PT";
        }

        if (_languages is null || _languages.Count == 0)
        {
            var res = await OpenSubtitlesApi.GetLanguageList(cancellationToken).ConfigureAwait(false);

            if (!res.Ok || res.Data?.Data is null)
            {
                throw new HttpRequestException(string.Format(CultureInfo.InvariantCulture, "Failed to get language list: {0}", res.Code));
            }

            _languages = res.Data.Data.Where(x => !string.IsNullOrWhiteSpace(x.Code)).Select(x => x.Code!).ToList();
        }

        var found = _languages.FirstOrDefault(x => string.Equals(x, language, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
        {
            return found;
        }

        if (language.Contains('-', StringComparison.OrdinalIgnoreCase))
        {
            return await GetLanguage(language.Split('-')[0], mediaPath, cancellationToken).ConfigureAwait(false);
        }

        throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture, "Language '{0}' is not supported ({1})", language, mediaPath));
    }

    internal void ConfigurationChanged(PluginConfiguration e)
    {
        _configuration = e;
        // force a login next time a request is made
        _login = null;
    }

    private void UpdateResetTime(DateTime resetTime)
    {
        // Do not update if the time is within 2s (api seems to return different values that are within a second or two of each other)
        if (_limitReset.HasValue && Math.Abs(_limitReset.Value.Subtract(resetTime).TotalSeconds) <= 2)
        {
            return;
        }

        _limitReset = resetTime;
        _logger.LogDebug("Updated expiration time to {ResetTime}", _limitReset);
    }
}
