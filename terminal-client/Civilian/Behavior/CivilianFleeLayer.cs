using BepInEx.Logging;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace Manimal.Terminal.Civilian.Behavior;

internal sealed class CivilianFleeLayer(BotOwner botOwner, int priority) : CustomLayer(botOwner, priority)
{
	private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Terminal.Civ");

	private bool _wasActiveLastFrame;

	private bool _lastActive;

	private int _activeEvaluationFrame = -1;

	public override string GetName()
	{
		return "Terminal.CivilianFleeLayer";
	}

	public override bool IsActive()
	{
		int frameCount = Time.frameCount;
		if (_activeEvaluationFrame == frameCount)
		{
			return _lastActive;
		}
		_activeEvaluationFrame = frameCount;
		if (BotOwner == null || BotOwner.GetPlayer == null || !BotOwner.GetPlayer.HealthController.IsAlive)
		{
			_lastActive = false;
			return false;
		}
		CivilianFleeState.State state = CivilianFleeState.Get(BotOwner);
		if (state.CoweringUntil > Time.time)
		{
			state.MotionRequested = false;
			return RecordActive(active: true, 0f);
		}
		CivilianThreatScanner.ThreatResult threatResult = CivilianThreatScanner.FindNearestThreat(BotOwner, 25f);
		bool active;
		if (threatResult.HasThreat)
		{
			state.LastThreatSeenAt = Time.time;
			active = true;
		}
		else
		{
			active = Time.time - state.LastThreatSeenAt < 8f;
		}
		return RecordActive(active, threatResult.Distance);
	}

	private bool RecordActive(bool active, float dist)
	{
		_lastActive = active;
		LogTransition(active, dist);
		return active;
	}

	private void LogTransition(bool active, float dist)
	{
		if (active != _wasActiveLastFrame)
		{
			Log.LogInfo(string.Format("Flee {0} on {1} (dist={2:F1})", active ? "ENTER" : "EXIT", BotOwner.Profile?.Nickname ?? "?", dist));
			_wasActiveLastFrame = active;
		}
	}

	public override Action GetNextAction()
	{
		return new Action(typeof(CivilianFleeLogic), "civilian-flee");
	}

	public override bool IsCurrentActionEnding()
	{
		return !IsActive();
	}
}
