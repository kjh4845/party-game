# Project Hotfix 아트 방향성 가이드

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| 문서 버전 | 1.9.0 C1B Static Interop Baseline + 2026-09-01 r11 Global Fair T-Pose Addendum |
| 최종 수정일 | 2026-09-01 |
| 상위 기준 | `01_PRD.md` 1.8.0, `02_SRS.md` 1.8.0 |
| 기준 | `CHARACTER_TECHNICAL_SPEC.md` 0.12.0, `MAP_DESIGN_GUIDE.md` 1.8.0, `PATCH_DESIGN.md` 0.5.0 |
| 목적 | 캐릭터·맵·무기·UI의 공통 시각 언어와 Blender→Unity 제작 품질 Gate 정의 |

### 0.0 2026-09-01 r11 Global Fair T-Pose 시각 결정

- C1B-002..005의 기술 PASS는 보존하지만, 현 faceted head·egg/peg body·exposed proximal cap·detached-looking
  limb는 v0.13 시각 방향을 충족하지 못하므로 current visual acceptance를 철회하고 `REWORK_REQUIRED`로 전환한다.
- r01/r02 technical source·Evidence와 r03..r10 시도는 역사로 보존하지만 current visual acceptance에서는
  `REWORK_REQUIRED / SUPERSEDED`다. 현재 r11은 round head, visible neck0와 horizontal T-pose를 유지하되,
  r10에 남아 있던 torso/shoulder/belly/hip의 저주파 다중 bulge를 연속 profile과 전신 fairing으로 제거한다.
- r11 torso→shoulder→arm은 cap·socket·patch·deep pit 없이 tangent-continuous하고, axilla는 짧고 둥글게 열린다.
  팔은 capsule 하나와 monotone taper가 소유하며 몸통→골반→다리는 접합선 없이 갈라진다.
- 표면 승인에서는 Neutral과 단일 소프트 Rake를 함께 본다. 고주파 band·terrace·normal ripple뿐 아니라
  짧은 간격으로 방향이 반복되는 저주파 bulge도 반려한다. 어깨·허리·배·골반은 구간별 다중 혹이 아니라
  하나의 넓고 연속적인 볼륨 곡선으로만 남긴다.
- C1B rework chain의 r11 Neutral/Silhouette/Rake 12-view를 `UG-C1B-NEUTRAL`에서 승인하기 전에는
  Rig·Pose clip·Animation·FBX·Unity import·Build·Commit/push/LFS 승격을 만들지 않는다.
- r11 candidate의 Body component1, `227942V/455880E/227940F`, triangle/quad `0/227940`, adjacent angle max
  `6.843839°`, exact mirror max `1.884956e-7H`, visible T-arm center max deviation `6.694555e-5H`는
  재현·검토값이지 user visual approval·production topology가 아니다.

### 0.1 1.9.0 변경 요약

- 첫 C1B Unity 반입에서 검출한 handedness 반전을 전역 수동 보정 없이 `ModelInteropProfile r02`의
  static `C1BBlockout` override로 해결했다.
- Canonical source는 그대로 두고 transient export copy만 ReflectX+winding reversal하며, Unity root는
  identity·scale1·`+Z Forward`를 유지한다.
- UV0/tangent가 없는 Blockout에만 tangent import `None`을 허용하고 production UV/tangent 규칙은 유지했다.
- Unity Neutral/Silhouette four-view를 2048²로 만들고 source mask bounds `0.005H`와 geometry signature를
  직접 비교한다. Neutral QA는 product lighting이 아닌 고정 key1+분산 fill 총0.35를 사용한다.

### 0.2 1.8.0 변경 요약

- Alpha Art를 Lobby Greybox, EyeSet·Mustache·Headwear 각 placeholder 대표 1개 또는 동등 최소 catalog,
  한국어 Text, basic SFX로 제한하고 BGM을 0으로 했다.
- production Lobby, full Cosmetic catalog, English·font fallback과 music을 post-Alpha로 분리했다.
- Disconnect grace Character를 freeze·ghost·invulnerable로 표현하지 않고 current Alive/spectator state를
  따르게 하며 Forfeit를 gameplay elimination·PatchAuthor cue로 꾸미지 않게 했다.
- 지속 Match·Active Patch·Ammo HUD를 0으로 하고 transient Patch UI와 developer-only debug를 분리했다.
- Unity 6.3 LTS·Blender 5.2 LTS를 profile reference로 고정하고 exact installed patch lock은 Plan 2.5
  `FDN-010`에 귀속했다.

### 0.3 1.7.0 변경 요약

- Pistol 7발 semi-auto single recoil과 LongGun 30발 full-auto sustained recoil·SpreadBloom 표현을 추가했다.
- visible Host Projectile의 first-hit 비관통 궤적과 read-only muzzle/recoil presentation 경계를 고정했다.
- Ammo 0 forced release→SpentPendingCleanup→remove를 Action Matrix와 Supply cap 판독 범위에 추가했다.
- Host Rigidbody impulse/torque·Muzzle·ShotSequence와 Animator recoil pose를 분리하고 Projectile/Hit authority를 0으로 유지했다.
- FIR-001..003 gameplay ownership, WPN-005 통합과 ANP-003 Firearm visual ownership을 연결했다.

### 0.4 1.6.0 변경 요약

