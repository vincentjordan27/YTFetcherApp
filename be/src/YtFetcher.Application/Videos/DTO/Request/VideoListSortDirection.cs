namespace YtFetcher.Application.Videos.DTO.Request;

/// <summary>The one shared sort-direction contract for both paged video lists.</summary>
public enum VideoListSortDirection
{
    /// <summary>Lowest/earliest first.</summary>
    Asc,

    /// <summary>Highest/latest first. This is the default for both video lists.</summary>
    Desc
}
