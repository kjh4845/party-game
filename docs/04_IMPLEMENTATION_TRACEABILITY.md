# Project Hotfix Lean 구현 추적표

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| 문서 버전 | 1.5 Approved Baseline |
| 기준일 | 2026-08-26 |
| 상위 문서 | PRD 1.8.0, SRS 1.8.0 |
| 상세 패치 기준 | PATCH_DESIGN 0.5.0 · 승인 Patch12·Air Action·Firearm source |
| 실행 계획 | Implementation Plan 2.5 · 173 Task · 251.0일 |
| 범위 | SRS 요구사항 186개, 인수 시나리오 44개 |

이 문서는 요구사항의 구현·검증 소유권을 확인하는 표다. 제품 규칙을 새로 만들지 않으며,
상위 문서와 충돌하면 PRD와 SRS를 먼저 고친다. 이전처럼 모든 요구를 한 행씩 반복하지 않고
같은 시스템 책임을 가진 연속 범위를 묶어 추적한다.

---

## 1. 고정 경계

- Alpha는 LAN/direct P2P를 2인·3인·4인으로 검증하고 Steam 완료를 주장하지 않는다.
- Steam auth·친구 Lobby·친구 초대·room code·P2P/SDR은 Alpha 승인 뒤 G4에서 검증한다.
- 별도 Backend·Coordinator·DB·Blob·Bake Worker·Dedicated Server와 Docker 계열 artifact는 없다.
- Host Ready와 물리 Lobby StartLever는 없으며 Host의 고정 Start button만 사용한다.
- 플레이어 노출 명칭은 `패치`이며 승인 Patch12의 Alpha 화면은 평문 2×2·timer·result·active list만 사용한다.
- 최종 Patch icon·animation·VFX·SFX·layout은 후속이며 core는 semantic event/read-model만 제공한다.
- 기본 timed sky weapon supply는 AuthorityHost가 판정하며 2인 10s/22s/cap2, 3인 8s/16s/cap2,
  4인 6s/12s/cap3의 승인 계약을 사용한다.
- Ground L/R tap/hold는 Punch/Grab, Airborne single tap은 L/R Kick, valid dual down-edge chord는 Dropkick이며
  episode당 AirAttackToken은 하나다. DropkickRecovery는 DownEpisode가 아니다.
- Authority Rigidbody·Hit와 read-only Animator/procedural pose를 분리하고 gameplay Root Motion은 0이다.
- 네 Weapon은 M1911-inspired Pistol, AK-47-inspired LongGun, baseball Bat와 sledgehammer의 generic
  low-poly reference를 사용하고 일반 UI 이름·logo·marking·exact replica는 0이다.
- Pistol은 semi-auto total7, LongGun은 full-auto total30이며 reserve/reload는 0이다. Ammo0은 Host
  forced release→SpentPendingCleanup 2~4초→deadline remove이고 replacement는 다음 정규 pulse다.
- Firearm Projectile·Ammo·recoil/spread는 Host 권한이다. Projectile은 swept SphereCast first-hit,
  no-pierce/ricochet·gravity0 START이며 Fire는 Playing+SuddenDeath, RoundResult부터 0이다.
- Persistent Match HUD는 0이며 transient countdown/Patch/status, on-demand Tab score+active Patch와
  developer-only Ammo debug만 허용한다. Match Esc는 local-only non-pausing menu다.
- Explicit Guest Leave는 즉시 Forfeit, unexpected disconnect는 30초 physical·vulnerable grace 뒤
  Forfeit다. Forfeit는 PatchAuthor가 아니며 영구 참가자 1명은 score·Patch 없이 Lobby로 돌아간다.
  Host Leave/Loss는 Session 종료다.
- Alpha 품질은 Greybox Lobby, EyeSet/Mustache/Headwear 대표 placeholder, Korean-only, BGM 0과 basic
  combat·weapon·environment SFX다. Production Lobby·full Cosmetic·English/font fallback·music·key-help는 후속이다.
- Foundation은 Unity 6.3 LTS·Blender 5.2 LTS exact patch, art profile, binary/LFS, license와 사용자 수동
  Windows x64 Build Profile을 각각 소유한다.
