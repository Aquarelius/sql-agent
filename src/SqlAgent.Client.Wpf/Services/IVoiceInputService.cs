namespace SqlAgent.Client.Wpf.Services;

/// <summary>
/// The replaceable boundary for voice input (CD-50 T9 DoD: "voice input is behind a replaceable service
/// interface"). The chat view only knows this contract, so a real speech-to-text engine can be dropped in
/// later without touching any view model. <see cref="IsSupported"/> lets the UI hide the mic button when no
/// engine is wired.
/// </summary>
public interface IVoiceInputService
{
    /// <summary>True when a real engine is available. The default implementation returns false.</summary>
    bool IsSupported { get; }

    /// <summary>Captures one utterance and returns the recognized text, or null if nothing was recognized.</summary>
    Task<string?> CaptureAsync(CancellationToken ct = default);
}

/// <summary>
/// No-op voice service shipped by default: there is no bundled speech engine yet, so it reports unsupported
/// and never returns text. Swapping in a real engine is a one-line change in <c>App</c>.
/// ponytail: a stub, not a TODO — the interface is the deliverable; the engine is a separate task.
/// </summary>
public sealed class NullVoiceInputService : IVoiceInputService
{
    public bool IsSupported => false;

    public Task<string?> CaptureAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
}
