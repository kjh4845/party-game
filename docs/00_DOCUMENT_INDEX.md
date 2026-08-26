# Project Hotfix 문서 인덱스

## 0. 기준선

| 항목 | 값 |
|---|---|
| 기준일 | 2026-08-26 |
| 현재 목표 | `Alpha = Vertical Slice`; Steam 제품 통합 전 LAN/direct endpoint 2·3·4인 완성 |
| 캐릭터 상태 | `Hybrid Core v0.13` C1a 방향 승인, C1b exact profile·최종 Mesh·physics 미승인 |
| 제품 기준 | PRD 1.8.0 → SRS 1.8.0 → Patch Design 0.5.0·분야별 사양 → 구현계획 2.5 → 추적 부록 1.5 |
| 충돌 처리 | 가장 최근의 명시적 사용자 결정이 최우선이며 상위 문서를 먼저 수정한 뒤 하위 문서에 전파 |

아래 표에 `현행`으로 표시된 문서만 구현 근거로 사용한다. 이미지와 과거 문구는 현행 문서의
입력·권한·범위를 바꿀 수 없다.

---

## 1. 권장 읽기 순서

| 순서 | 문서 | 버전·상태 | 역할 |
|---:|---|---|---|
| 1 | `01_PRD.md` | 1.8.0 Product Baseline · 현행 | 제품 비전, 단계, 범위, 게임 규칙 |
| 2 | `02_SRS.md` | 1.8.0 Software Baseline · 현행 | 구현·검증 가능한 시스템 요구사항 |
| 3 | `PATCH_DESIGN.md` | 0.5.0 Approved Patch12 Baseline · 현행 | Patch12·후보·적용·supply/drop·이탈·표현 경계의 단일 source |
| 4 | `CHARACTER_TECHNICAL_SPEC.md` | 0.11.0 Character·Action·Firearm·Participation Baseline · 현행 | C1~C4 비율·Rig·Air action·총기 반동 표현·이탈 중 물리·Paint·Patch·Grip |
| 5 | `ART_DIRECTION.md` | 1.8.0 Alpha Quality·Action·Firearm Boundary · 현행 | 캐릭터·액션·무기·환경·Alpha placeholder/SFX와 표현 단계 |
| 6 | `UI_UX_FLOW.md` | 1.8.0 Minimal Match UI·Leave·Alpha Quality · 현행 | Main·Lobby·Match no-HUD·non-pausing Esc/Leave·평문 Patch 흐름 |
| 7 | `MAP_DESIGN_GUIDE.md` | 1.8.0 Map·Participation·Projectile Compatibility · 현행 | 공통 맵·이탈 중 Character·projectile·Hazard·Camera·2/3/4인 계약 |
| 8 | `MAP_P00_CONSTRUCTION_DROP.md` | 0.7.0 Greybox·Participation·Projectile Compatibility · 현행 | P00 이탈/중단·projectile/Spent·Air action·승인 Patch12 |
| 9 | `WEAPON_DESIGN.md` | 0.7.0 Alpha Minimal UI·Participation·Firearm Combat · 현행 | debug-only ammo, 이탈 중 무기, 4종 archetype·Supply·실제 전투 |
| 10 | `03_IMPLEMENTATION_PLAN.md` | 2.5 Approved Baseline · 현행 | 173 Task·251.0일 WBS, 제작 순서, Evidence와 사용자 Gate |
| 11 | `04_IMPLEMENTATION_TRACEABILITY.md` | 1.5 Approved Baseline · 현행 | 요구사항과 Task의 downstream 추적 감사 |

`04_IMPLEMENTATION_TRACEABILITY.md`는 상위 제품 요구를 새로 만들지 않는다. 추적 부록이나 계획이
PRD·SRS와 충돌하면 상위 문서가 우선한다.

---

## 2. 현행 제품 계약 요약