- `UG-PATCH12-DESIGN`은 2026-08-25, `UG-DOC`는 모든 추가 범위를 반영한 2026-08-26 사용자 승인으로 PASSED다.
- 공개 매칭·서버 목록·MMR·Rank는 Deferred가 아니라 영구 비범위다.
- Player Build와 실제 Steam 계정 시험은 USER-MANUAL evidence가 없으면 완료가 아니다.

---

## 2. PRD→SRS 제품 범위

| PRD 범위 | 제품 의미 | SRS 범위 |
|---|---|---|
| PRD-F-001..005 | 2~4인·Ground/Air 입력·Down·Camera | SRS-SYS-001..004, SRS-GAME-001, SRS-INPUT-001..009, SRS-PHYS-001..014, SRS-CAM-001..006 |
| PRD-F-006..010 | 탈락·Round·Patch·Reset | SRS-GAME-002..007, SRS-ROUND-001..006, SRS-PATCH-001..009, SRS-MAP-005..008 |
| PRD-F-011..016 | Lobby·Sprint·Guest Ready·Host Start·Lobby/Match Esc | SRS-LOBBY-001..010, SRS-UI-001..003, SRS-UI-006, SRS-INPUT-002..006 |
| PRD-F-017..019 | Customizer·local Preset·placeholder Cosmetic | SRS-APPEAR-001..013, SRS-APPEAR-023 |
| PRD-F-020..022 | Authority·Alpha direct·Greybox/Korean/basic SFX·금지 인프라 | SRS-SYS-001..014, SRS-SYS-027, SRS-NET-001..015, SRS-SEC-001..005, SRS-NFR-011 |
| PRD-F-023 | Alpha 실제 무기 전투·7/30 no-reload·Projectile/Recoil·visual archetype·W1 Air mapping·supply | SRS-WEAPON-001..017, SRS-APPEAR-021..022 |
| PRD-F-024..026 | Rematch/OpponentLeft·동일 맵·Minimal Match UI/Tab | SRS-LOBBY-008, SRS-ROUND-001..006, SRS-MAP-001..010, SRS-INPUT-007, SRS-UI-004..006, SRS-NET-014 |
| PRD-F-027..029 | G4 Steam·room code·영구 matchmaking/rank 제외 | SRS-SYS-005, SRS-SYS-009, SRS-SYS-024, SRS-SYS-026, SRS-STEAM-001..008 |
| PRD-F-030 | G5 data-only Workshop | SRS-SYS-015, SRS-SYS-025 |
| PRD-F-031 | Guest reconnect·Leave/Forfeit·1명 잔존·Host loss | SRS-NET-008..015, SRS-PATCH-002, SRS-CAM-004, SRS-ERR-003 |

## 3. SRS 요구사항 소유권

