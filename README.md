# ManimalTerminal (zzap 부트렉)

---

### ⚠️ IMPORTANT NOTICE / DISCLAIMER

- **Original Author:** Manimal (danauraborealis)
- **Original Repository:** ManimalTerminal
- **Original Link:** https://github.com/danauraborealis/ManimalTerminal
- **License:** MIT

1. **Reflection & Take-Downs:** I deeply reflect on the ECOT incident. As an AI-assisted "vibe coder," I will immediately delete files if the original authors ask.
2. **No Re-Distribution:** These ported builds are unverified, temporary fixes. Please do NOT re-upload or share them anywhere else.
3. **Do Not Pester Original Authors:** Never report bugs or pester original modders regarding issues from my unofficial ports.
4. **Full Credit & Respect:** I will always credit original creators on GitHub and prioritize their decisions above all else.
5. **Support Original Creators:** Instead of using my ports, please visit the original authors' Forge pages to leave kind words or tips.

---

## 변경 이력

- 2026-10-26 23:32 — 엔딩 후 게임이 안 끝나던 문제, RUAF 쌀먹, 음향 준비 멈춤, 자막 크기.
  - 포격이 켜진 채 레이드가 끝나면 게임의 포격 소리 정리 코드가 "목록이 바뀌었다"는 오류로 멈추고, 그 뒤 월드 정리가 끊겨 엔딩 화면/메뉴로 넘어가지 못했습니다. 터미널에서는 그 오류만 삼켜 정리가 끝까지 진행되게 했습니다.
  - RUAF는 "먼저 피를 보기 전까지 중립"이 원래 설계인데, 적 등록을 허용하는 사유가 좁아서 쏘거나 죽여도 끝까지 반응하지 않았습니다(무장 쌀먹 가능). 이제 RUAF에게 피해를 준 사람은 그 레이드 동안 모든 RUAF 분대의 적이 됩니다.
  - 음향 준비 첫 시도가 6초 넘게 멈추던 것: 짧은 이름의 게임 클래스를 이름마다 모든 모드 DLL에서 다시 찾던 것을, 한 번 만든 이름 목록에서 찾게 했습니다.
  - 자막: 글자 크기를 F12 SubtitleSize(기본 1.4배)로 키우고, 상자 크기를 재기 전에 글자를 먼저 준비해 상자가 글자보다 작게 나오던 것을 막았습니다. 상자는 아래쪽 기준으로 위로 커집니다. 로그에 쓸 수 있는 한글 글꼴 이름을 남깁니다.
  - 시민 진단 보강: 더 작은 흔들림(0.45m 이상)과 두뇌 레이어가 짧은 시간에 왔다 갔다 하는 것도 `[CivJump]`로 기록합니다.
- 2026-10-26 23:09 — 자막 글꼴·상자, 음향 준비 멈춤, 시민 진단.
  - 한국어 자막이 투박하고 글자가 상자 오른쪽으로 삐져나오던 것: 글꼴을 Noto Sans KR(없으면 맑은 고딕)로 바꾸고, F12 → Terminal → KoreanFont에서 설치된 다른 글꼴로 바꿀 수 있게 했습니다. 상자 너비를 잴 때 굵은 화자 이름(헌병: 등)을 빼고 재던 것을 고쳐 글자가 상자 안에 들어갑니다.
  - 레이드 시작 뒤 음향 준비 첫 시도에서 7초 넘게 멈추던 것: 맵 전체 오브젝트 약 30만 개의 경로 사전을 시도마다 두 번씩 새로 만들던 것을, 필요한 경로만 이름을 따라 찾아가게 바꿨습니다(결과는 예전과 같음 — 무작위 트리 9,235개 경로 비교에서 차이 0).
  - 시민이 버벅이며 순간이동하는 현상: 로그만으로는 원인을 알 수 없어, 시민이 한 프레임에 비정상적으로 멀리 움직이는 순간을 기록하는 진단(`[CivJump]` — 그 순간의 두뇌 레이어·행동·이동·애니메이션 상태)을 넣었습니다. 아무것도 바꾸지 않고 기록만 합니다(F12 → CivilianJumpProbe).
