using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Manimal.Terminal;

internal static class TerminalLoopProbe
{
	[StructLayout(LayoutKind.Sequential, Size = 1)]
	private struct Mark<T>
	{
	}

	private static bool _installed;

	private static PlayerLoopSystem _original;

	private static long[] _stamps;

	private static double[] _cost;

	private static double[] _peak;

	private static string[] _names;

	private static readonly double TickMs = 1000.0 / (double)Stopwatch.Frequency;

	private static readonly Type[] MarkTypes = new Type[32]
	{
		typeof(Mark<byte>),
		typeof(Mark<sbyte>),
		typeof(Mark<short>),
		typeof(Mark<ushort>),
		typeof(Mark<int>),
		typeof(Mark<uint>),
		typeof(Mark<long>),
		typeof(Mark<ulong>),
		typeof(Mark<float>),
		typeof(Mark<double>),
		typeof(Mark<decimal>),
		typeof(Mark<char>),
		typeof(Mark<bool>),
		typeof(Mark<string>),
		typeof(Mark<object>),
		typeof(Mark<Vector2>),
		typeof(Mark<Vector3>),
		typeof(Mark<Vector4>),
		typeof(Mark<Quaternion>),
		typeof(Mark<Color>),
		typeof(Mark<Rect>),
		typeof(Mark<Bounds>),
		typeof(Mark<Matrix4x4>),
		typeof(Mark<Ray>),
		typeof(Mark<Plane>),
		typeof(Mark<Color32>),
		typeof(Mark<Vector2Int>),
		typeof(Mark<Vector3Int>),
		typeof(Mark<RectInt>),
		typeof(Mark<BoundsInt>),
		typeof(Mark<LayerMask>),
		typeof(Mark<Keyframe>)
	};

	private static float _nextVerify;

	private static int _reinstalls;

	internal static void Tick()
	{
		bool flag = TerminalGate.On && Plugin.DevMode.Value && Plugin.ProfilePlayerLoop.Value;
		if (flag && !_installed)
		{
			Install();
		}
		else if (!flag && _installed)
		{
			Restore();
		}
		else
		{
			if (!flag || !_installed || Time.realtimeSinceStartup < _nextVerify)
			{
				return;
			}
			_nextVerify = Time.realtimeSinceStartup + 2f;
			if (!MarkersPresent())
			{
				_reinstalls++;
				_installed = false;
				Install();
				if (_reinstalls <= 5)
				{
					Plugin.Log.LogWarning($"[LoopProbe] markers had been evicted from the player loop — re-armed (#{_reinstalls}). " + "readings before this point were stale.");
				}
			}
		}
	}

	private static bool MarkersPresent()
	{
		try
		{
			PlayerLoopSystem[] subSystemList = PlayerLoop.GetCurrentPlayerLoop().subSystemList;
			if (subSystemList == null)
			{
				return false;
			}
			for (int i = 0; i < subSystemList.Length; i++)
			{
				if (subSystemList[i].type != typeof(PostLateUpdate))
				{
					continue;
				}
				PlayerLoopSystem[] subSystemList2 = subSystemList[i].subSystemList;
				if (subSystemList2 == null)
				{
					return false;
				}
				for (int j = 0; j < subSystemList2.Length; j++)
				{
					if (subSystemList2[j].type == MarkTypes[0])
					{
						return true;
					}
				}
				return false;
			}
		}
		catch
		{
		}
		return false;
	}

	internal static double InstantOf(string name)
	{
		if (!_installed || _names == null)
		{
			return 0.0;
		}
		for (int i = 0; i < _names.Length; i++)
		{
			if (_names[i] == name)
			{
				double num = (double)(_stamps[i + 1] - _stamps[i]) * TickMs;
				if (!(num < 0.0) && !(num > 5000.0))
				{
					return num;
				}
				return 0.0;
			}
		}
		return 0.0;
	}

	internal static long EndStampOf(string name)
	{
		if (!_installed || _names == null)
		{
			return 0L;
		}
		for (int i = 0; i < _names.Length; i++)
		{
			if (_names[i] == name)
			{
				return _stamps[i + 1];
			}
		}
		return 0L;
	}

	internal static double MsBetween(long a, long b)
	{
		if (a == 0L || b == 0L)
		{
			return 0.0;
		}
		double num = (double)(b - a) * TickMs;
		if (!(num < 0.0) && !(num > 5000.0))
		{
			return num;
		}
		return 0.0;
	}

	internal static double MeasuredSum()
	{
		if (!_installed || _cost == null)
		{
			return 0.0;
		}
		double num = 0.0;
		for (int i = 0; i < _cost.Length; i++)
		{
			num += _cost[i];
		}
		return num;
	}

