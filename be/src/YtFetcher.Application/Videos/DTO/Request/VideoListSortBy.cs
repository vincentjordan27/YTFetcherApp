namespace YtFetcher.Application.Videos.DTO.Request;

/// <summary>
/// The one shared sort-column contract for both paged video lists. Only these persisted columns may be
/// ordered on, and the wire values are the member names.
/// </summary>
public enum VideoListSortBy
{
    /// <summary>Video title, case-insensitive, with the channel name as a deterministic secondary key.</summary>
    Title,

    /// <summary>Current view count.</summary>
    Views,

    /// <summary>Current like count.</summary>
    Likes,

    /// <summary>Publication timestamp.</summary>
    PublishedAt,

    /// <summary>Last statistics fetch timestamp; rows without one always sort last.</summary>
    LastUpdatedAt
}
