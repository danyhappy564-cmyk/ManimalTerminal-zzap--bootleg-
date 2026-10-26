using System.Collections.Generic;
using BepInEx.Logging;
using EFT;
using UnityEngine;

namespace Manimal.Terminal.Civilian;

internal static class CivilianUnstickHelper
{
	public enum Status
	{
		Moving,
		Attempted,
		NeedsRepath
	}

	private sealed class StuckState
	{
		public Vector3 SamplePos;

		public float SampleAt;

		public float StalledSince = -1f;

		public float NextAttemptAt;

		public int FailedAttempts;
	}

	private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Terminal.Civ");

	private static readonly Dictionary<BotOwner, StuckState> States = new Dictionary<BotOwner, StuckState>();

	private const float SampleInterval = 0.5f;

	private const float StuckSampleDistance = 0.5f;

	private const float StuckTriggerSeconds = 2f;

	private const float MinRemainingDistance = 4f;

	private const float AttemptCooldownSeconds = 2f;

	private const int FailedAttemptsBeforeRepath = 2;

	public static Status Check(BotOwner bot, Vector3 destination)
	{
		if (bot == null)
		{
			return Status.Moving;
		}
		Player getPlayer = bot.GetPlayer;
		if (getPlayer == null)
		{
			return Status.Moving;
		}
		if (bot.Mover == null || !bot.Mover.IsMoving)
		{
			if (States.TryGetValue(bot, out var value))
			{
				ResetStall(value);
			}
			return Status.Moving;
		}
		float time = Time.time;
		if (!States.TryGetValue(bot, out var value2))
		{
			value2 = new StuckState
			{
				SamplePos = bot.Position,
				SampleAt = time
			};
			States[bot] = value2;
			return Status.Moving;
		}
		if (time - value2.SampleAt < 0.5f)
		{
			return Status.Moving;
		}
		Vector3 position = bot.Position;
		float num = Vector3.Distance(position, value2.SamplePos);
		value2.SamplePos = position;
		value2.SampleAt = time;
		if (Vector3.Distance(position, destination) < 4f)
		{
			ResetStall(value2);
			return Status.Moving;
		}
		if (num >= 0.5f)
		{
			ResetStall(value2);
			return Status.Moving;
		}
		if (value2.StalledSince < 0f)
		{
			value2.StalledSince = time;
		}
		if (time - value2.StalledSince < 2f)
		{
			return Status.Moving;
		}
		if (time < value2.NextAttemptAt)
		{
			return Status.Moving;
		}
		value2.FailedAttempts++;
		value2.NextAttemptAt = time + 2f;
		value2.StalledSince = time;
		if (value2.FailedAttempts >= 2)
		{
			Log.LogInfo($"[Unstick] {bot.Profile?.Nickname} still stuck after {value2.FailedAttempts} attempt(s); repathing");
			value2.FailedAttempts = 0;
			return Status.NeedsRepath;
		}
		TryUnstick(bot, getPlayer);
		return Status.Attempted;
	}

	public static void Reset(BotOwner bot)
	{
		if (!(bot == null))
		{
			States.Remove(bot);
		}
	}

	public static void ClearAll()
	{
		States.Clear();
	}

	private static void ResetStall(StuckState state)
	{
		state.StalledSince = -1f;
		state.FailedAttempts = 0;
	}

	private static void TryUnstick(BotOwner bot, Player player)
	{
		MovementContext movementContext = player.MovementContext;
		if (movementContext == null || !movementContext.TryVaulting())
		{
			player.Jump();
		}
	}
}
