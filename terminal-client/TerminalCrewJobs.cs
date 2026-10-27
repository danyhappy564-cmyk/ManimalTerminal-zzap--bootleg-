using System;
using System.Collections.Generic;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace Manimal.Terminal;

internal static class TerminalCrewJobs
{
	internal enum Job
	{
		None,
		Guard,
		Hunt,
		Siege
	}

	internal sealed class Rec
	{
		public Job Job;

		public Vector3? RushTo;

		public Bounds Zone;

		public float RushUntil;
	}

	private sealed class PendingForceAttack
	{
		internal BotOwner Bot;

		internal string Role;

		internal float QueuedAt;

		internal float NextTry;

		internal int Attempts;

		internal string LastError;
	}

	private sealed class ForceAttackHost : MonoBehaviour
	{
		private void Update()
		{
			if (!TerminalGate.On)
			{
				UnityEngine.Object.Destroy(gameObject);
			}
			else
			{
				PumpForceAttacks();
			}
		}

		private void OnDestroy()
		{
			if (_forceAttackHost == gameObject)
			{
				_forceAttackHost = null;
			}
		}
	}

	internal static readonly Dictionary<string, Rec> ByProfile = new Dictionary<string, Rec>();

	private static float _eventAt = -1f;

	private const float EventWindow = 180f;

	private static readonly Dictionary<string, PendingForceAttack> PendingForceAttacks = new Dictionary<string, PendingForceAttack>();

	// zzap: Ghost reads pending native orders without advancing the job allocator.
	internal static bool GhostHasPendingOrder(string profileId) => profileId != null && PendingForceAttacks.ContainsKey(profileId);

	private static GameObject _forceAttackHost;

	private const float ForceAttackTimeout = 20f;

	private static bool _registered;

	private static bool _sainMode;

	private static Bounds? _bdHold;

	private static bool _bdHoldLooked;

	internal static WildSpawnType? LastBlackDivRole;

	private static readonly Dictionary<string, float> FirstSeen = new Dictionary<string, float>();

	private static Bounds? _armoryBounds;

	private static bool _armoryLooked;

	private static readonly Vector3 Tb4Pos = new Vector3(131.1f, -49f, -277.1f);

	private static Vector3? _armoryNavAnchor;

	internal static void OnMigrated(BotOwner bot)
	{
		if ((bool)bot && !string.IsNullOrEmpty(bot.ProfileId))
		{
			ByProfile.Remove(bot.ProfileId);
			FirstSeen.Remove(bot.ProfileId);
			PendingForceAttacks.Remove(bot.ProfileId);
		}
	}

	internal static void Register()
	{
		if (!_registered)
		{
			_registered = true;
			_sainMode = TerminalSainCompat.Detected;
			List<string> brainNames = new List<string> { "Assault", "CursedAssault", "PMC", "PmcBear", "PmcUsec", "ExUsec" };
			BrainManager.AddCustomLayer(typeof(TerminalCrewLayer), brainNames, 68);
			int num = TerminalStagePolicy.RushPriority(_sainMode);
			BrainManager.AddCustomLayer(typeof(TerminalRushLayer), brainNames, num);
			BrainManager.AddCustomLayer(typeof(TerminalRuafDefenseLayer), brainNames, 72);
			Plugin.Log.LogWarning($"[CrewLayer] bigbrain layers registered (Assault/PMC-family, crew 68 / ruaf-defense 72 / rush {num}; " + (_sainMode ? "SAIN handoff mode" : "native force-attack mode") + ")");
		}
	}

	internal static void NoteEvent(string name)
	{
		using var scope = TerminalTickProfiler.Measure("Crew.NoteEvent");
		TerminalPopulationDirector.NoteProgressEvent(name);
		_eventAt = Time.time;
		Plugin.Log.LogDebug($"[CrewLayer] tier event '{name}' noted — bots spawning in the next {180f:0}s push the players");
		if (string.Equals(name, "TB8", StringComparison.OrdinalIgnoreCase))
		{
			TerminalHangarCrew.Arm();
		}
	}

