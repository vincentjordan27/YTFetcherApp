namespace YtFetcher.Application.Settings.DTO;

public sealed record UpdateGeneralSettingsResult(
    GeneralSettingsResponse? Settings,
    IReadOnlyDictionary<string, string[]>? Errors)
{
    public bool IsValid => Errors is null;
}
