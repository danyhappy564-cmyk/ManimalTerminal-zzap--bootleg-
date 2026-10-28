using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace Manimal.Terminal;

// zzap 10/28 (user): "f12 설정값 한글로 변경", then "f12는 영어로도 보이게 ... 영문 모드로도
// 작동되게". Keys and sections stay English so every saved value carries over; only what
// F12 (ConfigurationManager) SHOWS changes, and it follows TerminalLang.IsKorean (F12
// Language, Auto = the game's language). ConfigurationManager rebuilds its list each time
// the window opens, so switching the tags is enough.
// ConfigurationManager reads System.ComponentModel DisplayName/Category/Description
// tags, and stops at the first tag of any other type unless it is named
// ConfigurationManagerAttributes — so ours go first.
internal static class TerminalConfigKorean
{
	private static readonly FieldInfo TagsField = AccessTools.Field(typeof(ConfigDescription), "<Tags>k__BackingField");
	private static readonly FieldInfo DescriptionField = AccessTools.Field(typeof(ConfigDescription), "<Description>k__BackingField");

	private static readonly Dictionary<string, string> Sections = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["Terminal"] = "터미널",
		["Terminal Ghost"] = "터미널 · 유령 모드",
		["Population"] = "터미널 · 봇 인구",
	};

	// key -> (표시 이름, 설명). Ghost keys are shared by name between the visible
	// [Terminal Ghost] entry and its hidden [Terminal] legacy source.
	private static readonly Dictionary<string, (string Name, string Text)> Entries = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
	{
		// 조명·그래픽
		["LampAuthored"] = ("조명: 리테일 원본 밝기", "가로등마다 리테일 원본 밝기·색·범위를 씁니다(꺼진 고장 조명은 계속 꺼짐). 끄면 아래 '조명 밝기' 하나로 맞춥니다."),
		["LampAuthoredScale"] = ("조명: 원본 밝기 배율", "리테일 원본 밝기에 곱하는 값입니다. 1.0 = 리테일과 같음."),
		["LampIntensity"] = ("조명: 밝기", "되살린 조명의 밝기입니다. 0이면 조명을 완전히 꺼서 그래픽 부담이 크게 줄어듭니다."),
		["AmbientIntensity"] = ("주변광: 밝기", "그늘진 곳이 새까맣지 않도록 전체에 깔리는 기본 밝기입니다."),
		["AmbientColorOverride"] = ("주변광: 색 직접 지정", "하늘에서 가져온 주변광 색 대신 아래 빨강·초록·파랑 값을 씁니다. 레이드 중 바로 바꿔 비교할 수 있습니다."),
		["AmbientColorR"] = ("주변광: 빨강", "주변광 색 직접 지정이 켜져 있을 때의 빨강 값(0~1)."),
		["AmbientColorG"] = ("주변광: 초록", "주변광 색 직접 지정이 켜져 있을 때의 초록 값(0~1)."),
		["AmbientColorB"] = ("주변광: 파랑", "주변광 색 직접 지정이 켜져 있을 때의 파랑 값(0~1)."),
		["LampShadows"] = ("조명: 실시간 그림자", "되살린 조명이 실시간 그림자를 만듭니다. 훨씬 보기 좋지만 훨씬 무겁습니다."),
		["LodBiasClamp"] = ("LOD 배율 상한", "유니티 전역 LOD 배율의 상한입니다(즉시 적용). 낮출수록 먼 물체를 단순하게 그려 프레임이 오릅니다."),
		["LodCullFloor"] = ("먼 물체 숨김 기준", "가까운 범위 밖에서, 화면에서 이 비율보다 작게 보이는 물체는 그리지 않습니다(즉시 적용). 높이면 프레임이 오르지만 멀리서 물체가 툭 사라지는 게 더 보입니다. -1 = 리테일 그대로."),
		["LodCullNearFloor"] = ("가까운 물체 숨김 기준", "가까운 범위 안에서 물체를 숨기는 화면 비율입니다(즉시 적용). 주변 물체가 깜빡이지 않도록 아주 작게 둡니다. -1 = 리테일 그대로."),
		["LodCullNearRadius"] = ("가까운 범위(야외, m)", "야외에서 '가까운 범위'로 보는 반경입니다(즉시 적용)."),
		["LodCullNearRadiusIndoor"] = ("가까운 범위(실내, m)", "실내에서 '가까운 범위'로 보는 반경입니다(즉시 적용). 실내는 시야가 짧아 작게 둡니다."),
		["LodCellSize"] = ("LOD 구역 크기(m)", "맵을 나누는 구역 한 칸의 크기입니다. 클수록 계산이 가볍지만 경계가 거칠어집니다."),
		["LootCullRadius"] = ("바닥 아이템 표시 거리(m)", "바닥에 떨어진 아이템을 이 거리까지만 그립니다(즉시 적용)."),
		["LightCullDistance"] = ("조명 꺼짐 거리(m)", "조명이 이 거리에서 완전히 꺼지도록 서서히 어두워집니다. 낮출수록 프레임이 오르고 어두워집니다(낮추는 것만 즉시 적용, 올리면 다음 레이드부터)."),
		["GrassEnabled"] = ("풀 표시", "터미널 리테일 풀(약 8만 8천 개, 150m)을 되살립니다. 다음 레이드부터 적용."),
		["LensFlares"] = ("조명 렌즈 플레어", "가로등마다 붙는 리테일 렌즈 플레어(3,011개)를 다시 만듭니다. 프레임 비교용 스위치."),
		["WaterPlanes"] = ("바다·배수장 물 표시", "항구와 펌프장 물 표면을 그립니다. 항구 쪽 프레임 하락 비교용으로 한 판 꺼 볼 수 있습니다."),
		["WeaponLightShadows"] = ("총기 라이트 그림자", "봇·플레이어의 총기 라이트와 적외선 조명이 실시간 그림자를 만듭니다. 꺼 두는 것이 북쪽 구역 프레임 끊김 해결책입니다."),
		["NvgAmbient"] = ("야간투시경 주변광 배율", "야간투시경을 켰을 때 주변광 밝기에 곱하는 값입니다. 1.0 = 그대로."),
		["EnvironmentExposure"] = ("실내외 노출 보정", "리테일의 실내·실외 카메라 노출 보정을 얼마나 적용할지입니다. 0 = 적용 안 함(밤은 계속 밤)."),
		["PcDriverEnabled"] = ("가림 컬링(pcbake)", "미리 계산한 가림 데이터로 안 보이는 물체를 그리지 않습니다(즉시 적용). 물체가 갑자기 나타나면 꺼서 비교해 보세요."),
		["InteriorCrossCull"] = ("바깥에서 실내 통째로 숨김", "카메라가 건물 밖에 있으면 일정 거리 밖 실내를 통째로 그리지 않습니다(즉시 적용)."),
		["CrossCullDistance"] = ("실내 숨김 거리(m)", "바깥에서 이 거리 안의 실내는 문·창문 너머로 보이도록 계속 그립니다(즉시 적용)."),
		["CamDonorSkip"] = ("개발: 카메라 이식 제외 목록", "카메라 설정을 옮길 때 건너뛸 컴포넌트 이름(쉼표 구분). 문제 찾기용이니 비워 두세요."),
		["DevMode"] = ("개발자 모드", "카메라 덤프, 전체 카메라 렌더 측정, 프레임 기록, 봇 경로 진단을 켭니다. 평소에는 꺼 두세요."),

		// 하늘·날씨·소리
		["SkyHourOffset"] = ("하늘 시간 보정(시간)", "하늘이 쓰는 시간에만 더하는 값입니다(레이드 시계는 그대로)."),
		["SkyNightStrength"] = ("밤하늘 고정 세기", "21시~6시에 하늘을 얼마나 강하게 밤으로 고정할지입니다. 0 = 고정 안 함."),
		["WeatherStack"] = ("날씨 장치 복원", "리테일 날씨 장치(비·구름·바람 등)를 다시 만듭니다. 없으면 비가 아예 안 내립니다."),
		["ForceWeather"] = ("시작 날씨 직접 지정", "아래 비·구름·안개 값으로 시작 날씨를 덮어씁니다. 리테일 임무 날씨보다 우선합니다."),
		["RetailMissionWeather"] = ("리테일 임무 날씨", "리테일 터미널 임무의 날씨와 시간을 레이드 시작 후 적용하고 하늘 시간을 고정합니다."),
		["WeatherStartHoldSeconds"] = ("직접 지정 날씨 유지(초)", "직접 지정한 시작 날씨를 몇 초 유지한 뒤 원래 날씨 흐름으로 돌아갈지입니다."),
		["WeatherRain"] = ("시작 날씨: 비", "시작 날씨 직접 지정 시 비의 양."),
		["WeatherClouds"] = ("시작 날씨: 구름", "시작 날씨 직접 지정 시 구름 양(-1 맑음 ~ 1 흐림)."),
		["WeatherFog"] = ("시작 날씨: 안개", "안개 농도입니다. 아주 작은 값을 쓰며 0.004가 맑은 편입니다."),
		["WeatherWind"] = ("시작 날씨: 바람", "바람 세기."),
		["WeatherThunder"] = ("시작 날씨: 천둥", "번개·천둥 확률."),
		["RaidStartHour"] = ("레이드 시작 시각", "리테일 임무 날씨를 끄거나 시작 날씨를 직접 지정할 때의 시작 시각입니다. -1 = 원래 시각 유지."),
		["AmbientSplines"] = ("추가 환경음(바람·비·금속)", "선택 사항인 환경음 장치를 켭니다. 꺼도 바닷가 파도 소리는 납니다."),
		["AmbientRetail"] = ("리테일 환경음", "리테일 환경음(사운드 뱅크 44개, 재생 지점 1,375개)을 원래 볼륨으로 다시 만듭니다."),
		["SpatialAudio"] = ("리테일 공간 음향", "리테일 공간 음향(방 76개, 통로 220개, 가림 계산)과 실내외 소리 구분을 되살립니다."),
		["SoundRig"] = ("리테일 사운드 연출", "말소리·발소리·먼 교전 같은 단계별 배경음, 사이렌, 먼 총격전 연출을 재생합니다."),
		["SoundRigVolume"] = ("사운드 연출 볼륨", "사운드 연출 전체 볼륨 배율입니다(즉시 적용)."),
		["SoundRigAlarm"] = ("공습 사이렌 반복", "공격 컷신 이후 공습 사이렌을 반복 재생합니다."),
		["SoundRigFirefight"] = ("먼 총격전 소리", "멀리서 들리는 총격전 배경음입니다. 프레임 끊김 비교용으로 한 판 꺼 볼 수 있습니다."),
		["DoorFoleyVolume"] = ("문 소리 볼륨", "문 여닫는 소리·삐걱임 볼륨 배율입니다(1.0 = 원본)."),
		["SoundProbeKey"] = ("진단 키: 주변 소리 기록", "근처에서 들리는 소리를 모두 로그에 남깁니다('이 소리 뭐지?' 버튼)."),

		// 컷신·자막·언어
		["IntroCutscene"] = ("도착 컷신", "레이드 시작 때 도착 컷신을 재생합니다."),
		["IntroCutsceneSkippable"] = ("도착 컷신 건너뛰기 허용", "스페이스바로 도착 컷신을 건너뛸 수 있습니다."),
		["AttackCutscene"] = ("공격 컷신", "레이드 중간에 항구 공격 컷신을 재생합니다."),
		["AttackCutsceneDelay"] = ("공격 컷신 시점(초)", "도착 컷신이 끝나고 몇 초 뒤에 공격 컷신이 나올지입니다(45 = 리테일)."),
		["AttackCutsceneSkippable"] = ("공격 컷신 건너뛰기 허용", "스페이스바로 공격 컷신을 건너뛸 수 있습니다. 끄면 리테일처럼 못 건너뜁니다."),
		["HoldBotsForCutscene"] = ("공격 컷신 전 봇 대기", "공격 컷신이 나오기 전에는 봇이 나오지 않습니다(공격 전 항구는 평화로운 상태)."),
		["CutscenePlayerTop"] = ("컷신 주인공 상의", "도착 컷신의 주인공이 내 PMC가 입은 상의를 입습니다."),
		["EndingCutscene"] = ("엔딩 컷신", "주브르 탈출 시 리테일 엔딩 컷신을 재생합니다. 스페이스바로 건너뛸 수 있습니다."),
		["Epilogue"] = ("엔딩 화면", "엔딩 컷신 뒤 리테일 6장짜리 엔딩 화면(훈장·이야기·통계·업적·보상)을 보여 줍니다."),
		["EpilogueTestMode"] = ("개발: 엔딩 화면 시험", "어느 맵이든 생존 탈출하면 엔딩 화면을 띄웁니다. 평소에는 꺼 두세요."),
		["CutsceneSubtitles"] = ("컷신 자막", "컷신 중에 자막을 보여 줍니다."),
		["Language"] = ("언어", "터미널 자막·알림·안내·엔딩 화면 언어입니다. Auto = 게임 언어를 따름."),
		["SubtitleSize"] = ("자막 크기", "컷신 자막 글자 크기 배율입니다(1.0 = 원래 크기)."),
		["KoreanFont"] = ("한국어 글꼴", "한국어 자막·엔딩 화면에 쓸 설치된 글꼴 이름(쉼표로 여러 개, 앞에서부터 시도). 없으면 맑은 고딕을 씁니다."),

		// 맵 기믹
		["GatesExplosion"] = ("정문 폭파 기믹", "정문은 힘 엘리트(51)로 열거나 폭약(SZ-1)을 설치해 터뜨려야 합니다(설치 5초, 폭발 10초)."),
		["CraneFalling"] = ("크레인 붕괴 함정", "크레인 밑을 지나가면 크레인이 무너집니다(먼지·불·충격, 깔리면 사망)."),
		["FinalExit"] = ("리테일 최종 탈출", "주브르 탈출구를 리테일 위치로 맞추고, 3번 게이트를 열면 남은 시간이 3분으로 줄어듭니다."),
		["Artillery"] = ("포격", "터미널 리테일 포격과 포격 구역을 바꾸는 장치 4개를 되살립니다."),
		["PumpStation"] = ("펌프장 전기 퍼즐", "고장 난 배전함 하나를 고쳐(공구 = 안전, 맨손 = 10% 확률 + 감전) 패널에 전기를 넣는 퍼즐입니다."),
		["BallisticGlassPen"] = ("방탄유리 관통", "터미널 방탄유리를 플레이어와 봇 모두 쏴서 뚫을 수 있게 합니다(원래는 보이지 않는 방탄벽)."),
		["BreachableDoors"] = ("돌파 가능한 문 수", "정유 저장소 마당의 잠긴 문 가운데 레이드 시작 때 몇 개를 돌파 가능하게 할지입니다."),
		["GateAmbushTime"] = ("정문 매복조 등장 시각(초)", "정문 매복조 2명이 나오는 레이드 시각입니다. 리테일은 950초. -1 = 원래 시각."),
		["GearConfiscation"] = ("입장 장비 압수", "항구 경비가 레이드 시작 때 머리·무기·가방·소지품을 압수해 무작위 장비함에 넣습니다(조끼·방탄복·벨트·보안 컨테이너는 유지)."),

		// 봇
		["RuafNeutral"] = ("RUAF 중립", "RUAF는 먼저 공격받기 전에는 사람을 적으로 보지 않습니다."),
		["RuafInvulnerable"] = ("RUAF 무적(플레이어 피해)", "플레이어의 총·수류탄 등 모든 피해에 RUAF가 무적입니다. 피해·출혈·골절 없음, 계속 중립. 끄면 쏘는 순간 RUAF 전체가 적대합니다."),
		["RuafDefense"] = ("RUAF 방어 행동", "RUAF가 교전 중 적을 놓치면 흩어지지 않고 대장 근처 엄폐로 모여 치료합니다(리테일 방식)."),
		["StageDirector"] = ("단계 진행", "리테일 단계 신호에 따라 1구역의 남는 봇을 2구역으로 옮깁니다."),
		["EventWavesPush"] = ("이벤트 웨이브 돌격", "단계 이벤트 웨이브가 자기 구역을 순찰하는 대신 플레이어 쪽으로 몰려옵니다."),
		["RetailAIBake"] = ("리테일 AI 데이터", "레이드 시작 때 리테일 AI 데이터(엄폐·순찰 경로 등)를 채웁니다. 끄면 봇이 서 있기만 합니다."),
		["TerminalBosses"] = ("터미널 보스", "컨테이너 부두 보스를 등장시킵니다. 끄면 터미널 전용 보스 후보 5종이 빠집니다."),
		["MaxBotsCreatedPerRaid"] = ("진단: 레이드당 봇 생성 한도", "한 레이드에서 만들 수 있는 봇 수입니다. 죽어도 다시 늘지 않습니다. 0 = 제한 없음."),
		["DisableAllBots"] = ("진단: 봇 전부 끄기", "모든 봇 등장을 막습니다. 맵 진행 확인용이니 평소에는 꺼 두세요."),
		["ShowSpawnTriggers"] = ("진단: 봇 등장 영역 표시", "봇 등장 영역 상자를 선으로 그립니다. 보기만 하며 즉시 적용됩니다."),
		["CivilianUpperBodyMask"] = ("시민: 상체 IK 끄기", "칼·빈손 시민의 무기·손·전신 IK를 매 프레임 끕니다(원작 동작). 레이드 중 바로 적용."),
		["CivilianRetailAnimator"] = ("시민: 전용 애니메이션", "시민 전용 리테일 애니메이션을 씁니다. 켜면 시민이 매 프레임 떨리는 문제가 있어 기본은 꺼짐(스캐브 기본 동작). 새로 나오는 시민부터 적용."),
		["CivilianJumpProbe"] = ("진단: 시민 동작 기록", "시민 순간이동·떨림·봇 화면 비용을 로그에 남깁니다. 읽기만 합니다."),

		// 진단
		["WorldDiff"] = ("진단: 끊김 직전 기록", "끊김이 시작되면 직전 45초의 프레임과 맵 사건을 기록합니다."),
		["SceneCensus"] = ("진단: 장면 전체 집계", "60초마다 모든 렌더러·소리·조명을 셉니다. 이 자체가 끊김을 만들 수 있어 평소에는 꺼 두세요."),
		["TraceFrameCycle"] = ("진단: 프레임 파형 기록", "프레임 시간을 1초에 4번 기록합니다."),
		["ProfilePlayerLoop"] = ("진단: 유니티 갱신 단계 측정", "유니티 렌더·스키닝 등 갱신 단계를 하나씩 잽니다. 시험용."),
		["BreachDoorProbe"] = ("진단: 문 돌파 상태 기록", "문을 볼 때 상호작용 목록과 돌파 상태를 기록합니다(게임 재시작 후 적용)."),
		["InteractProbeKey"] = ("진단 키: 주변 상호작용 기록", "6m 안 상호작용 물체 상태를 모두 기록합니다('이 문 왜 안 열리지?' 버튼)."),
		["LightProbeKey"] = ("진단 키: 주변 조명 기록", "4m 안 켜진 조명을 모두 기록합니다."),

		// 봇 인구
		["MaxAliveScavs"] = ("동시 스캐브 최대 수", "동시에 살아 있는 일반 스캐브 수 상한입니다. 0 = 따로 제한 안 함."),
		["MaxAliveBots"] = ("동시 봇 최대 수", "일반 스캐브와 특수 웨이브를 합친 동시 생존 봇 상한입니다. 0 = 제한 없음."),
		["MaxResidentScavs"] = ("스캐브+시체 최대 수", "살아 있는 일반 스캐브와 정리 안 된 스캐브 시체를 합친 상한입니다. 0 = 제한 없음."),
		["FreshProfileWaveInterval"] = ("새 봇 생성 간격(초)", "새 봇 프로필을 만드는 웨이브 사이 최소 간격입니다. 0 = 끔."),
		["LowPopulationWaveInterval"] = ("봇 적을 때 생성 간격(초)", "봇 수가 아래 기준 이하일 때 쓰는 더 짧은 생성 간격입니다."),
		["LowPopulationWaveThreshold"] = ("봇 적음 기준", "살아 있거나 나올 예정인 봇이 이 수 이하이면 짧은 생성 간격을 씁니다."),
		["ScavCorpseCleanup"] = ("시체 정리", "멀리 있는 오래된 봇 시체를 정리해 부담을 줄입니다."),
		["ScavCorpseLifetime"] = ("스캐브 시체 유지(초)", "일반 스캐브 시체를 정리하기 전 최소 유지 시간입니다."),
		["ScavCorpseCleanupDistance"] = ("스캐브 시체 정리 거리(m)", "모든 플레이어에게서 이 거리 밖인 스캐브 시체만 정리합니다. 뒤지는 중인 시체는 사라지지 않습니다."),
		["SpecialCorpseLifetime"] = ("특수 봇 시체 유지(초)", "블랙 디비전·RUAF·시민·보스·PMC 등 시체를 정리하기 전 유지 시간입니다."),
		["SpecialCorpseCleanupDistance"] = ("특수 봇 시체 정리 거리(m)", "모든 플레이어에게서 이 거리 밖인 특수 봇 시체만 정리합니다."),
		["ScavCorpseCleanupPerSweep"] = ("한 번에 정리할 시체 수", "10초마다 정리하는 시체 최대 수입니다."),
		["ScavCorpseCleanupKey"] = ("시체 즉시 정리 키", "거리 조건을 만족하는 시체를 시간과 관계없이 지금 정리합니다."),
		["ScavRecycler"] = ("스캐브 재활용(구버전)", "놀고 있는 스캐브를 다음 웨이브 자리로 옮겨 씁니다. 끄면 새 봇을 만듭니다."),
		["RuafRecycler"] = ("RUAF 재활용(구버전)", "놀고 있는 RUAF를 다음 웨이브 자리로 옮겨 씁니다."),
		["BlackDivisionRecycler"] = ("블랙 디비전 재활용(구버전)", "놀고 있는 블랙 디비전을 다음 웨이브 자리로 옮겨 씁니다."),
		["ScavRecycleMinDistance"] = ("재활용: 원래 자리 거리(m)", "플레이어에게서 이 거리 밖인 봇만 다른 곳으로 옮깁니다."),
		["ScavRecycleDestinationDistance"] = ("재활용: 도착 자리 거리(m)", "플레이어에게서 이 거리 밖인 자리로만 옮깁니다."),
		["ScavRecycleMinAge"] = ("재활용: 최소 대기(초)", "현재 자리에 이 시간 이상 있던 봇만 옮깁니다."),

		// 유령 모드
		["GhostMode"] = ("유령 모드 켜기", "멀리 있는 분대의 AI를 재워 부담을 줄입니다. Fika가 설치되어 있으면 작동하지 않습니다."),
		["GhostSleepDistance"] = ("잠드는 거리(m)", "분대원 전원이 모든 플레이어에게서 이 거리 밖이면 잠듭니다. 기본 125m."),
		["GhostWakeDistance"] = ("깨는 거리(m)", "플레이어가 이 거리 안으로 오면 깹니다. 잠드는 거리보다 작게 적용되며, 50m 안 위협은 바로 전원 깹니다. 기본 100m."),
		["GhostResleepDelay"] = ("다시 잠들기 대기(초)", "깬 뒤 다시 잠들기까지 최소 대기 시간입니다."),
		["GhostOverlay"] = ("상태 표시", "화면 오른쪽 위에 잠든 봇·숨긴 이동·모의 싸움 수를 표시합니다. 테스트용."),
		["GhostBrainThrottle"] = ("잠든 봇 AI 멈춤", "잠든 봇의 AI를 멈춥니다. 끄면 모든 봇을 깨우고 새로 재우지 않습니다."),
		["GhostSainThrottle"] = ("잠든 봇 SAIN 멈춤", "잠든 봇의 SAIN 갱신을 건너뜁니다."),
		["GhostDiagnostics"] = ("진단 기록", "30초마다 잠듦·이동·싸움·기상·비용을 로그에 남깁니다."),
		["GhostCombat"] = ("모의 싸움", "잠든 적대 분대끼리 보이지 않는 곳에서 싸운 결과만 적용합니다."),
		["GhostCombatRange"] = ("모의 싸움 거리(m)", "잠든 분대끼리 싸움이 시작되는 거리입니다. 30m 밖은 벽에 막히면 싸우지 않습니다."),
		["GhostAudio"] = ("모의 싸움 소리", "모의 싸움 총소리를 냅니다(실제 발사·봇 청각 없음)."),
		["GhostAudioRange"] = ("모의 싸움 소리 거리(m)", "플레이어에게서 이 거리 안의 모의 싸움만 소리를 냅니다."),
		["GhostAudioMaxFights"] = ("동시 소리 싸움 수", "동시에 소리를 내는 모의 싸움 수입니다."),
		["GhostRuaf"] = ("재울 대상: RUAF", "RUAF 분대를 재웁니다."),
		["GhostScavs"] = ("재울 대상: 스캐브", "스캐브(저격수 포함)를 재웁니다."),
		["GhostGoons"] = ("재울 대상: 로그 보스 분대", "나이트·빅 파이프·버드아이를 재웁니다."),
		["GhostBosses"] = ("재울 대상: 보스와 부하", "보스와 부하 분대를 함께 재웁니다."),
		["GhostCultists"] = ("재울 대상: 광신도", "광신도를 재웁니다."),
		["GhostRaiders"] = ("재울 대상: 레이더", "레이더·도적을 재웁니다."),
		["GhostBloodhounds"] = ("재울 대상: 블러드하운드", "블러드하운드를 재웁니다."),
		["GhostOthers"] = ("재울 대상: 기타", "PMC·모드 봇 등 기타 세력을 재웁니다. 시민은 재우지 않습니다."),
		["GhostBlackDivision"] = ("재울 대상: 블랙 디비전", "블랙 디비전을 재웁니다."),
		["GhostMovement"] = ("숨긴 이동", "잠든 분대가 숨은 채로 돌진·추격 목적지나 적 분대 쪽으로 이동합니다."),
		["GhostMovementSpeed"] = ("숨긴 이동 속도(m/s)", "숨긴 이동 속도입니다. 적 분대 쪽 배회는 이 속도의 0.4배입니다."),
		["GhostSleepAwake"] = ("깬 적 분대도 재우기", "잠든 분대가 깨어 있는 적대 분대를 만나면, 안전할 때 그 분대도 재워 모의 싸움으로 넘깁니다."),
		["GhostEncounterDistance"] = ("깬 분대와 만남 거리(m)", "잠든 분대와 깨어 있는 적대 분대가 이 거리 안이면 만난 것으로 봅니다."),
		["GhostWakeDelayMs"] = ("기상 간격(ms)", "봇을 하나씩 깨우는 간격입니다. 피격·50m 근접은 바로 전원 깨웁니다. 0 = 지연 없음."),
		["GhostScopeWake"] = ("스코프로 보면 깨우기", "광학 조준 중 시선 안의 잠든 봇을 먼 거리에서도 깨웁니다."),
		["GhostScopeMaxDistance"] = ("스코프 기상 최대 거리(m)", "실제 거리는 깨는 거리 × 현재 배율이며, 이 값을 넘지 않습니다."),
		["GhostFightFrequency"] = ("모의 싸움 빈도", "Low = 낮음, Normal = 보통, High = 높음. 승패·피해에는 관여하지 않습니다."),
		["GhostCombatLethality"] = ("모의 싸움 치명도", "모의 싸움에서 죽는 인원 배율입니다(기본 1.0)."),
	};

	private sealed class Original
	{
		internal ConfigDescription Description;
		internal string Text;
		internal object[] Tags;
		internal object[] KoreanTags;
		internal string KoreanText;
	}

	private static readonly List<Original> Originals = new List<Original>(192);
	private static bool? _korean;
	private static float _nextCheck;

	// Called once after the last Bind: remembers the English text/tags and prepares the
	// Korean set. Nothing is shown in Korean until Tick knows the language.
	// English descriptions for entries whose code-side description was written in Korean
	// (Ghost Mode and the 10/28 switches), so English mode never shows Hangul in F12.
	private static readonly Dictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["GhostMode"] = "Turn Ghost Mode on. Squads far from every player go to sleep to save performance. Does not work with Fika.",
		["GhostSleepDistance"] = "Sleep distance (m). A squad sleeps when every living member is farther than this from every player. Default 125 m.",
		["GhostWakeDistance"] = "Wake distance (m). A sleeping squad wakes when a player comes this close. Applied below the sleep distance; threats within 50 m wake the whole squad at once. Default 100 m.",
		["GhostResleepDelay"] = "Minimum seconds after waking before a squad may sleep again.",
		["GhostOverlay"] = "Show the Ghost Mode status line (sleeping bots, hidden moves, simulated fights) in the top-right corner. For testing.",
		["GhostBrainThrottle"] = "Pause the AI of sleeping bots. Turning it off wakes every bot and stops new sleeps.",
		["GhostSainThrottle"] = "Skip SAIN updates for sleeping bots.",
		["GhostDiagnostics"] = "Log sleep, movement, fight, wake and cost numbers every 30 seconds.",
		["GhostCombat"] = "Simulated fights: sleeping hostile squads fight out of sight and only the result is applied.",
		["GhostCombatRange"] = "Distance (m) at which sleeping squads start a fight. Beyond 30 m a wall in between prevents it.",
		["GhostAudio"] = "Play gunfire for simulated fights (sound only; no real shots, bots do not hear it).",
		["GhostAudioRange"] = "Only simulated fights within this distance (m) of a player make sound.",
		["GhostAudioMaxFights"] = "How many simulated fights may make sound at the same time.",
		["GhostRuaf"] = "Sleep target: RUAF squads.",
		["GhostScavs"] = "Sleep target: scavs (including snipers).",
		["GhostGoons"] = "Sleep target: the Goons (Knight, Big Pipe, Birdeye).",
		["GhostBosses"] = "Sleep target: bosses and their followers (the whole squad together).",
		["GhostCultists"] = "Sleep target: cultists.",
		["GhostRaiders"] = "Sleep target: raiders and rogues.",
		["GhostBloodhounds"] = "Sleep target: Bloodhounds.",
		["GhostOthers"] = "Sleep target: other factions (PMCs, modded bots). Civilians are never put to sleep.",
		["GhostBlackDivision"] = "Sleep target: Black Division.",
		["GhostMovement"] = "Sleeping squads move while hidden: toward crew rush/hunt destinations, or toward a sleeping hostile squad.",
		["GhostMovementSpeed"] = "Hidden movement speed (m/s). Roaming toward an enemy squad uses 0.4x this speed.",
		["GhostSleepAwake"] = "When a sleeping squad meets an awake hostile squad, put that squad to sleep too (when safe) and hand them to a simulated fight.",
		["GhostEncounterDistance"] = "Distance (m) at which a sleeping squad and an awake hostile squad count as meeting.",
		["GhostWakeDelayMs"] = "Delay (ms) between bots when a squad wakes normally. Getting shot or a threat within 50 m wakes everyone at once. 0 = no delay.",
		["GhostScopeWake"] = "Looking through an optic (ADS, more than 1x) wakes sleeping bots inside the view cone at long range.",
		["GhostScopeMaxDistance"] = "Maximum scope wake distance (m). The actual distance is the wake distance times the current zoom, capped here.",
		["GhostFightFrequency"] = "Simulated fight frequency: Low, Normal, High. Only changes how often fights start, not who wins.",
		["GhostCombatLethality"] = "Lethality multiplier for simulated fights (deaths only, default 1.0).",
		["CivilianUpperBodyMask"] = "Civilians holding a knife or nothing get weapon/hand/full-body IK switched off every frame (original behaviour). Applies immediately.",
		["CivilianRetailAnimator"] = "Use the retail civilian animation. Off by default because it makes civilians shake every frame; off = default scav animation. Applies to civilians spawned afterwards.",
		["RuafInvulnerable"] = "RUAF on Terminal ignore all player damage (bullets, grenades, melee): no damage, bleeding or fractures, and they stay neutral. Off = the old behaviour, where shooting one turns every RUAF hostile.",
		["GhostDistanceVersion"] = "Internal: distance-default migration marker.",
	};

	internal static void Apply(ConfigFile config)
	{
		if (TagsField == null || DescriptionField == null)
		{
			Plugin.Log.LogWarning("[ConfigKr] ConfigDescription layout changed; F12 stays English");
			return;
		}
		int named = 0, missing = 0;
		foreach (var pair in config)
		{
			ConfigEntryBase entry = pair.Value;
			ConfigDescription description = entry?.Description;
			if (description == null) continue;
			var tags = new List<object>(4);
			if (Sections.TryGetValue(entry.Definition.Section, out string category)) tags.Add(new CategoryAttribute(category));
			string koreanText = description.Description;
			if (Entries.TryGetValue(entry.Definition.Key, out var text))
			{
				tags.Add(new DisplayNameAttribute(text.Name));
				tags.Add(new DescriptionAttribute(text.Text));
				koreanText = text.Text;
				named++;
			}
			else missing++;
			object[] original = description.Tags ?? new object[0];
			tags.AddRange(original);
			string englishText = English.TryGetValue(entry.Definition.Key, out string english) ? english : description.Description;
			Originals.Add(new Original { Description = description, Text = englishText, Tags = original, KoreanTags = tags.ToArray(), KoreanText = koreanText });
		}
		Plugin.Log.LogInfo($"[ConfigKr] F12 names ready: {named} Korean, {missing} without a name; follows the Terminal language");
	}

	// Plugin.Update: every 2s, switch F12 between Korean and the English originals.
	internal static void Tick()
	{
		if (Originals.Count == 0 || UnityEngine.Time.realtimeSinceStartup < _nextCheck) return;
		_nextCheck = UnityEngine.Time.realtimeSinceStartup + 2f;
		bool korean;
		try { korean = TerminalLang.IsKorean; }
		catch { return; }
		if (_korean == korean) return;
		_korean = korean;
		foreach (Original item in Originals)
		{
			TagsField.SetValue(item.Description, korean ? item.KoreanTags : item.Tags);
			DescriptionField.SetValue(item.Description, korean ? item.KoreanText : item.Text);
		}
		Plugin.Log.LogInfo("[ConfigKr] F12 shown in " + (korean ? "Korean" : "English"));
	}
}
