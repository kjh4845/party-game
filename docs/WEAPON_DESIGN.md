# Project Hotfix 무기 설계·Alpha 전투 Gate

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| 문서 버전 | 0.7.0 Alpha Minimal UI·Participation·Firearm·Weapon Combat Baseline |
| 최종 수정일 | 2026-08-26 |
| 상위 기준 | `01_PRD.md` 1.8.0, `02_SRS.md` 1.8.0 |
| 기준 | `CHARACTER_TECHNICAL_SPEC.md` 0.12.0, `ART_DIRECTION.md` 1.9.0, `PATCH_DESIGN.md` 0.5.0 |
| 상태 | 네 무기 archetype·권한 보급·총기 발사 구조·초기 Patch12 결정, W1 세부·balance와 exact visual Lock 대기 |

### 0.1 0.7.0 변경 요약

- Persistent Match HUD와 Player-facing Ammo HUD를 제거하고 Host Ammo·fire/projectile 정보는 developer-only
  debug에서만 확인하게 했다.
- Match Esc와 unexpected disconnect grace 동안 새 Weapon input은 neutral이지만 Character는 계속
  physical·vulnerable하고 기존 Held/Loose Weapon·projectile 수명주기는 진행되는 권한 경계를 추가했다.
- explicit Leave/grace 만료 Forfeit의 owner 정리, Forfeit PatchAuthor 0, 한 명 잔존 시 score·Patch 없이
  OpponentLeft→Lobby와 Host Loss Session 종료를 고정했다.
- Unity 6.3 LTS·Blender 5.2 LTS exact patch lock, repository/LFS·style profile·license/NOTICE 기반과
  Korean-only·기본 weapon SFX·BGM 0의 Alpha 범위를 연결했다.

### 0.2 0.6.0 변경 요약

- Pistol은 press당 한 발의 semi-auto·총 7발, LongGun은 hold full-auto·총 30발로 승인했다.
- reserve ammo와 reload를 제거하고 ammo 0 뒤 Host forced release→`SpentPendingCleanup`→제거 수명주기를 추가했다.
- Host projectile을 swept SphereCast, no pierce·ricochet, gravity 0 `START`, TTL·OOB·hit·reset 정리로 고정했다.
- Pistol accurate/strong per-shot recoil과 LongGun deterministic cumulative spread bloom, authoritative bounded
  recoil physics와 read-only visual recoil 경계를 추가했다.

### 0.3 0.5.0 변경 요약

- Pistol은 M1911-inspired low-poly, LongGun은 AK-47-inspired low-poly, Bat은 baseball bat,
  Hammer는 sledgehammer(오함마) functional archetype으로 승인했다.
- 실제 제조사명은 사용자에게 표시하지 않고 logo·marking·serial과 exact replica를 금지했다.
- 각 archetype의 silhouette·Grip/Combat Socket·Collider·CenterOfMass와 Blender source→Unity import
  비교 기준을 한 source에 고정했다.
- WPA-003의 실제 반입 비교 뒤 `UG-WEAPON-ART`에서 exact visual을 별도 Lock한다.

### 0.4 0.4.0 변경 요약

- `라운드당 무기 1개` 안을 폐기하고 60초 Playing의 인원별 주기 보급과 동시 Weapon 상한을 승인했다.
- `Incoming → Loose → Held` 권한 수명주기, 결정적 4종 shuffle, 안전 DropZone, cap skip·OOB 보충과
  Round reset 계약을 추가했다.
- Character Patch `PATCH-PROT-001..008`에 Weapon Patch `PATCH-PROT-009..012`를 더해 초기 Patch12로 확장했다.
- Supply 파생 spawn 재귀 0, Weapon hit forced drop의 기존 ownership·hit·drop·reset 우회 0을 명시했다.
- 보급·Patch 전용 최종 UI·VFX·SFX는 계속 후속으로 두고 Alpha에서는 기능 text와 의미 event만 요구한다.

---

## 1. 공통 결정

Alpha는 다음 네 기능명과 제작 archetype을 유지한다.

| 기능 ID | 승인 제작 archetype | silhouette 계약 |
|---|---|---|
| `Pistol` | M1911-inspired low-poly pistol | 짧은 barrel/slide mass와 한손 grip이 읽히는 compact handgun. 실물 치수·control을 복제하지 않음 |
| `LongGun` | AK-47-inspired low-poly long gun | 긴 barrel/receiver, stock과 완만한 curved magazine의 큰 mass가 구분되는 양손 long gun. 실물 receiver를 복제하지 않음 |
| `Bat` | generic baseball bat | 가는 handle에서 굵은 barrel로 이어지는 단순 silhouette. wood/metal surface는 AssetBrief `START` 비교 |
| `Hammer` | sledgehammer(오함마) | 긴 handle과 크고 양쪽이 읽히는 무거운 head. claw hammer로 해석되지 않아야 함 |

M1911, AK-47, baseball bat와 sledgehammer는 제작자가 형태 역할을 이해하기 위한 functional reference다.
일반 Player UI에는 Weapon 이름을 표시하지 않는다. 내부 ID·Alpha diagnostic·test fixture에서만
`Pistol`, `LongGun`, `Bat`, `Hammer`를 사용하며 실제 제조사·상표와의 제휴나 역사적 복제품으로 설명하지 않는다.
일반 Player-facing Match 화면에는 Ammo 숫자·magazine·reserve·reload prompt도 표시하지 않는다.
Host-confirmed AmmoRemaining, FireMode, ShotSequence, projectile와 Spent 정보는 developer-only debug에서만
내부 ID와 함께 확인한다.

각 Weapon Asset에는 다음 금지 항목을 적용한다.

- logo, trademark, 제조사명, 국적·군 표식, serial number와 실제 각인 0
- 실물 청사진 tracing, exact receiver/slide/control 치수와 식별 가능한 고유 marking 복제 0
- 사진 texture와 실물 product scan 0
- silhouette 판독에 필요하지 않은 작은 safety·pin·screw·sight detail 양산 0

모든 무기는 다음 공통 규칙을 따른다.