| SRS 범위 | 의미 | 주 구현 Task | 주 검증 Task | 후속 Gate |
|---|---|---|---|---|
| SRS-SYS-001..008 | AuthorityHost P2P, 영구 비범위, Alpha direct 경계 | FDN-003, FDN-006, FDN-009, NET-001..003, NET-010 | QA-003, ALP-002 | UG-ALPHA |
| SRS-SYS-009 | G4 Steam 제품 전송 | STM-001..008 | STM-009..013 | UG-STEAM |
| SRS-SYS-010..014 | Transport 교체·모듈·Renderless Simulation·신뢰 한계 | FDN-003, FDN-006..007, NET-001..003 | QA-003, ALP-002 | UG-ALPHA |
| SRS-SYS-015 | G5 data-only Workshop 경계 | DEF-001 | DEF-001 | UG-G5 |
| SRS-SYS-016..018 | local 저장·Preset 복구 | FDN-008, APT-001..003 | QA-003, QA-008 | UG-ALPHA |
| SRS-SYS-019..021 | local structured diagnostic·bounded write | DIA-001..002 | QA-003, QA-010 | UG-ALPHA |
| SRS-SYS-022..023 | 2/3/4 Alpha 검증과 승인 | QA-004..010, ALP-001..003 | ALP-001..003 | UG-ALPHA |
| SRS-SYS-024, SRS-SYS-026 | G4 실제 Steam·최종 금지범위 | STM-009..013, SEC-002 | STM-012..013 | UG-STEAM |
| SRS-SYS-025 | G5 Host/Guest content validation | DEF-001 | DEF-001 | UG-G5 |
| SRS-SYS-027 | Alpha Greybox InteractiveLobby 품질 경계 | LBY-001, UI-003, DEF-001 | QA-003..008, ALP-001 | UG-ALPHA |
| SRS-GAME-001..007 | 인원·라운드·점수·탈락 | RND-001..004, P00-003..005 | QA-002, QA-004..006, QA-009 | UG-ALPHA |
| SRS-INPUT-001..009 | 기본 입력·Sprint·Esc·Tab·Ground/Air tap-hold-chord | INP-001..005, CHR-004, AIR-001 | AIR-002, QA-001..002, UI-005 | UG-HAND, UG-C2 |
| SRS-PHYS-001..014 | 이동·손·Air Kick/Dropkick·DownCount·Ragdoll·복구 | CHR-002..011, AIR-002, ANP-001..002 | AIR-002, PAT-004..005, QA-001..002, NET-007, QA-009 | UG-C2 |
| SRS-CAM-001..006 | SharedGameplayCamera | CAM-001..005 | QA-002, NET-011..014, QA-009 | UG-CAM |
| SRS-ROUND-001..006 | countdown·reset·세대 격리 | RND-001..004, CHR-010, NET-007 | QA-001..006 | UG-ALPHA |
| SRS-PATCH-001..009 | PATCH_DESIGN 0.5.0 승인 Patch12·Punch/Kick/Dropkick/Projectile source·projected set·평문 2×2·supply/drop·FIFO·presentation seam | PAT-001..005, AIR-002, FIR-001..003, WPN-008, PAT-020..023, NET-005, NET-008 | PAT-005, WPN-008, PAT-023, QA-009 | UG-PATCH12-DESIGN, UG-PATCH12, UG-PATCH20 |
| SRS-MAP-001..010 | 공통 맵·Air action·P00·P01/P02·Workshop 경계 | AIR-002, P00-001..006, P00-020..023, M12-001..005 | P00-006, P00-023, M12-005 | UG-P00-GREY, UG-P00-ART-LOCK |
| SRS-NET-001..015 | direct P2P·Action·Ammo/Projectile·동기화·reconnect·Leave/Forfeit·Host loss | FIR-001..003, NET-001..015 | QA-003..006, QA-009 | USER-MANUAL DIRECT |
| SRS-LOBBY-001..010 | FreeRoam·Guest Ready·Host Start·return | LBY-001..007 | UI-003, QA-004..006, QA-008 | UG-ALPHA |
| SRS-APPEAR-001..013 | local preset·Paint·Cosmetic·Host P2P relay | APT-001..006, UI-004 | QA-003..006, QA-008..009 | UG-ALPHA |
| SRS-APPEAR-014..023 | C1b·Interop·Hybrid action animation·Weapon visual archetype·production quality·Alpha placeholder catalog | C1B-001..006, WPA-001..003, ANP-001..003, APT-007, C4-001..004, ANM-001..004 | WPA-003, ANP-003, CAM-005, APT-006, QA-003, C4-004 | UG-C1B, UG-WEAPON-ART, UG-C4-LOCK |
| SRS-WEAPON-001..017 | W1·Air mapping·4종 art·7/30 no-reload·Spent·Projectile·recoil/spread·melee·supply·reset | WPA-001..003, FIR-001..003, WPN-001..008, ANP-003 | WPA-003, WPN-005..008, QA-001..002, NET-011..014, QA-009 | UG-WEAPON-ART, UG-W1, UG-PATCH12 |
| SRS-UI-001..006 | Host Start·blocking reason·Tab/Minimal Match UI·local-only Match menu | UI-003, INP-004, INP-006, UI-007, LBY-004 | QA-003..006, QA-008 | UG-ALPHA |
| SRS-STEAM-001..008 | Steam private Lobby·code·invite·P2P/SDR | STM-001..008 | STM-007..012 | UG-STEAM |
| SRS-SEC-001..005 | bounded input/content·secret redaction·peer 격리 | NET-010, APT-001..006, STM-002..008 | QA-003, ALP-002, STM-009..011 | UG-ALPHA, UG-STEAM |
| SRS-NFR-001..005 | network 격리·Preset·Appearance 성능 | NET-014, APT-003, APT-006, DIA-002 | QA-003..010 | UG-ALPHA |
| SRS-NFR-006..011 | 비색상 정보·Alpha Tab/Korean/basic SFX/BGM0·후속 접근성/audio 설정/localization/music·Camera | UI-003, UI-005..007, CAM-005, AV-001..003, DEF-001, STM-002 | AV-003, QA-008..010, ALP-001, DEF-001, STM-009..011 | UG-ALPHA, UG-G5, UG-STEAM |
| SRS-ERR-001..003 | 안전한 오류와 reconnect/HostLost UX | UI-006, LBY-007, DIA-001, STM-008, SEC-001..002 | QA-003..009, STM-009..011 | UG-ALPHA, UG-STEAM |

