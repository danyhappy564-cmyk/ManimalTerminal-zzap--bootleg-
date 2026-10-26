using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Manimal.Terminal.Civilian;

internal static class CivilianFleeState
{
	public sealed class State
	{
		public float LastThreatSeenAt = -999f;

		public float CoweringUntil;

		public bool InCover;

		public Vector3 HomePosition;

		public bool HasHome;

		public float LastGunshotAt = -999f;

		public Vector3 LastGunshotPos;

		public bool MotionRequested;
	}

	private static readonly Dictionary<string, State> States = new Dictionary<string, State>();

	public static State Get(BotOwner owner)
	{
		string text = owner?.Profile?.Id;
		if (string.IsNullOrEmpty(text))
		{
			return new State();
		}
		if (!States.TryGetValue(text, out var value))
		{
			value = new State();
			States[text] = value;
		}
		return value;
	}

	public static bool TryGet(BotOwner owner, out State state)
	{
		string text = owner?.Profile?.Id;
		if (string.IsNullOrEmpty(text))
		{
			state = null;
			return false;
		}
		return States.TryGetValue(text, out state);
	}

	public static void ClearAll()
	{
		States.Clear();
	}

	public static void EnsureHomeInitialized(BotOwner owner)
	{
		if (!(owner == null))
		{
			State state = Get(owner);
			if (!state.HasHome)
			{
				state.HomePosition = owner.Position;
				state.HasHome = true;
			}
		}
	}
}
