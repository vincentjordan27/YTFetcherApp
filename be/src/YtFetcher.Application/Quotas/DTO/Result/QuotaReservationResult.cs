namespace YtFetcher.Application.Quotas.DTO.Result;

/// <summary>
/// Outcome of one attempt to reserve a unit from a daily provider quota bucket, or of the paired first-page
/// slot claim. The distinction between a missing window and a genuinely full bucket is what lets an operator
/// receive a readable 503 or 429 instead of a silent provider call.
/// </summary>
public enum QuotaReservationResult
{
    /// <summary>Exactly one unit (or one paired slot) was reserved for this Pacific date.</summary>
    Reserved,

    /// <summary>
    /// The date's first-page window was never initialized, so no reservation is valid and no provider call is
    /// allowed yet. Approval maps this to 503.
    /// </summary>
    WindowNotReady,

    /// <summary>
    /// The bucket still has a live marker but no remaining capacity after protected first-page slots.
    /// The caller makes no provider call.
    /// </summary>
    NoRemainingCapacity,

    /// <summary>
    /// The bucket was already reported exhausted by the provider for this date. The caller makes no provider
    /// call and does not re-mark it.
    /// </summary>
    QuotaExhausted,

    /// <summary>
    /// A daily-owner-fenced reservation found that the supplied token no longer owns a live, non-completed
    /// date. The caller makes no provider call, sets no marker, and the daily run stops without finalizing.
    /// </summary>
    OwnershipLost
}
