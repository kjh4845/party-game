# Project Hotfix 구현 계획 — Lean Execution WBS

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| 문서 버전 | 2.5 Approved Baseline + 2026-09-04 Action Presentation Addendum |
| 기준일 | 2026-09-05 |
| 최종 갱신일 | 2026-09-11 |
| 현재 목표 | Alpha에서 2·3·4인 전체 흐름과 실제 무기 전투를 검증한 뒤 Steam 통합 |
| 기준 문서 | PRD 1.8.0, SRS 1.8.0, PATCH_DESIGN 0.5.0과 현행 분야별 사양 |
| 현재 구현 상태 | r16 Neutral 외형 방향은 2026-09-02 `UG-C1B-NEUTRAL` 승인 완료. r20은 기존 r16 geometry를 유지한 Blender Generic22(논리20+hip helper2) 리그 검토 후보다. 내부 Neck은 허용하며 가시 목0을 유지한다. 원본 r20의 Action0은 보존하고 별도 후보에서 공식 정적 Pose·보행/Grip·Grab/Wall IK와 팔꿈치 중심 Punch Action2를 검토했다. Unity 게임용 Prefab/Collider/animation 통합은 미완료다. 캐릭터 최종 Gate에는 `C1BRW-004` 공식 정적 Pose8·lineup 검토가 남아 있으며 `C1BRW-005` static parity와 `C1B-006 / UG-C1B`는 미완료다. Foundation 최초 구축은 완료됐지만 living inventory와 역사/current verifier 불일치가 점검돼 정리 대상이다. 현재 로컬 시제품은 승인된 유한 경기장·22CU 카메라와 정적 가장자리 복귀·한손/양손 하중 반응 자세·자동 상향 들기/상호 잡기 차단, 승인된2배 이동 속도(걷기1.40/달리기2.10m/s)와 공용 구도 보행·고정60Hz 연산·렌더 보간, 최신 광배 가중치, 빠른 부위별 펀치, Grab/Throw·Down/GetUp 및2·3·4명 공용 카메라·화면축 이동을 연결했다. 정식 Prefab·InputAction·Match/P00·peer 통합과 최종 Gate는 후속이다. |
| 계획 규모 | 178 Task, 258.0 집중 개발일 |
| 자동화 금지 | Unity Player Build, Steam 배포, 외부 서비스 배포 |

이 계획은 솔로 개발자가 하나씩 실행할 수 있도록 작업을 0.5~2 집중 개발일로 나눈다.
제품 결과와 검증 기준을 적되 serializer byte 배열, 특정 부동소수점 연산 순서, 반복 횟수
강제처럼 구현 전에 확정할 필요가 없는 세부는 별도 기술 기록과 테스트에서 결정한다.

### 2026-09-05 공통 Grip·전 주기 변형·다방향 회복 결정

- 총기 Grip은 무기 종류와 좌우에 공통인 팔 뻗기 template 하나다. 동일 template를 실제 잡은 팔에
  적용하며 왼손·오른손 어느 쪽도 가능하다. 손 점유와 Main/Support 역할은 gameplay 정보이며
  Pistol/LongGun별 pose나 좌우별 별도 authored clip을 만드는 근거가 아니다. fire/recoil·reload clip0 유지.
- 리깅 QA는 걷기·뛰기 한 주기의 전 sample frame과 loop 경계에서 어깨·팔꿈치·골반·무릎의 꺾임,
  자기교차·면 뒤집힘·국소 압축/늘어남과 육안 자연스러움을 함께 확인한다. 한 정지 자세의 PASS로 대체하지 않는다.
- 회복 진입은 공격 방향이 아니라 Host가 확인한 실제 torso 자세와 지지면 normal을 사용한다.
  앞·뒤·좌·우 및 대각선·임의 yaw·경사면을 지원하고, 안정 지지나 clearance가 없으면 기다린다.
- 사용자가 권장안을 선택했다. 옆으로 누우면 현재 몸의 수평 방향을 보존하면서 가까운 앞/뒤 자세로
  정렬한 뒤 공통 face-up/face-down GetUp을 사용한다. 좌우 전용 GetUp clip은 추가하지 않는다.
  정렬과 GetUp은 순간이동·즉시 방향 반전 없이 연결하고 Host만 단계·완료를 판정한다.
- DropkickRecovery와 DownEpisode는 위 시각 경로를 공유하되 gameplay state·DownCount·groggy·Down Trigger는 분리한다.
  Upright Dropkick settle은 일어나기 없이 locomotion 복귀가 가능하며 떠 있거나 head-down이면 먼저 물리 안정화를 기다린다.


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
- Air attack은 episode당 한 번이다. DropkickRecovery는 DownEpisode와 별도 state로 짧은 physics tumble을
  사용하고 prone settle 뒤 방향별 Ragdoll GetUp 시각 모션만 공유할 수 있으며 DownCount·groggy·Down Patch
  Trigger를 만들지 않는다.

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
- Pistol Main arm과 LongGun Main+Support arm은 앞으로 뻗은 단순 Firearm Grip pose를 사용한다. 별도 authored
  fire/recoil animation clip은 0이고 Host Unity Rigidbody/Joint가 bounded recoil impulse·torque를 적용해
  해당 팔과 Weapon을 위로 움직인 뒤 spring/damping으로 복귀시킨다. Pistol은 좁은 spread·큰 단발 recoil,
  AK는 연사 중 누적 `RecoilAccumulator/SpreadBloom`으로 덜 정확해지고 release/gap에서 회복한다.
- Pistol, LongGun, Bat, Hammer의 실제 전투를 Alpha에서 시험한다.
- 무기 입력은 W1 비교와 사용자 결정 뒤 구현하며 발사·근접 impact·drop·damage·knockback까지 검증한다.
- Unity Authority는 Rigidbody 이동·Action phase·Hit sweep·impulse를 소유하고 Animator·procedural pose는
  semantic state를 표현한다. Gameplay displacement를 만드는 Root Motion은 0이다.
- Unity 3D Physics fixed-step은 60Hz다. Authority/Network cadence는 Physics 두 step마다 30Hz,
  Snapshot 20Hz는 세 step마다·15Hz는 네 step마다 실행하며 같은 tick으로 합치지 않는다.
