# Project Hotfix 구현 계획 — Lean Execution WBS

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| 문서 버전 | 2.5 Approved Baseline |
| 기준일 | 2026-08-26 |
| 현재 목표 | Alpha에서 2·3·4인 전체 흐름과 실제 무기 전투를 검증한 뒤 Steam 통합 |
| 기준 문서 | PRD 1.8.0, SRS 1.8.0, PATCH_DESIGN 0.5.0과 현행 분야별 사양 |
| 현재 구현 상태 | FDN-001..011·ART-001·LIC-001·BLD-001·C1B-002 완료: private GitHub origin과 repository-local Git LFS, Unity6.3 URP, module/Input/Physics/UTP, renderless SimulationHarness/test-evidence, local atomic storage, 금지 인프라 guard, START art/license 기반과 Windows x64 Profile 존재. C1B proportion은 H=1 START/CANDIDATE이며 미승인. Review image18은 일반 Git 참고 전용·shipping0. Gameplay/Network code·Player Build 없음 |
| 계획 규모 | 173 Task, 251.0 집중 개발일 |
| 자동화 금지 | Unity Player Build, Steam 배포, 외부 서비스 배포 |

이 계획은 솔로 개발자가 하나씩 실행할 수 있도록 작업을 0.5~2 집중 개발일로 나눈다.
제품 결과와 검증 기준을 적되 serializer byte 배열, 특정 부동소수점 연산 순서, 반복 횟수
강제처럼 구현 전에 확정할 필요가 없는 세부는 별도 기술 기록과 테스트에서 결정한다.

---

## 1. 변경 불가 제품 계약

### 1.1 P2P와 실행 구조

- 별도 Backend, Coordinator, Database, Blob service, Bake Worker와 Dedicated Server를 만들지 않는다.
- Docker·OCI·Compose·container image를 만들거나 사용하지 않는다.
- 방장 Client가 AuthorityHost 역할을 맡고 Guest 1~3명과 직접 연결한다.
- Alpha는 같은 LAN 또는 사용자가 전달한 Host endpoint를 사용하는 Development direct 연결만 검증한다.
- Alpha direct 전송은 Unity Transport 2.6.0을 low-level adapter 뒤에서 사용하고 NGO·Unity Relay/Lobby
  Service는 사용하지 않는다. 실제 adapter/protocol은 NET-001..003, Steam 교체 adapter는 STM-006이 소유한다.
- Alpha가 통과한 뒤 Steam auth·친구 전용 Lobby·친구 초대·코드·P2P/SDR을 붙인다.
- 제품 코드는 SteamLobbyId를 checksum과 함께 가역적으로 표현하며 server lookup을 요구하지 않는다.
- 공개 매칭, 서버 목록, MMR과 Rank는 현재와 향후 범위에 없다.

### 1.2 Lobby 시작

- Guest는 Ready/CancelReady 버튼을 사용한다.
- Host는 처음부터 같은 action 위치에 Start 버튼만 본다. Host Ready 상태는 없다.
- Start는 총 인원 2~4명, 모든 Guest 연결·Ready, 전원 외형 확정, Host가 편집 중이 아님,
  Session이 InteractiveLobby일 때만 활성화된다.
- Host 혼자 시작, NotReady Guest를 둔 시작, UI 우회 시작은 모두 금지한다.
- 이전 Lobby StartLever는 제거한다. 맵 기믹의 물리 Lever는 별도 gameplay 장치로 유지한다.

### 1.3 입력과 상태

- Left Shift를 누르는 동안 Lobby와 Match에서 Sprint한다. Stamina는 만들지 않고 배율은 Alpha에서 조정한다.
- Esc는 Lobby Cursor를 열고, 다시 Esc를 누르면 닫아 캐릭터 조작으로 돌아간다.
- Cursor를 닫을 때 눌린 Mouse 버튼은 모두 놓은 뒤에만 손 입력으로 재무장한다.
- Tab 정보창은 Settings에서 Hold 또는 Toggle을 선택하며 현재 점수와 활성 Patch만 표시한다.
- 첫 Down은 BaseDuration을 사용한다. 같은 Round의 두 번째 Down부터 매번 Increment만큼
  groggy/down 시간이 늘어나고 MaxDuration을 넘지 않는다. DownCount는 새 episode마다 한 번만
  증가하며 Round reset에서 0으로 되돌린다.
- Ground quick tap L/R은 해당 손 Punch, Hold는 해당 손 Grab이다. Airborne quick tap L/R은 해당 발 Kick,
  두 Button down edge가 `60/80/100ms` 비교 window 안이면 Dropkick, Hold는 Hand·Ledge Grab을 유지한다.
- Air attack은 episode당 한 번이며 DropkickRecovery는 짧은 physics tumble을 사용할 수 있지만
  DownEpisode·DownCount와 Down Patch Trigger를 만들지 않는다.

### 1.4 패치·무기 Supply Alpha 경계

- 플레이어 노출 명칭은 `패치`다.
- `PATCH_DESIGN.md`에서 승인한 Patch12와 projected active set/FIFO 후보 규칙을 구현 source로 사용한다.
- Alpha 화면은 평문 Trigger 2개→Effect 2개, 남은 시간, 확정 결과와 활성 목록만 제공한다.
- 최종 icon·animation·VFX·SFX·layout은 후속이며 Alpha core에는 semantic event/read-model seam만 둔다.
- Presentation은 Authority runtime의 read-only 상태를 소비하며 Patch gameplay 결과를 직접 변경하지 않는다.
- AuthorityHost의 기본 timed sky weapon supply는 2인 `initial 10s / interval 22s / cap 2`,
  3인 `8s / 16s / cap 2`, 4인 `6s / 12s / cap 3`의 사용자 승인값을 사용한다.
- `PATCH-PROT-009..012`는 supply double·second wave와 weapon-hit victim/attacker source weapon forced drop을 담당한다.

### 1.5 외형·무기와 Action 표현

- Preset은 local atomic save만 사용한다.
- Paint·Cosmetic source는 크기와 catalog를 Host가 검증하고 P2P로 다른 참가자에게 전달한다.
- 외부 이미지, 임의 Texture, 사용자 Mesh·Shader·Script는 허용하지 않는다.
- `Pistol`은 M1911 계열에서 영감을 받은 generic low-poly 권총, `LongGun`은 AK-47 계열에서 영감을 받은
  generic low-poly 소총, `Bat`은 low-poly 야구방망이, `Hammer`는 low-poly construction sledgehammer로 만든다.
- 실제 logo·각인·serial·제조사 표기와 exact replica는 0이며 사용자에게 Weapon 이름을 노출하지 않는다.
- Pistol은 press당 단발·총 7발, AK는 valid Fire hold 연발·총 30발이다. Reserve Ammo, Reload command와
  Reloading state는 0이다. ammo 0은 Host forced release와 `SpentPendingCleanup 2~4초 START` 뒤 제거로 끝난다.
- Host는 muzzle에서 실제 Projectile을 만들고 fixed-step 이전→다음 위치를 swept SphereCast한다.
  Client ammo·Projectile·Hit 주장은 0이며 piercing·ricochet과 map control 원격 작동도 0이다.
- Host Unity physics가 bounded recoil impulse·torque를 적용한다. Pistol은 좁은 spread·큰 단발 recoil,
  AK는 연사 중 누적 `RecoilAccumulator/SpreadBloom`으로 덜 정확해지고 release/gap에서 회복한다.
- Pistol, LongGun, Bat, Hammer의 실제 전투를 Alpha에서 시험한다.
- 무기 입력은 W1 비교와 사용자 결정 뒤 구현하며 발사·근접 impact·drop·damage·knockback까지 검증한다.
- Unity Authority는 Rigidbody 이동·Action phase·Hit sweep·impulse를 소유하고 Animator·procedural pose는
  semantic state를 표현한다. Gameplay displacement를 만드는 Root Motion은 0이다.
- Unity 3D Physics fixed-step은 60Hz다. Authority/Network cadence는 Physics 두 step마다 30Hz,
  Snapshot 20Hz는 세 step마다·15Hz는 네 step마다 실행하며 같은 tick으로 합치지 않는다.
- Alpha action matrix는 locomotion·Jump phase, L/R Punch, L/R Air Kick, Dropkick·Recovery,
  Grab·Lift·Throw, 네 Weapon fire/swing와 Ragdoll·GetUp을 포함한다. 후속 ANM은 이를 production polish한다.

### 1.6 Match UI·Leave·Alpha 품질

