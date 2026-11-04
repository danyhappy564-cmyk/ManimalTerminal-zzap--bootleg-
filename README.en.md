<p align="center">
  <a href="README.md"><img alt="한국어" src="https://img.shields.io/badge/%ED%95%9C%EA%B5%AD%EC%96%B4-%ED%95%9C%EA%B5%AD%EC%96%B4%EB%A1%9C%20%EB%B3%B4%EA%B8%B0-6e7681?style=for-the-badge"></a>
  <a href="README.en.md"><img alt="English (this page)" src="https://img.shields.io/badge/English-This%20page-1f6feb?style=for-the-badge"></a>
</p>

# ManimalTerminal (zzap bootleg)

---

### ⚠️ IMPORTANT NOTICE / DISCLAIMER

- **Original Author:** Manimal (danauraborealis)
- **Original Repository:** ManimalTerminal
- **Original Link:** https://github.com/danauraborealis/ManimalTerminal
- **License:** MIT

- **Ghost Mode reference:** ORBIT by Chazut — the Ghost Mode (sleeping distant squads, hidden movement, simulated fights) was built with reference to ORBIT.
- **ORBIT Repository:** https://github.com/Chazut/ORBIT
- **ORBIT License:** MIT — Copyright (c) 2026 Chazu (full text: [`licenses/ORBIT-LICENSE.txt`](licenses/ORBIT-LICENSE.txt)). Thank you to the author for kindly confirming it can be used.

1. **Reflection & Take-Downs:** I deeply reflect on the ECOT incident. As an AI-assisted "vibe coder," I will immediately delete files if the original authors ask.
2. **No Re-Distribution:** These ported builds are unverified, temporary fixes. Please do NOT re-upload or share them anywhere else.
3. **Do Not Pester Original Authors:** Never report bugs or pester original modders regarding issues from my unofficial ports.
4. **Full Credit & Respect:** I will always credit original creators on GitHub and prioritize their decisions above all else.
5. **Support Original Creators:** Instead of using my ports, please visit the original authors' Forge pages to leave kind words or tips.

---

## Distribution

- **Before installing: you must download and install the original Manimal Terminal 0.2.20 first, then extract this archive over it for it to work** (this archive does not include map bundles). Original: https://pub-38fba86cac60421ebd03a218d2535c13.r2.dev/Manimal-Terminal-SPT-4.1-0.2.20.zip
  (Install the original Manimal Terminal 0.2.20 first, then extract this build over it - it has no map bundles.)
- Latest: [zzap v2 release notes](release-notes/v2.md) — fixes a menu memory leak after finishing a different map (install over the original 0.2.20) · Previous: [zzap v1](release-notes/v1.md)
- Ghost Mode code was built with reference to [ORBIT](https://github.com/Chazut/ORBIT). Praise and credit go to manimal1920 and chazut. Donate: [manimal1920](https://ko-fi.com/manimal1920) · [chazut](https://ko-fi.com/chazut)

## Changelog

The full changelog (32 entries) is in the [`changelog/`](changelog/) folder, 10 per file (higher number = newer, latest: [`CHANGELOG-004.md`](changelog/CHANGELOG-004.md)). Per-release summaries: [Releases](../../releases).

## Introduction

An unofficial fork of Manimal's original mod, which brings the **Terminal** map from retail Tarkov to SPT 4.1.
This fork focuses on **frame rate optimization** and **compatibility with other mods**.

| Folder | Contents |
|---|---|
| `terminal-client/` | BepInEx client plugin (`Manimal-Terminal`) |
| `terminal-server/` | SPT server mod (provides the Terminal map entry) |
| `terminal-prepatch/` | Prepatch that registers the civilian bot type |
| `terminal-fika/` | Fika add-on (unchanged 0.2.2 source — building against 0.2.20 is unverified) |

## About the client source

- The .cs files in `terminal-client/` were decompiled from the release 0.2.20 DLL using ILSpy, so **the original comments are missing.**
  For the original comments, see the 0.2.2 files in this repository's history (commit `1d45c1c`).
- The 0.2.20 client uses **ZLinq 1.5.3**. In-game, it is supplied by `BepInEx/plugins/UnityToolkit/ZLinq.dll` (UnityToolkit from WTT).

## Required mods

Client: WTT-ClientCommonLib, WTT-ContentBackport, BigBrain, MoreBotsAPI, Black Division, UnityToolkit(ZLinq)
Server: WTT-CommonLib ^3.0.6, WTT-ContentBackport ^2.0.1, Black Division, MoreBotsAPI ≥2.1.1, **RUAF Come Home ≥1.1.0**

## Installation

1. First install the entire original `Manimal-Terminal-SPT-4.1-0.2.20` release (including about 4GB of map bundles) into the game folder.
2. Install this fork's build over it, replacing only `BepInEx/plugins/ManimalTerminal/ManimalTerminalClient.dll` (and the server's `terminal-server.dll` if needed).

Map bundles and large audio files (such as seasonal ambience WAV files) are omitted from the repository due to their size.

## Build

```
dotnet build terminal-client/terminal-client.csproj -c Release
dotnet build terminal-server/terminal-server.csproj -c Release
```

The game path is `SPTPath` in `Directory.Build.props` (default: `E:\SPT 4.1`). Builds do not automatically copy files into the game folder (`SkipDeploy=true`).

## License

MIT, like the original (`LICENSE`).