- 렌더 FPS가 이동량·Action timer·Hit/Down 결과를 변경하지 않아야 한다. 입력 edge는 시간과 순서를 보존해 고정 step에서 소비하고, 시각 보간 값은 Authority 연산에 되먹이지 않는다.
- Alpha presentation matrix는 locomotion·Jump phase, L/R Punch, L/R Air Kick, Dropkick·별도 Recovery,
  Grab·release Throw(별도 Lift 조작0), Firearm forward-arm Grip+physics recoil, Melee swing와 Ragdoll/shared GetUp을 포함한다.
  후속 ANM은 authored Firearm fire/recoil 및 전용 Jump/착지 clip 없이 이를 production polish한다.
  Jump는 Unity 물리·실제 접촉과 procedural 하체 pose로 표현한다. 기존 Jump Action5는 비교 자료다.

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
BLOCKED, DEFERRED, REWORK_REQUIRED를 사용한다. `REWORK_REQUIRED`는 당시 PASS Evidence를 실패로 다시 쓰지 않고,
최신 사용자 시각 결정 때문에 그 산출물을 current acceptance 입력에서 제외한다는 뜻이다.

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
| FDN-011 | 1 | Repository ignore·attribute·large binary policy 구성 | FDN-001 | SYS,ART | `.gitignore`·`.gitattributes`·binary policy/inventory + LFS/Remote guard | Unity 생성물 제외·source/meta/lock 추적, initial binary20·living pre-production binary18 hash 일치; private origin/main 초기 push, repo-local Git LFS3.8·source pattern10, FDN 당시 file/candidate0; current r04 1/1·fresh fetch는 C1B-003 소유, 기존 PNG migration·history rewrite0 | — | PASSED |
| FDN-002 | 1.5 | Unity URP project 생성·검증 | FDN-010..011 | SYS,NFR | `Project hotfix` Unity client | Unity6000.3.9f1·URP17.3.0 import/C# compile error0, package/source change0, Player Build0 | — | PASSED |
| FDN-003 | 1.5 | Simulation·Presentation·Input·Transport 모듈 경계 생성 | FDN-002 | SYS | Contracts leaf + 4 runtime asmdef graph | project edge4·cycle0, Presentation→Simulation path0, folder ownership 일치, Unity compile0·EditMode4/4, gameplay code0 | — | PASSED |
| FDN-004 | 1 | Input package 선택 | FDN-002..003 | INPUT | InputPackageDecision | InputSystem1.18.0 Registry/direct·New-only 채택, Legacy/Both·다른 module ref0, Input test4/4·전체8/8; action map은 INP-001 소유 | — | PASSED |
| FDN-005 | 1 | Physics package와 fixed-step 선택 | FDN-002..003 | PHYS | PhysicsPackageDecision | built-in PhysX·Physics60Hz/Authority30Hz, guard4/4·isolated PlayMode contact/joint2/2·전체 EditMode12/12, Player Build0 | — | PASSED |
| FDN-006 | 1.5 | P2P transport adapter와 Alpha direct 구현 방향 선택 | FDN-002..003 | NET | TransportPackageDecision | UTP2.6 Registry/direct·Transport sole owner, NGO/Services/Relay/Lobby0, adapter seam, Transport4/4·전체16/16 | — | PASSED |
| FDN-007 | 1 | Renderless SimulationHarness·EditMode·PlayMode·Evidence 기반 생성 | FDN-002..003 | SYS,NFR | kernel+harness+strict validators | 동일 runtime kernel Unit3/Edit2/Play1, 전체 Edit21/Play3·core skip0, legacy Evidence35+strict schema 검증 | — | PASSED |
| FDN-008 | 1 | Settings·Preset local atomic storage 기반 생성 | FDN-001 | APPEAR,UI | bounded byte envelope/current·last-good·pending repository + LocalStorageProfile | version·length·SHA-256와 validator-aware 복구, 동일 process 다중 instance 직렬화, storage Edit19·boundary2·Play1/전체 Edit42·Play4, server dependency 0 | — | PASSED |
| FDN-009 | 0.5 | 금지 인프라 guard 생성 | FDN-001 | SYS,SEC | versioned policy + Git inventory/content/package audit + adversarial fixtures | 최종 inventory259·content83·manifest2에서 Backend·DB·Docker/Container·Dedicated·audit 위반0, self-test14/245·전체 Edit42/Play4 | — | PASSED |
| ART-001 | 1.5 | LowPolyStyle·ModelInterop·AlphaVisualQA Profile 최초 작성 | FDN-002,FDN-010 | ART,NFR | 세 versioned START profile + semantic/scope validator | unit·axis, palette role·bevel class, FBX/ModelImporter preset, neutral QA·2/3/4×세 화면비 capture 기준과 owner/version 누락0; tests24/206·scope asset/capture0·전체 Edit42/Play4 | — | PASSED |
| LIC-001 | 1 | Third-party package·font·audio·asset license/NOTICE inventory 생성 | FDN-004..006,ART-001 | SYS,SEC,ART | living policy·inventory·NOTICE index + fail-closed verifier | lock58/58(direct46/transitive12) source·version·license·NOTICE disposition, cache58/58·locator40; C2PA review18 shipping0과 first-party seam 분리, LIC r02 snapshot firstParty0·mutation22/176; current r03 firstParty9·shipping0·mutation26/199는 C1B-003 소유; source-unproven3+Unity tutorial/readme14 제거, package change·Build0, final Player audit는 BLD-001/ALP-001 | — | PASSED |
| BLD-001 | 1.5 | Windows x64 Build Profile·PlayerSettings·Scene list 준비 | FDN-002..003,LIC-001 | SYS,NFR | Unity-generated Development·Steam Reserved profiles + policy/manual + guard | profile2·GUID2, Windows x64 Player/Mono, dev/SteamReserved define 상호배타, 임시 identity4, 공용 SampleScene1 START·release-ready0, profile별 Quality/Graphics/PlayerSettings override0, Steam SDK/AppID/기능0; static17/100·전체 Edit52/52·Play4/4, Player Build·BuildAndRun·배포0 | — | PASSED |

### 5.2 C1b exact profile

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| C1B-001 | 0.5 | v0.13 C1a 승인 source 고정 | 역사적 승인 | CHAR | source record | 승인 파일·hash 일치 | UG-C1A | PASSED |
| C1B-002 | 1.5 | 역사 normalized proportion 후보 | DOC-005,C1B-001,ART-001 | CHAR | `CharacterProportionProfile-C1B-002-r01` + Evidence | 당시 H1·landmark17·bounds 기술 PASS 보존; current visual acceptance=`REWORK_REQUIRED`, superseded | — | REWORK_REQUIRED |
| C1B-003 | 2 | 역사 6-part faceted Blockout | C1B-002 | CHAR | Blender source·render8·Evidence | 당시 source/render/LFS 기술 PASS 보존; current visual acceptance=`REWORK_REQUIRED`, superseded | — | REWORK_REQUIRED |
| C1B-004 | 1.5 | 역사 static Pose8·4인 lineup | C1B-003 | CHAR | pose/lineup bundle·Evidence | 당시 기술 PASS 보존; current visual acceptance=`REWORK_REQUIRED`, 새 Neutral에서 재파생 | — | REWORK_REQUIRED |
| C1B-005 | 1.5 | 역사 FBX/Unity static parity | C1B-003..004,FDN-002 | CHAR | FBX·Prefab·capture8·Evidence | 당시 parity 기술 PASS 보존; current visual acceptance=`REWORK_REQUIRED`, superseded geometry 전달 결과 | — | REWORK_REQUIRED |
| C1BRW-001 | 1.5 | 현행 r16 방향 profile·역사 구분 기록 | C1B-001,DOC-005,ART-001 | CHAR | r16 승인 source identity + 역사 결정 | r16 Neutral 방향 승인 반영. old/r01..r15 역사 보존, current measurement/profile Evidence 정리 | — | EVIDENCE_PENDING |
| C1BRW-002 | 2 | r16 Neutral/T-pose 기술 결과 정리 | C1BRW-001 | CHAR | r16 source·BodyCrotchFairQAReport·render12 | 기존 technical PASS와 source/report hash 일치. 미등록 asset inventory·현행 Evidence 연결은 정리 중; 새로운 외형 제작0 | — | EVIDENCE_PENDING |
| C1BRW-003 | 0.5 | r16 Neutral 방향 사용자 승인 기록 | C1BRW-002 | CHAR | UG-C1B-NEUTRAL-r16-20260902 | 2026-09-02 NeutralApprovalRecord의 시각 방향 승인 완료. production topology·gameplay rig·Animation·Collider·UG-C1B 승인과 구분 | UG-C1B-NEUTRAL | PASSED |
| C1BRW-004 | 1.5 | r16/r20 기반 공식 정적 Pose8·4인 lineup 검토 | C1BRW-003 | CHAR | r20 공식 Pose8·lineup2·추가 표본5·render32·검토 report | 원본 hash 불변, exact Pose8·overlap/spread, visible open hole·exposed cap0, static 방향 판독; authored Action/Animation claim0. r17 report만으로 완료 처리0, 이전 r20 시험 포즈8 대체0; 현재 표본13 구조 PASS·시각 승인 대기 | — | USER_REVIEW |
| C1BRW-005 | 1.5 | 승인 Neutral의 static FBX/Unity parity 재검증 | C1BRW-004,FDN-002 | CHAR | new static FBX·identity Prefab·capture8 | source 불변·manual correction0, scale·axis·surface·four-view parity와 LFS/license. 반입 산출물의 Rig/Collider/Animation·Build0, 기존 Blender 검토 리그 보존 | — | NOT_STARTED |
| C1B-006 | 0.5 | exact profile 최종 사용자 승인 | C1B-001,C1BRW-001,C1BRW-002,C1BRW-003,C1BRW-004,C1BRW-005 | CHAR | approved profile | Neutral Gate를 통과한 ID·version·수치·Pose·Unity 결과 명시 | UG-C1B | BLOCKED |