- Persistent Match HUD는 0이다. Timer·Alive·Ammo·Kill Feed·Result Panel을 상시 표시하지 않는다.
- 허용 화면은 transient 3·2·1, Round 사이 plain-text Patch, OpponentLeft/HostLoss/error, on-demand Tab
  score+active Patch와 developer-only debug다. Ammo는 Player HUD가 아니라 debug에서만 확인한다.
- Match Esc는 Simulation을 멈추지 않는 local-only menu다. Local gameplay input만 neutralize하고 닫을 때
  Mouse all-up 뒤 재무장한다.
- Explicit Guest Leave는 즉시 Forfeit, unexpected disconnect는 30초 neutral-input physical/vulnerable
  Character·Camera/slot 유지 뒤 Forfeit다. Forfeit는 PatchAuthor가 아니다.
- Permanent participant가 2명 이상이면 계속하고 1명만 남으면 score·Patch 0으로 OpponentLeft 뒤 Lobby다.
  Host Leave/Loss는 Session 종료다.
- Alpha는 Lobby Greybox, 대표 EyeSet/Mustache/Headwear placeholder, Korean-only, BGM 0과 기본
  combat·weapon·environment SFX를 사용한다. Production Lobby/full Cosmetic/English/font fallback/music은 post-Alpha다.

### 1.7 검증 원칙

- 인원 의존 기능은 2인·3인·4인을 모두 실행한다.
- 자동 Test 성공을 Player Build 또는 Steam 성공으로 표시하지 않는다.
- Mock·Loopback·LAN 성공을 Internet NAT traversal·Steam P2P/SDR 성공으로 표시하지 않는다.
- Blender source와 Unity 반입 결과는 같은 profile·view·조명으로 비교하고 사용자 시각 승인을 남긴다.

---

## 2. 단계와 사용자 Gate

| 단계 | 결과 | 필수 사용자 Gate |
|---|---|---|
| DOC | 새 제품 계약과 Lean 문서 기준선 | UG-DOC |
| G0 | Repository·Unity 기반과 C1b exact 비율 | UG-C1B |
| G1 | Offline 전투·공중 Kick/Dropkick·Alpha Action Animation·Sprint·Down 누적·P00·timed sky supply·승인 Patch12·실제 무기 전투 | UG-HAND, UG-C2, UG-CAM, UG-W1, UG-WEAPON-ART, UG-PATCH12-DESIGN, UG-PATCH12, UG-P00-GREY |
| G2 | LAN/direct P2P Alpha 전체 Lobby→Match→Lobby, local 외형 P2P 동기화 | USER-MANUAL DIRECT |
| G3 | 제품 수준 P00, P01/P02 Greybox, Patch13..20 확장, C4, 전투·맵 Audio/VFX와 Alpha 승인 | UG-ALPHA |
| G4 | Steam auth·친구 Lobby·초대·코드·P2P/SDR 제품 경로 | USER-MANUAL STEAM |
| G5 | Workshop·공식 콘텐츠와 출시 범위 재계획 | 별도 승인 |

`UG-PATCH12-DESIGN`은 2026-08-25 사용자 승인 완료다. Patch12 내용과 supply 계약의 설계
승인이며, 구현 결과 `UG-PATCH12` 승인을 대신하지 않는다. 문서 전체 실행 기준선 `UG-DOC`는
아래 2026-08-26 추가 결정까지 반영한 사용자 확정으로 PASSED다.
네 Weapon archetype과 Hybrid Physics+Animation·Air Kick/Dropkick 방향도 2026-08-25 사용자 승인 완료다.
`UG-WEAPON-ART`는 실제 Blender→Unity low-poly 결과의 시각 Lock이다.
Pistol total7 semi-auto, AK total30 full-auto, no-reload와 Host Projectile·hybrid recoil/spread 방향은
2026-08-26 사용자 승인 완료다. `UG-W1`은 이 탄약 계약을 바꾸지 않고 입력·cadence·airborne WeaponUse를 결정한다.
Unity 6.3 LTS·Blender 5.2 LTS, minimal Match UI, non-pausing Match Esc/Leave/Forfeit와 Alpha Greybox·placeholder·Korean-only·
BGM0 범위도 2026-08-26 사용자 승인 완료다. exact installed patch와 package lock은 FDN-010에서 고정한다.

---

## 3. 실행 규칙

- Task는 0.5, 1, 1.5, 2 집중 개발일 중 하나다.
- 선행 Task와 사용자 Gate가 끝나기 전 다음 작업을 시작하지 않는다.
- Task 완료에는 산출물, 관련 자동 Test, 필요한 수동 capture와 Evidence가 모두 필요하다.
- 수치가 미승인이면 시작값과 비교 범위만 기록하고 실제 결과 뒤 version을 승인한다.
- 한 번에 한 tuning 축만 바꾸고 같은 인원·맵·상황으로 전후를 비교한다.
- P0/P1 결함, authority divergence, crash, 무한 grab, 입력 중복은 다음 Gate로 넘기지 않는다.
- Player Build와 Steam 실제 계정 시험은 사용자가 직접 실행한다.

상태는 NOT_STARTED, READY, IN_PROGRESS, EVIDENCE_PENDING, USER_REVIEW, PASSED, FAILED,
BLOCKED, DEFERRED를 사용한다.

---

## 4. DOC — 문서 기준선

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| DOC-001 | 0.5 | 현행 문서와 삭제 대상을 확정 | — | 전체 | 문서 inventory | 현행 12개 유지, 미사용 문서·누락 참조 0 | — | PASSED |
| DOC-002 | 1 | 현행 사용자 결정을 상위 계약에 반영 | DOC-001 | PRD/SRS | Decision record | P2P·Start·Sprint·Down·무기·패치·Backend 0 충돌 0 | — | PASSED |
| DOC-003 | 2 | 과명세를 제거한 Lean SRS와 Trace 작성 | DOC-002 | SRS 전체 | SRS 1.8.0, Trace 1.5 | unique ID, orphan 0, 제품 결과 중심 | — | PASSED |
| DOC-004 | 2 | Lean WBS와 Evidence 검증 갱신 | DOC-003 | 본 계획 | Plan 2.5, verifier | Task schema·dependency·link 오류 0 | — | PASSED |
| DOC-005 | 0.5 | 새 실행 기준선 사용자 승인 | DOC-001..004 | 문서 승인 | 결정 기록 | 질문 0, 2026-08-25 Action/Weapon·Patch12와 2026-08-26 Firearm·minimal UI·Leave/Forfeit·Alpha 품질·도구 범위 승인 기록 | UG-DOC | PASSED |

## 5. G0 — Foundation와 C1b

