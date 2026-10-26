using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal;

// zzap: optional SAIN gates. All reflection/delegate compilation happens in Awake.
// No SAIN reference, object[] arguments, boxed SoundEvent/AISoundData or runtime searches.
internal static class TerminalGhostSain
{
	private const string EnemyType = "SAIN.SAINComponent.Classes.EnemyClasses.Enemy";
	private const string InputType = "SAIN.Classes.Bot.Sense.Hearing.HearingInput";
	private const string PlayerType = "SAIN.Components.PlayerComponentSpace.PlayerComponent";
	private const string WorldType = "SAIN.Components.GameWorldComponent";
	private const string HearingType = "SAIN.Components.BotControllerSpace.Classes.BotHearingClass";
	private static readonly string[] InputLists = { "SoundDataToReactTo", "AISoundCachedEvents", "AISoundCachedEvents_Conversations", "AISoundCachedEvents_Gunshots", "AISoundCachedEvents_Gunshots_Suppressed" };

	private sealed class Hook
	{
		internal MethodInfo Method;
		internal Func<object, BotOwner> Owner;
		internal Func<object, Player> Target;
		internal Action<object> Clear;
		internal Action<object> Prune;
		internal bool FaultReported;
	}

	private static Hook _bot, _valid, _add, _los, _look, _botLook;
	private static Hook _source, _dispatch, _ignore, _process, _flyby, _emit, _baseSound, _talk;
	private static Hook _world, _find;
	private static Hook _enemyTalk, _groupTalk, _shot, _watch;
	private static Action<object> _activationTick;
	private static Func<object, object> _getActivation;
	private static Func<object, object> _getController;
	private static Func<object, int> _enemyCount;
	private static Func<object, string> _enemyId;
	private static Action<object, object[]> _copyEnemies;
	private static Action<object, string> _removeEnemy;
	private static object[] _wakeEnemies;
	private static bool _resetBusy;
	private static bool _soundCacheDirty;
	private static bool _enabled, _diagnostics, _botReady;
	private static int _epoch, _reportFrame;
	private static long _ticks, _skipped;

	internal struct Sample
	{
		internal long Start;
		internal int Epoch;
	}

