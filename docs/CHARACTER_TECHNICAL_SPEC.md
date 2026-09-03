# Project Hotfix 캐릭터 기술 사양

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| 문서 버전 | 0.12.0 C1B Static Interop Baseline + 2026-09-01 r11 Global Fair T-Pose Addendum |
| 최종 수정일 | 2026-09-01 |
| 상위 기준 | `01_PRD.md` 1.8.0, `02_SRS.md` 1.8.0 |
| 목적 | `MasterCharacter`의 승인 방향, 제작 Gate, 물리·입력·Paint·Cosmetic·무기 통합 기준 정의 |
| 우선순위 | 게임 규칙은 PRD/SRS, 패치 콘텐츠는 `PATCH_DESIGN.md` 0.5.0, 시각 언어는 `ART_DIRECTION.md` 1.9.0, 구체 캐릭터 제작은 본 문서 |

이 문서는 결과와 승인 기준을 정의한다. 구현체의 바이너리 배치, 내부 직렬화 수식과 반복 fixture
개수는 구현·테스트 문서에서 관리하며 캐릭터의 제품 방향으로 취급하지 않는다.

### 0.0 2026-09-01 r11 Global Fair T-Pose 결정

- C1B-002..005는 당시 수치·Blender·Pose·FBX/Unity 기술 Evidence로는 PASS였지만, v0.13의 부드러운 한 몸
  방향과 다른 faceted head, egg/peg body, exposed proximal cap과 detached-looking limb 때문에 current visual
  acceptance에서는 `REWORK_REQUIRED`다. 역사 Evidence와 Commit은 수정하지 않는다.
- r01/r02 technical source·Evidence와 r03..r10 시도는 역사로 보존하지만 current visual result는
  `REWORK_REQUIRED / SUPERSEDED`다. r11은 r10의 round head, visible neck0와 T-pose를 유지하면서 몸통 폭·깊이
  저주파 profile 자체를 재형성하고 shoulder/axilla broad blend와 전신 fairing을 적용한다.
  Pixel distance나 특정 production topology를 reference에서 역산하지 않는다.
- C1B rework chain의 r11 Neutral/Silhouette/Rake 12-view를 사용자가 `UG-C1B-NEUTRAL`에서 먼저 승인해야 한다.
  이 Gate 전에는 Rig, Pose clip, Animation, FBX, Unity import, Collider, Build, Commit/push/LFS 승격을 만들지 않는다.
- 현재 USER_REVIEW candidate `CHR_MasterCharacter_C1B_NeutralRework_r11`은 Body component1/Euler2,
  `227942V/455880E/227940F`, triangle/quad `0/227940`, runtime modifier0, topology error0,
  adjacent angle max `6.843839°`, exact mirror max `1.884956e-7H`, visible T-arm center max deviation
  `6.694555e-5H`, r10 signed volume 오차 `4.489624e-11`, camera/render `4/12`,
  scene `LOCAL_USER_REVIEW`다. Head는 별도 closed round direct-contact Mesh이고 visible neck·authored Neck node·eyes·hands는0다.
  이 수치는 visual approval이나 production topology lock이 아니다.

### 0.1 0.12.0 변경 요약

- 첫 C1B static Blockout 반입에서 확인한 Blender/Unity handedness 차이를 `ModelInteropProfile-ART-001-r02`의
  `C1BBlockout` 전용 transient `ReflectX + reverse winding` export override로 고정했다.
- 이 override는 canonical `.blend`를 바꾸거나 Unity Prefab에 개별 Rotation·Scale correction을 남기지 않으며,
  Static Blockout에만 적용된다. Armature·Skinned Character와 base r01 preset에는 적용하지 않는다.
- C1B Blockout은 UV0가 없는 비생산 Mesh이므로 Unity tangent import를 `None`으로 두는 좁은 예외를 추가했다.
  전역 UV0·authored tangent 요구는 유지하며 C4 production source부터 다시 필수다.
- C1B-005는 static silhouette·landmark·bounds·pivot·관절 접합만 검증한다. Armature·Action·Animation·Collider와
  모션 자연스러움은 0이며 ANP/C2 이후 검증으로 남긴다.

### 0.2 0.11.0 변경 요약

- Guest 30초 Disconnect grace의 Neutral Input과 계속되는 Character physics·vulnerability·Alive/Camera
  참여, current Alive/spectator reconnect를 고정했다.
- explicit Leave·timeout Forfeit의 PatchAuthor 제외, 2명 이상 continuation과 one-left Score·Patch 0
  Lobby return, Host loss Session end를 추가했다.
- Match local menu를 local-only·non-pausing presentation으로 분리하고 지속 Match·Active Patch·Ammo HUD를 0으로 했다.
- Alpha Cosmetic은 EyeSet·Mustache·Headwear 각 placeholder 대표 1개 또는 같은 기능 범위의 동등 최소
  catalog만 사용하고 full catalog를 post-Alpha로 분리했다.
- ModelInteropProfile의 reference toolchain을 Unity 6.3 LTS·Blender 5.2 LTS로 정하고 실제 설치 patch
  version은 Plan 2.5 `FDN-010` ToolchainProfile이 소유하게 했다.

### 0.3 0.10.0 변경 요약

- Pistol 7발 semi-auto와 LongGun 30발 full-auto·reload 0, Ammo 0 spent cleanup lifecycle을 추가했다.
- Host visible Projectile의 fixed-step swept SphereCast·first-hit·no-pierce/no-ricochet와 TTL·OOB·reset을 정의했다.
- Host Rigidbody recoil impulse/torque·muzzle physics와 read-only recoil animation을 분리했다.
- Pistol single recoil과 LongGun RecoilAccumulator·SpreadBloom·deterministic ShotSequence pose/state를 Action Matrix에 추가했다.
- Projectile별 Patch03·04 dedupe와 source spent/owner loss 뒤 Patch12 NoEligibleTarget을 고정했다.

### 0.4 0.9.0 변경 요약

- Ground hand Punch·Grab과 Airborne foot Kick·Dropkick·hand/ledge Grab의 입력 arbitration을 고정했다.
- `AirAttackToken=1`, `KickAnchor_L/R`, Dropkick 단일 AttackAction·dedupe와 non-Down DropkickRecovery를 추가했다.
- Authority Rigidbody·Ragdoll gameplay와 Animator/procedural presentation을 분리하고 root-motion·Animation Event authority를 0으로 고정했다.
- Punch·Kick·Dropkick·Grab·Throw·Weapon의 action/animation matrix와 Animator↔Ragdoll 전환 정책을 정의했다.
- Alpha는 primitive/procedural pose로 기능을 검증하고 C4/ANM production polish가 gameplay 판정을 바꾸지 않게 했다.

### 0.5 0.8.0 변경 요약

- 승인 Catalog를 `PATCH-PROT-001..012`로 확장하고 Character modifier subset 001..008과
  Weapon Supply·Forced Drop subset 009..012의 책임을 분리했다.
- Host-confirmed Weapon hit만 Patch11·12를 발동하며 victim Held/source Weapon Forced Drop이 손·Grab,
  Damage·Ammo·cadence와 Character base profile을 바꾸지 않게 했다.