2026-09-05 다음 Jump/착지 preflight에서 FK70°/80°와 발 접지 큰 굽힘의 하체 surface foldover를 재현했다.
[하체 변형 감사](../artifacts/reports/R20_CROUCH_DEFORMATION_AUDIT_20260905.md) 이후 사용자가 weight-only 후보 제작을 승인했다.
[하체 웨이트 후보](../artifacts/reports/R20_LOWER_WEIGHT_REVIEW_20260905.md)는 92표본 표면 검사 PASS이며 후속 사용자가 이 후보로 진행하도록 승인했다.
후속 [점프·착지 Action5](../artifacts/reports/R20_JUMP_LANDING_REVIEW_20260905.md)는 사용자 결정으로 비교 자료가 됐다.
[물리 점프 전환 승인](../artifacts/reports/R20_PHYSICS_JUMP_DECISION_20260905.json)에 따라 전용 clip 제작을 중단하고
local Unity Rigidbody·접촉 기반 점프와 procedural 하체 pose를 구현한다. 준비 영상의 길이를 입력 지연으로 사용하지 않는다.
검토용 capsule collider와 승인 모델 transport는 최종 CHR-001/002 Prefab·joint profile 승격이 아니다.
[Unity 물리 점프 검토](../artifacts/reports/R20_UNITY_PHYSICS_JUMP_REVIEW_20260905.md): EditMode85/85·PlayMode14/14,
승인 skin의 clip0 실제 재생 검증. 보행·최종 지형 발 IK·Down/GetUp·network 통합은 후속이다.
후속 [점프 전환 검토](../artifacts/reports/R20_JUMP_TRANSITION_REVIEW_20260905.md)에서 local 보행/Sprint·yaw torque와
8방향 착지·공중 방향 변경을 연결했다.18조건·EditMode85/85·PlayMode15/15 통과, stance foot 고정·화면축 본게임 입력·최종 통합은 남아 있다.
후속 [발 접지 스타일 비교](../artifacts/reports/R20_FOOT_CONTACT_STYLE_REVIEW_20260905.md)는 정속 직진6조건을 측정했다.
당시 빠른 발/느린 이동 후보는 채택하지 않았다. 후속 사용자 지시로
[ReadableGait 재작업](../artifacts/reports/R20_READABLE_GAIT_REWORK_20260906.md)을 기본 시제품에 적용했다.
기존 이동 속도·주기를 유지하며 발 들림·보폭·골반 체중 이동을 보강했고 EditMode85/85·PlayMode19/19과 전 주기 표면 검사를 통과했다.
기존 승인 펀치와 원본 r16/r20은 유지하며 전체 ANP-002는 IN_PROGRESS다.
후속 [월드 발 고정 검사](../artifacts/reports/R20_WORLD_FOOT_PLANT_FEASIBILITY_20260906.md)에서 실제8방향·16조건의 도달 범위를 확인했다.
이전 속도·주기의 지지 전체 고정은 불가능해 속도·발놀림 절충 후보를 비교했고, 후속 사용자가 프로토타입 기준으로 채택했다. 최종 접지/캐릭터 Gate 완료 승격은 없다.
[접지 절충안](../artifacts/reports/R20_FOOT_PLANT_CANDIDATE_20260906.md)을 당시 기본 프로토타입 실행에 적용했다. 현재 Shared Arena 속도는 아래2026-09-10의2배 승인으로 갱신됐다.
Walk0.70/Sprint1.05m/s·주기3.0/4.5Hz로 평지 정속 고정16조건, 점프 전환18조건과 PlayMode21/21을 통과했다.
프로토타입 사용은 승인됐다. 최대6.8mm 표면 접촉 잔차·지형/게임 통합과 최종 체감 검증은 남아 있다.
같은 맵의 두 캐릭터로 이동·추격·회피·점프·기본 타격/잡기를 시험할 수 있는 첫 통합 프로토타입에서 사용자와 속도·보폭·모델/리그를 재평가한다.
대표 구간 이동 시간, 추격/회피 반응, 점프/공격 거리와 동작 판독을 기록해 CHR-012/QA-002 검토에 연결한다. 지금 모델/리그를 재작업하지 않는다.
후속 [승인 펀치 Unity 조합](../artifacts/reports/R20_UNITY_PUNCH_INTEGRATION_20260906.md)을 ANP-002의 독립 표현 검토로 연결했다.
승인 원본 일치170표본과 접지 이동·점프·좌우 소유권·중단/Reset8조건, EditMode85/85·PlayMode23/23을 통과했다. 실제 Hand Intent·Hit/Grab physics는 후속이며 CHR-006/007 전체 착수·완료나 선행 Gate 통과로 승격하지 않는다.
그다음 [손 입력·타격 시제품](../artifacts/reports/R20_HAND_STRIKE_REVIEW_20260906.md)에서 Ground tap/hold·권한 phase/sweep·Target dedupe·실제 bounded impulse·Esc/Reset을 연결했다.
150ms는 문서 START, 타격 반경0.045m·충격20N·s(한 hit 속도 변화1m/s 상한)는 비교 시작값이다. UG-HAND와 최종 전투 tuning 승인을 대신하지 않는다.
사용자가 승인한 프로토타입 진행 범위의 부분 구현을 CHR-006·INP-002/003의 IN_PROGRESS로 추적한다. 정식 선행 Gate 완료를 뜻하지 않는다.
독립 로컬 검토이며 정식 InputAction map·Grab joint/Release/Break·피해/Down·Air attack·peer는 미완료다. 다음은 GrabSeek의 실제 접촉·제한 joint와 반대 손 Punch 조합이다.
후속 [Grab 물리 시제품](../artifacts/reports/R20_GRAB_PHYSICS_REVIEW_20260906.md)에서 contact/joint·Release/Break·하중 피로와 팔 IK를 연결했다.
사용자가 하중에만 피로를 누적하도록 선택했다.22조건·256표면 표본과 EditMode96/96·PlayMode25/25를 검토했으며,
CHR-006/007은 local 부분 구현으로 추적한다. Lift/Throw·피해/Down·static Ledge·실제2인 조작과 최종 Gate는 후속이다.
그다음 [들기·던지기 공통 물리 API/pose](../artifacts/reports/R20_LIFT_THROW_PHYSICS_REVIEW_20260906.md)를 검토했다.
사용자가 별도 들기 대신 **클릭 유지 Grab→버튼 release Throw**를 선택했다. [Release Throw 입력 검토](../artifacts/reports/R20_RELEASE_THROW_REVIEW_20260906.md)에 local 입력 연결을 추적한다.
동일 대상은 마지막 본인 손 release에1회, 서로 다른 대상은 독립 처리하며 강제 취소는 impulse0이다. 정식 InputAction map·통합/peer·연출은 후속이다.
후속 사용자 요청으로 [잡기 점프의 부분 하중 보정](../artifacts/reports/R20_HELD_JUMP_WEIGHT_20260906.md)을 CHR-005/006 local 시제품에 적용했다.
현재75% 지원은 같은 체급 수동 상대를 잡을 때 solo보다 낮게, 무보정보다 높게 뛰도록 한다. 양손 대상 중복·동시 점프 중첩·무효 대상 보정을 방지한다.
중력·질량은 유지하며 추가 운동량/지원 비율 상한은 START다. 최종 재미 검증은 통합 플레이테스트에서 수행한다.
다음 [넉다운 상태 검토](../artifacts/reports/R20_DOWN_STATE_REVIEW_20260906.md)는 CHR-008 local 시간/누적·행동 잠금과 기존 CHR-009 방향 회복 gate를 연결한다.
실제 Ragdoll/GetUp 운동·pose와 공격별 피격→Down 기준은 후속이다. 사용자 결정으로 던진 뒤 일반 충돌 Damage/피해 누적은0이며, 충돌 기반 자동 Down은 미승인이다. 테스트 timing을 기본 게임 tuning으로 채택하지 않는다.
후속 [실제 지지면·기립 공간 query 검토](../artifacts/reports/R20_RECOVERY_SUPPORT_REVIEW_20260906.md)에서 CHR-009 local 회복 시작/중단 gate를 연결한다. query는 몸을 이동시키거나 GetUp 완료를 대신하지 않는다.
다음 [래그돌·물리 회복 후보](../artifacts/reports/R20_RAGDOLL_RECOVERY_REVIEW_20260906.md)는 실제12 body 물리와 회복 보조·이동 복귀를 별도 장면에서 연결한다. 손 짚기 clip 제작이나 기본 시각 승인을 대신하지 않는다.
2026-09-07 사용자가 물리 회복의 느낌을 확인하고 애니메이션 추가를 요청했다. [앞뒤 GetUp clip2 후보](../artifacts/reports/R20_GETUP_ANIMATION_REVIEW_20260907.md)를 기본 검토 장면에 연결한다. 기존 물리-only 검사는 비교 이력으로 유지한다.
사용자가 회복이 어색하다고 지적해 [무력 래그돌·단계별 회복 수정](../artifacts/reports/R20_NATURAL_GETUP_REVIEW_20260907.md)을 진행한다. 기존 clip의 기술 PASS를 시각 승인으로 해석하지 않는다.