### 5.1 Foundation

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| FDN-001 | 1 | 기존 자료를 보존한 root repository 생성 | DOC-005 | SYS | root tree | 중첩 repository 0, 초기화 전 기존 파일 75개 보존(`assets/`는 착수 시 미존재) | — | PASSED |
| FDN-010 | 1 | Unity6.3 LTS·Blender5.2 LTS·Package exact version lock | FDN-001 | SYS,ART | ToolchainProfile | Unity6000.3.9f1·Blender5.2.0 LTS, ProjectVersion.txt·manifest/lock hash 고정, 자동 upgrade 0; package 채택은 FDN-004..006 소유 | — | PASSED |
| FDN-011 | 1 | Repository ignore·attribute·large binary policy 구성 | FDN-001 | SYS,ART | `.gitignore`·`.gitattributes`·binary policy/inventory + LFS/Remote guard | Unity 생성물 제외·source/meta/lock 추적, initial binary20·living binary18 hash 일치; private origin/main 초기 push 일치, repo-local Git LFS3.8·source pattern10, 현재 LFS file/candidate0, 기존 PNG migration·history rewrite0 | — | PASSED |
| FDN-002 | 1.5 | Unity URP project 생성·검증 | FDN-010..011 | SYS,NFR | `Project hotfix` Unity client | Unity6000.3.9f1·URP17.3.0 import/C# compile error0, package/source change0, Player Build0 | — | PASSED |
| FDN-003 | 1.5 | Simulation·Presentation·Input·Transport 모듈 경계 생성 | FDN-002 | SYS | Contracts leaf + 4 runtime asmdef graph | project edge4·cycle0, Presentation→Simulation path0, folder ownership 일치, Unity compile0·EditMode4/4, gameplay code0 | — | PASSED |
| FDN-004 | 1 | Input package 선택 | FDN-002..003 | INPUT | InputPackageDecision | InputSystem1.18.0 Registry/direct·New-only 채택, Legacy/Both·다른 module ref0, Input test4/4·전체8/8; action map은 INP-001 소유 | — | PASSED |
| FDN-005 | 1 | Physics package와 fixed-step 선택 | FDN-002..003 | PHYS | PhysicsPackageDecision | built-in PhysX·Physics60Hz/Authority30Hz, guard4/4·isolated PlayMode contact/joint2/2·전체 EditMode12/12, Player Build0 | — | PASSED |
| FDN-006 | 1.5 | P2P transport adapter와 Alpha direct 구현 방향 선택 | FDN-002..003 | NET | TransportPackageDecision | UTP2.6 Registry/direct·Transport sole owner, NGO/Services/Relay/Lobby0, adapter seam, Transport4/4·전체16/16 | — | PASSED |
| FDN-007 | 1 | Renderless SimulationHarness·EditMode·PlayMode·Evidence 기반 생성 | FDN-002..003 | SYS,NFR | kernel+harness+strict validators | 동일 runtime kernel Unit3/Edit2/Play1, 전체 Edit21/Play3·core skip0, legacy Evidence35+strict schema 검증 | — | PASSED |
| FDN-008 | 1 | Settings·Preset local atomic storage 기반 생성 | FDN-001 | APPEAR,UI | bounded byte envelope/current·last-good·pending repository + LocalStorageProfile | version·length·SHA-256와 validator-aware 복구, 동일 process 다중 instance 직렬화, storage Edit19·boundary2·Play1/전체 Edit42·Play4, server dependency 0 | — | PASSED |
| FDN-009 | 0.5 | 금지 인프라 guard 생성 | FDN-001 | SYS,SEC | versioned policy + Git inventory/content/package audit + adversarial fixtures | 최종 inventory259·content83·manifest2에서 Backend·DB·Docker/Container·Dedicated·audit 위반0, self-test14/245·전체 Edit42/Play4 | — | PASSED |
| ART-001 | 1.5 | LowPolyStyle·ModelInterop·AlphaVisualQA Profile 최초 작성 | FDN-002,FDN-010 | ART,NFR | 세 versioned START profile + semantic/scope validator | unit·axis, palette role·bevel class, FBX/ModelImporter preset, neutral QA·2/3/4×세 화면비 capture 기준과 owner/version 누락0; tests24/206·scope asset/capture0·전체 Edit42/Play4 | — | PASSED |
| LIC-001 | 1 | Third-party package·font·audio·asset license/NOTICE inventory 생성 | FDN-004..006,ART-001 | SYS,SEC,ART | living policy·inventory·NOTICE index + fail-closed verifier | lock58/58(direct46/transitive12) source·version·license·NOTICE disposition, cache58/58·locator40; C2PA review18 shipping0과 first-party production seam(current0) 분리, Player-shipping forbidden/unlicensed external asset0; source-unproven3+Unity tutorial/readme14 제거; initial r01 mutation16/142, living r02 22/176, package change·Build0, final Windows Player audit는 BLD-001/ALP-001 | — | PASSED |
| BLD-001 | 1.5 | Windows x64 Build Profile·PlayerSettings·Scene list 준비 | FDN-002..003,LIC-001 | SYS,NFR | Unity-generated Development·Steam Reserved profiles + policy/manual + guard | profile2·GUID2, Windows x64 Player/Mono, dev/SteamReserved define 상호배타, 임시 identity4, 공용 SampleScene1 START·release-ready0, profile별 Quality/Graphics/PlayerSettings override0, Steam SDK/AppID/기능0; static17/100·전체 Edit52/52·Play4/4, Player Build·BuildAndRun·배포0 | — | PASSED |

### 5.2 C1b exact profile

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| C1B-001 | 0.5 | v0.13 C1a 승인 source 고정 | 역사적 승인 | CHAR | source record | 승인 파일·hash 일치 | UG-C1A | PASSED |
| C1B-002 | 1.5 | normalized exact proportion 후보 작성 | DOC-005,C1B-001,ART-001 | CHAR | `CharacterProportionProfile-C1B-002-r01` + fail-closed guard | H1 START/CANDIDATE, landmark17·exact-height front/side section17·envelope11·누락0, bounds W0.58/D0.265, head0.20·terminal-crotch0.045; gameplay meter/physics/production값·pixel역산·승인·asset·Build0, mutation22/152 | — | PASSED |
| C1B-003 | 2 | 동일 source의 front/side/back/3/4 Blockout 제작 | C1B-002 | CHAR | Blender source·renders + first-party/LFS evidence | 사용자 확인 sourceOwner, canonical `.blend`는 Unity Assets 밖·LFS pointer/upload/fresh fetch, view별 silhouette 일치 | — | NOT_STARTED |
| C1B-004 | 1.5 | Neutral·Grab·Strike·L/R Kick·Dropkick과 4인 lineup 제작 | C1B-003 | CHAR | pose/lineup bundle | 별도 손·발 Mesh 0, Hand/Kick terminal과 2/3/4 action 판독 기준 준비 | — | NOT_STARTED |
| C1B-005 | 1.5 | Blender→Unity Blockout 동등성 확인 | C1B-003..004,FDN-002 | CHAR | import comparison | scale·axis·silhouette의 의미 있는 drift 0 | — | NOT_STARTED |
| C1B-006 | 0.5 | exact profile 사용자 승인 | C1B-002..005 | CHAR | approved profile | ID·version·수치 명시 | UG-C1B | NOT_STARTED |

## 6. G1 — Offline gameplay Alpha core

### 6.1 Character·Input

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| CHR-001 | 1.5 | 승인 C1b Skeleton·Prefab 구성 | C1B-006,FDN-002 | CHAR | prototype prefab | visible hand/finger/toe 0, Hand/Strike/Grab·Kick L/R·Dropkick anchor 존재 | — | NOT_STARTED |
| CHR-002 | 2 | Collider·mass·joint profile 구성 | CHR-001,FDN-005 | PHYS,CHAR | physics profile | 모든 player 동일, dynamic MeshCollider 0 | — | NOT_STARTED |
| CHR-003 | 1.5 | 화면축 이동과 자동 facing 구현 | CHR-002 | GAME,PHYS | locomotion | 8방향 이동·turn 안정, teleport rotation 0 | — | NOT_STARTED |
| CHR-004 | 1 | Left Shift hold Sprint 구현 | CHR-003 | INPUT,PHYS | sprint profile | Lobby/Match 동일, stamina 0, 배율 조정 가능 | — | NOT_STARTED |
| CHR-005 | 1.5 | Jump·buffer·coyote 구현 | CHR-003 | INPUT,PHYS | jump controller | edge·slope에서 false double jump 0 | — | NOT_STARTED |
| CHR-006 | 2 | 좌우 Strike·Grab·GripStress 구현 | CHR-002,INP-002 | GAME,PHYS | hand interaction | 좌우 독립, hold 전 Strike 0, 무한 grab 0 | — | NOT_STARTED |
| CHR-007 | 1.5 | Lift·Throw·Grab break 구현 | CHR-006 | GAME,PHYS | throw resolver | release당 중복 impulse 0 | — | NOT_STARTED |
| CHR-008 | 1.5 | DownCount 기반 groggy/Ragdoll 누적 구현 | CHR-002 | PHYS,ROUND | down profile | Match 첫 Down=Base·이후 Increment·Max cap·episode당 1회, Lobby는 Base·count 0 | — | NOT_STARTED |
| CHR-009 | 1.5 | clearance 기반 GetUp 구현 | CHR-008 | PHYS | recovery state | 공간 없을 때 teleport get-up 0 | — | NOT_STARTED |
| CHR-010 | 1 | Round reset에서 DownCount·Action 일시 상태 초기화 | CHR-006..009,AIR-002,RND-004 | ROUND | reset participant | DownCount·AirAttackToken·DropkickRecovery residue 0, 오래된 transition 0 | — | NOT_STARTED |
| CHR-011 | 1 | Ledge Grab·제한 ClimbAssist 구현 | CHR-005..006 | MAP,PHYS | ledge controller | 실제 접촉+한 손+Jump만, 무한 climb 0 | — | NOT_STARTED |
| CHR-012 | 1 | Character feel·Action 표현 통합 검토 | CHR-003..011,AIR-002,ANP-002,INP-005 | NFR | tuning report | 초기 2/3/4 이동·Punch·Kick·Dropkick·Grab·Ragdoll 비교와 사용자 결정; 전체 playtest는 QA-002 소유 | UG-C2 | NOT_STARTED |
| INP-001 | 1 | Lobby·Match·UI action map 작성 | FDN-004 | INPUT | input actions | Shift/Esc/Tab 포함, generic Interact 0 | — | NOT_STARTED |
| INP-002 | 1.5 | tap Strike·hold Grab resolver 구현 | INP-001 | INPUT | hand state machine | press 즉시 hit 0, sequence당 Strike 1 이하 | — | NOT_STARTED |
| INP-003 | 1 | Esc Cursor open/close와 안전 재무장 구현 | INP-001..002 | INPUT,LOBBY | cursor context | 두 번째 Esc로 복귀, held click 오발 0 | — | NOT_STARTED |
| INP-004 | 1 | Tab Hold/Toggle setting 구현 | INP-001,FDN-008 | INPUT,UI | info overlay setting | 두 mode 모두 score+active Patch만 표시 | — | NOT_STARTED |
| INP-005 | 1 | 120/150/180ms 손 임계값 비교·승인 | INP-002,CHR-006 | INPUT | comparison report | false pre-grab Strike 0, 선택값 version 기록 | UG-HAND | NOT_STARTED |
| INP-006 | 1.5 | Match local-only non-pausing menu input·rearm·Leave 구현 | INP-003,RND-001 | INPUT,UI,NET | match menu input context | Simulation pause0, local gameplay neutral, close mouse all-up rearm, explicit Leave command 1회 | — | NOT_STARTED |
| AIR-001 | 1.5 | Ground/Air tap·hold와 dual-click chord resolver 구현 | INP-002,CHR-005 | INPUT,GAME | airborne action resolver | Ground hand Punch/Grab 유지, Air L/R Kick·`DualClickChordWindow 60/80/100ms` Dropkick·Hold Hand/Ledge Grab, episode token 1 | — | NOT_STARTED |
| AIR-002 | 2 | 권한 Air Kick·Dropkick physics·Hit·Recovery 구현 | AIR-001,CHR-002,CHR-006,CHR-008 | GAME,PHYS | air combat runtime/report | KickAnchor L/R, Dropkick action당 target hit 1, bounded forward impulse·air steer 감소; DropkickRecovery DownEpisode·DownCount·TRG-DOWN 0 및 종료 전 새 Attack 0, 2/3/4 기능 일치 | — | NOT_STARTED |