- Ground Punch·Grab과 Air Kick·Dropkick·hand/ledge Grab의 action silhouette·presentation matrix를 추가했다.
- Host Rigidbody/Ragdoll authority와 Animator/procedural pose를 분리하고 gameplay root motion·Animation Event authority를 0으로 고정했다.
- Alpha procedural prototype과 C4/ANM production clip polish의 완료 범위를 분리했다.
- 네 Weapon을 M1911·AK-47에서 영감을 받은 generic low-poly firearm, baseball bat와 sledgehammer로
  시각 brief하되 실제 명칭·logo·marking·정확 복제를 금지했다.

### 0.5 1.5.0 변경 요약

- 승인 Catalog를 Patch12로 확장하되 Patch09..12의 Supply·Forced Drop도 Alpha에서는 평문 기능 결과와
  기존 Weapon asset/state만으로 검증하게 했다.
- Incoming Weapon은 combat·pickup·map control 표현에서 제외하고 착지 뒤 Loose부터 기존 Weapon 시각
  언어를 사용하게 했다.
- 인원별 반복 Supply·cap·Safe DropZone과 Patch09 double·Patch10 second wave가 base Character·Weapon·Map
  asset과 Hazard Telegraph를 바꾸지 않게 했다.
- Patch 전용 icon·Animation·VFX·SFX·최종 Layout은 계속 후속이며 `ReadyTeal + check + 상태 label`을 유지했다.

### 0.6 1.4.0 변경 요약

- 패치의 Alpha Gate를 plain-text 기능 검증으로 한정하고 icon·Animation·VFX·SFX·최종 Layout 제작을
  후속 표현 단계로 이동했다.
- Runtime의 의미 발동 event와 Art subscriber를 분리해 표현이 Authority 판정을 바꾸지 않게 했다.
- `PATCH-PROT-001..008`은 Character·Map·Weapon base asset과 profile을 변형하지 않는다는 경계를 추가했다.
- Guest Ready의 `ReadyTeal + check icon + 상태 label` 계약은 그대로 유지했다.

---

## 1. 아트 비전

`Project Hotfix`는 둥글고 단순한 캐릭터가 장난감 같은 로우폴리 공간에서 서로를 붙잡고
넘어뜨리는 3D 물리 난투 게임이다. 완전한 3D 에셋과 물리를 사용하되 화면에서는 2.5D처럼
간결하게 읽혀야 한다.

### 1.1 원칙

1. **Silhouette First**: 작은 화면에서도 머리, 몸통, 양팔·양다리와 동작 방향이 구분된다.
2. **Motion First**: micro detail보다 Grab, Punch, Kick, Dropkick, Throw, Sprint, down, GetUp과 Hazard motion을 우선한다.
3. **One Shared World**: 캐릭터·환경·무기는 같은 scale, bevel, palette와 material 언어를 쓴다.
4. **Readable Danger**: 위험을 색 하나가 아니라 형태·움직임·빛·소리로 전달한다.
5. **Constrained Expression**: Paint와 Cosmetic 자유는 허용하되 판정과 Camera 가독성은 고정한다.
6. **Reusable Production**: Base file, profile, kit와 비교 capture로 반복 품질을 유지한다.
7. **Reference, Not Replica**: 참고작의 고유 Mesh·실루엣·색·Animation을 복제하지 않는다.

---

## 2. 캐릭터 시각 언어

캐릭터는 하나의 `MasterCharacter`를 공유한다.

- 흰색 무안면 Paint canvas
- 둥근 head, visible neck `0`, torso에 직접 붙는 head
- 배 돌출을 줄인 비교적 곧고 부드러운 몸통
- 짧고 굵은 다리와 낮은 중심
- 가랑이보다 위에서 끝나는 짧고 굵은 중립 팔
- torso→shoulder→arm visible seam·groove·step·cap·detached boundary `0`, torso→U-crotch→leg의 continuous silhouette
- 별도 가시 손·발 없이 둥글게 닫힌 forearm/lower-leg terminal
- Neutral 단계 one review object와 visually continuous direct head overlap; production topology·UV·weight lock은 아님
- Paint·Cosmetic을 제거해도 완성된 외형

`Hybrid Core v0.13`은 C1a 큰 방향 승인본이다. exact 비율·Collider·reach·최종 Mesh 승인이 아니며
C1b orthographic·measurement 사용자 Gate를 거쳐야 한다. UI 이미지 속 캐릭터는 이 기준을
대체하지 않는다.

거부 failure class는 `FACETED_HEAD`, `EGG_OR_PEG_BODY`, `EXPOSED_PROXIMAL_CAP`,
`DETACHED_LOOKING_LIMB`, `FLAT_TERMINAL_DISC`, `BACKGROUND_THROUGH_HOLE`다. 한 view crop·조명·Pose로
이를 숨길 수 없으며, reference pixel 거리나 특정 vertex topology를 그대로 복제하는 방식도 금지한다.

부드러운 물리 인상은 젤리 Shader 하나가 아니라 형태와 움직임으로 만든다.

- smooth silhouette와 제한된 specular의 매트 표면
- impact 방향에 따른 짧은 visual squash
- terminal과 발의 약한 secondary lag
- Ragdoll·GetUp의 pose pop 억제
- gameplay Collider·Anchor에는 영향을 주지 않는 visual deformation

Sprint는 stride·상체 기울기·팔 lag로 기본 이동과 구분하되 별도 stamina 표현은 만들지 않는다.
반복 down은 같은 Round의 `DownCount` 증가에 따라 groggy/recovery가 길어진다는 사실을 Pose와
UI feedback으로 읽을 수 있게 하되 과도한 화면 효과로 gameplay를 가리지 않는다.