---

## 4. 인수 시나리오 소유권

| AT | 주 검증 Task | Evidence 단계 |
|---|---|---|
| AT-001 | QA-004, QA-009 | Alpha 2인 |
| AT-002 | QA-005, QA-009 | Alpha 3인 |
| AT-003 | QA-006, QA-009 | Alpha 4인 |
| AT-004 | LBY-004, UI-003 | Host Start matrix |
| AT-005 | LBY-004, ALP-002 | Host Ready·StartLever absence |
| AT-006 | NET-011..014, QA-004..006 | Alpha direct |
| AT-007 | STM-005, STM-007, STM-009..011 | Steam invite |
| AT-008 | STM-004, STM-007 | room code |
| AT-009 | STM-003, STM-007 | private room |
| AT-010 | NET-002..003, NET-010 | authority attack |
| AT-011 | NET-008, NET-015, CAM-004, QA-004..006, STM-008 | 30초 vulnerable grace·current Alive/Spectator reconnect·timeout |
| AT-012 | NET-009, NET-015, LBY-007, UI-006..007, QA-004..006, STM-008 | Host explicit Leave/Loss·transient HostLoss·Session 종료 |
| AT-013 | NET-006, LBY-005..006 | Scene 왕복 |
| AT-014 | NET-014 | 2/3/4 impairment |
| AT-015 | CHR-004, QA-002 | Sprint |
| AT-016 | INP-003 | Esc safe rearm |
| AT-017 | INP-004, UI-005 | Tab modes |
| AT-018 | INP-002, CHR-006 | tap/hold/grab |
| AT-019 | CHR-008..010, NET-007 | DownCount |
| AT-020 | WPN-003..005 | W1·firearm |
| AT-021 | WPN-006..008 | melee·timed supply·forced drop |
| AT-022 | PAT-001..005, WPN-008, NET-005, NET-008 | 승인 Patch12의 2/3/4인 plain-text 선택·timeout·supply/drop·다음 Round 적용·reset/reconnect·FIFO 기능 lifecycle; 최종 UI/VFX/SFX 제외 |
| AT-023 | P00-001..006, M12-002..005 | map matrix |
| AT-024 | CAM-001..005, QA-009 | shared camera |
| AT-025 | APT-001..006 | local appearance relay |
| AT-026 | APT-001, APT-005..006 | appearance attack |
| AT-027 | APT-004..006, C4-003 | cosmetic invariance |
| AT-028 | C1B-005..006, C4-004 | Blender→Unity parity |
| AT-029 | NET-014, APT-003, APT-006, DIA-002, QA-010 | performance |
| AT-030 | UI-003, UI-005, AV-001..003, QA-008 | Alpha non-color cue·Tab mode 최소 접근성; audio를 포함한 확장 설정은 DEF-001 |
| AT-031 | UI-006, LBY-007, DIA-001..002, SEC-001..002, STM-008 | error·diagnostics |
| AT-032 | FDN-009, ALP-002, STM-013 | permanent exclusion scan |
| AT-033 | DEF-001 | data-only UGC 재계획·검증 소유권 |
| AT-034 | AIR-001..002, INP-005 | Ground/Air tap·hold·chord·token |
| AT-035 | AIR-002, PAT-004..005, NET-007..008 | Dropkick authority·Patch·non-Down recovery |
| AT-036 | ANP-001..003, ANM-001..003, QA-002 | Hybrid animation matrix·root-motion authority 0 |
| AT-037 | WPA-001..003, WPN-003..004, ANP-003 | Weapon archetype·visual Lock·W1 Air mapping 보존 |
| AT-038 | FIR-001, WPN-005, WPN-007, NET-007..008 | Pistol7·LongGun30 no-reload·Spent deadline·next-pulse replacement |
| AT-039 | FIR-002, WPN-005, NET-003..004, NET-007..010 | Host Projectile authority·swept first-hit·TTL/OOB/Result/reset |
| AT-040 | FIR-003, ANP-003, WPN-005, QA-002 | Pistol recoil/accuracy·LongGun deterministic bloom·HUD/W1·SuddenDeath phase |
| AT-041 | UI-007, INP-004, QA-003..006 | Persistent Match HUD 0·허용 transient/Tab·developer-only Ammo debug |
| AT-042 | INP-006, UI-007, QA-003..006 | Match Esc non-pausing local neutral·Mouse all-up rearm |
| AT-043 | NET-008..010, NET-015, UI-006..007, QA-004..006 | 2/3/4 Lobby/Match Leave·30초 vulnerable grace·reconnect/Forfeit·1명 Lobby·Host loss matrix |
| AT-044 | LBY-001, APT-007, UI-005, AV-001..003, ALP-001 | Greybox Lobby·대표 placeholder·Korean-only·BGM0/basic SFX Alpha 범위 |