| 영역 | 기준선 |
|---|---|
| 인원 | Host 포함 총 2~4인. Alpha에서 2인·3인·4인 모두 실제 검증 |
| Authority | 방장 PC의 `AuthorityHost`가 Lobby·Match·무기·점수·패치·Scene을 판정 |
| Alpha 연결 | LAN 또는 명시적 direct endpoint. Steam auth·invite·code·P2P/SDR 없음 |
| 재접속 | unexpected Guest disconnect 뒤 30초 동안 neutral-input Character·Camera·slot과 전체 state를 보존 |
| Match Esc | Simulation은 계속되고 local gameplay input만 neutral. 닫을 때 Mouse all-up 뒤 재무장 |
| Guest Leave·Forfeit | 명시적 Leave는 즉시 Forfeit, 30초 reconnect grace 만료도 Forfeit. Forfeit는 PatchAuthor가 아님 |
| 이탈 뒤 진행 | permanent participant 2명 이상이면 진행. 1명만 남으면 해당 Round score·Patch 0, `OpponentLeft` 뒤 Lobby |
| Host Leave·Loss | Host Migration 없이 Session 종료, Guest는 HostLoss를 확인하고 MainMenu로 복귀 |
| G4 Steam | Steam auth·persona·Friends Lobby·friend invite·Steam P2P/SDR·Steam code를 Alpha 뒤 통합 |
| Steam code | `SteamLobbyId`의 checksum 포함 가역 표현. 별도 Backend 저장·조회 없음 |
| 서버 구성 | Backend, Coordinator, DB, blob, bake worker, Dedicated Server와 자체 relay 0 |
| 실행 환경 | Docker·OCI·Compose·container image 0 |
| Foundation 도구 | Unity 6.3 LTS·Blender 5.2 LTS 계열. 설치된 exact patch와 Unity package manifest/lock은 `FDN-010`에서 고정 |
| 저장소·배포 기반 | `FDN-011` ignore/attribute/LFS, `ART-001` style profiles, `LIC-001` license/NOTICE, `BLD-001` Windows x64 Build Profile. 자동 Build 0 |
| 신규 계획 소유 | `INP-006` Match menu input, `UI-007` minimal Match surface, `APT-007` placeholder Cosmetic, `NET-015` Leave/disconnect/Forfeit |
| 이동 | WASD 화면 축 이동, 이동 방향 자동 회전, Space Jump, Left Shift hold Sprint |
| Sprint | Lobby·Match 공통, stamina 없음, multiplier는 Alpha tuning profile |
| AirKick·Dropkick | 공중 발차기와 전신 Dropkick. 기존 OOB·Hazard·Camera·hand-only control 사용, DropkickRecovery는 non-Down |
| Action 제작 분리 | `AIR-001..002`가 권한 gameplay를, `ANP-001..003`이 visual-only pose/animation을 소유하며 presentation은 authority를 바꾸지 않음 |
| 손 | LMB/RMB 독립 Pending→Strike 또는 Strike 없는 GrabSeek. Grab 전에 Punch 0 |
| Down/Ragdoll | 같은 Round의 진입 횟수마다 groggy duration 증가, base·increment·cap은 tuning, Round reset stack0 |
| 카메라 | Host 계산 `SharedGameplayCamera`, 플레이어·관전자 공통, 개인 회전·Zoom 없음 |
| Lobby Cursor | Esc open, 다시 Esc close·캐릭터 복귀, held Mouse all-up 뒤 Hand rearm |
| Guest action | 같은 role action slot의 Ready/CancelReady, E 또는 Cursor Mouse |
| Host action | Ready 상태 없음. Lobby 진입부터 같은 slot에 Start Button을 항상 표시 |
| Start Gate | 총 2~4명, 모든 Guest 연결·Ready·외형 확정과 Host 외형 확정일 때만 Start 활성 |
| Start 거부 | Host 혼자, NotReady·단절·외형 처리 중 Guest, Host 외형 처리 중에는 수락 0 |
| Lobby lever | 경기 시작용 물리 StartLever 완전 제거. P00 Crane lever는 Match Hazard control로 유지 |
| Tab | Settings의 Hold/Toggle. Match score와 active Patch만 표시 |
| Match 화면 | persistent timer·alive·ammo·killfeed·result panel 0. transient countdown·평문 Patch·이탈/오류와 on-demand Tab만 Player UI로 허용 |
| Ammo 표시 | 일반 Player HUD 0. Host-confirmed ammo·fire/projectile 상태는 developer-only debug에서만 표시 |
| 외형 진입 | C로 Lobby 어디서든 보호된 CharacterCustomizer 진입 |
| Preset | 최대 10개 local atomic save. Cloud·Backend 저장 없음 |
| 외형 동기화 | Host가 bounded appearance source를 검증한 뒤 Peer에 P2P relay, 실패자는 기본 외형 |
| Cosmetic | 고정 색상·크기, 사용자 scale·고정 slot·부위 금지 없음, 전신 위치·3축 회전·중첩·시각 관통 허용 |
| Round | 60초, 필요 시 Sudden Death, 마지막 생존자 1점, 4점 선취 |
| Patch | 사용자 노출 명칭은 `패치`. 승인 Patch12에서 2인 패자·3~4인 최초 탈락자가 Trigger→Effect를 작성하고 다음 Round부터 전원 적용, 최대 3개 FIFO |
| Alpha Patch 화면 | 평문 Trigger 2개→Effect 2개, 남은 시간·확정 결과·활성 목록만 표시. 최종 icon·animation·VFX·SFX·layout은 후속 |
| Patch 구조 | Authority runtime과 presentation을 분리하고 후속 화면·연출은 semantic event/read-model을 읽기만 함 |
| 기본 무기 Supply | AuthorityHost timed sky supply. 2인 10s/22s/cap2, 3인 8s/16s/cap2, 4인 6s/12s/cap3; Spent도 cap 포함 |
| Supply·Drop Patch | `PATCH-PROT-009..012`: supply double·second wave와 weapon-hit victim/attacker source weapon forced drop |
| Firearm | Pistol press당 semi-auto 1발·total7, LongGun hold full-auto·total30, reserve/reload 0 |
| Projectile | Host swept SphereCast, gravity0·no pierce/ricochet, first blocker/Character hit, TTL·OOB·RoundResult/reset 정리 |
| Recoil·spread | Host bounded recoil physics+visual; Pistol accurate/strong shot recoil, LongGun deterministic cumulative spread bloom |
| Ammo 소진 | ammo0→Host ForcedRelease→SpentPendingCleanup 2~4s→remove; cap 포함, 즉시 대체0·다음 pulse |
| Firearm 구현 소유 | `FIR-001..003`이 ammo/Spent, projectile pool/SphereCast, recoil·spread를 소유하고 WPN-005가 통합 |
| Round reset | 캐릭터·손·groggy·grab·무기/ammo/projectile/Spent·소품·Hazard 복원, 점수·map·seed·patch 유지 |
| Rematch | MatchResult 뒤 같은 Lobby, Guest NotReady, Host Start Gate 재개 |
| Alpha 콘텐츠 품질 | Lobby Greybox, 대표 EyeSet·Mustache·Headwear placeholder, Korean-only, BGM 0, 기본 combat·weapon·environment SFX |
| Post-Alpha 표현·설정 | Production Lobby art, full Cosmetic, English·font fallback·music·key help와 key rebind/cursor/UI scale/shake/effect/subtitle/audio volume 등 확장 설정은 Alpha 뒤 재분할 |
| 맵 | 2·3·4인 같은 geometry·Bounds·Hazard·Camera, Spawn 배정과 승인 Weapon supply profile만 변경 |
| 고지대 | RecoveryBand에서 ledge grab·제한 ClimbAssist, 더 낮은 OOB에서 실제 탈락 |
| 첫 맵 | P00 `Construction Drop`: Crane 압착 LethalHazard와 Swing Hook DisplacementHazard |
| 무기 내부 ID | `Pistol`, `LongGun`, `Bat`, `Hammer`는 internal/debug functional ID. 일반 Player UI에는 Weapon 이름을 표시하지 않음 |
| 무기 제작 archetype | M1911-inspired low-poly, AK-47-inspired low-poly, generic baseball bat, sledgehammer(오함마) |
| 무기 복제 금지 | archetype 이름은 제작 reference만 사용. logo·marking·serial·실물 exact replica와 사용자-facing 제조사명 0 |
| 무기 제작 Gate | `WPA-001..003` AssetBrief→Blender source→Unity 비교 뒤 `UG-WEAPON-ART`에서 exact visual Lock |
| 영구 비범위 | 공개 matchmaking·server browser·rank·MMR·Dedicated Server·Host Migration |

