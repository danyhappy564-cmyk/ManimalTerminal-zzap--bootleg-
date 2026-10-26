using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT.InputSystem;
using EFT.Settings;
using UnityEngine;

namespace Manimal.Terminal;

internal static class TerminalInteractKey
{
	private static readonly List<KeyCode> _keys = new List<KeyCode>();

	private static float _nextRefresh;

	private static bool _loggedOnce;

	internal static bool Held()
	{
		Refresh();
		for (int i = 0; i < _keys.Count; i++)
		{
			if (Input.GetKey(_keys[i]))
			{
				return true;
			}
		}
		return false;
	}

	internal static string Name()
	{
		Refresh();
		if (_keys.Count <= 0)
		{
			return "F";
		}
		return _keys[0].ToString();
	}

	private static void Refresh()
	{
		if (Time.unscaledTime < _nextRefresh && _keys.Count > 0)
		{
			return;
		}
		_nextRefresh = Time.unscaledTime + 5f;
		try
		{
			List<KeyGroup> list = Singleton<SettingsManager>.Instance?.Control?.Settings?.UserKeyBindings?.Value;
			if (list != null)
			{
				foreach (KeyGroup item in list)
				{
					if (item == null || item.keyName != EGameKey.Interact || item.variants == null)
					{
						continue;
					}
					_keys.Clear();
					foreach (InputSource variant in item.variants)
					{
						if (variant?.keyCode == null)
						{
							continue;
						}
						foreach (KeyCode item2 in variant.keyCode)
						{
							if (item2 != KeyCode.None && !_keys.Contains(item2))
							{
								_keys.Add(item2);
							}
						}
					}
					break;
				}
			}
		}
		catch (Exception ex)
		{
			if (!_loggedOnce)
			{
				_loggedOnce = true;
				Plugin.Log.LogWarning("[InteractKey] lookup failed, falling back to F: " + ex.Message);
			}
		}
		if (_keys.Count == 0)
		{
			_keys.Add(KeyCode.F);
		}
	}
}
