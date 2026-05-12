using System;
using System.Collections.Generic;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses;
using Xunit;

namespace Jellyfin.Plugin.OpenSubtitles.Tests;

public static class OpenSubtitleDownloaderTests
{
    [Fact]
    public static void BuildSearchQuery_EpisodeWithSeriesName_UsesShowAndCode()
    {
        Assert.Equal(
            "Some Show S01E05",
            OpenSubtitleDownloader.BuildSearchQuery(
                isEpisode: true,
                seriesName: "Some Show",
                season: 1,
                episode: 5,
                name: null,
                mediaPath: "/media/Show.S01E05.1080p.WEB-DL.x264-GROUP.mkv"));
    }

    [Fact]
    public static void BuildSearchQuery_EpisodeMissingSeriesName_FallsBackToFileName()
    {
        Assert.Equal(
            "Show.S01E05.mkv",
            OpenSubtitleDownloader.BuildSearchQuery(
                isEpisode: true,
                seriesName: string.Empty,
                season: 1,
                episode: 5,
                name: null,
                mediaPath: "/media/Show.S01E05.mkv"));
    }

    [Fact]
    public static void BuildSearchQuery_MovieWithName_PrefersName()
    {
        Assert.Equal(
            "The Big Lebowski",
            OpenSubtitleDownloader.BuildSearchQuery(
                isEpisode: false,
                seriesName: null,
                season: null,
                episode: null,
                name: "The Big Lebowski",
                mediaPath: "/media/The.Big.Lebowski.1998.1080p.BluRay.x264.mkv"));
    }

    [Fact]
    public static void BuildSearchQuery_MovieWithoutName_FallsBackToFileName()
    {
        Assert.Equal(
            "movie.mkv",
            OpenSubtitleDownloader.BuildSearchQuery(
                isEpisode: false,
                seriesName: null,
                season: null,
                episode: null,
                name: null,
                mediaPath: "/media/movie.mkv"));
    }

    [Fact]
    public static void BuildSearchQuery_NullMediaPath_ReturnsEmpty()
    {
        Assert.Equal(
            string.Empty,
            OpenSubtitleDownloader.BuildSearchQuery(
                isEpisode: false,
                seriesName: null,
                season: null,
                episode: null,
                name: null,
                mediaPath: null));
    }

    [Fact]
    public static void BuildSearchQuery_PadsZeroes()
    {
        Assert.Equal(
            "Show S12E03",
            OpenSubtitleDownloader.BuildSearchQuery(
                isEpisode: true,
                seriesName: "Show",
                season: 12,
                episode: 3,
                name: null,
                mediaPath: "/x.mkv"));
    }

    [Fact]
    public static void ShouldKeepResult_StrictEpisode_RejectsMismatchedSeasonOrEpisode()
    {
        var criteria = MakeCriteria(isEpisode: true, season: 1, episode: 5);
        var mismatch = MakeResponseData("Episode", season: 1, episode: 6);
        var match = MakeResponseData("Episode", season: 1, episode: 5);

        Assert.False(OpenSubtitleDownloader.ShouldKeepResult(mismatch, criteria, strict: true, Array.Empty<int>()));
        Assert.True(OpenSubtitleDownloader.ShouldKeepResult(match, criteria, strict: true, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_NonStrictEpisode_KeepsMismatchedSeasonOrEpisode()
    {
        var criteria = MakeCriteria(isEpisode: true, season: 1, episode: 5);
        var mismatch = MakeResponseData("Episode", season: 2, episode: 9);

        Assert.True(OpenSubtitleDownloader.ShouldKeepResult(mismatch, criteria, strict: false, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_StrictMovie_RejectsMismatchedImdb()
    {
        var criteria = MakeCriteria(isEpisode: false, imdbId: 100);
        var entry = MakeResponseData("Movie", imdbId: 42);

        Assert.False(OpenSubtitleDownloader.ShouldKeepResult(entry, criteria, strict: true, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_NonStrictMovie_KeepsMismatchedImdb()
    {
        var criteria = MakeCriteria(isEpisode: false, imdbId: 100);
        var entry = MakeResponseData("Movie", imdbId: 42);

        Assert.True(OpenSubtitleDownloader.ShouldKeepResult(entry, criteria, strict: false, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_RequiresFeatureType()
    {
        var criteria = MakeCriteria(isEpisode: true, season: 1, episode: 5);
        var movieEntry = MakeResponseData("Movie", season: 1, episode: 5);

        Assert.False(OpenSubtitleDownloader.ShouldKeepResult(movieEntry, criteria, strict: false, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_DropsBadSubtitlesForAutomatedRequest()
    {
        var criteria = MakeCriteria(isEpisode: true, season: 1, episode: 5, isAutomated: true);
        var entry = MakeResponseData("Episode", season: 1, episode: 5, fileId: 99);

        Assert.False(OpenSubtitleDownloader.ShouldKeepResult(entry, criteria, strict: true, new[] { 99 }));
        Assert.True(OpenSubtitleDownloader.ShouldKeepResult(entry, criteria, strict: true, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_ManualSearch_OnlyChecksFileAndType()
    {
        var entry = MakeResponseData("Episode", season: 99, episode: 99);

        Assert.True(OpenSubtitleDownloader.ShouldKeepResult(entry, criteria: null, strict: true, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_DropsEntriesWithoutFileId()
    {
        var noFiles = new ResponseData
        {
            Attributes = new Attributes
            {
                FeatureDetails = new FeatureDetails { FeatureType = "Movie" }
            }
        };

        Assert.False(OpenSubtitleDownloader.ShouldKeepResult(noFiles, criteria: null, strict: false, Array.Empty<int>()));
    }

    private static MatchCriteria MakeCriteria(bool isEpisode, int? season = null, int? episode = null, long imdbId = 0, bool isAutomated = false, bool isPerfectMatch = false)
    {
        return new MatchCriteria(isEpisode, season, episode, imdbId, isAutomated, isPerfectMatch);
    }

    private static ResponseData MakeResponseData(string featureType, int? season = null, int? episode = null, int? imdbId = null, int fileId = 1)
    {
        return new ResponseData
        {
            Attributes = new Attributes
            {
                FeatureDetails = new FeatureDetails
                {
                    FeatureType = featureType,
                    SeasonNumber = season,
                    EpisodeNumber = episode,
                    ImdbId = imdbId
                },
                Files = new List<SubFile>
                {
                    new SubFile { FileId = fileId }
                }
            }
        };
    }
}
