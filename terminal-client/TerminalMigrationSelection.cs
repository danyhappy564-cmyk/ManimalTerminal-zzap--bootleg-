using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using UnityEngine;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalMigrationSelection
{
	internal readonly struct Candidate
	{
		internal int Index { get; }

		internal ISpawnPoint SpawnPoint { get; }

		internal Candidate(int index, ISpawnPoint spawnPoint)
		{
			Index = index;
			SpawnPoint = spawnPoint;
		}
	}

	internal readonly struct Selection
	{
		internal ISpawnPoint SpawnPoint { get; }

		internal AICorePoint CorePoint { get; }

		internal Vector3 TargetPosition { get; }

		internal int SpawnIndex { get; }

		internal Selection(ISpawnPoint spawnPoint, AICorePoint corePoint, Vector3 targetPosition, int spawnIndex)
		{
			SpawnPoint = spawnPoint;
			CorePoint = corePoint;
			TargetPosition = targetPosition;
			SpawnIndex = spawnIndex;
		}
	}

	internal delegate Candidate SpawnSelector(IReadOnlyList<ISpawnPoint> points);

	internal static bool TrySelect(BotOwner bot, BotZone destination, out Selection selection, out TerminalMigrationProbe.Result diagnostic)
	{
		return Evaluate(bot, destination, SelectRandom, out selection, out diagnostic);
	}

	internal static bool Evaluate(BotOwner bot, BotZone destination, SpawnSelector selector, out Selection selection, out TerminalMigrationProbe.Result diagnostic)
	{
		//IL_023f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0382: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a6: Unknown result type (might be due to invalid IL or missing references)
		selection = default;
		if (bot == null || destination == null)
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.Unavailable, default, null, -1, 0);
			return false;
		}
		try
		{
			if (bot.SpawnBotZone == destination)
			{
				diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.SameZone, default, null, -1, 0);
				return false;
			}
		}
		catch
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.Unavailable, default, null, -1, 0);
			return false;
		}
		ISpawnPoint[] spawnPoints;
		try
		{
			spawnPoints = destination.SpawnPoints;
		}
		catch
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.Unavailable, default, null, -1, 0);
			return false;
		}
		if (spawnPoints == null || spawnPoints.Length == 0)
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.NoSpawn, default, null, -1, 0);
			return false;
		}
		Candidate candidate;
		try
		{
			candidate = selector(spawnPoints);
		}
		catch
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.Unavailable, default, null, -1, 0);
			return false;
		}
		if (candidate.Index < 0 || candidate.Index >= spawnPoints.Length || candidate.SpawnPoint == null)
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.Unavailable, default, null, candidate.Index, 0);
			return false;
		}
		Vector3 position;
		int corePointId;
		try
		{
			position = candidate.SpawnPoint.Position;
			corePointId = candidate.SpawnPoint.CorePointId;
		}
		catch
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.Unavailable, default, null, candidate.Index, 0);
			return false;
		}
		AICorePoint aICorePoint;
		try
		{
			List<AICorePoint> list = bot.BotsController?.CoversData?.AICorePointsHolder?.CorePoints;
			if (list == null)
			{
				diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.MissingCore, position, corePointId, candidate.Index, 0);
				return false;
			}
			aICorePoint = ValueEnumerableExtensions.FirstOrDefault<FromList<AICorePoint>, AICorePoint>(ValueEnumerable.AsValueEnumerable<AICorePoint>(list), (Func<AICorePoint, bool>)((AICorePoint point) => point != null && point.Id == corePointId));
		}
		catch
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.MissingCore, position, corePointId, candidate.Index, 0);
			return false;
		}
		if (aICorePoint == null)
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.MissingCore, position, corePointId, candidate.Index, 0);
			return false;
		}
		Vector3 vector;
		try
		{
			vector = ((bot.MyHead != null) ? bot.MyHead.position : (bot.Position + Vector3.up * 1.6f));
		}
		catch
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.Unavailable, position, corePointId, candidate.Index, 0);
			return false;
		}
		GameWorld instance;
		try
		{
			instance = Singleton<GameWorld>.Instance;
		}
		catch
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.Unavailable, position, corePointId, candidate.Index, 0);
			return false;
		}
		if (instance == null || instance.AllAlivePlayersList == null)
		{
			diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.Unavailable, position, corePointId, candidate.Index, 0);
			return false;
		}
		int num = 0;
		Player[] array = ValueEnumerableExtensions.ToArray<Player>(ValueEnumerableExtensions.Where<Player>(ValueEnumerable.AsValueEnumerable<Player>(instance.AllAlivePlayersList), (Func<Player, bool>)((Player player2) => player2 != null && !player2.IsAI)));
		foreach (Player player in array)
		{
			num++;
			Vector3 vector2;
			try
			{
				vector2 = player.Position + Vector3.up * 1.6f;
			}
			catch
			{
				diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.Unavailable, position, corePointId, candidate.Index, num);
				return false;
			}
			if ((vector2 - vector).sqrMagnitude <= 10000f)
			{
				diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.NearPlayer, position, corePointId, candidate.Index, num);
				return false;
			}
			if (!Physics.Linecast(vector2, vector, LayersMaskController.HighPolyWithTerrainMask))
			{
				diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.VisibleToPlayer, position, corePointId, candidate.Index, num);
				return false;
			}
		}
		selection = new Selection(candidate.SpawnPoint, aICorePoint, position, candidate.Index);
		diagnostic = new TerminalMigrationProbe.Result(TerminalMigrationProbe.DecisionReason.EligibleProjection, position, corePointId, candidate.Index, num);
		return true;
	}

	private static Candidate SelectRandom(IReadOnlyList<ISpawnPoint> points)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		return ValueEnumerableExtensions.ToArray<Select2<FromEnumerable<ISpawnPoint>, ISpawnPoint, Candidate>, Candidate>(ValueEnumerableExtensions.Select<FromEnumerable<ISpawnPoint>, ISpawnPoint, Candidate>(ValueEnumerable.AsValueEnumerable<ISpawnPoint>((IEnumerable<ISpawnPoint>)points), (Func<ISpawnPoint, int, Candidate>)((ISpawnPoint point, int index) => new Candidate(index, point)))).RandomElement();
	}
}
