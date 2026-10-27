using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace Manimal.Terminal;

public class TerminalTierEventLogic : AIPlaceInfoLogic
{
	public string EventName = "none";

	public bool EnterRaise = true;

	public bool ExitRaise;

	public bool OnlyPlayerNotAI = true;

	private AIPlaceInfo _place;

	private float _nextCoopPoll;

	private readonly HashSet<string> _inside = new HashSet<string>();

	private readonly List<Player> _humans = new List<Player>();

	internal static readonly Dictionary<string, int> FireCounts = new Dictionary<string, int>();

	private void Update()
	{
		if (!TerminalCoop.Active || !TerminalCoop.Authority || Time.time < _nextCoopPoll)
		{
			return;
		}
		_nextCoopPoll = Time.time + 0.25f;
		BoxCollider component = GetComponent<BoxCollider>();
		if (!component || !component.enabled)
		{
			return;
		}
		TerminalCoop.CollectHumans(_humans);
		HashSet<string> present = new HashSet<string>();
		foreach (Player human in _humans)
		{
			Vector3 point = component.transform.InverseTransformPoint(human.Position);
			if (new Bounds(component.center, component.size).Contains(point))
			{
				present.Add(human.ProfileId);
				OnEnter(human);
			}
			else if (_inside.Contains(human.ProfileId))
			{
				OnExit(human);
			}
		}
		_inside.RemoveWhere((string id) => !present.Contains(id));
	}

	public override void Init(AIPlaceInfo aiPlaceInfo, BotsController botsController)
	{
		if (!(_place == aiPlaceInfo))
		{
			if (_place != null)
			{
				_place.OnPlayerEnter -= OnEnter;
				_place.OnPlayerExit -= OnExit;
			}
			_inside.Clear();
			base.Init(aiPlaceInfo, botsController);
			_place = aiPlaceInfo;
			_place.OnPlayerEnter += OnEnter;
			_place.OnPlayerExit += OnExit;
			Plugin.Log.LogDebug("[AIPlaces] event logic wired: '" + gameObject.name + "' -> '" + EventName + "'");
		}
	}

	public override void Dispose()
	{
		if (_place != null)
		{
			_place.OnPlayerEnter -= OnEnter;
			_place.OnPlayerExit -= OnExit;
		}
		_place = null;
		_inside.Clear();
		base.Dispose();
	}

	internal static void ResetFireCounts()
	{
		FireCounts.Clear();
	}

	private void OnEnter(Player p)
	{
		if (TerminalCoop.Authority && !(p == null) && (!OnlyPlayerNotAI || !p.IsAI) && (!TerminalCoop.Active || p.IsAI || _inside.Add(p.ProfileId)) && EnterRaise)
		{
			using var scope = TerminalTickProfiler.Measure("TierEvent.OnEnter");
			FireCounts.TryGetValue(EventName, out var value);
			value = (FireCounts[EventName] = value + 1);
			Plugin.Log.LogWarning($"[AIPlaces] ENTER '{gameObject.name}' -> raising event '{EventName}' (fire #{value} this raid)");
			TerminalCrewJobs.NoteEvent(EventName);
			Singleton<GlobalEventDispatcher>.Instance?.AnyEvent(EventName);
			TerminalPopulationDirector.EnsureProgressWavesActivated(EventName);
			TerminalCoop.Request(TerminalEvent.Wave, EventName);
		}
	}

	private void OnExit(Player p)
	{
		if (TerminalCoop.Authority && !(p == null) && (!OnlyPlayerNotAI || !p.IsAI) && (!TerminalCoop.Active || p.IsAI || _inside.Remove(p.ProfileId)))
		{
			Plugin.Log.LogWarning("[AIPlaces] EXIT  '" + gameObject.name + "' ('" + EventName + "')" + (ExitRaise ? " -> raising on exit too" : ""));
			if (ExitRaise)
			{
				Singleton<GlobalEventDispatcher>.Instance?.AnyEvent(EventName);
				TerminalPopulationDirector.EnsureProgressWavesActivated(EventName);
			}
		}
	}
}
