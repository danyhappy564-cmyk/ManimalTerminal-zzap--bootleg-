# Manimal Terminal 0.2.20 zzap v1

[한국어](#한국어) · [English](#english)

---

## 한국어

> 2026-10-28 · 비공식 수정 빌드 · 원작 Manimal Terminal 0.2.20 위에 덮어쓰기
> 배포: 디스코드 · 아카라이브 (SPT 포지에는 올리지 않습니다)
> 원작자 버전이 아니니 원작자분께 문의하지 말아 주세요.

배포 파일: `Manimal-Terminal-0.2.20-zzap-v1.zip`

### 감사의 말

유령 모드 코드는 [ORBIT](https://github.com/Chazut/ORBIT) 모드를 참고했습니다.

manimal1920님과 chazut님에게 찬사를 보내며, 그들에게 공을 올리고 싶습니다.

- manimal1920 후원: https://ko-fi.com/manimal1920
- chazut 후원: https://ko-fi.com/chazut

### 무엇이 좋아졌나

| 항목 | 전 | 후 |
|---|---|---|
| 레이드 시작 준비 멈춤 | 7.2초 | 2.4초 (나눠서 처리) |
| 1초 넘는 멈춤 | 분당 2.7회 | 분당 0.8회 |
| 터미널 모드 때문에 생긴 끊김 | 판마다 약 20회 | 0~5회 |
| 유령 모드 켠 판 평균 프레임 | 62fps (1% low 34) | 65fps (1% low 43~48) |
| 시민 떨림 (100프레임당 머리 흔들림) | 87~99회 | 0.4~0.7회 (다른 봇과 같음) |
| 모의 싸움 (유령 모드) | 0회 | 한 판 6~13회 |

※ 개발자 PC에서 터미널을 한 판씩 돌려 잰 값입니다. 사양·모드 구성에 따라 다릅니다.

### 바뀐 점

**한글화**
- 컷신 자막, 확성기 방송, 알림, 상호작용 버튼, 엔딩 화면, 퀘스트, 대화, 업적, 아이템을 한국어로 표시합니다(게임 언어가 한국어일 때).
- F12 설정 이름과 설명이 한글로 나옵니다. 설정 키는 그대로라 기존 저장값이 이어집니다.
- 게임 언어가 한국어가 아니면 F12와 유령 모드 상태 줄이 영어로 나옵니다. F12 → 터미널 → "언어"로 직접 정할 수도 있습니다(Auto = 게임 언어).

**안정성·성능**
- 레이드 시작·중간 렉을 줄이고, 레이드가 끝나면 메모리를 바로 돌려줍니다.
- LootInstantSearch(즉시 수색)와 같이 쓰면 게임이 튕기던 문제를 고쳤습니다.
- SVM 커스텀 주머니에서 프라퍼 편지가 특수 슬롯에 들어가지 않아 입장이 막히던 문제를 고쳤습니다.

**봇**
- RUAF는 터미널에서 플레이어의 총·수류탄 등 모든 공격에 무적이고 계속 중립입니다. F12 → 터미널 → "RUAF 무적(플레이어 피해)"를 끄면 예전처럼 쏘는 순간 RUAF 전체가 적이 됩니다.
- 이벤트 스캐브가 뒤를 보고 걸어오던 문제, 등 뒤에서 맞아도 돌아보지 않던 문제를 고쳤습니다.
- 가까이 가도 안 보이다가 죽어야 보이던 봇을 고쳤습니다(게임 자체의 먼 봇 숨김 장치와 충돌).
- 시민이 몇 미터씩 순간이동하던 문제를 고쳤습니다.
- 시민이 제자리에서 덜덜 떨던 문제는 원인이 시민 전용 애니메이션이라 기본으로 껐습니다. 대신 시민이 스캐브 기본 자세(주먹 쥔 자세)로 보입니다. F12 → 터미널 → "시민: 전용 애니메이션"으로 다시 켤 수 있지만 떨림이 돌아옵니다.

### 유령 모드 (기본 켜짐)

플레이어에게서 먼 분대를 재워 부담을 줄이는 기능입니다.

- 모든 플레이어에게서 125m 밖이면 잠들고, 100m 안으로 오면 깹니다. 맞거나 스코프로 보면 바로 깹니다.
- 잠든 적 분대끼리는 숨은 채로 서로 다가가 보이지 않는 곳에서 모의로 싸우고, 멀리서 총소리가 납니다.
- 끄는 법: F12 → Manimal-Terminal → 터미널 · 유령 모드 → "유령 모드 켜기"를 끕니다.
- 화면 오른쪽 위에 상태 줄이 뜹니다. 같은 곳의 "상태 표시"를 끄면 사라집니다.
- Fika(멀티)를 쓰면 작동하지 않습니다.

### 필요한 것

- Manimal Terminal **0.2.20 원작 릴리즈**가 이미 설치되어 있어야 합니다.
- 예전 zzap 테스트 빌드를 쓰고 있었다면 그 위에 그대로 덮어쓰면 됩니다.

### 설치

1. 게임과 서버를 끕니다.
2. 혹시 몰라 아래 두 폴더를 다른 곳에 복사해 백업합니다.
   - `BepInEx\plugins\ManimalTerminal`
   - `SPT_Runtime\user\mods\ManimalTerminal`
3. 압축 안의 `BepInEx`, `SPT_Runtime` 폴더를 SPT 설치 폴더(`EscapeFromTarkov.exe`가 있는 곳)에 그대로 덮어씁니다.

### 들어 있는 파일 (원작 0.2.20 대비 바뀐 8개)

| 파일 | 내용 |
|---|---|
| `BepInEx/plugins/ManimalTerminal/ManimalTerminalClient.dll` | 터미널 모드 본체 (SHA256 앞 `ab144db3c17d3b23`) |
| `BepInEx/plugins/ManimalTerminal/plugin-data/lang/kr.json` | 자막·알림·버튼 한국어 |
| `BepInEx/plugins/ManimalTerminal/plugin-data/epilogue/terminal_epilogue.kr.json` | 엔딩 화면 한국어 |
| `SPT_Runtime/user/mods/ManimalTerminal/terminal-server.dll` | 서버 모드 (SHA256 앞 `c34ec80d8cd30a03`) |
| `SPT_Runtime/user/mods/ManimalTerminal/db/locales/kr.json` | 아이템·이름 한국어 |
| `SPT_Runtime/user/mods/ManimalTerminal/db/CustomQuests/prapor/Locales/kr.json` | 퀘스트 한국어 |
| `SPT_Runtime/user/mods/ManimalTerminal/db/CustomAchievements/Locales/kr.json` | 업적 한국어 |
| `SPT_Runtime/user/mods/ManimalTerminal/db/CustomDialogues/TheTicketLightkeeperDialogue.json` | 등대지기 대화 한국어 |

맵 번들 등 나머지는 원작 그대로 씁니다.

### 알려진 문제

- 봇이 처음 나타나는 순간 잠깐 끊길 수 있습니다(게임 자체의 봇 초기화).
- 시민 전용 애니메이션을 다시 켜면 시민이 떨립니다.
- 레이드 종료 때 게임 로그에 `BaseLocalGame.CleanUp` 오류가 남습니다(원인 미확정, 플레이 영향 없음).

### 문제가 생기면

- 백업한 두 폴더로 되돌리면 원래대로 돌아갑니다.
- `BepInEx\LogOutput.log` 파일을 같이 보내 주시면 원인을 찾기 쉽습니다.

---

## English

> 2026-10-28 · Unofficial patch build · Drop-in overwrite for Manimal Terminal 0.2.20
> Shared on Discord and Arca.live only (not on the SPT Forge)
> This is NOT the original author's build. Please do not contact the original author about it.

File: `Manimal-Terminal-0.2.20-zzap-v1.zip`

### Credits

The Ghost Mode code was based on the [ORBIT](https://github.com/Chazut/ORBIT) mod.

All praise to manimal1920 and chazut — the credit belongs to them.

- Support manimal1920: https://ko-fi.com/manimal1920
- Support chazut: https://ko-fi.com/chazut

### What got better

| Item | Before | After |
|---|---|---|
| Raid-start setup freeze | 7.2 s | 2.4 s (spread over frames) |
| Freezes longer than 1 s | 2.7 per minute | 0.8 per minute |
| Hitches caused by the Terminal mod | ~20 per raid | 0–5 per raid |
| Average FPS with Ghost Mode on | 62 fps (1% low 34) | 65 fps (1% low 43–48) |
| Civilian shaking (head flips per 100 frames) | 87–99 | 0.4–0.7 (same as other bots) |
| Simulated fights (Ghost Mode) | 0 | 6–13 per raid |

Measured on the developer's PC, one Terminal raid per sample. Your numbers will depend on hardware and mod list.

### Changes

**Korean localization**
- Cutscene subtitles, PA announcements, notifications, interaction prompts, ending screen, quests, dialogue, achievements and items show in Korean when the game language is Korean.
- F12 settings show Korean names and descriptions when playing in Korean, and English otherwise. Setting keys are unchanged, so your saved values carry over.
- English mode: F12 and the Ghost Mode status line are in English unless the game language is Korean. You can force it with F12 → Terminal → "Language" (Auto = follow the game).

**Stability and performance**
- Less stutter at raid start and mid-raid; memory is released right after the raid ends.
- Fixed a crash when used together with LootInstantSearch.
- Fixed SVM custom pockets refusing Prapor's letter in the special slot (which blocked entry to Terminal).

**Bots**
- RUAF are invulnerable to all player damage on Terminal (bullets, grenades, melee) and stay neutral. Turn off F12 → Terminal → "RUAF invulnerable (player damage)" to get the old behaviour, where shooting one turns all RUAF hostile.
- Fixed event scavs walking toward you backwards and not turning around when shot from behind.
- Fixed bots that stayed invisible up close and only appeared when killed (a clash with the game's own distant-body culling).
- Fixed civilians teleporting a few metres at a time.
- Civilians shaking in place: the cause is the civilian-specific animation, so it is now off by default. Civilians use the default scav stance (fists up) instead. You can turn it back on under F12 → Terminal → "Civilian: retail animation", but the shaking returns.

### Ghost Mode (on by default)

Puts squads far from every player to sleep to save performance.

- Squads sleep when all members are more than 125 m from every player and wake within 100 m. Getting shot or being looked at through a scope wakes them immediately.
- Sleeping hostile squads walk toward each other while hidden and fight a simulated battle out of sight; you hear the gunfire from afar.
- To turn it off: F12 → Manimal-Terminal → Terminal · Ghost Mode → untick "Ghost Mode on".
- A status line appears in the top-right corner. Turn off "Status display" in the same section to hide it.
- Does not work with Fika (multiplayer).

### Requirements

- The original **Manimal Terminal 0.2.20** release must already be installed.
- If you were using an earlier zzap test build, just overwrite it.

### Install

1. Close the game and the server.
2. Back up these two folders somewhere else, just in case:
   - `BepInEx\plugins\ManimalTerminal`
   - `SPT_Runtime\user\mods\ManimalTerminal`
3. Copy the `BepInEx` and `SPT_Runtime` folders from the zip into your SPT folder (where `EscapeFromTarkov.exe` is) and overwrite.

### Files (the 8 that differ from the original 0.2.20)

Client DLL (`ab144db3c17d3b23`), two client Korean data files, server DLL (`c34ec80d8cd30a03`), and four server Korean data files (locales, quests, achievements, Lightkeeper dialogue). Everything else, including the map bundles, comes from the original release.

### Known issues

- A brief hitch can happen the moment a bot first appears (the game's own bot initialization).
- Turning the civilian retail animation back on brings the shaking back.
- An `BaseLocalGame.CleanUp` error is logged at raid end (cause unknown, no gameplay effect).

### If something goes wrong

- Restore the two folders you backed up.
- Please send `BepInEx\LogOutput.log` along with your report.

Full change history (Korean): [`changelog/`](../changelog/)