2026-09-09 확정 정책: [빠른 펀치·부위별 누적 후보](../artifacts/reports/R20_FAST_PUNCH_PRECISION_20260909.md)는 몸통·팔다리1점, 머리2점으로 누적한다. 넉다운 기준은10→9→8→7→6점에서 멈추며 기존 groggy 대기 시간 증가도 유지한다. 누적은 시간이 지나도 감소하지 않고 새 Down에서 소비하며, Round reset은 누적과 DownCount를 초기화한다. Down 중 추가 타격은 물리 반응만 적용하고 점수·Count·timer를 늘리지 않는다. 머리 보너스는 실제 머리 접촉에만 적용한다. 기존 항상5회 고정은 이전 비교 정책이다. 후속 펀치 작업은 이 새 profile을 기준으로 한다. 코드의 기존5회 기본값은 이전 비교 harness 호환용이며 최신 검토 메뉴는 부위 점수 profile을 명시적으로 활성화한다.

2026-09-09 [광배·옆구리 가중치 후보](../artifacts/reports/R20_LAT_WEIGHT_ADJUSTMENT_20260909.md)는 몸통의 어깨/상완 영향 일부를 기존 Spine/Chest 슬롯으로 옮긴다. 사용자 후속 진행 지시로 다음 로컬 시제품과 현재 검토 메뉴의 기준으로 채택했으며 원본 source·이전 transport·하체 가중치·리깅·동작 곡선을 보존한다. 펀치 속도와 피격/회복 정책 변경은 없다.

2026-09-09 [공용 카메라·화면축 이동 시제품](../artifacts/reports/R20_SHARED_ARENA_REVIEW_20260909.md)은2·3·4명과16:9/16:10/21:9에서 authority Root의 고정 범위를 사용한다. fixed yaw, pitch34도/FOV42도/Dolly8~15CU/여백15%를 START로 검토하며 local 화면축 WASD·개발용F1~F4 조작 전환·Esc 비정지 입력 중단·전체 review reset을 연결한다. 실제 PC별 동시 조작, peer 목표 전송, 최종 P00/occlusion·hazard framing·UG-CAM과 정식 InputAction/Match UI는 후속이다.

2026-09-10 사용자가 비교 영상의 [공용 카메라 보행 후보](../artifacts/reports/R20_CAMERA_GAIT_CANDIDATE_20260910.md)를 더 자연스럽다고 승인했다. 해당 설정을2·3·4인 Shared Arena의 기본 보행으로 채택한다. 당시의 속도0.70/1.05m/s와 주기2.4/3.6Hz는 이후 아래의2배 이동 속도 승인으로 갱신됐다. `Open Shared Arena 2/3/4 Players`가 채택 보행을 사용하며, `Open Previous Shared Gait 2/3/4 Players`는 이전 보행 비교를 제공한다. 고정60Hz 연산과 snapshot 보간을 유지하며, 원본 mesh/rig/weight와 최종 캐릭터 Gate 상태는 유지한다.

2026-09-10 프레임 독립성 요구를 반영해 [고정 시점 연산·렌더 보간 검증](../artifacts/reports/R20_FRAME_INDEPENDENCE_REVIEW_20260910.md)을 추가한다. 로컬 prototype 입력·이동·손 동작·Down/GetUp·보행 및 카메라 상태는 Physics60Hz에서 갱신하고, 렌더는 이전/현재 snapshot만 보간한다. 마우스 tap/hold는 원본 event timestamp를 사용한다. 렌더된 bone/root를 다음 물리 진입값으로 읽지 않는다. 서로 다른 렌더 일정에 같은 고정 입력을 재생해 gameplay 결과와 trajectory를 비교하고, 실기기 성능·peer 전송 검증은 해당 후속 Gate에서 수행한다.

2026-09-10 사용자가 [2배 이동 속도](../artifacts/reports/R20_MOVEMENT_SPEED_REVIEW_20260910.md)를 기본으로 선택했다.2·3·4인 Shared Arena는 걷기1.40m/s·달리기2.10m/s·가속도20m/s²와 검증한 보행 주기4.8/7.2Hz, 발 들림16mm·골반 bob12mm를 사용한다. 기본 factory의 고정60Hz·FPS 독립성과 잡고 이동/점프/Throw를 재검증했다. 사방 분산 시 기존 카메라 한계에 더 빨리 닿는 점은 맵/카메라 통합에서 계속 확인한다.

2026-09-10 사용자가 [8.8×6.2CU 경기장·최대22CU 카메라](../artifacts/reports/R20_ARENA_BOUNDARY_REVIEW_20260910.md)를 기본 시제품으로 채택하고 가장자리 복귀 진행을 승인했다. 동일 geometry의2/3/4 Spawn, 실제 core Collider OOB·같은 tick의 camera 제외·강제 grip 해제/초기화를 검토했다. 손 접촉 Ledge Grab·ClimbAssist, Hazard와 Round/peer 통합은 후속이며 최종 P00/Camera Gate 완료는 아니다.

2026-09-10 후속 [가장자리 복귀 시제품](../artifacts/reports/R20_LEDGE_RECOVERY_REVIEW_20260910.md)을 기본2/3/4인 장면에 연결했다. 정적 Lip/Facade의 실제 손 접촉과 클릭 유지, Space 보조(공중당2회·350ms), 하중 피로·놓기·재잡기·OOB/Reset을 검토한다. 상승은 옥상 위0.15CU를 목표로 필요한 만큼만,1회 최대1.25CU인 START다. 공중 벽 마찰에 의한 무입력 매달림을 제거하고 지상 마찰은 유지한다. 움직이는 표면·Beam·Hazard·Round/peer와 최종 체감 Gate는 후속이다.

