# EV-DOC-BASELINE — 2026-08-26 승인 문서 기준선 r06

## 1. 증거 정보

| 항목 | 값 |
|---|---|
| Evidence ID | EV-DOC-BASELINE-20260826-r06 |
| 범위 | 현행 문서 12개, Minimal Match UI, Match Esc, Leave/Disconnect/Forfeit, Alpha 품질, Toolchain/Foundation, Plan·Trace |
| 실행 | Read-only 문서 parser·SHA-256, 독립 의미 감사, 독립 WBS/의존성 감사 |
| 실행하지 않음 | 구현, Unity 실행·Player Build, Steam 실행·배포, Docker·외부 서비스 |
| 상태 | DOC-001..005 PASSED, `UG-DOC` PASSED |

## 2. 승인 계약

- Alpha Match에는 persistent timer·alive·ammo·killfeed·result panel이 없다. Player surface는 transient
  3·2·1, Round 사이 평문 Patch, OpponentLeft·HostLoss·error와 on-demand Tab score+active Patch뿐이다.
  Ammo·FireMode·Projectile·Supply는 developer debug 전용이다.
- Match Esc menu는 local-only이며 Authority simulation·Round timer·Hazard·외부 physics를 멈추지 않는다.
  Local gameplay Input만 neutral이고 닫은 뒤 모든 Mouse button up을 확인해야 Hand·Weapon 입력이 재무장된다.
- Match의 explicit Guest Leave는 즉시 Forfeit다. Unexpected disconnect는 30초 동안 neutral input의
  physical·vulnerable Character·Alive Camera subject를 유지하며 Down·탈락할 수 있다. Reconnect는 Host의
  현재 Alive/Spectator와 전체 권한 상태로 원자 복원하고 timeout은 Forfeit다.
- Forfeit는 PatchAuthor가 아니다. Permanent participant가 2명 이상이면 계속하고 1명이면 score·Patch 없이
  OpponentLeft 뒤 같은 Lobby로 돌아간다. Host Leave/Loss는 Host Migration 없이 Session을 종료한다.
- Alpha 표현은 InteractiveLobby Greybox, EyeSet·Mustache·Headwear 각 대표 placeholder 1개 또는 동등 최소
  catalog, Korean-only, BGM 0과 고정 개발 mix의 basic combat·weapon·environment SFX로 제한한다.
- Alpha Setting은 Tab Hold/Toggle만 직접 소유한다. Key rebind, cursor/UI scale, shake/motion/effect,
  subtitle, 별도 color-vision, Patch review와 audio volume은 post-Alpha 재분할이다.
- Production Lobby, full Cosmetic, English/StringTable/font fallback, music·key-help와 확장 설정은
  post-Alpha다. UI·연출 완성도를 Alpha 기능 Gate로 사용하지 않는다.
- Foundation 기준은 Unity 6.3 LTS와 Blender 5.2 LTS다. Exact installed patch·Unity package lock,
  repository binary/LFS, art/interoperability profile, license/NOTICE와 사용자 수동 Windows x64 Build Profile은
  각각 FDN-010, FDN-011, ART-001, LIC-001, BLD-001이 소유한다.
- 방장 Unity 프로세스 `AuthorityHost`가 최종 판정한다. 별도 Backend·Coordinator·DB·Blob·Bake Worker,
  별도 Server 프로세스·Dedicated Server·Docker/OCI/Compose/container는 0이다.
- Alpha는 신뢰된 LAN/direct endpoint의 2·3·4인 검증이다. 통과 뒤 Steam auth·friends-only Lobby·invite·code·
  P2P/SDR을 붙이며 공개 matchmaking·server browser·Rank·MMR은 영구 비범위다.
- 기존 승인 Patch12, timed sky Weapon supply, Air Kick/Dropkick, Hybrid Physics+Animation, 네 low-poly Weapon과
  Pistol7·LongGun30 no-reload·Host Projectile·recoil/spread 계약은 그대로 유지한다.

## 3. 현행 문서 Manifest