### 6.2 Shared Camera·Round

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| CAM-001 | 1 | authority Root 기반 subject bounds 구현 | CHR-002 | CAM | subject provider | limb·weapon·cosmetic 영향 0 | — | NOT_STARTED |
| CAM-002 | 1.5 | shared focus·dolly solver 구현 | CAM-001 | CAM | camera solver | 개인 yaw/zoom/split screen 0 | — | NOT_STARTED |
| CAM-003 | 1.5 | damping·dead zone·look-ahead tuning 구현 | CAM-002 | CAM | camera profile | code 변경 없이 tuning 가능 | — | NOT_STARTED |
| CAM-004 | 1 | Lobby·Match·recovery·spectator mask 전환 | CAM-001..003 | CAM,LOBBY | context tests | 탈락·재투입 전환 snap 0 | — | NOT_STARTED |
| CAM-005 | 1 | 2/3/4인·세 화면비 사용자 검토 | CAM-003..004 | CAM,NFR | capture bundle | play/spectate 가독성과 편안함 승인 | UG-CAM | NOT_STARTED |
| RND-001 | 1.5 | Match·Round state machine 구현 | FDN-003 | ROUND,GAME | state machine | invalid 전이 거부, Host clock 하나 | — | NOT_STARTED |
| RND-002 | 1 | 3초 countdown·60초·Sudden Death 구현 | RND-001 | ROUND | timer flow | countdown 전 input 0, Host만 시간 판정 | — | NOT_STARTED |
| RND-003 | 1.5 | 점수·4승·동시전멸 처리 | RND-001..002 | GAME,ROUND | result resolver | 2/3/4 winner 규칙 일치 | — | NOT_STARTED |
| RND-004 | 1.5 | Round baseline reset과 Match state 보존 | RND-001..003 | ROUND | reset registry | DownCount 포함 일시 상태 reset, score/map/Patch 보존 | — | NOT_STARTED |