- 왼손과 오른손 어느 쪽도 Main hand가 될 수 있다.
- 필요하면 반대손 Support Grip을 사용한다.
- 별도 Hand Mesh 없이 Forearm terminal cap과 손잡이가 소폭 겹쳐 잡힌 인상을 만든다.
- Character collider, mass, base reach와 `ActiveSubjectBounds`를 바꾸지 않는다.
- AuthorityHost가 pickup/grab, owner, fire/swing, hit, damage, knockback와 drop을 판정한다.
- Host 검증을 모두 통과한 공격만 한 번의 `TRG-ATTACK-HIT-CONFIRMED` 의미 event를 만들 수 있다.
- Guest는 입력을 보내고 Host 결과를 표현한다.
- 2·3·4인은 같은 Weapon catalog·authority·combat rule을 사용한다. 보급 timing과 동시 상한만 아래의
  승인된 인원별 `START` profile을 사용한다.

### 1.1 Weapon AssetBrief와 exact visual Lock

각 Weapon은 다른 문서가 새 형태를 만들지 않도록 본 문서의 archetype과 하나의 versioned AssetBrief를
single source로 사용한다.

- 동일 source의 front·side·back·three-quarter orthographic reference와 MasterCharacter scale lineup
- gameplay envelope, muzzle/impact 방향, handle·head/barrel bounds와 앞뒤 silhouette
- `GripSocket_Main`, 필요한 `GripSocket_Support`, `CombatSocket`, `CenterOfMassSocket`의 목적과 overlay
- Render Mesh와 단순 ColliderSet 분리, Dynamic MeshCollider 0
- Blender editable `.blend`, export FBX, reference render와 Unity Prefab revision 추적
- 승인 `ModelInteropProfile`의 unit·axis·transform·normal/tangent·material import 사용
- Unity root scale `(1,1,1)`과 개별 수동 rotation·scale·normal 보정 0

Pistol과 LongGun은 approved inspired archetype을 유지하되 exact replica가 되지 않도록 큰 mass와 gameplay
방향만 Lock한다. Bat의 wood/metal surface와 authored color는 visual brief의 `START` 비교 항목이다.
Hammer는 sledgehammer head/handle mass를 유지한다.

WPA-003은 Blender source와 Unity 결과의 silhouette, scale, Socket·Collider·CenterOfMass와 material을 같은
view에서 비교한다. 의미 있는 drift가 없고 2·3·4인 Camera에서 종류·앞뒤·owner가 읽힌 결과를 사용자가
`UG-WEAPON-ART`에서 승인해야 exact visual을 Lock할 수 있다. W1은 입력 Gate이므로 이 시각 승인을 대신하지 않는다.

---

## 2. 공통 구조와 상태

```text
Weapon
├── PhysicsRoot
├── RenderRoot
├── ColliderSet
├── GripSocket_Main
├── GripSocket_Support   # optional
├── CenterOfMassSocket
└── CombatSocket         # 승인된 무기 class에 한함
```

공통 상태 흐름:

```text
Scheduled Supply Pulse
→ Incoming
→ Loose
→ GripCandidate
→ HeldMain / HeldMainSupport
→ CombatReady
→ Fire 또는 Swing
→ Recovery
├→ ammo 남음: Held
└→ ammo 0: Host ForcedRelease → SpentPendingCleanup → Removed
→ ReleaseDrop
→ Loose
→ ReacquireGrip
```

state 이름 자체보다 다음 결과가 중요하다.

- owner와 held hand가 모든 Peer에서 같다.
- release/drop 뒤 Character relation이 해제되고 Rigidbody가 loose 물리로 돌아간다.
- 다시 잡으면 같은 Socket과 pose로 수렴한다.
- stale input이 새 owner나 다음 Round에서 fire/swing을 만들지 않는다.
- `SpentPendingCleanup`은 owner·pickup·fire/swing·hit·map interaction 0인 소진 상태이며 정리 전까지 supply cap에는 포함된다.

### 2.1 AuthorityHost 무기 보급

보급 시계의 원점은 RoundCountdown 종료 뒤 `Playing=0초`다. `라운드당 1개` 고정안은 사용하지 않고,
60초 Playing 구간 안에서 다음 `START` profile로 정규 supply pulse를 실행한다.

| 인원 | 첫 pulse | 이후 주기 | `Incoming+Loose+Held+SpentPendingCleanup` 동시 상한 | 60초 안의 예시 pulse |
|---:|---:|---:|---:|---|
| 2인 | 10초 | 22초 | 2 | 10, 32, 54초 |
| 3인 | 8초 | 16초 | 2 | 8, 24, 40, 56초 |
| 4인 | 6초 | 12초 | 3 | 6, 18, 30, 42, 54초 |

- Host는 Round 초기 participating roster로 이 profile을 한 번 고정한다. Disconnect·30초 Reconnect와
  중도 Forfeit는 현재 Round timer·cap을 바꾸지 않고 다음 Round 초기화에서만 새 roster를 반영한다.
- AuthorityHost만 pulse, admission, Weapon type, DropZone, spawn와 state transition을 확정한다.
- 정규 pulse의 기본 desired batch는 Weapon 1개다. pulse 시점의 `Incoming+Loose+Held+SpentPendingCleanup` Instance를 모두
  세고 남은 capacity까지만 admission한다.
- capacity가 0이면 해당 pulse는 `CapacityLimited`로 0개를 만들고 끝난다. 누락분을 queue하거나 다음
  pulse에 backlog로 더하지 않으며 다음 pulse 시각도 원래 cadence를 유지한다.
- Weapon이 OOB·유효 cleanup으로 제거되면 즉시 respawn하지 않는다. 비워진 capacity는 다음 정규 pulse가
  정상 admission할 때 보충한다.
- Projectile은 Weapon Instance 동시 상한에 포함하지 않지만 자체 bounded combat·reset 예산을 따른다.

