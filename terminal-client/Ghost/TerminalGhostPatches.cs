using System;
using System.Linq.Expressions;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;

namespace Manimal.Terminal;

// zzap: optional integrations resolved once; no runtime member searches/Invoke arrays.
internal static class TerminalGhostPatches
{
	private static bool _fikaPresent;
	// Hot path (per bot per frame): read the cached flag only; registry rechecks
	// happen at install time and at each raid start.
	internal static bool FikaPresent => _fikaPresent;
	internal static bool CoreReady { get; private set; }
	private static Type _sainComponent;
	private static MethodInfo _bigBrain;
	private static MethodInfo _sainGoal;
	private static MethodInfo _sainActivation;
	private static Func<object, BotOwner> _sainOwner;
	private static Func<object, BotOwner> _activationOwner;
	private static Func<object, object> _controller;
	private static Func<object, EnemyInfo> _enemyInfo;
	private static Func<object, bool> _humanSight;
	private static Func<object, bool> _sainActive;
	private static Action<object> _clearEnemy;
	private static Action<object> _deactivateSain;
	private static bool _sainSafe;
	private static bool _initialized;
	private static bool _hasBigBrain;
	private static bool _hasSain;
	private static bool _hooksFrozen;
	[ThreadStatic] private static BotOwner _activationBot;