- `Incoming` Weapon은 Character contact·Pickup·combat·map control에서 제외하고 착지 뒤 `Loose`가 되며,
  Round reset에서 Incoming·Loose·Held와 supply schedule을 함께 지우게 했다.
- 인원별 반복 Supply `START`와 cap이 Character/Weapon 접촉·Camera·2·3·4인 Gate에 포함됐다.

### 0.6 0.7.0 변경 요약

- 승인된 `PATCH-PROT-001..008`을 캐릭터 물리에 연결하되 base profile과 Round 범위
  `PatchModifier`를 분리했다.
- 첫 8개 패치는 Jump, Character-only Pulse, Attack knockback/recoil, Grab throw resistance/grip,
  Ragdoll friction/one-bounce channel만 사용한다.
- 패치가 실제 Character size·Collider·base mass·groggy duration·Hazard timing·Weapon damage를
  바꾸는 경로를 금지했다.
- 패치 기능은 2·3·4인에서 같은 규칙으로 검증하고 전용 Animation·VFX·SFX는 후속 표현 단계로 분리했다.

---

## 1. 고정된 캐릭터 방향

모든 플레이어는 같은 `MasterCharacter`를 사용한다. Mesh, Skeleton, Ragdoll, Collider, mass,
reach와 gameplay Anchor는 플레이어별 외형에 따라 달라지지 않는다.

기본 외형은 다음과 같다.

- 흰색의 매끈한 무안면 이족형 Paint canvas
- 전체 높이의 약 1/5인 비교적 큰 완만한 둥근 타원형 머리
- 배만 불룩한 인상을 줄인 짧고 넓은 몸통
- 낮은 중심이 읽히는 짧고 굵은 다리
- Gang Beasts를 우선 참고한 짧아진 중립 팔 위치
- Party Animals를 보조 참고한 굵고 둥근 연속 limb
- 별도 가시 손 Shape·Mesh·구·손바닥·손가락·주먹이 없는 Forearm terminal
- 별도 Shoe·Toe block 없이 넓게 접지하는 lower-leg terminal

2026-08-15 승인된 `Hybrid Core v0.13`은 큰 시각 방향만 승인한 C1a 자료다. 정면 중립 Pose에서
팔 terminal 최하단이 가랑이 기준선보다 약 `0.04~0.05H` 위에 오는 인상을 C1b에서 다시 확인한다.
이 이미지의 pixel을 Bone, Collider, Anchor, reach 또는 생산 Mesh 치수로 역산하지 않는다.

참고작은 판독성·물리 인상·생산 방식의 축으로만 사용한다. 고유 Mesh, 실루엣, 얼굴, 색 배치,
Material, Animation 또는 수치를 복제하지 않는다. UI 기준 이미지 속 캐릭터도 비율·Rig 기준이 아니다.

---

## 2. 제작 Gate

| Gate | 목적 | 필수 산출물 | 승인 범위 |
|---|---|---|---|
| `C0 Reference Lock` | 참고할 인상과 복제 금지 기록 | reference-role checklist | 참고 방향 |
| `C1a Direction Review` | 큰 체형 방향 확인 | v0.13 front·side·3/4 sheet | 큰 시각 방향만 |
| `C1b Neutral Review` | Pose·FBX 전에 현재 Neutral 조형 승인 | one review object·visually continuous direct head overlap의 front·side·back·3/4 Neutral/Silhouette | `UG-C1B-NEUTRAL`, Pose 제작 입력 |
| `C1b Exact Proportion` | 한 체형의 정확한 수치 승인 | front·side·back·3/4, measurement, Pose, 4인 lineup | C2 Prototype 입력 |
| `C2 Physics` | Rig·Collider·Joint·Ground/Air 행동 검증 | Unity physics Prefab, action/physics profile, capture | 물리 Prototype |
| `C3 Integration` | Camera·Paint·Cosmetic·무기·action presentation 통합 | 2·3·4인 capture, UV/cage/Grip/action report | 통합 Prototype |
| `C4 Production Lock` | 최종 topology·UV·weights·LOD·material 확정 | `.blend`, FBX, Prefab, manifest, 사용자 승인 | Alpha 생산 캐릭터 |

Gate를 건너뛰지 않는다. C2·C3에서 비율이나 reach 문제가 발견되면 Physics 값을 억지로 보정하지
않고 C1b minor revision으로 돌아가 다시 승인한다.

`C1b Neutral Review`는 `C1b Exact Proportion`을 대신하지 않는다. r11 globally-faired T-pose 조형을 먼저 승인한 뒤에만
Pose8·lineup/Animation과 FBX/Unity parity를 만들고, 마지막 `UG-C1B`에서 수치·Pose·Unity 결과를 함께 승인한다.

### 2.1 C1b 필수 자료

- 같은 source의 exact orthographic front·side·back·three-quarter
- Neutral, 양손 Grab, 왼손·오른손 Strike 준비, 좌우 foot Kick, Dropkick과 Air hand-reach Pose
- 흰색 기본 상태와 단색 silhouette
- 동일 profile 4인의 overlap·spread lineup
- Crown, Chin, Shoulder, Elbow, Forearm terminal, Chest, Pelvis, Crotch, Hip, Knee,
  lower-leg terminal의 landmark와 width/depth
- `CharacterProportionProfileId/Version`과 수정 전후 비교
- 사용자의 명시적 수치 승인

`H=1.0` normalized profile을 사용하고 profile과 Blockout의 주요 silhouette·landmark 오차는
`0.005H 이하`를 C1b/C2 시작 목표로 검증한다. 이 수치는 이미지 pixel 복제를 요구하는 값이 아니라
동일한 승인 source가 제작 단계에서 바뀌지 않았음을 확인하는 값이다.

### 2.2 Neutral rework 조형 계약

- Head는 faceted 또는 square mass가 아니라 둥근 mass로 읽혀야 한다.
- Visible neck과 authored Neck semantic node는 `0`이며, Head는 torso 상단에 직접 overlap/attachment되어야 한다.
- Torso→shoulder→arm의 visible seam·groove·step·cap·detached boundary는 모든 필수 view에서 `0`이어야 한다.
- Torso는 과도한 egg/pear mass가 아니라 v0.13의 비교적 곧고 부드러운 몸통이어야 한다.
- Crotch는 torso와 양 leg를 잇는 연속 U 형태이며 hip cap이나 분리 leg block이 보이지 않아야 한다.
- Forearm/lower-leg terminal은 별도 hand/foot 없이 둥글게 닫히고 flat disc로 읽히지 않아야 한다.
- Neutral review object는 `1`을 사용한다. r02의 closed component `2`는 seamless body field와 closed round head의
  직접 overlap/attachment를 뜻하며 visible gap·neck·seam을 허용하지 않는다. 이는 production topology·UV·weight를
  미리 확정하는 규칙이 아니라 seam/cap 회피와 조형 검토를 위한 현재 단계 계약이다.

다음 failure class는 새 Neutral Gate에서 명시적으로 거부한다: `FACETED_HEAD`, `EGG_OR_PEG_BODY`,
`EXPOSED_PROXIMAL_CAP`, `DETACHED_LOOKING_LIMB`, `FLAT_TERMINAL_DISC`, `BACKGROUND_THROUGH_HOLE`.
사용자는 four-view 전체를 보고 승인하며, 한 view crop·조명 또는 Pose로 실패를 숨기지 않는다.

