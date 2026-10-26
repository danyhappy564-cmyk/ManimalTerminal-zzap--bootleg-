using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.GameTriggers;
using EFT.Interactive;
using EFT.InventoryLogic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalGatesExplosion
{
	internal const string Suffix = "3037225082";

	internal const string PlaceTrigger = "place_bomb_3037225082";

	internal const string ExplosionTrigger = "bomb_explosion_3037225082";

	internal const string SoloOpenTrigger = "gates_opened_solo_3037225082";

	internal const string DuoWaitRegisterTrigger = "wait_open_gates_duo_3037225082";

	internal const string DuoWaitCancelTrigger = "dropped_wait_open_gates_duo_3037225082";

	internal const string DuoReadyTrigger = "start_open_gates_duo_3037225082";

	internal const string DuoOpenRegisterTrigger = "opened_gates_duo_3037225082";

	internal const string DuoOpenCancelTrigger = "dropped_opening_gates_duo_3037225082";

	internal const string DuoFinalTrigger = "gates_opened_duo_confirmed_3037225082";

	internal const float PlantSeconds = 5f;

	internal const float OpenSeconds = 5f;

	internal const float DuoWaitSeconds = 100f;

	internal const float DuoRangeMeters = 5f;

	internal static readonly string[] ChargeTpls = new string[2] { "69a0174087a75d2cbd0842e8", "6819f8df28294ec0730db6b4" };

	internal static bool Planted;

	internal static bool Opened;

	internal static bool _boomSeen;

	private static bool _staged;

	private static Transform _root;

	private static Transform _soloOpeningBranch;

	private static GateHoldSession _duoWaitSession;

	private static GateHoldSession _duoOpenSession;

	private static GamePlayerOwner _duoOwner;

	private static Transform _duoAnchor;

	private static bool _duoOpeningBranchSuppressed;

	private static readonly HashSet<string> NativeSet = new HashSet<string> { "HandlerAnimator", "HandlerExplosion", "HandlerGameObjectState", "HandlerGOState" };

	internal static bool Ready => _staged;

	internal static Transform OpeningAnchor
	{
		get
		{
			if (!_root)
			{
				_root = FindRoot();
			}
			if (!_root)
			{
				return null;
			}
			return TerminalRigFill.FindChildNamed(_root, "SwitchBoard");
		}
	}

	internal static bool DuoSessionActive
	{
		get
		{
			if (!(_duoWaitSession != null) && !(_duoOpenSession != null))
			{
				return _duoOpeningBranchSuppressed;
			}
			return true;
		}
	}

	internal static bool SoloOpeningBranchActive
	{
		get
		{
			if (_duoOpeningBranchSuppressed)
			{
				return false;
			}
			if (!_soloOpeningBranch)
			{
				if (!_root)
				{
					_root = FindRoot();
				}
				if ((bool)_root)
				{
					_soloOpeningBranch = TerminalRigFill.FindChildNamed(_root, "SoloOpeningTrigger");
				}
			}
			if ((bool)_soloOpeningBranch)
			{
				if (_soloOpeningBranch.gameObject.activeSelf)
				{
					return _soloOpeningBranch.gameObject.activeInHierarchy;
				}
				return false;
			}
			return true;
		}
	}

	internal static void ResetForRaid()
	{
		CancelDuoHolds();
		_staged = false;
		_root = null;
		_soloOpeningBranch = null;
		Planted = false;
		Opened = false;
		_boomSeen = false;
	}

	internal static bool IsNearOpening(Player player)
	{
		Transform openingAnchor = OpeningAnchor;
		if (player != null && openingAnchor != null)
		{
			return (player.Position - openingAnchor.position).sqrMagnitude <= 25f;
		}
		return false;
	}

	internal static void BeginDuoWait(GamePlayerOwner owner, Transform anchor)
	{
		if (DuoSessionActive)
		{
			return;
		}
		_duoOwner = owner;
		_duoAnchor = anchor ?? OpeningAnchor;
		if (_duoAnchor == null)
		{
			Plugin.Log.LogWarning("[Gates] duo wait requested before the opening anchor was staged");
			return;
		}
		GameObject gameObject = new GameObject("Terminal_GateDuoWait");
		GateHoldSession session = gameObject.AddComponent<GateHoldSession>();
		_duoWaitSession = session;
		session.Owner = owner;
		session.Anchor = _duoAnchor;
		session.Seconds = 100f;
		session.ShowProgress = false;
		session.PanelText = "Waiting for teammate {0:F1}";
		session.OnStart = () =>
		{
			if ((object)_duoWaitSession == session)
			{
				TerminalCoop.Request(TerminalEvent.GateDuoRegister);
			}
		};
		session.OnSuccess = () =>
		{
			if ((object)_duoWaitSession == session)
			{
				ForgetDuoSession(session);
				TerminalCoop.Request(TerminalEvent.GateDuoCancel);
			}
		};
		session.OnCancel = () =>
		{
			if ((object)_duoWaitSession == session)
			{
				ForgetDuoSession(session);
				TerminalCoop.Request(TerminalEvent.GateDuoCancel);
			}
		};
	}

	internal static void BeginDuoOpenHold()
	{
		if (_duoOpenSession != null)
		{
			return;
		}
		GamePlayerOwner duoOwner = _duoOwner;
		Transform transform = _duoAnchor ?? OpeningAnchor;
		CancelDuoHolds();
		if (transform == null)
		{
			Plugin.Log.LogWarning("[Gates] duo ready arrived before the opening anchor was staged");
			TerminalCoop.Request(TerminalEvent.GateDuoCancel);
			return;
		}
		_duoOwner = duoOwner;
		_duoAnchor = transform;
		_duoOpeningBranchSuppressed = true;
		Emit("start_open_gates_duo_3037225082");
		GameObject gameObject = new GameObject("Terminal_GateDuoOpen");
		GateHoldSession session = gameObject.AddComponent<GateHoldSession>();
		_duoOpenSession = session;
		session.Owner = duoOwner;
		session.Anchor = transform;
		session.Seconds = 5f;
		session.PanelText = "Opening gate {0:F1}";
		session.FoleyClip = "amb_terminal_interactive_gates_02_handle_open";
		session.OnSuccess = () =>
		{
			if ((object)_duoOpenSession == session)
			{
				ForgetDuoSession(session);
				TerminalCoop.Request(TerminalEvent.GateDuoFinish);
			}
		};
		session.OnCancel = () =>
		{
			if ((object)_duoOpenSession == session)
			{
				ForgetDuoSession(session);
				ClearDuoOpeningBranch(restoreOpening: true);
				TerminalCoop.Request(TerminalEvent.GateDuoCancel);
			}
		};
	}

	internal static void CancelDuoHolds(bool restoreOpening = false)
	{
		GateHoldSession duoWaitSession = _duoWaitSession;
		GateHoldSession duoOpenSession = _duoOpenSession;
		_duoWaitSession = null;
		_duoOpenSession = null;
		_duoOwner = null;
		_duoAnchor = null;
		if (duoWaitSession != null)
		{
			duoWaitSession.CancelSilently();
		}
		if (duoOpenSession != null)
		{
			duoOpenSession.CancelSilently();
		}
		ClearDuoOpeningBranch(restoreOpening);
	}

	private static void ClearDuoOpeningBranch(bool restoreOpening)
	{
		if (_duoOpeningBranchSuppressed)
		{
			_duoOpeningBranchSuppressed = false;
			if (restoreOpening)
			{
				Emit("dropped_opening_gates_duo_3037225082");
			}
		}
	}

	private static void ForgetDuoSession(GateHoldSession session)
	{
		if ((object)_duoWaitSession == session)
		{
			_duoWaitSession = null;
		}
		if ((object)_duoOpenSession == session)
		{
			_duoOpenSession = null;
		}
		_duoOwner = null;
		_duoAnchor = null;
	}

	internal static void TryStage()
	{
		if (_staged || !Plugin.GatesExplosion.Value || !TerminalGate.On)
		{
			return;
		}
		GameWorld gameWorld = (Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null);
		if (gameWorld == null || gameWorld.TriggersEmitter == null)
		{
			return;
		}
		Transform transform = FindRoot();
		if (!transform)
		{
			return;
		}
		JObject jObject;
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "plugin-data", "terminal_gates.json");
			if (!File.Exists(path))
			{
				Plugin.Log.LogWarning("[Gates] terminal_gates.json missing — gate stays inert");
				_staged = true;
				return;
			}
			jObject = JObject.Parse(File.ReadAllText(path));
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Gates] sidecar parse failed: " + ex.Message);
			_staged = true;
			return;
		}
		try
		{
			_root = transform;
			Dictionary<string, Transform> dictionary = new Dictionary<string, Transform>();
			TerminalRigFill.IndexTree(transform, "Gates_explosion", dictionary);
			TerminalRigFill.StageRows(jObject["rows"] as JArray, NativeSet, dictionary, "Gates");
			TerminalRigFill.StageDelayShims(jObject["rows"] as JArray, "Gates");
			dictionary.TryGetValue("Gates_explosion/Triggers/OpeningTriggers/StartTriggers/SoloOpeningTrigger", out _soloOpeningBranch);
			if (dictionary.TryGetValue("Gates_explosion/Triggers/ExpoldeTriggers/item_spec_sz_1", out var value))
			{
				value.gameObject.SetActive(value: false);
			}
			string[] array = new string[2] { "Gates_explosion/Triggers/ExpoldeTriggers/Switch_bomb", "Gates_explosion/Triggers/OpeningTriggers/Switch/SwitchBoard" };
			foreach (string text in array)
			{
				if (!dictionary.TryGetValue(text, out var value2))
				{
					Plugin.Log.LogWarning("[Gates] '" + text + "' GO not found — its prompt cannot appear");
					continue;
				}
				Transform transform2 = value2;
				while (transform2 != null && transform2 != transform.parent)
				{
					if (!transform2.gameObject.activeSelf)
					{
						transform2.gameObject.SetActive(value: true);
					}
					transform2 = transform2.parent;
				}
				Switch component = value2.GetComponent<Switch>();
				if (!component)
				{
					Plugin.Log.LogWarning("[Gates] no Switch component on '" + text + "' — prompt dead (bundle husk?)");
				}
				else
				{
					component.enabled = true;
				}
			}
			try
			{
				TriggersEmitter.Instance.Subscribe("bomb_explosion_3037225082", OnExplosionVfx);
				TriggersEmitter.Instance.Subscribe("bomb_explosion_3037225082", () =>
				{
					_boomSeen = true;
				});
				TriggersEmitter.Instance.Subscribe("place_bomb_3037225082", () =>
				{
					new GameObject("Terminal_GateFuseWatchdog").AddComponent<FuseWatchdog>();
				});
			}
			catch (Exception ex2)
			{
				Plugin.Log.LogWarning("[Gates] vfx listener subscribe failed: " + ex2.Message);
			}
			_staged = true;
			TerminalRigFill.ScheduleSubscriberDump("Gates", "place_bomb_3037225082", "bomb_explosion_3037225082", "gates_opened_solo_3037225082");
			Plugin.Log.LogInfo("[Gates] GATES_EXPLOSION RESTORED — retail fuse/blast/anim armed, interaction layer ours");
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Gates] staging failed: {arg}");
			_staged = true;
		}
	}

	private static void OnExplosionVfx()
	{
		if (TerminalCoop.Replaying)
		{
			return;
		}
		try
		{
			if (!_root)
			{
				return;
			}
			Vector3 position = _root.position;
			Transform transform = TerminalRigFill.FindChildNamed(_root, "C4_Explosion");
			if (transform != null)
			{
				position = transform.position;
				if (!transform.gameObject.activeSelf)
				{
					transform.gameObject.SetActive(value: true);
				}
				ParticleSystem[] componentsInChildren = transform.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
				foreach (ParticleSystem particleSystem in componentsInChildren)
				{
					if (!particleSystem.isPlaying)
					{
						particleSystem.Play();
					}
				}
			}
			AudioClip audioClip = FindClip("amb_terminal_interactive_c6_detonate");
			if (audioClip != null)
			{
				PlayAt(audioClip, position, 120f);
			}
			else
			{
				PlayAt(FindClip("IB_chain_door_explosion"), position, 120f);
				PlayAt(FindClip("IB_chain_door_explosion_reaction"), position, 60f, 1f);
			}
			Plugin.Log.LogInfo("[Gates] C4_Explosion VFX + boom foley fired ('" + ((audioClip != null) ? audioClip.name : "IB fallback pair") + "')");
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Gates] vfx failed: " + ex.Message);
		}
	}

	private static Transform FindRoot()
	{
		return TerminalRigFill.FindRootNamed("Gates_explosion");
	}

	internal static void Emit(string trigger)
	{
		try
		{
			TerminalWorldDiff.RecordEvent("map trigger", trigger);
			Player player = Singleton<GameWorld>.Instance?.MainPlayer;
			TriggersEmitter.Instance.Emit(trigger, (player != null) ? player.ProfileId : "");
			Plugin.Log.LogInfo("[Gates] emitted '" + trigger + "'");
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Gates] emit '{trigger}' failed: {arg}");
		}
	}

	internal static async Task<bool> ConsumeChargeCoop(Player player)
	{
		Item item = FindCharge(player);
		if (item == null)
		{
			return false;
		}
		if (IsCharge(player.HandsController?.Item))
		{
			TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
			player.SetEmptyHands((Result<IEmptyHandsController> r) =>
			{
				done.TrySetResult(result: true);
			});
			if (await Task.WhenAny(done.Task, Task.Delay(30000)) != done.Task)
			{
				return false;
			}
			if (IsCharge(player.HandsController?.Item))
			{
				return false;
			}
		}
		OperationResult<UnbindResult> operationResult = player.InventoryController.UnbindItemDirect(item, simulate: true);
		if (!operationResult.Failed)
		{
			IResult result = await player.InventoryController.TryRunNetworkTransaction(operationResult);
			if (result == null || result.Failed)
			{
				return false;
			}
		}
		OperationResult<RemoveResult> operationResult2 = ItemManipulator.Remove(item, player.InventoryController, simulate: true);
		if (operationResult2.Failed)
		{
			return false;
		}
		IResult result2 = await player.InventoryController.TryRunNetworkTransaction(operationResult2);
		return result2 != null && !result2.Failed;
	}

	internal static bool IsCharge(Item it)
	{
		if (it == null)
		{
			return false;
		}
		string[] chargeTpls = ChargeTpls;
		foreach (string text in chargeTpls)
		{
			if (it.TemplateId == (MongoID)text)
			{
				return true;
			}
		}
		return false;
	}

	internal static Item FindCharge(Player player)
	{
		if (player?.Profile?.Inventory == null)
		{
			return null;
		}
		foreach (Item allRealPlayerItem in player.Profile.Inventory.AllRealPlayerItems)
		{
			if (IsCharge(allRealPlayerItem))
			{
				return allRealPlayerItem;
			}
		}
		return null;
	}

	private static void DispatchRemove(Player player, Item item)
	{
		try
		{
			OperationResult<UnbindResult> operationResult = player.InventoryController.UnbindItemDirect(item, simulate: true);
			if (!operationResult.Failed)
			{
				player.InventoryController.TryRunNetworkTransaction(operationResult, (IResult r) =>
				{
					if (!r.Succeed)
					{
						Plugin.Log.LogWarning("[Gates] charge unbind failed post-validation: " + r.Error);
					}
				});
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Gates] unbind attempt threw (continuing): " + ex.Message);
		}
		OperationResult<RemoveResult> operationResult2 = ItemManipulator.Remove(item, player.InventoryController, simulate: true);
		if (operationResult2.Failed)
		{
			Plugin.Log.LogWarning($"[Gates] charge remove validation failed: {operationResult2.Error}");
			return;
		}
		player.InventoryController.TryRunNetworkTransaction(operationResult2, (IResult r) =>
		{
			if (!r.Succeed)
			{
				Plugin.Log.LogWarning("[Gates] charge remove failed post-validation: " + r.Error);
			}
		});
		Plugin.Log.LogInfo("[Gates] consumed charge '" + item.Name.Localized() + "'");
	}

	internal static bool ConsumeCharge(Player player)
	{
		Item item = FindCharge(player);
		if (item == null)
		{
			return false;
		}
		Item item2 = ((player.HandsController != null) ? player.HandsController.Item : null);
		if (item2 == null || (item2 != item && !(item2.Id == item.Id) && !IsCharge(item2)))
		{
			DispatchRemove(player, item);
			return true;
		}
		Plugin.Log.LogInfo("[Gates] charge IN HANDS — swapping to a weapon before consuming");
		player.SetFirstAvailableItem((Result<IHandsController> r) =>
		{
			try
			{
				if (player.HandsController != null && IsCharge(player.HandsController.Item))
				{
					Plugin.Log.LogWarning("[Gates] hands still hold a charge after swap — NOT consumed");
				}
				else
				{
					DispatchRemove(player, item);
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[Gates] post-swap consume failed: " + ex.Message);
			}
		});
		return true;
	}

	internal static AudioClip FindClip(string name)
	{
		return TerminalFxBundle.FindClip(name);
	}

	internal static void PlayAt(AudioClip clip, Vector3 pos, float maxDist, float delay = 0f)
	{
		if (!(clip == null))
		{
			GameObject gameObject = new GameObject("Terminal_GatesSnd");
			gameObject.transform.position = pos;
			AudioSource audioSource = gameObject.AddComponent<AudioSource>();
			audioSource.clip = clip;
			audioSource.spatialBlend = 1f;
			audioSource.maxDistance = maxDist;
			audioSource.rolloffMode = AudioRolloffMode.Linear;
			TerminalAudioRouting.Route(audioSource, effects: true);
			if (delay > 0f)
			{
				audioSource.PlayDelayed(delay);
			}
			else
			{
				audioSource.Play();
			}
			UnityEngine.Object.Destroy(gameObject, clip.length + delay + 0.5f);
		}
	}

	internal static void PlayFlat(AudioClip clip, float volume)
	{
		if (!(clip == null))
		{
			GameObject gameObject = new GameObject("Terminal_FlatSnd");
			AudioSource audioSource = gameObject.AddComponent<AudioSource>();
			audioSource.clip = clip;
			audioSource.spatialBlend = 0f;
			audioSource.volume = volume;
			TerminalAudioRouting.Route(audioSource, effects: true);
			audioSource.Play();
			UnityEngine.Object.Destroy(gameObject, clip.length + 0.5f);
		}
	}

	internal static Vector3 RigPos()
	{
		if (!_root)
		{
			return Vector3.zero;
		}
		return _root.position;
	}
}