Patch12 자체의 전용 Pose·Animation은 Alpha 기능 Gate가 아니다. Character 물리 subset 001..008의
Jump, pulse, recoil, Ragdoll slide·bounce와 Weapon subset 009..012의 Supply·Forced Drop은 우선 권한
결과와 plain text로 검증한다. 후속 표현 작업이 base Rig, Collider, motion state, Down duration,
Weapon damage와 Supply admission을 바꾸지 않게 한다.

### 2.1 Action Animation Matrix

| Action | Alpha 기능 표현 | Production 표현 | 바꾸면 안 되는 Authority |
|---|---|---|---|
| Locomotion·Jump | primitive/procedural body lean·takeoff pose | in-place locomotion·jump clips | PhysicsRoot movement·Jump impulse |
| Ground L/R Punch | 해당 Forearm terminal의 짧은 방향 pose | 좌우 punch clip·additive torso follow | StrikeAnchor sweep·active phase·Hit |
| Ground/Air hand Grab | Hand/Grab anchor를 향한 procedural reach | reach·brace IK와 held follow | Contact·constraint·GripStress |
| Air L/R Kick | 해당 lower-leg terminal의 읽히는 kick pose | 좌우 foot kick clip·body counter-pose | KickAnchor sweep·AirAttackToken·Hit |
| Dropkick | 양발 전방 silhouette와 reduced-steering pose | in-place takeoff·flight·bounded recovery sequence | forward impulse·single AttackAction·dedupe |
| Lift·Throw | held target 방향 brace와 release pose | lift·throw anticipation/follow-through | constraint·Release·Throw impulse |
| Pistol semi-auto | 한 Shot의 강한 muzzle·body recoil pose | 짧고 강한 single recoil·settle clip | Ammo·Shot·Projectile·impulse/torque·Hit |
| LongGun full-auto | hold 중 누적 muzzle rise·brace·bloom pose | sustained fire와 release/gap recovery blend | ShotSequence·RecoilAccumulator·SpreadBloom·Hit |
| Ammo 0 | 즉시 forced release와 무상호작용 spent 상태 | SpentPendingCleanup release cue; reload pose 0 | owner release·spent timer·remove·supply cap |
| Bat·Hammer Melee | swing 방향과 기본 impact cue | 무기별 swing·impact clip | owner·attack window·Weapon sweep·Hit |
| Down/Ragdoll | Authority physics pose follow | pose-pop 없는 Animator→Ragdoll blend | DownEpisode·DownCount·body state |
| GetUp | face-up/down 구분과 root follow | in-place get-up clip·Ragdoll→Animator blend | clearance·AuthorityRoot·완료 state |

DropkickRecovery의 tumble은 Down/Ragdoll 연출을 재사용해도 semantic Down처럼 skull·groggy·DownCount cue를
표시하지 않는다. Grounded, GetUp 완료와 Round reset의 AirAttackToken 복원도 별도 power-up처럼 연출하지 않는다.

### 2.2 Animator·Ragdoll Presentation 정책

- 모든 gameplay action clip은 in-place이며 `Animator.applyRootMotion` 또는 동등한 root delta가
  PhysicsRoot·Collider·GameplayAnchor를 움직이는 경로는 0이다.
- Animator·procedural pose·IK는 Host semantic phase와 authority anchor를 읽기만 한다. Animation Event는
  Audio/VFX cue 외에 Shot, Projectile, recoil/spread, Attack, Hit, Grab, Release, impulse, Down과 GetUp 완료를 확정하지 않는다.
- Down 전환은 Host가 시작한 Authority Ragdoll pose·velocity로 Visual을 blend한다. Ragdoll 동안 Animator가
  gameplay body를 되감거나 pose를 강제하지 않는다.
- GetUp은 Host clearance·orientation 뒤 AuthorityRoot에 VisualRoot를 맞추고 in-place clip으로 blend한다.
  Clip root motion으로 공간을 통과하거나 순간이동하지 않는다.
- Remote Client는 Host action/Ragdoll state를 보간하고 같은 Action/Event identity의 반응을 한 번만 표현한다.

### 2.3 Alpha와 Production 분리

Alpha의 시각·오디오 범위는 다음 최소 기능 세트다.

| 영역 | Alpha | Post-Alpha Production |
|---|---|---|
| InteractiveLobby | Unity primitive와 기능 marker를 쓰는 Greybox | production environment·prop·lighting·polish |
| Cosmetic | `EyeSet`, `Mustache`, `Headwear` 각 placeholder 대표 1개(총 3개) 또는 같은 기능 범위의 동등 최소 catalog | full authored Cosmetic catalog |
| 언어·Font | 한국어 Player Text만 사용, 현재 개발 Font | English를 포함한 추가 언어·font fallback·production typography |
| Audio | action·hit·Hazard 판독용 basic SFX | BGM·music system과 production mix·확장 SFX |
| Match UI | 지속 Match/Active Patch/Ammo HUD 0, transient Patch UI와 developer debug만 | 별도 사용자 승인 뒤 제품 HUD·final Patch layout |

Alpha BGM은 0이다. Music volume control이나 빈 music event를 실제 music 제작 완료로 취급하지 않는다.
Korean-only는 Runtime Text Key 구조를 금지하는 뜻이 아니라 Alpha용 English 번역·font fallback·pseudo-localization
생산을 요구하지 않는 단계 경계다.