2026-09-10 후속 [하중에 반응하는 매달림 자세](../artifacts/reports/R20_HANGING_LOAD_REVIEW_20260910.md)를 정적 가장자리 장면에 추가했다. 실제 손별 지지력을 읽어 한 손은 잡은 어깨 아래로 골반을 기울이고, 양손은 가운데로 처지며 힘을 나눈다. 손 전환·놓기·상승 시 고정60Hz에서 감쇠해 이어지며, 표면 접촉은 IK로 유지한다. 이 자세는 절차적 표현이고 기존 지지력·피로·도달 판정·복귀 예산은 그대로다. 원본 모델·리그·weight를 수정하지 않았으며 사용자 체감 확인은 남아 있다.

2026-09-11 사용자가 위쪽 잡기 제안을 승인하고 상호 잡기 시 서로 들리지 않도록 요청했다. [자동 상향 들기·상호 잡기 차단](../artifacts/reports/R20_AUTOMATIC_LIFT_REVIEW_20260911.md)을 기본 정적 가장자리 시제품에 연결한다. 실제 접촉 뒤 유지하면 점차 들어 올리고, 마지막 손을 놓으면 준비 완료를 기다리지 않고 던진다. 모든 손의 입력/접촉 확정 뒤 상호 관계를 검사해 같은 tick에서 양쪽 상승을 차단한다. 정적 벽의 높은 접촉·하중 자세·복귀도 연결하며 정식 통합 Gate 승격은 없다.

2026-09-17 사용자가 서 있는 상대·상호 잡기 후보를 시각 승인해 로컬2인 잡기 검토2단계를 마치고 다음 진행을 요청했다. [물리 팔 펀치·정밀 접촉 후보](../artifacts/reports/R20_PHYSICAL_PUNCH_CANDIDATE_20260917.md)는 승인된 빠른 펀치를 관절 힘으로 따라가며 실제 손끝 접촉으로만 명중을 기록한다. 짧은 클릭/유지 잡기/해제 던지기를 연결하고 머리2점·몸통/팔다리1점을 누적한다. 이 단계는 첫10점의 Down 요청까지이며 실제8→12몸체 Down·회복 전환과 잡기 유지 인계는 아직 연결하지 않았다. 기존 기본 장면과 정식 캐릭터/네트워크 Gate는 변경하지 않는다.

사용자는 이전 빠른 펀치를 ‘툭 건드리는’ 느낌으로 평가해 체감 승인을 하지 않았다. [준비·가격·힘 전달 재수정 후보](../artifacts/reports/R20_PUNCH_FOLLOW_THROUGH_20260917.md)는 팔꿈치를 먼저 깊게 접고 어깨를 준비한 뒤 빠르게 뻗으며, 명중 직후 시간을 건너뛰어 회수하던 동작과 손의 역방향 보조 impulse를 제거한다. 몸통의 작은 물리 목표·실제 손끝의 fixed-step 경로 판정·시작 거리로 고정하는 초근접 직선 궤적을 함께 검토한다. 제자리/이동 연타는 각각 첫10명중을 확인했다. 전체0.4초·머리2/몸통·팔다리1점·잡기 유지/해제 던지기는 유지한다. PlayMode19/19·EditMode146/146 및15/60/144FPS 결과 일치를 확인했다. 접촉 후 표시 피부 간격 최대6.97mm는 보완 대상으로 남기며, 사용자 체감 승인 전 다음 Down·회복 단계로 진행하지 않는다.

2026-09-18 최신 진행: 사용자가 흐물거리는 펀치의 재설계 진행을 요청했다. [1단계 참고 원형](../artifacts/reports/R20_PUNCH_REFERENCE_20260918.md)을 전용 무물리 장면으로 제작하고 정면/측면·좌우 단발을 검토한다. Rigidbody/Collider0, 대칭·접지·원위치 복귀와 공유 로더 경로3/3 검사를 통과했다. 기존 물리 펀치·판정·모델은 유지한다. 원형 사용자 승인 뒤 자세·속도 기반 물리 추종과 실제 피격 반응을 검토하며, 이번 결과로 전투 타격감 또는 통합 승인을 가정하지 않는다.

2026-09-17 사용자가 누운 상대 접촉 후보를 승인하고 다음 진행을 요청했다. [서 있는 상대·상호 잡기 접촉 후보](../artifacts/reports/R20_STANDING_SKIN_CONTACT_20260917.md)에서 양쪽 피부를 고정 물리 상태로 계산하고, 팔끼리의 같은 접촉이 중복 처리되지 않도록 연결한다. 손별 소유권을 유지해 한쪽 release가 상대 잡기를 없애지 않으며, 상호 자동 들기 차단과 유효 마지막 손 release Throw를 함께 확인한다. 별도 메뉴에서 검토하며 원본 모델·기본 Shared Arena·정식 캐릭터 Gate는 변경하지 않는다.

2026-09-17 사용자가 들기 후보를 시각/체감 승인하고 다음 진행을 요청했다. 승인한80% 보조·들기 목표·즉시 놓기 던지기를 기준으로 [손/피부 접촉 보완 후보](../artifacts/reports/R20_SKIN_CONTACT_CANDIDATE_20260917.md)를 별도 래그돌 검토 장면에 추가한다. 렌더 보간과 분리된 고정 자세에서 실제 피부 삼각형과 접점을 계산하고, 손·전완 주변 접촉면이 상대 물리 몸체에 힘을 전달한다. 원본 모델·팔 길이·래그돌 자세 drive는 바꾸지 않는다. 이 범위는 서 있는 상대/상호 잡기·새 물리 펀치·상태 전환·게임 전체 통합 완료를 뜻하지 않는다.

2026-09-17 사용자가 **잡아 들고 있는 동안만 부분 중력 보조**하는 방식을 승인했다. [래그돌 들기·놓기 던지기 후보](../artifacts/reports/R20_LIMP_LIFT_ASSIST_20260917.md)를 B 기반 별도 장면에 연결했다. 대상의20kg·관성·중력 설정과 자세 drive0을 유지하며, 유효 잡기/보유자 지지 조건에서만 대상당 한 번 보조하고 해제·상실·공중 전환에 중단한다. 상호 잡기는 추가 자동 상승을 차단한다. 현재80% 보조·팔 구동력·목표 높이는 체감 검토용 수치다. 이번 승인은 들기/해제 후보 범위이며 손/피부 접촉면 보완 승인이나 전체2단계 완료,3단계 전체 완료를 뜻하지 않는다. 기본 Shared Arena·승인 자산은 자동 교체하지 않는다.

2026-09-16 B 거리·높이 반응형 선택 후 [어깨 보완·낮은 래그돌 잡기 후보](../artifacts/reports/R20_SHOULDER_REPAIR_LOW_GRAB_20260916.md)를 구현했다. 기존 메쉬와22개 뼈를 보존하고 피부 보조 뼈2개를 추가한 별도 후보에서 어깨 검사와 네 방향 래그돌의 잡기·끌기·해제를 검증했다. 골반과 상체를 함께 낮추며 대상의 자세 drive0·20kg을 유지한다. 일부 손과 피부 간격이 최대 약3.6cm 남아 접촉면 보완의 추가 진행 여부를 확인 중이다. 전체2단계는 미완료이며3단계 펀치·던지기는 미착수다. 기존 승인 자산·기본 Shared Arena와B 기본 메뉴의 모델은 자동 교체하지 않는다.

