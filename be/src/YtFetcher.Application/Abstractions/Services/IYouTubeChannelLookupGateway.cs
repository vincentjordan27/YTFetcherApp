using YtFetcher.Application.Channels.DTO.Result;

namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// YouTube channel-handle lookup boundary for the manual add flow. It is separate from
/// <see cref="IYouTubeChannelDiscoveryGateway"/> because one handle lookup and keyword discovery have
/// different input filters and different callers.
/// </summary>
public interface IYouTubeChannelLookupGateway
{
    Task<ManualChannelLookupResult> LookupByHandleAsync(string handle, CancellationToken cancellationToken);
}
