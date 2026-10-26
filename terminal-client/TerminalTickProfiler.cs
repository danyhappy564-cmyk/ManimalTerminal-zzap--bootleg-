using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalTickProfiler
{
	private sealed class Entry
	{
		internal double EmaMs;

		internal double LastMs;

		internal int LastSampleFrame = -1;

		internal readonly FrameSample[] Samples = new FrameSample[4];

		internal void Add(int frame, double ms)
		{
			int num = frame % 4;
			if (num < 0)
			{
				num += 4;
			}
			ref FrameSample reference = ref Samples[num];
			if (reference.Frame != frame)
			{
				reference.Frame = frame;
				reference.TotalMs = 0.0;
				reference.Calls = 0;
			}
			reference.TotalMs += ms;
			reference.Calls++;
			EmaMs = EmaMs * 0.95 + ms * 0.05;
			LastMs = ms;
			LastSampleFrame = frame;
		}

		internal bool TryGet(int frame, out FrameCost cost)
		{
			int num = frame % 4;
			if (num < 0)
			{
				num += 4;
			}
			FrameSample frameSample = Samples[num];
			if (frameSample.Frame != frame || frameSample.Calls == 0)
			{
				cost = default;
				return false;
			}
			cost = new FrameCost(frameSample.TotalMs, frameSample.Calls);
			return true;
		}

		internal FrameCost Get(int frame)
		{
			if (!TryGet(frame, out var cost))
			{
				return default;
			}
			return cost;
		}
	}

	private struct FrameSample
	{
		internal int Frame;

		internal double TotalMs;

		internal int Calls;
	}

	private readonly struct FrameCost
	{
		internal readonly double TotalMs;

		internal readonly int Calls;

		internal FrameCost(double totalMs, int calls)
		{
			TotalMs = totalMs;
			Calls = calls;
		}
	}

	private readonly struct NamedFrameCost
	{
		internal readonly string Name;

		internal readonly FrameCost Cost;

		internal NamedFrameCost(string name, FrameCost cost)
		{
			Name = name;
			Cost = cost;
		}
	}

	internal readonly struct Scope : IDisposable
	{
		private readonly string _name;

		private readonly long _started;

		private readonly int _frame;

		internal Scope(string name)
		{
			_name = name;
			_started = Stopwatch.GetTimestamp();
			_frame = Time.frameCount;
		}

		public void Dispose()
		{
			if (!string.IsNullOrEmpty(_name))
			{
				Add(_name, Stopwatch.GetTimestamp() - _started, _frame);
			}
		}
	}

	private const int FrameHistoryLength = 4;

	private const double SlowScopeMs = 100.0;

	private const double GiantScopeMs = 1000.0;

	private const int SlowLogCooldownFrames = 30;

	private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(32);

	private static readonly Dictionary<string, int> LastSlowLogFrame = new Dictionary<string, int>(16);

	private static readonly double TickToMs = 1000.0 / (double)Stopwatch.Frequency;

	internal static Scope Measure(string name)
	{
		return new Scope(name);
	}

	internal static void Add(string name, long elapsedTicks)
	{
		Add(name, elapsedTicks, Time.frameCount);
	}

	private static void Add(string name, long elapsedTicks, int frame)
	{
		if (!string.IsNullOrEmpty(name))
		{
			if (!Entries.TryGetValue(name, out var value))
			{
				value = new Entry();
				Entries[name] = value;
			}
			double num = (double)elapsedTicks * TickToMs;
			value.Add(frame, num);
			if (num >= 100.0)
			{
				LogSlowScope(name, frame, num);
			}
		}
	}

	private static void LogSlowScope(string name, int frame, double ms)
	{
		if (!LastSlowLogFrame.TryGetValue(name, out var value) || frame - value >= 30 || ms >= 1000.0)
		{
			LastSlowLogFrame[name] = frame;
			try
			{
				Plugin.Log.LogWarning($"[PerfScope] frame={frame} name={name} elapsed={ms:F1}ms");
			}
			catch
			{
			}
		}
	}

	internal static void Reset()
	{
		Entries.Clear();
		LastSlowLogFrame.Clear();
	}

	internal static string TopN(int n)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		if (Entries.Count == 0 || n <= 0)
		{
			return "";
		}
		KeyValuePair<string, Entry>[] array = ValueEnumerableExtensions.ToArray<OrderBySkipTake<FromDictionary<string, Entry>, KeyValuePair<string, Entry>, double>, KeyValuePair<string, Entry>>(ValueEnumerableExtensions.Take<FromDictionary<string, Entry>, KeyValuePair<string, Entry>, double>(ValueEnumerableExtensions.OrderByDescending<FromDictionary<string, Entry>, KeyValuePair<string, Entry>, double>(ValueEnumerable.AsValueEnumerable<string, Entry>(Entries), (Func<KeyValuePair<string, Entry>, double>)((KeyValuePair<string, Entry> pair) => pair.Value.EmaMs)), n));
		StringBuilder stringBuilder = new StringBuilder();
		int num = array.Length;
		for (int num2 = 0; num2 < num; num2++)
		{
			if (num2 > 0)
			{
				stringBuilder.Append(' ');
			}
			Entry value = array[num2].Value;
			stringBuilder.Append(array[num2].Key).Append('=').Append(value.LastMs.ToString("F1"))
				.Append("ms/")
				.Append(value.EmaMs.ToString("F1"))
				.Append("avg(lastFrame=")
				.Append(value.LastSampleFrame)
				.Append(')');
		}
		return stringBuilder.ToString();
	}

	internal static string PreviousFrameSummary()
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		int frame = Time.frameCount - 1;
		NamedFrameCost[] array = ValueEnumerableExtensions.ToArray<OrderBy<SelectWhere<FromDictionary<string, Entry>, KeyValuePair<string, Entry>, NamedFrameCost>, NamedFrameCost, double>, NamedFrameCost>(ValueEnumerableExtensions.OrderByDescending<SelectWhere<FromDictionary<string, Entry>, KeyValuePair<string, Entry>, NamedFrameCost>, NamedFrameCost, double>(ValueEnumerableExtensions.Where<FromDictionary<string, Entry>, KeyValuePair<string, Entry>, NamedFrameCost>(ValueEnumerableExtensions.Select<FromDictionary<string, Entry>, KeyValuePair<string, Entry>, NamedFrameCost>(ValueEnumerable.AsValueEnumerable<string, Entry>(Entries), (Func<KeyValuePair<string, Entry>, NamedFrameCost>)((KeyValuePair<string, Entry> pair) => new NamedFrameCost(pair.Key, pair.Value.Get(frame)))), (Func<NamedFrameCost, bool>)((NamedFrameCost row) => row.Cost.Calls > 0)), (Func<NamedFrameCost, double>)((NamedFrameCost row) => row.Cost.TotalMs)));
		StringBuilder stringBuilder = new StringBuilder(96);
		stringBuilder.Append("frame=").Append(frame).Append(" scopes=")
			.Append(array.Length);
		for (int num = 0; num < array.Length; num++)
		{
			NamedFrameCost namedFrameCost = array[num];
			stringBuilder.Append(' ').Append(namedFrameCost.Name).Append(" totalMs=")
				.Append(namedFrameCost.Cost.TotalMs.ToString("F1"))
				.Append(" calls=")
				.Append(namedFrameCost.Cost.Calls);
		}
		return stringBuilder.ToString();
	}
}