2026-09-13 후속 [복귀 중 잡기 자세 반복 수정](../artifacts/reports/R20_CLIMB_REGRAB_FIX_20260913.md): 영상 자동 입력이 충분한 상승 뒤에도 클릭을 재입력하던 경로를 수정했다. 실제 연속 클릭의 입력/물리 정책은 유지하며, 깊은 곳의 필요한 두 번째 재잡기는 보존한다.

2026-09-13 사용자 진행 승인으로 [잡기 접촉·벽 복귀 후보](../artifacts/reports/R20_GRAB_CONTACT_CANDIDATE_20260913.md)를 별도 검토 장면에 구현했다. 작은 손·팔 보정과 벽 매달림 간격/등반 종료 표현을 시험하며 기본 Shared Arena는 기존 승인본을 유지한다. 안정된 매달림은 개선했으나 잡기 직전·등반 전환·상호 잡기의 관통은 미해결이다. 상호 접촉을 손목·전완으로 바꾸는 선택과 벽 접근·복귀 때 빈팔을 위로 접어 피하는 전환 동작은 사용자 답변 대기이며 아직 규칙으로 채택하지 않는다.

2026-09-11 손·팔 관통 수정안은 승인된 모션을 과도하게 바꿨다는 사용자 지적으로 거절됐다. [해당 수정안](../artifacts/reports/R20_GRAB_CONTACT_CLEARANCE_20260911.md)은 철회하고 직전 자동 들기·상호 들기 차단·상향 벽 잡기 버전으로 복원했다. 관통 문제는 미해결이다. 후속 수정은 승인된 모션의 형태와 동작 의도를 유지해야 하며, 이를 바꾸는 안은 적용 전에 사용자 확인이 필요하다.


2026-09-08 이전 이력: [골반·하체 회복 수정](../artifacts/reports/R20_PELVIS_GETUP_REVIEW_20260908.md) 이후 [실제 피격→Down 연결](../artifacts/reports/R20_HIT_DOWN_INTEGRATION_REVIEW_20260908.md)을 검토했다. 당시 사용자 선택은 [고정 타격 수·대기 시간 증가·무력 ragdoll](../artifacts/reports/R20_FIXED_HIT_LIMP_DOWN_REVIEW_20260908.md)이다. 당시 START는 항상 펀치5회였으며 이전5→4→3→2 감소를 제거했다. DownCount는 대기 시간에만 적용하고, Down 중 clip/blend/자세 drive는0이다. CHR-008/009는 로컬 부분 구현 상태이며 최종 physics profile·게임/peer·balance Gate는 후속이다.

사용자 후속 지시로 [골반·Spine·Chest 물리 분할](../artifacts/reports/R20_ARTICULATED_TORSO_REVIEW_20260908.md)을 별도12 body 후보로 검토한다. 기존20kg·rig/skin·고정 타격/누적 시간 규칙을 유지하고, 실제 torso 운동과 GetUp 및 반복 Down의 joint 재활성화 방향을 검증한다. 정식 CHR-002/CHR-012 Gate나 다인 성능 완료를 대신하지 않는다.

(2026-10-20 대체: r46 시제품은 잡힌 채 대기가 끝나면 버둥거린 뒤 빠져나온다. PRD 2026-10-20 결정 참조.) [잡기·하중·회복 연결](../artifacts/reports/R20_RAGDOLL_GRAB_INTEGRATION_20260908.md)의 로컬 구현은 분할된 대상의 전체 질량, 실제 부위 접점, 중복 없는 점프/Throw와 상태 전환 시 grab 유지를 반영했다. 사용자는 지지/공간이 확보되면 잡힌 상태에서도 회복하도록 선택했다. 정식 Input/peer/다인 체감 Gate는 후속이다.

2026-09-05 후속 진행 지시로 ANP-002의 독립 검토용 Punch L/R Action2와 Grab/Wall L/R/LR IK 시나리오를 제작했다.
200표본·저장 Action98샘플은 통과했으나 게임용 Prefab·Hit/contact·physics/전환 통합은 미완료다.
검토용 선행 산출물을 Unity 반입·UG-C1B·CHR-007/011 완료나 전체 ANP-002 PASS로 승격하지 않는다.

2026-09-05 추가 사용자 지시로 CHR-009의 방향 판별·side 정렬/state 기반을 선행 구현했다.
이는 독립 prototype 범위이며 CHR-008/CHR-002 물리 통합의 선행 Gate를 완료한 것으로 표시하지 않는다.
보행 cycle/Grip 변형 검토는 C1BRW-004의 motion preflight이며 ANP-002 게임용 clip 완료가 아니다.

2026-09-05 현황 정리는 기존 사용자 승인 날짜를 보존한다. C1BRW-003의 PASSED는 Neutral 방향에 한정하며
선행 001/002의 EVIDENCE_PENDING은 현행 profile·목록·증거 연결의 정리 상태다. 새 외형 승인 Gate를 다시 열지 않는다.
r18..r20은 CHR-001의 Blender 검토용 선행 산출물로 존재하지만 Unity Prefab·anchor와 UG-C1B가 미완료여서
CHR-001 전체는 NOT_STARTED를 유지한다. CharacterRecoveryStateMachine은 CHR-008/009·AIR-002 관련 미통합 초안이다.

위 네 역사 Task의 당시 기술 Evidence `PASSED`는 그대로 보존한다. 상태 열의 `REWORK_REQUIRED`는 최신 active visual
acceptance만 철회했음을 뜻하며 C1BRW chain이 이를 대체한다. 과거 Evidence를 FAILED로 다시 쓰지 않는다.

## 6. G1 — Offline gameplay Alpha core