	internal static void Initialize()
	{
		Plugin.GhostMode.SettingChanged += SettingsChanged;
		Plugin.GhostSainThrottle.SettingChanged += SettingsChanged;
		Plugin.GhostDiagnostics.SettingChanged += SettingsChanged;
		RefreshSettings();
		_bot = Resolve("SAIN.Components.BotComponent", "ManualUpdate", typeof(void), new[] { typeof(float), typeof(float) }, "BotOwner", ConfigureBot);
		_valid = Resolve(EnemyType, "CheckValid", typeof(bool), Type.EmptyTypes, "BotOwner", EnemyTarget);
		_add = Resolve("SAIN.SAINComponent.Classes.EnemyClasses.EnemyListController", "tryAddEnemy", null, new[] { typeof(IPlayer) }, "BotOwner");
		_los = Resolve(EnemyType, "ShallCheckLoS", typeof(bool), new[] { typeof(float) }, "BotOwner", EnemyTarget);
		_look = Resolve(EnemyType, "ShallCheckLook", typeof(bool), new[] { typeof(float), typeof(float).MakeByRefType() }, "BotOwner", EnemyTarget);
		_botLook = Resolve("SAIN.SAINComponent.Classes.SAINBotLookClass", "UpdateLook", typeof(int), new[] { typeof(float) }, "BotOwner");
		_source = Resolve(WorldType, "UpdatePlayerSoundCache", typeof(void), new[] { TerminalTypes.ByName(PlayerType) }, null, ConfigureSource);
		_dispatch = Resolve(PlayerType, "PlayAISound", typeof(void), new[] { TerminalTypes.ByName("SAIN.Preset.Shared.Enums.SAINSoundType"), typeof(Vector3), typeof(float), typeof(float), typeof(EPhraseTrigger), typeof(ETagStatus) }, "BotOwner");
		_ignore = Resolve(InputType, "IsIgnoringSounds", typeof(bool), new[] { typeof(bool) }, "BotOwner");
		_process = Resolve(InputType, "ProcessAISoundCache", typeof(void), Type.EmptyTypes, "BotOwner", ConfigureInput);
		_flyby = Resolve(InputType, "OnBulletFlyBy", typeof(void), new[] { TerminalTypes.ByName(PlayerType), TerminalTypes.ByName("EFT.Ballistics.Shot") }, "BotOwner", ConfigureFlyby);
		_emit = Resolve(HearingType, "PlayAISound", typeof(void), new[] { TerminalTypes.ByName(PlayerType), TerminalTypes.ByName("SAIN.Preset.Shared.Enums.SAINSoundType"), typeof(Vector3), typeof(float), typeof(float), typeof(bool) }, null, ConfigureSourceOwner);
		_baseSound = Resolve(HearingType, "playBotEvent", typeof(void), new[] { typeof(Player), typeof(Vector3), typeof(float), TerminalTypes.ByName("SAIN.Preset.Shared.Enums.SAINSoundType") });
		_talk = Resolve(HearingType, "PlayerTalked", typeof(void), new[] { typeof(EPhraseTrigger), typeof(ETagStatus), typeof(Player) });
		_enemyTalk = Resolve("SAIN.SAINComponent.Classes.Talk.EnemyTalk", "playerTalked", typeof(void), new[] { typeof(EPhraseTrigger), typeof(ETagStatus), typeof(Player) }, "BotOwner");
		_groupTalk = Resolve("SAIN.SAINComponent.Classes.Talk.GroupTalk", "EnemyConversation", typeof(void), new[] { typeof(EPhraseTrigger), typeof(ETagStatus), typeof(Player) }, "BotOwner");
		_shot = Resolve("SAIN.SAINComponent.Classes.Tactics.SoundWatch", "OnShot", typeof(void), new[] { TerminalTypes.ByName(PlayerType), typeof(Vector3) }, null, ConfigureSourceOwner);
		_watch = Resolve("SAIN.SAINComponent.Classes.Tactics.SoundWatch", "Heard", typeof(bool), new[] { TerminalTypes.ByName("SAIN.Components.BotComponent"), typeof(Vector3).MakeByRefType(), typeof(bool).MakeByRefType(), typeof(string).MakeByRefType() }, null, ConfigureWatch);
		_world = Resolve(WorldType, "WorldTick", typeof(void), new[] { typeof(float) });
		Type bot = TerminalTypes.ByName("SAIN.Components.BotComponent");
		Type enemy = TerminalTypes.ByName(EnemyType);
		_find = Resolve("SAIN.Components.VisionRaycastJob", "FindEnemies", typeof(void), bot == null || enemy == null ? null : new[] { typeof(HashSet<>).MakeGenericType(bot), typeof(List<>).MakeGenericType(enemy) });
	}

