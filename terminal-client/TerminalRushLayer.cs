using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace Manimal.Terminal;

internal class TerminalRushLayer : CustomLayer
{
	private bool _contactHandoffLogged;

	public TerminalRushLayer(BotOwner botOwner, int priority)
		: base(botOwner, priority)
	{
	}

	public override string GetName()
	{
		return "TerminalCrewRush";
	}

	public override bool IsActive()
	{
		if (!TerminalGate.On)
		{
			return false;
		}
		TerminalCrewJobs.Rec rec = TerminalCrewJobs.For(BotOwner);
		if (rec == null || rec.Job == TerminalCrewJobs.Job.None || Time.time >= rec.RushUntil)
		{
			return false;
		}
		Player getPlayer = BotOwner.GetPlayer;
		if (!getPlayer || getPlayer.HealthController == null || !getPlayer.HealthController.IsAlive)
		{
			return false;
		}
		try
		{
			EnemyInfo enemyInfo = BotOwner.Memory?.GoalEnemy;
			float num = (TerminalSainCompat.Detected ? 18f : 40f);
			if (enemyInfo != null && enemyInfo.IsVisible && (enemyInfo.CurrPosition - BotOwner.Position).sqrMagnitude < num * num)
			{
				if (!_contactHandoffLogged)
				{
					_contactHandoffLogged = true;
					Plugin.Log.LogDebug($"[CrewLayer] {BotOwner.name}: contact at <{num:0}m — deployment -> " + (TerminalSainCompat.Detected ? "SAIN combat" : "native combat"));
				}
				return false;
			}
		}
		catch
		{
		}
		return true;
	}

	public override Action GetNextAction()
	{
		return new Action(typeof(TerminalHuntLogic), "rush the players");
	}

	public override bool IsCurrentActionEnding()
	{
		if (CurrentAction != null)
		{
			return CurrentAction.Type != typeof(TerminalHuntLogic);
		}
		return true;
	}
}
