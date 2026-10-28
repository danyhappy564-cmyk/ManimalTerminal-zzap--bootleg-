<!-- 디스코드용: 메시지 2개(한국어 / English). 각 메시지는 아래 "=== 메시지" 줄 사이만 복사해서 붙여 넣으세요. 각각 2,000자 안. zip은 첫 메시지에 첨부. -->

=== 메시지 1 (한국어) ===

## Manimal Terminal 0.2.20 zzap v1
비공식 수정 빌드 · 원작 **Manimal Terminal 0.2.20** 위에 덮어쓰기 · 원작자분께 문의하지 말아 주세요.

**무엇이 좋아졌나** (개발자 PC, 터미널 한 판 기준)
- 레이드 시작 준비 멈춤 7.2초 → 2.4초, 1초 넘는 멈춤 분당 2.7 → 0.8회
- 터미널 모드 때문에 생긴 끊김 판마다 약 20회 → 0~5회
- 시민 떨림 사라짐 (100프레임당 87~99회 → 0.4~0.7회)

**바뀐 점**
- 한글화: 자막·방송·알림·버튼·엔딩·퀘스트·대화·업적·아이템
- F12 설정 한글 표시 (게임 언어가 한국어가 아니면 영어)
- LootInstantSearch 같이 쓰면 튕기던 문제, SVM 주머니 프라퍼 편지 문제 수정
- RUAF: 플레이어 공격에 무적, 계속 중립 (F12에서 끌 수 있음)
- 뒤로 걸어오는 이벤트 스캐브, 안 보이는 봇, 시민 순간이동 수정
- 시민 전용 애니메이션 기본 꺼짐 (떨림 원인) → 시민이 주먹 쥔 자세로 보임

**유령 모드 (기본 켜짐)**
125m 밖 분대를 재우고 100m 안에서 깨움. 잠든 적 분대끼리 숨은 채 다가가 모의로 싸우고 멀리서 총소리가 납니다. Fika에서는 작동 안 함.
끄기: F12 → Manimal-Terminal → 터미널 · 유령 모드 → 유령 모드 켜기 해제

**설치**: 게임·서버 끄기 → `BepInEx\plugins\ManimalTerminal`, `SPT_Runtime\user\mods\ManimalTerminal` 백업 → 압축 안 두 폴더를 SPT 폴더에 덮어쓰기
문제 생기면 `BepInEx\LogOutput.log` 같이 보내 주세요.

**감사의 말**
유령 모드 코드는 ORBIT 모드를 참고했습니다 <https://github.com/Chazut/ORBIT>
manimal1920님과 chazut님에게 찬사를 보내며, 그들에게 공을 올리고 싶습니다.
manimal1920 후원: <https://ko-fi.com/manimal1920>
chazut 후원: <https://ko-fi.com/chazut>

=== 메시지 2 (English) ===

## Manimal Terminal 0.2.20 zzap v1
Unofficial patch build · drop-in overwrite for the original **Manimal Terminal 0.2.20** · please don't contact the original author about it.

**What got better** (developer PC, one Terminal raid per sample)
- Raid-start setup freeze 7.2 s → 2.4 s; freezes over 1 s 2.7/min → 0.8/min
- Hitches caused by the Terminal mod ~20 per raid → 0–5
- Civilian shaking gone (87–99 head flips per 100 frames → 0.4–0.7)

**Changes**
- F12 settings follow the language: English unless the game is in Korean (Korean localization included)
- Fixed the crash with LootInstantSearch and SVM custom pockets refusing Prapor's letter
- RUAF: invulnerable to player damage and stay neutral (can be turned off in F12)
- Fixed backwards-walking event scavs, invisible bots, teleporting civilians
- Civilian retail animation off by default (it caused the shaking) → civilians use the scav stance

**Ghost Mode (on by default)**
Squads farther than 125 m sleep and wake within 100 m. Sleeping hostile squads close in while hidden and fight a simulated battle you hear from afar. Not for Fika.
Turn off: F12 → Manimal-Terminal → Terminal · Ghost Mode → untick Ghost Mode on

**Install**: close game and server → back up `BepInEx\plugins\ManimalTerminal` and `SPT_Runtime\user\mods\ManimalTerminal` → copy both folders from the zip into your SPT folder and overwrite.
Please attach `BepInEx\LogOutput.log` to bug reports.

**Credits**
Ghost Mode code was based on the ORBIT mod <https://github.com/Chazut/ORBIT>
All praise to manimal1920 and chazut — the credit belongs to them.
Support manimal1920: <https://ko-fi.com/manimal1920>
Support chazut: <https://ko-fi.com/chazut>
