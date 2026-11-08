namespace YtFetcher.Application.Settings.DTO;

public sealed record UpdateGeneralSettingsResult(
    GeneralSettingsResponse? Settings,
    IReadOnlyList<GeneralSettingsValidationError>? Errors)
{
    public bool IsValid => Errors is null;
}

public sealed record GeneralSettingsValidationError(string Field, string Message);