Host는 Round 시작마다 `MatchSeed + Round + WeaponCatalogVersion`으로 Pistol, LongGun, Bat, Hammer가
각각 한 번 들어간 결정적 shuffle bag을 만든다. 실제 admission된 Weapon만 cursor를 소비하고 cap skip과
미생성분은 소비하지 않는다. 네 종류를 모두 소비하면 같은 입력에서 결정적인 다음 bag을 만든다.

Map은 versioned safe DropZone pool을 제공한다. Host는 안정적인 Zone ID와 Round·pulse·spawn ordinal로
서로 다른 유효 Zone을 선택하며 Player Spawn, OOB·RecoveryBand, Lethal/DisplacementHazard, map control과
moving part의 작동 범위를 침범하는 Zone을 사용하지 않는다.

Admission과 landing에서 Host는 Zone의 `LandingClearance`가 Character, 다른 Incoming·Loose·Held·SpentPendingCleanup Weapon과
moving part에 겹치는지 다시 검사한다. admission 시 유효 Zone이 없으면 `NoSafeDropZone`으로 0개를 만들고
bag cursor·backlog·즉시 재시도를 만들지 않는다.

`Incoming`은 공급 표현 상태이지 공격이나 Hazard가 아니다.

- Character damage·Down·knockback·Grab, 다른 Weapon collision과 Weapon pickup 0
- Lever·Hook·Crane·panel 등 map control·Hazard contact/activation 0
- owner 없음, fire/swing/use 0, SharedGameplayCamera subject 0
- 승인 landing에 도달하고 clearance가 빈 Host transition 뒤에만 `Loose`가 되어 정상
  Collider·Grab·pickup·physics 활성

Landing이 막혀 있으면 noninteractive Incoming 상태로 `START 1~2초`만 clearance를 기다린다. 그 안에
비면 Loose로 전환하고, 끝까지 막히면 `LandingBlocked`로 제거한다. 제거 전에 Collider·Damage·Knockback은
활성화하지 않으며 이미 admission된 bag cursor를 되돌리거나 즉시 다른 Weapon을 투하하지 않는다.

Map의 Host `WeaponCleanupBoundary`는 playable·recovery surface 아래의 회수 불가능한 Loose Weapon을
제거한다. Held Weapon은 긴 Collider 일부가 경계를 넘었다는 이유만으로 삭제하지 않고 owner elimination
또는 유효 release 뒤에 평가한다. cleanup은 capacity만 비우며 다음 정규 pulse 전 보상 spawn은 없다.

Alpha 기능 표시는 safe landing primitive 또는 평문 상태만 사용한다. 최종 drop model, icon,
descent/landing VFX·SFX와 완성 HUD Layout은 기능 Gate가 아니다.
이는 Ammo HUD를 임시 Player UI로 만든다는 뜻이 아니다. Player-facing persistent Match/Ammo HUD는 0이고,
공급·Ammo·fire 진단 평문은 developer-only debug에만 둔다.

Playing→SuddenDeath 또는 RoundResult 전환은 새 정규 pulse와 pending derived wave를 모두 취소한다.
이미 존재하는 Incoming·Loose·Held Weapon은 Round reset까지 유지한다. `SpentPendingCleanup` deadline은
SuddenDeath에도 진행해 만료 시 제거하고 빈 capacity는 next pulse만 사용한다. Round reset은 supply timer,
shuffle cursor, pending wave, 모든 Weapon Instance, projectile, owner와 combat residue를 제거하고 새 Round의
profile을 처음부터 예약한다. 위 시간·주기·상한은 승인 구조 안의 `START` 값이며 2·3·4인 Evidence 뒤
조정할 수 있지만 인원 profile을 조용히 합치거나 `라운드당 1개` 안으로 되돌리지 않는다.

Reconnect Client는 current profile, next pulse, shuffle bag/cursor, pending second wave와 모든
Incoming·Loose·Held·SpentPendingCleanup state와 cleanup deadline을 Host snapshot에서 원자 복원한다. pulse·type·Zone을 다시 계산하거나 이미
admission된 Weapon을 중복 spawn하지 않는다.

### 2.2 Weapon Supply Patch09·10

정규 base pulse만 `TRG-WEAPON-SUPPLY-SCHEDULED`를 한 번 만든다. 같은 Trigger를 공유하는 두 Patch는
`TriggerOccupancy`에 따라 상호 배타다.

| Patch | Effect | Alpha 문장 | 공급 결과 |
|---|---|---|---|
| `PATCH-PROT-009` | `EFF-WEAPON-SUPPLY-DOUBLE` | `보급 시간이 되면 무기 두 개가 동시에 떨어집니다.` | 정규 pulse desired batch를 별도 Weapon Instance 2개로 변경 |
| `PATCH-PROT-010` | `EFF-WEAPON-SUPPLY-SECOND-WAVE` | `보급 시간이 되면 잠시 뒤 무기가 한 번 더 떨어집니다.` | 기본 admission 뒤 `START 6~10초`의 derived wave 1개 예약 |

`PATCH-PROT-009`도 base 동시 상한을 올리지 않는다. desired 2보다 capacity가 작으면 capacity만큼만
admission하고 `CapacityLimited`를 기록한다. capacity 1이면 1개, capacity 0이면 0개이며 미생성분의
backlog·retry는 0이다.

`PATCH-PROT-010`의 derived wave는 실행 시 capacity 1칸을 다시 admission한다. full이면
`CapacityLimited`로 0개를 만들고 종료하며 queue하지 않는다. `Playing`이 끝나기 전에 취소되거나
RoundGeneration이 바뀐 wave도 0개다. 두 Patch의 추가·derived spawn은
`TRG-WEAPON-SUPPLY-SCHEDULED`를 다시 만들지 않으며 shuffle cursor는 실제 admission 수만큼만 소비한다.
Patch가 Weapon Damage, ammo, cadence, 동시 상한과 Hazard timing을 변경하는 경로는 0개다.

### 2.3 Match Esc·Guest Leave·disconnect·Forfeit

- Match Esc는 Host Simulation·Round/supply clock을 멈추지 않는다. local gameplay input만 neutralize하므로
  새 pickup·manual drop·fire/swing request는 0이고, 닫을 때 Mouse all-up 뒤 Hand/WeaponUse를 재무장한다.