### 6.1 Character·Input

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| CHR-001 | 1.5 | 승인 C1b Skeleton·Prefab 구성 | C1B-006,FDN-002 | CHAR | prototype prefab | visible hand/finger/toe 0, Hand/Strike/Grab·Kick L/R·Dropkick anchor 존재 | — | NOT_STARTED |
| CHR-002 | 2 | Collider·mass·joint profile 구성 | CHR-001,FDN-005 | PHYS,CHAR | physics profile | 모든 player 동일, dynamic MeshCollider 0 | — | NOT_STARTED |
| CHR-003 | 1.5 | 화면축 이동과 자동 facing 구현 | CHR-002 | GAME,PHYS | locomotion | 8방향 이동·turn 안정, teleport rotation 0; local 화면축 이동·yaw torque·점프 전환/공용 카메라 시제품, 최종 authority 통합 후속 | — | IN_PROGRESS |
| CHR-004 | 1 | Left Shift hold Sprint 구현 | CHR-003 | INPUT,PHYS | sprint profile | Lobby/Match 동일, stamina 0, 배율 조정 가능; local Shift 배율/보행 cadence·착지 전환 시제품, 상태 잠금·Lobby/Match 통합 후속 | — | IN_PROGRESS |
| CHR-005 | 1.5 | Jump·buffer·coyote 구현 | CHR-003 | INPUT,PHYS | jump controller | local Rigidbody·실제 접촉·buffer/coyote·edge/slope 중복 점프 방지 시제품; 최종 motor/input·Down/reset/network 통합과 체감 검토는 후속 | — | IN_PROGRESS |
| CHR-006 | 2 | 좌우 Strike·Grab·GripStress 구현 | CHR-002,INP-002 | GAME,PHYS | hand interaction | 좌우 독립, hold 전 Strike 0, 무한 grab 0; local Strike·contact/joint Grab·하중 피로 검토 구현, 최종 physics profile·게임/peer 통합 후속 | — | IN_PROGRESS |
| CHR-007 | 1.5 | Grab release Throw·Grab break 구현 | CHR-006 | GAME,PHYS | throw resolver | release당 중복 impulse 0; local 들기/내리기·명시적 bounded throw·거리/충격/피로/상실 해제 검토. local hold/release·자동 상향 들기·상호 잡기 차단 연결, 별도 Lift 입력0; 연출·정식 입력 map·게임 통합 후속 | — | IN_PROGRESS |
| CHR-008 | 1.5 | DownCount 기반 groggy/Ragdoll 누적 구현 | CHR-002 | PHYS,ROUND | down profile | Match 첫 Down=Base·이후 Increment·Max cap·episode당 1회, Lobby는 Base·count 0; local timed state·행동 잠금/reset 및10 body 무력 Ragdoll/단계별 회복 후보 검토. 피격→Down·시각 승인·최종 profile/통합 후속 | — | IN_PROGRESS |
| CHR-009 | 1.5 | 지지면·4방향 진입·side 정렬 기반 shared GetUp 구현 | CHR-008 | PHYS | recovery state | front/back/left/right·임의 yaw·경사면, stable support+clearance gate, side 정렬→공통 face-up/down, 수평 방향 보존·teleport0; 방향 resolver·side alignment/state 및 local 실제 지지/기립 공간 query 검토. local10 body 무력 Ragdoll·side 정렬·골반 중심 기립·앞뒤 GetUp clip2/접지 보정 후보. 시각/최종 통합 검토 후속 | — | IN_PROGRESS |
| CHR-010 | 1 | Round reset에서 DownCount·Action 일시 상태 초기화 | CHR-006..009,AIR-002,RND-004 | ROUND | reset participant | DownCount·AirAttackToken·DropkickRecovery residue 0, 오래된 transition 0 | — | NOT_STARTED |
| CHR-011 | 1 | Ledge Grab·제한 ClimbAssist 구현 | CHR-005..006 | MAP,PHYS | ledge controller | 실제 접촉+한 손+Jump만; local Lip/Facade grip·하중 피로·공중당2회/350ms ClimbAssist·3깊이×4방향 복귀·한손/양손 하중 반응 자세. 움직이는 surface·정식 profile/peer 후속 | — | IN_PROGRESS |
| CHR-012 | 1 | Character feel·Action 표현 통합 검토 | CHR-003..011,AIR-002,ANP-002,INP-005 | NFR | tuning report | 초기 2/3/4 이동·Punch·Kick·Dropkick·Grab·Ragdoll 비교와 사용자 결정; 전체 playtest는 QA-002 소유 | UG-C2 | NOT_STARTED |
| INP-001 | 1 | Lobby·Match·UI action map 작성 | FDN-004 | INPUT | input actions | Shift/Esc/Tab 포함, generic Interact 0 | — | NOT_STARTED |
| INP-002 | 1.5 | tap Strike·hold Grab resolver 구현 | INP-001 | INPUT | hand state machine | press 즉시 hit 0, sequence당 Strike 1 이하; local Ground resolver·threshold120/150/180ms 경계 검사, 최종 InputAction map/peer 전달 후속 | — | IN_PROGRESS |
| INP-003 | 1 | Esc Cursor open/close와 안전 재무장 구현 | INP-001..002 | INPUT,LOBBY | cursor context | 두 번째 Esc로 복귀, held click 오발 0; local 타격 검토의 cancel/all-up 구현, Lobby/UI 연결 후속 | — | IN_PROGRESS |
| INP-004 | 1 | Tab Hold/Toggle setting 구현 | INP-001,FDN-008 | INPUT,UI | info overlay setting | 두 mode 모두 score+active Patch만 표시 | — | NOT_STARTED |
| INP-005 | 1 | 120/150/180ms 손 임계값 비교·승인 | INP-002,CHR-006 | INPUT | comparison report | false pre-grab Strike 0, 선택값 version 기록 | UG-HAND | NOT_STARTED |
| INP-006 | 1.5 | Match local-only non-pausing menu input·rearm·Leave 구현 | INP-003,RND-001 | INPUT,UI,NET | match menu input context | Simulation pause0, local gameplay neutral, close mouse all-up rearm, explicit Leave command 1회 | — | NOT_STARTED |
| AIR-001 | 1.5 | Ground/Air tap·hold와 dual-click chord resolver 구현 | INP-002,CHR-005 | INPUT,GAME | airborne action resolver | Ground hand Punch/Grab 유지, Air L/R Kick·`DualClickChordWindow 60/80/100ms` Dropkick·Hold Hand/Ledge Grab, episode token 1 | — | NOT_STARTED |
| AIR-002 | 2 | 권한 Air Kick·Dropkick physics·Hit·Recovery 구현 | AIR-001,CHR-002,CHR-006,CHR-008 | GAME,PHYS | air combat runtime/report | KickAnchor L/R, Dropkick action당 target hit 1, bounded forward impulse·air steer 감소; DropkickRecovery DownEpisode·DownCount·TRG-DOWN 0 및 종료 전 새 Attack 0, 2/3/4 기능 일치 | — | NOT_STARTED |

### 6.2 Shared Camera·Round

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| CAM-001 | 1 | authority Root 기반 subject bounds 구현 | CHR-002 | CAM | subject provider | limb·weapon·cosmetic 영향 0; local Root 고정 bounds와 포함 mask 검토, 실제 Alive/Forfeit 연결 후속 | — | IN_PROGRESS |
| CAM-002 | 1.5 | shared focus·dolly solver 구현 | CAM-001 | CAM | camera solver | 개인 yaw/zoom/split screen 0; local 고정 방향 perspective focus/dolly·화면축 이동 검토, peer 목표 전달 후속 | — | IN_PROGRESS |
| CAM-003 | 1.5 | damping·dead zone·look-ahead tuning 구현 | CAM-002 | CAM | camera profile | code 변경 없이 tuning 가능; local damping/dead zone/제한 look-ahead·safe framing 검토, 최종 tuning 후속 | — | IN_PROGRESS |
| CAM-004 | 1 | Lobby·Match·recovery·spectator mask 전환 | CAM-001..003 | CAM,LOBBY | context tests | local core OOB와 같은 tick의 subject 제외·grip/분리 physics 정리·Reset snapshot 검토; 정식 Match/Forfeit/spectator 전환 후속 | — | IN_PROGRESS |
| CAM-005 | 1 | 2/3/4인·세 화면비 사용자 검토 | CAM-003..004 | CAM,NFR | capture bundle | play/spectate 가독성과 편안함 승인 | UG-CAM | NOT_STARTED |
| RND-001 | 1.5 | Match·Round state machine 구현 | FDN-003 | ROUND,GAME | state machine | invalid 전이 거부, Host clock 하나 | — | NOT_STARTED |
| RND-002 | 1 | 3초 countdown·60초·Sudden Death 구현 | RND-001 | ROUND | timer flow | countdown 전 input 0, Host만 시간 판정 | — | NOT_STARTED |
| RND-003 | 1.5 | 점수·4승·동시전멸 처리 | RND-001..002 | GAME,ROUND | result resolver | 2/3/4 winner 규칙 일치 | — | NOT_STARTED |
| RND-004 | 1.5 | Round baseline reset과 Match state 보존 | RND-001..003 | ROUND | reset registry | DownCount 포함 일시 상태 reset, score/map/Patch 보존 | — | NOT_STARTED |

### 6.3 P00·Patch·Weapon