### 6.3 P00·Patch·Weapon

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| P00-001 | 1 | C1b 기준 CharacterUnit과 P00 profile 작성 | C1B-006 | MAP | P00 profile | START/LOCKED 구분, 숨은 scale 보정 0 | — | NOT_STARTED |
| P00-002 | 1.5 | 옥상 layout·2/3/4 Spawn 구축 | P00-001 | MAP | Greybox scene | geometry 동일, spawn만 인원별 변경 | — | NOT_STARTED |
| P00-003 | 1.5 | RecoveryBand·OOB 구축 | P00-002,CHR-011 | MAP | recovery/oob | 네 방향 복구·최종 탈락 구분 | — | NOT_STARTED |
| P00-004 | 2 | Crane lethal·Hook displacement 구현 | P00-002,CHR-006 | MAP | hazard runtime | Telegraph·counterplay, Hook direct kill 0 | — | NOT_STARTED |
| P00-005 | 1.5 | Edge Tilt·reset·Camera 연결 | P00-003..004,CAM-004,RND-004 | MAP,ROUND,CAM | P00 integration | 단계 적용·reset·Camera 일치 | — | NOT_STARTED |
| P00-006 | 2 | P00 2/3/4인 Greybox Gate | P00-005,WPN-008 | MAP,NFR | playtest report | 접촉 시간·종료 시간·탈락 이해도·timed supply·Patch12 결과 기록 | UG-P00-GREY | NOT_STARTED |
| PAT-001 | 1.5 | 승인 Patch12 catalog port와 projected active set 호환성 검사 | FDN-003,UG-PATCH12-DESIGN | PATCH | approved catalog adapter | FIFO 제거 투영 뒤 실제 2×2 보장, 무한 chain·충돌·숨은 No-op 노출 0 | — | NOT_STARTED |
| PAT-002 | 1.5 | 평문 2×2 selector·7초 offer와 자동 선택 구현 | PAT-001,RND-003 | PATCH | text selection flow | 작성자 외 선택 0, Trigger→Effect·timer·timeout·result 진행 보장 | — | NOT_STARTED |
| PAT-003 | 1.5 | 다음 Round 적용·최대3·FIFO runtime과 semantic presentation seam 구현 | PAT-001..002,RND-004 | PATCH | Patch runtime + read model/events | 전원 동일 적용, reset 후 재적용, Presentation authority mutation 0 | — | NOT_STARTED |
| PAT-004 | 2 | Character Patch 001..008 구현 | PAT-003,CHR-005..008,AIR-002 | PATCH | PATCH-PROT-001..008 | Punch·Kick·Dropkick Attack source별 승인 대상·수치·episode 제한, 기본 탈락 경로 봉쇄·무한 loop 0 | — | NOT_STARTED |
| PAT-005 | 1 | Character Patch 001..008 2/3/4인 기능 pre-gate | PAT-004 | PATCH,NFR | subset functional report | 인원별 plain-text 선택·timeout·다음 Round 발동·FIFO·reset 결과 기록, 최종 UI/VFX/SFX 요구 0 | — | NOT_STARTED |
| WPA-001 | 1 | 승인 네 Weapon low-poly visual brief·scale lineup 작성 | C1B-006 | WEAPON,ART | WeaponArtProfile | M1911-inspired Pistol·AK-47-inspired LongGun·baseball Bat·sledgehammer, functional ID 유지·logo/marking/exact replica 0 | — | NOT_STARTED |
| WPA-002 | 2 | 네 Weapon Blender low-poly source·UV·material·LOD 제작 | WPA-001 | WEAPON,ART | `.blend`·FBX source bundle | silhouette·앞뒤·grip/COM pivot·단순 material·LOD 누락 0, Bat surface는 profile START | — | NOT_STARTED |
| WPA-003 | 1.5 | Weapon Blender→Unity 반입·2/3/4 visual Lock | WPA-002,CHR-001..002,FDN-002 | WEAPON,ART | imported prefab·comparison capture | source/import scale·axis·silhouette·Collider/Socket overlay drift 0, Camera 판독·사용자 승인 | UG-WEAPON-ART | NOT_STARTED |
| WPN-001 | 1.5 | 네 Weapon physics proxy와 socket 통합 | WPA-003,CHR-001..002 | WEAPON | Pistol/LongGun/Bat/Hammer prefabs | 좌우·양손 Grip, Collider/COM과 Camera 판독 | — | NOT_STARTED |
| WPN-002 | 1.5 | pickup·held·drop·reacquire와 맵 Spawn interface 구현 | WPN-001,CHR-006 | WEAPON | weapon state + supply spawn interface | Host ownership·release physics과 WPN-007 scheduler용 P00 Safe DropZone interface | — | NOT_STARTED |
| WPN-003 | 1.5 | W1 입력안 비교 Prototype | WPN-002,INP-005 | WEAPON,INPUT | Editor comparison prototype/report | Context Hand/Weapon Mode/Separate Use 장단점 실측, Player Build 0 | — | NOT_STARTED |
| WPN-004 | 0.5 | Alpha 무기 입력·combat tuning 사용자 결정 | WPN-003 | WEAPON | decision record | fire/swing/drop binding, Air Kick/Dropkick 우선권을 보존한 airborne WeaponUse 허용·별도 입력 여부와 fire cadence·melee timing 귀속 명시; no-reload 7/30 계약 유지 | UG-W1 | NOT_STARTED |
| FIR-001 | 1.5 | Pistol/AK FireMode·Ammo·Spent lifecycle 구현 | WPN-004,WPN-002,RND-004 | WEAPON,ROUND | firearm state profile | Pistol press당 1발·총7, AK valid hold 연발·총30, Reserve/Reload0; ammo0 atomic fire stop·forced release·Spent 2~4s deadline remove·cap 포함·replacement next pulse | — | NOT_STARTED |
| FIR-002 | 2 | Host Projectile pool·fixed-step swept collision 구현 | FIR-001,FDN-005 | WEAPON,PHYS | projectile runtime | owner/cadence/ammo atomic validation, ProjectileId/ShotSequence, previous→next SphereCast first hit, pierce·ricochet·Client hit claim0, TTL/OOB/RoundResult/reset cleanup | — | NOT_STARTED |
| FIR-003 | 2 | Host recoil physics·deterministic spread/bloom 구현 | FIR-001,CHR-002,FDN-005 | WEAPON,PHYS | recoil/spread profiles | bounded impulse·torque; Pistol narrow spread·strong shot recoil, AK capped accumulator/bloom·release recovery, ShotSequence 재현·Grip/Down/Hazard 우회0 | — | NOT_STARTED |
| WPN-005 | 2 | Pistol·AK 실제 발사 전투 통합 | FIR-001..003 | WEAPON | firearm combat report | 7/30 no-reload, semi/full-auto, projectile·damage/knockback·Spent deadline cleanup; Playing+SuddenDeath fire 허용·RoundResult 새 Fire0과 2/3/4 권한 결과 일치 | — | NOT_STARTED |
| WPN-006 | 2 | Bat·Hammer 실제 근접 전투 구현 | WPN-004 | WEAPON | melee combat | swing·impact·damage·knockback 판정 | — | NOT_STARTED |
| ANP-001 | 1 | Alpha Action Animation Matrix·Authority/Presentation 계약 작성 | C1B-004,FDN-003 | CHAR,GAME | action phase profile | locomotion·Jump·Punch·Kick·Dropkick·Grab·Throw·Weapon·Ragdoll/GetUp phase, gameplay Root Motion 0 | — | NOT_STARTED |
| ANP-002 | 2 | Blockout Rig용 locomotion·hand·air action animation prototype | ANP-001,AIR-002,CHR-007,CHR-009 | CHAR,GAME | alpha body action set | Idle/Walk/Sprint·Jump phase·L/R Punch/Kick·Dropkick/Recovery·Grab/Lift/Throw·Ragdoll transition이 Hit state와 일치 | — | NOT_STARTED |
| ANP-003 | 2 | Weapon action animation·Ragdoll/network·2/3/4 통합 | ANP-002,WPN-005..006,WPA-003 | CHAR,WEAPON,NFR | alpha action presentation report | Pistol single recoil·AK sustained recoil/bloom, Bat/Hammer swing, Host phase 추종·false hit cue/root-motion authority 0, 인원별 판독 | — | NOT_STARTED |
| WPN-007 | 2 | 기본 timed sky weapon supply·spawn·reset·2/3/4인 통합 | WPN-005..006,ANP-003,RND-004,P00-005 | WEAPON,ROUND | weapon supply runtime/report | Round-frozen profile, Host schedule·deterministic bag·safe DropZone, Incoming/Loose/Held/Spent cap·Spent deadline remove/replacement next pulse·no backlog; 2p 10s/22s/cap2, 3p 8s/16s/cap2, 4p 6s/12s/cap3; reset divergence 0 | — | NOT_STARTED |
| WPN-008 | 2 | Patch 009..012 구현과 Patch12 전체 2/3/4인 기능 Gate | WPN-007,PAT-005 | PATCH,WEAPON,NFR | PATCH-PROT-009..012 + Patch12 report | Supply pair와 Weapon Hit forced-drop pair, Pistol/AK Projectile별 AttackAction Patch03/04·delayed source Patch12 dedupe, cap/FIFO/reset divergence 0 | UG-PATCH12 | NOT_STARTED |
| QA-001 | 1.5 | G1 자동 regression | CHR-001..012,INP-001..005,AIR-001..002,ANP-001..003,WPA-001..003,FIR-001..003,CAM-001..005,RND-001..004,P00-001..006,PAT-001..005,WPN-001..008 | 전체 G1 | test index | compile/test failure 0, Player Build 0 | — | NOT_STARTED |
| QA-002 | 2 | 2/3/4인 offline 전체 Round 직접 검증 | QA-001 | NFR | playtest report | 인원별 기본전투·Sprint·Down·Pistol7/AK30 no-reload Projectile·recoil/spread·Spent·무기·Patch 원자료 | — | NOT_STARTED |

## 7. G2 — LAN/direct P2P Alpha

### 7.1 Network

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| NET-001 | 1.5 | Development direct adapter 구현 | FDN-006,QA-002 | NET | LAN/direct transport | 신뢰된 Test LAN/direct 전용, 민감 credential 0, public discovery·NAT·제품 보안 성공 주장 0 | — | NOT_STARTED |
| NET-002 | 1.5 | AuthorityHost simulation lifecycle 구현 | NET-001,FDN-003 | NET,SYS | host runtime | active world 1, Host local 우회 0 | — | NOT_STARTED |
| NET-003 | 2 | input·snapshot protocol 구현 | NET-002,INP-002,AIR-001,FIR-001..002 | NET | shared protocol | bounded Ground/Air/Fire edge·sequence와 Ammo/Projectile semantic, Guest Action/Ammo/Projectile/Hit authority field 0 | — | NOT_STARTED |
| NET-004 | 1.5 | local prediction·remote interpolation·correction | NET-003 | NET | presentation sync | action·Projectile·recoil cue duplicate 0, correction metric 기록 | — | NOT_STARTED |
| NET-005 | 1.5 | roster·Ready·result·Patch reliable event 구현 | NET-003 | NET | event stream | 중복·역순 상태 mutation 0 | — | NOT_STARTED |
| NET-006 | 2 | Lobby↔Match Scene 전환·Launch freeze 구현 | NET-003..005 | NET,LOBBY | scene protocol | roster·appearance 동결, build/content/map/appearance 호환 뒤 activate, 변화·실패 시 Lobby 복귀 | — | NOT_STARTED |
| NET-007 | 1.5 | Round reset·DownCount·Action·무기 supply/state 동기화 | NET-003..006,AIR-002,FIR-001..003,RND-004 | NET,ROUND | round sync | Air action, AmmoRemaining·Fire hold/cadence·Projectile·Recoil/Bloom·Spent timer, Supply profile·bag·Incoming/Loose/Held/Spent의 stale 적용 0 | — | NOT_STARTED |
| NET-008 | 1.5 | Guest 30초 reconnect·원자 Recovery 구현 | NET-003..007 | NET | reconnect flow | bounded checksum, same Session·PlayerSlot 결속, Score·Participation·Transform·Down·Action·Patch·Supply/Weapon·Hazard·Scene·Appearance와 현재 Alive/Spectator를 input 재개 전 원자 복원; 과거 action/shot/effect·중복 spawn/replay0, 30초 경계 결과는 NET-015 귀속 | — | NOT_STARTED |
| NET-009 | 1 | Host loss 종료 처리 | NET-002,NET-008 | NET | HostLost flow | Host migration 0, Guest MainMenu 복귀 | — | NOT_STARTED |
| NET-010 | 1.5 | malformed·flood·stale lifecycle·replay 격리 | NET-003..008 | NET,SEC | abuse tests | Session/Scene/Round stale, Join/Reconnect/Recovery rate-limit·replay, impossible fire/claim 격리와 Guest inbound Host-state validation; peer/client crash0 | — | NOT_STARTED |
| NET-015 | 1.5 | Guest Leave·disconnect grace·Forfeit·roster outcome 구현 | NET-006,NET-008..010,RND-003,PAT-003 | NET,ROUND,PATCH | participation lifecycle report | explicit Leave 즉시/abnormal30s neutral physical-vulnerable·Alive Camera subject·Down/탈락 가능, reconnect current Alive/Spectator, Forfeit PatchAuthor0; permanent>=2 continue, 1이면 score·Patch0 OpponentLeft→Lobby; Lobby slot remove/reserve, Host Leave/Loss end | — | NOT_STARTED |
| NET-011 | 2 | 2인 direct 전체 흐름 | NET-004..010,NET-015 | NET | 2p report | 10분 flow, state divergence·Leave/timeout outcome 0 | — | NOT_STARTED |
| NET-012 | 2 | 3인 direct 전체 흐름 | NET-011 | NET | 3p report | 3인 spawn·camera·score·무기 divergence 0 | — | NOT_STARTED |
| NET-013 | 2 | 4인 direct 전체 흐름 | NET-012 | NET | 4p report | 4인 전체 entity·score divergence 0 | — | NOT_STARTED |
| NET-014 | 2 | Local/Target/Edge impairment·느린 Peer·worst-case 성능 Gate | NET-011..013 | NET,NFR | aggregate report | 2/3/4 latency/loss, 4인 cap3·최대 Fire cadence·Projectile pool·Spent·Patch3, 한 slow Guest의 Host tick/다른 Guest 지연 0 | — | NOT_STARTED |