- local Match menu를 연 Character와 기존 Held/Loose Weapon은 무적·비충돌 상태가 되지 않는다. 이미 Host가
  수락한 projectile, recoil recovery, Spent deadline과 Weapon physics도 계속 진행한다.
- unexpected Guest disconnect는 30초 동안 neutral input으로 같은 Character·slot과 Held/Loose/Spent,
  Ammo·projectile·recoil/spread state를 보존한다. Character는 physical·vulnerable 상태이고 Weapon은 각
  current state의 기존 physics/lifecycle을 유지한다. disconnect만으로 owner를 바꾸거나 Weapon을 자동 삭제·재공급하지 않는다.
- grace 중 valid hit·Down·OOB·Hazard가 기존 forced drop/elimination을 만들 수 있다. reconnect는 Host의
  최신 owner·Ammo·projectile·Spent snapshot을 원자 복원하고 disconnect 전 input을 replay하지 않는다.
- 명시적 Guest Leave는 즉시 Forfeit, grace 만료도 Forfeit다. Host는 남은 owner/hand relation을 기존
  release 경로로 Instance당 한 번 정리한다. Round가 계속되면 유효 Weapon은 Loose가 되고, 이 transition은
  Weapon Hit·Attack Hit·Supply Trigger·PatchAuthor를 만들지 않는다.
- permanent participant가 한 명만 남으면 해당 Round의 score·PatchAuthor·Patch 적용을 0으로 두고
  `OpponentLeft` 뒤 Lobby로 복귀한다. 새 fire/supply를 중지하고 모든 Weapon·projectile·Spent·owner residue를 정리한다.
- Host Leave/Loss는 Session 종료다. Weapon Authority·projectile·shuffle cursor를 Guest에게 migration하거나
  Round score·Patch를 새로 확정하지 않는다.

---

## 3. Character 통합 Gate

실제 combat 전에 네 무기 모두 다음을 통과한다.

| Weapon | Grip·형태 Gate |
|---|---|
| Pistol / M1911-inspired | 좌·우 한손 Main Grip, compact 앞뒤 silhouette, body 관통, drop·reacquire |
| LongGun / AK-47-inspired | Main+Support reach, stock·receiver·barrel 방향, Chest·Head 관통, drop·reacquire |
| Bat / baseball bat | 한손·양손 간격, handle→barrel taper, swing 준비 envelope, loose collision |
| Hammer / sledgehammer | 한손·양손 간격, 양면 head 방향, center of mass, loose collision |

- terminal과 손잡이 겹침은 `0.01~0.015H`를 시작 시각 범위로 비교한다.
- Support hand가 닿지 않으면 팔을 늘리지 않고 Weapon Socket을 수정한다.
- 2·3·4인 공용 Camera 최대 거리에서 종류·앞뒤·owner를 구분한다.
- Held, Sprint, Jump, down/Ragdoll, GetUp에서 영구 관통·Socket 분리·pose 폭발이 없다.

---

## 4. W1 입력 사용자 Gate

LMB/RMB는 이미 각 손의 Tap Strike / Hold Grab을 담당한다. 따라서 actual weapon use 입력은
사용자 승인 없이 확정하지 않는다.

공중 기본 액션 예약은 W1보다 우선한다.

- airborne `LMB`는 왼쪽 AirKick, airborne `RMB`는 오른쪽 AirKick mapping을 우선 보존한다.
- 승인된 동시 click/chord는 Dropkick을 요청하며 WeaponUse가 이를 암묵적으로 가로채지 않는다.
- Weapon을 Held한 공중 상태에서도 Kick·Dropkick mapping을 삭제하거나 무기별로 다르게 바꾸지 않는다.
- airborne WeaponUse를 허용할지, Grounded와 같은 mode를 쓸지, 별도 입력을 둘지는 `UG-W1` 미결정이다.
- W1 승인 전에는 AirKick·Dropkick을 Weapon fire/swing으로 해석하거나 양손 click을 무기 사용으로 완료 처리하지 않는다.

W1에서 최소 다음을 비교한다.

| 안 | 설명 | 위험 |
|---|---|---|
| Context Hand | 무기를 든 손의 Tap/Hold 의미 변경 | Grab과 fire/swing 의도 혼동 |
| Weapon Mode | mode key 동안 손 버튼이 무기 사용 | mode 오류와 학습 비용 |
| Separate Use | 별도 WeaponUse/Drop 입력 | key 증가 |

사용자 결정에는 다음이 포함돼야 한다.

- held hand의 Tap/Hold와 기본 Strike/Grab 관계
- 한손·양손 무기 입력
- fire/swing과 Drop 구분
- Air L/R Kick·dual-click Dropkick을 보존하면서 airborne WeaponUse를 허용할지와 그 입력
- pickup이 Hold Grab의 자연스러운 결과인지 여부
- Sprint 중 fire/swing 허용·정확도·회전 제한 방향
- down/Ragdoll 진입 시 held relation과 drop 처리

W1 승인 전에는 실제 combat input을 임의 구현 완료로 표시하지 않는다. 승인 뒤 선택 입력을 문서와
튜닝 profile에 기록하고 네 무기에 공통 적용한다.

### 4.1 Character Patch03·04 Attack event 경계

`PATCH_DESIGN.md` 0.5.0의 `TRG-ATTACK-HIT-CONFIRMED`는 다음 단계로 source를 확장한다.

1. Character Patch01..08 기능 검증에서는 AuthorityHost가 확정한 Punch, AirKick과 Dropkick hit가 event source다.
2. W1 승인 뒤 Firearm과 Melee가 owner, 입력, fire/swing rate, 상태, collider/hit와 중복 방지를 모두
   통과하면 같은 의미 event source를 재사용한다.

`TRG-ATTACK-HIT-CONFIRMED`는 단순 contact, Client hit 주장, 발사 request, projectile spawn 또는 Swing
시작 event가 아니다. 한 `AttackAction`과 victim의 유효 hit를 Host가 확정한 뒤에만 한 번 발생한다.

