// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Net;
using System.Xml;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace YTArchivarius.YouTube;

/// <summary>
/// In-memory, process-lifetime cache of the subscription feed. Each channel's uploads playlist is
/// paged lazily, only as far back as the oldest week requested so far. Nothing is written to disk.
/// </summary>
public sealed class FeedCache
{
    private const int ChannelParallelism = 8;

    private FeedState _state = new();
    private long _quotaUsed;

    /// <summary>Quota units spent by this process (every list call costs 1 unit).</summary>
    public long QuotaUsed => Interlocked.Read(ref _quotaUsed);

    public void Clear() => _state = new FeedState();

    public async Task<IReadOnlyList<VideoDto>> GetRangeAsync(
        UserCredential credential, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var state = _state;

        using var youTube = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "YTArchivarius",
        });

        var channels = await GetChannelsAsync(youTube, state, ct);

        var uploads = new ConcurrentBag<Upload>();
        await Parallel.ForEachAsync(
            channels,
            new ParallelOptions { MaxDegreeOfParallelism = ChannelParallelism, CancellationToken = ct },
            async (channel, token) =>
            {
                foreach (var upload in await GetChannelRangeAsync(youTube, channel, from, to, token))
                    uploads.Add(upload);
            });

        var distinct = uploads.DistinctBy(u => u.VideoId).ToList();
        await LoadDetailsAsync(youTube, state, distinct.Select(u => u.VideoId), ct);

        return distinct
            .Select(u => state.Details.GetValueOrDefault(u.VideoId) is { } details
                ? details with { PublishedAt = u.PublishedAt }
                : null)
            .OfType<VideoDto>()
            .OrderByDescending(v => v.PublishedAt)
            .ToList();
    }

    private async Task<IReadOnlyList<ChannelFeed>> GetChannelsAsync(
        YouTubeService youTube, FeedState state, CancellationToken ct)
    {
        if (state.Channels is { } cached)
            return cached;

        await state.ChannelsLock.WaitAsync(ct);
        try
        {
            if (state.Channels is { } loaded)
                return loaded;

            var subscriptions = new List<string>();
            string? pageToken = null;
            do
            {
                var request = youTube.Subscriptions.List("snippet");
                request.Mine = true;
                request.MaxResults = 50;
                request.PageToken = pageToken;
                var response = await ExecuteAsync(request, ct);

                foreach (var item in response.Items ?? [])
                {
                    if (item.Snippet?.ResourceId?.ChannelId is { } channelId)
                        subscriptions.Add(channelId);
                }

                pageToken = response.NextPageToken;
            } while (pageToken is not null);

            var channels = new List<ChannelFeed>();
            foreach (var chunk in subscriptions.Chunk(50))
            {
                var request = youTube.Channels.List("contentDetails");
                request.Id = chunk;
                request.MaxResults = 50;
                var response = await ExecuteAsync(request, ct);

                var uploadPlaylists = (response.Items ?? [])
                    .Where(c => c.ContentDetails?.RelatedPlaylists?.Uploads is not null)
                    .ToDictionary(c => c.Id, c => c.ContentDetails.RelatedPlaylists.Uploads);

                foreach (var id in chunk)
                {
                    if (uploadPlaylists.TryGetValue(id, out var playlistId))
                        channels.Add(new ChannelFeed(playlistId));
                }
            }

            state.Channels = channels;
            return channels;
        }
        finally
        {
            state.ChannelsLock.Release();
        }
    }

    private async Task<List<Upload>> GetChannelRangeAsync(
        YouTubeService youTube, ChannelFeed channel, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await channel.Lock.WaitAsync(ct);
        try
        {
            // The uploads playlist is newest first, so keep paging until we are past the start of the range.
            while (!channel.Exhausted && !(channel.Oldest < from))
            {
                var request = youTube.PlaylistItems.List("contentDetails");
                request.PlaylistId = channel.UploadsPlaylistId;
                request.MaxResults = 50;
                request.PageToken = channel.NextPageToken;

                PlaylistItemListResponse response;
                try
                {
                    response = await ExecuteAsync(request, ct);
                }
                catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
                {
                    // Channels without public uploads have no uploads playlist.
                    channel.Exhausted = true;
                    break;
                }

                foreach (var item in response.Items ?? [])
                {
                    var videoId = item.ContentDetails?.VideoId;
                    var publishedAt = item.ContentDetails?.VideoPublishedAtDateTimeOffset;
                    if (videoId is null || publishedAt is null || !channel.Seen.Add(videoId))
                        continue;

                    channel.Uploads.Add(new Upload(videoId, publishedAt.Value));
                    if (channel.Oldest is null || publishedAt < channel.Oldest)
                        channel.Oldest = publishedAt;
                }

                channel.NextPageToken = response.NextPageToken;
                if (channel.NextPageToken is null || response.Items is not { Count: > 0 })
                    channel.Exhausted = true;
            }

            return channel.Uploads.Where(u => u.PublishedAt >= from && u.PublishedAt < to).ToList();
        }
        finally
        {
            channel.Lock.Release();
        }
    }

    private async Task LoadDetailsAsync(
        YouTubeService youTube, FeedState state, IEnumerable<string> videoIds, CancellationToken ct)
    {
        var missing = videoIds.Where(id => !state.Details.ContainsKey(id)).ToList();
        foreach (var chunk in missing.Chunk(50))
        {
            var request = youTube.Videos.List("snippet,contentDetails");
            request.Id = chunk;
            request.MaxResults = 50;
            var response = await ExecuteAsync(request, ct);

            var found = (response.Items ?? []).ToDictionary(v => v.Id);
            foreach (var id in chunk)
            {
                // Private, deleted or otherwise unavailable videos are not returned; remember them as null.
                state.Details[id] = found.TryGetValue(id, out var video) ? ToDto(video) : null;
            }
        }
    }

    private static VideoDto ToDto(Video video)
    {
        var snippet = video.Snippet;
        var thumbnails = snippet?.Thumbnails;
        return new VideoDto(
            Id: video.Id,
            Title: snippet?.Title ?? video.Id,
            ChannelId: snippet?.ChannelId ?? "",
            ChannelTitle: snippet?.ChannelTitle ?? "",
            PublishedAt: snippet?.PublishedAtDateTimeOffset ?? DateTimeOffset.MinValue,
            ThumbnailUrl: (thumbnails?.Medium ?? thumbnails?.High ?? thumbnails?.Default__)?.Url,
            DurationSeconds: ParseDuration(video.ContentDetails?.Duration),
            LiveStatus: snippet?.LiveBroadcastContent ?? "none");
    }

    private static int? ParseDuration(string? isoDuration)
    {
        if (string.IsNullOrEmpty(isoDuration))
            return null;

        try
        {
            var seconds = (int)XmlConvert.ToTimeSpan(isoDuration).TotalSeconds;
            return seconds > 0 ? seconds : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task<T> ExecuteAsync<T>(YouTubeBaseServiceRequest<T> request, CancellationToken ct)
    {
        Interlocked.Increment(ref _quotaUsed);
        return await request.ExecuteAsync(ct);
    }

    private sealed class FeedState
    {
        public SemaphoreSlim ChannelsLock { get; } = new(1, 1);
        public IReadOnlyList<ChannelFeed>? Channels { get; set; }
        public ConcurrentDictionary<string, VideoDto?> Details { get; } = new();
    }

    private sealed class ChannelFeed(string uploadsPlaylistId)
    {
        public string UploadsPlaylistId { get; } = uploadsPlaylistId;

        public SemaphoreSlim Lock { get; } = new(1, 1);
        public List<Upload> Uploads { get; } = [];
        public HashSet<string> Seen { get; } = [];
        public DateTimeOffset? Oldest { get; set; }
        public string? NextPageToken { get; set; }
        public bool Exhausted { get; set; }
    }

    private sealed record Upload(string VideoId, DateTimeOffset PublishedAt);
}