- `AIR-001..002`는 input arbitration, KickAnchor sweep, Dropkick physics·Hit·recovery와 2·3·4인 기능을
  primitive/procedural pose로 검증한다.
- `ANP-001`은 Action Animation Matrix, authority/presentation phase와 root-motion 0을 고정한다.
- `ANP-002`는 locomotion·hand·air prototype, `ANP-003`은 Weapon action·Ragdoll·network presentation과
  2·3·4인 통합을 담당한다.
- `FIR-001`은 ammo/fire mode/spent, `FIR-002`는 Projectile, `FIR-003`은 recoil/spread authority를
  소유하고 `WPN-005`가 Firearm gameplay를 통합한다. `ANP-003`은 같은 semantic state의 read-only visual만 소유한다.
- Alpha 기능 통과를 위해 완성된 clip polish, secondary motion, camera accent와 전용 VFX·SFX를 요구하지 않는다.
- C4/ANM polish는 승인된 ActionId, active window, Collider, impulse와 network event를 수정하지 않는다.
- 30초 Disconnect grace의 Alive Character는 기존 physical pose·impact·Down·Camera framing을 그대로
  사용한다. ghost, freeze, invulnerability bubble과 안전지대 이동을 표현하지 않으며 reconnect는 현재
  Alive 또는 spectator presentation으로 수렴한다.
- explicit Leave·timeout Forfeit는 OOB·LethalHazard elimination이나 PatchAuthor cue처럼 연출하지 않는다.
  Forfeit로 한 명만 남아 Lobby로 돌아가는 경로에도 score·Patch 획득 celebration을 만들지 않는다.
- Match local menu는 local-only·non-pausing overlay일 뿐 world·Character·Hazard·Patch timer를 pause한 것처럼 freeze-frame,
  global dim transition 또는 authority state 변경을 만들지 않는다.

---

## 3. Paint·Cosmetic 시각 기준

사용자는 local Preset에서 BaseColor, AccentColor와 Brush Stroke를 편집한다. 외부 이미지·사용자
Mesh·Shader·script는 가져오지 않는다.

Paint 기준:

- 전신 unique UV
- 머리 전면·가슴 중앙·terminal 주면의 불필요한 seam 없음
- 관절 변형에서 뒤집힘·bleed 최소화
- Preview와 실제 P2P 공유 결과의 색·Brush 모양 일치
- 흰색 기본 외형과 Player marker가 복잡한 Paint에서도 읽힘

Cosmetic 기준:

- 게임 제공 3D Mesh만 사용
- Alpha content는 `EyeSet`, `Mustache`, `Headwear` 각 placeholder 대표 1개(총 3개)를 필수로 하며,
  category를 바꾸면 얼굴 부착형·돌출 장식형·머리 착용형의 동등 최소 catalog를 명시
- fixed color, material, authored size와 mount pivot
- 전신 surface에서 위치·3축 회전·복제·삭제
- 사용자 scale·tint 없음
- 같은 위치 중첩과 Character Mesh 관통 허용
- Collider, mass, hitbox, reach와 Camera bounds 불변

외형은 local에 저장하고 AuthorityHost가 version·크기·catalog·개수 한도를 확인한 뒤 P2P로 relay한다.
검증 실패는 해당 플레이어만 승인된 기본 외형으로 대체하며 다른 참가자의 경기 진행을 막지 않는다.
이 최소 catalog로 Cage·배치·저장·relay를 검증한 뒤 full catalog 제작은 post-Alpha에서 별도 Art Gate로 연다.

Customization은 Lobby에서 `C`로 어디서든 진입한다. Booth는 공간 연출로 남길 수 있지만 접근
조건이나 권한 trigger가 아니다.

---

## 4. 환경·맵 아트 언어

환경은 큰 형태와 움직임 중심의 로우폴리 장난감 세트다.

- photo texture와 사실적 마모보다 단색·gradient·mask·decal 우선
- 같은 class의 bevel과 normal 처리 통일
- Render Mesh와 단순 Collision Mesh 분리
- 플레이 면, grab 가능 표면과 배경 장식의 형태·대비 분리
- 움직이는 부품을 `StaticFrame`과 `MovingPart`로 분리
- 배경에 gameplay Collider를 넣지 않음
- 맵 전체를 하나의 Blender Scene으로 반입하지 않고 module·Prefab으로 조립

공통 `EnvironmentKit`은 floor, wall/rail, ramp, lever/handle, warning sign, panel, light와 background
prop을 공유한다. 개별 맵은 고유 LethalHazard와 핵심 배경에 제작 시간을 집중한다.

InteractiveLobby는 Alpha에서 gameplay·Ready/Start·Customizer·reconnect 흐름을 검증하는 Greybox만
제작한다. 승인 UI 이미지의 분위기와 prop 후보는 reference일 뿐 production Lobby environment,
완성 lighting, decorative prop set과 final material은 post-Alpha Art scope다.

Hazard 표현:

| 상태 | 시각 목표 |
|---|---|
| Idle | 기능·방향은 보이되 낮은 강조 |
| Telegraph | 형태 변화·motion·light·audio로 사전 경고 |
| ActiveNonLethal | 아직 탈출·구출 가능한 움직임 |
| ActiveLethal | LethalHazard에만 존재, 치명 구간 최고 대비 |
| Recovery | 위험 종료와 재작동 대기 |