	internal static void Initialize()
	{
		if (_initialized) return;
		_initialized = true;
		AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		for (int i = 0; i < assemblies.Length; i++)
		{
			string name = assemblies[i].GetName().Name;
			if (name.IndexOf("BigBrain", StringComparison.OrdinalIgnoreCase) >= 0) _hasBigBrain = true;
			if (string.Equals(name, "SAIN", StringComparison.OrdinalIgnoreCase)) _hasSain = true;
			if (DetectFika(name)) return;
		}
		_bigBrain = Resolve("DrakiaXYZ.BigBrain.Patches.BotAgentUpdatePatch", "PatchPrefix");
		_sainGoal = Resolve("SAIN.SAINComponent.Classes.EnemyClasses.SAINEnemyController", "set_GoalEnemy");
		_sainActivation = Resolve("SAIN.SAINComponent.Classes.SAINActivationClass", "CheckBotActive");
		_sainComponent = TerminalTypes.ByName("SAIN.Components.BotComponent");
		if (_sainGoal == null || _sainActivation == null || _sainComponent == null) return;
		try
		{
			_sainOwner = Getter<BotOwner>(_sainGoal.DeclaringType, "BotOwner");
			_activationOwner = Getter<BotOwner>(_sainActivation.DeclaringType, "BotOwner");
			_controller = Getter<object>(_sainComponent, "EnemyController");
			_enemyInfo = Getter<EnemyInfo>(_sainGoal.GetParameters()[0].ParameterType, "EnemyInfo");
			_humanSight = Getter<bool>(_sainGoal.DeclaringType, "HumanEnemyInLineofSight");
			_sainActive = Getter<bool>(_sainActivation.DeclaringType, "BotActive");
			var instance = Expression.Parameter(typeof(object), "instance");
			_clearEnemy = Expression.Lambda<Action<object>>(Expression.Call(Expression.Convert(instance, _sainGoal.DeclaringType),
				AccessTools.Method(_sainGoal.DeclaringType, "ClearEnemy", Type.EmptyTypes)), instance).Compile();
			_deactivateSain = Expression.Lambda<Action<object>>(Expression.Call(Expression.Convert(instance, _sainActivation.DeclaringType),
				AccessTools.Method(_sainActivation.DeclaringType, "SetActive", new[] { typeof(bool) }), Expression.Constant(false)), instance).Compile();
			_sainSafe = true;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Ghost] SAIN accessors unavailable; SAIN bots will stay awake: " + ex.Message);
		}
	}

	// Integration DLLs can contain "Fika" without installing the Fika client.
	// Recheck the registry too: a plugin GUID can appear after its assembly event.
	private static bool DetectFika(string assemblyName)
	{
		if (!_fikaPresent && (string.Equals(assemblyName, "Fika.Core", StringComparison.OrdinalIgnoreCase)
			|| Chainloader.PluginInfos.ContainsKey("com.fika.core")))
		{
			_fikaPresent = true;
			Plugin.Log.LogWarning("[Ghost] Fika installed; Ghost disabled for the entire session");
		}
		return _fikaPresent;
	}

	private static void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
	{
		// BepInEx may load another plugin after our Awake. Do not touch Unity here;
		// the director restores on the next main-thread Update if Fika loads late.
		string name = args.LoadedAssembly.GetName().Name;
		DetectFika(name);
		if (_hooksFrozen && (string.Equals(name, "SAIN", StringComparison.OrdinalIgnoreCase) || name.IndexOf("BigBrain", StringComparison.OrdinalIgnoreCase) >= 0))
		{
			CoreReady = false;
			Plugin.Log.LogWarning("[Ghost] AI integration loaded after hook resolution; Ghost disabled until restart");
		}
	}

	private static MethodInfo Resolve(string typeName, string methodName)
	{
		try
		{
			Type type = TerminalTypes.ByName(typeName);
			MethodInfo method = type == null ? null : AccessTools.Method(type, methodName);
			if (method != null) return method;
		}
		catch (Exception) { }
		Plugin.Log.LogWarning("[Ghost] optional hook unavailable (skipped once): " + typeName + "." + methodName);
		return null;
	}

	private static Func<object, T> Getter<T>(Type type, string property)
	{
		var instance = Expression.Parameter(typeof(object), "instance");
		var member = AccessTools.Property(type, property);
		return Expression.Lambda<Func<object, T>>(Expression.Convert(Expression.Property(Expression.Convert(instance, type), member), typeof(T)), instance).Compile();
	}

	private static bool Installed(MethodBase target, Type patch)
	{
		if (target == null) return false;
		var info = Harmony.GetPatchInfo(target);
		if (info == null) return false;
		for (int i = 0; i < info.Prefixes.Count; i++)
			if (info.Prefixes[i].PatchMethod.DeclaringType == patch) return true;
		for (int i = 0; i < info.Postfixes.Count; i++)
			if (info.Postfixes[i].PatchMethod.DeclaringType == patch) return true;
		return false;
	}

	internal static void CheckInstalled()
	{
		_hooksFrozen = true;
		if (DetectFika(null)) return;
		CoreReady = Installed(AccessTools.Method(typeof(LocalGame), "Create"), typeof(Patch_NewRaid))
			&& Installed(AccessTools.Method(typeof(GameWorld), "Dispose"), typeof(Patch_EndRaid))
			&& Installed(AccessTools.Method(typeof(AICoreAgent<BotLogicDecision>), "Update"), typeof(Patch_Agent))
			&& Installed(AccessTools.Method(typeof(EnemyInfo), "CheckLookEnemy"), typeof(Patch_Look))
			&& Installed(AccessTools.Method(typeof(EnemyInfo), "SetVisible"), typeof(Patch_Visible))
			&& Installed(AccessTools.PropertySetter(typeof(BotMemory), "GoalEnemy"), typeof(Patch_Goal))
			&& Installed(AccessTools.Method(typeof(ActiveHealthController), "ApplyDamage"), typeof(Patch_Damage))
			&& Installed(AccessTools.Method(typeof(Player), "OnDead"), typeof(Patch_Death))
			&& Installed(AccessTools.Method(typeof(ShootData), "Shoot"), typeof(Patch_Shoot))
			&& Installed(AccessTools.Method(typeof(Player), "Move"), typeof(Patch_Move))
			&& Installed(AccessTools.Method(typeof(WeaponPrefab), "Parent"), typeof(Patch_WeaponParent))
			&& Installed(AccessTools.Method(typeof(WeaponPrefab), "ReturnToPool"), typeof(Patch_WeaponPool))
			&& (!_hasBigBrain || (_bigBrain != null && Installed(_bigBrain, typeof(Patch_BigBrain))));
		_sainSafe = _sainSafe && Installed(_sainGoal, typeof(Patch_SainGoal))
			&& Installed(_sainActivation, typeof(Patch_SainActivation))
			&& Installed(AccessTools.Method(typeof(BotMover), "Stop", Type.EmptyTypes), typeof(Patch_Stop));
		if (!CoreReady) Plugin.Log.LogWarning("[Ghost] required safety hook missing; Ghost stays disabled");
	}

	internal static bool HumanCombatSafe(TerminalGhostDirector.BotRecord record)
	{
		if (_sainComponent == null) return !_hasSain;
		if (!_sainSafe) return false;
		if (record.SainController == null)
		{
			var component = record.Bot.GetPlayer.GetComponent(_sainComponent);
			// Activation may still be running; after 30s SAIN is not managing this bot
			// (10/28 15시: the same 4 squads stayed "sain"-blocked all raid), so only
			// the native human-combat checks in SleepBlock apply.
			if (component == null) return UnityEngine.Time.time - record.CreatedAt >= 30f;
			record.SainController = _controller(component);
		}
		// SAIN ActiveHumanEnemy means KnownEnemies contains a human, including
		// sound-only knowledge. Native SleepBlock already guards recent visual
		// combat, and TrySleep independently checks every living human's distance.
		return record.SainController != null && !_humanSight(record.SainController);
	}

	internal static void ClearSain(TerminalGhostDirector.BotRecord record)
	{
		if (!_sainSafe || _sainComponent == null) return;
		if (record.SainController == null)
		{
			var component = record.Bot.GetPlayer.GetComponent(_sainComponent);
			if (component != null) record.SainController = _controller(component);
		}
		if (record.SainController != null) _clearEnemy(record.SainController);
	}

	internal static void SuspendSain(TerminalGhostDirector.BotRecord record, object activation)
	{
		if (!_sainSafe || _sainComponent == null) throw new InvalidOperationException("SAIN activation access unavailable");
		if (activation == null) throw new InvalidOperationException("SAIN activation disappeared while dormant");
		if (!_sainActive(activation)) return;
		BotOwner previous = _activationBot;
		try
		{
			_activationBot = record.Bot; // preserve stage-1 Stop protection during SetActive(false)
			_deactivateSain(activation);
		}
		finally { _activationBot = previous; }
	}

	private static bool Hidden(EnemyInfo enemy)
	{
		return TerminalGhostDirector.IsDormant(enemy.Owner) || TerminalGhostDirector.IsDormant(enemy.Person?.AIData?.BotOwner);
	}

	[HarmonyPatch(typeof(BotsController), "Init")]
	internal static class Patch_Attach
	{
		[HarmonyPostfix]
		private static void Postfix(BotsController __instance)
		{
			if (TerminalGate.On && !FikaPresent) TerminalGhostDirector.Attach(__instance?.BotSpawner);
		}
	}

	[HarmonyPatch(typeof(LocalGame), "Create")]
	internal static class Patch_NewRaid
	{
		[HarmonyPrefix, HarmonyPriority(Priority.First)]
		private static void Prefix()
		{
			DetectFika(null);
			TerminalGhostDirector.ResetForRaid();
			Manimal.Terminal.Civilian.CivilianTeleportProbe.ResetForRaid();
			Patch_Death.ResetDeathLog();
		}
	}

	[HarmonyPatch(typeof(GameWorld), "Dispose")]
	internal static class Patch_EndRaid
	{
		[HarmonyPrefix, HarmonyPriority(Priority.First)]
		private static void Prefix() { TerminalGhostDirector.ResetForRaid(); }
	}

	[HarmonyPatch(typeof(AICoreAgent<BotLogicDecision>), "Update")]
	internal static class Patch_Agent
	{
		[HarmonyPrefix, HarmonyPriority(Priority.First)]
		private static bool Prefix(AICoreAgent<BotLogicDecision> __instance)
		{
			return !TerminalGhostDirector.IsBrainPausedAgent(__instance);
		}
	}

	[HarmonyPatch]
	internal static class Patch_BigBrain
	{
		private static bool Prepare() { return !FikaPresent && _bigBrain != null; }
		private static MethodBase TargetMethod() { return _bigBrain; }
		[HarmonyPrefix, HarmonyPriority(Priority.First)]
		private static bool Prefix(AICoreAgent<BotLogicDecision> __0, ref bool __result)
		{
			if (!TerminalGhostDirector.IsBrainPausedAgent(__0)) return true;
			__result = false; // skip both BigBrain's body AND the native agent body
			return false;
		}
	}

	[HarmonyPatch(typeof(EnemyInfo), "CheckLookEnemy")]
	internal static class Patch_Look
	{
		[HarmonyPrefix]
		private static bool Prefix(EnemyInfo __instance)
		{
			if (!Hidden(__instance)) return true;
			__instance.IsVisible = false;
			__instance._canShoot = false;
			__instance._visibleType = EEnemyPartVisibleType.NotVisible;
			return false;
		}
	}

	[HarmonyPatch(typeof(EnemyInfo), "SetVisible")]
	internal static class Patch_Visible
	{
		[HarmonyPrefix]
		private static void Prefix(EnemyInfo __instance, ref bool __0)
		{
			if (!Hidden(__instance)) return;
			__0 = false;
			__instance._canShoot = false;
		}
	}

	[HarmonyPatch(typeof(BotMemory), "set_GoalEnemy")]
	internal static class Patch_Goal
	{
		[HarmonyPrefix]
		private static bool Prefix(BotMemory __instance, ref EnemyInfo __0)
		{
			if (__0 != null && !TerminalGhostDirector.AllowTarget(__instance._owner, __0.Person))
			{
				__0 = null;
				TerminalGhostDirector.ForgetTarget(__instance._owner);
			}
			return true; // run the setter's normal target-loss notifications
		}
	}

	[HarmonyPatch(typeof(Player), "Move")]
	internal static class Patch_Move
	{
		[HarmonyPrefix]
		private static void Prefix(Player __instance, ref UnityEngine.Vector2 direction)
		{
			if (TerminalGhostDirector.IsDormant(__instance.AIData?.BotOwner)) direction = UnityEngine.Vector2.zero;
		}
	}

	[HarmonyPatch(typeof(ActiveHealthController), "ApplyDamage")]
	internal static class Patch_Damage
	{
		[HarmonyPrefix, HarmonyPriority(Priority.First)]
		private static void Prefix(out TerminalTickProfiler.Scope __state)
		{
			__state = TerminalGate.On ? TerminalTickProfiler.Measure("Patch.ApplyDamage.total") : default;
		}
		[HarmonyFinalizer]
		private static void Finalizer(TerminalTickProfiler.Scope __state) { __state.Dispose(); }

		[HarmonyPostfix]
		private static void Postfix(ActiveHealthController __instance, float __result, EFT.Ballistics.DamageInfo damageInfo)
		{
			// Only this simulation's victim is exempt. Real hits (including nested
			// damage to another player) still cancel the fight and wake that squad.
			if (__result > 0f && !TerminalGhostCombatDamage.IsSimulated(__instance.Player))
			{
				IPlayer attacker = damageInfo.Player?.iPlayer;
				TerminalGhostDirector.Damage(__instance.Player, attacker != null && !attacker.IsAI);
			}
		}
	}

	[HarmonyPatch(typeof(Player), "OnDead")]
	internal static class Patch_Death
	{
		[HarmonyPrefix, HarmonyPriority(Priority.First)]
		private static void Prefix(Player __instance, out TerminalTickProfiler.Scope __state)
		{
			__state = TerminalGate.On ? TerminalTickProfiler.Measure("Patch.PlayerDeath.total") : default;
			// Combat restored its casualty BEFORE entering the normal Player damage
			// pipeline. Keep that body visible without waking its remaining squad.
			if (!TerminalGhostCombatDamage.IsSimulated(__instance))
				TerminalGhostDirector.WakeNow(__instance.AIData?.BotOwner, TerminalGhostDirector.WakeReason.Damage);
			LogDeath(__instance);
		}

		// zzap 10/28: user saw Black Division "fight each other and die" right after
		// ghost mode let go of a big wave; nothing in the log said who killed whom.
		private static readonly AccessTools.FieldRef<Player, IPlayer> LastAggressor =
			AccessTools.FieldRefAccess<Player, IPlayer>("LastAggressor");
		private static int _deathLines;

		private static void LogDeath(Player victim)
		{
			if (!TerminalGate.On || !Plugin.GhostDiagnostics.Value || !victim || !victim.IsAI || _deathLines >= 300) return;
			try
			{
				IPlayer killer = LastAggressor(victim);
				WildSpawnType? victimRole = victim.Profile?.Info?.Settings?.Role;
				string killerText = "unknown";
				if (killer != null)
				{
					WildSpawnType? killerRole = killer.Profile?.Info?.Settings?.Role;
					float distance = UnityEngine.Vector3.Distance(victim.Position, killer.Position);
					killerText = killer.IsAI
						? $"{(killerRole.HasValue ? TerminalRoles.Name(killerRole.Value) : "?")} group={killer.GroupId} {distance:F0}m"
						: $"human {distance:F0}m";
				}
				_deathLines++;
				Plugin.Log.LogInfo($"[Death] {(victimRole.HasValue ? TerminalRoles.Name(victimRole.Value) : "?")} group={victim.GroupId} killedBy {killerText}, simulated={TerminalGhostCombatDamage.IsSimulated(victim)}");
			}
			catch { }
		}

		internal static void ResetDeathLog() { _deathLines = 0; }
		[HarmonyFinalizer]
		private static void Finalizer(TerminalTickProfiler.Scope __state) { __state.Dispose(); }
	}

	// See TerminalGhostBody.Reapply: EFT's culler rewrites the body flags on every
	// distance-state change; dormant and stage-held bodies must stay hidden after it.
	[HarmonyPatch(typeof(OfflinePlayerCulling), "ApplyVisibleState")]
	internal static class Patch_Culling
	{
		[HarmonyPostfix]
		private static void Postfix(OfflinePlayerCulling __instance)
		{
			if (!TerminalGate.On) return;
			try
			{
				BotOwner bot = __instance._player?.AIData?.BotOwner;
				if (ReferenceEquals(bot, null)) return;
				TerminalGhostDirector.EnforceBody(bot);
				TerminalStageWait.EnforceHidden(bot);
			}
			catch { }
		}
	}

	[HarmonyPatch(typeof(ShootData), "Shoot")]
	internal static class Patch_Shoot
	{
		[HarmonyPrefix]
		private static bool Prefix(ShootData __instance, ref bool __result)
		{
			BotOwner bot = __instance._owner;
			// zzap 10/28: "bots face me but never fire" - count which Ghost gate said no.
			if (TerminalGhostDirector.IsDormant(bot)) TerminalGhostDirector.ShootBlockedDormant++;
			else if (TerminalGhostDirector.SainWakePending(bot)) TerminalGhostDirector.ShootBlockedSain++;
			else if (!TerminalGhostDirector.AllowTarget(bot, bot?.Memory?.GoalEnemy?.Person)) TerminalGhostDirector.ShootBlockedTarget++;
			else return true;
			__instance.EndShoot();
			__result = false;
			return false;
		}
	}

	[HarmonyPatch]
	internal static class Patch_SainGoal
	{
		private static bool Prepare() { return !FikaPresent && _sainSafe && _sainGoal != null; }
		private static MethodBase TargetMethod() { return _sainGoal; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, object __0)
		{
			if (__0 == null) return true;
			BotOwner bot = null;
			try
			{
				bot = _sainOwner(__instance);
				if (TerminalGhostDirector.AllowTarget(bot, _enemyInfo(__0)?.Person)) return true;
				_clearEnemy(__instance); // null setter is allowed; no stale SAIN goal remains
				return false;
			}
			catch (Exception ex) { TerminalGhostDirector.Fault(bot, ex); return true; }
		}
	}

	[HarmonyPatch]
	internal static class Patch_SainActivation
	{
		private static bool Prepare() { return !FikaPresent && _sainSafe && _sainActivation != null; }
		private static MethodBase TargetMethod() { return _sainActivation; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, out BotOwner __state)
		{
			__state = _activationBot;
			try
			{
				_activationBot = _activationOwner(__instance);
				if (!TerminalGhostDirector.IsDormant(_activationBot)) return true;
				// Renderer-only hiding leaves PlayerComponent active. Explicitly suspend
				// SAIN too, once; its normal CheckBotActive re-enables it after wake.
				if (_sainActive(__instance)) _deactivateSain(__instance);
				return false;
			}
			catch (Exception ex)
			{
				TerminalGhostDirector.Fault(_activationBot, ex);
				return true;
			}
		}
		[HarmonyFinalizer]
		private static Exception Finalizer(Exception __exception, BotOwner __state)
		{
			BotOwner bot = _activationBot;
			_activationBot = __state; // also restored on exceptions and nested calls
			if (__exception != null && TerminalGhostDirector.IsDormant(bot))
			{
				TerminalGhostDirector.Fault(bot, __exception);
				return null;
			}
			return __exception;
		}
	}

	[HarmonyPatch(typeof(BotMover), "Stop", new Type[0])]
	internal static class Patch_Stop
	{
		[HarmonyPrefix]
		private static bool Prefix(BotMover __instance)
		{
			return !(_activationBot && _activationBot.Mover == __instance && TerminalGhostDirector.IsDormant(_activationBot));
		}
	}

	[HarmonyPatch(typeof(WeaponPrefab), "Parent")]
	internal static class Patch_WeaponParent
	{
		[HarmonyPrefix, HarmonyPriority(Priority.First)]
		private static void Prefix(out TerminalTickProfiler.Scope __state)
		{
			__state = TerminalGate.On ? TerminalTickProfiler.Measure("Patch.WeaponParent.total") : default;
		}
		[HarmonyFinalizer]
		private static void Finalizer(TerminalTickProfiler.Scope __state) { __state.Dispose(); }
		[HarmonyPostfix]
		private static void Postfix(WeaponPrefab __instance, IPlayer player) { TerminalGhostDirector.WeaponParented(__instance, player); }
	}

	[HarmonyPatch(typeof(WeaponPrefab), "ReturnToPool")]
	internal static class Patch_WeaponPool
	{
		[HarmonyPrefix]
		private static void Prefix(WeaponPrefab __instance, out TerminalTickProfiler.Scope __state)
		{
			__state = TerminalGate.On ? TerminalTickProfiler.Measure("Patch.WeaponReturnToPool.total") : default;
			TerminalGhostDirector.WeaponReturning(__instance);
		}
		[HarmonyFinalizer]
		private static void Finalizer(TerminalTickProfiler.Scope __state) { __state.Dispose(); }
	}
}
