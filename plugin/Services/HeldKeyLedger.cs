using KeySender.Models;

namespace KeySender.Services;

/// <summary>
/// Plugin-level held/uncertain key ledger keyed by catalog token.
/// Mutations must occur only on the serial execution path owned by <see cref="ExecutionService"/>.
/// Successful Down while already held keeps a single entry (not reference-counted).
/// </summary>
public sealed class HeldKeyLedger
{
    private readonly Dictionary<string, HeldKeyEntry> _byToken = new(StringComparer.Ordinal);
    private readonly List<string> _holdOrder = [];
    private long _sequenceNumber;

    public bool IsEmpty => _byToken.Count == 0;

    public int Count => _byToken.Count;

    public bool Contains(string key) => key is not null && _byToken.ContainsKey(key);

    public IReadOnlyList<string> TokensInHoldOrder => _holdOrder;

    public IReadOnlyCollection<string> Tokens => _byToken.Keys;

    /// <summary>
    /// Record a successful (or uncertain) Down. Repeated Down keeps one entry and original source.
    /// </summary>
    public void NoteDown(
        KeyTokenInfo key,
        Guid runId,
        string nodeId,
        DateTimeOffset heldSince,
        bool uncertain = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        if (_byToken.TryGetValue(key.Token, out var existing))
        {
            if (uncertain)
            {
                existing.Uncertain = true;
            }

            Bump();
            return;
        }

        _byToken[key.Token] = new HeldKeyEntry
        {
            Key = key.Token,
            VirtualKey = key.VirtualKey,
            ScanCode = key.ScanCode,
            Extended = key.Extended,
            SourceRunId = runId,
            SourceNodeId = nodeId,
            HeldSince = heldSince,
            Uncertain = uncertain,
        };
        _holdOrder.Add(key.Token);
        Bump();
    }

    /// <summary>Remove after a successful Up. No-op when the key was not held.</summary>
    public void NoteUp(string key)
    {
        if (key is null)
        {
            return;
        }

        if (!_byToken.Remove(key))
        {
            return;
        }

        _holdOrder.Remove(key);
        Bump();
    }

    /// <summary>Reverse hold order for cleanup Ups (most recently held first).</summary>
    public IReadOnlyList<HeldKeyEntry> SnapshotReverseHoldOrder()
    {
        if (_holdOrder.Count == 0)
        {
            return [];
        }

        var list = new List<HeldKeyEntry>(_holdOrder.Count);
        for (var i = _holdOrder.Count - 1; i >= 0; i--)
        {
            if (_byToken.TryGetValue(_holdOrder[i], out var entry))
            {
                list.Add(Clone(entry));
            }
        }

        return list;
    }

    public HeldKeysSnapshot CreateSnapshot(bool isReleasing, bool senderAvailable)
    {
        var keys = new List<HeldKeyEntry>(_holdOrder.Count);
        foreach (var token in _holdOrder)
        {
            if (_byToken.TryGetValue(token, out var entry))
            {
                keys.Add(Clone(entry));
            }
        }

        return new HeldKeysSnapshot
        {
            SequenceNumber = _sequenceNumber,
            Keys = keys,
            IsReleasing = isReleasing,
            SenderAvailable = senderAvailable,
        };
    }

    public void Clear()
    {
        if (_byToken.Count == 0)
        {
            return;
        }

        _byToken.Clear();
        _holdOrder.Clear();
        Bump();
    }

    private void Bump() => _sequenceNumber++;

    private static HeldKeyEntry Clone(HeldKeyEntry entry) => new()
    {
        Key = entry.Key,
        VirtualKey = entry.VirtualKey,
        ScanCode = entry.ScanCode,
        Extended = entry.Extended,
        SourceRunId = entry.SourceRunId,
        SourceNodeId = entry.SourceNodeId,
        HeldSince = entry.HeldSince,
        Uncertain = entry.Uncertain,
    };
}