DisplacementHazard에 가짜 ActiveLethal을 만들지 않는다. 색을 제거한 capture에서도 인접 phase가
형태·motion·timing 중 하나 이상으로 구분돼야 한다.

---

## 5. 무기 아트

Pistol, LongGun, Bat, Hammer는 같은 로우폴리 형태 언어를 사용한다.

| Internal/debug functional ID | 내부 Visual Brief | 제품 사용자 노출 |
|---|---|---|
| `Pistol` | M1911에서 큰 slide·grip 비례만 참고한 generic low-poly pistol | 없음 |
| `LongGun` | AK-47에서 긴 barrel·receiver·curved magazine 판독성만 참고한 generic low-poly rifle | 없음 |
| `Bat` | 손잡이와 타격부가 명확한 low-poly baseball bat | 없음 |
| `Hammer` | 긴 handle과 큰 양면 head가 읽히는 low-poly sledgehammer | 없음 |

M1911·AK-47은 내부 reference role일 뿐 제품 Weapon 이름·브랜드가 아니다. 실제 logo, 제조사·모델명,
serial·selector marking, 고유 각인·색 배치, exact silhouette·치수와 부품 구성을 복제하지 않는다.
네 functional ID는 source·debug·Evidence에서만 사용한다. 일반 명칭을 포함해 Weapon 이름을 HUD,
Pickup prompt, Patch 문장, 결과와 사용자 설정에 노출하는 경로는 0이다.

- 공용 Camera 최대 거리에서 종류와 앞뒤 방향이 구분되는 큰 silhouette
- Forearm terminal과 맞는 손잡이 두께
- `GripSocket_Main`, optional Support, CenterOfMass 기준
- terminal cap에 소폭 겹쳐 별도 손 없이 잡힌 인상
- Render Mesh, 단순 Collider와 Socket 분리
- 실제 상표·제조사 외형·사실적 각인 금지

`WPA-001`은 위 visual brief와 네 Weapon scale lineup, `WPA-002`는 Blender source·UV·material·LOD,
`WPA-003`은 Blender→Unity import·Collider/Socket overlay와 2·3·4인 visual Lock을 담당하며
`UG-WEAPON-ART`에서 승인한다.

W1 입력 사용자 승인 뒤 Alpha에서는 다음 실제 전투 표현을 제작한다.

- Pistol semi-auto single recoil, 7-shot progression, muzzle feedback와 spent release
- LongGun full-auto sustained recoil, 30-shot progression, 누적 muzzle rise·SpreadBloom와 recovery
- Host-visible Projectile의 진행 방향·TTL 종료·first impact와 no-pierce/no-ricochet 판독
- Bat·Hammer swing, impact, damage/knockback와 drop
- Host 판정 event에 따른 단일 visual/audio response
- 2·3·4인 Camera에서 사용자·방향·impact 원인 판독

Pistol 총 Ammo 7, LongGun 총 Ammo 30과 reload 0은 확정이다. 정확한 cadence, Projectile
speed/radius/TTL, damage·knockback, recoil/spread/bloom/recovery와 swing timing은 Alpha tuning이다.
W1 전에는 무기 입력을 시각 asset로 먼저 굳히지 않는다.

- Pistol은 narrow spread와 한 발마다 강한 bounded Host recoil을 짧고 명확한 pose로 표시한다.
- LongGun은 Host의 deterministic ShotSequence·RecoilAccumulator·SpreadBloom을 따라 hold 중 누적 pose,
  release 또는 승인 gap 뒤 recovery pose를 표시한다. Visual frame이 bloom 값을 만들지 않는다.
- Host Rigidbody impulse/torque와 authority Muzzle 방향이 gameplay 기준이다. Camera/Animator recoil은
  read-only cue이며 Projectile path·Hit를 움직이지 않는다.
- Projectile, muzzle rise와 recoil pose는 SharedGameplayCamera subject bounds를 키우지 않는다.
- Projectile은 눈에 보이되 tracer가 다른 Target을 관통하거나 튕긴 것처럼 보이면 안 된다. 첫 blocking
  impact에서 끝나며 Map hit를 Lever·Hazard activation처럼 연출하지 않는다.
- Ammo 0 forced release 뒤 `SpentPendingCleanup`은 `START 2~4초` 동안 비상호작용 상태로 보이고 제거된다.
  reload, magazine 교체, ammo pickup pose·icon·sound는 만들지 않는다.
- Firearm Fire/Projectile 표현은 Playing과 SuddenDeath에서 유효하고 RoundResult 진입 시 active
  Projectile cue를 정리한다. Supply만 SuddenDeath에서 중단되며 기존 Firearm combat은 유지된다.

W1과 WeaponUse 표현은 승인된 Air L/R quick-tap Kick과 L+R chord Dropkick mapping을 덮어쓰지 않는다.
Airborne에서 WeaponUse를 허용할지와 별도 입력·우선순위는 `UG-W1` 결정으로 남기며 Art가 임의의
air-fire·air-swing pose로 gameplay binding을 선결정하지 않는다.

반복 Supply는 같은 네 Weapon asset을 재사용한다. 별도 “보급 전용 무기” Mesh나 능력치를 만들지 않는다.

- 2인 `10초/22초/cap2`, 3인 `8초/16초/cap2`, 4인 `6초/12초/cap3`은 gameplay `START`이며
  Art가 timing·cap을 보정하지 않는다.