---

## 3. Blender → FBX → Unity 계약

| 항목 | 기준 |
|---|---|
| Blender | `+Z Up`, Character forward `-Y` |
| Unity | `+Y Up`, `+Z Forward`, `+X Right` |
| 단위 | `1 Blender meter = 1 Unity unit = 1 meter` |
| Source pivot | Neutral Pose 양발 사이 바닥 접점 |
| Unity root | scale `(1,1,1)`, 음수 scale 없음 |
| Reference toolchain | Blender `5.2 LTS`, Unity `6.3 LTS` |
| Exact patch lock | Plan 2.5 `FDN-010` ToolchainProfile의 실제 설치 version·package lock |

`ModelInteropProfileId/Version/hash` 하나로 Blender export preset과 Unity importer preset을 고정한다.
profile은 위 LTS family와 실제 설치 Blender·Unity patch version, 단위와 축, transform 적용, normals/tangents, Skeleton,
material import와 mesh 처리 설정을 추적한다. 개별 FBX의 수동 rotation·scale·normal 보정으로 문제를 숨기지 않는다.
문서의 `5.2 LTS`·`6.3 LTS` 표기만으로 exact patch를 추정하지 않고 Plan 2.5 `FDN-010`에서 고정한 profile과
project/package lock이 없으면 import parity를 승인하지 않는다.

`ModelInteropProfile-ART-001-r02`는 C1B static Blockout에 한해 좌표계 handedness를 다음처럼 운반한다.

- canonical Blender source의 `+X Right / -Y Forward / +Z Up`과 vertex는 수정하지 않는다.
- export용 transient copy에서만 X를 한 번 반사하고 polygon winding을 함께 뒤집어 outward normal을 보존한다.
- Blender FBX base axis `-Z Forward / +Y Up`에 `bake_space_transform=true`를 적용하고, Unity는
  `bakeAxisConversion=false`로 읽는다.
- Unity imported root와 export root는 identity rotation·scale1·positive determinant를 유지하고
  `+X Right / +Y Up / +Z Forward`가 되어야 한다.
- 이 처리는 versioned `C1BBlockout` asset-class override이며 개별 FBX post-import correction이 아니다.
  Armature·Skinned Character·Weapon·Map에 자동 확장하지 않는다.

C1B Blockout Mesh는 production UV를 만들기 전의 수치·실루엣 검토본이므로 `UV0=0`, tangent stream `0`,
Unity `importTangents=None`을 허용한다. Normals는 계속 `Import`하고 finite/outward 상태를 검사한다. 이 예외는
Paint, normal map, production Material이나 shipping topology 준비를 뜻하지 않으며 C4 source에서는 끝난다.
전역 `UV0 required`와 authored tangent 계약은 그대로 유지한다.

Blender 5.2 FBX는 생성 시각과 Python-hash UUID metadata 때문에 같은 semantic export의 binary SHA가 매번
같다고 보장하지 않는다. Manifest는 선택된 canonical FBX의 SHA를 기록하고, 재현 Gate는 source/preset hash와
Mesh position·surface topology·landmark·bounds semantic fingerprint를 함께 사용한다. Binary를 사후 patch하거나
vendor exporter를 monkeypatch해 hash만 맞추지 않는다.

첫 Unity 반입은 승인 C1b source와 동일한 네 방향에서 다음을 비교한다.

- silhouette·landmark·bounds 오차 `0.005H 이하`
- ground pivot 오차 `0.5%H 이하` 시작 목표
- 잘못된 vertex·normal, 음수 scale과 축 반전 0건
- Bone 이름·parent와 Bind Pose 일치
- profile hash와 source→FBX→Prefab version 추적 가능

C1B-005에서는 Skeleton·UV·tangent·Material 항목이 위 Blockout 예외 또는 `N/A`인지 명시하고, 대신
Neutral four-view mask bounds drift `0.005H 이하`, source/import geometry signature, L/R landmark와
`+Z Forward`를 직접 검사한다. 정적 Pose의 실루엣·관통·관절 접합은 검토하지만 Animation timing·transition·
deformation·무게감은 Armature/Action이 생기는 ANP/C2 전에는 승인하지 않는다.

불일치하면 Collider나 Joint로 보정하지 않고 source 또는 interop profile을 수정해 다시 반입한다.

---

## 4. Prefab·Rig·물리

```text
CHR_MasterCharacter
├── CharacterRootState
├── SimulationBody
│   ├── PhysicsRoot
│   ├── CoreBodies
│   ├── HandBodies
│   └── SecondaryRagdollBodies
├── VisualRoot
│   ├── SharedMesh
│   ├── Armature
│   ├── PaintableSurface
│   └── CosmeticAttachmentCage
├── GameplayAnchors
├── CosmeticRoot
├── ActiveSubjectBounds
└── Presentation
```

Simulation과 Visual을 분리한다. Squash & Stretch, secondary motion과 render 보간이 Authority
Collider, Root, Anchor 또는 접촉 판정을 바꾸면 실패다.

권장 Runtime은 `권한 gameplay action + Rigidbody/Joint physics + read-only animation presentation`의
Hybrid다.

- AuthorityHost의 `SimulationBody`가 PhysicsRoot transform·velocity, Joint target, Contact, Hit,
  Grab constraint, Throw·Jump·Dropkick·Firearm recoil impulse/torque, authority Muzzle과 Ragdoll을 판정한다.
- `VisualRoot`의 Animator·procedural pose·IK는 Host semantic action phase와 보간된 physics anchor를
  따라가며 gameplay Rigidbody·Collider·Anchor를 역으로 구동하지 않는다.
- locomotion, Jump, Punch, Kick, Dropkick, Grab, Throw, Weapon과 GetUp clip은 in-place가 원칙이다.
  `Animator.applyRootMotion` 또는 동등한 root transform 적용이 Authority PhysicsRoot를 이동시키는 경로는 0개다.
- Animation Event는 Audio/VFX cue를 요청할 수 있지만 Attack active window, sweep, Hit, Grab, Release,
  Shot, Projectile, recoil/spread, impulse, Down과 GetUp 완료를 확정하지 않는다.
- 완전 수동 Ragdoll contact만으로 의도 행동을 추론하지 않는다. Host action phase가 Rigidbody motor·joint
  target과 sweep 자격을 열고 실제 physics 결과가 최종 위치와 Contact를 만든다.

필수 Bone은 Root, Pelvis, Spine, Chest, Neck, Head, 좌우 Clavicle·UpperArm·Forearm·논리 Hand,
Thigh·Calf·Foot다. Finger·Toe bone은 만들지 않는다. helper bone은 deformation 시험이 필요성을
증명할 때만 추가한다. Vertex당 최대 4 Bone weight를 Prototype 시작 제한으로 둔다.

Collider는 승인 C1b profile 하나에서 도출한다.

- Pelvis, Chest, Head, UpperArm, Forearm, Thigh, Calf
- 가시 손이 아닌 `InvisibleTerminalContact`
- 별도 가시 발이 아닌 `LowerLegGroundContact`
- Dynamic MeshCollider 금지
- 일반 Pose의 visual↔Collider 표면 차이 `0.03H 이하` 시작 목표
- terminal↔Strike/Grab Anchor 지속 오차 `0.015H 이하` 시작 목표
- lower-leg terminal↔KickAnchor 지속 오차 `0.015H 이하` 시작 목표