	private static void Install()
	{
		try
		{
			_original = PlayerLoop.GetCurrentPlayerLoop();
			PlayerLoopSystem currentPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
			PlayerLoopSystem[] subSystemList = currentPlayerLoop.subSystemList;
			if (subSystemList == null)
			{
				Plugin.Log.LogWarning("[LoopProbe] player loop has no subsystems");
				return;
			}
			for (int i = 0; i < subSystemList.Length; i++)
			{
				if (subSystemList[i].type != typeof(PostLateUpdate))
				{
					continue;
				}
				PlayerLoopSystem[] subSystemList2 = subSystemList[i].subSystemList;
				if (subSystemList2 == null || subSystemList2.Length == 0)
				{
					break;
				}
				int num = Mathf.Min(subSystemList2.Length, MarkTypes.Length - 1);
				_stamps = new long[num + 1];
				_cost = new double[num];
				_peak = new double[num];
				_names = new string[num];
				List<PlayerLoopSystem> list = new List<PlayerLoopSystem>(subSystemList2.Length + num + 1);
				for (int j = 0; j < num; j++)
				{
					_names[j] = ((subSystemList2[j].type != null) ? subSystemList2[j].type.Name : "native");
					int idx = j;
					list.Add(new PlayerLoopSystem
					{
						type = MarkTypes[j],
						updateDelegate = () =>
						{
							_stamps[idx] = Stopwatch.GetTimestamp();
						}
					});
					list.Add(subSystemList2[j]);
				}
				list.Add(new PlayerLoopSystem
				{
					type = MarkTypes[num],
					updateDelegate = FoldFrame
				});
				for (int num2 = num; num2 < subSystemList2.Length; num2++)
				{
					list.Add(subSystemList2[num2]);
				}
				subSystemList[i].subSystemList = list.ToArray();
				currentPlayerLoop.subSystemList = subSystemList;
				PlayerLoop.SetPlayerLoop(currentPlayerLoop);
				_installed = true;
				if (_reinstalls == 0)
				{
					StringBuilder stringBuilder = new StringBuilder();
					for (int num3 = 0; num3 < num; num3++)
					{
						stringBuilder.Append("\n    ").Append(num3).Append(". ")
							.Append(_names[num3]);
					}
					if (subSystemList2.Length > num)
					{
						stringBuilder.Append($"\n    (+{subSystemList2.Length - num} more beyond the marker budget — NOT measured)");
					}
					Plugin.Log.LogWarning($"[LoopProbe] instrumented {num}/{subSystemList2.Length} PostLateUpdate subsystem(s):{stringBuilder}");
				}
				return;
			}
			Plugin.Log.LogWarning("[LoopProbe] PostLateUpdate not found in the player loop");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[LoopProbe] install failed: " + ex.Message);
			try
			{
				PlayerLoop.SetPlayerLoop(_original);
			}
			catch
			{
			}
			_installed = false;
		}
	}

	private static void FoldFrame()
	{
		_stamps[_stamps.Length - 1] = Stopwatch.GetTimestamp();
		for (int i = 0; i < _cost.Length; i++)
		{
			double num = (double)(_stamps[i + 1] - _stamps[i]) * TickMs;
			if (!(num < 0.0) && !(num > 5000.0))
			{
				_cost[i] = _cost[i] * 0.95 + num * 0.05;
				if (num > _peak[i])
				{
					_peak[i] = num;
				}
			}
		}
	}

	internal static void Restore()
	{
		if (_installed)
		{
			try
			{
				PlayerLoop.SetPlayerLoop(_original);
			}
			catch
			{
			}
			_installed = false;
			Plugin.Log.LogWarning("[LoopProbe] player loop restored");
		}
	}

	internal static void ResetForRaid()
	{
		if (_cost != null)
		{
			Array.Clear(_cost, 0, _cost.Length);
		}
		if (_peak != null)
		{
			Array.Clear(_peak, 0, _peak.Length);
		}
	}

	internal static string TopN(int n)
	{
		if (!_installed || _cost == null)
		{
			return "";
		}
		List<int> list = new List<int>(_cost.Length);
		for (int i = 0; i < _cost.Length; i++)
		{
			list.Add(i);
		}
		list.Sort((int a, int b) => _cost[b].CompareTo(_cost[a]));
		StringBuilder stringBuilder = new StringBuilder();
		int num = Mathf.Min(n, list.Count);
		for (int num2 = 0; num2 < num; num2++)
		{
			int num3 = list[num2];
			if (_cost[num3] < 0.05)
			{
				break;
			}
			if (num2 > 0)
			{
				stringBuilder.Append(' ');
			}
			stringBuilder.Append(_names[num3]).Append('=').Append(_cost[num3].ToString("F1"))
				.Append('/')
				.Append(_peak[num3].ToString("F0"))
				.Append('p');
		}
		return stringBuilder.ToString();
	}
}
