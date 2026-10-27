using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalStageWait
{
	internal sealed class Patch_Brain : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Constructor(typeof(BaseBrain), new Type[1] { typeof(BotOwner) });
		}

		[PatchPostfix]
		private static void Postfix(BaseBrain __instance, BotOwner owner)
		{
			if (!Enabled || !TerminalGate.On || !TerminalCoop.Authority || !Plugin.StageDirector.Value)
			{
				return;
			}
			if (__instance._layers.ContainsKey(24) || Enum.IsDefined(typeof(BotLogicDecision), (BotLogicDecision)7000))
			{
				Plugin.Log.LogError("[StageDirector] wait adapter ID collision; leaving the existing brain intact");
				return;
			}
			WaitLayer waitLayer = new WaitLayer(owner);
			if (__instance.TryAddLayer(24, waitLayer, activeOnStart: true))
			{
				Layers.Add(waitLayer);
			}
		}
	}

	internal sealed class Patch_Node : ModulePatch
	{
		protected override MethodBase GetTargetMethod()
		{
			return AccessTools.Method(typeof(AIActionsList), "CreateNode");
		}

		[PatchPrefix]
		private static bool Prefix(BotLogicDecision type, BotOwner bot, ref AICoreNode __result)
		{
			if (type != (BotLogicDecision)7000)
			{
				return true;
			}
			__result = new WaitNode(bot);
			return false;
		}
	}

	private sealed class WaitLayer : BaseLogicLayer
	{
		private bool _isHit;

		private bool _entered;

		private readonly Dictionary<Renderer, bool> _visibility = new Dictionary<Renderer, bool>();

		internal bool OwnsVisibility => _entered;

		internal WaitLayer(BotOwner owner)
			: base(owner, 150)
		{
		}

		public override string Name()
		{
			return "TerminalWait";
		}

		public override bool ShallUseNow()
		{
			if (_entered && (bool)_owner && !_owner.IsDead && _owner.Memory != null && IsHit(10f))
			{
				_isHit = true;
			}
			int num;
			if (!_isHit && (bool)_owner && !_owner.IsDead)
			{
				num = (TerminalStageDirector.ShouldHold(_owner) ? 1 : 0);
				if (num != 0)
				{
					goto IL_007c;
				}
			}
			else
			{
				num = 0;
			}
			ReleaseVisibility();
			goto IL_007c;
			IL_007c:
			return (byte)num != 0;
		}

		public override AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision()
		{
			// zzap: restore Ghost's flags BEFORE Wait snapshots its own visibility.
			TerminalGhostDirector.WakeNow(_owner, TerminalGhostDirector.WakeReason.Protect);
			_entered = true;
			ApplyWait();
			return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>((BotLogicDecision)7000, "wait");
		}

		public override AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
		{
			if (decision.Action != (BotLogicDecision)7000)
			{
				return FinishNodeLogic;
			}
			return ContinueNodeLogic;
		}

		internal void ApplyWait()
		{
			if (!_owner)
			{
				return;
			}
			_owner.ShootData?.EndShoot();
			_owner.Sprint(val: false);
			_owner.Mover?.Stop();
			if (_visibility.Count == 0 && (bool)_owner.GetPlayer)
			{
				Renderer[] componentsInChildren = _owner.GetPlayer.GetComponentsInChildren<Renderer>(includeInactive: true);
				foreach (Renderer renderer in componentsInChildren)
				{
					if ((bool)renderer)
					{
						_visibility[renderer] = renderer.forceRenderingOff;
					}
				}
			}
			foreach (KeyValuePair<Renderer, bool> item in _visibility)
			{
				if ((bool)item.Key)
				{
					item.Key.forceRenderingOff = true;
				}
			}
		}

		internal void ReleaseVisibility()
		{
			foreach (KeyValuePair<Renderer, bool> item in _visibility)
			{
				if ((bool)item.Key)
				{
					item.Key.forceRenderingOff = item.Value;
				}
			}
			_visibility.Clear();
			_entered = false;
		}

		public override void Dispose()
		{
			ReleaseVisibility();
			Layers.Remove(this);
			base.Dispose();
		}
	}

	private sealed class WaitNode : BaseNode<CoreActionResultParams>
	{
		internal WaitNode(BotOwner owner)
			: base(owner)
		{
		}

		public override void UpdateNodeByBrain(CoreActionResultParams data)
		{
			BaseBrain baseBrain = _owner?.Brain?.BaseBrain;
			if (baseBrain != null && baseBrain._layers.TryGetValue(24, out var value) && value is WaitLayer waitLayer)
			{
				waitLayer.ApplyWait();
			}
		}
	}

	internal static bool Enabled;

	private const int LayerId = 24;

	private const BotLogicDecision WaitDecision = (BotLogicDecision)7000;

	private static readonly HashSet<WaitLayer> Layers = new HashSet<WaitLayer>();

	internal static bool OwnsVisibility(BotOwner bot)
	{
		BaseBrain baseBrain = bot?.Brain?.BaseBrain;
		if (baseBrain != null && baseBrain._layers.TryGetValue(24, out var value) && value is WaitLayer waitLayer)
		{
			return waitLayer.OwnsVisibility;
		}
		return false;
	}

	internal static void Release(BotOwner bot)
	{
		BaseBrain baseBrain = bot?.Brain?.BaseBrain;
		if (baseBrain != null && baseBrain._layers.TryGetValue(24, out var value) && value is WaitLayer waitLayer)
		{
			waitLayer.ReleaseVisibility();
		}
	}

	internal static void ResetForRaid()
	{
		foreach (WaitLayer layer in Layers)
		{
			layer.ReleaseVisibility();
		}
		Layers.Clear();
	}

	internal static void ReleaseAfterRaid() { Layers.Clear(); }
}