전체 질량은 30kg을 첫 C2 시작값으로 하며 모든 플레이어에게 동일하다. 이 base mass는 승인 Patch12가
변경하지 않는다. 세부 질량·Joint spring,
damper와 limit는 `CharacterPhysicsProfile`에서 비교하고 승인 전에는 `START`다. Pose 모양보다 solver
안정성, 비폭발적 Joint와 재현 가능한 Host 판정을 우선한다.

---

## 5. 공통 이동 행동

Lobby와 Match는 같은 locomotion, Jump, Sprint, Punch, Air Kick·Dropkick, Grab, Ragdoll과 GetUp 동작을 사용한다.
화면 축 기준 WASD로 이동하고 이동 방향으로 자동 회전한다.

### 5.1 Sprint

- `Left Shift`를 누르고 있는 동안 Sprint를 요청한다.
- Sprint는 Lobby와 Match에서 동일한 캐릭터 행동이다.
- release 즉시 기본 이동으로 돌아간다.
- stamina, 소모 meter, 재충전과 피로 상태는 Alpha 범위에 두지 않는다.
- Sprint multiplier는 `MovementTuningProfile`의 Alpha `START` 값이며 2·3·4인 이동·Camera·맵
  측정 뒤 승인한다.
- Stunned, Ragdoll, Recovering, Customizing과 입력 잠금 상태에서는 Sprint가 적용되지 않는다.
- Sprint가 Grab strength, weapon damage, mass 또는 Collider를 암묵적으로 바꾸지 않는다.
- Sprint 추가로 Spawn 접촉 시간, edge 도달 시간, Camera Dolly와 Hazard 회피가 깨지면 multiplier와
  맵 수치를 함께 비교하고 한쪽만 몰래 보정하지 않는다.

### 5.2 Ground·Airborne 손 입력과 Air Attack

Grounded에서는 LMB/RMB가 해당 손의 행동을 결정한다.

- `GrabHoldThreshold` 전 quick release는 왼손/오른손 Punch다.
- threshold까지 hold하면 같은 손의 GrabSeek이며 Punch는 0이다.

Airborne이며 DownEpisode가 아닐 때는 같은 입력을 다음 순서로 중재한다.

1. 각 down edge를 `AirIntentPending`으로 기록한다.
2. 좌우 down edge가 `DualClickChordWindow` 안에 들어오면 두 번째 edge에서 두 Pending을 하나의 Dropkick으로 즉시 commit·소비한다.
3. quick release는 chord window가 닫힌 뒤 같은 쪽 foot Kick으로 확정한다.
4. Grab threshold까지 hold한 손은 commit되지 않은 single Kick 후보를 취소하고 hand/ledge GrabSeek로 확정한다.
   이미 commit된 Dropkick은 이후 hold로 rollback하지 않는다.

`DualClickChordWindow` 후보는 `60/80/100ms`, `START=80ms`다. Ground Punch/Grab의 threshold 후보
`120/150/180ms`와 별도 tuning 축으로 기록한다.

- Host는 각 down edge에서 Ground/Air context를 고정한다. Ground에서 시작한 press가 takeoff 뒤 Kick으로
  바뀌지 않고, Dropkick은 두 edge가 모두 Airborne·non-Down일 때만 성립한다.
- Ground를 떠나 시작한 non-Down Air episode는 `AirAttackToken=1`을 가진다.
- 첫 유효 Kick 또는 Dropkick이 token을 소비한다. 같은 Air episode의 이후 quick tap은 공격을 만들지
  않지만 hold Grab·ledge Grab은 가능하다.
- Grounded, GetUp 완료 또는 Round reset이 token을 1로 복원한다. DownEpisode 중에는 Air Attack을 시작하지 않는다.
- 왼쪽/오른쪽 Kick은 각각 `KickAnchor_L/R`의 Host-authoritative sweep을 사용한다.
- Dropkick은 두 KickAnchor가 하나의 `AttackActionId`를 공유하며 같은 Target을 한 번만 Hit한다.
- Host만 Attack phase, sweep, contact eligibility, Hit, Damage·Knockback과 impulse를 확정한다. passive foot
  collision, Animation pose와 Client sweep 주장은 Attack Hit가 아니다.
- Dropkick은 bounded forward impulse, reduced steering과 기본 Kick보다 강한 승인 knockback을 가진다.
  종료 뒤 별도 `DropkickRecovery`에서 bounded physics tumble을 허용하되 DownEpisode·DownCount와
  `TRG-DOWN-EPISODE-START`는 0이다. Grounded가 token을 복원해도 recovery 종료 전 새 Attack은 0이다.
- Kick·Dropkick Hit는 `TRG-ATTACK-HIT-CONFIRMED`의 SourceKind가 되고 Patch03은 Action·Target당,
  Patch04는 AttackAction당 최대 한 번 적용된다. `TRG-WEAPON-HIT-CONFIRMED`는 만들지 않는다.
- W1 WeaponUse binding은 승인된 Air L/R quick-tap Kick과 L+R chord Dropkick을 덮어쓰지 않는다.
  Airborne WeaponUse를 허용할지, 별도 입력과 Kick/Dropkick 대비 우선순위는 `UG-W1` 결정으로 남긴다.

### 5.3 Ragdoll·GetUp·반복 Down

`CharacterMotionState`는 Locomotion, Stunned, Ragdoll, Recovering을 구분한다. Ragdoll은 최소
face-up, face-down, left, right에서 시험한다. clearance가 없으면 순간이동해 일어나지 않고 기다린다.

같은 Round에서 반복 down을 다음처럼 누적한다.

```text
Round 시작: DownCount = 0
새 down/Ragdoll 진입:
  DownCount = DownCount + 1
  Duration = min(MaxDuration, BaseDuration + IncrementDuration × (DownCount - 1))
다음 Round 시작: DownCount = 0
```

따라서 첫 down은 BaseDuration, 이후 down/groggy는 점차 길어지고 cap을 넘지 않는다.
`BaseDuration`, `IncrementDuration`, `MaxDuration`은 Alpha tuning `START` 값이다.

- 하나의 연속 Ragdoll 안에서 상태 callback이 반복돼도 DownCount는 한 번만 증가한다.
- GetUp 뒤 새 유효 down/Ragdoll에 들어갈 때 다시 증가한다.
- Host가 DownCount와 적용 duration을 판정하고 모든 Peer가 같은 값을 표시한다.
- Round reset에서 반드시 0으로 돌아가며 Match score와 혼동하지 않는다.
- down 증가가 영구 행동 불능이나 무한 stunlock을 만들면 cap·impact threshold·recovery를 조정한다.
- Lobby Ragdoll은 항상 BaseDuration을 사용하고 Match Round의 DownCount를 만들거나 증가시키지 않는다.

### 5.4 Base profile과 Round-scoped PatchModifier

`MovementTuningProfile`, `CharacterPhysicsProfile`과 `PhysicsTuningProfile`은 승인된 캐릭터
baseline을 소유한다. 활성 패치는 이 profile 자체를 덮어쓰지 않고 현재 `RoundGeneration`에 묶인
`PatchModifier`로만 적용한다.

