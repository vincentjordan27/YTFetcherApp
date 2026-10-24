namespace YtFetcher.Application.Abstractions.Services;

/// <summary>Provides the persisted application user id responsible for the current operation.</summary>
public interface ICurrentActor
{
    Guid UserId { get; }
}