| ID | 일 | 목표 | 선행 | 요구 | 산출물 | 완료 기준 | Gate | 상태 |
|---|---:|---|---|---|---|---|---|---|
| P00-001 | 1 | C1b 기준 CharacterUnit과 P00 profile 작성 | C1B-006 | MAP | P00 profile | local capsule1m=1CU preflight·8.8×6.2 시작 profile; C1B 최종 height/LOCKED 검증 후속 | — | IN_PROGRESS |
| P00-002 | 1.5 | 옥상 layout·2/3/4 Spawn 구축 | P00-001 | MAP | Greybox scene | local 동일 상판·2/3/4 cardinal Spawn/배치 순환 검토; core/panel·Hazard/DropZone 포함 정식 Greybox 후속 | — | IN_PROGRESS |
| P00-003 | 1.5 | RecoveryBand·OOB 구축 | P00-002,CHR-011 | MAP | recovery/oob | local core Collider OOB·정적 Lip/Facade grip/Climb 검토; Beam·움직이는 표면·정식 맵/round 연결 후속 | — | IN_PROGRESS |
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
| FIR-003 | 2 | Host recoil physics·deterministic spread/bloom 구현 | FIR-001,CHR-002,FDN-005 | WEAPON,PHYS | recoil/spread profiles | 좌우 공통 팔 뻗기 Grip template1·Held arm 적용, authored fire/recoil clip0; bounded Rigidbody/Joint impulse·torque로 팔·Weapon 상승과 spring/damping 복귀, Pistol narrow spread·strong shot recoil, AK capped accumulator/bloom·release recovery, ShotSequence 재현·Grip/Down/Hazard 우회0 | — | NOT_STARTED |
| WPN-005 | 2 | Pistol·AK 실제 발사 전투 통합 | FIR-001..003 | WEAPON | firearm combat report | 7/30 no-reload, semi/full-auto, projectile·damage/knockback·Spent deadline cleanup; Playing+SuddenDeath fire 허용·RoundResult 새 Fire0과 2/3/4 권한 결과 일치 | — | NOT_STARTED |
| WPN-006 | 2 | Bat·Hammer 실제 근접 전투 구현 | WPN-004 | WEAPON | melee combat | swing·impact·damage·knockback 판정 | — | NOT_STARTED |
| ANP-001 | 1 | Alpha Action Animation Matrix·Authority/Presentation 계약 작성 | C1BRW-004,FDN-003 | CHAR,GAME | action phase profile | locomotion·Jump·Punch·Kick·Dropkick·Grab·Throw·Weapon·Ragdoll/GetUp phase, gameplay Root Motion 0 | — | NOT_STARTED |
| ANP-002 | 2 | Blockout Rig용 locomotion·hand·air action animation prototype | ANP-001,AIR-002,CHR-007,CHR-009,CHR-011 | CHAR,GAME | alpha body action set | Character Spec §6.1.1 행동별 scenario: Idle·Walk/Sprint 전 주기/전환·Jump/착지·L/R Punch/Kick·Dropkick/Recovery·한손/양손 Grab/release Throw(별도 Lift 조작0)·Wall/Ledge/Beam 매달리기·ClimbAssist·Ragdoll/shared GetUp. 관절 과굴곡/압축·손 고정/발 접지·중단/reset·한손 Grab+반대 손 Punch와 Hit state 일치 | — | IN_PROGRESS |
| ANP-003 | 2 | Weapon·recovery presentation과 Ragdoll·offline 2/3/4 통합 | ANP-002,WPN-005..006,WPA-003 | CHAR,WEAPON,NFR | alpha action presentation report | 공통 팔 뻗기 Grip template1+physics recoil, authored Firearm fire/recoil clip0, Bat/Hammer swing, DropkickRecovery/Down state 분리+shared GetUp, local authority phase 추종·false hit cue/root-motion authority0, offline 인원별 판독; 실제 peer 검증은 NET-004/007/011..014 | — | NOT_STARTED |
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
| NET-004 | 1.5 | local prediction·remote interpolation·correction | NET-003 | NET | presentation sync | ANP body/Weapon/recovery presentation의 실제 peer phase 추종, action·Projectile·recoil cue duplicate 0, correction metric 기록 | — | NOT_STARTED |
| NET-005 | 1.5 | roster·Ready·result·Patch reliable event 구현 | NET-003 | NET | event stream | 중복·역순 상태 mutation 0 | — | NOT_STARTED |
| NET-006 | 2 | Lobby↔Match Scene 전환·Launch freeze 구현 | NET-003..005 | NET,LOBBY | scene protocol | roster·appearance 동결, build/content/map/appearance 호환 뒤 activate, 변화·실패 시 Lobby 복귀 | — | NOT_STARTED |
| NET-007 | 1.5 | Round reset·DownCount·Action·무기 supply/state 동기화 | NET-003..006,AIR-002,FIR-001..003,RND-004 | NET,ROUND | round sync | Air action, AmmoRemaining·Fire hold/cadence·Projectile·Recoil/Bloom·Spent timer, Supply profile·bag·Incoming/Loose/Held/Spent의 stale 적용 0; Down/Dropkick state 분리와 shared GetUp의 peer phase 일치 | — | NOT_STARTED |
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
| ANM-001 | 1.5 | locomotion·Sprint·Air Kick/Dropkick와 procedural Jump polish | C4-002,CHR-005,ANP-002 | GAME | animation set | Idle arms-to-torso, Walk small arm swing+leg motion, Sprint large arm swing+fast leg cadence와 Alpha action phase·KickAnchor 일치, gameplay Root Motion·authority mutation0 | — | NOT_STARTED |
| ANM-002 | 2 | Strike·Grab·Throw·Melee production animation과 Firearm physics presentation polish | C4-002,WPN-007,ANP-003 | GAME,WEAPON | action set | Alpha Hit phase, 공통 팔 뻗기 Grip template1+physics recoil·authored recoil clip0·Spent release, Bat/Hammer swing, 손·무기 방향 2/3/4 판독 | — | NOT_STARTED |
| ANM-003 | 1.5 | Down 누적·DropkickRecovery·Ragdoll·shared GetUp presentation | C4-002,CHR-008..009,ANP-002 | PHYS | recovery set | DropkickRecovery/DownEpisode state 분리, front/back/left/right 진입과 side 정렬→공통 face-up/down GetUp, Down duration·non-Down tumble 구분과 state 일치 | — | NOT_STARTED |
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
7. r16 Neutral 방향 승인 기록(PASSED)과 기존 r20 리그를 확인하고 현행 profile·inventory·Evidence 연결을 정리한다. r20 내부 Neck 허용, r16/r20 원본은 보존한다.
8. `C1BRW-004` 공식 정적 Pose8·lineup과 `C1BRW-005` static Neutral FBX/Unity parity 뒤 `C1B-006 / UG-C1B` 최종 승인. 이 두 작업의 authored Animation claim0이며 게임용 Skeleton/Prefab은 CHR-001, 동작 prototype은 ANP-002..003이 소유한다.
9. G1 Character/Input·AIR·Alpha Action Animation부터 한 Task씩 실행
10. WPA-001..003으로 네 Weapon low-poly 결과를 잠근 뒤 WPN 구현 시작
11. UG-W1 뒤 FIR-001..003으로 7/30 no-reload state, Projectile, Recoil/Spread를 분리 구현
12. PAT-001은 2026-08-25 `UG-PATCH12-DESIGN` 승인 기록과 FDN-003 완료 뒤 시작

Player Build, Steam 배포와 외부 서비스 실행은 이 자동 순서에 포함하지 않는다.

## 12. 계획 검증 체크리스트

- 모든 Task는 0.5~2일이며 하나의 검증 가능한 결과를 가진다.
- Plan은 178 Task·258.0일이며 역사 C1B PASS Evidence를 보존한 채 C1B rework chain `C1BRW-001..005`가 current acceptance를 소유한다.
- r16 Neutral 방향 승인과 UG-C1B 최종 승인을 구분한다. 기존 r20 리그는 보존하고 C1BRW-004/005는 static 검토만, CHR-001은 game Prefab, ANP는 동작 구현만 완료 처리한다.
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