### 7.2 Lobby·UI·Appearance

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| LBY-001 | 2 | 벽 없는 Alpha Greybox InteractiveLobby 구축 | NET-006,CAM-005 | LOBBY | functional Greybox Lobby scene | 본게임 controller 재사용, production art·map UI 0 | — | NOT_STARTED |
| LBY-002 | 1.5 | ball·crate·sandbag·handle과 재투입 구현 | LBY-001 | LOBBY | props/respawn | 1~2초 복귀, score·elimination 0 | — | NOT_STARTED |
| LBY-003 | 1 | 어디서든 C Customizer 진입·보호 구현 | LBY-001,INP-003 | LOBBY,APPEAR | customize state | Ready Guest는 confirm 후 해제, 외부 방해 0 | — | NOT_STARTED |
| LBY-004 | 1.5 | Guest Ready·Host Start button 구현 | LBY-001..003,NET-005 | LOBBY,UI | lobby action state | Guest ReadyTeal+check+label, 고정 slot·Host Ready 0; solo, NotReady·단절 Guest, Host/Guest appearance pending, wrong phase start 0 | — | NOT_STARTED |
| LBY-005 | 1.5 | Start 수락 뒤 내부 RandomMap·호환성 Gate | LBY-004,P00-006 | LOBBY,MAP | launch flow | roster/appearance freeze, build/content/map 호환, Lobby map UI 0, 변화·실패 시 Guest NotReady | — | NOT_STARTED |
| LBY-006 | 1.5 | MatchResult→Lobby 재준비 구현 | LBY-005,NET-006 | LOBBY,ROUND | return flow | Guest 전원 NotReady, Host Start disabled | — | NOT_STARTED |
| LBY-007 | 1 | reconnect·HostLoss Lobby UX 연결 | LBY-004..006,NET-008..009 | LOBBY,ERR | status flow | Guest grace와 HostLost 혼동 0 | — | NOT_STARTED |
| UI-001 | 1.5 | graphite/off-white/Amber theme·Main 구현 | FDN-002 | UI | UI theme/Main | 방 만들기·직접 연결, server list 0 | — | NOT_STARTED |
| UI-002 | 1 | Alpha Host endpoint direct connect flow | UI-001,NET-001 | UI,NET | connect panel | LAN/direct 한계 명시, retry/cancel | — | NOT_STARTED |
| UI-003 | 2 | RoomChip·Roster·Guest Ready·Host Start HUD | UI-001,LBY-004 | UI,LOBBY | Lobby HUD | action slot 역할별 교체, 비활성 이유 표시 | — | NOT_STARTED |
| UI-004 | 2 | Paint·Placeholder Cosmetic·Preset Customizer 구현 | UI-001,APT-001..004,APT-007 | UI,APPEAR | editor overlay | scale·external image0, 손상/비호환 Preset 복구·삭제와 last-good 유지 경로 | — | NOT_STARTED |
| UI-005 | 1.5 | Tab·Korean Alpha 최소 Settings 구현 | UI-001,INP-004 | UI,NFR | Korean-only minimal Settings | Tab Hold/Toggle, 전체 Alpha Player-facing Text 한국어 glyph/copy 누락0; key rebind·cursor/UI scale·shake/motion/effect·subtitle·별도 color-vision·Patch review·StringTable/font fallback·English 0 | — | NOT_STARTED |
| UI-006 | 1 | 오류 범주·안전한 Message·다음 행동 구현 | UI-002..005,LBY-007 | UI,ERR,SEC | error catalog | secret/path/peer 존재 노출 0, reconnect·HostLost·OpponentLeft·appearance·scene·Preset recovery별 retry/return action | — | NOT_STARTED |
| UI-007 | 1.5 | Minimal Match transient·non-pausing Esc·Leave UI 구현 | UI-001,INP-006,RND-003,PAT-002,NET-015 | UI,ROUND | match transient surfaces | persistent HUD(timer/alive/ammo/killfeed/result)0; transient 3-2-1·Patch·OpponentLeft/HostLoss/error, Tab only score+active Patch, debug-only Ammo | — | NOT_STARTED |
| APT-001 | 1.5 | bounded local appearance source·rasterizer 구현 | FDN-008 | APPEAR,SEC | appearance core | 임의 file/texture input 0 | — | NOT_STARTED |
| APT-002 | 1.5 | Paint·Undo/Redo·symmetry 구현 | APT-001 | APPEAR | paint tools | local preview와 저장 source 일치 | — | NOT_STARTED |
| APT-003 | 1.5 | Preset 최대10 local atomic 저장·성능 검증 | APT-001..002 | APPEAR,NFR | preset repository | restart 복원·손상 격리, save/load/delete p95 목표 측정 | — | NOT_STARTED |
| APT-004 | 2 | 전신 Cosmetic cage와 최대16 편집 구현 | CHR-001,APT-001,APT-007 | APPEAR,CHAR | cage/editor | 위치·회전만, overlap·관통 허용 | — | NOT_STARTED |
| APT-005 | 1.5 | Host appearance 검증·P2P relay 구현 | APT-001..004,NET-003 | APPEAR,NET | relay pipeline | Host validation 전 remote apply 0 | — | NOT_STARTED |
| APT-006 | 2 | 2/3/4인 외형 수렴·Timeout·Fallback 검증 | APT-005,NET-011..013 | APPEAR,NFR | convergence report | timeout→해당 player default→Start Gate 재평가, peer별 동일 source·다른 player block 0 | — | NOT_STARTED |
| APT-007 | 1 | Alpha Placeholder Cosmetic 최소 Catalog 제작·반입 | ART-001,CHR-001,FDN-002 | APPEAR,ART | EyeSet/Mustache/Headwear representative prefabs | category별 1개 이상, source/import hash·fixed size/color, Collider/reach/mass0; production catalog 아님 | — | NOT_STARTED |
| DIA-001 | 1.5 | Secret 없는 Local structured diagnostic 구현 | FDN-007,NET-002 | SYS,ERR | local diagnostic sink | build/session/peer/scene/round/map/profile context, bounded nonblocking write | — | NOT_STARTED |
| DIA-002 | 1 | Network·Combat·Patch·Scene·Reconnect metric와 summary 연결 | DIA-001,NET-014,PAT-003 | SYS,NFR | diagnostic summary | tick/input/snapshot/correction/Ammo/Fire reject/Projectile/Recoil-Bloom/Spent/queue/reconnect/scene/patch/termination 누락 0 | — | NOT_STARTED |
| SEC-001 | 1 | Alpha direct·appearance·error trust-boundary 감사 | NET-010,NET-015,APT-006,UI-006,DIA-002 | SEC | Alpha security report | trusted-test-network 한계, Join/Recovery rate-limit·replay, Host/Guest inbound validation, malformed isolation, secret/raw endpoint log 0 | — | NOT_STARTED |
| QA-003 | 1.5 | G2 자동 network/Lobby/appearance regression | NET-001..015,LBY-001..007,UI-001..007,APT-001..007,DIA-001..002,SEC-001 | 전체 G2 | test index | 2/3/4 core failure 0 | — | NOT_STARTED |
| QA-004 | 1.5 | 2인 direct 전체 Alpha flow | QA-003,BLD-001 | NFR | 2p flow report | Lobby→Match→Lobby, explicit/timeout→score·Patch0 OpponentLeft, grace 중 Camera·Down/탈락과 reconnect current Alive/Spectator, non-pausing Esc/rearm·무기·외형 완료 | USER-MANUAL | NOT_STARTED |
| QA-005 | 1.5 | 3인 direct 전체 Alpha flow | QA-003,BLD-001 | NFR | 3p flow report | one Forfeit 뒤 permanent2 continue, current Alive/Spectator reconnect와 second leave→Lobby 완료 | USER-MANUAL | NOT_STARTED |
| QA-006 | 1.5 | 4인 direct 전체 Alpha flow | QA-003,BLD-001 | NFR | 4p flow report | Host+3 Guest, multi leave/timeout·grace Camera/Down·current-state reconnect·PatchAuthor exclusion·HostLoss 완료 | USER-MANUAL | NOT_STARTED |