	private static Bounds? BdHoldBounds()
	{
		if (_bdHoldLooked)
		{
			return _bdHold;
		}
		_bdHoldLooked = true;
		try
		{
			GameObject gameObject = GameObject.Find("BD_hold");
			BoxCollider boxCollider = ((gameObject != null) ? gameObject.GetComponent<BoxCollider>() : null);
			if (boxCollider != null)
			{
				_bdHold = boxCollider.bounds;
			}
		}
		catch
		{
		}
		if (!_bdHold.HasValue)
		{
			Plugin.Log.LogWarning("[CrewLayer] BD_hold volume not found — hangar guard duty off");
		}
		return _bdHold;
	}

	internal static void OnBotCreated(BotOwner bot)
	{
		using var scope = TerminalTickProfiler.Measure("Crew.OnBotCreated");
		try
		{
			if (TerminalGate.On && Plugin.EventWavesPush.Value && (object)bot != null && bot.ProfileId != null && (!(bot.GetPlayer != null) || !bot.GetPlayer.IsYourPlayer) && (bot.Profile?.Info?.Settings?.Role.ToString() ?? "").IndexOf("blackdiv", StringComparison.OrdinalIgnoreCase) < 0 && !(_eventAt < 0f) && !(Time.time - _eventAt > 180f))
			{
				string text = bot.Profile?.Info?.Settings?.Role.ToString() ?? "";
				if (text.IndexOf("assault", StringComparison.OrdinalIgnoreCase) < 0 || text.IndexOf("ruaf", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("vsrf", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					Plugin.Log.LogDebug("[CrewLayer] " + bot.name + ": role '" + text + "' not a push role — left to its own brain");
				}
				else if (_sainMode)
				{
					ByProfile[bot.ProfileId] = new Rec
					{
						Job = Job.Hunt,
						Zone = new Bounds(bot.Position, new Vector3(28f, 8f, 28f)),
						RushUntil = Time.time + 180f
					};
					Plugin.Log.LogDebug("[CrewLayer] " + bot.name + ": SAIN DEPLOYMENT order role='" + text + "' (Terminal travel -> SAIN combat)");
				}
				else
				{
					QueueNativeForceAttack(bot, text);
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[CrewLayer] assign failed: " + ex.Message);
		}
	}

	private static void QueueNativeForceAttack(BotOwner bot, string role)
	{
		string text = bot?.ProfileId;
		if (!string.IsNullOrEmpty(text) && !PendingForceAttacks.ContainsKey(text))
		{
			PendingForceAttacks[text] = new PendingForceAttack
			{
				Bot = bot,
				Role = role,
				QueuedAt = Time.realtimeSinceStartup,
				NextTry = Time.realtimeSinceStartup
			};
			if (!_forceAttackHost)
			{
				_forceAttackHost = new GameObject("Terminal_ForceAttackDispatcher");
				_forceAttackHost.AddComponent<ForceAttackHost>();
			}
			Plugin.Log.LogDebug("[CrewLayer] " + bot.name + ": native force-attack queued until brain initialization completes");
		}
	}

	private static string ForceAttackReadiness(BotOwner bot)
	{
		try
		{
			if (!bot)
			{
				return "bot destroyed";
			}
			if (!bot.GetPlayer)
			{
				return "player missing";
			}
			if (bot.GetPlayer.HealthController == null || !bot.GetPlayer.HealthController.IsAlive)
			{
				return "bot dead";
			}
			if (bot.Settings?.FileSettings?.Mind == null)
			{
				return "settings missing";
			}
			if (bot.Brain == null)
			{
				return "brain missing";
			}
			if (bot.Brain.BaseBrain == null)
			{
				return "base brain missing";
			}
			if (bot.PriorityAxeTarget == null)
			{
				return "pursuit controller missing";
			}
			if (bot.LeaveData == null)
			{
				return "leave controller missing";
			}
			if (bot.BotState != EBotState.Active)
			{
				return $"state={bot.BotState}";
			}
			return null;
		}
		catch (Exception ex)
		{
			return "readiness threw " + ex.GetType().Name;
		}
	}

	private static void PumpForceAttacks()
	{
		if (_sainMode || !TerminalGate.On || !Plugin.EventWavesPush.Value)
		{
			PendingForceAttacks.Clear();
		}
		else
		{
			if (PendingForceAttacks.Count == 0)
			{
				return;
			}
			float realtimeSinceStartup = Time.realtimeSinceStartup;
			List<string> list = new List<string>();
			foreach (KeyValuePair<string, PendingForceAttack> pendingForceAttack in PendingForceAttacks)
			{
				PendingForceAttack value = pendingForceAttack.Value;
				if (realtimeSinceStartup < value.NextTry)
				{
					continue;
				}
				value.NextTry = realtimeSinceStartup + 0.25f;
				string text = ForceAttackReadiness(value.Bot);
				switch (text)
				{
				case "bot destroyed":
				case "player missing":
				case "bot dead":
					list.Add(pendingForceAttack.Key);
					continue;
				}
				if (text == null)
				{
					try
					{
						BotsForceAttackEvent botsForceAttackEvent = Singleton<IBotGame>.Instance?.BotsController?.EventsController?.ForceAttackEvent;
						if (botsForceAttackEvent == null)
						{
							text = "event controller missing";
							goto IL_01ea;
						}
						if (!botsForceAttackEvent.CanActivate(value.Bot))
						{
							Plugin.Log.LogDebug("[CrewLayer] " + value.Bot.name + ": not force-attack eligible by BSG settings — left to its own brain");
							list.Add(pendingForceAttack.Key);
							continue;
						}
						botsForceAttackEvent._botZonesLeaveController.NoZoneBlocks = true;
						botsForceAttackEvent.BotEventActive(value.Bot);
						Plugin.Log.LogDebug($"[CrewLayer] {value.Bot.name}: NATIVE FORCE ATTACK ready after {realtimeSinceStartup - value.QueuedAt:F1}s role='{value.Role}'");
						list.Add(pendingForceAttack.Key);
					}
					catch (Exception ex)
					{
						value.Attempts++;
						value.LastError = ex.ToString();
						text = "activation threw " + ex.GetType().Name;
						goto IL_01ea;
					}
					continue;
				}
				goto IL_01ea;
				IL_01ea:
				if (realtimeSinceStartup - value.QueuedAt >= 20f)
				{
					string text2 = (string.IsNullOrEmpty(value.LastError) ? text : value.LastError);
					Plugin.Log.LogWarning("[CrewLayer] gave up deferred force attack for " + (value.Bot?.name ?? pendingForceAttack.Key) + " " + $"after {20f:F0}s ({text}, attempts={value.Attempts}): {text2}");
					list.Add(pendingForceAttack.Key);
				}
			}
			foreach (string item in list)
			{
				PendingForceAttacks.Remove(item);
			}
		}
	}

	internal static Rec For(BotOwner bot)
	{
		string text = bot?.ProfileId;
		if (text == null)
		{
			return null;
		}
		if (ByProfile.TryGetValue(text, out var value))
		{
			return value;
		}
		string text2 = bot.Profile?.Info?.Settings?.Role.ToString() ?? "";
		if (text2.IndexOf("blackdiv", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			if (!LastBlackDivRole.HasValue && bot.Profile?.Info?.Settings != null)
			{
				LastBlackDivRole = bot.Profile.Info.Settings.Role;
			}
			if (!FirstSeen.TryGetValue(text, out var value2))
			{
				FirstSeen[text] = Time.time;
				return null;
			}
			if (Time.time - value2 < 2f)
			{
				return null;
			}
			string text3 = bot.BotsGroup?.BotZone?.name ?? "";
			if (text3.IndexOf("HangarBD", StringComparison.OrdinalIgnoreCase) >= 0 && TerminalBlackDivisionPlaces.TryAcquire(bot, out var assignment))
			{
				Bounds? bounds = BdHoldBounds();
				if (!bounds.HasValue && assignment.TryGetAuthoredBounds(out var bounds2))
				{
					bounds = bounds2;
				}
				if (bounds.HasValue)
				{
					ByProfile[text] = new Rec
					{
						Job = Job.Guard,
						Zone = bounds.Value
					};
					Plugin.Log.LogInfo("[CrewLayer] " + bot.name + ": retail BD place claimed " + $"AreaId={assignment.AreaId} connectionGroup={assignment.ConnectionGroupId}; " + "reusing existing hangar guard job");
					TerminalHangarCrew.Arm();
					return ByProfile[text];
				}
				Plugin.Log.LogWarning("[CrewLayer] " + bot.name + ": BD place claimed but no authored hold bounds; preserving zone job");
			}
			if (text3.IndexOf("HangarAmbush", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				Bounds zone = ArmoryBounds();
				ByProfile[text] = new Rec
				{
					Job = Job.Siege,
					Zone = zone,
					RushTo = ArmoryNavAnchor(),
					RushUntil = Time.time + 300f
				};
				Plugin.Log.LogDebug($"[CrewLayer] {bot.name}: SIEGE armory (rush TB4 then patrol ring) role='{text2}' zone='{text3}' pos={bot.Position}");
				return ByProfile[text];
			}
			Bounds? bounds3;
			if (text3.IndexOf("HangarBD", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				bounds3 = BdHoldBounds();
				if (bounds3.HasValue)
				{
					Bounds valueOrDefault = bounds3.GetValueOrDefault();
					ByProfile[text] = new Rec
					{
						Job = Job.Guard,
						Zone = valueOrDefault
					};
					Plugin.Log.LogDebug($"[CrewLayer] {bot.name}: GUARD BD_hold (hangar camp) role='{text2}' zone='{text3}' pos={bot.Position}");
					TerminalHangarCrew.Arm();
					return ByProfile[text];
				}
			}
			if (text3.IndexOf("Ambush", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				ByProfile[text] = new Rec
				{
					Job = Job.Guard,
					Zone = new Bounds(bot.Position, new Vector3(24f, 8f, 24f))
				};
				Plugin.Log.LogDebug($"[CrewLayer] {bot.name}: GUARD ambush post role='{text2}' zone='{text3}' pos={bot.Position}");
				return ByProfile[text];
			}
			bounds3 = BdHoldBounds();
			if (bounds3.HasValue)
			{
				Bounds valueOrDefault2 = bounds3.GetValueOrDefault();
				Bounds bounds4 = valueOrDefault2;
				bounds4.Expand(new Vector3(30f, 12f, 30f));
				if (bounds4.Contains(bot.Position))
				{
					ByProfile[text] = new Rec
					{
						Job = Job.Guard,
						Zone = valueOrDefault2
					};
					Plugin.Log.LogDebug($"[CrewLayer] {bot.name}: GUARD BD_hold (position fallback) role='{text2}' pos={bot.Position}");
					TerminalHangarCrew.Arm();
					return ByProfile[text];
				}
			}
			ByProfile[text] = new Rec
			{
				Job = Job.Guard,
				Zone = new Bounds(bot.Position, new Vector3(28f, 8f, 28f))
			};
			Plugin.Log.LogDebug($"[CrewLayer] {bot.name}: GUARD patrol post (BD default) role='{text2}' pos={bot.Position}");
			return ByProfile[text];
		}
		ByProfile[text] = null;
		return null;
	}

	internal static Bounds? BdHoldBoundsPublic()
	{
		return BdHoldBounds();
	}

	internal static Vector3 ArmoryNavAnchor()
	{
		if (_armoryNavAnchor.HasValue)
		{
			return _armoryNavAnchor.Value;
		}
		Bounds bounds = ArmoryBounds();
		NavMeshHit hit2;
		if (NavMesh.SamplePosition(new Vector3(bounds.center.x, bounds.min.y + 1f, bounds.center.z), out var hit, 15f, -1))
		{
			_armoryNavAnchor = hit.position;
		}
		else if (NavMesh.SamplePosition(Tb4Pos, out hit2, 15f, -1))
		{
			_armoryNavAnchor = hit2.position;
		}
		else
		{
			_armoryNavAnchor = Tb4Pos;
		}
		Plugin.Log.LogDebug($"[CrewLayer] armory nav anchor: {_armoryNavAnchor} (bounds center was {bounds.center})");
		return _armoryNavAnchor.Value;
	}

	internal static Bounds ArmoryBounds()
	{
		if (_armoryLooked && _armoryBounds.HasValue)
		{
			return _armoryBounds.Value;
		}
		if (!_armoryLooked)
		{
			using var scope = TerminalTickProfiler.Measure("Crew.ArmoryLookup");
			_armoryLooked = true;
			try
			{
				Transform transform = null;
				float num = float.MaxValue;
				Transform[] array = UnityEngine.Object.FindObjectsOfType<Transform>();
				foreach (Transform transform2 in array)
				{
					ConsiderArmory(transform2, ref transform, ref num);
				}
				ApplyArmory(transform, num);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[CrewLayer] armory lookup failed: " + ex.Message);
			}
		}
		return _armoryBounds ?? new Bounds(Tb4Pos, new Vector3(38f, 10f, 38f));
	}

	private static void ConsiderArmory(Transform transform2, ref Transform best, ref float bestSqr)
	{
		if ((bool)transform2 && !(transform2.name != "Administration_03_small"))
		{
			float sqrMagnitude = (transform2.position - Tb4Pos).sqrMagnitude;
			if (sqrMagnitude < bestSqr)
			{
				bestSqr = sqrMagnitude;
				best = transform2;
			}
		}
	}

	private static void ApplyArmory(Transform transform, float num)
	{
		if ((bool)transform)
		{
			Renderer[] componentsInChildren = transform.GetComponentsInChildren<Renderer>();
			if (componentsInChildren.Length != 0)
			{
				Bounds bounds = componentsInChildren[0].bounds;
				Renderer[] array2 = componentsInChildren;
				foreach (Renderer renderer in array2)
				{
					bounds.Encapsulate(renderer.bounds);
				}
				bounds.Expand(new Vector3(24f, 6f, 24f));
				_armoryBounds = bounds;
				Plugin.Log.LogDebug($"[CrewLayer] armory = Administration_03_small at {transform.position} " + $"(dist to TB4 {Mathf.Sqrt(num):0}m), patrol box {bounds.size}");
			}
		}
		if (!_armoryBounds.HasValue)
		{
			Plugin.Log.LogWarning("[CrewLayer] Administration_03_small not found — armory siege falls back to the TB4 area");
		}
	}

	// zzap perf: ArmoryBounds() ran FindObjectsOfType<Transform>() (every active
	// transform, a name string each) inside the first hangar bot's brain tick — a
	// mid-raid hitch. Do the same search ahead of time, 3ms per frame; ArmoryBounds()
	// keeps its synchronous path in case a bot asks before this finishes.
	internal static System.Collections.IEnumerator WarmArmoryBounds()
	{
		if (_armoryLooked)
		{
			yield break;
		}
		Transform best = null;
		float bestSqr = float.MaxValue;
		var budget = System.Diagnostics.Stopwatch.StartNew();
		for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
		{
			UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
			if (!scene.isLoaded)
			{
				continue;
			}
			GameObject[] roots = scene.GetRootGameObjects();
			foreach (GameObject root in roots)
			{
				if (_armoryLooked)
				{
					yield break;
				}
				if (!root)
				{
					continue;
				}
				// active-in-hierarchy only, like FindObjectsOfType
				Transform[] transforms = root.GetComponentsInChildren<Transform>(includeInactive: false);
				for (int t = 0; t < transforms.Length; t++)
				{
					ConsiderArmory(transforms[t], ref best, ref bestSqr);
					if (budget.ElapsedMilliseconds >= 3)
					{
						yield return null;
						if (_armoryLooked)
						{
							yield break;
						}
						budget.Restart();
					}
				}
			}
		}
		if (_armoryLooked)
		{
			yield break;
		}
		_armoryLooked = true;
		try
		{
			ApplyArmory(best, bestSqr);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[CrewLayer] armory lookup failed: " + ex.Message);
		}
	}

	internal static void Reset()
	{
		ByProfile.Clear();
		FirstSeen.Clear();
		_eventAt = -1f;
		_bdHold = null;
		_bdHoldLooked = false;
		_armoryBounds = null;
		_armoryLooked = false;
		_armoryNavAnchor = null;
		PendingForceAttacks.Clear();
		if ((bool)_forceAttackHost)
		{
			UnityEngine.Object.Destroy(_forceAttackHost);
		}
		_forceAttackHost = null;
		LastBlackDivRole = null;
		TerminalHangarCrew.ResetForRaid();
	}
}
