using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalShadowGuard
{
	private static float _next;

	private static float _nextRefresh;

	private static bool _dumped;

	private static int _lastKilled;

	private static int _lastAlive = -1;

	private static readonly List<Light> _candidates = new List<Light>();

	private static readonly List<Light> _lamps = new List<Light>();

	internal static void ResetForRaid()
	{
		_next = 0f;
		_nextRefresh = 0f;
		_dumped = false;
		_lastKilled = 0;
		_lastAlive = -1;
		_lastRefreshAt = -100f;
		_candidates.Clear();
		_lamps.Clear();
		_isMapLight.Clear();
	}

	// zzap perf: Refresh() walks ~1900 lights and every parent's components (~28ms).
	// It ran on every change of the alive count — with bots spawning and dying that was
	// roughly once a second. Alive-count refreshes are now at least 5s apart (the 20s
	// timer is unchanged), and a light's map/actor classification is remembered.
	private static float _lastRefreshAt = -100f;

	private static readonly Dictionary<int, bool> _isMapLight = new Dictionary<int, bool>();

	private static bool IsMapLight(Light l)
	{
		try
		{
			int id = l.GetInstanceID();
			if (_isMapLight.TryGetValue(id, out var known))
			{
				return known;
			}
			bool result = false;
			Transform transform = l.transform;
			while (transform != null)
			{
				if (transform.GetComponent<CullingLightObject>() != null)
				{
					result = true;
					break;
				}
				transform = transform.parent;
			}
			_isMapLight[id] = result;
			return result;
		}
		catch
		{
			return false;
		}
	}

	internal static void Tick()
	{
		if (!TerminalGate.On || Time.realtimeSinceStartup < _next)
		{
			return;
		}
		_next = Time.realtimeSinceStartup + 1f;
		try
		{
			int num = -1;
			try
			{
				GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
				num = ((gameWorld != null && gameWorld.AllAlivePlayersList != null) ? gameWorld.AllAlivePlayersList.Count : (-1));
			}
			catch
			{
			}
			if ((num != _lastAlive && Time.realtimeSinceStartup - _lastRefreshAt >= 5f) || Time.realtimeSinceStartup >= _nextRefresh)
			{
				_lastAlive = num;
				_lastRefreshAt = Time.realtimeSinceStartup;
				_nextRefresh = Time.realtimeSinceStartup + 20f;
				Refresh();
			}
			int num2 = 0;
			if (!Plugin.WeaponLightShadows.Value)
			{
				num2 += Sweep(_candidates);
			}
			if (!Plugin.LampShadows.Value)
			{
				num2 += Sweep(_lamps);
			}
			if (num2 > 0 && num2 != _lastKilled)
			{
				_lastKilled = num2;
				Plugin.Log.LogInfo($"[ShadowGuard] shadows dropped on {num2} light(s) this pass " + $"({_candidates.Count} weapon/actor + {_lamps.Count} lamp(s) tracked)");
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[ShadowGuard] sweep failed: " + ex.Message);
		}
	}

	private static int Sweep(List<Light> list)
	{
		int num = 0;
		for (int num2 = list.Count - 1; num2 >= 0; num2--)
		{
			Light light = list[num2];
			if (light == null)
			{
				list.RemoveAt(num2);
			}
			else if (light.shadows != LightShadows.None)
			{
				light.shadows = LightShadows.None;
				num++;
			}
		}
		return num;
	}

	private static void Refresh()
	{
		_candidates.Clear();
		_lamps.Clear();
		List<string> list = (_dumped ? null : new List<string>());
		Light[] array = UnityEngine.Object.FindObjectsOfType<Light>();
		foreach (Light light in array)
		{
			if (light == null)
			{
				continue;
			}
			bool flag = IsMapLight(light);
			(flag ? _lamps : _candidates).Add(light);
			if (list != null && light.shadows != LightShadows.None && list.Count < 14)
			{
				string text = light.gameObject.name;
				Transform parent = light.transform.parent;
				while (parent != null)
				{
					text = parent.name + "/" + text;
					parent = parent.parent;
				}
				list.Add(string.Format("\n    [{0}] {1} shadows={2} ", flag ? "LAMP" : "weapon/actor", light.type, light.shadows) + $"range={light.range:0.#} intensity={light.intensity:0.##} {text}");
			}
		}
		if (list != null && list.Count > 0)
		{
			_dumped = true;
			Plugin.Log.LogWarning("[ShadowGuard] shadow-casting light(s) found — each renders a shadow map " + $"per frame ({_lamps.Count} lamps / {_candidates.Count} weapon-actor tracked):" + string.Join("", list.ToArray()));
		}
	}
}