- `PATCH-PROT-003 / EFF-HIT-KNOCKBACK`은 기존 damage 계산 뒤 victim knockback channel만 bounded하게 수정한다.
- `PATCH-PROT-004 / EFF-ATTACKER-RECOIL`은 Attack action당 attacker에게 recoil을 한 번만 적용한다.
- Patch는 damage, ammo, reload, fire/swing rate, ownership, pickup, forced/manual drop과 Round reset을
  성공 처리하거나 우회하지 않는다.
- Patch recoil·knockback이 map physical control을 원격 activation하거나 새 Weapon hit를 만들지 않는다.
- RoundGeneration이 지난 Attack/Patch event는 새 owner나 다음 Round에 적용하지 않는다.

Alpha에서는 Patch-specific muzzle variant, impact VFX, recoil Animation과 SFX를 만들 필요가 없다.
Runtime은 Patch ID, Attack source, attacker, victim과 결과가 포함된 의미 presentation event를 port로
내보내고 후속 표현 subscriber는 Authority damage·hit·impulse를 바꾸지 않는다.

### 4.2 Weapon Patch11·12 Hit forced drop

`TRG-WEAPON-HIT-CONFIRMED`는 W1 뒤 Host가 Firearm 또는 Melee의 Weapon source, owner, use rate,
AttackAction, victim과 중복 방지를 모두 검증한 Character hit에서만 한 번 발생한다. Punch, Hazard,
loose Weapon collision, projectile spawn, 빗나간 use와 Client hit 주장은 이 Trigger가 아니다.
같은 유효 Weapon hit는 Character 공격 규칙용 `TRG-ATTACK-HIT-CONFIRMED`와 Weapon 관계용
`TRG-WEAPON-HIT-CONFIRMED`를 같은 Root Attack identity로 만들 수 있다.

| Patch | Effect | Alpha 문장 | 강제 Drop 대상 |
|---|---|---|---|
| `PATCH-PROT-011` | `EFF-VICTIM-HELD-WEAPON-FORCED-DROP` | `무기로 공격을 맞히면 맞은 플레이어가 들고 있던 무기를 놓칩니다.` | hit 시 victim의 모든 유효 Held Weapon Instance |
| `PATCH-PROT-012` | `EFF-ATTACKER-SOURCE-WEAPON-FORCED-DROP` | `무기로 공격을 맞히면 공격한 플레이어도 사용한 무기를 놓칩니다.` | 해당 hit의 정확한 source Weapon Instance |

같은 Trigger를 공유하는 Patch11·12는 상호 배타다. Patch11에서 같은 Weapon의 Main+Support relation은
Instance 한 개로 dedupe하고 victim이 유효 Weapon을 들지 않았다면 `NoEligibleTarget`이다. Patch12는
hit 시점의 source instance만 대상으로 하며 이미 owner relation이 끝났다면 새로 든 다른 Weapon을
대신 놓게 하지 않고 `NoEligibleTarget`으로 끝낸다. 한 Attack이 여러 Target을 맞혀도 Patch12의 source
Weapon release는 AttackAction당 최대 한 번이다.

마지막 탄 projectile의 delayed hit 시 source Weapon이 `SpentPendingCleanup` 또는 Removed라면 hit·Damage·
Knockback과 immutable attribution은 그대로 유효하다. 다만 Patch12 forced-drop 대상은 없으므로
`NoEligibleTarget`이며 attacker의 다른 Held Weapon을 대신 놓게 하지 않는다.

Hit, damage, knockback와 attribution을 먼저 확정한 뒤 기존 forced release→owner 해제→Loose physics→
replication 경로를 한 번 호출한다. Patch는 이미 성립한 hit를 취소하거나 Damage, ammo, reload,
fire/swing cadence, Attack recovery, Weapon mass·Collider와 Round reset을 바꾸지 않는다. Down이나 다른
기존 원인도 같은 Weapon을 놓게 하면 Instance당 실제 release transition은 한 번만 처리한다.
Patch 전용 추가 drop impulse는 0이다.
Held→Loose는 같은 Weapon Instance의 상태 변경이므로 동시 상한 count를 줄이거나 새 supply pulse를
만들지 않는다.
Patch11·12 전용 최종 icon·Animation·VFX·SFX는 Alpha Gate가 아니며 plain text 결과와 semantic event만
초기 Patch12 기능 검증에 사용한다.

---

## 5. Pistol·LongGun Alpha 전투

W1 뒤 Pistol과 LongGun은 다음을 구현한다.

- fire request와 Host fire 판정
- muzzle 방향, authoritative projectile와 가시 feedback
- swept SphereCast를 사용하는 map blocker·Character hit 판정
- damage와 bounded knockback
- held relation 해제와 manual/forced drop
- loose collision과 reacquire
- Round reset에서 projectile·fire cooldown·ammo·spread bloom·recoil·owner·Spent residue 제거
- 승인 hit마다 `TRG-ATTACK-HIT-CONFIRMED`가 정확히 한 번 발생하고 invalid fire에는 0번 발생
- 승인 Firearm hit마다 `TRG-WEAPON-HIT-CONFIRMED`가 action-victim당 정확히 한 번 발생

### 5.1 승인 Ammo·Fire·Spent 수명주기

| Weapon | Fire mode | Spawn total ammo | reserve | reload |
|---|---|---:|---:|---:|
| `Pistol` | Host가 수락한 press edge당 정확히 1발의 semi-auto | 7 | 0 | 없음 |
| `LongGun` | 유효 WeaponUse를 hold하는 동안 승인 cadence의 full-auto | 30 | 0 | 없음 |

- Client는 ammo, fire cadence, shot ordinal과 마지막 탄 소진을 확정하지 않는다.
- Pistol held input과 key repeat는 새 press edge 없이 추가 shot을 만들지 않는다.
- LongGun은 release, owner/held 상실, invalid state, ammo 0, RoundResult 또는 reset에서 즉시 새 shot 생성을 멈춘다.
  SuddenDeath에서도 이미 존재하는 유효 Weapon의 fire는 계속 허용한다.
