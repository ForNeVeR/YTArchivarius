// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace YTArchivarius.YouTube;

public sealed record VideoDto(
    string Id,
    string Title,
    string ChannelId,
    string ChannelTitle,
    DateTimeOffset PublishedAt,
    string? ThumbnailUrl,
    int? DurationSeconds,
    string LiveStatus);

public sealed record WeekResponse(IReadOnlyList<VideoDto> Videos, long QuotaUsed);

public sealed record StatusResponse(bool Configured, bool SignedIn, DateOnly HistoryStart, long QuotaUsed);
