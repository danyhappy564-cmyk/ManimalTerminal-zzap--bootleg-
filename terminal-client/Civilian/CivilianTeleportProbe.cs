using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Manimal.Terminal.Civilian;

// Read-only attribution at native position writers. Prefixes take value snapshots;
// postfixes log only a confirmed displacement, never just an attempted correction.
internal static class CivilianTeleportProbe
{
	private const int MaxLines = 200, MaxBots = 512;
	private static readonly Dictionary<int, float> NextLog = new Dictionary<int, float>();
	private static int _logged;
	private static bool _faulted;

	internal struct Snapshot
	{
		internal Player Player;
		internal Vector3 Root, Body, Requested;
	}

	internal static void ResetForRaid()
	{
		NextLog.Clear();
		_logged = 0;
		_faulted = false;
	}

	private static bool Enabled => TerminalGate.On && Plugin.CivilianJumpProbe.Value && !_faulted && _logged < MaxLines;

	private static Snapshot Begin(Player player, Vector3 requested)
	{
		if (!Enabled || !player || player.IsYourPlayer) return default;
		try
		{
			var role = player.Profile?.Info?.Settings?.Role;
			if (!role.HasValue || !CivilianConstants.IsCivilian(role.Value) || player.HealthController?.IsAlive != true) return default;
			int id = player.GetInstanceID();
			if (NextLog.TryGetValue(id, out float until))
			{
				if (Time.realtimeSinceStartup < until) return default;
			}
			else if (NextLog.Count >= MaxBots) return default;
			return new Snapshot { Player = player, Root = player.Transform.Original.position, Body = player.Position, Requested = requested };
		}
		catch (Exception ex) { Disable(ex); return default; }
	}

	private static void End(Snapshot state, MethodBase method)
	{
		if (!state.Player || !Enabled) return;
		try
		{
			Player player = state.Player;
			int id = player.GetInstanceID();
			float now = Time.realtimeSinceStartup;
			// A nested position writer may already have claimed this displacement.
			if (NextLog.TryGetValue(id, out float until) && now < until) return;
			Vector3 root = player.Transform.Original.position, body = player.Position;
			float rootDist = Vector3.Distance(state.Root, root), bodyDist = Vector3.Distance(state.Body, body);
			float distance = Mathf.Max(rootDist, bodyDist);
			if (distance < 0.2f || (NextLog.Count >= MaxBots && !NextLog.ContainsKey(id))) return;
			NextLog[id] = now + 10f;
			_logged++;
			// Six caller frames on ONE physical log line, keeping the raid line cap.
			var trace = new StackTrace(1, false);
			var stack = new StringBuilder(512);
			int emitted = 0;
			for (int i = 0; i < trace.FrameCount && i < 24 && emitted < 6; i++)
			{
				MethodBase caller = trace.GetFrame(i)?.GetMethod();
				Type type = caller?.DeclaringType;
				if (caller == null || type == typeof(CivilianTeleportProbe) || type?.DeclaringType == typeof(CivilianTeleportProbe)) continue;
				if (emitted++ > 0) stack.Append(" <- ");
				stack.Append(type?.FullName ?? "<dynamic>").Append('.').Append(caller.Name);
			}
			Plugin.Log.LogWarning($"[CivTeleport] who={method.DeclaringType?.FullName}.{method.Name} bot={player.Profile?.Nickname} id={id} frame={Time.frameCount} "
				+ $"from={state.Root.ToString("F2")} -> to={root.ToString("F2")} dist={distance:F2}m rootDist={rootDist:F2} bodyDist={bodyDist:F2} "
				+ $"bodyFrom={state.Body.ToString("F2")} bodyTo={body.ToString("F2")} requested={state.Requested.ToString("F2")} stack={stack}");
		}
		catch (Exception ex) { Disable(ex); }
	}

	private static void Disable(Exception ex)
	{
		if (_faulted) return;
		_faulted = true;
		if (_logged < MaxLines)
		{
			_logged++;
			Plugin.Log.LogWarning("[CivTeleport] probe disabled for raid: " + ex.GetType().Name);
		}
	}

	[HarmonyPatch]
	internal static class Patch_MoverPosition
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(BotMover), "Teleport", new[] { typeof(Vector3) });
			yield return AccessTools.Method(typeof(BotMover), "SetPlayerToNavMesh", new[] { typeof(Vector3) });
			yield return AccessTools.Method(typeof(BotMover), "SetPosition", new[] { typeof(Vector3) });
			yield return AccessTools.Method(typeof(BotMover), "CastFromPos", new[] { typeof(Vector3), typeof(bool) });
		}
		[HarmonyPrefix]
		private static void Prefix(BotMover __instance, Vector3 __0, out Snapshot __state) { __state = Begin(__instance._player, __0); }
		[HarmonyPostfix]
		private static void Postfix(Snapshot __state, MethodBase __originalMethod) { End(__state, __originalMethod); }
	}

	[HarmonyPatch]
	internal static class Patch_PlayerPosition
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(Player), "Teleport", new[] { typeof(Vector3), typeof(bool) });
			yield return AccessTools.PropertySetter(typeof(Player), "Position");
		}
		[HarmonyPrefix]
		private static void Prefix(Player __instance, Vector3 __0, out Snapshot __state) { __state = Begin(__instance, __0); }
		[HarmonyPostfix]
		private static void Postfix(Snapshot __state, MethodBase __originalMethod) { End(__state, __originalMethod); }
	}

	[HarmonyPatch(typeof(MovementContext), "TransformPosition", MethodType.Setter)]
	internal static class Patch_MovementPosition
	{
		// Harmony injects MovementContext's protected _player field (three prefix
		// underscores plus the field's own underscore); no hot-path reflection.
		[HarmonyPrefix]
		private static void Prefix(Player ____player, Vector3 __0, out Snapshot __state) { __state = Begin(____player, __0); }
		[HarmonyPostfix]
		private static void Postfix(Snapshot __state, MethodBase __originalMethod) { End(__state, __originalMethod); }
	}
}