- Round 시작은 모든 임시 modifier·lease·발동 cache를 지운 뒤 active Patch를 정해진 순서로 다시 등록한다.
- Round 종료, Scene 전환과 stale generation event는 현재 Round의 modifier를 남기지 않는다.
- AuthorityHost만 Trigger와 modifier 결과를 확정하고 Guest는 같은 결과를 표시한다.
- 모든 패치는 전원에게 같은 규칙으로 적용하며 특정 사용자를 작성자가 지정하지 않는다. 실제 대상은
  Trigger actor, hit victim, grab target처럼 권한 event 문맥에서 정한다.

승인 Patch12 중 Character modifier를 소유하는 subset은 `PATCH_DESIGN.md` 0.5.0의
`PATCH-PROT-001..008`뿐이다.

| Patch | Character channel | 경계 |
|---|---|---|
| `PATCH-PROT-001` | 승인된 일반 Jump의 수직 impulse modifier | `ClimbAssist`와 강제 launch에는 적용하지 않음 |
| `PATCH-PROT-002` | Jump당 한 번의 Character-only radial pulse | 다른 생존 Character만 대상; prop·Weapon·map control·Hazard 제외 |
| `PATCH-PROT-003` | `TRG-ATTACK-HIT-CONFIRMED`의 victim knockback modifier | damage와 Down duration 불변 |
| `PATCH-PROT-004` | 같은 Attack event의 attacker recoil | 한 Attack action에서 중복 recoil 없음 |
| `PATCH-PROT-005` | Player Grab target의 lift/throw resistance modifier | 실제 Rigidbody base mass와 Collider 불변; 같은 Grab relation 중복 없음 |
| `PATCH-PROT-006` | Player Grab relation의 grip modifier | GripStress·break 경로 유지; ledge·prop·Weapon Grab 제외 |
| `PATCH-PROT-007` | 현재 Down episode의 Ragdoll ground friction modifier | GetUp·reset에서 제거; groggy duration 불변 |
| `PATCH-PROT-008` | 새 Down episode당 한 번의 bounded bounce | 직접 Lethal/OOB 뒤 발동 0; DownCount 추가 증가 0 |

승인 Patch12에서 실제 Character scale·Collider dimension·base mass, groggy `BaseDuration`·`IncrementDuration`·
`MaxDuration`, Hazard timing과 Weapon damage를 변경하는 PatchModifier는 0개다. Patch 전용
Animation·icon·VFX·SFX도 기능 승인 조건이 아니다. Runtime은 안정적인 Patch ID와 의미 발동 event를
표현 port로 내보내기만 하며 후속 표현 subscriber가 Authority state를 바꿀 수 없다.

`PATCH-PROT-009..012`는 Character tuning channel을 새로 만들지 않는다.

| Patch | Character·Held relation 경계 |
|---|---|
| `PATCH-PROT-009..010` | Supply가 만든 `Incoming` Weapon은 Character contact·Pickup·Hit·Camera subject가 아니며 착지 뒤 `Loose`부터 기존 Weapon relation을 사용한다. |
| `PATCH-PROT-011` | Host-confirmed Weapon hit victim의 모든 Held Weapon Instance를 기존 강제 Release 경로로 Drop한다. Main·Support가 같은 Instance면 한 번이며 대상이 없으면 `NoEligibleTarget`이다. |
| `PATCH-PROT-012` | 같은 Host-confirmed hit에서 attacker가 사용했고 Effect 시점에도 같은 attacker가 Held 중인 source Weapon Instance 하나만 기존 강제 Release 경로로 Drop한다. |

강제 Drop은 Hand Input, Punch, Grab, Weapon Hit와 새 Patch Trigger를 합성하지 않고 Character의 Damage,
DownCount·groggy, base mass, reach와 Collider를 바꾸지 않는다. 승인 Patch12는 같은 Trigger를 공유하는
두 Patch를 모두 상호 배타로 취급해 retained active set에서 Trigger당 Instance를 최대 하나만 허용한다.

각 조합은 2·3·4인에서 동일 tuning으로 Trigger 1회성, 대상 결정, 중첩 상한, Round reset과 Peer 수렴을
각각 검증한다.

---

## 6. 손·Grab·Grip

왼손과 오른손은 독립된 상태를 가진다. 아래는 Grounded hand sequence다.

```text
Press → HandIntentPending
threshold 전 Release → HandStrike 1회 → Recovering → Idle
threshold 통과 → Strike 없이 GrabSeek
GrabSeek + valid contact → Grabbing
Release → constraint 해제 또는 non-strike recovery → Idle
```

`GrabHoldThresholdMs`는 120/150/180ms를 비교하고 150ms를 `START`로 사용한다. Pending에서 hit를
만들지 않고 같은 입력에서 Strike와 Grab을 함께 만들지 않는다.

Airborne quick release는 이 `HandStrike` 분기를 사용하지 않고 5.2의 Kick 후보가 된다. Air hold가
threshold를 넘긴 시점부터만 같은 GrabSeek·Grabbing 경로를 재사용하며 그 press의 Kick은 0이다.

`PATCH-PROT-005..006`은 새 Player-to-Player Grab relation이 권한 확정된 뒤에만 해당 relation의
throw resistance 또는 grip channel을 승인된 bounded lifetime 동안 수정한다. 같은 Grab relation의
반복 contact로 magnitude를 누적하지 않으며 Release·Break·Target 상실·elimination·expiry·Round reset
중 먼저 성립한 시점에 제거한다. Character base mass·Collider, Ledge Grab, map control, prop과 Weapon
Grip에는 이 modifier를 재사용하지 않는다.

Gameplay Anchor는 CharacterForward, Hand L/R, Strike L/R, Grab L/R, WeaponHand L/R,
Kick L/R, DropkickReference와 Nameplate를 가진다. 가시 손 대신 Forearm 방향과 terminal 내부 Anchor가
공격·잡기·무기 방향을 정하고 lower-leg terminal의 KickAnchor가 Air Kick sweep을 정한다. 유효 Ledge를
한 손 이상 잡고 Jump했을 때만 제한된 `ClimbAssist` 후보가 된다.

### 6.1 Authority Action과 Animation Matrix

| Gameplay context | Host semantic action·physics | Presentation |
|---|---|---|
| Locomotion·Jump | 권한 movement motor, `JumpAccepted`와 Root impulse·Ground/Air state | in-place Idle/Walk/Sprint·takeoff/air/landing pose |
| Ground L/R tap | `HandStrike_L/R`, StrikeAnchor active sweep·contact, Action·Target dedupe | 해당 손 in-place Punch pose |
| Ground/Air L/R hold | `GrabSeek_L/R` → 권한 Hand contact·constraint | Hand reach·brace IK, constraint를 따라가는 pose |
| Air L/R quick tap | `AirKick_L/R`, 해당 KickAnchor sweep, AirAttackToken 소비 | 좌/우 foot Kick pose |
| Air L+R chord | 단일 `Dropkick`, 양발 shared Action, forward impulse·reduced steering | 양발 Dropkick pose와 non-Down recovery tumble |
| Held Lift·Throw | 권한 constraint, release와 bounded Throw impulse | Lift·Throw pose, Hand/Target anchor follow |
| Pistol semi-auto | 권한 single Shot, Ammo-1, Projectile·strong bounded recoil impulse/torque | 짧고 강한 single recoil·muzzle pose |
| LongGun full-auto | 권한 hold cadence, ShotSequence, Ammo-1, RecoilAccumulator·SpreadBloom | 지속 fire, 누적 muzzle rise·body brace와 release/gap recovery pose |
| Bat·Hammer Melee | 권한 WeaponAction·Weapon sweep·Hit | in-place swing·impact pose |
| Ammo 0 | 권한 forced release→SpentPendingCleanup→remove | spent release cue; pickup/reload pose 0 |
| DownEpisode | 권한 Down transition·Ragdoll bodies·DownCount | Animator에서 physics Ragdoll pose로 blend |
| GetUp | Host clearance·orientation·완료 state | face-up/down in-place GetUp, AuthorityRoot follow |

