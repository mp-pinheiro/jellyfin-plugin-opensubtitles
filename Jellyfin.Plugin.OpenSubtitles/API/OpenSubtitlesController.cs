using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.OpenSubtitles.API;

/// <summary>
/// The open subtitles plugin controller.
/// </summary>
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(Policy = Policies.SubtitleManagement)]
public class OpenSubtitlesController : ControllerBase
{
    /// <summary>
    /// Validates login info.
    /// </summary>
    /// <remarks>
    /// Accepts plugin configuration as JSON body.
    /// </remarks>
    /// <response code="200">Login info valid.</response>
    /// <response code="400">Login info is missing data.</response>
    /// <response code="401">Login info not valid.</response>
    /// <param name="body">The request body.</param>
    /// <returns>
    /// An <see cref="NoContentResult"/> if the login info is valid, a <see cref="BadRequestResult"/> if the request body missing is data
    /// or <see cref="UnauthorizedResult"/> if the login info is not valid.
    /// </returns>
    [HttpPost("Jellyfin.Plugin.OpenSubtitles/ValidateLoginInfo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ValidateLoginInfo([FromBody] LoginInfoInput body)
    {
        var response = await OpenSubtitlesApi.LogInAsync(
            body.Username,
            body.Password,
            CancellationToken.None).ConfigureAwait(false);

        if (!response.Ok)
        {
            var msg = $"{response.Code}{(response.Body.Length < 150 ? $" - {response.Body}" : string.Empty)}";

            if (response.Body.Contains("message\":", StringComparison.Ordinal))
            {
                var err = JsonSerializer.Deserialize<ErrorResponse>(response.Body);
                if (err is not null)
                {
                    msg = string.Equals(err.Message, "You cannot consume this service", StringComparison.Ordinal) ? "Invalid API key provided" : err.Message;
                }
            }

            return Unauthorized(new { Message = msg });
        }

        if (response.Data is not null)
        {
            await OpenSubtitlesApi.LogOutAsync(response.Data, CancellationToken.None).ConfigureAwait(false);
        }

        return Ok(new { Downloads = response.Data?.User?.AllowedDownloads ?? 0 });
    }

    /// <summary>
    /// Performs a manual subtitle search bypassing the strict S/E or IMDb post-filters.
    /// </summary>
    /// <param name="body">The manual search request body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// An <see cref="OkObjectResult"/> with the list of matching subtitles, a <see cref="BadRequestResult"/>
    /// if the body is missing required fields, or a <see cref="StatusCodes.Status503ServiceUnavailable"/>
    /// if the downloader has not been initialized yet.
    /// </returns>
    [HttpPost("Jellyfin.Plugin.OpenSubtitles/Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> ManualSearch([FromBody] ManualSearchRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body.Language))
        {
            return BadRequest(new { Message = "language is required" });
        }

        if (string.IsNullOrWhiteSpace(body.Query) && (!body.ImdbId.HasValue || body.ImdbId.Value <= 0))
        {
            return BadRequest(new { Message = "query or imdbId is required" });
        }

        var downloader = OpenSubtitleDownloader.Instance;
        if (downloader is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { Message = "Plugin not initialized" });
        }

        try
        {
            var results = await downloader.ManualSearchAsync(
                body.Query,
                body.Language,
                string.IsNullOrWhiteSpace(body.Type) ? "movie" : body.Type,
                body.Season,
                body.Episode,
                body.ImdbId,
                body.Year,
                cancellationToken).ConfigureAwait(false);

            return Ok(results.Select(r => new ManualSearchResult
            {
                Id = r.Id,
                Name = r.Name,
                Author = r.Author,
                Comment = r.Comment,
                CommunityRating = r.CommunityRating,
                DownloadCount = r.DownloadCount,
                Format = r.Format,
                ThreeLetterISOLanguageName = r.ThreeLetterISOLanguageName,
                IsHashMatch = r.IsHashMatch,
                HearingImpaired = r.HearingImpaired,
                MachineTranslated = r.MachineTranslated,
                AiTranslated = r.AiTranslated,
                Forced = r.Forced,
                FrameRate = r.FrameRate,
                DateCreated = r.DateCreated
            }).ToList());
        }
        catch (System.Security.Authentication.AuthenticationException ex)
        {
            return Unauthorized(new { Message = ex.Message });
        }
        catch (MediaBrowser.Common.Extensions.RateLimitExceededException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Downloads a subtitle by id and writes it to disk next to the supplied media file.
    /// </summary>
    /// <param name="body">The download request body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// An <see cref="OkObjectResult"/> with the written path, a <see cref="BadRequestResult"/> if the
    /// body is missing required fields, or a <see cref="StatusCodes.Status503ServiceUnavailable"/>
    /// if the downloader has not been initialized.
    /// </returns>
    [HttpPost("Jellyfin.Plugin.OpenSubtitles/Download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> ManualDownload([FromBody] ManualDownloadRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body.Id) || string.IsNullOrWhiteSpace(body.DestinationPath))
        {
            return BadRequest(new { Message = "id and destinationPath are required" });
        }

        var downloader = OpenSubtitleDownloader.Instance;
        if (downloader is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { Message = "Plugin not initialized" });
        }

        try
        {
            var written = await downloader.DownloadToFileAsync(body.Id, body.DestinationPath, cancellationToken).ConfigureAwait(false);
            return Ok(new { Path = written });
        }
        catch (System.Security.Authentication.AuthenticationException ex)
        {
            return Unauthorized(new { Message = ex.Message });
        }
        catch (MediaBrowser.Common.Extensions.RateLimitExceededException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Returns the OpenSubtitles language list for populating the manual-search dropdown.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list of supported language codes and names.</returns>
    [HttpGet("Jellyfin.Plugin.OpenSubtitles/Languages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<LanguageOption>>> GetLanguages(CancellationToken cancellationToken)
    {
        var response = await OpenSubtitlesApi.GetLanguageList(cancellationToken).ConfigureAwait(false);

        if (!response.Ok || response.Data?.Data is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { Message = $"Failed to fetch language list: {response.Code}" });
        }

        return Ok(response.Data.Data
            .Where(l => !string.IsNullOrWhiteSpace(l.Code))
            .Select(l => new LanguageOption { Code = l.Code!, Name = l.Code! })
            .ToList());
    }
}
