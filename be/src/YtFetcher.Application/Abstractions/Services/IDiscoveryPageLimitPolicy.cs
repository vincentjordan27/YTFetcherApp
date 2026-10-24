namespace YtFetcher.Application.Abstractions.Services;

/// <summary>Configured successful-page cap per keyword for one discovery run; zero means unlimited.</summary>
public interface IDiscoveryPageLimitPolicy
{
    int PageLimit { get; }
}
