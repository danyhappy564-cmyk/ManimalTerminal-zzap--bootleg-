namespace Manimal.Terminal;

internal enum TerminalEvent : byte
{
	PumpLayout = 1,
	PumpRepair,
	PumpDrain,
	GateReserve,
	GatePlant,
	GateOpen,
	Trigger,
	GearCabinet,
	GearDone,
	IntroStart,
	IntroDone,
	AttackStart,
	AttackDone,
	Evac,
	Door,
	Wave,
	GateCancel,
	GearPrepared,
	GateDuoRegister,
	GateDuoCancel,
	GateDuoFinish,
	GateDuoReady,
	GateDuoFinal
}