## 8. G3 — Alpha content·quality

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| C4-001 | 0.5 | C2·외형·무기·Patch12 결과로 production 착수 승인 | CHR-012,WPN-008,APT-006 | CHAR | start bundle | 핵심 판독·reach·supply/drop 문제 0 | UG-C4-START | NOT_STARTED |
| C4-002 | 2 | production Mesh·Rig 제작 | C4-001 | CHAR | character source | C1b silhouette 유지, 별도 손 0 | — | NOT_STARTED |
| C4-003 | 2 | UV·material·LOD·cage 통합 | C4-002 | CHAR,APPEAR | production prefab | Paint·Cosmetic·2/3/4 가독성 유지 | — | NOT_STARTED |
| C4-004 | 1 | Blender→Unity 최종 품질·사용자 Lock | C4-003 | CHAR | generation manifest | source/import/profile hash와 시각 승인 | UG-C4-LOCK | NOT_STARTED |
| ANM-001 | 1.5 | locomotion·Sprint·Jump·Air Kick/Dropkick production polish | C4-002,CHR-005,ANP-002 | GAME | animation set | Alpha action phase·KickAnchor와 일치, gameplay Root Motion·authority mutation 0 | — | NOT_STARTED |
| ANM-002 | 2 | Strike·Grab·Throw·Weapon production animation | C4-002,WPN-007,ANP-003 | GAME,WEAPON | action set | Alpha Hit phase, Pistol single/AK sustained recoil·Spent release 유지, 손·무기 방향 2/3/4 판독 | — | NOT_STARTED |
| ANM-003 | 1.5 | Down 누적·DropkickRecovery·Ragdoll·GetUp presentation | C4-002,CHR-008..009,ANP-002 | PHYS | recovery set | Down duration·non-Down tumble 구분과 state 일치 | — | NOT_STARTED |
| ANM-004 | 1 | Lobby respawn·Hazard reaction | ANM-001..003 | GAME | reaction set | duplicate network reaction 0 | — | NOT_STARTED |
| P00-020 | 1.5 | P00 Art brief·StylePreflight | P00-006,C4-001,NET-014 | MAP,ART | art brief | 4인 worst-case 성능 예산 안에서 gameplay bounds 변경 0 | UG-P00-ART | NOT_STARTED |
| P00-021 | 2 | rooftop environment kit 제작·반입 | P00-020 | MAP,ART | environment prefabs | source/import Style Gate | — | NOT_STARTED |
| P00-022 | 2 | Crane·Hook·material·lighting 제품화 | P00-020..021 | MAP,ART | hazard art | phase·pivot·Telegraph 유지 | — | NOT_STARTED |
| P00-023 | 2 | 2/3/4인 P00 제품 Lock | P00-021..022,ANM-004 | MAP,NFR | final P00 report | Greybox 회귀 0, 성능·가독성 통과 | UG-P00-ART-LOCK | NOT_STARTED |
| PAT-020 | 1 | Patch13..20 설계 승인 | WPN-008,P00-006 | PATCH | content matrix | 승인 Patch12와 중복·무한 chain 0 | UG-PATCH20 | NOT_STARTED |
| PAT-021 | 2 | Patch13..16 구현 | PAT-020 | PATCH | content 13..16 | 조합별 실행·reset 통과 | — | NOT_STARTED |
| PAT-022 | 2 | Patch17..20 구현 | PAT-021 | PATCH | content 17..20 | 조합별 실행·reset 통과 | — | NOT_STARTED |
| PAT-023 | 1.5 | Patch20 2/3/4인 network·이해도 검증 | PAT-022,NET-014 | PATCH,NFR | Patch20 report | peer divergence 0, 결과 설명 가능 | — | NOT_STARTED |
| M12-001 | 1 | P01·P02 콘셉트 사용자 선택 | P00-006 | MAP | concept decision | P00과 다른 두 리듬 선택 | UG-M12 | NOT_STARTED |
| M12-002 | 2 | P01·P02 Lean MapSpec 작성 | M12-001 | MAP | two MapSpecs | OOB/Lethal/Displacement/Camera/Spawn 명시 | — | NOT_STARTED |
| M12-003 | 2 | P01 Greybox 구현 | M12-002 | MAP | P01 scene | 2/3/4 geometry 동일 | — | NOT_STARTED |
| M12-004 | 2 | P02 Greybox 구현 | M12-002 | MAP | P02 scene | 2/3/4 geometry 동일 | — | NOT_STARTED |
| M12-005 | 2 | P01/P02 2/3/4인 Patch20 검증 | M12-003..004,PAT-023 | MAP,NFR | two-map report | 인원별 spawn·camera·hazard divergence 0 | — | NOT_STARTED |
| AV-001 | 1 | Alpha basic SFX/VFX event·고정 mix profile | FDN-003 | NFR | Alpha AV catalog·mix profile | combat·weapon·environment category만 제공, semantic event 중복 0, 사용자 audio channel control·BGM event/asset 0 | — | NOT_STARTED |
| AV-002 | 2 | 전투·무기·환경 basic feedback | AV-001,ANM-001..004 | NFR | Alpha feedback set | Pending false hit cue 0, production Lobby ambience·music 0 | — | NOT_STARTED |
| AV-003 | 2 | P00 Hazard Telegraph와 Alpha 최소 접근성 검증 | AV-001..002,P00-023,UI-005 | MAP,NFR | AV report | Identity·Ready·Down·Hazard color-only cue 0, Tab mode 동작, basic SFX와 visual Telegraph 대응, BGM·사용자 audio control 0 | — | NOT_STARTED |
| QA-007 | 2 | Alpha 전체 자동 regression | QA-003,C4-004,ANM-004,P00-023,PAT-023,M12-005,AV-003 | 전체 Alpha | test index | G1·G2·G3 compile/test failure·skip 0 | — | NOT_STARTED |
| QA-008 | 2 | Main·Lobby·Customizer 첫 사용자 UX | QA-007 | UI,NFR | UX report | 2/3/4 역할과 Start 이해 | USER-MANUAL | NOT_STARTED |
| QA-009 | 2 | 외부 2/3/4인 전체 경기 playtest | QA-007..008 | NFR | playtest report | 각 인원 full Match·재경기 완료 | USER-MANUAL | NOT_STARTED |
| QA-010 | 1.5 | KPI·결함·성능 종합 | QA-009 | NFR | Alpha quality report | open P0/P1 0, 표본 한계 명시 | — | NOT_STARTED |
| ALP-001 | 1 | Alpha Evidence·요구사항·asset/license audit | QA-003..010,LIC-001,BLD-001 | 전체 Alpha | evidence index | G2 direct 2/3/4·G3 포함 orphan/숨긴 실패·license/NOTICE 누락0; Player Text Korean-only, 사용자 audio channel control·BGM0, basic SFX 존재; Player Build 실행 여부를 사실대로 분리 | — | NOT_STARTED |
| ALP-002 | 0.5 | 금지 범위 audit | ALP-001 | SYS,SEC | scope audit | Backend·Docker·Dedicated·public match·rank 0 | — | NOT_STARTED |
| ALP-003 | 0.5 | Alpha 사용자 승인 | ALP-001..002 | 전체 Alpha | decision record | 승인 또는 재작업 목록 | UG-ALPHA | NOT_STARTED |

## 9. G4 — Alpha 이후 Steam 통합