- 2026-10-26 22:33 — 한글화. 게임 언어가 한국어면 터미널의 컷신 자막(33줄)·확성기 방송·알림·상호작용 버튼·엔딩 화면이 한국어로 나옵니다. 퀘스트 14개(추락한 하늘 1~7부, 티켓 1~7부)·업적·등대지기 대화·맵 소개·보관함 이름도 한국어입니다. 관련 아이템 13종(프라퍼의 편지, 장갑 케이스, 증거 폴더 등)은 Content Backport에 한국어가 없어 터미널 서버가 대신 넣습니다. 자막과 엔딩 화면은 원래 글꼴(Bender)에 한글이 없어 한국어일 때 윈도우 기본 한글 글꼴(맑은 고딕)로 그립니다. F12 → Terminal → Language(Auto/Korean/English)로 바꿀 수 있습니다. 번역 표(`plugin-data/lang/kr.json`)는 영어 원문을 열쇠로 쓰기 때문에, 표에 없는 문구는 영어로 남아 깨지지 않습니다(인게임 미확인).
- 2026-10-26 22:19 — 터미널 시작 멈춤과 레이드 중 끊김 줄이기(1차). 10/26 실제 레이드 로그(시작 직후 한 프레임 17.8초, 그 뒤 10초짜리 3번, 100ms 넘는 프레임 142번) 기준으로 고쳤습니다(인게임 재측정 전).
  - 레이드 시작 때 관문·크레인·탈출구·포격·물 준비가 한 프레임에 몰려 7초 넘게 멈추던 것: 게임 클래스를 이름으로 찾을 때마다 모든 모드 DLL을 뒤지던 것을 바로 찾고 기억하게 했습니다. 다섯 준비 작업이 각자 맵 전체를 뒤지던 것을 한 번만 뒤지게 했고, 한 프레임에 8ms를 넘기면 나머지는 다음 프레임으로 넘깁니다. 크레인은 맵 전체 셰이더 재연결 대신 크레인 부분만, 물은 텍스처 검색 한 번만 합니다. 늦은 장면 정리(1.1초)는 프레임마다 3ms씩 나눠 합니다.
  - 음향 준비가 시도마다 약 10초씩 멈추던 것: 같은 "이름으로 클래스 찾기"가 원인이었습니다. Fika 없이 Fika용 부품이 깔린 모드가 있으면 이 검색이 매번 예외를 냅니다. 이제 터미널 전체가 빠른 공용 찾기 도구를 씁니다.
  - 다른 맵에도 영향을 주던 것: 가로등마다 매 프레임 씬 이름을 새로 만들던 것, 문을 볼 때마다 꺼져 있는 문 진단이 메모리를 쓰던 것, 스위치를 볼 때마다 씬 이름을 확인하던 것, 총이 발사될 때마다 민간인 확인을 하던 것을 고쳤습니다(진단은 설정을 켤 때만 설치).
  - 레이드 중: 봇이 스폰·사망할 때마다 조명 약 1,900개를 다시 훑던 그림자 감시를 최소 5초 간격으로, 매 프레임 스코프 카메라를 찾던 것을 0.5초 간격으로 바꿨습니다. 격납고 봇 첫 판단 때 맵 전체를 뒤지던 것은 미리 나눠서 계산해 둡니다. RUAF 판정은 역할 이름을 매번 만들지 않게, 출혈 목록 정리는 매 프레임 리플렉션 조회를 하지 않게 했습니다.
  - 렌더 계측·경로 진단은 개발 모드(DevMode)일 때만 켜집니다.
  - 참고: 20초마다 약 1초씩 끊기던 것은 예전 설정 파일에 켜져 있던 진단 기능(WorldDiff·TraceFrameCycle·ProfilePlayerLoop, 기본값은 꺼짐) 때문이었습니다. 설정에서 끄면 사라집니다.