	private static Hook Resolve(string typeName, string methodName, Type result, Type[] args, string owner = null, Action<Hook, Type> configure = null)
	{
		var hook = new Hook();
		try
		{
			Type type = TerminalTypes.ByName(typeName);
			if (type == null || args == null) throw new MissingMethodException();
			for (int i = 0; i < args.Length; i++) if (args[i] == null) throw new MissingMethodException();
			MethodInfo method = AccessTools.DeclaredMethod(type, methodName, args);
			if (method == null || (result != null ? method.ReturnType != result : method.ReturnType.IsValueType)) throw new MissingMethodException();
			if (owner != null) hook.Owner = Getter<BotOwner>(type, owner);
			configure?.Invoke(hook, type);
			hook.Method = method;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Ghost] optional SAIN hook unavailable (skipped once): " + typeName + "." + methodName + " (" + ex.Message + ")");
		}
		return hook;
	}

	private static Expression Member(Expression instance, string path)
	{
		string[] parts = path.Split('.');
		for (int i = 0; i < parts.Length; i++) instance = Expression.PropertyOrField(instance, parts[i]);
		return instance;
	}

	private static Func<object, T> Getter<T>(Type type, string path)
	{
		var value = Expression.Parameter(typeof(object), "value");
		return Expression.Lambda<Func<object, T>>(Expression.Convert(Member(Expression.Convert(value, type), path), typeof(T)), value).Compile();
	}

	private static void EnemyTarget(Hook hook, Type type) { hook.Target = Getter<Player>(type, "EnemyPlayer"); }
	// The emit hook receives PlayerComponent as argument 0, rather than its BotHearingClass instance.
	private static void ConfigureSourceOwner(Hook hook, Type type) { hook.Owner = Getter<BotOwner>(TerminalTypes.ByName(PlayerType), "BotOwner"); }
	private static void ConfigureFlyby(Hook hook, Type type) { hook.Target = Getter<Player>(TerminalTypes.ByName(PlayerType), "Player"); }
	private static void ConfigureSource(Hook hook, Type type)
	{
		Type player = TerminalTypes.ByName(PlayerType);
		hook.Owner = Getter<BotOwner>(player, "BotOwner");
		var value = Expression.Parameter(typeof(object), "value");
		hook.Clear = Expression.Lambda<Action<object>>(Expression.Call(Member(Expression.Convert(value, player), "AISoundCachedEvents"), "Clear", Type.EmptyTypes), value).Compile();
	}

	private static Expression ClearInput(Expression input)
	{
		var calls = new Expression[InputLists.Length];
		for (int i = 0; i < calls.Length; i++) calls[i] = Expression.Call(Member(input, InputLists[i]), "Clear", Type.EmptyTypes);
		return Expression.Block(calls);
	}

	private static void ConfigureBot(Hook hook, Type type)
	{
		var value = Expression.Parameter(typeof(object), "value");
		Expression bot = Expression.Convert(value, type);
		Expression controller = Member(bot, "EnemyController");
		Expression enemies = Member(controller, "EnemiesArray");
		Type enemy = enemies.Type.GetGenericArguments()[0];
		// One startup allocation, shared by sequential main-thread wake transitions.
		// HashSet.CopyTo writes into the correctly typed Enemy[] without boxing an enumerator.
		_wakeEnemies = (object[])Array.CreateInstance(enemy, 4096);
		var buffer = Expression.Parameter(typeof(object[]), "buffer");
		_copyEnemies = Expression.Lambda<Action<object, object[]>>(Expression.Call(enemies, "CopyTo", Type.EmptyTypes,
			Expression.Convert(buffer, enemy.MakeArrayType())), value, buffer).Compile();
		_getController = Getter<object>(type, "EnemyController");
		_enemyCount = Getter<int>(type, "EnemyController.EnemiesArray.Count");
		_enemyId = Getter<string>(enemy, "EnemyProfileId");
		var id = Expression.Parameter(typeof(string), "id");
		_removeEnemy = Expression.Lambda<Action<object, string>>(Expression.Call(Expression.Convert(value, controller.Type),
			"RemoveEnemy", Type.EmptyTypes, id), value, id).Compile();
		_getActivation = Getter<object>(type, "BotActivation");
		_activationTick = Expression.Lambda<Action<object>>(Expression.Call(Member(bot, "BotActivation"), "ManualUpdate", Type.EmptyTypes), value).Compile();
		hook.Clear = Expression.Lambda<Action<object>>(Expression.Block(ClearInput(Member(bot, "Hearing.HearingInput")),
			Expression.Call(Member(bot, "PlayerComponent.AISoundCachedEvents"), "Clear", Type.EmptyTypes)), value).Compile();
	}

	private static void ConfigureInput(Hook hook, Type type)
	{
		var value = Expression.Parameter(typeof(object), "value");
		Expression input = Expression.Convert(value, type);
		hook.Clear = Expression.Lambda<Action<object>>(ClearInput(input), value).Compile();
		var loops = new Expression[InputLists.Length];
		for (int i = 0; i < loops.Length; i++)
			loops[i] = PruneList(Member(input, InputLists[i]), "Sound.PlayerComponent", true);
		hook.Prune = Expression.Lambda<Action<object>>(Expression.Block(loops), value).Compile();
	}

	private static Expression PruneList(Expression list, string sourcePath, bool component)
	{
		var index = Expression.Variable(typeof(int), "index");
		Expression source = Member(Expression.Property(list, "Item", index), sourcePath);
		Expression player = component ? Expression.Condition(Expression.Equal(source, Expression.Constant(null, source.Type)),
			Expression.Constant(null, typeof(Player)), Member(source, "Player")) : source;
		var done = Expression.Label();
		return Expression.Block(new[] { index }, Expression.Assign(index, Expression.Subtract(Expression.Property(list, "Count"), Expression.Constant(1))),
			Expression.Loop(Expression.IfThenElse(Expression.LessThan(index, Expression.Constant(0)), Expression.Break(done),
				Expression.Block(Expression.IfThen(Expression.Call(typeof(TerminalGhostSain), nameof(HiddenPlayer), Type.EmptyTypes, player),
					Expression.Call(list, "RemoveAt", Type.EmptyTypes, index)), Expression.PostDecrementAssign(index), Expression.Empty())), done));
	}

	private static void ConfigureWatch(Hook hook, Type type)
	{
		hook.Owner = Getter<BotOwner>(TerminalTypes.ByName("SAIN.Components.BotComponent"), "BotOwner");
		FieldInfo sounds = AccessTools.Field(type, "_sounds");
		if (sounds == null || !sounds.IsStatic) throw new MissingFieldException("SoundWatch._sounds");
		var unused = Expression.Parameter(typeof(object), "unused");
		hook.Prune = Expression.Lambda<Action<object>>(PruneList(Expression.Field(null, sounds), "Player", false), unused).Compile();
	}

	private static void SettingsChanged(object sender, EventArgs args) { RefreshSettings(); }
	private static void RefreshSettings()
	{
		_enabled = Plugin.GhostMode.Value && Plugin.GhostSainThrottle.Value;
		_diagnostics = Plugin.GhostDiagnostics.Value;
		_epoch++;
		_ticks = _skipped = 0;
		_reportFrame = -1; // next main-thread frame starts a fresh comparison window
	}

	internal static void CheckInstalled()
	{
		_botReady = Installed(_bot, typeof(Patch_Bot));
		if (_bot.Method != null && !_botReady)
			Plugin.Log.LogWarning("[Ghost] optional SAIN bot update hook was not installed; stage-1 CoreReady unchanged");
	}

	private static bool Installed(Hook hook, Type patch)
	{
		if (hook.Method == null) return false;
		var info = Harmony.GetPatchInfo(hook.Method);
		if (info == null) return false;
		for (int i = 0; i < info.Prefixes.Count; i++) if (info.Prefixes[i].PatchMethod.DeclaringType == patch) return true;
		return false;
	}

	private static bool Ready(Hook hook) { return !TerminalGhostPatches.FikaPresent && hook.Method != null; }
	private static bool Enabled => _enabled && TerminalGhostDirector.Running;
	private static void Skipped() { if (_diagnostics) _skipped++; }
	private static bool HiddenPlayer(Player player) { return TerminalGhostDirector.IsDormant(player?.AIData?.BotOwner); }

	private static void Fault(Hook hook, BotOwner bot, Exception error)
	{
		if (!ReferenceEquals(bot, null)) TerminalGhostDirector.Fault(bot, error);
		else if (!hook.FaultReported)
		{
			hook.FaultReported = true;
			Plugin.Log.LogWarning("[Ghost] SAIN hook accessor failed once; original allowed: " + hook.Method.Name + ": " + error.Message);
		}
	}

	private static void FaultInstance(Hook hook, object instance, Exception error)
	{
		BotOwner bot = null;
		try { if (instance != null) bot = hook.Owner(instance); }
		catch (Exception) { }
		Fault(hook, bot, error);
	}

	private static bool SkipOwner(Hook hook, object instance, bool includeWake = true)
	{
		bool enabled = Enabled;
		if (instance == null || (!enabled && (!includeWake || !TerminalGhostDirector.HasSainWakePending))) return false;
		BotOwner bot = null;
		try
		{
			bot = hook.Owner(instance);
			if (!(enabled && TerminalGhostDirector.IsDormant(bot)) && (!includeWake || !TerminalGhostDirector.SainWakePending(bot))) return false;
			Skipped();
			return true;
		}
		catch (Exception ex) { Fault(hook, bot, ex); return false; }
	}

	private static bool HiddenEnemy(Hook hook, object instance, bool targetOnly = false)
	{
		if (!Enabled) return false;
		BotOwner bot = null, target = null;
		try
		{
			bot = hook.Owner(instance);
			target = hook.Target(instance)?.AIData?.BotOwner;
			if (!TerminalGhostDirector.IsDormant(target) && (targetOnly || !TerminalGhostDirector.IsDormant(bot))) return false;
			Skipped();
			return true;
		}
		catch (Exception ex)
		{
			Fault(hook, bot, ex);
			TerminalGhostDirector.WakeNow(target);
			return false;
		}
	}

	internal static bool NeedsWakeRefresh => _botReady;
	internal static void DormancyChanged() { _soundCacheDirty = true; }
	internal static void BeginFrame()
	{
		if (_reportFrame < 0) _reportFrame = Time.frameCount;
	}
	internal static void ReadWindow(out double ms, out long skipped)
	{
		int frames = _reportFrame < 0 ? 0 : Time.frameCount - _reportFrame;
		ms = frames <= 0 ? 0 : _ticks * 1000.0 / Stopwatch.Frequency / frames;
		skipped = _skipped;
		_ticks = _skipped = 0;
		_reportFrame = Time.frameCount;
	}
	internal static void ResetForRaid()
	{
		_epoch++;
		_ticks = _skipped = 0;
		_reportFrame = -1;
		_soundCacheDirty = false;
	}
	private static Sample StartSample()
	{
		if (!_diagnostics || !TerminalGate.On) return default;
		return new Sample { Start = Stopwatch.GetTimestamp(), Epoch = _epoch };
	}
	private static void EndSample(Sample sample)
	{
		if (sample.Start != 0 && sample.Epoch == _epoch && _diagnostics) _ticks += Stopwatch.GetTimestamp() - sample.Start;
	}

	[HarmonyPatch]
	internal static class Patch_Bot
	{
		private static bool Prepare() { return Ready(_bot); }
		private static MethodBase TargetMethod() { return _bot.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, out BotOwner __state)
		{
			__state = null;
			if (!_botReady || (!Enabled && !TerminalGhostDirector.HasSainWakePending)) return true;
			BotOwner bot = null;
			try
			{
				bot = _bot.Owner(__instance);
				if (Enabled && TerminalGhostDirector.IsDormant(bot))
				{
					// Stage 1 must deactivate synchronously BEFORE skipping its activation tick.
					TerminalGhostDirector.SuspendSain(bot, _getActivation(__instance));
					Skipped();
					return false;
				}
				if (!TerminalGhostDirector.SainWakePending(bot)) return true;
				// Another mod may re-enter a bot tick during removal notifications.
				// Defer that refresh instead of sharing the snapshot while it is in use.
				if (_resetBusy) return false;
				__state = bot;
				_bot.Clear(__instance);
				int count = _enemyCount(__instance);
				if (count > _wakeEnemies.Length) throw new InvalidOperationException("SAIN wake enemy snapshot capacity exceeded");
				try
				{
					_resetBusy = true;
					_copyEnemies(__instance, _wakeEnemies);
					object controller = _getController(__instance);
					// Use SAIN's normal removal notifications and EFT enemy cleanup.
					// Copy first: never modify an enemies collection during enumeration.
					for (int i = 0; i < count; i++) _removeEnemy(controller, _enemyId(_wakeEnemies[i]));
				}
				finally { Array.Clear(_wakeEnemies, 0, count); _resetBusy = false; }
				_activationTick(__instance);
			}
			catch (Exception ex) { Fault(_bot, bot, ex); }
			return true;
		}
		[HarmonyPostfix]
		private static void Postfix(BotOwner __state, bool __runOriginal)
		{
			if (__runOriginal && !ReferenceEquals(__state, null)) TerminalGhostDirector.FinishSainWake(__state);
		}
		[HarmonyFinalizer]
		private static Exception Finalizer(Exception __exception, BotOwner __state)
		{
			if (__exception != null && !ReferenceEquals(__state, null)) Fault(_bot, __state, __exception);
			return __exception;
		}
	}

	[HarmonyPatch]
	internal static class Patch_Valid
	{
		private static bool Prepare() { return Ready(_valid); }
		private static MethodBase TargetMethod() { return _valid.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, ref bool __result)
		{
			if (!HiddenEnemy(_valid, __instance, true)) return true;
			__result = false; // leave WasValid untouched; use SAIN's normal removal path
			return false;
		}
	}

	[HarmonyPatch]
	internal static class Patch_Add
	{
		private static bool Prepare() { return Ready(_add); }
		private static MethodBase TargetMethod() { return _add.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, IPlayer __0)
		{
			if (SkipOwner(_add, __instance, false)) return false; // allow fresh enemy registration during the wake tick
			if (!Enabled || !TerminalGhostDirector.IsDormant(__0?.AIData?.BotOwner)) return true;
			Skipped();
			return false; // Harmony initializes the reference result to null; no ref object boxing
		}
	}

	[HarmonyPatch]
	internal static class Patch_Los
	{
		private static bool Prepare() { return Ready(_los); }
		private static MethodBase TargetMethod() { return _los.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, ref bool __result)
		{
			if (!HiddenEnemy(_los, __instance)) return true;
			__result = false;
			return false;
		}
	}

	[HarmonyPatch]
	internal static class Patch_Look
	{
		private static bool Prepare() { return Ready(_look); }
		private static MethodBase TargetMethod() { return _look.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, ref float __1, ref bool __result)
		{
			if (!HiddenEnemy(_look, __instance)) return true;
			__1 = 0f;
			__result = false;
			return false;
		}
	}

	[HarmonyPatch]
	internal static class Patch_BotLook
	{
		private static bool Prepare() { return Ready(_botLook); }
		private static MethodBase TargetMethod() { return _botLook.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, ref int __result, out Sample __state)
		{
			__state = StartSample();
			if (!SkipOwner(_botLook, __instance)) return true;
			__result = 0;
			return false;
		}
		[HarmonyFinalizer]
		private static void Finalizer(Sample __state) { EndSample(__state); }
	}

	[HarmonyPatch]
	internal static class Patch_Source
	{
		private static bool Prepare() { return Ready(_source); }
		private static MethodBase TargetMethod() { return _source.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __0)
		{
			if (!SkipOwner(_source, __0)) return true;
			try { _source.Clear(__0); }
			catch (Exception ex) { FaultInstance(_source, __0, ex); return true; }
			return false;
		}
	}

	[HarmonyPatch]
	internal static class Patch_Dispatch
	{
		private static bool Prepare() { return Ready(_dispatch); }
		private static MethodBase TargetMethod() { return _dispatch.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance) { return !SkipOwner(_dispatch, __instance); }
	}

	[HarmonyPatch]
	internal static class Patch_Ignore
	{
		private static bool Prepare() { return Ready(_ignore); }
		private static MethodBase TargetMethod() { return _ignore.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, ref bool __result)
		{
			if (!SkipOwner(_ignore, __instance)) return true;
			__result = true;
			return false;
		}
	}

	[HarmonyPatch]
	internal static class Patch_Process
	{
		private static bool Prepare() { return Ready(_process); }
		private static MethodBase TargetMethod() { return _process.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance)
		{
			bool enabled = Enabled;
			if (!enabled && !TerminalGhostDirector.HasSainWakePending) return true;
			BotOwner bot = null;
			try
			{
				bot = _process.Owner(__instance);
				if ((enabled && TerminalGhostDirector.IsDormant(bot)) || TerminalGhostDirector.SainWakePending(bot))
				{
					_process.Clear(__instance);
					Skipped();
					return false;
				}
				if (enabled) _process.Prune(__instance); // typed list indexing: no boxed sound structs
			}
			catch (Exception ex) { Fault(_process, bot, ex); }
			return true;
		}
	}

	[HarmonyPatch]
	internal static class Patch_Flyby
	{
		private static bool Prepare() { return Ready(_flyby); }
		private static MethodBase TargetMethod() { return _flyby.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, object __0)
		{
			if (SkipOwner(_flyby, __instance)) return false;
			if (!Enabled || __0 == null) return true;
			try
			{
				if (!HiddenPlayer(_flyby.Target(__0))) return true;
				Skipped();
				return false;
			}
			catch (Exception ex) { FaultInstance(_flyby, __instance, ex); return true; }
		}
	}

	[HarmonyPatch]
	internal static class Patch_Emit
	{
		private static bool Prepare() { return Ready(_emit); }
		private static MethodBase TargetMethod() { return _emit.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __0) { return !SkipOwner(_emit, __0); }
	}

	[HarmonyPatch]
	internal static class Patch_BaseSound
	{
		private static bool Prepare() { return Ready(_baseSound); }
		private static MethodBase TargetMethod() { return _baseSound.Method; }
		[HarmonyPrefix]
		private static bool Prefix(Player __0)
		{
			if (!Enabled || !HiddenPlayer(__0)) return true;
			Skipped();
			return false;
		}
	}

	[HarmonyPatch]
	internal static class Patch_Talk
	{
		private static bool Prepare() { return Ready(_talk); }
		private static MethodBase TargetMethod() { return _talk.Method; }
		[HarmonyPrefix]
		private static bool Prefix(Player __2)
		{
			if (!Enabled || !HiddenPlayer(__2)) return true;
			Skipped();
			return false;
		}
	}

	private static bool AllowTalk(Hook hook, object instance, Player source)
	{
		if (SkipOwner(hook, instance)) return false;
		if (!Enabled || !HiddenPlayer(source)) return true;
		Skipped();
		return false;
	}

	[HarmonyPatch]
	internal static class Patch_EnemyTalk
	{
		private static bool Prepare() { return Ready(_enemyTalk); }
		private static MethodBase TargetMethod() { return _enemyTalk.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, Player __2) { return AllowTalk(_enemyTalk, __instance, __2); }
	}

	[HarmonyPatch]
	internal static class Patch_GroupTalk
	{
		private static bool Prepare() { return Ready(_groupTalk); }
		private static MethodBase TargetMethod() { return _groupTalk.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __instance, Player __2) { return AllowTalk(_groupTalk, __instance, __2); }
	}

	[HarmonyPatch]
	internal static class Patch_Shot
	{
		private static bool Prepare() { return Ready(_shot); }
		private static MethodBase TargetMethod() { return _shot.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __0) { return !SkipOwner(_shot, __0); }
	}

	[HarmonyPatch]
	internal static class Patch_Watch
	{
		private static bool Prepare() { return Ready(_watch); }
		private static MethodBase TargetMethod() { return _watch.Method; }
		[HarmonyPrefix]
		private static bool Prefix(object __0, ref Vector3 __1, ref bool __2, ref string __3, ref bool __result)
		{
			if (SkipOwner(_watch, __0))
			{
				__1 = default;
				__2 = false;
				__3 = null;
				__result = false;
				return false;
			}
			if (!Enabled || !_soundCacheDirty) return true;
			try { _watch.Prune(null); }
			catch (Exception ex) { FaultInstance(_watch, __0, ex); }
			_soundCacheDirty = false; // prune shared records once per sleep change, not per listener
			return true;
		}
	}

	// Three disjoint main-thread entries. WorldTick contains bot/player/sound updates;
	// BotLook and FindEnemies run from separate vision coroutines, outside WorldTick.
	// Worker-job execution/waits and other coroutine bodies are NOT a total SAIN profiler.
	[HarmonyPatch]
	internal static class Patch_WorldTiming
	{
		private static bool Prepare() { return Ready(_world); }
		private static MethodBase TargetMethod() { return _world.Method; }
		[HarmonyPrefix]
		private static void Prefix(out Sample __state) { __state = StartSample(); }
		[HarmonyFinalizer]
		private static void Finalizer(Sample __state) { EndSample(__state); }
	}

	[HarmonyPatch]
	internal static class Patch_FindTiming
	{
		private static bool Prepare() { return Ready(_find); }
		private static MethodBase TargetMethod() { return _find.Method; }
		[HarmonyPrefix]
		private static void Prefix(out Sample __state) { __state = StartSample(); }
		[HarmonyFinalizer]
		private static void Finalizer(Sample __state) { EndSample(__state); }
	}
}
