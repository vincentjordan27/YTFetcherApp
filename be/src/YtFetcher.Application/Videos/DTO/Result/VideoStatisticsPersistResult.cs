namespace YtFetcher.Application.Videos.DTO.Result;

/// <summary>
/// Outcome of applying one successful provider statistics batch. Ownership is reported rather than assumed:
/// when a row is no longer owned by the supplied claim, nothing at all may be persisted for that batch.
/// </summary>
public sealed record VideoStatisticsPersistResult(bool OwnershipRetained, int Updated, int Unavailable)
{
    /// <summary>No ownership, so no row was written.</summary>
    public static VideoStatisticsPersistResult OwnershipLost() => new(false, 0, 0);
}