| 문서 | 버전 | SHA-256 | Lines |
|---|---|---|---:|
| docs/00_DOCUMENT_INDEX.md | 2026-08-26 | 925f658fab12a793efbb18a9929729d9602c499b125b518f26d7963c58ec09ca | 176 |
| docs/01_PRD.md | 1.8.0 | 2e079f53559846ff66d69af90ec139ee6278b95315ad0e31fa38b893a02b805f | 878 |
| docs/02_SRS.md | 1.8.0 | 577bf0f13c819c6658e2582b31241f51a727b5f736904853bdb54a06d6736b39 | 403 |
| docs/PATCH_DESIGN.md | 0.5.0 | 711f832e53e85c4bbc8b02809afb4e8f018cbe1ce29e560fbca0e091048e37e9 | 913 |
| docs/CHARACTER_TECHNICAL_SPEC.md | 0.11.0 | 87979bbaf1cb9c8adcc3886b24302664b81525d489cb56319c80373edbc8bc7c | 641 |
| docs/ART_DIRECTION.md | 1.8.0 | e9cb61e038ce04168243dd7b8f7ae015b3df7ddb77e59b1fa9967164e815a11a | 503 |
| docs/UI_UX_FLOW.md | 1.8.0 | 8d3bec50a939b5cf85a911ddc80e7fbbe1c192ea67d69ec03efa243370a74c4d | 764 |
| docs/MAP_DESIGN_GUIDE.md | 1.8.0 | 4ed341618e7bbf9b98040062e19c59c418f06f9c9a7deef2b0e7a597be8f5ad8 | 543 |
| docs/MAP_P00_CONSTRUCTION_DROP.md | 0.7.0 | cb4a280b1d0c9f23645e14b9b6c876598b70d078236144487da2581fbce2da36 | 711 |
| docs/WEAPON_DESIGN.md | 0.7.0 | 7eaf969179f98c362fef195c9d2da2ad9065e627dbf99c0591c4d190177eb6a7 | 633 |
| docs/03_IMPLEMENTATION_PLAN.md | 2.5 | 88e0f7a7a70d32a1b9452579ad5b051dce237ad03429c06c3600887440841708 | 438 |
| docs/04_IMPLEMENTATION_TRACEABILITY.md | 1.5 | 2c7525ccce59bb4ff3155151f6045fe36d34925b551079f5a8b9d2ab7892da1e | 184 |

## 4. 검증 결과

| 검사 | 결과 |
|---|---|
| Active documents | 12/12 |
| PRD IDs | 31 unique |
| SRS IDs | 186 unique |
| Acceptance IDs | 44 unique |
| Prototype Patch IDs | 12 unique, binding mismatch 0 |
| Plan | 173 unique Task, 251.0 focused days, Task당 0.5~2일 |
| Dependency | missing 0, cycle 0 |
| Alpha ancestry | QA-003..010·UI-007·NET-015·SEC-001·AV-003·BLD-001 우회 0 |
| PRD→SRS→Task Trace | missing·extra 0 |
| AT ownership | missing·duplicate 0 |
| Markdown·local links | 문제 0 |
| Archive·container·구 Backend architecture | 0 |
| 구 active source·용어 | stale reference 0 |
| 독립 의미 감사 | PASS, 잔여 finding 0 |
| 독립 WBS/의존성 감사 | PASS, 잔여 actionable gap 0 |
| 최종 결과 | PASS |

원시 결과는 [validator](verify_doc_baseline.py)와
[raw validation](EV-DOC-RAW-VALIDATION-20260826-r06.txt)에 보존한다. 구조화 Manifest는
[r06 YAML](EV-DOC-BASELINE-20260826-r06.yaml)이다.

## 5. Gate와 한계

`UG-DOC`는 2026-08-26 사용자 확정으로 PASSED다. 구현·Unity Project 생성·Build는 시작하지 않았다.
다음 실행은 사용자의 별도 착수 지시 뒤 Plan의 FDN-001부터 한 Task씩 진행한다.

남은 항목은 문서 질문이 아니라 실행 Evidence다. Exact Unity/Blender patch와 package는 FDN-010,
C1b·Weapon visual은 각 사용자 Art Gate, 실제 기능·2/3/4인 결과는 G1~G3, Player Build와 Steam/NAT는
사용자 수동 Gate에서 검증한다.