---

## 3. 단계 경계

| 단계 | 완료 의미 |
|---|---|
| Foundation | Unity/Blender exact patch·package, repository/LFS, style profile, license/NOTICE와 Windows Build Profile을 고정하되 Build는 실행하지 않음 |
| Prototype | 기본 전투·AirKick·Dropkick·Action Animation·Firearm ammo/projectile/recoil·Camera·Patch12·Supply·Lobby Greybox 검증 |
| Alpha | Direct endpoint 실제 PC 2/3/4인 Lobby Greybox→Match→Lobby, 최소 placeholder 외형 P2P, Patch20 기능, 실제 무기 전투와 기본 SFX |
| G4 Steam 제품 통합 | Steam auth·Friends Lobby·invite·code·P2P/SDR를 Windows Steam 계정 2/3/4인으로 검증 |
| Post-Alpha/1.0 | Production Lobby art·full Cosmetic·English/font fallback·music, 공식 맵6·Patch40+·Steam Workshop Editor·출시 UX와 접근성 |

Alpha 완료는 Steam 출시, Steam 제품 adapter 완료나 Player Build 자동 실행을 뜻하지 않는다. Steam
제품 기능을 Alpha 완료 조건으로 소급하지 않고 G4에서 검증한다.

공개 matchmaking, rank와 MMR은 후속 후보가 아니다. 다시 도입하려면 제품 범위 변경과 사용자
승인을 새로 받아야 한다.