Steam 단계도 방장 AuthorityHost를 유지한다. Steam Lobby는 친구 초대와 위치 찾기를 제공할 뿐
게임 권한이나 별도 server가 아니다.

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| STM-001 | 1.5 | Steamworks wrapper·transport 조합 선택 | ALP-003 | STEAM,SEC | decision record | Windows 지원·license·maintenance·platform security 확인, custom crypto 0 | — | NOT_STARTED |
| STM-002 | 1.5 | Steam auth·persona 연동 | STM-001 | STEAM,SEC | identity adapter | local/remote SteamId 검증, unsafe persona escape·ticket log 0 | — | NOT_STARTED |
| STM-003 | 1.5 | friends-only private Steam Lobby 생성·참가 | STM-002 | STEAM | Lobby flow | public listing·matchmaking 0 | — | NOT_STARTED |
| STM-004 | 1 | checksum 포함 SteamLobbyId code 구현 | STM-003 | STEAM | PartyCode codec/UI | server lookup 0, 잘못된 code 거부 | — | NOT_STARTED |
| STM-005 | 1.5 | Steam 친구 초대 구현 | STM-003 | STEAM | invite flow | 실행/비실행 초대 route | — | NOT_STARTED |
| STM-006 | 2 | Steam P2P/SDR transport 구현 | STM-001..003 | STEAM,NET,SEC | Steam adapter | direct/SDR 동일 protocol, Steam 인증·암호화 사용, plaintext·custom fallback 0 | — | NOT_STARTED |
| STM-007 | 1.5 | code·friend invite의 same Lobby/Host 수렴 검증 | STM-004..006 | STEAM | convergence report | 별도 game server·Party 분기 0 | — | NOT_STARTED |
| STM-008 | 1.5 | Steam reconnect·HostLoss 통합 | STM-006..007 | STEAM,NET | reconnect flow | 30초 Guest 복구, Host migration 0 | — | NOT_STARTED |
| STM-009 | 1.5 | 실제 Steam 2인 direct/SDR·제한 NAT 시험 | STM-008 | STEAM,NFR | 2p report | 서로 다른 실제 계정과 전체 Match, route 기록 | USER-MANUAL | NOT_STARTED |
| STM-010 | 1.5 | 실제 Steam 3인 direct/SDR·제한 NAT 시험 | STM-009 | STEAM,NFR | 3p report | 3계정 전체 Match, route 기록 | USER-MANUAL | NOT_STARTED |
| STM-011 | 1.5 | 실제 Steam 4인 direct/SDR·제한 NAT 시험 | STM-010 | STEAM,NFR | 4p report | Host+3 Guest 전체 Match, route 기록 | USER-MANUAL | NOT_STARTED |
| SEC-002 | 1 | Steam transport·identity·secret regression | STM-001..011,UI-006 | STEAM,SEC | Steam security report | maintained library, Lobby membership, ticket/endpoint redaction, plaintext·replay fallback 0 | — | NOT_STARTED |
| STM-013 | 0.5 | Post-G4 release 금지범위 audit | STM-009..011,SEC-002 | SYS,SEC | release scope report | Backend·DB·Dedicated·container·direct fallback·public matching/rank/MMR 0 | — | NOT_STARTED |
| STM-012 | 0.5 | Steam 제품 경로 사용자 승인 | STM-013 | STEAM | decision record | auth·invite·code·P2P/SDR·제한 NAT·금지범위 명시 | UG-STEAM | NOT_STARTED |

## 10. G5 경계

Workshop, 공식 맵 6개, Patch40, 가격·상점·출시 운영은 Steam 통합 결과를 검토한 뒤 새 WBS로
분리한다. Dedicated Server, Host Migration, 공개 매칭, MMR과 Rank는 G5 후보에도 넣지 않는다.

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| DEF-001 | 0.5 | Post-Alpha/G5 presentation·settings·content·출시 범위를 재승인·재분할 | STM-012 | SYS,MAP,ART,UI | post-Alpha decision/WBS | Production Lobby·full Cosmetic·English/StringTable/font fallback·music·key-help·key rebind/cursor/UI scale/shake/motion/effect/subtitle/color-vision/Patch review/audio volume과 data-only Workshop을 실제 구현 전 별도 Task로 분리하고 Host/Guest validation·금지 code 경계 확정 | UG-G5 | DEFERRED |

---

## 11. 승인 직후 실행 순서

1. DOC-005 기준선 승인 기록 확인(PASSED)
2. FDN-001 root repository
3. FDN-010..011에서 Unity/Blender exact patch와 repository binary policy 고정
4. FDN-002 Unity project, FDN-003 module boundary
5. FDN-004..009 package·test·local storage·forbidden-infrastructure guard
6. ART-001 style/interoperability profile, LIC-001 license inventory, BLD-001 수동 Build Profile 준비
7. C1B-003..006 Blockout·Pose·Unity parity와 사용자 승인(C1B-003 직전 first-party sourceOwner 확인)
8. G1 Character/Input·AIR·Alpha Action Animation부터 한 Task씩 실행
9. WPA-001..003으로 네 Weapon low-poly 결과를 잠근 뒤 WPN 구현 시작
10. UG-W1 뒤 FIR-001..003으로 7/30 no-reload state, Projectile, Recoil/Spread를 분리 구현
11. PAT-001은 2026-08-25 `UG-PATCH12-DESIGN` 승인 기록과 FDN-003 완료 뒤 시작

Player Build, Steam 배포와 외부 서비스 실행은 이 자동 순서에 포함하지 않는다.

## 12. 계획 검증 체크리스트

- 모든 Task는 0.5~2일이며 하나의 검증 가능한 결과를 가진다.
- Host Start는 Guest 전원 Ready 전에는 활성화되지 않는다.
- Shift Sprint, Esc Cursor toggle, Tab Hold/Toggle와 DownCount reset이 각각 구현·검증 Task를 가진다.
- Ground hand Punch/Grab과 Air L/R Kick·dual-click Dropkick·Hold Ledge Grab이 결정적 resolver와
  Authority physics Task를 가지며 DropkickRecovery는 DownCount를 만들지 않는다.
- Alpha Action Animation Matrix와 prototype set이 G1에 있고 Unity physics/Hit를 바꾸는 Root Motion은 0이다.
- M1911-inspired Pistol, AK-47-inspired LongGun, baseball Bat와 sledgehammer의 Blender source→Unity
  import·Collider/Socket·2/3/4 Camera Lock Task가 존재한다.
- FIR-001..003은 Pistol 7발 semi-auto·AK 30발 full-auto·Reload0, Spent cleanup, Host Projectile
  swept collision과 bounded deterministic recoil/spread를 각각 소유한다.
- 실제 무기 전투가 Alpha 안에 있고 W1 사용자 결정 뒤 구현된다.
- Patch12는 승인 설계 기준으로 plain-text 2×2·timer·result·active list와 실제 적용만 Alpha에서 검증한다.
- 기본 timed sky weapon supply는 승인된 2/3/4인 initial·interval·cap을 사용하고 WPN-008에서 Patch12 전체를 닫는다.
- Patch runtime의 semantic event/read-model은 후속 presentation과 분리되고 Patch 전용 최종
  icon·animation·VFX·SFX·layout은 Alpha Gate가 아니다. 기본 Action Animation은 G1 필수다.
- 2인·3인·4인이 offline, direct P2P, map, camera, appearance, weapon, Steam 단계에서 각각 검증된다.
- Match에는 Persistent HUD가 없고 transient countdown/Patch/status, Tab score+active Patch와 developer-only
  Ammo debug만 존재한다. Main key-help는 post-Alpha다.
- Match Esc는 Host Simulation을 멈추지 않고 local input만 neutralize하며 mouse all-up 이후 재무장한다.
- Guest explicit Leave는 즉시 Forfeit, 비정상 단절은 30초 vulnerable Character 유지 뒤 Forfeit이며
  Forfeit는 PatchAuthor가 아니다. 영구 참가자 1명은 score/Patch 없이 Lobby, Host loss는 Session 종료다.
- Alpha presentation은 Lobby Greybox·대표 placeholder Cosmetic·Korean-only·BGM 0·basic SFX로 제한되고,
  Production Lobby/full Cosmetic/English/font fallback/music은 Deferred Task에만 있다.
- Unity 6.3 LTS·Blender 5.2 LTS exact patch, art profile, binary/LFS policy, license inventory와
  사용자 수동 Windows x64 Build Profile이 Foundation Task로 분리되어 있다.
- Backend·Coordinator·DB·Blob·Bake Worker·Dedicated Server와 Docker artifact가 없다.
- Steam은 Alpha 승인 뒤 시작하고 code와 friend invite가 같은 Host P2P로 수렴한다.
- 공개 매칭·MMR·Rank가 현재·Deferred 어디에도 없다.
- 과명세는 분야별 기술 기록으로 이동하며 제품 문서에 성공 결과를 미리 적지 않는다.
