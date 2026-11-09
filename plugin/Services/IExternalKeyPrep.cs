namespace KeySender.Services;

/// <summary>
/// Pre-sequence external key readiness (design 03 §8). Fakeable for tests.
/// </summary>
public interface IExternalKeyPrep
{
    /// <summary>
    /// Wait up to 2000 ms for disallowed physically-down keys to release.
    /// Ledger-held keys and sequence-first explicit Ups are allowed to stay down.
    /// </summary>
    Task WaitReadyAsync(
        IReadOnlySet<string> ledgerHeldTokens,
        IReadOnlySet<string> sequenceExplicitFirstUps,
        CancellationToken cancellationToken = default);
}

/// <summary>No-op prep used by default in unit tests and when Win32 sampling is injected separately.</summary>
public sealed class NoOpExternalKeyPrep : IExternalKeyPrep
{
    public Task WaitReadyAsync(
        IReadOnlySet<string> ledgerHeldTokens,
        IReadOnlySet<string> sequenceExplicitFirstUps,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
