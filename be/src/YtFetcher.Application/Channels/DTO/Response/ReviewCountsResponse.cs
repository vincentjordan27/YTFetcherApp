namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>
/// Navigation summary for the three channel ledgers. It exists so every visible tab badge is real
/// even while another tab is open.
/// </summary>
public sealed record ReviewCountsResponse(int Pending, int Approved, int Rejected);