Animation clip과 procedural pose는 semantic phase의 결과다. Clip frame, foot/hand visual overlap 또는
Animation Event가 active sweep과 constraint를 열지 않는다.

### 6.2 Animator↔Ragdoll 전환

- Locomotion·공격·Grab 동안 VisualRoot는 semantic phase와 SimulationBody anchor를 따라가며 root motion은 0이다.
- Host가 실제 DownEpisode를 시작할 때 진행 중 Attack을 닫고 SimulationBody의 현재 pose·velocity에서
  Authority Ragdoll을 시작한다. Visual skeleton은 그 결과로 blend하며 Animator가 Ragdoll body를 되감지 않는다.
- Ragdoll 동안 Host physics body가 위치·회전·contact의 기준이고 VisualRoot는 보간만 한다.
- GetUp은 Host clearance와 face-up/down orientation이 유효할 때만 시작한다. VisualRoot를 AuthorityRoot에
  맞추고 in-place GetUp을 재생하며 완료 state도 Host가 확정한다. Clip root delta로 순간이동하지 않는다.
- DropkickRecovery는 DownEpisode 전환을 사용하지 않는다. 별도 action/recovery phase와 bounded physics
  pose를 사용하고 끝나면 Airborne 또는 Grounded locomotion으로 돌아간다.

### 6.3 Network·Prediction 경계

- Guest는 hand edge·Jump·movement input과 sequence만 보내고 Punch·Kick·Dropkick·Grab·Hit 결과를 주장하지 않는다.
- Host는 `AttackActionId`, action phase, AirAttackToken, MotionState, authority Root·Hand·Kick anchor 결과와
  Firearm Ammo·ShotSequence·Projectile·Spent timer·RecoilAccumulator/SpreadBloom, Hit·Grab·Throw event를
  snapshot/reliable semantic으로 제공한다.
- Local prediction은 root movement와 visual action anticipation에 한정하고 Hit, knockback, Down,
  constraint와 DropkickRecovery 종료는 Host 결과에 수렴한다.
- Remote Client는 전체 Ragdoll을 권한 재시뮬레이션하지 않고 Host의 core/필수 body state를 보간한다.
  Animation과 Ragdoll 전환 cue의 중복 재생은 Action/Event identity로 억제한다.
- Guest는 Projectile spawn/path/Hit, Ammo, recoil/spread와 spent cleanup 결과를 주장하지 않는다. Local
  muzzle/recoil anticipation은 Host ShotSequence와 state로 수렴하고 delayed Projectile을 중복 생성하지 않는다.
- Match local menu는 해당 Client의 gameplay command를 Neutral로 route하는 local presentation이다.
  Menu를 열어도 Host Tick·Round clock·physics와 다른 Player action은 계속되며 Character를 freeze,
  invulnerable, spectator 또는 authority owner로 바꾸지 않는다.

Implementation ownership은 다음과 같다.

- `AIR-001`: Ground/Air tap-hold와 `60/80/100ms` chord resolver
- `AIR-002`: 좌우 Kick·Dropkick 권한 physics·sweep·Hit·recovery와 2·3·4인 기능
- `ANP-001`: Action Animation Matrix·semantic phase·root-motion authority 0
- `ANP-002`: body locomotion·hand·air Alpha presentation prototype
- `ANP-003`: Weapon action·Animator↔Ragdoll·network presentation과 2·3·4인 통합
- `FIR-001`: Pistol/LongGun ammo·fire mode·SpentPendingCleanup
- `FIR-002`: visible authority Projectile·fixed-step swept SphereCast
- `FIR-003`: Host recoil impulse/torque·Muzzle·spread/bloom·ShotSequence recovery
- `WPN-005`: Firearm gameplay 통합, `ANP-003`: read-only Firearm presentation 통합

### 6.4 Disconnect grace·Forfeit와 현재 상태 복원

- Guest connection이 끊기면 Host는 최대 30초 동안 그 Player의 입력을 Neutral로 처리한다. Character는
  Scene에서 제거하거나 안전 위치로 이동하지 않고 현재 Transform·Velocity·MotionState·Alive 상태로
  계속 Authority physics를 받는다.
- grace 중 Alive Character는 다른 Player의 Punch·Kick·Grab·Weapon Hit와 knockback을 받을 수 있고,
  OOB·LethalHazard 판정도 그대로 적용된다. Alive인 동안 SharedGameplayCamera subject에도 남는다.
- reconnect는 disconnect 이전 상태를 rewind하지 않는다. Host snapshot 적용 시 아직 Alive면 현재
  physical Alive state로 조작을 재개하고, 이미 실제 gameplay로 탈락했다면 현재 spectator state로 복원한다.
- grace 중 OOB·LethalHazard로 발생한 elimination은 정상 gameplay elimination이다. 반면 명시적 Leave와
  30초 timeout은 `Forfeit`이며 탈락 순서와 PatchAuthor 후보에 포함하지 않는다.
- Forfeit 뒤 영구 참가자가 2명 이상이면 현재 Round를 계속한다. 한 명만 남으면 새 Score·Patch 또는
  Patch Offer를 만들지 않고 Lobby에 돌아간다.
- Host Leave·Loss는 Session을 종료한다. Host Migration, 다른 Guest의 authority 승계와 Character state
  continuation은 0이다.

---

## 7. Paint와 local Preset

Paint는 `SharedMesh`의 전신 unique UV0를 사용한다.

- UV0 `0~1`, 겹침·좌우 mirroring 없음
- 머리 전면·가슴 중앙·terminal 주면의 불필요한 seam 없음
- 관절 deformation에서 뒤집힘·다른 island bleed 없음
- 512×512 RGBA8 sRGB를 Alpha `START` 기준으로 사용
- texel density 전신 평균 `±10%`, limb가 평균의 85% 아래로 내려가지 않는 것을 시작 목표로 검증

Preset은 local profile에 최대 10개 저장한다. BaseColor, AccentColor, Brush Stroke와 Cosmetic 배치의
편집 가능한 source를 보존하며 atomic save로 마지막 정상본을 보호한다. Preview cache는 원본이 아니다.

Lobby/party에서 외형을 공유할 때는 Client가 편집 source의 크기·좌표·catalog item·배치 수를 검증하고,
AuthorityHost가 허용 version과 한도를 다시 확인한 뒤 P2P로 검증된 외형 manifest와 필요한 visual data만
relay한다. gameplay Snapshot에 per-frame Cosmetic transform이나 Paint pixel을 넣지 않는다. 잘못된 외형은
해당 플레이어만 승인된 기본 외형으로 대체한다.

