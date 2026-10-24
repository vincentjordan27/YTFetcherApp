namespace YtFetcher.Application.Channels.DTO.Result;

/// <summary>
/// Normalized outcome of one manual channel handle lookup. It never carries a raw provider DTO or
/// provider error text.
/// </summary>
public enum ManualChannelLookupStatus
{
    Found,
    NotFound,
    Unavailable
}
