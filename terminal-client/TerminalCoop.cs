using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalCoop
{
	internal static Func<bool> IsAuthority;

	internal static Action<TerminalEvent, string, string, int> Send;

	internal static Action<Player> Plant;

	internal static bool Applying;

	internal static bool Replaying;

	internal static bool RaidStarted;

	internal static bool SyncReady;

	internal static double IntroStarted = -1.0;

	internal static bool IntroReleased;

	internal static bool AttackReleased;

	internal static double AttackStarted = -1.0;

	internal static Func<double> SharedClock;

	private static bool _fikaRaidActive;

	private static readonly List<Player> Nearby = new List<Player>();

	internal static double Now => SharedClock?.Invoke() ?? Time.realtimeSinceStartupAsDouble;

	internal static bool Active
	{
		get
		{
			if (!_fikaRaidActive)
			{
				AbstractGame instance = Singleton<AbstractGame>.Instance;
				if ((object)instance == null)
				{
					return false;
				}
				return instance.GetType().Name.IndexOf("Coop", StringComparison.OrdinalIgnoreCase) >= 0;
			}
			return true;
		}
	}

	internal static bool Authority
	{
		get
		{
			if (Active)
			{
				return IsAuthority?.Invoke() ?? false;
			}
			return true;
		}
	}

	internal static bool LocalHuman => IsHuman(Singleton<GameWorld>.Instance?.MainPlayer);

	internal static event Action RaidReset;

	internal static bool IsHuman(Player player)
	{
		if (player != null && !player.IsAI && player.HealthController != null && player.HealthController.IsAlive)
		{
			return !(player.Profile?.Info?.Nickname ?? "").StartsWith("headless_", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	internal static void CollectHumans(List<Player> into)
	{
		into.Clear();
		GameWorld instance = Singleton<GameWorld>.Instance;
		if (instance?.RegisteredPlayers != null)
		{
			foreach (IPlayer registeredPlayer in instance.RegisteredPlayers)
			{
				if (registeredPlayer is Player player && IsHuman(player) && !into.Contains(player))
				{
					into.Add(player);
				}
			}
		}
		if (IsHuman(instance?.MainPlayer) && !into.Contains(instance.MainPlayer))
		{
			into.Add(instance.MainPlayer);
		}
	}

	internal static Player NearestHuman(Vector3 position)
	{
		CollectHumans(Nearby);
		Player result = null;
		float num = float.MaxValue;
		foreach (Player item in Nearby)
		{
			float sqrMagnitude = (item.Position - position).sqrMagnitude;
			if (sqrMagnitude < num)
			{
				num = sqrMagnitude;
				result = item;
			}
		}
		return result;
	}

	internal static void Request(TerminalEvent kind, string key = "", string actor = "", int number = 0)
	{
		if (Active && (!Applying || kind == TerminalEvent.IntroDone || kind == TerminalEvent.AttackDone))
		{
			if (Send == null)
			{
				Plugin.Log.LogError("[TerminalCoop] Install the matching Terminal Fika addon on every peer, including the headless host.");
			}
			else
			{
				Send(kind, key, actor, number);
			}
		}
	}

	internal static void MarkFikaRaid()
	{
		_fikaRaidActive = true;
	}

	internal static void Reset()
	{
		_fikaRaidActive = false;
		Applying = (Replaying = (RaidStarted = (SyncReady = (IntroReleased = (AttackReleased = false)))));
		IntroStarted = -1.0;
		AttackStarted = -1.0;
		RaidReset?.Invoke();
	}
}
