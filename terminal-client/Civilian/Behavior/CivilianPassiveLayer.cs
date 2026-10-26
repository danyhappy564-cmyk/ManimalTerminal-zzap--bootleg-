using BepInEx.Logging;
using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace Manimal.Terminal.Civilian.Behavior;

internal sealed class CivilianPassiveLayer : CustomLayer
{
	private bool _loggedOnce;

	public CivilianPassiveLayer(BotOwner botOwner, int priority)
		: base(botOwner, priority)
	{
	}

	public override string GetName()
	{
		return "Terminal.CivilianPassiveLayer";
	}

	public override bool IsActive()
	{
		if (BotOwner == null || BotOwner.GetPlayer == null || !BotOwner.GetPlayer.HealthController.IsAlive)
		{
			return false;
		}
		if (!_loggedOnce)
		{
			_loggedOnce = true;
			string text = BotOwner.Brain?.BaseBrain?.ShortName() ?? "<null>";
			Logger.CreateLogSource("Terminal.Civ").LogInfo("Passive attached to " + BotOwner.Profile?.Nickname + " brain='" + text + "'");
		}
		CivilianFleeState.EnsureHomeInitialized(BotOwner);
		return true;
	}

	public override Action GetNextAction()
	{
		return new Action(typeof(CivilianIdleLogic), "civilian-idle");
	}

	public override bool IsCurrentActionEnding()
	{
		return false;
	}
}
