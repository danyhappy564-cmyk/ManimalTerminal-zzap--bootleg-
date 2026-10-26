using UnityEngine;

namespace Manimal.Terminal;

internal class FuseWatchdog : MonoBehaviour
{
	private float _armed;

	private void Start()
	{
		_armed = Time.time;
	}

	private void Update()
	{
		if (TerminalCoop.Active && !TerminalCoop.Authority)
		{
			Object.Destroy(gameObject);
		}
		else if (TerminalGatesExplosion._boomSeen)
		{
			Object.Destroy(gameObject);
		}
		else if (!(Time.time - _armed < 10.5f))
		{
			Plugin.Log.LogWarning("[Gates] native fuse silent 10.5s after the plant — watchdog detonating");
			TerminalGatesExplosion.Emit("bomb_explosion_3037225082");
			Object.Destroy(gameObject);
		}
	}
}