- 2026-10-26 20:35 — SVM(서버 설정 모드)의 "커스텀 주머니"를 쓰면 프라퍼의 편지(항구 검문소용)를 갖고 있어도 터미널에 "물건이 없다"며 입장이 안 되던 문제를 고침. 편지는 특수 슬롯에만 들어가는 아이템인데, 터미널 서버는 기본 주머니와 언허드 주머니의 특수 슬롯에만 편지 자리를 열어 줬습니다. SVM은 기본 주머니를 복제해 새 주머니를 만들기 때문에, 그 주머니에는 편지가 들어갈 자리가 없었습니다. 이제 주머니 종류인 아이템이면 전부 편지를 허용합니다. 다른 모드가 나중에 주머니를 추가하는 경우에 대비해, 모든 모드가 로드된 뒤 한 번 더 확인합니다(인게임 미확인).
- 2026-10-26 20:12 — LootInstantSearch(즉시 수색 모드)와 같이 쓰면 레이드 중 게임이 튕기던 문제를 고침. 터미널의 "보관함 장비 수색 표시" 기능(`TerminalCabinetSearch` — 압수된 장비를 보관함에 넣은 뒤 다시 찾을 수 있게 표시하는 부분)이 아이템이 "알려짐"으로 바뀔 때마다 맵과 상관없이 "이 아이템을 아는지" 다시 물어봤습니다. 그런데 즉시 수색 모드는 그 질문을 받으면 다시 "알려짐" 처리를 해서, 두 모드가 서로를 끝없이 불러 튕겼습니다. 이제 보관함에 넣은 그 장비일 때만 확인하고(그래서 다른 맵에서는 아무 일도 하지 않음), 확인하는 동안 다시 불려 와도 한 번 더 들어가지 않습니다. 두 모드 흐름을 흉내 낸 모의 실행에서 수정 전은 끝나지 않고 수정 후는 바로 끝나는 것을 확인했습니다(인게임 미확인).
- 2026-10-26 20:08 — 기준 소스를 원작 릴리즈 0.2.20으로 바꿈. 원작자가 깃허브에 올린 소스(0.2.2, 9월 12일)보다 실제 배포된 0.2.20(10월 16일)이 훨씬 앞서 있었습니다. 새 파일 36개·바뀐 파일 57개(전투 연출, 블랙디비전 로직, 봇 이동, 임무 날씨, 계절 음향 등) 차이가 있어, 깃허브 소스로 고치면 최신 기능이 사라지는 상태였습니다. 그래서 릴리즈 DLL을 디컴파일(소스로 되돌리기)해서 클라이언트 소스를 통째로 교체했습니다. 다시 빌드한 DLL을 또 디컴파일해 원본과 비교하면 같은 코드가 나옵니다(조건 순서 같은 표현 차이만). 서버는 바뀐 곳이 한 군데(같은 경호원이 두 번 스폰되지 않게 웨이브 인원 정리)뿐이라 원작 주석이 있는 소스에 그 부분만 옮겼습니다. 코드가 읽는 데이터 JSON 8개도 릴리즈와 맞췄습니다. 게임 동작은 원본 0.2.20과 같아야 합니다(인게임 미확인).

## 소개

리테일 타르코프의 **터미널** 맵을 SPT 4.1로 옮겨 오는 원작 모드(Manimal)의 비공식 포크입니다.
이 포크의 목표는 **프레임 최적화**와 **다른 모드와의 호환**입니다.

| 폴더 | 내용 |
|---|---|
| `terminal-client/` | BepInEx 클라이언트 플러그인 (`Manimal-Terminal`) |
| `terminal-server/` | SPT 서버 모드 (터미널 맵 자리를 채움) |
| `terminal-prepatch/` | 민간인 봇 종류를 등록하는 사전 패치 |
| `terminal-fika/` | Fika용 부가 모드 (0.2.2 소스 그대로 — 0.2.20 기준 빌드 미확인) |

## 클라이언트 소스에 대해

- `terminal-client/`의 .cs 파일은 릴리즈 0.2.20 DLL을 ILSpy로 디컴파일한 것이라 **원작 주석이 없습니다.**
  원작 주석이 필요하면 이 저장소 이력에서 0.2.2 시절 파일(커밋 `1d45c1c`)을 참고하세요.
- 0.2.20 클라이언트는 **ZLinq 1.5.3**을 씁니다. 게임에서는 `BepInEx/plugins/UnityToolkit/ZLinq.dll`(WTT 쪽 UnityToolkit)이 제공합니다.

## 필요한 모드

클라이언트: WTT-ClientCommonLib, WTT-ContentBackport, BigBrain, MoreBotsAPI, Black Division, UnityToolkit(ZLinq)
서버: WTT-CommonLib ^3.0.6, WTT-ContentBackport ^2.0.1, Black Division, MoreBotsAPI ≥2.1.1, **RUAF Come Home ≥1.1.0**

## 설치

1. 원작 릴리즈 `Manimal-Terminal-SPT-4.1-0.2.20` 전체(맵 번들 약 4GB 포함)를 게임 폴더에 먼저 설치합니다.
2. 이 포크의 빌드는 그 위에 `BepInEx/plugins/ManimalTerminal/ManimalTerminalClient.dll`(필요하면 서버 `terminal-server.dll`도)만 덮어씁니다.

맵 번들과 큰 음향 파일(계절 음향 WAV 등)은 용량 때문에 저장소에 넣지 않았습니다.

## 빌드

```
dotnet build terminal-client/terminal-client.csproj -c Release
dotnet build terminal-server/terminal-server.csproj -c Release
```

게임 경로는 `Directory.Build.props`의 `SPTPath`(기본 `E:\SPT 4.1`)입니다. 빌드는 게임 폴더에 자동으로 복사하지 않습니다(`SkipDeploy=true`).

## 라이선스

원작과 같이 MIT입니다(`LICENSE`).