- Host가 선택한 Safe DropZone에서 `Incoming`으로 내려오는 동안에는 공격 준비 Pose, Pickup highlight,
  hit feedback과 map-control contact를 표시하거나 Camera subject로 사용하지 않는다.
- 안전하게 착지해 `Loose`가 된 뒤 기존 silhouette, Collider, Pickup·Held·Drop 표현을 사용한다.
- LandingClearance 대기 중에는 계속 비공격 `Incoming`으로 보이고, `NoSafeDropZone`·`LandingBlocked` 또는
  WeaponCleanupBoundary 제거를 성공 착지·impact·Patch 발동처럼 꾸미지 않는다. Alpha에서는 진단 text만 허용한다.
- cap은 Incoming·Loose·Held·SpentPendingCleanup을 모두 세므로 spent를 숨겨 gameplay cap을 속이지 않는다.
- OOB 뒤 즉시 대체 Weapon이 나타나는 연출, cap full backlog와 catch-up을 암시하는 queue 연출을 만들지 않는다.
- SuddenDeath에서 pending Supply Drop 표현을 취소하되 이미 존재하는 Weapon의 combat은 유지한다.
  SpentPendingCleanup deadline과 제거 표현도 계속 진행한다. RoundResult에서는 Fire cue와 active
  Projectile을 정리하고 Weapon은 reset lifecycle을 따른다.
- Round마다 네 Weapon이 한 번씩 든 결정적 shuffle bag과 Safe DropZone 후보는 일반 HUD에 노출하지 않고
  필요할 때 Alpha diagnostic text로만 확인한다.

---

## 6. 패치 표현 단계 경계

`PATCH_DESIGN.md` 0.5.0의 승인 `PATCH-PROT-001..012`는 Alpha에서 동작과 2·3·4인 결과를 먼저 검증한다.

- PatchAuthor는 plain text Trigger 두 개를 보고 하나를 고른 뒤 plain text Effect 두 개를 고른다.
- 다른 참가자는 대기 문구와 확정 결과 문장을 보고, 모두가 활성 패치 최대 3개를 text로 확인한다.
- 전용 icon, 선택 Animation, activation VFX, Patch SFX와 최종 화면 Layout은 Alpha 승인 조건이 아니다.
- Base Character size·Collider·mass, Weapon damage와 Map Hazard timing을 패치처럼 보이게 바꾸는 Art
  shortcut을 사용하지 않는다.
- Patch09 double supply는 capacity가 허용한 실제 0~2개 결과만, Patch10 second wave는 Base Pulse 뒤
  `START 6~10초`의 실제 0~1개 결과만 표현한다. 생성되지 않은 수량·Wave를 backlog처럼 쌓지 않는다.
- Patch11은 victim의 모든 고유 Held Weapon Instance, Patch12는 attacker source Weapon Instance의
  실제 Forced Drop 결과만 기존 Drop 표현으로 보여준다. Main·Support의 같은 Instance를 두 개로 보이게
  하거나 Weapon이 없는 victim에게 가짜 Drop을 만들지 않는다.
- Firearm Projectile마다 Patch03·04 결과 cue는 Action/Target dedupe 뒤 한 번만 표시한다. LongGun 반복
  Shot의 cue가 합쳐져 무한 recoil처럼 보이지 않게 하고, delayed Hit 때 source가 Spent/remove·owner loss면
  Patch12는 `NoEligibleTarget`이며 다른 Weapon의 가짜 Drop을 표시하지 않는다.
- 승인 Patch12의 same-trigger pair는 모두 상호 배타이므로 두 Effect가 동시에 활성화된 것처럼 합성 표현하지 않는다.

Runtime은 Patch ID, Trigger/Effect ID, source와 target이 포함된 의미 presentation event를 port로
내보낸다. 후속 UI·Animation·VFX·SFX는 이 port를 구독할 수 있지만 Simulation event를 새로 만들거나
hit·Grab·Down·Supply·Forced Drop·Hazard·elimination을 확정할 수 없다. Patch-specific art는 2·3·4인 기능 Gate 뒤 별도
Art brief와 사용자 검토를 거쳐 추가한다.

---

## 7. 색상·재질·조명

- 공통 neutral과 warning palette를 사용한다.
- 각 맵은 주 accent와 보조 accent를 기본 2개 이내로 둔다.
- Player 식별색을 배경의 넓은 면에 사용하지 않는다.
- 위험·상호작용·안전 색의 의미를 맵마다 바꾸지 않는다.
- 색각 차이가 있어도 shape·value·motion으로 상태를 구분한다.

공통 Material family:

- `MAT_Character_Paintable`
- `MAT_Environment_Matte`
- `MAT_Hazard_Active`
- `MAT_Interactable`
- `MAT_Glass_Restricted`
- `MAT_Decal`

`AlphaVisualQAProfile`은 채택 URP Shader family, palette swatch, texture color space, tone/exposure,
고정 QA light와 camera를 묶는다. Blender와 Unity는 같은 중립 조건에서 side-by-side 검토한다.
pixel-perfect나 임의의 색차 수치에 과적합하지 않고 의미 있는 hue, value, specular와 silhouette
drift가 없는지를 사람이 승인한다.

---

## 8. UI 시각 언어와 이미지 경계

UI는 현대적인 PC Desktop Editor의 밀도와 정렬을 사용한다.

