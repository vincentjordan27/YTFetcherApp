using YtFetcher.Application.Usage.DTO.Response;

namespace YtFetcher.Application.Abstractions.UseCases;

/// <summary>Application use-case boundary for reading the current Pacific day's quota ledger.</summary>
public interface IUsageUseCase
{
    Task<TodayUsageResponse> GetTodayAsync(CancellationToken cancellationToken);
}
