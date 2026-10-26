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
