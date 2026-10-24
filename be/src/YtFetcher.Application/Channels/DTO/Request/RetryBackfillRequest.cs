namespace YtFetcher.Application.Channels.DTO.Request;

/// <summary>
/// Request data for retrying a terminally failed backfill page. It carries no value today; it exists so
/// every non-GET channel action accepts a body and the use-case signature stays stable when fields are
/// added. The channel that is retried is identified by the route.
/// </summary>
public sealed record RetryBackfillRequest;
