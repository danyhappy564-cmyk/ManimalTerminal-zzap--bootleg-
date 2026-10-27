using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using UnityEngine;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalBotMigration
{
	private readonly struct PendingTiltTask
	{
		internal AITaskManager Manager { get; }

		internal int Id { get; }

		internal PendingTiltTask(AITaskManager manager, int id)
		{
			Manager = manager;
			Id = id;
		}
	}

	private sealed class MigrationContext
	{
		internal BotOwner Bot;

		internal BotZone Destination;

		internal TerminalMigrationSelection.Selection Selection;

		internal BotsGroup OldGroup;

		internal IBotGame BotGame;

		internal BotsController Controller;

		internal BotSpawner Spawner;

		internal BotZone OldZone;

		internal AICorePoint OldCore;

		internal Vector3 OldPosition;

		internal IPlayer OldGoalEnemyPerson;

		internal BossGroup OldBossGroup;

		internal IBossToFollow OldFollowerBoss;

		internal int OldFollowerIndex;

		internal ABossLogic OldBossLogic;

		internal int OldFollowersTargetCount;

		internal bool OldIamBoss;

		internal IPlayer[] OldEnemyKeys;

		internal IPlayer[] OldGroupEnemyKeys;

		internal int RaidGeneration;

		internal BotsGroup NewGroup;

		internal PendingTiltTask TiltTask;

		internal bool TiltTaskScheduled;

		internal bool BrainRestartStarted;

		internal bool BrainDisposed;

		internal bool BrainActivateStarted;

		internal bool BrainActivated;

		internal bool OldGroupAllyRemoved;

		internal bool SearchDisposeStarted;

		internal bool SearchDisposed;

		internal bool PatrollingDisposeStarted;

		internal bool PatrollingDisposed;

		internal bool PatrollingActivated;

		internal bool OldFollowerDetached;

		internal bool FollowerDisposeStarted;

		internal bool FollowerDisposed;

		internal bool FollowerActivated;

		internal bool OldBossGroupDetached;

		internal bool BossSetStarted;

		internal bool NewBossSet;

		internal bool NewGroupRegistrationAttempted;

		internal bool NewGroupRegistered;

		internal bool NewGroupMemberAdded;

		internal bool RequestsUnbound;

		internal bool Committed;
	}

	internal const float TiltStopDelay = 3f;

	private const EBotEnemyCause MigrationEnemyCause = EBotEnemyCause.initCauseEnemy;

	private static readonly List<PendingTiltTask> PendingTiltTasks = new List<PendingTiltTask>();

	private static readonly HashSet<BotOwner> InFlight = new HashSet<BotOwner>();

	// zzap: Ghost reads the existing migration owner, no duplicate state machine.
	internal static bool IsInFlight(BotOwner bot) => InFlight.Contains(bot);

	private static int _raidGeneration;

	internal static event Action<BotOwner> OnMigrated;

	internal static void ResetForRaid()
	{
		for (int i = 0; i < PendingTiltTasks.Count; i++)
		{
			PendingTiltTask pendingTiltTask = PendingTiltTasks[i];
			try
			{
				pendingTiltTask.Manager?.CancelDelayedTask(pendingTiltTask.Id);
			}
			catch
			{
			}
		}
		PendingTiltTasks.Clear();
		InFlight.Clear();
		_raidGeneration++;
		OnMigrated = null;
	}

	internal static bool TryMigrateTo(BotOwner bot, BotZone destination, out string reason)
	{
		using var scope = TerminalTickProfiler.Measure("Migration.TryMigrateTo");
		reason = "unavailable";
		if (!ScopeIsActive())
		{
			reason = "scope-inactive";
			return false;
		}
		if (bot == null || destination == null)
		{
			reason = "unavailable";
			return false;
		}
		TerminalGhostDirector.WakeNow(bot, TerminalGhostDirector.WakeReason.Move);
		if (!InFlight.Add(bot))
		{
			reason = "migration-in-flight";
			return false;
		}
		try
		{
			TerminalMigrationSelection.Selection selection;
			try
			{
				if (!TerminalMigrationSelection.TrySelect(bot, destination, out selection, out var diagnostic))
				{
					reason = "ineligible:" + diagnostic.ReasonCode;
					return false;
				}
			}
			catch (Exception ex)
			{
				reason = "selection-failed:" + ex.GetType().Name;
				return false;
			}
			if (!TryCreateContext(bot, destination, selection, out var context, out reason))
			{
				return false;
			}
			try
			{
				Execute(context);
				TerminalGhostDirector.GroupChanged(bot);
				reason = "migrated";
				if (CanInvokeMigrationCallback(context))
				{
					try
					{
						OnMigrated?.Invoke(bot);
					}
					catch
					{
					}
				}
				return true;
			}
			catch (Exception ex2)
			{
				reason = "migration-failed:" + ex2.GetType().Name;
				Rollback(context);
				return false;
			}
		}
		finally
		{
			InFlight.Remove(bot);
		}
	}

	private static bool CanInvokeMigrationCallback(MigrationContext context)
	{
		try
		{
			return context != null && context.RaidGeneration == _raidGeneration && ScopeIsActive() && context.Bot != null && context.Bot.BotState == EBotState.Active && context.Bot.HealthController != null && context.Bot.HealthController.IsAlive;
		}
		catch
		{
			return false;
		}
	}

	private static bool ScopeIsActive()
	{
		try
		{
			return TerminalGate.On && TerminalCoop.Authority && Plugin.StageDirector != null && Plugin.StageDirector.Value;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryCreateContext(BotOwner bot, BotZone destination, TerminalMigrationSelection.Selection selection, out MigrationContext context, out string reason)
	{
		//IL_02d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0317: Unknown result type (might be due to invalid IL or missing references)
		//IL_033b: Unknown result type (might be due to invalid IL or missing references)
		context = null;
		reason = "preflight-failed";
		try
		{
			if (bot.BotState != EBotState.Active || bot.HealthController == null || !bot.HealthController.IsAlive || bot.GetPlayer == null || bot.BotsGroup == null || bot.Memory == null || bot.Memory.GoalTarget == null || bot.Memory.BotCurrentCoverInfo == null || bot.Brain == null || bot.Brain.BaseBrain == null || bot.Brain.Agent == null || bot.Mover == null || bot.Tilt == null || bot.BotFollower == null || bot.PatrollingData == null || bot.SearchData == null || bot.BotRequestController == null || bot.LookSensor == null || bot.Boss == null || bot.Boss._followers == null || bot.EnemiesController == null || bot.AITaskManager == null || selection.SpawnPoint == null || selection.CorePoint == null)
			{
				return false;
			}
			BotsGroup botsGroup = bot.BotsGroup;
			if (!botsGroup.Contains(bot))
			{
				return false;
			}
			if (!TerminalMigrationBrainCompat.TryValidate(bot, out var reason2))
			{
				reason = "brain-preflight:" + reason2;
				return false;
			}
			IBotGame botGame = botsGroup.BotGame;
			BotsController botsController = botGame?.BotsController;
			BotSpawner botSpawner = botsController?.BotSpawner;
			if (botGame == null || botsController == null || botSpawner == null || botSpawner.Groups == null || botSpawner._allPlayers == null || botSpawner._deadBodiesController == null || botsGroup.Enemies == null || bot.EnemiesController.EnemyInfos == null)
			{
				return false;
			}
			_ = bot.Profile.Info.Side;
			_ = bot.Profile.Info.Settings.Role;
			context = new MigrationContext
			{
				Bot = bot,
				Destination = destination,
				Selection = selection,
				OldGroup = botsGroup,
				BotGame = botGame,
				Controller = botsController,
				Spawner = botSpawner,
				OldZone = bot.SpawnBotZone,
				OldCore = bot.StartCorePoint,
				OldPosition = bot.Position,
				OldGoalEnemyPerson = bot.Memory.GoalEnemy?.Person,
				OldBossGroup = botsGroup.BossGroup,
				OldIamBoss = bot.Boss.IamBoss,
				OldFollowerBoss = bot.BotFollower.BossToFollow,
				OldFollowerIndex = bot.BotFollower.Index,
				OldBossLogic = bot.Boss.BossLogic,
				OldFollowersTargetCount = (bot.Boss._followers?.TargetFollowersCount ?? 0),
				OldEnemyKeys = ValueEnumerableExtensions.ToArray<FromEnumerable<IPlayer>, IPlayer>(ValueEnumerableExtensions.Where<FromEnumerable<IPlayer>, IPlayer>(ValueEnumerable.AsValueEnumerable<IPlayer>((IEnumerable<IPlayer>)bot.EnemiesController.EnemyInfos.Keys), (Func<IPlayer, bool>)((IPlayer enemy) => enemy != null))),
				OldGroupEnemyKeys = ValueEnumerableExtensions.ToArray<FromEnumerable<IPlayer>, IPlayer>(ValueEnumerableExtensions.Where<FromEnumerable<IPlayer>, IPlayer>(ValueEnumerable.AsValueEnumerable<IPlayer>((IEnumerable<IPlayer>)botsGroup.Enemies.Keys), (Func<IPlayer, bool>)((IPlayer enemy) => enemy != null))),
				RaidGeneration = _raidGeneration
			};
			return true;
		}
		catch
		{
			context = null;
			return false;
		}
	}

	private static void Execute(MigrationContext context)
	{
		//IL_025b: Unknown result type (might be due to invalid IL or missing references)
		//IL_027f: Unknown result type (might be due to invalid IL or missing references)
		BotOwner bot = context.Bot;
		BotsGroup oldGroup = context.OldGroup;
		bot.Mover.Stop();
		bot.SpawnBotZone = context.Destination;
		bot.StartCorePoint = context.Selection.CorePoint;
		context.BrainRestartStarted = true;
		TerminalMigrationBrainCompat.BeforeRestart(bot);
		bot.Brain.Dispose();
		context.BrainDisposed = true;
		context.BrainActivateStarted = true;
		TerminalMigrationBrainCompat.BeforeRestart(bot);
		bot.Brain.Activate();
		context.BrainActivated = true;
		int id = bot.AITaskManager.RegisterDelayedTask(bot, 3f, () =>
		{
			try
			{
				bot.Tilt.Stop();
			}
			catch
			{
			}
		});
		context.TiltTask = new PendingTiltTask(bot.AITaskManager, id);
		context.TiltTaskScheduled = true;
		PendingTiltTasks.Add(context.TiltTask);
		bot.Tilt.Stop();
		context.OldGroupAllyRemoved = true;
		oldGroup.RemoveAlly(bot);
		bot.Memory.GoalEnemy = null;
		bot.Memory.GoalTarget.Clear();
		context.SearchDisposeStarted = true;
		bot.SearchData.Dispose();
		context.SearchDisposed = true;
		bot.SearchData.Activate();
		if (bot.BotFollower.HaveBoss)
		{
			bot.BotFollower.BossToFollow.RemoveFollower(bot);
			context.OldFollowerDetached = true;
		}
		context.PatrollingDisposeStarted = true;
		bot.PatrollingData.Dispose();
		bot.PatrollingData.IsActivated = false;
		ResetPatrollingOwnership(bot);
		context.PatrollingDisposed = true;
		if (context.OldIamBoss && oldGroup.BossGroup != null && oldGroup.BossGroup.Boss == bot)
		{
			oldGroup.BossGroup.Dispose();
			oldGroup.BossGroup = null;
			context.OldBossGroupDetached = true;
		}
		bot.Mover.Teleport(context.Selection.TargetPosition);
		context.Spawner.SetBotAsEnemy(bot);
		List<BotOwner> enemies = ValueEnumerableExtensions.ToList<Where<FromEnumerable<BotOwner>, BotOwner>, BotOwner>(ValueEnumerableExtensions.Where<FromEnumerable<BotOwner>, BotOwner>(ValueEnumerable.AsValueEnumerable<BotOwner>(context.Spawner.GetBotEnemiesList(bot)), (Func<BotOwner, bool>)((BotOwner enemy) => enemy != null)));
		BotsGroup botsGroup = (context.NewGroup = new BotsGroup(context.Destination, context.Spawner.BotGame, bot, enemies, context.Spawner._deadBodiesController, context.Spawner._allPlayers, forBoss: true));
		context.NewGroupRegistrationAttempted = true;
		if (context.Spawner._freeForAll)
		{
			context.Spawner.Groups.AddNoKey(botsGroup, context.Destination);
		}
		else
		{
			context.Spawner.Groups.Add(context.Destination, bot.Side, botsGroup, isBossOrFollower: true);
		}
		context.NewGroupRegistered = true;
		bot.BotsGroup = botsGroup;
		bot.Memory._botsGroup = botsGroup;
		botsGroup.AddMember(bot, onActivation: true);
		context.NewGroupMemberAdded = botsGroup.Contains(bot);
		if (!context.NewGroupMemberAdded)
		{
			throw new InvalidOperationException("new-group-member-add-failed");
		}
		context.BossSetStarted = true;
		bot.Boss.SetBoss(0);
		context.NewBossSet = true;
		RebuildEnemyInfos(context);
		bot.PatrollingData.Activate();
		context.PatrollingActivated = true;
		context.FollowerDisposeStarted = true;
		bot.BotFollower.Dispose();
		context.FollowerDisposed = true;
		bot.BotFollower.Activate();
		context.FollowerActivated = true;
		bot.Mover.Stop();
		bot.Memory.Spotted(byHit: false);
		bot.Memory.BotCurrentCoverInfo.Spotted();
		bot.LookSensor.UpdateZoneValue(context.Destination);
		bot.BotRequestController.Dispose();
		bot.BotRequestController.CurRequest = null;
		bot.BotRequestController._groupRequestController = null;
		context.RequestsUnbound = true;
		context.Committed = true;
	}

	private static void RebuildEnemyInfos(MigrationContext context)
	{
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		BotEnemiesController enemiesController = context.Bot.EnemiesController;
		IPlayer[] oldEnemyKeys = context.OldEnemyKeys;
		foreach (IPlayer player in oldEnemyKeys)
		{
			if (player != null && enemiesController.EnemyInfos.ContainsKey(player))
			{
				enemiesController.Remove(player);
			}
		}
		oldEnemyKeys = ValueEnumerableExtensions.ToArray<Distinct<Where<Concat<FromArray<IPlayer>, FromEnumerable<IPlayer>, IPlayer>, IPlayer>, IPlayer>, IPlayer>(ValueEnumerableExtensions.Distinct<Where<Concat<FromArray<IPlayer>, FromEnumerable<IPlayer>, IPlayer>, IPlayer>, IPlayer>(ValueEnumerableExtensions.Where<Concat<FromArray<IPlayer>, FromEnumerable<IPlayer>, IPlayer>, IPlayer>(ValueEnumerableExtensions.Concat<FromArray<IPlayer>, IPlayer>(ValueEnumerable.AsValueEnumerable<IPlayer>(context.OldEnemyKeys), (IEnumerable<IPlayer>)context.OldGroupEnemyKeys), (Func<IPlayer, bool>)((IPlayer enemy) => enemy != null))));
		foreach (IPlayer player2 in oldEnemyKeys)
		{
			context.NewGroup.AddEnemy(player2, EBotEnemyCause.initCauseEnemy);
			if (!context.NewGroup.Enemies.ContainsKey(player2))
			{
				Plugin.Log.LogDebug("[Migration] skipped rejected enemy key"); // zzap: expected for neutral/friendly groups (RUAF filter); Unity LogError stack capture spiked frames (10/28 errors.log x83)
			}
		}
		oldEnemyKeys = ValueEnumerableExtensions.ToArray<FromEnumerable<IPlayer>, IPlayer>(ValueEnumerableExtensions.Where<FromEnumerable<IPlayer>, IPlayer>(ValueEnumerable.AsValueEnumerable<IPlayer>((IEnumerable<IPlayer>)context.NewGroup.Enemies.Keys), (Func<IPlayer, bool>)((IPlayer enemy) => enemy != null)));
		foreach (IPlayer player3 in oldEnemyKeys)
		{
			context.NewGroup.AddEnemy(player3, EBotEnemyCause.initCauseEnemy);
			if (!context.NewGroup.Enemies.TryGetValue(player3, out var value) || value == null)
			{
				Plugin.Log.LogDebug("[Migration] skipped missing enemy info");
			}
			else if (!enemiesController.EnemyInfos.ContainsKey(player3))
			{
				EnemyInfo enemyInfo = enemiesController.AddNew(context.NewGroup, player3, value);
				if (enemyInfo == null)
				{
					Debug.LogError("Terminal bot migration skipped failed enemy info");
				}
				else
				{
					enemiesController.SetInfo(player3, enemyInfo);
				}
			}
		}
	}

	private static void Rollback(MigrationContext context)
	{
		RunRollbackStep("tilt", () =>
		{
			CancelPendingTiltTask(context);
		});
		if (context.NewGroup != null)
		{
			RunRollbackStep("destination-member", () =>
			{
				if (context.NewGroup.Contains(context.Bot))
				{
					context.NewGroup.RemoveAlly(context.Bot);
				}
			});
			RunRollbackStep("destination-group", () =>
			{
				if (context.NewGroup.MembersCount == 0)
				{
					context.NewGroup.Dispose();
					DetachRegisteredGroup(context);
				}
			});
		}
		RunRollbackStep("source-binding", () =>
		{
			context.Bot.BotsGroup = context.OldGroup;
			context.Bot.Memory._botsGroup = context.OldGroup;
			context.Bot.SpawnBotZone = context.OldZone;
			context.Bot.StartCorePoint = context.OldCore;
		});
		RunRollbackStep("source-position", () =>
		{
			context.Bot.Mover.Teleport(context.OldPosition);
		});
		RunRollbackStep("source-member", () =>
		{
			if (context.OldGroupAllyRemoved && !context.OldGroup.Contains(context.Bot))
			{
				context.OldGroup.AddMember(context.Bot, onActivation: true);
			}
		});
		RunRollbackStep("source-boss-group", () =>
		{
			if (context.OldBossGroupDetached)
			{
				context.OldGroup.BossGroup = new BossGroup(context.Bot);
			}
			else
			{
				context.OldGroup.BossGroup = context.OldBossGroup;
			}
		});
		RunRollbackStep("boss-state", () =>
		{
			RestoreBossState(context);
		});
		if (context.FollowerDisposeStarted && context.FollowerActivated)
		{
			RunRollbackStep("follower-dispose", () =>
			{
				context.Bot.BotFollower.Dispose();
			});
		}
		if (context.FollowerDisposeStarted)
		{
			RunRollbackStep("follower-activate", () =>
			{
				context.Bot.BotFollower.Activate();
			});
		}
		RunRollbackStep("follower-link", () =>
		{
			RestoreFollowerState(context);
		});
		RunRollbackStep("enemy-state", () =>
		{
			RestoreEnemyInfos(context);
		});
		RunRollbackStep("brain", () =>
		{
			RestoreBrainState(context);
		});
		if (context.SearchDisposeStarted)
		{
			RunRollbackStep("search", () =>
			{
				context.Bot.SearchData.Activate();
			});
		}
		if (context.PatrollingDisposeStarted)
		{
			RunRollbackStep("patrol-dispose", () =>
			{
				context.Bot.PatrollingData.Dispose();
			});
			RunRollbackStep("patrol-state", () =>
			{
				context.Bot.PatrollingData.IsActivated = false;
			});
			RunRollbackStep("patrol-ownership", () =>
			{
				ResetPatrollingOwnership(context.Bot);
			});
			RunRollbackStep("patrol-activate", () =>
			{
				context.Bot.PatrollingData.Activate();
			});
		}
		RunRollbackStep("look-sensor", () =>
		{
			context.Bot.LookSensor.UpdateZoneValue(context.OldZone);
		});
		RunRollbackStep("requests", () =>
		{
			UnbindRequests(context.Bot);
		});
		RunRollbackStep("mover", () =>
		{
			context.Bot.Mover.Stop();
		});
	}

	private static void RunRollbackStep(string name, Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			try
			{
				Debug.LogError("Terminal bot migration rollback step '" + name + "' failed: " + ex.GetType().Name + ": " + ex.Message);
			}
			catch
			{
			}
		}
	}

	private static void RestoreEnemyInfos(MigrationContext context)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		BotEnemiesController enemiesController = context.Bot.EnemiesController;
		IPlayer[] array = ValueEnumerableExtensions.ToArray<FromEnumerable<IPlayer>, IPlayer>(ValueEnumerableExtensions.Where<FromEnumerable<IPlayer>, IPlayer>(ValueEnumerable.AsValueEnumerable<IPlayer>((IEnumerable<IPlayer>)enemiesController.EnemyInfos.Keys), (Func<IPlayer, bool>)((IPlayer enemy) => enemy != null)));
		foreach (IPlayer player in array)
		{
			if (enemiesController.EnemyInfos.ContainsKey(player))
			{
				enemiesController.Remove(player);
			}
		}
		array = ValueEnumerableExtensions.ToArray<Distinct<Where<Concat<FromArray<IPlayer>, FromEnumerable<IPlayer>, IPlayer>, IPlayer>, IPlayer>, IPlayer>(ValueEnumerableExtensions.Distinct<Where<Concat<FromArray<IPlayer>, FromEnumerable<IPlayer>, IPlayer>, IPlayer>, IPlayer>(ValueEnumerableExtensions.Where<Concat<FromArray<IPlayer>, FromEnumerable<IPlayer>, IPlayer>, IPlayer>(ValueEnumerableExtensions.Concat<FromArray<IPlayer>, IPlayer>(ValueEnumerable.AsValueEnumerable<IPlayer>(context.OldEnemyKeys), (IEnumerable<IPlayer>)context.OldGroupEnemyKeys), (Func<IPlayer, bool>)((IPlayer enemy) => enemy != null))));
		foreach (IPlayer player2 in array)
		{
			context.OldGroup.AddEnemy(player2, EBotEnemyCause.initCauseEnemy);
			if (context.OldGroup.Enemies.TryGetValue(player2, out var value) && value != null && !enemiesController.EnemyInfos.ContainsKey(player2))
			{
				EnemyInfo enemyInfo = enemiesController.AddNew(context.OldGroup, player2, value);
				if (enemyInfo != null)
				{
					enemiesController.SetInfo(player2, enemyInfo);
				}
			}
		}
		if (context.OldGoalEnemyPerson != null && enemiesController.EnemyInfos.TryGetValue(context.OldGoalEnemyPerson, out var value2))
		{
			try
			{
				context.Bot.Memory.GoalEnemy = value2;
				return;
			}
			catch
			{
				return;
			}
		}
		try
		{
			context.Bot.Memory.GoalEnemy = null;
		}
		catch
		{
		}
	}

	private static void RestoreFollowerState(MigrationContext context)
	{
		BotFollower botFollower = context.Bot.BotFollower;
		IBossToFollow oldFollowerBoss = context.OldFollowerBoss;
		if (oldFollowerBoss != null)
		{
			List<BotOwner> followers = oldFollowerBoss.Followers;
			if (followers != null)
			{
				for (int num = followers.Count - 1; num >= 0; num--)
				{
					if ((object)followers[num] == context.Bot)
					{
						followers.RemoveAt(num);
					}
				}
				int num2 = context.OldFollowerIndex;
				if (num2 < 0 || num2 > followers.Count)
				{
					num2 = followers.Count;
				}
				followers.Insert(num2, context.Bot);
			}
		}
		SetFollowerBackingField(botFollower, "<BossToFollow>k__BackingField", oldFollowerBoss);
		SetFollowerBackingField(botFollower, "<Index>k__BackingField", context.OldFollowerIndex);
	}

	private static void SetFollowerBackingField(object follower, string fieldName, object value)
	{
		if (follower == null)
		{
			return;
		}
		FieldInfo fieldInfo = null;
		Type type = follower.GetType();
		while (type != null)
		{
			fieldInfo = type.GetField(fieldName, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (fieldInfo != null)
			{
				break;
			}
			type = type.BaseType;
		}
		if (fieldInfo == null)
		{
			throw new MissingFieldException(follower.GetType().FullName, fieldName);
		}
		fieldInfo.SetValue(follower, value);
	}

	private static void ClearPrivateMember(object target, string fieldName, string propertyName)
	{
		if (target == null)
		{
			return;
		}
		FieldInfo fieldInfo = null;
		Type type = target.GetType();
		while (type != null)
		{
			fieldInfo = type.GetField(fieldName, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (fieldInfo != null)
			{
				break;
			}
			type = type.BaseType;
		}
		if (fieldInfo != null)
		{
			fieldInfo.SetValue(target, null);
			return;
		}
		if (propertyName != null)
		{
			Type type2 = target.GetType();
			while (type2 != null)
			{
				PropertyInfo property = type2.GetProperty(propertyName, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (property?.SetMethod != null)
				{
					property.SetValue(target, null, null);
					return;
				}
				type2 = type2.BaseType;
			}
		}
		throw new MissingMemberException(target.GetType().FullName, fieldName);
	}

	private static void RestoreBrainState(MigrationContext context)
	{
		if (!context.BrainRestartStarted)
		{
			return;
		}
		try
		{
			try
			{
				TerminalMigrationBrainCompat.BeforeRestart(context.Bot);
			}
			catch
			{
			}
			try
			{
				context.Bot.Brain.Dispose();
			}
			catch
			{
			}
			try
			{
				TerminalMigrationBrainCompat.BeforeRestart(context.Bot);
			}
			catch
			{
			}
			try
			{
				context.Bot.Brain.Activate();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private static void RestoreBossState(MigrationContext context)
	{
		try
		{
			if (context.BossSetStarted)
			{
				ABossLogic bossLogic = context.Bot.Boss.BossLogic;
				if (bossLogic != null && bossLogic != context.OldBossLogic)
				{
					try
					{
						bossLogic.Dispose();
					}
					catch
					{
					}
				}
				context.Bot.Boss.BossLogic = context.OldBossLogic;
				context.Bot.Boss._followers.SetTargetFollowersCount(context.OldFollowersTargetCount);
			}
			context.Bot.Boss.IamBoss = context.OldIamBoss;
		}
		catch
		{
		}
	}

	private static void CancelPendingTiltTask(MigrationContext context)
	{
		if (context == null || !context.TiltTaskScheduled)
		{
			return;
		}
		try
		{
			context.TiltTask.Manager?.CancelDelayedTask(context.TiltTask.Id);
		}
		catch
		{
		}
		for (int num = PendingTiltTasks.Count - 1; num >= 0; num--)
		{
			PendingTiltTask pendingTiltTask = PendingTiltTasks[num];
			if (pendingTiltTask.Manager == context.TiltTask.Manager && pendingTiltTask.Id == context.TiltTask.Id)
			{
				PendingTiltTasks.RemoveAt(num);
			}
		}
		context.TiltTaskScheduled = false;
	}

	private static void UnbindRequests(BotOwner bot)
	{
		if (bot?.BotRequestController == null)
		{
			return;
		}
		try
		{
			bot.BotRequestController.Dispose();
		}
		catch
		{
		}
		try
		{
			bot.BotRequestController.CurRequest = null;
		}
		catch
		{
		}
		try
		{
			bot.BotRequestController._groupRequestController = null;
		}
		catch
		{
		}
	}

	private static void ResetPatrollingOwnership(BotOwner bot)
	{
		PatrollingData patrollingData = bot?.PatrollingData;
		if (patrollingData == null)
		{
			return;
		}
		try
		{
			patrollingData.PointChooser?.Disable();
		}
		catch
		{
		}
		BotPointControl pointControl = patrollingData.PointControl;
		if (pointControl != null)
		{
			try
			{
				pointControl.Way?.RemoveMe(bot);
			}
			catch
			{
			}
			pointControl.Way = null;
			pointControl._reservedPatrolPoint = null;
			ClearPrivateMember(pointControl, "_lastSetOwner", null);
			ClearPrivateMember(pointControl, "<PatrolPoint>k__BackingField", "PatrolPoint");
		}
		PatrolPathControl patrolPathControl = patrollingData.PatrolPathControl;
		if (patrolPathControl != null)
		{
			ClearPrivateMember(patrolPathControl, "FromPoint", "FromPoint");
			ClearPrivateMember(patrolPathControl, "ToPoint", "ToPoint");
		}
		ClearPrivateMember(patrollingData, "<PointChooser>k__BackingField", "PointChooser");
		patrollingData.CurTargetPoint = default;
	}

	private static void DetachRegisteredGroup(MigrationContext context)
	{
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (!context.Spawner.Groups.TryGetValue(context.Destination, out var value) || value == null)
			{
				return;
			}
			value._bossGroup.Remove(context.NewGroup);
			value._noConnectionGroups.Remove(context.NewGroup);
			WildSpawnType[] array = ValueEnumerableExtensions.ToArray<FromEnumerable<WildSpawnType>, WildSpawnType>(ValueEnumerable.AsValueEnumerable<WildSpawnType>((IEnumerable<WildSpawnType>)value.data.Keys));
			foreach (WildSpawnType key in array)
			{
				if (value.data.TryGetValue(key, out var value2) && value2 == context.NewGroup)
				{
					value.data[key] = null;
				}
			}
		}
		catch
		{
		}
	}
}
