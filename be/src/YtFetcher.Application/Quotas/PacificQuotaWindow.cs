namespace YtFetcher.Application.Quotas;

/// <summary>
/// The one Application owner of the Pacific quota-calendar policy. Every daily ledger date and quota-window
/// boundary is derived from this type, so no consumer keeps its own timezone lookup or handwritten conversion.
/// It never reads the clock; callers keep using injected <see cref="TimeProvider"/>.
/// </summary>
public static class PacificQuotaWindow
{
    public const string WindowsTimeZoneId = "Pacific Standard Time";

    public static TimeZoneInfo TimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById(WindowsTimeZoneId);

    /// <summary>Converts a UTC instant into its Pacific calendar date.</summary>
    public static DateOnly GetQuotaDate(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, TimeZone).DateTime);

    /// <summary>Interprets midnight for the supplied Pacific calendar date and returns that instant in UTC.</summary>
    public static DateTimeOffset GetQuotaDateStartUtc(DateOnly quotaDate) =>
        new(TimeZoneInfo.ConvertTimeToUtc(
            quotaDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            TimeZone));
}
