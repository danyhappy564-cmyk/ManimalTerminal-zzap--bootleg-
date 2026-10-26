using System;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace Manimal.Terminal;

internal class TerminalCrewLayer : CustomLayer
{
	public TerminalCrewLayer(BotOwner botOwner, int priority)
		: base(botOwner, priority)
	{
	}

	public override string GetName()
	{
		return "TerminalCrew";
	}

	public override bool IsActive()
	{
		if (!TerminalGate.On)
		{
			return false;
		}
		TerminalCrewJobs.Rec rec = TerminalCrewJobs.For(BotOwner);
		if (rec == null || rec.Job == TerminalCrewJobs.Job.None)
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
			if (BotOwner.Memory != null)
			{
				if (BotOwner.Memory.IsUnderFire)
				{
					return false;
				}
				EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
				if (goalEnemy != null)
				{
					TerminalCrewJobs.Rec rec2 = TerminalCrewJobs.For(BotOwner);
					if (rec2 == null || rec2.Job != TerminalCrewJobs.Job.Siege)
					{
						return false;
					}
					if (goalEnemy.IsVisible || Time.time - goalEnemy.TimeLastSeenReal < 8f)
					{
						return false;
					}
				}
			}
		}
		catch
		{
			return false;
		}
		return true;
	}

	public override Action GetNextAction()
	{
		return TerminalCrewJobs.For(BotOwner)?.Job switch
		{
			TerminalCrewJobs.Job.Hunt => new Action(typeof(TerminalHuntLogic), "hunt the players"), 
			TerminalCrewJobs.Job.Siege => new Action(typeof(TerminalSiegeLogic), "hold the armory"), 
			_ => new Action(typeof(TerminalGuardLogic), "guard the zone"), 
		};
	}

	public override bool IsCurrentActionEnding()
	{
		Type type = TerminalCrewJobs.For(BotOwner)?.Job switch
		{
			TerminalCrewJobs.Job.Hunt => typeof(TerminalHuntLogic), 
			TerminalCrewJobs.Job.Siege => typeof(TerminalSiegeLogic), 
			_ => typeof(TerminalGuardLogic), 
		};
		if (CurrentAction != null)
		{
			return CurrentAction.Type != type;
		}
		return true;
	}
}
