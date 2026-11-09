namespace KeySender.Models;

/// <summary>Result of a single SendInput(1) submission.</summary>
public readonly struct KeyboardSendResult
{
    public KeyboardSendResult(bool submitted, int nativeError = 0, bool uncertain = false)
    {
        Submitted = submitted;
        NativeError = nativeError;
        Uncertain = uncertain;
    }

    public bool Submitted { get; }

    public int NativeError { get; }

    /// <summary>True when the outcome cannot be trusted (exception / ambiguous failure on Down).</summary>
    public bool Uncertain { get; }

    public static KeyboardSendResult Ok() => new(submitted: true);

    public static KeyboardSendResult Failed(int nativeError) => new(submitted: false, nativeError);

    public static KeyboardSendResult Ambiguous(int nativeError = 0)
        => new(submitted: false, nativeError, uncertain: true);
}
