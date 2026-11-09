using KeySender.Models;
using KeySender.Services;

namespace MioKit.KeySender.Tests.Fakes;

public sealed class FakeKeyboardSender : IKeyboardSender
{
    private readonly object _sync = new();
    private readonly List<(string Key, bool KeyDown)> _submitted = [];

    public IReadOnlyList<(string Key, bool KeyDown)> Submitted
    {
        get
        {
            lock (_sync)
            {
                return _submitted.ToArray();
            }
        }
    }

    /// <summary>When set, the next SendAsync returns this result instead of success.</summary>
    public KeyboardSendResult? NextResult { get; set; }

    /// <summary>Optional per-call override. Return null to use default/NextResult.</summary>
    public Func<KeyTokenInfo, bool, int, KeyboardSendResult?>? Resolver { get; set; }

    public Task<KeyboardSendResult> SendAsync(
        KeyTokenInfo key,
        bool keyDown,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);

        int index;
        lock (_sync)
        {
            index = _submitted.Count;
            _submitted.Add((key.Token, keyDown));
        }

        var resolved = Resolver?.Invoke(key, keyDown, index);
        if (resolved is { } custom)
        {
            return Task.FromResult(custom);
        }

        if (NextResult is { } once)
        {
            NextResult = null;
            return Task.FromResult(once);
        }

        return Task.FromResult(KeyboardSendResult.Ok());
    }
}