- graphite base와 한 단계 밝은 flat panel
- 1px divider, 4~6px corner radius
- off-white text와 제한된 ActionAmber
- Guest Ready 확정에는 ReadyTeal Button을 사용하고 check icon·상태 label을 함께 표시
- pill·과한 gradient·metal frame·bolt 장식 금지
- 3D playfield와 캐릭터가 panel보다 먼저 보이는 구성

Alpha Match 중 지속 노출되는 Match HUD, Active Patch HUD와 Ammo HUD는 0이다. Ammo·ShotSequence,
Projectile·recoil/spread·Spent와 active Patch 상세는 developer-only debug capture에서만 보며 일반
Player surface에 남기지 않는다. Round 사이 transient Patch selection/result Text와 local-only Match
menu는 허용하지만 두 surface 모두 Simulation·Patch·physics authority가 0이고 global pause를 만들지 않는다.

Alpha Player Text는 한국어만 제작한다. English copy, font fallback과 production typography는 post-Alpha다.
현재 한국어 기능 Text는 누락 glyph 없이 읽혀야 하지만 이 단계에서 다국어 layout을 Alpha Gate로 만들지 않는다.

이미지별 사용 범위:

| 이미지 | 사용할 것 | 사용하지 않을 것 |
|---|---|---|
| Main Menu approved | full-bleed diorama, 왼쪽 action hierarchy, graphite/Amber | Character 비율·손·Rig |
| Character Customizer approved | CommandBar·AssetBrowser·Viewport·Inspector 밀도 | `크기` tool, 고정 slot 해석, 예시 Character 비율 |
| Interactive Lobby v2 | playfield-first, compact room/roster, world prop 분위기 | `[R] 준비`, pixel-perfect 배치, Character 비율 |
| Private Lobby approved | 색·Typography·density | 정적 lineup, 전체 높이 rail, UI Start Button |

Customization은 `C`로 어디서든 진입하고 Ready·시작 UI는 상위 UI 규칙을 따른다. Lobby Start 방식은
본 아트 문서가 새로 정의하지 않는다.

Alpha 패치 선택 화면은 본 절의 최종 시각 Layout 승인 대상이 아니다. transient plain text
선택·timer·결과와 필요 시 developer debug의 활성 목록만 확인하고 graphite panel, icon family와
전환 motion은 후속 설계에서 정한다.

---

## 9. Blender → FBX → Unity 파이프라인

| 단계 | 산출물 |
|---|---|
| Greybox | Unity Primitive와 gameplay 검증 |
| Style Preflight | `LowPolyStyleProfile`, `AssetBrief`, reference lineup |
| Blender Source | `.blend`, Mesh, Skeleton, UV, 분리 부품·Pivot |
| Export | versioned FBX, reference render, `GenerationManifest` |
| Unity Assembly | Prefab, Material, Collider, Rigidbody, LOD |
| Visual QA | 2·3·4인 Camera capture, 판독성과 성능 결과 |

### 9.1 Profile

`LowPolyStyleProfile`은 shape language, bevel class, normals, material family, palette와 금지 detail을
정한다. `ModelInteropProfile`은 Blender/Unity version, export/import preset, unit·axis·transform,
normal/tangent와 material 처리 결과를 추적한다.

Toolchain family reference는 Blender 5.2 LTS와 Unity 6.3 LTS다. 실제 작업에 설치한 정확한 patch
version, Unity package manifest/lock과 Blender export preset revision은 Plan 2.5 `FDN-010`이
profile에 기록·고정한다. LTS family 문자열만 일치하고 exact installed patch가 누락된 산출물은
GenerationManifest와 import parity Gate를 통과하지 못한다.

C1B-005가 검증한 `ModelInteropProfile-ART-001-r02`는 base r01을 폐기하지 않고 static
`C1BBlockout`에만 다음 override를 둔다.

- Blender source는 수정하지 않고 transient export copy의 X handedness만 반사한다.
- 반사와 동시에 face winding을 뒤집어 outward normal을 보존하고 `bake_space_transform=true`로 내보낸다.
- Unity는 identity root, scale1, positive determinant와 `+Z Forward`를 가져야 하며 개별 Rotation·Scale
  wrapper나 post-import normal repair는 0이다.
- C1B Blockout의 UV0/tangent0은 수치·실루엣 검토 범위에서만 허용하고 Unity tangent import는 `None`이다.
  Production Character·Paint·normal map·Skinned source에는 이 예외를 사용할 수 없다.
- FBX byte hash는 선택된 canonical 산출물 identity로 기록하되, 재-export metadata 차이를 숨기기 위한
  binary patch는 하지 않는다. Source/preset hash와 semantic geometry signature를 함께 비교한다.

Neutral QA lighting은 제품 Lighting이 아니다. Blender rotation을 Unity Euler로 그대로 복사하지 않고 실제
light ray를 좌표 변환해 key relative intensity `1.0`과 Back/Left/Right fill 합계 `0.35`를 고정한다.
모든 View에서 off-white body와 limb 접합이 읽혀야 하며, 이 조명으로 Palette·Shader를 승인하지 않는다.

자동화와 Blender 보조 도구는 생산 수단이지 품질 승인자가 아니다. Prompt나 script 실행 성공,
FBX 생성만으로 완료 처리하지 않는다.

### 9.2 두 단계 Gate

