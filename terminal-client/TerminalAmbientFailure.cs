namespace Manimal.Terminal;

internal readonly struct TerminalAmbientFailure
{
	internal bool Retry { get; }

	internal bool UseFallback { get; }

	internal TerminalAmbientFailure(bool retry, bool fallback)
	{
		Retry = retry;
		UseFallback = fallback;
	}
}
