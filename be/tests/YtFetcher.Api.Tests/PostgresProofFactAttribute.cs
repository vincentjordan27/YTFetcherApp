namespace YtFetcher.Api.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> for proofs that need a real PostgreSQL target. The delegation says
/// the target is supplied to the test process as <c>YTF_TEST_POSTGRES</c>; while it is absent the
/// proof is reported as skipped instead of silently passing or failing.
/// </summary>
public sealed class PostgresProofFactAttribute : FactAttribute
{
    public override string? Skip
    {
        get => PostgresProofTarget.IsAvailable
            ? null
            : "Set YTF_TEST_POSTGRES to a disposable PostgreSQL database to run this proof.";
        set => base.Skip = value;
    }
}
