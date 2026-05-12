using System;
using System.Collections.Generic;
using Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.OpenSubtitles.Tests;

public static class OpenSubtitleDownloaderTests
{
    [Fact]
    public static void BuildSearchQuery_EpisodeWithSeriesName_UsesShowAndCode()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            SeriesName = "Some Show",
            ParentIndexNumber = 1,
            IndexNumber = 5,
            MediaPath = "/media/Show.S01E05.1080p.WEB-DL.x264-GROUP.mkv"
        };

        Assert.Equal("Some Show S01E05", OpenSubtitleDownloader.BuildSearchQuery(request));
    }

    [Fact]
    public static void BuildSearchQuery_EpisodeMissingSeriesName_FallsBackToFileName()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            SeriesName = string.Empty,
            ParentIndexNumber = 1,
            IndexNumber = 5,
            MediaPath = "/media/Show.S01E05.mkv"
        };

        Assert.Equal("Show.S01E05.mkv", OpenSubtitleDownloader.BuildSearchQuery(request));
    }

    [Fact]
    public static void BuildSearchQuery_MovieWithName_PrefersRequestName()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = "The Big Lebowski",
            MediaPath = "/media/The.Big.Lebowski.1998.1080p.BluRay.x264.mkv"
        };

        Assert.Equal("The Big Lebowski", OpenSubtitleDownloader.BuildSearchQuery(request));
    }

    [Fact]
    public static void BuildSearchQuery_MovieWithoutName_FallsBackToFileName()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = string.Empty,
            MediaPath = "/media/movie.mkv"
        };

        Assert.Equal("movie.mkv", OpenSubtitleDownloader.BuildSearchQuery(request));
    }

    [Fact]
    public static void BuildSearchQuery_NullMediaPath_ReturnsEmpty()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie,
            Name = string.Empty,
            MediaPath = null!
        };

        Assert.Equal(string.Empty, OpenSubtitleDownloader.BuildSearchQuery(request));
    }

    [Fact]
    public static void BuildSearchQuery_PadsZeroes()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            SeriesName = "Show",
            ParentIndexNumber = 12,
            IndexNumber = 3,
            MediaPath = "/x.mkv"
        };

        Assert.Equal("Show S12E03", OpenSubtitleDownloader.BuildSearchQuery(request));
    }

    [Fact]
    public static void ShouldKeepResult_StrictEpisode_RejectsMismatchedSeasonOrEpisode()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            ParentIndexNumber = 1,
            IndexNumber = 5
        };

        var mismatch = MakeResponseData("Episode", season: 1, episode: 6);
        var match = MakeResponseData("Episode", season: 1, episode: 5);

        Assert.False(OpenSubtitleDownloader.ShouldKeepResult(mismatch, request, strict: true, Array.Empty<int>()));
        Assert.True(OpenSubtitleDownloader.ShouldKeepResult(match, request, strict: true, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_NonStrictEpisode_KeepsMismatchedSeasonOrEpisode()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            ParentIndexNumber = 1,
            IndexNumber = 5
        };

        var mismatch = MakeResponseData("Episode", season: 2, episode: 9);

        Assert.True(OpenSubtitleDownloader.ShouldKeepResult(mismatch, request, strict: false, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_NonStrictMovie_KeepsMismatchedImdb()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Movie
        };

        var entry = MakeResponseData("Movie", imdbId: 42);

        Assert.True(OpenSubtitleDownloader.ShouldKeepResult(entry, request, strict: false, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_RequiresFeatureType()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            ParentIndexNumber = 1,
            IndexNumber = 5
        };

        var movieEntry = MakeResponseData("Movie", season: 1, episode: 5);

        Assert.False(OpenSubtitleDownloader.ShouldKeepResult(movieEntry, request, strict: false, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_DropsBadSubtitlesForAutomatedRequest()
    {
        var request = new SubtitleSearchRequest
        {
            ContentType = VideoContentType.Episode,
            ParentIndexNumber = 1,
            IndexNumber = 5,
            IsAutomated = true
        };

        var entry = MakeResponseData("Episode", season: 1, episode: 5, fileId: 99);

        Assert.False(OpenSubtitleDownloader.ShouldKeepResult(entry, request, strict: true, new[] { 99 }));
        Assert.True(OpenSubtitleDownloader.ShouldKeepResult(entry, request, strict: true, Array.Empty<int>()));
    }

    [Fact]
    public static void ShouldKeepResult_ManualSearch_OnlyChecksFileAndType()
    {
        var entry = MakeResponseData("Episode", season: 99, episode: 99);

        Assert.True(OpenSubtitleDownloader.ShouldKeepResult(entry, request: null, strict: true, Array.Empty<int>()));
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

        Assert.False(OpenSubtitleDownloader.ShouldKeepResult(noFiles, request: null, strict: false, Array.Empty<int>()));
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
