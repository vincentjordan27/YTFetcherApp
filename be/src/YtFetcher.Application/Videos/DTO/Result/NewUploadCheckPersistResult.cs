namespace YtFetcher.Application.Videos.DTO.Result;

/// <summary>
/// Outcome of one committed M6 channel page. <see cref="OwnershipRetained"/> false means the daily owner moved
/// on and nothing was written; <see cref="ChannelStillEligible"/> false means the channel was deleted or left
/// the approved/completed set, so its marker was not advanced and a later date may revisit it.
/// </summary>
public sealed record NewUploadCheckPersistResult(
    bool OwnershipRetained,
    bool ChannelStillEligible,
    int InsertedCount,
    int SkippedKnownCount)
{
    /// <summary>The daily token no longer owns the date: no channel, video, snapshot, or marker change happened.</summary>
    public static NewUploadCheckPersistResult OwnershipLost() => new(false, false, 0, 0);

    /// <summary>The channel left the eligible set: no video, snapshot, or marker change happened.</summary>
    public static NewUploadCheckPersistResult ChannelUnavailable() => new(true, false, 0, 0);
}