- 각 Host-accepted shot은 ammo를 정확히 1 감소시키고 별도 reserve pool·magazine 교체·reload action은 없다.
- AmmoRemaining은 Authority·developer diagnostic 값이며 Player-facing HUD 숫자가 아니다. accepted/rejected
  fire를 local prediction이나 persistent Player UI로 먼저 성공 표시하지 않는다.
- 마지막 유효 shot의 projectile을 생성한 뒤 ammo가 0이면 Host가 같은 Weapon Instance의 모든 hand relation을
  한 번만 ForcedRelease하고 owner를 해제해 `SpentPendingCleanup`으로 전환한다.
- `SpentPendingCleanup`은 `START 2~4초` 동안 cap에는 포함되지만 Collider·pickup·Grab·fire/swing·hit·
  Damage·Patch Trigger·map control·Hazard interaction은 0이다. deadline 또는 Round reset에서 Host가 제거한다.
- 마지막 shot에서 이미 생성된 projectile은 source Weapon이 Spent/Removed가 되어도 취소하지 않는다.
  shot accept 시 고정한 immutable attacker/source snapshot으로 TTL·OOB·hit 규칙을 계속 실행한다.
- Spent 제거는 capacity만 비운다. 즉시 대체·backlog·catch-up은 없고 다음 정규 supply pulse만 남은 capacity를 admission한다.
- manual/Patch/Down forced release와 ammo-0 release가 같은 tick에 겹쳐도 Instance당 실제 release와 state transition은 한 번이다.

### 5.2 Authority projectile와 hit

- Host만 accepted shot마다 하나의 projectile identity, muzzle pose, direction, speed, SphereCast radius와 TTL을 만든다.
- speed·radius·TTL은 versioned Firearm profile의 `START` tuning이며 projectile gravity는 `0`이다.
- 각 Host tick은 이전 위치에서 새 위치까지 swept SphereCast를 실행해 빠른 projectile의 tunneling을 막는다.
- 가장 먼저 만난 유효 blocker 또는 Character hit 하나에서 projectile을 종료한다. piercing, ricochet와
  한 projectile의 다중 Character hit는 0이다.
- Character hit만 Damage·Knockback과 승인 Attack/Weapon Hit event를 만들 수 있다. Presentation tracer,
  Client hit 주장과 겹친 collider callback은 추가 hit를 만들지 않는다.
- delayed hit는 shot accept 시점의 immutable attacker/source identity를 사용한다. 현재 Weapon owner나 새로
  Held한 Weapon으로 source를 바꾸지 않는다.
- World/Map blocker에 맞으면 Damage 없이 종료한다. projectile은 Lever·Crane·Hook·panel·Hazard phase,
  prop과 다른 Weapon을 원격 activation하거나 physics impulse로 움직이지 않는다.
- hit, TTL 만료, projectile OOB, RoundResult와 Round reset 중 먼저 성립한 조건에서 제거하고 stale
  Generation projectile이 새 Round hit를 만들지 않는다.
- 새 supply만 SuddenDeath 진입에 취소한다. 이미 Held/Loose인 총기와 SuddenDeath 전에 발사됐거나 그 안에서
  승인된 projectile은 동일 fire·hit·TTL/OOB 규칙을 계속 사용한다.

### 5.3 Authority recoil·visual recoil·spread

- Host는 accepted shot마다 profile 상한 안의 bounded recoil physics를 weapon/holder relation에 한 번 적용한다.
- visual recoil·muzzle pose는 Host shot event를 읽는 Presentation이며 projectile direction, Character Root,
  Weapon authority와 map control을 다시 계산하거나 바꾸지 않는다.
- Pistol은 낮은 기본 spread의 accurate shot과 한 발마다 읽히는 강한 per-shot recoil을 사용한다.
- LongGun은 hold full-auto 동안 shot ordinal 기반 deterministic spread bloom을 누적한다. Client frame rate,
  wall clock과 local random은 spread 결과를 바꾸지 않는다.
- LongGun bloom 증가·상한·release 뒤 decay/reset, 두 총기의 recoil magnitude·recovery와 fire cadence exact 값은
  2·3·4인 Alpha `START` tuning이지만 semi/full-auto·7/30·no-reload 구조를 바꾸지 않는다.
- recoil physics·visual recoil·spread가 Lever·Hazard·prop에 remote impulse를 만들거나 새 shot/Hit Trigger를 합성하는 경로는 0이다.

### Pistol

- M1911-inspired compact low-poly silhouette를 사용하되 실물 slide/control/marking을 복제하지 않는다.
- 한손 사용을 기본으로 검증한다.
- 좌·우 어느 손에서도 muzzle이 Forearm·몸통을 향하지 않는다.
- 공용 Camera에서 작은 silhouette와 fire 방향이 읽힌다.
- valid press edge당 1발, total 7과 no-reload·Spent 전환을 검증한다.
- LongGun보다 accurate하고 shot당 강한 recoil 역할을 유지한다.
- 근거리 spam이 무한 stunlock이나 즉시 ring-out을 만들지 않게 한다.

### LongGun

- AK-47-inspired stock·receiver·barrel·curved-magazine mass를 사용하되 실물 receiver 치수와 각인을 복제하지 않는다.
- Main+Support Grip을 기본 후보로 검증한다.
- Support hand가 Joint limit 안에서 닿고 Sprint·회전 중 팔이 분리되지 않는다.
- 긴 barrel이 벽·Crane·다른 Character를 시각적으로 통과하지 않게 한다.
- held WeaponUse 동안 full-auto, total 30과 deterministic cumulative spread bloom을 검증한다.
- Pistol과 다른 fire cadence·knockback 역할을 가지되 exact 값은 Alpha tuning에서 결정한다.

미승인 tuning:

- Pistol/LongGun fire cadence와 projectile speed·SphereCast radius·TTL
- Pistol recoil magnitude·recovery와 LongGun recoil·spread bloom 증가/상한/decay
- damage·knockback·stun 기여
- `SpentPendingCleanup` 2~4초 중 시작값

이 값은 W1 뒤 2·3·4인 비교로 결정한다.

---

## 6. Bat·Hammer Alpha 전투

