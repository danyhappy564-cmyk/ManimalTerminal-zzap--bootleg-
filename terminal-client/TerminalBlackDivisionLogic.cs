using System;
using EFT;
using ZLinq;

namespace Manimal.Terminal;

internal sealed class TerminalBlackDivisionLogic : AIPlaceInfoLogic
{
	private AIPlaceInfo _place;

	private string[] _possibleZoneNames = Array.Empty<string>();

	private BotZone[] _possibleZones = Array.Empty<BotZone>();

	private int _ownerId;

	private bool _checkZones;

	internal AIPlaceInfo Place => _place;

	internal int ConnectionGroupId { get; private set; }

	internal int CoverAreaId => TerminalBlackDivisionPolicy.CoverAreaId(_place?.AreaId ?? 0);

	internal bool HaveOnwer => TerminalBlackDivisionPolicy.HaveOwner(_ownerId);

	internal string[] PossibleZoneNames => _possibleZoneNames;

	internal void Configure(AIPlaceInfo place, int connectionGroupId, string[] possibleZoneNames)
	{
		_place = place;
		ConnectionGroupId = connectionGroupId;
		_possibleZoneNames = possibleZoneNames ?? Array.Empty<string>();
		_checkZones = _possibleZoneNames.Length != 0;
		_possibleZones = TerminalBlackDivisionPlaces.ResolveZones(_possibleZoneNames);
	}

	public override void Init(AIPlaceInfo aiPlaceInfo, BotsController botsController)
	{
		_place = aiPlaceInfo ?? _place;
		if (_possibleZoneNames.Length != 0)
		{
			_possibleZones = TerminalBlackDivisionPlaces.ResolveZones(_possibleZoneNames);
		}
		base.Init(aiPlaceInfo, botsController);
	}

	internal bool CanUse(BotOwner owner)
	{
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		if (!owner || owner.Covers == null || _place == null)
		{
			return false;
		}
		try
		{
			BotZone ownerZone = owner.SpawnBotZone;
			bool hasMatchingZone = !_checkZones || (_possibleZones != null && ValueEnumerableExtensions.Any<BotZone>(ValueEnumerable.AsValueEnumerable<BotZone>(_possibleZones), (Func<BotZone, bool>)((BotZone zone) => (bool)zone && zone == ownerZone)));
			return TerminalBlackDivisionPolicy.CanUse(_ownerId, ConnectionGroupId, owner.Id, owner.Covers.ConnectionGroupId, _checkZones, hasMatchingZone);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogDebug("[AIPlaces] Black Division CanUse deferred: " + ex.GetType().Name);
			return false;
		}
	}

	internal bool TryClaim(BotOwner owner)
	{
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		if (!owner || owner.Covers == null || _place == null)
		{
			return false;
		}
		try
		{
			BotZone ownerZone = owner.SpawnBotZone;
			bool hasMatchingZone = !_checkZones || (_possibleZones != null && ValueEnumerableExtensions.Any<BotZone>(ValueEnumerable.AsValueEnumerable<BotZone>(_possibleZones), (Func<BotZone, bool>)((BotZone zone) => (bool)zone && zone == ownerZone)));
			return TerminalBlackDivisionPolicy.TryClaim(ref _ownerId, ConnectionGroupId, owner.Id, owner.Covers.ConnectionGroupId, _checkZones, hasMatchingZone);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogDebug("[AIPlaces] Black Division claim deferred: " + ex.GetType().Name);
			return false;
		}
	}

	internal void ResetOwner()
	{
		_ownerId = 0;
	}

	public override void Dispose()
	{
		base.Dispose();
	}
}
