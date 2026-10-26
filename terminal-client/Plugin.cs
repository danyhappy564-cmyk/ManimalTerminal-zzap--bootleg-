using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Manimal.Terminal.Civilian.Patches;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Manimal.Terminal;

[BepInPlugin("com.manimal.terminal", "Manimal-Terminal", "0.2.20")]
[BepInDependency("com.arys.unitytoolkit", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.wtt.commonlib", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.wtt.contentbackport", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.morebotsapi.tacticaltoaster", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.blackdiv.tacticaltoaster", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.manimal.motionmagic", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
	internal static ManualLogSource Log;

	internal static Harmony HarmonyInstance;

	internal static ConfigEntry<float> LampIntensity;

	internal static ConfigEntry<bool> LampAuthored;

	internal static ConfigEntry<float> LampAuthoredScale;

	internal static ConfigEntry<float> AmbientIntensity;

	internal static ConfigEntry<bool> AmbientColorOverride;

	internal static ConfigEntry<float> AmbientColorR;

	internal static ConfigEntry<float> AmbientColorG;

	internal static ConfigEntry<float> AmbientColorB;

	internal static ConfigEntry<bool> LampShadows;

	internal static ConfigEntry<float> LightCullDistance;

	internal static ConfigEntry<bool> GrassEnabled;

	internal static ConfigEntry<float> LodBiasClamp;

	internal static ConfigEntry<float> LodCullFloor;

	internal static ConfigEntry<float> LodCullNearFloor;

	internal static ConfigEntry<float> LodCullNearRadius;

	internal static ConfigEntry<float> LodCullNearRadiusIndoor;

	internal static ConfigEntry<float> LodCellSize;

	internal static ConfigEntry<float> LootCullRadius;

	internal static ConfigEntry<string> CamDonorSkip;

	internal static ConfigEntry<bool> DevMode;

	internal static ConfigEntry<bool> LensFlares;

	internal static ConfigEntry<bool> CutscenePlayerTop;

	internal static ConfigEntry<bool> AttackCutscene;

	internal static ConfigEntry<float> AttackCutsceneDelay;

	internal static ConfigEntry<bool> AttackCutsceneSkippable;

	internal static ConfigEntry<bool> PcDriverEnabled;

	internal static ConfigEntry<float> RaidStartHour;

	internal static ConfigEntry<bool> RetailAIBake;

	internal static ConfigEntry<bool> HoldBotsForCutscene;

	internal static ConfigEntry<bool> EventWavesPush;

	internal static ConfigEntry<bool> RuafNeutral;

	internal static ConfigEntry<bool> RuafDefense;

	internal static ConfigEntry<bool> StageDirector;

	// zzap: terminal-only squad dormancy, opt-in until raid validation.
	internal static ConfigEntry<bool> GhostMode;
	internal static ConfigEntry<float> GhostSleepDistance;
	internal static ConfigEntry<float> GhostWakeDistance;
	internal static ConfigEntry<bool> GhostBrainThrottle;
	internal static ConfigEntry<bool> GhostSainThrottle;
	internal static ConfigEntry<bool> GhostDiagnostics;
	internal static ConfigEntry<bool> GhostCombat;
	internal static ConfigEntry<float> GhostCombatRange;
	internal static ConfigEntry<bool> GhostAudio;
	internal static ConfigEntry<float> GhostAudioRange;
	internal static ConfigEntry<int> GhostAudioMaxFights;

	internal static ConfigEntry<int> MaxAliveScavs;

	internal static ConfigEntry<int> MaxAliveBots;

	internal static ConfigEntry<int> MaxResidentScavs;

	internal static ConfigEntry<float> FreshProfileWaveInterval;

	internal static ConfigEntry<float> LowPopulationWaveInterval;

	internal static ConfigEntry<int> LowPopulationWaveThreshold;

	internal static ConfigEntry<int> MaxBotsCreatedPerRaid;

	internal static ConfigEntry<bool> DisableAllBots;

	internal static ConfigEntry<bool> ScavCorpseCleanup;

	internal static ConfigEntry<float> ScavCorpseLifetime;

	internal static ConfigEntry<float> ScavCorpseCleanupDistance;

	internal static ConfigEntry<float> SpecialCorpseLifetime;

	internal static ConfigEntry<float> SpecialCorpseCleanupDistance;

	internal static ConfigEntry<int> ScavCorpseCleanupPerSweep;

	internal static ConfigEntry<KeyboardShortcut> ScavCorpseCleanupKey;

	internal static ConfigEntry<bool> ScavRecycler;

	internal static ConfigEntry<bool> RuafRecycler;

	internal static ConfigEntry<bool> BlackDivisionRecycler;

	internal static ConfigEntry<float> ScavRecycleMinDistance;

	internal static ConfigEntry<float> ScavRecycleDestinationDistance;

	internal static ConfigEntry<float> ScavRecycleMinAge;

	internal static ConfigEntry<bool> TerminalBosses;

	internal static ConfigEntry<int> BreachableDoors;

	internal static ConfigEntry<bool> BreachDoorProbe;

	internal static ConfigEntry<bool> ProfilePlayerLoop;

	internal static ConfigEntry<bool> TraceFrameCycle;

	internal static ConfigEntry<bool> WorldDiff;

	internal static ConfigEntry<bool> SceneCensus;

	internal static ConfigEntry<bool> WeaponLightShadows;

	internal static ConfigEntry<bool> ShowSpawnTriggers;

	internal static ConfigEntry<float> NvgAmbient;

	internal static ConfigEntry<bool> GearConfiscation;

	internal static ConfigEntry<bool> SpatialAudio;

	internal static ConfigEntry<bool> AmbientRetail;

	internal static ConfigEntry<float> EnvironmentExposure;

	internal static ConfigEntry<bool> BallisticGlassPen;

	internal static ConfigEntry<float> GateAmbushTime;

	internal static ConfigEntry<float> SkyHourOffset;

	internal static ConfigEntry<float> SkyNightStrength;

	internal static ConfigEntry<bool> WeatherStack;

	internal static ConfigEntry<bool> RetailMissionWeather;

	internal static ConfigEntry<bool> GatesExplosion;

	internal static ConfigEntry<bool> CraneFalling;

	internal static ConfigEntry<bool> FinalExit;

	internal static ConfigEntry<bool> Artillery;

	internal static ConfigEntry<bool> PumpStation;

	internal static ConfigEntry<bool> EndingCutscene;

	internal static ConfigEntry<bool> Epilogue;

	internal static ConfigEntry<bool> EpilogueTestMode;

	internal static ConfigEntry<bool> IntroCutscene;

	internal static ConfigEntry<bool> IntroCutsceneSkippable;

	internal static ConfigEntry<bool> ForceWeather;

	internal static ConfigEntry<float> WeatherStartHoldSeconds;

	internal static ConfigEntry<float> WeatherRain;

	internal static ConfigEntry<float> WeatherClouds;

	internal static ConfigEntry<float> WeatherFog;

	internal static ConfigEntry<float> WeatherWind;

	internal static ConfigEntry<float> WeatherThunder;

	internal static ConfigEntry<float> DoorFoleyVolume;

	internal static ConfigEntry<KeyboardShortcut> SoundProbeKey;

	internal static ConfigEntry<KeyboardShortcut> LightProbeKey;

	internal static ConfigEntry<KeyboardShortcut> InteractProbeKey;

	internal static ConfigEntry<bool> SoundRig;

	internal static ConfigEntry<float> SoundRigVolume;

	internal static ConfigEntry<bool> SoundRigAlarm;

	internal static ConfigEntry<bool> WaterPlanes;

	internal static ConfigEntry<bool> CutsceneSubtitles;

	internal static ConfigEntry<string> Language;

	internal static ConfigEntry<string> KoreanFont;

	internal static ConfigEntry<bool> CivilianJumpProbe;

	internal static ConfigEntry<float> SubtitleSize;

	internal static ConfigEntry<bool> AmbientSplines;

	internal static ConfigEntry<bool> SoundRigFirefight;

	internal static ConfigEntry<bool> InteriorCrossCull;

	internal static ConfigEntry<float> CrossCullDistance;

	private static readonly (string Name, Action Run)[] Stagers = new (string, Action)[9]
	{
		("GatesExpl", TerminalGatesExplosion.TryStage),
		("Crane", TerminalCraneFalling.TryStage),
		("FinalExit", TerminalFinalExit.TryStage),
		("ArtyStg", TerminalArtillery.TryStage),
		("PumpStg", TerminalPumpStation.TryStage),
		("Water", TerminalWater.TryStage),
		("DryPln", TerminalDryPlanes.TryStage),
		("ShoreAud", TerminalShoreAudio.TryStage),
		("RainAud", TerminalRainAudio.TryStage)
	};

	private static int _stagerCursor;

	private void Update()
	{
		if (TerminalGate.On)
		{
			TerminalPerfWatch.OursBegin();
			// zzap perf: the stagers all became ready in the same frame and ran back to
			// back (7s of the 17.8s raid-start frame on 10/26). Each one retries every
			// frame until it has staged, so once this frame has spent its budget the rest
			// simply go next frame — round-robin so nobody starves.
			long frameStart = Stopwatch.GetTimestamp();
			long budget = Stopwatch.Frequency * 8 / 1000;
			for (int k = 0; k < Stagers.Length; k++)
			{
				int idx = (_stagerCursor + k) % Stagers.Length;
				long t0 = Stopwatch.GetTimestamp();
				Stagers[idx].Run();
				long now = Stopwatch.GetTimestamp();
				TerminalTickProfiler.Add(Stagers[idx].Name, now - t0);
				if (now - frameStart > budget)
				{
					_stagerCursor = (idx + 1) % Stagers.Length;
					break;
				}
			}
			long timestamp = Stopwatch.GetTimestamp();
			TerminalWeather.TryEnsureRainVisualReady();
			TerminalTickProfiler.Add("RainVis", Stopwatch.GetTimestamp() - timestamp);
			TerminalMissionWeather.Pump();
			timestamp = Stopwatch.GetTimestamp();
			TerminalArtillery.Pump();
			TerminalTickProfiler.Add("ArtyPump", Stopwatch.GetTimestamp() - timestamp);
			timestamp = Stopwatch.GetTimestamp();
			TerminalShadowGuard.Tick();
			TerminalTickProfiler.Add("ShadowGd", Stopwatch.GetTimestamp() - timestamp);
			TerminalPerfWatch.OursEnd();
			TerminalPerfWatch.Tick();
			timestamp = Stopwatch.GetTimestamp();
			TerminalSceneScrub.TickLateSweep();
			TerminalTickProfiler.Add("Scrub", Stopwatch.GetTimestamp() - timestamp);
			timestamp = Stopwatch.GetTimestamp();
			TerminalAcoustics.TickDiagnostics();
			TerminalTickProfiler.Add("AcousDiag", Stopwatch.GetTimestamp() - timestamp);
		}
	}

	private void Awake()
	{
		Log = Logger;
		HarmonyInstance = new Harmony("com.manimal.terminal");
		GhostMode = Config.Bind("Terminal", "GhostMode", false, "Pause distant ordinary squads (Terminal only; disabled with any Fika assembly). Experimental Ghost mode.");
		GhostSleepDistance = Config.Bind("Terminal", "GhostSleepDistance", 250f, new ConfigDescription("All living squad members must be farther than this from every living human (meters).", new AcceptableValueRange<float>(50f, 1500f)));
		GhostWakeDistance = Config.Bind("Terminal", "GhostWakeDistance", 200f, new ConfigDescription("Immediate squad wake distance (meters); clamped below sleep distance at runtime.", new AcceptableValueRange<float>(10f, 1499f)));
		GhostBrainThrottle = Config.Bind("Terminal", "GhostBrainThrottle", true, "Stage 1 uses a full brain stop while dormant. OFF wakes Ghost-owned bots and suspends new sleeps for safety.");
		GhostSainThrottle = Config.Bind("Terminal", "GhostSainThrottle", true, "Skip dormant bots' optional SAIN updates, vision targets and hearing. Requires GhostMode. Missing SAIN hooks are skipped individually; switching OFF preserves wake cleanup for already throttled bots.");
		GhostDiagnostics = Config.Bind("Terminal", "GhostDiagnostics", true, "Log Ghost counts and timings every 30 seconds, including selected SAIN main-thread entry points and skipped calls. SAIN timing also works with GhostMode OFF for comparison; it excludes worker jobs and unmeasured coroutines.");
		GhostCombat = Config.Bind("Terminal", "GhostCombat", true, "Simulate fights between hostile Ghost-owned dormant squads. Requires GhostMode; OFF cancels remaining simulated damage immediately on the next Ghost update.");
		GhostCombatRange = Config.Bind("Terminal", "GhostCombatRange", 60f, new ConfigDescription("Maximum simulated contact distance in meters (nearest living member pair; three clear world rays required).", new AcceptableValueRange<float>(10f, 150f)));
		GhostAudio = Config.Bind("Terminal", "GhostAudio", true, "Play equipped weapon sounds for simulated fights only while GhostMode and GhostCombat are ON. Sound only; no AI hearing events. OFF discards pending sounds on the next Ghost update.");
		GhostAudioRange = Config.Bind("Terminal", "GhostAudioRange", 700f, new ConfigDescription("Maximum distance from a living human to Ghost gunfire (meters). Outside this range sounds are skipped, with no catch-up.", new AcceptableValueRange<float>(1f, 1500f)));
		GhostAudioMaxFights = Config.Bind("Terminal", "GhostAudioMaxFights", 2, new ConfigDescription("Maximum audible Ghost fights, nearest first. Independent of retail ambience's 12 sequence slots; all Ghost bodies/tails share four source starts per frame.", new AcceptableValueRange<int>(1, TerminalGhostCombat.MaxFights)));
		TerminalGhostPatches.Initialize();
		TerminalGhostSain.Initialize();
		TerminalAILimitFirewall.Install(HarmonyInstance);
		TerminalSainCompat.Install(HarmonyInstance);
		TerminalCrashGuard.TryPatch(HarmonyInstance);
		LampAuthored = Config.Bind("Terminal", "LampAuthored", defaultValue: false, new ConfigDescription("restore each lamp's RETAIL-authored intensity/color/range (7585 extracted — colored indicators, sodium spots, and ~3000 authored-dark broken fixtures that stay dark). off = the old flat LampIntensity for everything", null));
		LampAuthoredScale = Config.Bind("Terminal", "LampAuthoredScale", 1f, new ConfigDescription("multiplier on the authored intensities (1.0 = exactly retail)", new AcceptableValueRange<float>(0f, 4f)));
		LampIntensity = Config.Bind("Terminal", "LampIntensity", 2f, new ConfigDescription("brightness of the revived lamp lights (0 = lights fully OFF — a big GPU win, emissives carry the look)", new AcceptableValueRange<float>(0f, 12f)));
		AmbientIntensity = Config.Bind("Terminal", "AmbientIntensity", 1.8f, new ConfigDescription("flat ambient fill light — lifts shadowed areas out of black (no real bounce without a bake)", new AcceptableValueRange<float>(0f, 3f)));
		AmbientColorOverride = Config.Bind("Terminal", "AmbientColorOverride", defaultValue: true, new ConfigDescription("override the sky-sampled ambient tint with the R/G/B values below. flip live to A/B a colour", null));
		AmbientColorR = Config.Bind("Terminal", "AmbientColorR", 0.1f, new ConfigDescription("ambient red (0-1) when AmbientColorOverride is on", new AcceptableValueRange<float>(0f, 1f)));
		AmbientColorG = Config.Bind("Terminal", "AmbientColorG", 0.13f, new ConfigDescription("ambient green (0-1) when AmbientColorOverride is on", new AcceptableValueRange<float>(0f, 1f)));
		AmbientColorB = Config.Bind("Terminal", "AmbientColorB", 0.2f, new ConfigDescription("ambient blue (0-1) when AmbientColorOverride is on", new AcceptableValueRange<float>(0f, 1f)));
		LampShadows = Config.Bind("Terminal", "LampShadows", defaultValue: false, new ConfigDescription("let the revived lamps cast realtime shadows — much prettier, much heavier", null));
		LodBiasClamp = Config.Bind("Terminal", "LodBiasClamp", 1f, new ConfigDescription("caps unity's global LOD bias (LIVE). EFT's own slider floors at 2.0, which on a ripped map means props render at full detail far past where they matter. lower = more fps and earlier mesh swaps, higher = retail look. -1 = leave the game's value alone. NOTE this also shrinks every LOD CULL distance, which is what the floors below exist to compensate for", new AcceptableValueRange<float>(-1f, 4f)));
		LodCullFloor = Config.Bind("Terminal", "LodCullFloor", 0.05f, new ConfigDescription("FAR-tier cull cap (LIVE): past LodCullNearRadius, props stop rendering below this screen fraction. higher = culls more = more fps but more visible popping at distance. -1 = retail heights", new AcceptableValueRange<float>(-1f, 0.2f)));
		LodCullNearFloor = Config.Bind("Terminal", "LodCullNearFloor", 0.006f, new ConfigDescription("NEAR-tier cull cap (LIVE): inside the near radius, props only vanish below this screen fraction — the anti-dither guarantee for scenery around you. -1 = retail heights near you", new AcceptableValueRange<float>(-1f, 0.05f)));
		LodCullNearRadius = Config.Bind("Terminal", "LodCullNearRadius", 80f, new ConfigDescription("meters around the camera that count as the near tier while OUTDOORS (LIVE). terminal's open sightlines need far more than a ship corridor, so this starts high — lower it for fps, raise it if scenery pops in ahead of you", new AcceptableValueRange<float>(5f, 250f)));
		LodCullNearRadiusIndoor = Config.Bind("Terminal", "LodCullNearRadiusIndoor", 40f, new ConfigDescription("same, but while the camera is INDOORS (LIVE) — interiors have short sightlines, so a tighter bubble lets the far tier eat everything outside the room. drives off retail's EnvironmentManager/IndoorTriggers", new AcceptableValueRange<float>(5f, 250f)));
		LodCellSize = Config.Bind("Terminal", "LodCellSize", 70f, new ConfigDescription("size in meters of the cells the map is bucketed into for tiering. bigger = fewer cells and a cheaper re-tier sweep, but coarser granularity at the radius edge. terminal's harbour wants coarse cells. NEEDS A RAID RESTART — cells are quantized around this at build", new AcceptableValueRange<float>(10f, 200f)));
		LootCullRadius = Config.Bind("Terminal", "LootCullRadius", 40f, new ConfigDescription("meters at which loose LOOT stops rendering (LIVE). a hard cutoff — loot is visible at EVERY range inside it and simply gone outside, no fading. cheaper than hundreds of loot models rendering to subpixel size. 0 = off (loot follows the global LOD bias again)", new AcceptableValueRange<float>(0f, 250f)));
		LightCullDistance = Config.Bind("Terminal", "LightCullDistance", 20f, new ConfigDescription("meters at which lamp lights finish fading to zero (live, lowering only — raising needs a raid restart). tightens bsg's native 50-80m fade window; lower = more fps + darker distance, 80 = authored retail look", new AcceptableValueRange<float>(20f, 80f)));
		GrassEnabled = Config.Bind("Terminal", "GrassEnabled", defaultValue: true, new ConfigDescription("restore Terminal's retail GPU-instanced grass (88,215 placements, 150m authored draw distance). takes effect next raid", null));
		CamDonorSkip = Config.Bind("Terminal", "CamDonorSkip", "", new ConfigDescription("comma-separated component type names the donor graft must skip (bisecting a bad graft component)", null));
		DevMode = Config.Bind("Terminal", "DevMode", defaultValue: false, new ConfigDescription("developer tooling: records the camera donor dump on vanilla raids, and arms the all-camera render profiler, frame-timing capture and [PathPerf] bot path diagnostics (path diagnostics need a game restart). OFF for normal play", null));
		LensFlares = Config.Bind("Terminal", "LensFlares", defaultValue: true, new ConfigDescription("rebuild the 3011 retail per-lamp lens flares (perf A/B lever)", null));
		CutscenePlayerTop = Config.Bind("Terminal", "CutscenePlayerTop", defaultValue: true, new ConfigDescription("the intro cutscene's player actor wears YOUR pmc's equipped top instead of the generic packed torso (mesh + materials swapped onto the actor rig, bones remapped by name; aborts to the generic on any skeleton mismatch)", null));
		AttackCutscene = Config.Bind("Terminal", "AttackCutscene", defaultValue: true, new ConfigDescription("play the timed PortCutscene_01_Attack mid-raid", null));
		AttackCutsceneDelay = Config.Bind("Terminal", "AttackCutsceneDelay", 45f, new ConfigDescription("seconds after the intro cutscene ends before the attack cutscene fires (45 = retail timing, measured off live footage)", new AcceptableValueRange<float>(5f, 600f)));
		AttackCutsceneSkippable = Config.Bind("Terminal", "AttackCutsceneSkippable", defaultValue: true, new ConfigDescription("SPACE skips the attack cutscene (mid-raid; off = retail-faithful unskippable)", null));
		HoldBotsForCutscene = Config.Bind("Terminal", "HoldBotsForCutscene", defaultValue: true, new ConfigDescription("no bots until the attack cutscene has played — the port isnt at war before the attack", null));
		DoorFoleyVolume = Config.Bind("Terminal", "DoorFoleyVolume", 1f, new ConfigDescription("door open/squeak foley volume multiplier (1.0 = authored levels)", new AcceptableValueRange<float>(0f, 1f)));
		GateAmbushTime = Config.Bind("Terminal", "GateAmbushTime", 450f, new ConfigDescription("raid seconds until the Zone1BDGateAmbush13 pair spawns. retail authors 950 (15:50 in) — they beat you to the gate only if you fight slow; -1 keeps the authored timer", new AcceptableValueRange<float>(-1f, 1800f)));
		BallisticGlassPen = Config.Bind("Terminal", "BallisticGlassPen", defaultValue: true, new ConfigDescription("make terminal's armored-glass panes shoot-through for player and AI (their colliders ship with penetration chance 0 = invisible bulletproof walls)", null));
		SkyHourOffset = Config.Bind("Terminal", "SkyHourOffset", 0f, new ConfigDescription("hours added to the SKY's copy of the raid time (raid clock untouched). the TOD solver reads retail's backend time reference, not raw raid time — at 0 offset a 22:00 raid renders the sun ~7 degrees ABOVE the horizon (probe-verified). ~15 degrees of sun drop per hour; raise if you can still see sun glow at night", new AcceptableValueRange<float>(-6f, 6f)));
		SkyNightStrength = Config.Bind("Terminal", "SkyNightStrength", 0f, new ConfigDescription("how hard to pin the sky to NIGHT between 21:00-06:00. the game's own ToDController (which normally darkens the atmosphere as the sun sets) lives on the scrubbed WeatherController, so without this the sky keeps the scene's authored daytime constants. 0 = leave the sky to the authored values", new AcceptableValueRange<float>(0f, 1f)));
		AmbientSplines = Config.Bind("Terminal", "AmbientSplines", defaultValue: false, new ConfigDescription("run the optional ambient spline emitter stack (wind/rain/moving-metal effects). Terminal's lightweight shoreline proximity emitters remain enabled so the sea is audible near the water", null));
		SoundRigFirefight = Config.Bind("Terminal", "SoundRigFirefight", defaultValue: true, new ConfigDescription("distant firefight ambience bursts. turn OFF for one raid as the other half of the frame-chop A/B", null));
		CutsceneSubtitles = Config.Bind("Terminal", "CutsceneSubtitles", defaultValue: true, new ConfigDescription("show timed subtitles during the cutscenes (live-locale text, speech-segmented timings — edit plugin-data/terminal_subtitle_timings.json to tune)", null));
		Language = Config.Bind("Terminal", "Language", "Auto", new ConfigDescription("language of Terminal's own subtitles, notifications, prompts and epilogue. Auto = follow the game's language (Korean text from plugin-data/lang/kr.json) / 터미널 자막·알림·안내·엔딩 화면 언어. Auto = 게임 언어를 따름", new AcceptableValueList<string>("Auto", "Korean", "English")));
		CivilianJumpProbe = Config.Bind("Terminal", "CivilianJumpProbe", defaultValue: true, new ConfigDescription("diagnostic: when a civilian moves implausibly far in one frame (teleport/stutter), log [CivJump] with its brain layer/logic, mover and animator state. read-only, logs at most once per 2s per civilian / 시민 순간이동 진단 로그", null));
		SubtitleSize = Config.Bind("Terminal", "SubtitleSize", 1.4f, new ConfigDescription("cutscene subtitle text size multiplier (1.0 = original) / 컷신 자막 글자 크기 배율 (1.0 = 원래 크기)", new AcceptableValueRange<float>(0.5f, 3f)));
		KoreanFont = Config.Bind("Terminal", "KoreanFont", "Noto Sans KR", new ConfigDescription("installed Windows font(s) for Korean subtitles/epilogue, comma separated, tried in order; Malgun Gothic is always the fallback / 한국어 자막·엔딩 화면 글꼴(설치된 글꼴 이름, 쉼표로 여러 개). 없으면 맑은 고딕", null));
		WaterPlanes = Config.Bind("Terminal", "WaterPlanes", defaultValue: true, new ConfigDescription("draw the harbor/pump water planes. turn OFF for one raid as the A/B test for the port-area fps tanking — if the port runs smooth without water, the water shader is the culprit", null));
		GatesExplosion = Config.Bind("Terminal", "GatesExplosion", defaultValue: true, new ConfigDescription("restore the authored Gates_explosion mechanic: solo open needs ELITE strength (51), or plant an SZ-1 charge (either tpl) on the bomb switch — 5s plant, 10s fuse, real blast opens the gate", null));
		CraneFalling = Config.Bind("Terminal", "CraneFalling", defaultValue: true, new ConfigDescription("restore the authored falling-crane trap: walking under the crane triggers the collapse — crash animation, dust/fire VFX, contusion, 1000dmg kill zones under the fall path", null));
		FinalExit = Config.Bind("Terminal", "FinalExit", defaultValue: true, new ConfigDescription("restore the retail endgame: the Zubr exfil snaps to the authored FinalExitZone box, and opening gate 3 cuts the remaining raid time to 3 minutes (the retail evac window)", null));
		Artillery = Config.Bind("Terminal", "Artillery", defaultValue: true, new ConfigDescription("restore Terminal's planned artillery barrage, authored battery and zone configuration, and four triggers that change the current shelling zones", null));
		PumpStation = Config.Bind("Terminal", "PumpStation", defaultValue: true, new ConfigDescription("restore the pump-station power puzzle: a random electrical cabinet spawns broken — repair it (toolkit = safe, bare hands = 10% odds + shock) to power the panel, then drain the reservoir", null));
		IntroCutscene = Config.Bind("Terminal", "IntroCutscene", defaultValue: true, new ConfigDescription("play the arrival cutscene at raid start", null));
		IntroCutsceneSkippable = Config.Bind("Terminal", "IntroCutsceneSkippable", defaultValue: true, new ConfigDescription("SPACE skips the intro cutscene", null));
		EndingCutscene = Config.Bind("Terminal", "EndingCutscene", defaultValue: true, new ConfigDescription("play the retail ending cutscene when extracting at the Zubr (needs the ending scene in the bundle — Author 21B + scene rebuild). SPACE skips; the raid ends Survived either way", null));
		Epilogue = Config.Bind("Terminal", "Epilogue", defaultValue: true, new ConfigDescription("after the ending cutscene + survived jingle, play the retail 6-slide epilogue screen (medal + narrative + stats + achievements + rewards + killfeed + thanks) layered over the normal results screen. off = go straight to the normal results", null));
		EpilogueTestMode = Config.Bind("Terminal", "EpilogueTestMode", defaultValue: false, new ConfigDescription("DEV: arm the epilogue screen on ANY survived extract (Factory works). skips the ending cutscene entirely — just extract, epilogue fires. turn OFF for normal play or every raid ends in the completion-results screen", null));
		WeatherStack = Config.Bind("Terminal", "WeatherStack", defaultValue: true, new ConfigDescription("rebuild retail's weather components (WeatherController/RainController/RainFall/Splash/Wind/Clouds) — without these the map CANNOT render rain at all. turn off if weather misbehaves", null));
		ForceWeather = Config.Bind("Terminal", "ForceWeather", defaultValue: false, new ConfigDescription("optional custom opening-weather override; takes priority over RetailMissionWeather. Off uses the recovered mission weather when enabled, otherwise the SPT forecast", null));
		RetailMissionWeather = Config.Bind("Terminal", "RetailMissionWeather", defaultValue: true, new ConfigDescription("restore retail FinalMissionWeather: apply the recovered mission weather and authored time after the raid starts, then lock the sky time; no timed return to the random forecast", null));
		WeatherStartHoldSeconds = Config.Bind("Terminal", "WeatherStartHoldSeconds", 60f, new ConfigDescription("seconds to hold the optional custom startup weather before a populated native weather curve resumes; this duration is not recovered retail authoring", new AcceptableValueRange<float>(5f, 300f)));
		WeatherRain = Config.Bind("Terminal", "WeatherRain", 1f, new ConfigDescription("optional opening override: rain amount", new AcceptableValueRange<float>(0f, 1f)));
		WeatherClouds = Config.Bind("Terminal", "WeatherClouds", 0.682f, new ConfigDescription("optional opening override: cloud density (-1 clear .. 1 overcast)", new AcceptableValueRange<float>(-1f, 1f)));
		WeatherFog = Config.Bind("Terminal", "WeatherFog", 0.012f, new ConfigDescription("fog density — small numbers, 0.004 is clear", new AcceptableValueRange<float>(0f, 0.1f)));
		WeatherWind = Config.Bind("Terminal", "WeatherWind", 0.3f, new ConfigDescription("wind magnitude", new AcceptableValueRange<float>(0f, 1f)));
		WeatherThunder = Config.Bind("Terminal", "WeatherThunder", 0.2f, new ConfigDescription("lightning/thunder probability", new AcceptableValueRange<float>(0f, 1f)));
		EnvironmentExposure = Config.Bind("Terminal", "EnvironmentExposure", 0f, new ConfigDescription("how much of retail's indoor/outdoor camera EXPOSURE the environment layer applies. 0 = none (our night stays night — retail's offsets assume baked lighting we don't have), 1 = retail's authored values", new AcceptableValueRange<float>(0f, 1f)));
		AmbientRetail = Config.Bind("Terminal", "AmbientRetail", defaultValue: true, new ConfigDescription("rebuild retail's authored ambient layer (44 sound banks + 1375 players/points/splines with their real volumes) instead of the hand-tuned approximation", null));
		SpatialAudio = Config.Bind("Terminal", "SpatialAudio", defaultValue: true, new ConfigDescription("resurrect retail's verified spatial audio (76 rooms / 220 portals / occlusion bake) + indoor-outdoor environment layer. needs terminal_sound.audiobakedata in plugin-data/acoustics", null));
		SoundProbeKey = Config.Bind("Terminal", "SoundProbeKey", new KeyboardShortcut(KeyCode.F11), new ConfigDescription("dump every audibly-playing source near the player to the log (the what-is-that-noise button)", null));
		GearConfiscation = Config.Bind("Terminal", "GearConfiscation", defaultValue: true, new ConfigDescription("port security takes your headgear, weapons, backpack and carried items at raid start — locked in one random equipment cabinet (rig/armor/belt stay on, secured container untouched)", null));
		NvgAmbient = Config.Bind("Terminal", "NvgAmbient", 1f, new ConfigDescription("MULTIPLIER on AmbientIntensity while NVGs are on (1.0 = no boost, matches day-time ambient; 2.0 = twice as bright under NVGs; etc). retail authors the NVG hemisphere at 0/black and relied on lightmaps we dont have — this scales our flat fill for the NVG-on frames only", new AcceptableValueRange<float>(0.5f, 8f)));
		MaxAliveScavs = Config.Bind("Population", "MaxAliveScavs", 10, new ConfigDescription("maximum living ordinary scavs, enforced as a scav-only sub-ceiling in addition to MaxAliveBots. scav waves wait before profile generation above either ceiling. 0 = no separate scav limit", new AcceptableValueRange<int>(0, 60)));
		MaxAliveBots = Config.Bind("Population", "MaxAliveBots", 28, new ConfigDescription("living AI ceiling for ordinary scavs and special waves without authored IgnoreMaxBots. special waves with IgnoreMaxBots bypass this ceiling but retain spawn pacing; their survivors still count against other admissions. squads wait intact before profile generation. 0 = unlimited", new AcceptableValueRange<int>(0, 60)));
		MaxResidentScavs = Config.Bind("Population", "MaxResidentScavs", 32, new ConfigDescription("maximum resident ordinary-scav resources: living scavs plus uncleaned scav corpses. retiring a corpse refunds capacity without permanently exhausting later-map spawns. 0 = unlimited", new AcceptableValueRange<int>(0, 120)));
		FreshProfileWaveInterval = Config.Bind("Population", "FreshProfileWaveInterval", 5f, new ConfigDescription("minimum seconds between Terminal admissions that generate fresh bot profiles. preserves authored squads while spacing profile creation. 0 = disabled", new AcceptableValueRange<float>(0f, 30f)));
		LowPopulationWaveInterval = Config.Bind("Population", "LowPopulationWaveInterval", 2f, new ConfigDescription("fresh-profile wave interval while living AI is at or below LowPopulationWaveThreshold, allowing the map to refill faster when action is sparse. 0 = no delay in low-population catch-up", new AcceptableValueRange<float>(0f, 30f)));
		LowPopulationWaveThreshold = Config.Bind("Population", "LowPopulationWaveThreshold", 12, new ConfigDescription("effective AI count (living plus admitted/loading placements) at or below which LowPopulationWaveInterval replaces the normal fresh-profile interval", new AcceptableValueRange<int>(0, 60)));
		MaxBotsCreatedPerRaid = Config.Bind("Terminal", "MaxBotsCreatedPerRaid", 0, new ConfigDescription("DIAGNOSTIC lifetime bot budget for one Terminal raid. each final bot placement consumes one slot and deaths do NOT refund it, so corpses/replacements/new gear cannot accumulate past this many unique bot instances. 0 = unlimited lifetime spawns; takes effect next raid", new AcceptableValueRange<int>(0, 500)));
		DisableAllBots = Config.Bind("Terminal", "DisableAllBots", defaultValue: false, new ConfigDescription("DIAGNOSTIC Terminal-only botless mode. ON removes every authored bot row and blocks wave, boss/event, non-wave and hangar fallback spawn paths at runtime. map progression, triggers and cutscenes remain enabled; takes effect next raid", null));
		ScavCorpseCleanup = Config.Bind("Population", "ScavCorpseCleanup", defaultValue: true, new ConfigDescription("retire AI corpses through EFT's unregister/dispose/pool path. ordinary scavs use the short cleanup tier and are always processed before remote, older special-role corpses", null));
		ScavCorpseLifetime = Config.Bind("Population", "ScavCorpseLifetime", 60f, new ConfigDescription("minimum seconds an ordinary scav corpse remains lootable before distant cleanup. nearby corpses remain protected; special roles use their separate longer timer", new AcceptableValueRange<float>(30f, 1800f)));
		ScavCorpseCleanupDistance = Config.Bind("Population", "ScavCorpseCleanupDistance", 50f, new ConfigDescription("minimum distance from every human player to an eligible scav corpse. this guarantees a body being searched cannot disappear, including in co-op", new AcceptableValueRange<float>(20f, 300f)));
		SpecialCorpseLifetime = Config.Bind("Population", "SpecialCorpseLifetime", 300f, new ConfigDescription("seconds a Black Division, RUAF, civilian, boss, follower, PMC or other non-scav AI corpse remains lootable before remote cleanup", new AcceptableValueRange<float>(60f, 3600f)));
		SpecialCorpseCleanupDistance = Config.Bind("Population", "SpecialCorpseCleanupDistance", 150f, new ConfigDescription("minimum distance from every human player for cleanup of any non-scav AI corpse", new AcceptableValueRange<float>(75f, 600f)));
		ScavCorpseCleanupPerSweep = Config.Bind("Population", "ScavCorpseCleanupPerSweep", 4, new ConfigDescription("maximum AI corpses retired in one 10-second sweep; eligible ordinary scavs are processed first and disposal work is spread across frames", new AcceptableValueRange<int>(1, 12)));
		MigrateCorpseCleanupDefaults();
		ScavCorpseCleanupKey = Config.Bind("Population", "ScavCorpseCleanupKey", new KeyboardShortcut(KeyCode.F6), new ConfigDescription("manually retire every currently distance-eligible AI corpse. age is ignored; scav and special-role distance protections still apply", null));
		ScavRecycler = Config.Bind("Population", "ScavRecycler", defaultValue: false, new ConfigDescription("legacy opt-in: relocate idle living scavs to fulfil later waves. OFF uses fresh bots for authored scav waves", null));
		RuafRecycler = Config.Bind("Population", "RuafRecycler", defaultValue: false, new ConfigDescription("legacy opt-in: relocate idle living RUAF soldiers to fulfil later waves. OFF uses fresh bots for authored RUAF waves", null));
		BlackDivisionRecycler = Config.Bind("Population", "BlackDivisionRecycler", defaultValue: false, new ConfigDescription("legacy opt-in: relocate idle living Black Division survivors to fulfil later waves. OFF uses fresh bots for authored Black Division waves", null));
		ScavRecycleMinDistance = Config.Bind("Population", "ScavRecycleMinDistance", 50f, new ConfigDescription("minimum player distance from a living scav, RUAF soldier or Black Division operator before it may be recycled out of its old zone. camera-visible and active/recent combatants remain protected", new AcceptableValueRange<float>(50f, 300f)));
		ScavRecycleDestinationDistance = Config.Bind("Population", "ScavRecycleDestinationDistance", 40f, new ConfigDescription("minimum player distance from a destination marker used by the scav recycler. lower than the source distance so progression zones can actually accept recycled scavs", new AcceptableValueRange<float>(30f, 200f)));
		ScavRecycleMinAge = Config.Bind("Population", "ScavRecycleMinAge", 20f, new ConfigDescription("minimum seconds a living recyclable bot must have occupied its current assignment before it can be reused at another triggered encounter", new AcceptableValueRange<float>(15f, 600f)));
		MigrateRecyclerDefaults();
		TerminalBosses = Config.Bind("Terminal", "TerminalBosses", defaultValue: true, new ConfigDescription("spawn the Terminal container-berth boss selected by BossRoll. diagnostic control: OFF removes all five Terminal-specific boss candidates while leaving T4, black division, scav waves, pump and map progression unchanged; takes effect next raid", null));
		BreachableDoors = Config.Bind("Terminal", "BreachableDoors", 1, new ConfigDescription("how many of the OilStorage courtyard's authored-locked doors get rolled BREACHABLE at raid start. they ship Locked with both breach flags off (no key, no breach) — this picks a random one each raid so the route through changes. 0 = none (retail-faithful dead ends)", new AcceptableValueRange<int>(0, 7)));
		ShowSpawnTriggers = Config.Bind("Terminal", "ShowSpawnTriggers", defaultValue: false, new ConfigDescription("draw every AI spawn-trigger box as a see-through wireframe with a label. visual only — the colliders are untouched, you still walk through them. flip live.", null));
		WeaponLightShadows = Config.Bind("Terminal", "WeaponLightShadows", defaultValue: false, new ConfigDescription("let bot/player weapon flashlights and IR illuminators cast real-time shadows. OFF is the fix for the north-half frame chop: these accumulate through a raid (1 -> 23 shadow casters measured) and each one re-renders shadow casters into a shadow map every frame against a 208k-renderer scene, stalling the GPU. corpses keep their lights on, which is why killing bots never helped. map lamps are unaffected — they follow LampShadows", null));
		WorldDiff = Config.Bind("Terminal", "WorldDiff", defaultValue: false, new ConfigDescription("automatic sawtooth-onset recorder: keeps 45 seconds of frame-phase history and recent map events, snapshots component counts plus per-instance animator/particle/audio/timeline/light/camera/probe state while smooth, then dumps the before/after evidence when postLate chop becomes sustained. causes a brief hitch during state snapshots: diagnostic raids only", null));
		SceneCensus = Config.Bind("Terminal", "SceneCensus", defaultValue: false, new ConfigDescription("DIAGNOSTIC: scan all scene renderers, audio sources, particles, lights and cameras every 60 seconds. these whole-scene searches can cause a hitch on Terminal; leave off for normal play. lightweight frame timings and population heartbeats remain enabled", null));
		TraceFrameCycle = Config.Bind("Terminal", "TraceFrameCycle", defaultValue: false, new ConfigDescription("log frame timings 4x/sec so the chop's actual WAVEFORM is visible — period, duty cycle, and which phase leads. every other perf number here is a 30s average, which cannot resolve a ~4-5s cycle at all. noisy: diagnostic raids only", null));
		ProfilePlayerLoop = Config.Bind("Terminal", "ProfilePlayerLoop", defaultValue: false, new ConfigDescription("TEST-BUILD DIAGNOSTIC: instrument Unity's PostLateUpdate subsystems individually (UpdateAllRenderers / UpdateAllSkinnedMeshes / PlayerUpdateCanvases / particles / cloth) and report per-system ms in the perf heartbeat. useful in tester logs; disable after the performance test cycle", null));
		BreachDoorProbe = Config.Bind("Terminal", "BreachDoorProbe", defaultValue: false, new ConfigDescription("log every door interaction's action list + full breach flag state when you look at a door — the why-is-BREACH-greyed-out switch (takes effect after a game restart)", null));
		InteractProbeKey = Config.Bind("Terminal", "InteractProbeKey", new KeyboardShortcut(KeyCode.F9), new ConfigDescription("dump the full interaction state of every interactive object within 6m (the why-cant-i-open-this-door button)", null));
		LightProbeKey = Config.Bind("Terminal", "LightProbeKey", new KeyboardShortcut(KeyCode.F10), new ConfigDescription("dump every lit light within 4m of the player to the log (the what-is-that-glow button)", null));
		RuafNeutral = Config.Bind("Terminal", "RuafNeutral", defaultValue: true, new ConfigDescription("ruaf never add human enemies unless shot first — neutral until you draw blood", null));
		StageDirector = Config.Bind("Terminal", "StageDirector", defaultValue: true, new ConfigDescription("respond to the authored T0 unlock event and retain the retail stage reserve while surplus Zone1 bots move to Zone2; movement uses the SPT compatibility adapter", null));
		RuafDefense = Config.Bind("Terminal", "RuafDefense", defaultValue: true, new ConfigDescription("retail VSRFDefence port: when ruaf lose sight of their enemy mid-fight they collapse onto cover near their boss (30m tether) and take heal breaks, instead of scattering or blind-pursuing. visible-enemy combat is untouched", null));
		EventWavesPush = Config.Bind("Terminal", "EventWavesPush", defaultValue: true, new ConfigDescription("tier-event waves storm the players (bigbrain hunt layer) instead of passively patrolling their zone", null));
		SceneManager.sceneLoaded += TerminalAcoustics.OnSceneLoaded;
		RetailAIBake = Config.Bind("Terminal", "RetailAIBake", defaultValue: true, new ConfigDescription("fill the AI holders with the retail bake (covers/voxels/patrols/mines) at raid start — off = empty holders, bots stand around", null));
		RaidStartHour = Config.Bind("Terminal", "RaidStartHour", 21f, new ConfigDescription("custom raid start hour when RetailMissionWeather is disabled or ForceWeather is enabled. Retail mission weather supplies its own authored hour. -1 = keep natural raid time", new AcceptableValueRange<float>(-1f, 23.99f)));
		SoundRig = Config.Bind("Terminal", "SoundRig", defaultValue: true, new ConfigDescription("replay the retail SOUND rig — phased ambience (speech/footsteps/far combat), sirens, and the distant firefight director (1.0 orchestration classes are husks in 4.0, this drives the surviving AudioSources)", null));
		SoundRigVolume = Config.Bind("Terminal", "SoundRigVolume", 1f, new ConfigDescription("master multiplier on all sound-rig playback (live)", new AcceptableValueRange<float>(0f, 2f)));
		SoundRigAlarm = Config.Bind("Terminal", "SoundRigAlarm", defaultValue: true, new ConfigDescription("loop the air-raid sirens after the attack cutscene", null));
		PcDriverEnabled = Config.Bind("Terminal", "PcDriverEnabled", defaultValue: true, new ConfigDescription("occlusion culling from the .pcbake sidecars (live kill switch — flip off to isolate pop-in: pops that stop are stale bake data)", null));
		InteriorCrossCull = Config.Bind("Terminal", "InteriorCrossCull", defaultValue: true, new ConfigDescription("cull interior volumes wholesale when the camera is outside them beyond CrossCullDistance (live)", null));
		CrossCullDistance = Config.Bind("Terminal", "CrossCullDistance", 30f, new ConfigDescription("how close an out-of-volume interior group must be to still render (doorway/window sightlines) (live)", new AcceptableValueRange<float>(10f, 80f)));
		try
		{
			string text = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "PerfectCullingRuntime.dll");
			if (File.Exists(text))
			{
				Assembly.LoadFrom(text);
				Log.LogInfo("PerfectCullingRuntime.dll preloaded (terminal occlusion culling)");
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning("PerfectCullingRuntime preload failed: " + ex.Message);
		}
		TerminalBundleHost.Init();
		Patch(typeof(TerminalGate.Patch_CaptureLocationId));
		Patch(typeof(TerminalIntroCutscene.Patch_PlayAtRaidStart));
		Patch(typeof(TerminalAttackCutscene.Patch_ArmAttackTimer));
		Patch(typeof(TerminalShaderRebind.Patch_RebindAtRaidStart));
		Patch(typeof(TerminalWater.Patch_PerPlaneMatrices));
		try
		{
			new TerminalSoundRig.Patch_AttachAtRaidStart().Enable();
		}
		catch (Exception arg)
		{
			Log.LogError($"sound rig attachment patch failed: {arg}");
		}
		Patch(typeof(TerminalAIBake.Patch_RestoreCoversData));
		// zzap perf: [PathPerf] diagnostics wrap BotPathFinderCorePoints.FindPath for every
		// bot on every map — developer mode only (takes effect after a game restart)
		if (DevMode.Value)
		{
			Patch(typeof(TerminalPathDiagnostics.Patch_CoverRoute));
			Patch(typeof(TerminalPathDiagnostics.Patch_NavQuery));
		}
		try
		{
			new TerminalAIPlaces.Patch_BuildSpawnTriggers().Enable();
		}
		catch (Exception arg2)
		{
			Log.LogError($"[AIPlaces] trigger restoration patch failed: {arg2}");
		}
		Patch(typeof(TerminalBotFixes.Patch_CoversCache));
		Patch(typeof(TerminalBotFixes.Patch_BotDoorsRefresh));
		Patch(typeof(TerminalBotFixes.Patch_PlaySoundAirbag));
		Patch(typeof(TerminalBotFixes.Patch_DoorTriggerEmit));
		Patch(typeof(TerminalBotFixes.Patch_BotActivationStepwise));
		Patch(typeof(TerminalBotFixes.Patch_PatrolSubPoints));
		Patch(typeof(TerminalBotFixes.Patch_GetSubPointEmptyGuard));
		Patch(typeof(TerminalInteractables.Patch_HealInteractables));
		Patch(typeof(TerminalBreachDoors.Patch_RollBreachableDoor));
		// zzap perf: this probe postfixes every door-action builder and Harmony boxes an
		// object[] of its args on every call (every frame you look at a door, every map)
		// even while the probe is off — only install it when the probe is wanted.
		if (BreachDoorProbe.Value)
		{
			Patch(typeof(Patch_BreachActionProbe));
		}
		try
		{
			new TerminalSpawnGate.Patch_ArmGate().Enable();
		}
		catch (Exception arg3)
		{
			Log.LogError($"spawn gate initialization patch failed: {arg3}");
		}
		Patch(typeof(TerminalSpawnGate.Patch_GateWaves));
		try
		{
			new TerminalSpawnGate.Patch_GateBosses().Enable();
		}
		catch (Exception arg4)
		{
			Log.LogError($"boss wave admission patch failed: {arg4}");
		}
		Patch(typeof(TerminalSpawnGate.Patch_GateNonWaves));
		Patch(typeof(TerminalSpawnGate.Patch_AdmitWithoutWave));
		Patch(typeof(TerminalSpawnGate.Patch_BypassNativeCapForAdmittedScavs));
		Patch(typeof(TerminalSpawnGate.Patch_FinalLifetimeCap));
		Patch(typeof(TerminalPopulationDirector.Patch_Attach));
		Patch(typeof(TerminalGhostPatches.Patch_Attach));
		Patch(typeof(TerminalGhostPatches.Patch_NewRaid));
		Patch(typeof(TerminalGhostPatches.Patch_EndRaid));
		Patch(typeof(TerminalGhostPatches.Patch_Agent));
		Patch(typeof(TerminalGhostPatches.Patch_BigBrain));
		Patch(typeof(TerminalGhostPatches.Patch_Look));
		Patch(typeof(TerminalGhostPatches.Patch_Visible));
		Patch(typeof(TerminalGhostPatches.Patch_Goal));
		Patch(typeof(TerminalGhostPatches.Patch_Damage));
		Patch(typeof(TerminalGhostPatches.Patch_Death));
		Patch(typeof(TerminalGhostPatches.Patch_Shoot));
		Patch(typeof(TerminalGhostPatches.Patch_Move));
		Patch(typeof(TerminalGhostPatches.Patch_SainGoal));
		Patch(typeof(TerminalGhostPatches.Patch_SainActivation));
		Patch(typeof(TerminalGhostPatches.Patch_Stop));
		Patch(typeof(TerminalGhostPatches.Patch_WeaponParent));
		Patch(typeof(TerminalGhostPatches.Patch_WeaponPool));
		Patch(typeof(TerminalGhostSain.Patch_Valid));
		Patch(typeof(TerminalGhostSain.Patch_Add));
		Patch(typeof(TerminalGhostSain.Patch_Los));
		Patch(typeof(TerminalGhostSain.Patch_Look));
		Patch(typeof(TerminalGhostSain.Patch_BotLook));
		Patch(typeof(TerminalGhostSain.Patch_Source));
		Patch(typeof(TerminalGhostSain.Patch_Dispatch));
		Patch(typeof(TerminalGhostSain.Patch_Ignore));
		Patch(typeof(TerminalGhostSain.Patch_Process));
		Patch(typeof(TerminalGhostSain.Patch_Flyby));
		Patch(typeof(TerminalGhostSain.Patch_Emit));
		Patch(typeof(TerminalGhostSain.Patch_BaseSound));
		Patch(typeof(TerminalGhostSain.Patch_Talk));
		Patch(typeof(TerminalGhostSain.Patch_EnemyTalk));
		Patch(typeof(TerminalGhostSain.Patch_GroupTalk));
		Patch(typeof(TerminalGhostSain.Patch_Shot));
		Patch(typeof(TerminalGhostSain.Patch_Watch));
		Patch(typeof(TerminalGhostSain.Patch_Bot));
		Patch(typeof(TerminalGhostSain.Patch_WorldTiming));
		Patch(typeof(TerminalGhostSain.Patch_FindTiming));
		TerminalGhostSain.CheckInstalled();
		TerminalGhostPatches.CheckInstalled();
		Patch(typeof(TerminalPopulationDirector.Patch_DropDestroyedBotTasks));
		Patch(typeof(TerminalPopulationDirector.Patch_DropDestroyedBleedingPlayers));
		Patch(typeof(TerminalPopulationDirector.Patch_AuditCorpseIdentity));
		Patch(typeof(TerminalAudioFixes.Patch_SpatialAudioInitSkip));
		Patch(typeof(TerminalAudioFixes.Patch_InteractiveOcclusionUninit));
		Patch(typeof(TerminalAudioFixes.Patch_SourceOcclusionUninit));
		Patch(typeof(TerminalAudioFixes.Patch_NullBarbedWireSound));
		Patch(typeof(TerminalInteractables.Patch_DoorProbe));
		Patch(typeof(TerminalRuafNeutral.Patch_RuafNeutralToHumans));
		Patch(typeof(TerminalGearTax.Patch_GearTax));
		try
		{
			new TerminalCabinetSearch.RevealPatch().Enable();
		}
		catch (Exception arg5)
		{
			Log.LogError($"[GearTax] cabinet search reveal patch failed: {arg5}");
		}
		Patch(typeof(TerminalTimeWeather.Patch_Arm));
		try
		{
			new TerminalWeatherLifecycle.Patch_WeatherNodes().Enable();
			new TerminalWeatherLifecycle.Patch_IncompleteSeasonVisuals().Enable();
			new TerminalWeatherLifecycle.Patch_LocalGameWeatherStage().Enable();
		}
		catch (Exception arg6)
		{
			Log.LogError($"[WeatherLifecycle] native weather initialization patches failed: {arg6}");
		}
		Patch(typeof(TerminalBallisticGlass.Patch_Sweep));
		Patch(typeof(TerminalGateAmbushTime.Patch_Retime));
		try
		{
			new TerminalStageWait.Patch_Node().Enable();
			new TerminalStageWait.Patch_Brain().Enable();
			TerminalStageWait.Enabled = true;
			new TerminalStageDirector.Patch_Arm().Enable();
		}
		catch (Exception arg7)
		{
			Log.LogError($"[StageDirector] stage event patch failed: {arg7}");
		}
		try
		{
			new TerminalArtillery.UpdateOncePatch().Enable();
			TerminalArtillery.UpdateGuardEnabled = true;
			new TerminalArtillery.PlannedZonesPatch().Enable();
			new TerminalArtillery.RetiredProjectilePatch().Enable();
			new TerminalArtillery.GeometryPatch().Enable();
			TerminalArtillery.ZoneAdapterEnabled = true;
		}
		catch (Exception arg8)
		{
			Log.LogError($"[Artillery] compatibility patch setup failed; Terminal configuration will not be injected: {arg8}");
		}
		Patch(typeof(TerminalAudioFixes.Patch_DoorFoleyVolume));
		Patch(typeof(TerminalEscortFix.Patch_EscortsHonourIgnoreMaxBots));
		Patch(typeof(TerminalLights.Patch_LightsAtRaidStart));
		Patch(typeof(TerminalLights.Patch_LampsDead));
		Patch(typeof(TerminalLights.Patch_LampsDeadTick));
		Patch(typeof(TerminalArtilleryDisposeGuard.Patch_SoundDisposeArmor));
		Patch(typeof(TerminalRuafNeutral.Patch_RuafProvokedByDamage));
		Patch(typeof(TerminalCameraDonor.Patch_DumpDonorCamera));
		Patch(typeof(TerminalCameraDonor.Patch_GraftDonorCamera));
		Patch(typeof(Patch_RejectShellCameraPrefab));
		Patch(typeof(Patch_GrenadeFlashPrismRef));
		Patch(typeof(Patch_EffectsControllerFrostbite));
		Patch(typeof(Patch_RainScreenOnCam2));
		Patch(typeof(Patch_NightVisionNeverSpams));
		Patch(typeof(Patch_EffectsControllerInit));
		Patch(typeof(TerminalCullingDriver.Patch_CaptureCamera));
		Patch(typeof(TerminalCullingDriver.Patch_AttachAtRaidStart));
		try
		{
			new TerminalGrass.Patch_RestoreAtRaidStart().Enable();
		}
		catch (Exception arg9)
		{
			Log.LogError($"grass patch FAILED: {arg9}");
		}
		try
		{
			new TerminalVegetation.TramplerAwakePatch().Enable();
		}
		catch (Exception arg10)
		{
			Log.LogError($"grass trampler patch FAILED: {arg10}");
		}
		try
		{
			new TerminalVegetation.RaidStartedPatch().Enable();
		}
		catch (Exception arg11)
		{
			Log.LogError($"vegetation patch FAILED: {arg11}");
		}
		Patch(typeof(Patch_OcclusionWhenUninitialized));
		Patch(typeof(Patch_WindowBreakerPrewarm));
		Patch(typeof(Patch_SpawnPmcScan));
		Patch(typeof(Patch_EnsureStationaryController));
		Patch(typeof(Patch_StationaryInit));
		Patch(typeof(Patch_BotZonePruneMarkers));
		Patch(typeof(Patch_BotDoorsRefresh));
		Patch(typeof(Patch_BotsInitFirewall));
		Patch(typeof(Patch_GameStartFirewall));
		Patch(typeof(Patch_EnsureEnvBeforeAnyPlayerInit));
		try
		{
			new Patch_GateSwitchActions().Enable();
		}
		catch (Exception arg12)
		{
			Log.LogError($"[Gates] switch interaction patch failed: {arg12}");
		}
		Patch(typeof(TerminalHoldLock.Patch_FreezeDuringHold));
		Patch(typeof(TerminalFinalExit.Patch_BuildZubrExit));
		Patch(typeof(Patch_PumpSwitchActions));
		Patch(typeof(TerminalPumpStation.Patch_SelectAtRaidStart));
		Patch(typeof(TerminalEndingCutscene.Patch_InterceptExtraction));
		Patch(typeof(TerminalEpilogueScreen.Patch_HijackExitStatus));
		Patch(typeof(TerminalEpilogueScreen.Patch_TestArmOnAnyExit));
		Patch(typeof(TerminalLootBind.Patch_DeferLootBind));
		Patch(typeof(TerminalAudioTeardown.Patch_AmbientDisposeArmor));
		Patch(typeof(TerminalAudioTeardown.Patch_BlendDisposeArmor));
		Patch(typeof(TerminalTimerArmor.Patch_TimerTextArmor));
		try
		{
			Patch_NativeCullingGate.TryPatch(HarmonyInstance);
		}
		catch (Exception arg13)
		{
			Log.LogError($"native culling gate FAILED: {arg13}");
		}
		try
		{
			TerminalQuestingBotsOff.TryPatch(HarmonyInstance);
		}
		catch (Exception arg14)
		{
			Log.LogWarning($"questing-bots mute failed: {arg14}");
		}
		try
		{
			TerminalSptCustomAiGuard.TryPatch(HarmonyInstance);
		}
		catch (Exception arg15)
		{
			Log.LogWarning($"SPT custom-AI map guard failed: {arg15}");
		}
		try
		{
			TerminalLockableDoorsOff.TryPatch(HarmonyInstance);
		}
		catch (Exception arg16)
		{
			Log.LogWarning($"lockable-doors shim failed: {arg16}");
		}
		try
		{
			TerminalCrashGuard.TryPatch(HarmonyInstance);
		}
		catch (Exception arg17)
		{
			Log.LogError($"crash guard failed: {arg17}");
		}
		try
		{
			new CivilianBrainInitPatch().Enable();
		}
		catch (Exception arg18)
		{
			Log.LogError($"civilian brain init failed: {arg18}");
		}
		try
		{
			new GunshotHearingPatch().Enable();
		}
		catch (Exception arg19)
		{
			Log.LogError($"civilian gunshot patch failed: {arg19}");
		}
		try
		{
			new CivilianKnifePatch().Enable();
		}
		catch (Exception arg20)
		{
			Log.LogError($"civilian knife patch failed: {arg20}");
		}
		try
		{
			new CivilianWeaponAvailabilityPatch().Enable();
		}
		catch (Exception arg21)
		{
			Log.LogError($"civilian weapon availability patch failed: {arg21}");
		}
		try
		{
			new CivilianArtilleryPatch().Enable();
		}
		catch (Exception arg22)
		{
			Log.LogError($"civilian artillery decision patch failed: {arg22}");
		}
		try
		{
			new CivilianMainWeaponPatch().Enable();
		}
		catch (Exception arg23)
		{
			Log.LogError($"civilian main weapon patch failed: {arg23}");
		}
		try
		{
			new CivilianAnimatorPatch().Enable();
		}
		catch (Exception arg24)
		{
			Log.LogError($"civilian animator patch failed: {arg24}");
		}
		try
		{
			new CivilianUpperBodyPatch().Enable();
		}
		catch (Exception arg25)
		{
			Log.LogError($"civilian upper-body patch failed: {arg25}");
		}
		if (Chainloader.PluginInfos.ContainsKey("com.manimal.motionmagic"))
		{
			try
			{
				new CivilianMotionMatchingPatch().Enable();
				Log.LogInfo("[Civ] MotionMagic compatibility active: matched locomotion, authored civilian upper body and idle/cower ownership");
			}
			catch (Exception arg26)
			{
				Log.LogError($"civilian MotionMatching compatibility failed: {arg26}");
			}
		}
		SceneManager.sceneLoaded += (Scene scene, LoadSceneMode mode) =>
		{
			if (scene.name == null || !scene.name.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			try
			{
				TerminalShaderRebind.CaptureSceneMaterials(scene);
			}
			catch (Exception ex2)
			{
				Log.LogWarning("[Plugin] material capture '" + scene.name + "' threw: " + ex2.Message);
			}
			try
			{
				TerminalSceneScrub.Scrub(scene);
			}
			catch (Exception ex3)
			{
				Log.LogWarning("[Plugin] scrub '" + scene.name + "' threw: " + ex3.Message);
			}
		};
		Log.LogInfo("[Manimal-Terminal] 0.2.20 loaded");
	}

	private void MigrateCorpseCleanupDefaults()
	{
		List<string> list = new List<string>();
		if (ScavCorpseLifetime.Value == 300f)
		{
			ScavCorpseLifetime.Value = 120f;
			list.Add("scavLifetime 300->120");
		}
		if (ScavCorpseCleanupDistance.Value == 75f)
		{
			ScavCorpseCleanupDistance.Value = 50f;
			list.Add("scavDistance 75->50");
		}
		if (SpecialCorpseLifetime.Value == 600f)
		{
			SpecialCorpseLifetime.Value = 300f;
			list.Add("specialLifetime 600->300");
		}
		if (SpecialCorpseCleanupDistance.Value == 300f)
		{
			SpecialCorpseCleanupDistance.Value = 150f;
			list.Add("specialDistance 300->150");
		}
		if (list.Count != 0)
		{
			Config.Save();
			Log.LogWarning("[CorpseCleanup] migrated former default retention settings: " + string.Join(", ", list));
		}
	}

	private void MigrateRecyclerDefaults()
	{
		List<string> list = new List<string>();
		if (MaxAliveBots.Value == 24)
		{
			MaxAliveBots.Value = 28;
			list.Add("maxAliveBots 24->28");
		}
		if (ScavRecycleMinDistance.Value == 75f)
		{
			ScavRecycleMinDistance.Value = 50f;
			list.Add("sourceDistance 75->50");
		}
		if (ScavRecycleMinAge.Value == 45f)
		{
			ScavRecycleMinAge.Value = 20f;
			list.Add("assignmentAge 45->20");
		}
		if (list.Count != 0)
		{
			Config.Save();
			Log.LogWarning("[Recycler] migrated former default eligibility settings: " + string.Join(", ", list));
		}
	}

	private void Patch(Type t)
	{
		try
		{
			HarmonyInstance.CreateClassProcessor(t).Patch();
		}
		catch (Exception arg)
		{
			Log.LogError($"patch {t.Name} FAILED: {arg}");
		}
	}
}
