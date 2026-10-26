using System;
using System.Collections.Generic;

namespace Manimal.Terminal;

internal sealed class TerminalGateGroupState
{
	internal const int RequiredPlayers = 2;

	internal const double HoldSeconds = 5.0;

	internal const double FinishWindowSeconds = 3.0;

	private readonly HashSet<string> _waiting = new HashSet<string>(StringComparer.Ordinal);

	private readonly Dictionary<string, double> _authorized = new Dictionary<string, double>(StringComparer.Ordinal);

	private readonly HashSet<string> _finished = new HashSet<string>(StringComparer.Ordinal);

	private readonly HashSet<string> _completedProfiles = new HashSet<string>(StringComparer.Ordinal);

	private string[] _lastReadyProfiles = Array.Empty<string>();

	private double _firstFinishedAt = -1.0;

	private bool _completed;

	internal int Count => _waiting.Count + _authorized.Count + _completedProfiles.Count;

	internal bool IsReady => _lastReadyProfiles.Length == 2;

	internal bool IsCompleting
	{
		get
		{
			if (_firstFinishedAt >= 0.0)
			{
				return !_completed;
			}
			return false;
		}
	}

	internal bool IsCompleted => _completed;

	internal double FirstFinishedAt => _firstFinishedAt;

	internal bool Contains(string profileId)
	{
		if (!string.IsNullOrEmpty(profileId))
		{
			if (!_waiting.Contains(profileId) && !_authorized.ContainsKey(profileId))
			{
				return _completedProfiles.Contains(profileId);
			}
			return true;
		}
		return false;
	}

	internal string[] SnapshotProfiles()
	{
		string[] array = new string[Count];
		_waiting.CopyTo(array, 0);
		_authorized.Keys.CopyTo(array, _waiting.Count);
		_completedProfiles.CopyTo(array, _waiting.Count + _authorized.Count);
		return array;
	}

	internal string[] SnapshotReadyProfiles()
	{
		return (string[])_lastReadyProfiles.Clone();
	}

	internal TerminalGateGroupResult Register(string profileId, double now)
	{
		if (string.IsNullOrEmpty(profileId) || _completed || Contains(profileId))
		{
			return TerminalGateGroupResult.Rejected;
		}
		_waiting.Add(profileId);
		if (_waiting.Count != 2)
		{
			return TerminalGateGroupResult.Registered;
		}
		string[] array = new string[2];
		_waiting.CopyTo(array);
		_waiting.Clear();
		_lastReadyProfiles = array;
		string[] array2 = array;
		foreach (string key in array2)
		{
			_authorized[key] = now;
		}
		return TerminalGateGroupResult.Ready;
	}

	internal bool Cancel(string profileId)
	{
		if (string.IsNullOrEmpty(profileId) || !Contains(profileId))
		{
			return false;
		}
		_waiting.Remove(profileId);
		_authorized.Remove(profileId);
		_completedProfiles.Remove(profileId);
		_finished.Remove(profileId);
		if (_finished.Count == 0)
		{
			_firstFinishedAt = -1.0;
		}
		_lastReadyProfiles = Array.Empty<string>();
		_completed = false;
		return true;
	}

	internal TerminalGateGroupResult Finish(string profileId, double now)
	{
		if (string.IsNullOrEmpty(profileId) || !_authorized.TryGetValue(profileId, out var value) || _finished.Contains(profileId) || _completed)
		{
			return TerminalGateGroupResult.Rejected;
		}
		if (now - value < 5.0)
		{
			return TerminalGateGroupResult.Early;
		}
		if (_firstFinishedAt >= 0.0 && now - _firstFinishedAt > 3.0)
		{
			ExpireFinishWindow(now);
		}
		_finished.Add(profileId);
		_authorized.Remove(profileId);
		_completedProfiles.Add(profileId);
		if (_finished.Count < 2)
		{
			_firstFinishedAt = now;
			return TerminalGateGroupResult.FirstFinished;
		}
		_completed = true;
		return TerminalGateGroupResult.Completed;
	}

	internal string[] ExpireFinishWindow(double now)
	{
		if (_firstFinishedAt < 0.0 || now - _firstFinishedAt <= 3.0)
		{
			return Array.Empty<string>();
		}
		string[] array = new string[_completedProfiles.Count];
		_completedProfiles.CopyTo(array);
		_finished.Clear();
		_completedProfiles.Clear();
		_firstFinishedAt = -1.0;
		return array;
	}

	internal void Reset()
	{
		_waiting.Clear();
		_authorized.Clear();
		_finished.Clear();
		_completedProfiles.Clear();
		_lastReadyProfiles = Array.Empty<string>();
		_firstFinishedAt = -1.0;
		_completed = false;
	}
}
