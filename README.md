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

## 배포

- 최신: [zzap v1 릴리즈 노트](releases/zzap-v1.md) — 원작 0.2.20 위에 덮어쓰는 빌드, 디스코드·아카라이브 배포
- 유령 모드 코드는 [ORBIT](https://github.com/Chazut/ORBIT) 모드를 참고했습니다. manimal1920님과 chazut님에게 찬사를 보내며, 그들에게 공을 올리고 싶습니다. 후원: [manimal1920](https://ko-fi.com/manimal1920) · [chazut](https://ko-fi.com/chazut)

## 변경 이력

- 2026-10-28 22:12 — zzap: F12 설정과 유령 모드 상태 줄이 언어를 따릅니다. 게임 언어가 한국어면 한글, 아니면 영어로 나옵니다(F12 터미널 > 언어로 직접 정할 수 있음). 배포 빌드를 9ebee69로 다시 만들고, 깃허브 노트(한·영)·디스코드용·아카라이브용 글과 압축 안 영어 안내문을 넣었습니다. 유령 모드는 ORBIT 모드를 참고했으며 manimal1920님과 chazut님께 감사를 표합니다(빌드 확인, 인게임 미검증).

전체 변경 이력 29개는 [`changelog/`](changelog/) 폴더에 10개씩 나눠 있습니다 (번호가 클수록 최신, 지금 마지막 파일: [`CHANGELOG-003.md`](changelog/CHANGELOG-003.md)).

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