외부 이미지, URL, clipboard image, 사용자 Mesh·Shader·script import는 허용하지 않는다.

Customization은 Lobby에서 `C`로 어디서든 진입한다. Booth 근접, 특정 trigger 또는 물리 충돌을
요구하지 않는다. Guest는 진입 시 자신의 Ready를 해제한다. Host에는 Ready가 없으므로 Appearance를
미확정 상태로 바꾸고 Start Gate를 잠근다. 두 역할 모두 편집 중 gameplay command를 중립화한다.

---

## 8. CosmeticAttachmentCage

Cosmetic은 render LOD와 분리된 숨은 전신 Cage의 stable surface point에 부착한다.

Alpha는 Cage·저장·P2P 검증 기능을 확인하기 위해 `EyeSet`, `Mustache`, `Headwear` 각 category의
placeholder 대표 Cosmetic을 1개씩, 총 3개 제공한다. Category 이름을 바꿀 경우에도 얼굴 부착형,
돌출 장식형, 머리 착용형의 같은 기능 범위를 가진 동등 최소 catalog를 명시해야 한다. 전체 Cosmetic
catalog와 production-quality Mesh 세트는 post-Alpha 범위이며 Alpha 완료를 막지 않는다.

- 머리·몸통·등·팔·terminal·관절·다리·발바닥을 포함한 모든 외부 surface 배치 가능
- 위치·3축 회전·복제·삭제 가능
- 사용자 scale·tint·material 교체 없음
- 같은 위치 중첩과 Character/Cosmetic 시각 관통 허용
- 자동 snap·밀어내기·삭제·부위 재배치 없음
- Alpha `START` 전역 상한 16개
- runtime scale `(1,1,1)`
- Collider, mass, hitbox, reach, Weapon relation과 Camera bounds 불변

Neutral, Grab, Strike, 좌우 Kick, Dropkick, Air Grab, crouch, Ragdoll, GetUp, Weapon 장착과 모든 LOD에서 같은 skin surface point를
따라야 한다. surface drift `0.002H 이하`를 C3 시작 목표로 측정하고 Cage revision 변경 시 Preset
migration 또는 명시적 incompatibility를 제공한다.

---

## 9. 무기 통합과 Alpha 전투

`Pistol`, `LongGun`, `Bat`, `Hammer` 네 종류를 유지한다. Character Gate에서는 먼저
GripCandidate→Held→ReleaseDrop→ReacquireGrip과 terminal/Socket 정렬을 검증한다.

W1 입력 사용자 승인 뒤에는 실제 전투가 Alpha 범위다.

- Pistol·LongGun: fire, hit, damage/knockback, release/drop, reacquire
- Bat·Hammer: swing, impact, damage/knockback, release/drop, reacquire
- Host authority의 owner·hit·damage·impulse 판정
- 2·3·4인 Camera와 network 상태 수렴 검증

### 9.1 Firearm Ammo·Projectile·Recoil

| Weapon | Fire mode | 총 Ammo | Reload |
|---|---|---:|---|
| `Pistol` | semi-auto, 유효 down edge당 최대 한 발 | `7` | 없음 |
| `LongGun` | full-auto, 유효 hold·cadence 동안 반복 | `30` | 없음 |

- Alpha Player surface에 지속 노출되는 Match HUD, Active Patch HUD와 Ammo HUD는 0이다. AmmoRemaining, FireMode,
  ShotSequence·Projectile·recoil/spread·spent state는 developer-only debug와 Evidence에서만 확인한다.
  transient Patch selection/result UI는 이 제한과 별개로 허용된다.
- Host가 Shot과 Ammo를 판정하고 유효 Shot마다 Ammo를 1 줄인다. Ammo 0 Shot 뒤 Weapon은 강제
  Release되어 `SpentPendingCleanup`으로 들어가며 `START 2~4초` 뒤 제거된다.
- Firearm Fire·Projectile combat은 Playing과 SuddenDeath에서 계속 유효하다. RoundResult부터 새 Fire는
  0이고 active Projectile은 제거되며 다음 Round로 이월하지 않는다. SuddenDeath에서 중단되는 것은
  새 Weapon Supply이지 이미 존재하는 Firearm combat이 아니다.
- Spent Weapon은 owner·Collider·Pickup·fire·swing·Hit·Patch Trigger·map control이 없지만 제거 전까지 supply
  cap에 포함된다. Reload, reserve magazine과 ammo pickup은 0이다.
- Host는 유효 Shot마다 visible Projectile과 immutable attacker·Source Weapon·ShotSequence·AttackAction을 만든다.
- Projectile은 Source Weapon·발사 Actor Collider를 제외하고 fixed-step 이전→다음 위치의 swept
  SphereCast 첫 blocking Hit만 처리한다. pierce·ricochet은 0이고 gravity는 `START=0`이며
  TTL·OOB·Round reset·stale generation에서 제거한다.
- Static/Map hit는 Projectile을 막지만 Lever·Hazard·map control을 작동시키지 않는다. Guest의 Projectile
  path·Hit·Damage·Knockback 주장은 0이다.
- Pistol은 narrow base spread와 strong single-shot recoil을 사용한다. Host가 bounded Character/Weapon
  Rigidbody impulse·torque와 Muzzle 방향을 확정하고 Visual은 single recoil pose로만 따른다.
- LongGun은 accepted Shot마다 deterministic ShotSequence로 bounded `RecoilAccumulator`와 `SpreadBloom`을
  누적하고 button release 또는 승인 gap 뒤 회복한다. frame rate·Animation·packet order는 값을 바꾸지 않는다.

### 9.2 Weapon Supply

Weapon은 라운드당 하나만 공급하지 않고 `Playing` 동안 반복 Supply를 사용한다.

| 인원 | 첫 Pulse / 반복 간격 / cap |
|---:|---|
| 2인 | `10초 / 22초 / 2` |
| 3인 | `8초 / 16초 / 2` |
| 4인 | `6초 / 12초 / 3` |

- Host는 Round 초기 participating roster로 profile을 고정하고 Disconnect·Reconnect·Forfeit는 다음
  Round에서만 반영한다.
- cap은 `Incoming + Loose + Held + SpentPendingCleanup`을 모두 포함한다.
- Host는 Round마다 MatchSeed·Round·WeaponCatalogVersion으로 Pistol·LongGun·Bat·Hammer가 한 번씩 든
  shuffle bag을 만들고 Safe DropZone을 선택한다. 실제 admission된 Spawn만 bag cursor를 소비한다.
- `Incoming`은 Character·Weapon combat contact, Pickup, Patch Trigger와 Map control을 만들거나 Camera
  subject가 되지 않고 안전하게 착지한 뒤 `Loose`로 전환한다.
- admission·landing의 Character/Weapon clearance가 없으면 `NoSafeDropZone` 또는 `LandingBlocked`로
  Collider·피해 없이 끝낸다. WeaponCleanupBoundary는 회수 불가능한 Loose만 제거하고 유효 Held Weapon의
  Collider 일부 진입은 제거하지 않는다.
- cap full·capacity-limited Pulse, OOB 제거와 Patch10 파생 Wave는 backlog·즉시 재시도를 만들지 않는다.
- Patch09 desired batch2는 남은 capacity만 admission한다. Patch10 파생 Wave는 Base Pulse 뒤
  `START 6~10초`에 당시 capacity로 한 개만 시도하며 Base Supply root를 다시 만들지 않는다.
