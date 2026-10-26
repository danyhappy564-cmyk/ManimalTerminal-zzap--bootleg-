using System;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace Manimal.Terminal;

internal class GateHoldSession : MonoBehaviour
{
	public GamePlayerOwner Owner;

	public Transform Anchor;

	public float Seconds = 5f;

	public string PanelText = "Planting charge {0:F1}";

	public string FoleyClip;

	public bool ShowProgress = true;

	public Action OnStart;

	public Action OnSuccess;

	public Action OnCancel;

	private float _start;

	private bool _started;

	private bool _ending;

	private bool _handsLocked;

	private bool _panelShown;

	private AudioSource _snd;

	private void Update()
	{
		try
		{
			if (_ending)
			{
				return;
			}
			Player player = Singleton<GameWorld>.Instance?.MainPlayer;
			if (player == null || Anchor == null || player.HealthController == null || !player.HealthController.IsAlive)
			{
				End(success: false);
				return;
			}
			if (!_started)
			{
				_started = true;
				_start = Time.time;
				if (OnStart != null)
				{
					OnStart();
				}
				if (_ending)
				{
					return;
				}
				if (ShowProgress && (UnityEngine.Object)(object)Owner != null)
				{
					Owner.ShowObjectivesPanel(PanelText, Seconds);
					_panelShown = true;
				}
				MovementContext movementContext = player.MovementContext;
				if (movementContext != null)
				{
					movementContext.BlockFirearms = true;
					_handsLocked = true;
					TerminalHoldLock.Acquire();
				}
				transform.position = Anchor.position;
				AudioClip audioClip = (string.IsNullOrEmpty(FoleyClip) ? null : TerminalGatesExplosion.FindClip(FoleyClip));
				if (audioClip != null)
				{
					_snd = gameObject.AddComponent<AudioSource>();
					_snd.clip = audioClip;
					_snd.spatialBlend = 1f;
					_snd.maxDistance = 25f;
					_snd.rolloffMode = AudioRolloffMode.Linear;
					TerminalAudioRouting.Route(_snd, effects: true);
					_snd.Play();
				}
			}
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				End(success: false);
			}
			else if ((player.Position - Anchor.position).sqrMagnitude > 25f)
			{
				End(success: false);
			}
			else if (!TerminalInteractKey.Held())
			{
				End(success: false);
			}
			else if (Time.time - _start >= Seconds)
			{
				End(success: true);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning("[Gates] hold session threw: " + ex.Message);
			End(success: false);
		}
	}

	private void End(bool success)
	{
		if (_ending)
		{
			return;
		}
		_ending = true;
		if (_snd != null)
		{
			_snd.Stop();
		}
		ReleaseResources();
		try
		{
			if (success)
			{
				if (OnSuccess != null)
				{
					OnSuccess();
				}
			}
			else if (OnCancel != null)
			{
				OnCancel();
			}
		}
		catch (Exception arg)
		{
			Plugin.Log.LogWarning($"[Gates] hold completion threw: {arg}");
		}
		finally
		{
			UnityEngine.Object.Destroy(gameObject);
		}
	}

	internal void CancelSilently()
	{
		if (!_ending)
		{
			_ending = true;
			if (_snd != null)
			{
				_snd.Stop();
			}
			ReleaseResources();
			UnityEngine.Object.Destroy(gameObject);
		}
	}

	private void OnDestroy()
	{
		ReleaseResources();
	}

	private void ReleaseResources()
	{
		if (_handsLocked)
		{
			MovementContext movementContext = Singleton<GameWorld>.Instance?.MainPlayer?.MovementContext;
			if (movementContext != null)
			{
				movementContext.BlockFirearms = false;
			}
			TerminalHoldLock.Release();
			_handsLocked = false;
		}
		if (_panelShown && (UnityEngine.Object)(object)Owner != null)
		{
			Owner.CloseObjectivesPanel();
		}
		_panelShown = false;
	}
}
