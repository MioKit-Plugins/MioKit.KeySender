using System.Runtime.InteropServices;
using KeySender.Models;
using KeySender.Validation;

namespace KeySender.Services;

/// <summary>
/// Samples GetAsyncKeyState before the event sequence (design 03 §8).
/// Does not inject Ups for physical keys.
/// </summary>
public sealed class Win32ExternalKeyPrep : IExternalKeyPrep
{
    public const int MaxWaitMs = 2000;
    public const int PollMs = 10;

    private readonly IDelayService _delay;

    public Win32ExternalKeyPrep(IDelayService delay)
    {
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public async Task WaitReadyAsync(
        IReadOnlySet<string> ledgerHeldTokens,
        IReadOnlySet<string> sequenceExplicitFirstUps,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ledgerHeldTokens);
        ArgumentNullException.ThrowIfNull(sequenceExplicitFirstUps);

        var allowed = new HashSet<ushort>();
        foreach (var token in ledgerHeldTokens.Concat(sequenceExplicitFirstUps))
        {
            if (KeyTokenCatalog.TryGet(token, out var info))
            {
                allowed.Add(info.VirtualKey);
            }
        }

        var deadline = Environment.TickCount64 + MaxWaitMs;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasDisallowedDown(allowed))
            {
                return;
            }

            if (Environment.TickCount64 >= deadline)
            {
                throw new InvalidOperationException($"{ValidationCodes.KeysReleaseTimeout}: external keys still down");
            }

            await _delay.DelayAsync(PollMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool HasDisallowedDown(HashSet<ushort> allowed)
    {
        foreach (var info in KeyTokenCatalog.All)
        {
            if (allowed.Contains(info.VirtualKey))
            {
                continue;
            }

            if (IsDown(info.VirtualKey))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDown(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