- Supply admission은 Playing에서만 가능하고 SuddenDeath·Round Result에서 pending을 취소한다. 기존
  Weapon은 reset까지 유지하며 다음 Round 시작에는 Incoming·Loose·Held·SpentPendingCleanup·Projectile,
  bag·timer·pending wave를 초기화한다.
- SpentPendingCleanup deadline은 SuddenDeath에서도 계속 진행되며 제거 전까지 cap에 포함된다.

### 9.3 Firearm·Patch Trigger 경계

`TRG-ATTACK-HIT-CONFIRMED`는 기본 전투 단계에서 승인된 Punch·Kick·Dropkick hit를 source로 사용한다. W1 뒤
Firearm·Melee가 같은 Host ownership·rate·hit·dedupe 검증을 통과하면 같은 의미 event source를
재사용할 수 있다. 이 연결은 `PATCH-PROT-003..004`의 knockback/recoil만 허용하며 Weapon damage,
ownership, fire/swing rate, drop과 Round reset을 우회하지 않는다.

Firearm은 Projectile마다 하나의 AttackAction을 사용한다. swept first Hit의 Action·Target에 Patch03을
최대 한 번, 같은 Projectile Action의 attacker에게 Patch04를 최대 한 번 적용한다. LongGun 반복 Shot은
각 ShotSequence를 독립 dedupe하되 cadence, accumulator와 최종 impulse 상한을 우회하지 않는다.

별도 `TRG-WEAPON-HIT-CONFIRMED`는 Host가 실제 Weapon ownership·rate·hit·dedupe를 모두 승인한
Weapon hit만 사용한다. `PATCH-PROT-011`의 victim Held Drop과 `PATCH-PROT-012`의 attacker source
Weapon Drop은 기존 Release·ownership·Loose replication을 재사용하며 Damage·Ammo·cadence를 바꾸거나
Forced Drop에서 새 Hit Trigger를 만들지 않는다. Patch12 source Weapon이 이미 놓였거나 owner가
바뀌었다면 다른 Weapon을 대신 해제하지 않고 `NoEligibleTarget`으로 끝낸다.
Punch·Kick·Dropkick은 Source Weapon이 없으므로 이 Weapon Trigger를 만들지 않는다.

Projectile은 Spawn 때 immutable source identity를 보존하므로 delayed Hit 전에 Source Weapon이
Spent/remove되거나 owner를 잃어도 Host-confirmed Attack/Weapon Hit event는 유효할 수 있다. 그러나
Patch12는 live held relation을 다시 확인하므로 이 경우 `NoEligibleTarget`이고 다른 Weapon을 대신 Drop하지 않는다.
Projectile static hit와 recoil/muzzle impulse는 hand-only Map control을 원격 작동시키지 않는다.

Pistol 7발, LongGun 30발과 reload 0은 확정이다. 정확한 fire cadence, Projectile speed/radius/TTL,
damage, knockback, recoil·spread·bloom/recovery, melee swing timing과 balance는 W1과 Alpha tuning에서
결정한다. W1 승인 전에는 임의 입력을 확정하거나 실제 combat를 구현 완료로 표시하지 않는다.

---

## 10. Camera·LOD·성능

공용 Camera는 render Mesh나 Ragdoll limb의 순간 bounds가 아니라 Authority core에 붙은 고정
`ActiveSubjectBounds`를 사용한다. Cosmetic, Weapon, Projectile, recoil pose와 visual deformation은 이를 키우지 않는다.

- 2·3·4인 모두 검증
- 16:9, 16:10, 21:9의 Min/Max Dolly 검증
- Disconnect grace의 Alive Character는 Camera subject에 남고 실제 elimination 뒤에만 spectator 처리되는지 검증
- Neutral, Sprint, Strike, 좌우 Kick, Dropkick·DropkickRecovery, Air Grab, down/Ragdoll, GetUp,
  Pistol single recoil·LongGun sustained/bloom·Spent release와 Weapon 상태 검증
- full-body silhouette, 양팔·양다리, terminal, Player marker와 무기 방향 판독

LOD0는 6k/10k/14k 후보, LOD1은 선택 LOD0의 약 50~60%, LOD2는 약 20~30%를 `START`
비교 범위로 둔다. 4인×각 16 Cosmetic×Supply cap3의 Incoming/Loose/Held/Spent Weapon×active Projectile×Ragdoll worst case에서 GPU frame, CPU skinning,
draw call, overdraw, memory와 LOD pop을 측정한다. 시각 차이가 없는 가장 낮은 후보를 기본으로 선택한다.

C4 source→FBX→Prefab Gate는 각 LOD의 silhouette·bounds, UV0, normal/tangent class,
material-slot mapping, Skeleton과 mount pivot을 비교한다.

---

## 11. UI 이미지 해석

- UI 이미지 속 Character 비율·손·Rig는 제작 기준이 아니다.
- Customizer 승인 이미지의 `크기` 도구는 비권위이며 실제 Cosmetic scale control은 없다.
- Lobby 이미지의 `[R] 준비`는 비권위이며 현재 Ready 입력 기준으로 사용하지 않는다.
- Legacy Private Lobby의 정적 lineup과 UI Start Button은 구현 근거가 아니다.
- Customization은 `C`로 어디서든 진입하며 Booth proximity를 요구하지 않는다.

---

## 12. 미승인·Alpha tuning 항목

- `C1BRW-002` current r11 globally-faired T-pose의 `UG-C1B-NEUTRAL` 시각 승인, 이후 C1b exact 비율과 전체 gameplay scale
- Collider·mass·Joint·reach와 `GrabHoldThresholdMs`
- `DualClickChordWindow 60/80/100ms`, Kick·Dropkick impulse/knockback·steering과 DropkickRecovery tuning
- Sprint multiplier
- down `BaseDuration`, `IncrementDuration`, `MaxDuration`
- 최종 topology, UV padding, Polygon, Bone, LOD와 Material 예산
- Alpha EyeSet·Mustache·Headwear 각 placeholder 대표 1개 또는 동등 최소 catalog의 authored size와
  기능 검증; full Cosmetic catalog는 post-Alpha
- W1 무기 입력, fire cadence·Projectile speed/radius/TTL·damage/knockback·recoil/spread/bloom과 combat balance
- SpentPendingCleanup `START 2~4초`의 최종 cleanup timing; Pistol7·LongGun30·reload0은 확정
- `PATCH-PROT-001..008` Character modifier의 tuning과 승인 Patch12의 2·3·4인 기능 결과
- 반복 Supply 시간·cap과 Patch10 second-wave는 승인 `START`; 최종 weapon density·balance는 Alpha Evidence 대기
- Alpha procedural action pose와 production Punch·Kick·Dropkick·Grab·Throw·Pistol single recoil·LongGun
  sustained/bloom·Spent release·Ragdoll/GetUp clip polish
- 후속 패치 icon·Animation·VFX·SFX 표현 방향
- 최종 Camera profile과 최소 GPU 기준

각 항목은 2·3·4인 측정, 비교 capture와 사용자 Gate 없이 `LOCKED`로 바꾸지 않는다.
