using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;
using ZLinq;
using ZLinq.Linq;

namespace Manimal.Terminal;

internal static class TerminalBlackDivisionPlaces
{
	internal sealed class Assignment
	{
		internal AIPlaceInfo Place;

		internal int AreaId;

		internal int ConnectionGroupId;

		internal bool TryGetAuthoredBounds(out Bounds bounds)
		{
			BoxCollider boxCollider = ((Place != null) ? Place.Collider : null);
			if (!boxCollider && Place != null)
			{
				boxCollider = Place.GetComponent<BoxCollider>();
			}
			if ((bool)boxCollider)
			{
				bounds = boxCollider.bounds;
				return true;
			}
			bounds = default;
			return false;
		}
	}

	private static readonly List<TerminalBlackDivisionLogic> _ordered = new List<TerminalBlackDivisionLogic>();

	private static AIPlaceInfoHolder _holder;

	internal static int RegisteredCount => _ordered.Count;

	internal static void ResetForNewRaid()
	{
		foreach (TerminalBlackDivisionLogic item in _ordered)
		{
			if ((bool)item)
			{
				item.ResetOwner();
			}
		}
		_ordered.Clear();
		_holder = null;
	}

	internal static void SetHolder(AIPlaceInfoHolder holder)
	{
		_holder = holder;
	}

	internal static TerminalBlackDivisionLogic Bind(AIPlaceInfo place, int connectionGroupId, IEnumerable<string> possibleZoneNames)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		if (place == null || connectionGroupId <= 0)
		{
			return null;
		}
		string[] possibleZoneNames2 = ValueEnumerableExtensions.ToArray<Distinct<Where<FromEnumerable<string>, string>, string>, string>(ValueEnumerableExtensions.Distinct<Where<FromEnumerable<string>, string>, string>(ValueEnumerableExtensions.Where<FromEnumerable<string>, string>(ValueEnumerable.AsValueEnumerable<string>(possibleZoneNames ?? Array.Empty<string>()), (Func<string, bool>)((string name) => !string.IsNullOrWhiteSpace(name)))));
		TerminalBlackDivisionLogic terminalBlackDivisionLogic = place.GetComponent<TerminalBlackDivisionLogic>() ?? place.gameObject.AddComponent<TerminalBlackDivisionLogic>();
		terminalBlackDivisionLogic.Configure(place, connectionGroupId, possibleZoneNames2);
		if (!_ordered.Contains(terminalBlackDivisionLogic))
		{
			_ordered.Add(terminalBlackDivisionLogic);
		}
		return terminalBlackDivisionLogic;
	}

	internal static bool TryAcquire(BotOwner owner, out Assignment assignment)
	{
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		assignment = null;
		if (!owner)
		{
			return false;
		}
		TerminalBlackDivisionLogic terminalBlackDivisionLogic = ((_holder?.Places == null) ? ValueEnumerableExtensions.FirstOrDefault<FromList<TerminalBlackDivisionLogic>, TerminalBlackDivisionLogic>(ValueEnumerable.AsValueEnumerable<TerminalBlackDivisionLogic>(_ordered), (Func<TerminalBlackDivisionLogic, bool>)((TerminalBlackDivisionLogic candidate) => (bool)candidate && candidate.CanUse(owner))) : ValueEnumerableExtensions.FirstOrDefault<ListWhereSelect<AIPlaceInfo, TerminalBlackDivisionLogic>, TerminalBlackDivisionLogic>(ValueEnumerableExtensions.Select<AIPlaceInfo, TerminalBlackDivisionLogic>(ValueEnumerableExtensions.Where<AIPlaceInfo>(ValueEnumerable.AsValueEnumerable<AIPlaceInfo>(_holder.Places), (Func<AIPlaceInfo, bool>)((AIPlaceInfo aIPlaceInfo) => aIPlaceInfo)), (Func<AIPlaceInfo, TerminalBlackDivisionLogic>)((AIPlaceInfo aIPlaceInfo) => aIPlaceInfo.InfoLogicAllEnemy as TerminalBlackDivisionLogic)), (Func<TerminalBlackDivisionLogic, bool>)((TerminalBlackDivisionLogic candidate) => (bool)candidate && candidate.CanUse(owner))));
		if (!terminalBlackDivisionLogic || !terminalBlackDivisionLogic.TryClaim(owner))
		{
			return false;
		}
		AIPlaceInfo place = terminalBlackDivisionLogic.Place;
		assignment = new Assignment
		{
			Place = place,
			AreaId = terminalBlackDivisionLogic.CoverAreaId,
			ConnectionGroupId = terminalBlackDivisionLogic.ConnectionGroupId
		};
		Plugin.Log.LogDebug($"[AIPlaces] Black Division claimed AreaId={assignment.AreaId} " + $"connectionGroup={assignment.ConnectionGroupId} owner={owner.Id} " + "place='" + place.name + "'");
		return true;
	}

	internal static BotZone[] ResolveZones(string[] names)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		if (names == null || names.Length == 0)
		{
			return Array.Empty<BotZone>();
		}
		return ValueEnumerableExtensions.ToArray<BotZone>(ValueEnumerableExtensions.Where<BotZone>(ValueEnumerable.AsValueEnumerable<BotZone>(UnityEngine.Object.FindObjectsOfType<BotZone>()), (Func<BotZone, bool>)((BotZone zone) =>
		{
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			return (bool)zone && ValueEnumerableExtensions.Any<string>(ValueEnumerable.AsValueEnumerable<string>(names), (Func<string, bool>)((string name) => string.Equals(zone.name, name, StringComparison.Ordinal)));
		})));
	}
}
