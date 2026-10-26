using System;
using System.Collections.Generic;
using System.Diagnostics;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace Manimal.Terminal;

internal static class TerminalPathDiagnostics
{
	internal struct Call
	{
		internal long Started;

		internal Vector3 From;

		internal Vector3 To;
	}

	private sealed class Repeat
	{
		internal Vector3 From;

		internal Vector3 To;

		internal int Calls;

		internal int Failures;

		internal float NextLog;
	}

	[HarmonyPatch(typeof(BotPathFinderCorePoints), "GoToPosition", new Type[]
	{
		typeof(IAICorePointLink),
		typeof(bool),
		typeof(float),
		typeof(bool)
	})]
	internal static class Patch_CoverRoute
	{
		[HarmonyPrefix]
		private static void Prefix(BotPathFinderCorePoints __instance, IAICorePointLink target, out Call __state)
		{
			__state = default;
			if (!TerminalGate.On || target == null || __instance._owner == null)
			{
				return;
			}
			try
			{
				__state.From = __instance._owner.Position;
				__state.To = target.Position;
				__state.Started = Stopwatch.GetTimestamp();
				if (_scopeDepth++ == 0)
				{
					_queries = (_failedQueries = 0);
					_queryTicks = 0L;
				}
			}
			catch
			{
				__state = default;
			}
		}

		[HarmonyFinalizer]
		private static void Finalizer(BotPathFinderCorePoints __instance, IAICorePointLink target, NavMeshPathStatus __result, Exception __exception, Call __state)
		{
			if (__state.Started == 0L)
			{
				return;
			}
			double num = (double)(Stopwatch.GetTimestamp() - __state.Started) * TickMs;
			_scopeDepth = Math.Max(0, _scopeDepth - 1);
			if (num < 5.0 || _scopeDepth != 0)
			{
				return;
			}
			try
			{
				if (!Repeats.TryGetValue(__instance, out var value))
				{
					value = (Repeats[__instance] = new Repeat());
				}
				if ((__state.From - value.From).sqrMagnitude > 0.01f || (__state.To - value.To).sqrMagnitude > 0.01f)
				{
					value.Calls = (value.Failures = 0);
					value.From = __state.From;
					value.To = __state.To;
				}
				value.Calls++;
				if (__exception != null || __result != NavMeshPathStatus.PathComplete)
				{
					value.Failures++;
				}
				float realtimeSinceStartup = Time.realtimeSinceStartup;
				if (realtimeSinceStartup >= _window)
				{
					_window = realtimeSinceStartup + 10f;
					_reports = 0;
				}
				if (!(realtimeSinceStartup < value.NextLog) && _reports < 4)
				{
					value.NextLog = realtimeSinceStartup + 10f;
					_reports++;
					bool flag = NavMesh.SamplePosition(__state.From, out var hit, 1.5f, -1);
					bool flag2 = NavMesh.SamplePosition(__state.To, out var hit2, 1.5f, -1);
					BotOwner owner = __instance._owner;
					CustomNavigationPoint customNavigationPoint = target as CustomNavigationPoint;
					Plugin.Log.LogWarning($"[PathPerf] bot={owner.Id} role={owner.Profile?.Info?.Settings?.Role} " + $"result={((__exception == null) ? __result.ToString() : __exception.GetType().Name)} cost={num:F1}ms " + $"navQueries={_queries} navFailed={_failedQueries} navTime={(double)_queryTicks * TickMs:F1}ms " + $"repeatedSlowCalls={value.Calls} failedCalls={value.Failures} " + $"from={__state.From:F2} to={__state.To:F2} coverId={customNavigationPoint?.Id ?? (-1)} " + "fromMeshDelta=" + (flag ? Vector3.Distance(__state.From, hit.position).ToString("F3") : "missing") + " toMeshDelta=" + (flag2 ? Vector3.Distance(__state.To, hit2.position).ToString("F3") : "missing"));
				}
			}
			catch
			{
			}
		}
	}

	[HarmonyPatch(typeof(BotPathFinderCorePoints), "FindPath")]
	internal static class Patch_NavQuery
	{
		[HarmonyPrefix]
		private static void Prefix(out long __state)
		{
			__state = ((_scopeDepth > 0) ? Stopwatch.GetTimestamp() : 0);
		}

		[HarmonyFinalizer]
		private static void Finalizer(bool __result, Exception __exception, long __state)
		{
			if (__state != 0L)
			{
				_queries++;
				if (__exception != null || !__result)
				{
					_failedQueries++;
				}
				_queryTicks += Stopwatch.GetTimestamp() - __state;
			}
		}
	}

	private static readonly Dictionary<BotPathFinderCorePoints, Repeat> Repeats = new Dictionary<BotPathFinderCorePoints, Repeat>();

	private static readonly double TickMs = 1000.0 / (double)Stopwatch.Frequency;

	private static float _window;

	private static int _reports;

	[ThreadStatic]
	private static int _scopeDepth;

	[ThreadStatic]
	private static int _queries;

	[ThreadStatic]
	private static int _failedQueries;

	[ThreadStatic]
	private static long _queryTicks;

	internal static void ResetForRaid()
	{
		Repeats.Clear();
		_window = 0f;
		_reports = 0;
		_scopeDepth = (_queries = (_failedQueries = 0));
		_queryTicks = 0L;
	}
}