---

## 4. 문서 우선순위와 변경 규칙

```text
명시적 최신 사용자 결정
→ 01_PRD
→ 02_SRS
→ PATCH_DESIGN / 분야별 Guide / Technical Spec
→ 개별 MapSpec / AssetSpec
→ 03_IMPLEMENTATION_PLAN
→ 04_IMPLEMENTATION_TRACEABILITY
→ Test Result / Tuning Profile
```

- 상위·하위 충돌을 조용히 해석하지 않고 먼저 상위 기준선을 갱신한다.
- `START`, 후보값과 목표값은 달성 결과가 아니다.
- tuning 수치를 `LOCKED`로 바꾸려면 test evidence와 필요한 사용자 승인이 있어야 한다.
- 새 입력·Camera·Authority·network 경로를 만들기 전에 PRD/SRS와 인수 기준을 갱신한다.
- 개별 Map·Asset이 공통 계약을 바꾸면 단독 예외 대신 공통 문서에 반영한다.
- 실제 Player Build, Steam 배포와 외부 서비스 변경은 문서 승인에 포함되지 않는다.

---

## 5. 이미지 기준

| 파일 | 용도 | 비권위 요소 |
|---|---|---|
| `assets/ui/main-menu-approved-v1.png` | MainMenu 시각 언어 | 이전 action label·network 상태 |
| `assets/ui/character-customizer-approved-v1.png` | Desktop Editor 정보 구조 | scale tool, Character 비율·손 Shape |
| `assets/ui/interactive-lobby-concept-v2.png` | playfield-first HUD 방향 | 물리 StartLever, `[R] Ready`, 이전 Start 위치 |
| `assets/ui/private-lobby-approved-v1.png` | 색·Typography·density 참고 | 정적 Lobby rail·Player card·start control 전체 |

이미지 속 StartLever, `[R]` hint, scale gizmo, Character 비율과 정적 Player layout은 현행 계약이
아니다. 실제 구현은 Guest `[E]`·Mouse Ready, Host Start UI, C anywhere, scale control 0을 따른다.

---

## 6. 승인 상태와 다음 사용자 Gate

완료된 제품 결정: Patch12·네 Weapon archetype·AirKick/Dropkick과 Firearm semi/full-auto·7/30·no-reload·
projectile·recoil/spread·Spent, minimal Match UI·Match Esc/Leave/Forfeit, Alpha 품질·Foundation 도구 방향은
2026-08-25~26 사용자 승인 기록이다. 이 결정들을 반영한 Lean 문서 실행 기준선 `UG-DOC`도
2026-08-26 사용자 확정으로 PASSED다.

1. 승인 Patch12 실제 기능·2/3/4인 결과 `UG-PATCH12`
2. C1b exact orthographic·measurement profile과 Collider/reach
3. Punch/Grab threshold 120·150·180ms 비교 결과
4. Sprint multiplier·가속 tuning
5. groggy base duration·increment·cap과 feedback
6. W1 WeaponUse/Drop·airborne mode/별도 입력
7. WPA-003 Blender→Unity 비교 뒤 네 Weapon exact visual Lock `UG-WEAPON-ART`
8. Alpha 공식 맵 1개·Greybox 2개와 2/3/4인 결과
9. Alpha Lobby Greybox·대표 Cosmetic placeholder의 2/3/4인 기능 판독 결과

이 Gate 전에도 Host Ready 제거, Start 조건, ReadyTeal+check+label, no Backend, Alpha direct/G4 Steam
분리, 사용자 노출 명칭 `패치`와 영구 matchmaking·rank·MMR 제외는 현행 고정 계약이다.