Character rework에는 production Style Gate 앞에 `UG-C1B-NEUTRAL`이 있다. r11의
Front·Side·Back·ThreeQuarter Neutral/Silhouette/Rake에서 round head·visible neck0·head direct torso attachment,
horizontal T-pose·tangent-continuous shoulder/axilla·bean torso·U-crotch/leg·rounded terminal을 사용자가 승인하기 전 `C1BRW-004` Pose·Animation과
`C1BRW-005` FBX/Unity import를 시작하지 않는다. 이 Gate는 pixel-perfect
comparison이나 production topology 승인이 아니라 현재 Neutral 방향 승인이다.

제작 전 `StylePreflight`:

- AssetBrief의 목적·bounds·silhouette·moving part·Socket·Collider·금지 요소 검토
- 기존 MasterCharacter와 승인 module을 곁들인 scale 방향 승인

제작 후 `StyleConsistencyGate`:

- 같은 orthographic framing의 front·side·back·three-quarter
- 승인 캐릭터·환경 module과 scale lineup
- material, palette, bevel, normal class 비교
- Character·MovingPart의 Pose·Pivot·Socket overlay
- ModelInteropProfile을 사용한 Unity import
- 2·3·4인 Min/Max Camera capture
- Blender reference와 Unity material side-by-side
- GenerationManifest와 자동 구조 검사

C1B static parity는 Blender/Unity Silhouette mask의 2048² foreground bounds를 같은 framing에서 비교해 최대
`0.005H`만 허용한다. IoU는 관찰값으로 기록하지만 임의 제품 승인 threshold로 사용하지 않는다. 동시에
quantized position·normal surface signature, landmark17, bounds와 `+Z Forward`를 확인한다. PNG 존재만으로
통과시키지 않으며, 실제 Animation이 없는 static Pose에서 motion 자연스러움을 주장하지 않는다.

Preflight sample 승인으로 최종 production asset의 post-import Gate를 대체하지 않는다.

---

## 10. LOD·성능·판독성 Gate

대량 제작 전 `ArtBenchmark`는 최소 다음을 포함한다.

- v0.13 C1a와 승인 C1b 한 체형
- C2 Skeleton·Ragdoll·Grab·Sprint·반복 down/GetUp
- Ground Punch·Grab, 좌우 Air Kick, Dropkick·DropkickRecovery와 Air hand/ledge Grab
- Pistol single recoil·Spent release, LongGun sustained recoil·SpreadBloom recovery와 visible Projectile first impact
- 흰색 기본·Paint·Alpha placeholder Cosmetic 3종 또는 승인된 동등 최소 catalog
- production art가 아닌 InteractiveLobby Greybox의 Ready/Start·Customizer·reconnect 판독
- 공통 환경 module과 Lethal/Displacement Hazard
- 네 Weapon의 Grip과 W1 뒤 실제 combat feedback
- 2·3·4인, 16:9·16:10·21:9 Camera capture
- 한국어 기능 Text와 basic action·hit·Hazard SFX; BGM·English·font fallback은 제외

Patch-specific icon·Animation·VFX·SFX asset은 이 Alpha `ArtBenchmark`의 필수 항목이 아니다. 다만
`PATCH-PROT-001..012`를 활성화해도 base silhouette, Collider 정렬, Incoming→Loose·Held→Spent/Forced Drop의
Weapon 판독성과 Hazard Telegraph가 깨지지 않는지는 2·3·4인에서 확인한다. Supply cap 최대 상태에는
Incoming·Loose·Held·SpentPendingCleanup을 모두 포함한다.

Character LOD0 6k/10k/14k, LOD1 50~60%, LOD2 20~30%는 최종값이 아니라 비교 시작 범위다.
2·3·4인, 최대 Cosmetic, 인원별 Supply cap의 Incoming/Loose/Held/Spent Weapon, active Projectile,
Ragdoll과 Hazard를 함께 둔 worst case에서 GPU/CPU·draw call·memory,
LOD pop과 silhouette를 기록한다. 시각 차이가 없는 가장 낮은 후보를 선택한다.

---

## 11. 승인 전 미결정

- C1b exact 비율과 gameplay scale
- Sprint multiplier와 down/groggy duration profile
- 최종 Palette와 Shader 구현
- 최종 Polygon·Texture·Material·LOD·GPU 예산
- Alpha EyeSet·Mustache·Headwear 각 placeholder 대표 1개의 exact shape·authored size 또는 동등 최소
  catalog 정의; full catalog는 post-Alpha
- W1 fire/swing/drop binding과 melee/WeaponUse input balance
- Pistol/LongGun cadence·Projectile speed/radius/TTL·recoil/spread/bloom/recovery와 Spent cleanup visual tuning;
  Ammo 7/30과 reload 0은 확정
- Airborne WeaponUse 허용 여부·별도 입력과 Kick/Dropkick 우선순위의 `UG-W1` 결정
- M1911/AK-47 reference를 generic silhouette로 변환한 네 Weapon의 `UG-WEAPON-ART` 결과
- 반복 Supply의 최종 density·Incoming 낙하 표현·Safe DropZone marker와 Patch09..12 전용 연출
- 필요한 최소 baked animation clip
- 패치 icon·Animation·VFX·SFX와 최종 selection/activation Layout
- production InteractiveLobby environment·prop·lighting
- English와 추가 언어, font fallback·production typography
- BGM·music system과 production audio mix; Alpha는 basic SFX만 사용하고 BGM 0
- 지속 Match/Ammo/Active Patch HUD를 post-Alpha에 새로 도입할지 여부와 별도 사용자 승인

모든 값은 2·3·4인 실제 Unity 화면, 성능 결과와 사용자 Gate 없이 `LOCKED`로 표기하지 않는다.
