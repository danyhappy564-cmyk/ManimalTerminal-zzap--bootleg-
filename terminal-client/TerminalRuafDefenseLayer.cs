using System;
using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace Manimal.Terminal;

internal class TerminalRuafDefenseLayer : CustomLayer
{
	public TerminalRuafDefenseLayer(BotOwner botOwner, int priority)
		: base(botOwner, priority)
	{
	}

	public override string GetName()
	{
		return "RuafDefense";
	}

	public override bool IsActive()
	{
		try
		{
			if (!TerminalGate.On || !Plugin.RuafDefense.Value)
			{
				return false;
			}
			string text = BotOwner.Profile?.Info?.Settings?.Role.ToString() ?? "";
			if (text.IndexOf("ruaf", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("vsrf", StringComparison.OrdinalIgnoreCase) < 0)
			{
				return false;
			}
			Player getPlayer = BotOwner.GetPlayer;
			if (!getPlayer || getPlayer.HealthController == null || !getPlayer.HealthController.IsAlive)
			{
				return false;
			}
			EnemyInfo enemyInfo = BotOwner.Memory?.GoalEnemy;
			if (enemyInfo == null)
			{
				return false;
			}
			if (enemyInfo.IsVisible)
			{
				return false;
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	public override Action GetNextAction()
	{
		return new Action(typeof(TerminalRuafDefenseLogic), "squad defense");
	}

	public override bool IsCurrentActionEnding()
	{
		if (CurrentAction != null)
		{
			return CurrentAction.Type != typeof(TerminalRuafDefenseLogic);
		}
		return true;
	}
}