---

## 5. Evidence 단계

| 단계 | 의미 |
|---|---|
| E0 | source·config·asset inspection, PATCH_DESIGN 0.5.0 Patch12·Air Action·Firearm/Projectile·supply와 Weapon visual 계약 검토 |
| E1 | unit·EditMode·PlayMode test |
| E2 | 2/3/4인 integration·impairment·performance |
| E3 | Editor/manual capture와 사용자 체감 |
| E4 | 사용자가 직접 만든 Player Build·실제 PC·Steam 계정 |
| E5 | 외부 playtest 원자료와 재계산 가능한 KPI |

파일 존재나 compile만으로 동작·체감·Player Build를 통과시키지 않는다. 실패 Evidence도 삭제하지
않고 새 revision으로 보존한다.

## 6. 자동 정합성 조건

- PRD ID 31개, SRS ID 186개와 AT 44개는 각각 unique하고 연속 범위를 가진다.
- 모든 PRD ID는 2장의 SRS 범위에 정확히 포함된다.
- 모든 SRS prefix와 AT ID가 이 문서에 적어도 한 번 등장한다.
- 모든 주 구현·검증 Task가 Plan 2.5에 실제 존재한다.
- Plan은 정확히 173 Task·251.0일이며 AIR-001..002, ANP-001..003, WPA-001..003,
  FIR-001..003, FDN-010..011, ART-001, LIC-001, BLD-001, INP-006, NET-015, UI-007,
  APT-007과 모든 선행관계가 존재해야 한다.
- Plan Task의 선행관계는 누락과 순환이 없어야 한다.
- 모든 Task 크기는 0.5~2일이어야 한다.
- 현행 문서에는 삭제된 문서 link가 없어야 한다.
- 현행 문서는 Index를 포함해 12개이며 PATCH_DESIGN 0.5.0의 Patch ID 12개가 active source여야 한다.
- Backend·Coordinator·DB·Blob·Bake Worker·Dedicated Server·Docker 계열 artifact가 없어야 한다.
- Host Ready, Lobby StartLever, 공개 매칭·MMR·Rank 경로가 없어야 한다.
- Alpha 완료와 Steam 완료를 같은 상태로 표시하지 않는다.
