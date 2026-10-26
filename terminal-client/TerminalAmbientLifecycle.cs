using System;

namespace Manimal.Terminal;

internal sealed class TerminalAmbientLifecycle
{
	internal const int MaxAttempts = 3;

	internal TerminalAmbientPhase Phase { get; private set; }

	internal int Attempts { get; private set; }

	internal bool IsConstructed
	{
		get
		{
			if (Phase != TerminalAmbientPhase.Constructing && Phase != TerminalAmbientPhase.Staged && Phase != TerminalAmbientPhase.Ready)
			{
				return Phase == TerminalAmbientPhase.Fallback;
			}
			return true;
		}
	}

	internal bool IsReady => Phase == TerminalAmbientPhase.Ready;

	internal bool IsFallback => Phase == TerminalAmbientPhase.Fallback;

	internal bool BeginAttempt()
	{
		if (Phase == TerminalAmbientPhase.Ready || Phase == TerminalAmbientPhase.Fallback)
		{
			return false;
		}
		if (Attempts >= 3)
		{
			return false;
		}
		Attempts++;
		Phase = TerminalAmbientPhase.Constructing;
		return true;
	}

	internal void MarkStaged()
	{
		if (Phase != TerminalAmbientPhase.Constructing)
		{
			throw new InvalidOperationException("Ambient tree must be constructing before it is staged.");
		}
		Phase = TerminalAmbientPhase.Staged;
	}

	internal void MarkReady()
	{
		if (Phase != TerminalAmbientPhase.Constructing && Phase != TerminalAmbientPhase.Staged)
		{
			throw new InvalidOperationException("Ambient tree must be staged before it is ready.");
		}
		Phase = TerminalAmbientPhase.Ready;
	}

	internal bool RecordDeferredReadiness()
	{
		if (Phase != TerminalAmbientPhase.Staged)
		{
			throw new InvalidOperationException("Only a staged ambient tree can defer readiness.");
		}
		if (Attempts >= 3)
		{
			return false;
		}
		Attempts++;
		return true;
	}

	internal TerminalAmbientFailure FailAttempt()
	{
		if (Phase != TerminalAmbientPhase.Constructing && Phase != TerminalAmbientPhase.Staged)
		{
			throw new InvalidOperationException("Only an active ambient attempt can fail.");
		}
		if (Attempts < 3)
		{
			Phase = TerminalAmbientPhase.Idle;
			return new TerminalAmbientFailure(retry: true, fallback: false);
		}
		Phase = TerminalAmbientPhase.Fallback;
		return new TerminalAmbientFailure(retry: false, fallback: true);
	}

	internal void Reset()
	{
		Attempts = 0;
		Phase = TerminalAmbientPhase.Idle;
	}

	internal bool InvalidateReadyGraph()
	{
		if (Phase != TerminalAmbientPhase.Ready && Phase != TerminalAmbientPhase.Staged)
		{
			return false;
		}
		Phase = TerminalAmbientPhase.Fallback;
		return true;
	}
}