W1 뒤 Bat와 Hammer는 다음을 구현한다.

- Host가 승인한 swing 시작·active·recovery 구간
- Render pose와 일치하는 impact volume
- 한 target에 대한 중복 impact 방지
- damage와 bounded knockback
- wall·floor·Hazard와의 충돌 결과
- manual/forced drop과 reacquire
- Round reset에서 swing·impact·owner residue 제거
- 승인 impact마다 `TRG-ATTACK-HIT-CONFIRMED`가 정확히 한 번 발생하고 중복 contact에는 추가 event 0
- 승인 Melee hit마다 `TRG-WEAPON-HIT-CONFIRMED`가 action-victim당 정확히 한 번 발생

### Bat

- generic baseball-bat silhouette를 사용하며 wood/metal surface는 visual brief에서 비교한다.
- 빠르고 읽기 쉬운 한손·양손 swing 후보를 비교한다.
- 좁은 공간에서 벽을 통과하거나 등 뒤 target을 맞히지 않는다.
- edge knockback이 강점일 수 있으나 기본 Strike를 무의미하게 만들지 않는다.

### Hammer

- 긴 handle과 큰 양면 head의 sledgehammer(오함마) silhouette를 사용하며 claw hammer 형태는 사용하지 않는다.
- 무거운 head와 center of mass가 시각·물리적으로 일치한다.
- Bat보다 느리고 강한 역할 후보를 비교한다.
- 큰 knockback이 Crane Pad·RecoveryBand·edge panel에서 즉시 확정 승리를 반복하지 않게 한다.
- impact 전 Telegraph가 공용 Camera에서 읽혀야 한다.

미승인 tuning:

- swing wind-up·active·recovery 시간
- damage·knockback·down 기여
- 한손·양손 차이
- durability, charge 또는 combo 유무

---

## 7. Sprint·DownCount 상호작용

Sprint는 stamina 없이 `Left Shift` hold로 동작하며 Weapon이 캐릭터의 sprint multiplier를 바꾸지 않는다.
W1에서 Sprint 중 fire/swing 허용 범위와 turn/accuracy 영향 방향을 결정한다.

반복 hit로 새 down/Ragdoll에 들어갈 때 Character의 `DownCount`가 증가하고 다음 down/groggy duration이
길어진다. Weapon은 자체 DownCount를 만들지 않고 Character 공통 규칙을 사용한다.

- 같은 impact가 중복 callback으로 DownCount를 두 번 증가시키지 않는다.
- 같은 Round에서 새 down만 누적한다.
- cap 뒤 duration이 더 늘어나지 않는다.
- Round reset에서 DownCount와 Weapon combat residue가 모두 초기화된다.
- 무기 한 종류가 2·3·4인에서 영구 stunlock을 만들면 damage·knockback·swing/fire cadence를 조정한다.

---

## 8. Map·Hazard·attribution

- Weapon impulse는 map physical control을 원격 activation하지 않는다.
- projectile은 authored blocker에서 종료되고 Character hit 외 Damage·Knockback을 만들지 않는다.
- projectile·recoil·swing은 Lever·Crane·Hook·panel·Hazard·prop을 원격 activation하거나 DisplacementHazard를 LethalHazard로 바꾸지 않는다.
- Weapon hit 뒤 OOB에 들어가면 OOB route를 유지하고 attacker attribution만 공통 규칙으로 기록한다.
- Crane final crush와 같은 valid LethalHazard가 같은 tick에 성립하면 map lethal 우선 규칙을 따른다.
- dropped Weapon이 Spawn·RecoveryBand·Lever·moving panel에 영구 끼임을 만들지 않는다.
- Incoming은 map control·Hazard와 상호작용하지 않으며 Loose 전환 뒤에만 일반 Weapon physics를 사용한다.
- DropZone은 Spawn·RecoveryBand·Crane Pad·Hook corridor·Lever·moving panel의 위험 범위와 겹치지 않는다.
- 2·3·4인은 같은 DropZone·shuffle·admission rule과 각자 승인된 timing/cap profile을 사용한다.
- `PATCH-PROT-003..004` knockback/recoil은 Lever·Hook·Crane·panel control input이 아니며 Hazard timing,
  phase와 lethal 조건을 바꾸지 않는다.
- `PATCH-PROT-009..010`은 Weapon supply schedule만 확장하고 Hazard schedule·동시 Weapon 상한을 바꾸지 않는다.
- `PATCH-PROT-011..012` forced drop은 기존 Loose physics를 사용할 뿐 map control을 직접 작동시키지 않는다.
- ammo 0의 `SpentPendingCleanup`은 cap에 포함되지만 map collision·control·Hazard interaction 0이며 Host deadline/reset에서 제거된다.

---

## 9. 2·3·4인 검증

각 Weapon은 다음 matrix를 통과한다.

- 2·3·4인 local/typical/target impairment
- Host owner와 Guest owner
- 왼손·오른손 Main, 가능한 Support
- 기본 이동·Sprint·Jump
- Held·fire/swing·recovery·drop·reacquire
- Pistol press 1발/7발 소진, LongGun hold full-auto/30발 소진과 no reserve/reload
- Host projectile SphereCast blocker/Character hit, TTL·OOB·reset과 no pierce/ricochet
- Pistol recoil/accuracy와 LongGun deterministic cumulative spread bloom
- ammo 0→ForcedRelease→SpentPendingCleanup 2~4초→remove·다음 pulse capacity
- down/Ragdoll·GetUp와 forced drop
- Patch 없는 baseline, `PATCH-PROT-003`, `PATCH-PROT-004` 각각의 valid/invalid attack
- base supply와 `PATCH-PROT-009..010`의 normal·CapacityLimited·Playing 종료 취소
- admission/landing clearance의 `NoSafeDropZone`, `LandingBlocked`와 WeaponCleanupBoundary
- `PATCH-PROT-011..012`의 Held 없음·Main+Support dedupe·source owner 상실·중복 Drop
- 중앙·edge·RecoveryBand·Hazard 근처
- 16:9·16:10·21:9 Min/Max Camera

