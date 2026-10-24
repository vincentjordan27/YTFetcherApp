using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>Outcome of one committed page-persistence transaction.</summary>
public sealed record BackfillPagePersistResult(
    int VideosInserted,
    int VideosSkipped,
    BackfillStatus BackfillStatus,
    bool HasNextPage);