완료 조건:

- owner, state, hit, damage, knockback와 drop 결과가 Peer 간 수렴
- 중복 fire/impact와 stale Round input 0
- valid Punch·AirKick·Dropkick·Firearm·Melee hit의 `TRG-ATTACK-HIT-CONFIRMED`는 action-victim당 1회, invalid source는 0회
- valid Firearm·Melee hit의 `TRG-WEAPON-HIT-CONFIRMED`는 action-victim당 1회, invalid source는 0회
- Patch가 damage·ownership·rate·hit·drop·supply cap·reset 결과를 우회하는 경우 0
- 인원별 pulse 시각·상한, admitted count, shuffle type·Zone, skip·OOB 보충과 Peer state가 일치
- blocked landing에서 활성 Collider·Damage·Knockback 0, cleanup 뒤 즉시 보충과 Held 부분 진입 오탐 0
- 2·3·4인에서 accepted shot·ammo·projectile·recoil/spread·Spent state와 Host/Guest 결과가 일치
- projectile blocker 뒤 hit 0, Character당 hit 1 이하, Lever·Crane·Hook·Hazard·prop remote impulse 0
- 공용 Camera에서 Weapon 종류·owner·공격 방향 판독
- Character collider·mass·base reach·Camera bounds 불변
- Pistol7·LongGun30, reserve/reload 0이며 infinite ammo·reload control과 미승인 balance를 최종값으로 표시하지 않음
- 2·3·4인 어느 한 구성이 특정 Weapon 때문에 지속적으로 압도되지 않음
- local Match menu·disconnect grace에서 새 Weapon input 0, Character physical·vulnerable 유지와 기존 Weapon·projectile·Spent lifecycle/reconnect 수렴
- explicit Leave/grace 만료 Forfeit가 PatchAuthor·Hit Trigger를 만들지 않고, 1명 잔존은 score·Patch 0 OpponentLeft→Lobby
- Host Leave/Loss의 Weapon state migration 0과 Session 종료 수렴
- Player-facing persistent Match/Ammo HUD 0, Ammo·FireMode·Projectile·Spent는 developer debug에서만 확인

---

## 10. Art·성능 Gate

각 Weapon은 Unity 6.3 LTS·Blender 5.2 LTS 계열을 사용하되 `FDN-010`이 고정한 exact installed patch와
Unity package manifest/lock을 따른다. `.blend/.fbx` source와 큰 binary는 `FDN-011` repository/LFS policy를,
외부 package·font·audio·asset은 `LIC-001` license/NOTICE inventory를 사용한다. `ART-001`이 최초 생산한
versioned `LowPolyStyleProfile`, `ModelInteropProfile`과 `AlphaVisualQAProfile` 없이 asset 양산을 시작하지 않는다.

- front·side·back·three-quarter reference render
- Character와 scale lineup
- Grip·muzzle/impact·center-of-mass overlay
- source→FBX→Unity scale·normal·material·Collider 일치
- Held·Sprint·fire/swing·drop·Ragdoll capture
- Incoming→landing→Loose와 동시 2개 supply capture
- Pistol semi-auto recoil, LongGun full-auto bloom과 ammo0 Spent transition capture
- 2·3·4인 Camera 판독
- projectile/impact VFX가 Hazard Telegraph를 가리지 않음
- 승인 functional archetype과 logo·marking·serial·exact replica 0 검사

WPA-003 source→Unity 비교와 `UG-WEAPON-ART` 사용자 승인이 exact visual Lock을 소유한다. compile,
FBX 생성, W1 입력 승인이나 Grip 기능 성공만으로 Weapon Art를 완료 처리하지 않는다.

위 VFX 항목은 기본 Weapon fire/impact feedback에 대한 기준이다. Patch-specific icon·Animation·VFX·
SFX와 최종 UI Layout은 Alpha 기능 Gate가 아니며 후속 presentation 작업으로 남긴다. 현재 Gate는
plain text Patch 결과와 의미 event port만으로 2·3·4인의 knockback/recoil, supply와 forced drop을 검증한다.

Alpha는 semantic event에 연결된 기본 fire·impact·swing·drop SFX를 제공하고 BGM event·asset은 0이다.
Player-facing 기능 문자열은 Korean-only이며 Weapon name·Ammo HUD를 추가하지 않는다. internal/debug ID는
localization 대상이 아니다. Production Lobby ambience, English/StringTable/font fallback과 music은 post-Alpha다.

성능은 네 Weapon, 4인 Character, 최대 Cosmetic, Hazard, supply cap3와 승인 cadence에서 동시에 존재할 수
있는 projectile·SpentPendingCleanup worst case를 함께 두고 측정한다.

---

## 11. Alpha tuning·미결정

- W1 actual input과 Drop 입력
- 네 functional archetype의 exact authored proportions·palette·bevel과 Pistol/LongGun inspired 변형 정도
- Bat wood/metal surface·authored color 비교와 sledgehammer handle/head material
- Pistol·LongGun fire cadence, damage·knockback과 projectile speed·SphereCast radius·TTL
- Pistol recoil magnitude/recovery와 LongGun deterministic spread bloom 증가·상한·decay/reset
- `SpentPendingCleanup` 2~4초 중 시작값과 cap density 영향
- Bat/Hammer swing timing·combo·charge
- weapon별 damage·knockback·down 기여
- 한손·양손 strength 차이
- 인원별 supply 첫 pulse·주기·동시 상한의 최종 `LOCKED` 값
- map별 safe DropZone 위치·descent/landing 시간과 기능용 최소 arrival 표시
- audio/VFX와 최종 balance
- `PATCH-PROT-003..004`의 Punch·Firearm·Melee 공통 knockback/recoil tuning
- `PATCH-PROT-009..010`의 CapacityLimited 결과와 second-wave `START 6~10초` tuning
- `PATCH-PROT-011..012`의 forced drop 체감과 중복 release 방어
- 후속 Patch-specific icon·Animation·VFX·SFX

이 항목은 W1 사용자 결정과 2·3·4인 Alpha playtest 전에는 `LOCKED`가 아니다.
