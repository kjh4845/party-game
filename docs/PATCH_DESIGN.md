# Project Hotfix 패치 설계

## 0. 문서 정보

| 항목 | 값 |
|---|---|
| 문서 버전 | `0.5.0 Approved Disconnect·Forfeit·Alpha UI Boundary` |
| 기준일 | 2026-08-26 |
| 사용자 노출명 | `패치` |
| 적용 단계 | Prototype Patch 12, Alpha Patch 20 확장 기반 |
| 기준 문서 | `docs/01_PRD.md` 1.8.0, `docs/02_SRS.md` 1.8.0, `docs/03_IMPLEMENTATION_PLAN.md` 2.5 |
| 승인 상태 | `UG-PATCH12-DESIGN` PASSED · Patch12·Ground/Air Action·Firearm Runtime·Disconnect/Forfeit 경계 사용자 승인 2026-08-26 |

이 문서는 승인된 첫 12개 패치와 이를 실행하는 최소 Runtime 계약을 정의한다. Alpha에서는
패치 선택과 적용 여부를 검증하는 평문 Text UI만 사용한다. 최종 UI, Icon, Animation, VFX와 SFX는
이 Runtime과 분리된 Presentation 계층에서 나중에 설계한다.

이 문서의 `START` 범위는 한 번 발동할 때 범위 안에서 무작위 값을 뽑는다는 뜻이 아니다. 구현 전에
각 범위 안에서 하나의 시작값을 Tuning Profile에 기록하고, 2·3·4인 Playtest Evidence를 거쳐 값을
조정한 뒤 별도 승인으로 고정한다.

### 0.1 0.5.0 변경 요약

- Guest의 30초 Disconnect grace 동안 입력만 중립화하고 Character의 물리·피격·OOB·Hazard·Camera 참여를 유지한다.
- 명시적 Leave와 grace timeout의 `Forfeit`를 PatchAuthor 후보에서 제외하고 다음 실제 gameplay
  elimination을 사용하며, 한 명만 남으면 Score·Patch 없이 Lobby로 돌아가게 했다.
- Match의 local menu는 Host Simulation과 Patch deadline을 멈추지 않는 presentation-only surface로 고정했다.
- 지속 Match HUD·Ammo HUD는 Alpha Player surface에서 제거하고 transient Patch UI와 developer-only debug를 분리했다.
- Runtime 기준 Toolchain은 Unity 6.3 LTS, 선택적 Patch presentation asset은 Blender 5.2 LTS를 참조하며
  실제 설치 patch version은 Plan 2.5 `FDN-010` ToolchainProfile에서 고정하게 했다.

### 0.2 0.4.0 변경 요약

- Pistol semi-auto 7발, LongGun full-auto 30발과 reload 0을 승인했다.
- Ammo 0의 Host forced release→`SpentPendingCleanup 2~4초`→remove lifecycle과 supply cap 유지 구간을 고정했다.
- Host-visible Projectile의 fixed-step swept SphereCast, first-hit·no-pierce·no-ricochet, gravity 0 START와 TTL·OOB·reset을 정의했다.
- Host Rigidbody impulse/torque·muzzle physics와 read-only recoil animation을 분리하고 Pistol single recoil,
  LongGun `RecoilAccumulator/SpreadBloom`·deterministic ShotSequence를 고정했다.
- Projectile별 Patch03·04 dedupe와 delayed hit 뒤 source spent/owner loss의 Patch12 `NoEligibleTarget` 경계를 추가했다.

### 0.3 0.3.0 변경 요약

- Ground 손 Punch·Grab과 Airborne foot Kick·Dropkick·hand/ledge Grab의 입력 중재 계약을 승인했다.
- Air episode당 `AirAttackToken=1`, 좌우 `KickAnchor` 권한 sweep과 Dropkick 단일 AttackAction·target dedupe를 고정했다.
- DropkickRecovery의 bounded physics tumble을 DownEpisode와 분리해 DownCount와 Down Trigger를 만들지 않게 했다.
- `TRG-ATTACK-HIT-CONFIRMED` SourceKind에 `Kick`, `Dropkick`을 추가하고 Patch03·04가 같은 권한 Hit에 한 번만 적용되게 했다.
- Host gameplay physics와 Animator/procedural presentation을 분리하고 root-motion authority를 0으로 고정했다.

---

## 1. 제품 규칙

### 1.1 패치가 하는 일

- Match가 끝나지 않은 Round 뒤 지정된 `PatchAuthor`가 Trigger 하나와 Effect 하나를 차례로 고른다.
- PatchAuthor는 OOB 또는 검증된 LethalHazard 같은 실제 gameplay elimination 결과에서만 정한다.
  명시적 Leave와 30초 grace timeout으로 생긴 `Forfeit`는 탈락 순서와 PatchAuthor 후보에서 제외하고,
  Round가 계속되면 그 뒤 처음 발생한 실제 gameplay elimination을 사용한다.
- 완성한 패치는 현재 Round 결과를 바꾸지 않고 다음 Round부터 모든 플레이어에게 같은 규칙으로 적용된다.
- 패치는 특정 플레이어에게만 영구 혜택을 주지 않는다. 누구든 Trigger 조건을 만족하면 같은 Effect를 받는다.
- Active Patch는 최대 3개이며, 네 번째가 활성화될 때 가장 오래된 Patch를 제거한다.
- 패치가 없어도 이동·Punch·Kick·Dropkick·Grab·Weapon·OOB·Hazard만으로 Round가 끝나야 한다.
- Forfeit 뒤 영구 참가자가 2명 이상이면 현재 Round를 계속한다. 한 명만 남으면 Score와 Patch를 만들지
  않고 Lobby로 돌아가며 Patch Offer도 열지 않는다. Host Leave·Loss는 Session 자체를 종료한다.

### 1.2 첫 12개에서 바꾸지 않는 것

첫 12개 Patch가 다음 값을 직접 수정하는 경로는 0개여야 한다.

- Map Hazard의 Timing, Phase, Strength와 Lethal 조건
- Character 또는 Weapon의 Size, Collider, Gameplay Reach와 Camera Bounds
- Character와 Weapon의 기본 Rigidbody Mass
- DownCount, Groggy BaseDuration·Increment·MaxDuration과 남은 Groggy 시간
- Weapon Damage, Ammo, Reload, Fire Cadence와 Melee Damage
- OOB Volume, RecoveryBand, Character Spawn과 Sudden Death 규칙
- Score, PatchAuthor, Round 제한시간과 Match 승리 조건

Patch impulse 때문에 Character가 기존 OOB나 LethalHazard 조건에 들어가는 것은 정상 gameplay다. 이때도
최종 탈락은 기존 Host 권한 OOB·Hazard 판정이 내리며 Patch가 직접 탈락을 선언하지 않는다.

`PATCH-PROT-009..010`은 승인된 Weapon supply의 한 pulse에 들어오는 수와 파생 second wave만 바꾼다.
기본 인원별 cadence·동시 존재 상한, Character Spawn과 Hazard timing은 바꾸지 않는다.
`PATCH-PROT-011..012`는 승인 Hit 뒤 기존 Weapon ownership relation을 강제로 해제할 뿐 Damage,
Knockback, Ammo, Fire·Swing cadence와 새 Hit를 만들지 않는다.

### 1.3 승인 Weapon 반복 투하 기준선

무기는 라운드당 한 개로 제한하지 않는다. 3초 Countdown 뒤 `Playing`에 진입한 Host 시간을 기준으로
다음 `START` profile의 정규 supply pulse를 반복한다.

| 참가자 | 첫 pulse | 이후 주기 | 동시 존재 상한 |
|---:|---:|---:|---:|
| 2인 | `10초` | `22초` | `2` |
| 3인 | `8초` | `16초` | `2` |
| 4인 | `6초` | `12초` | `3` |

- Host는 Round 초기화 시점의 participating roster로 이번 Round의 supply profile을 한 번 선택해
  Sudden Death와 Result까지 고정한다. Disconnect·30초 Reconnect·중도 Forfeit는 현재 Round의 timer·cap을
  재계산하지 않으며 다음 Round 초기화에서만 새 participating roster를 반영한다.
- 상한은 Host가 승인한 `Incoming + Loose + Held + SpentPendingCleanup` Weapon Instance 전체를 센다.
- pulse 시 상한이 가득 차면 투하하지 않고 `CapacityLimited`를 기록한다. pulse를 backlog로 쌓거나
  상한이 비는 즉시 보상 투하하지 않는다.
- OOB·정상 cleanup으로 상한이 비어도 즉시 재투하하지 않고 다음 정규 pulse에서만 보충한다.
- Host는 Round마다 `MatchSeed + Round + WeaponCatalogVersion`으로 Pistol, LongGun, Bat와 Hammer가 한 번씩
  들어 있는 결정적 shuffle bag을 만든다. 실제 admission된 Weapon만 cursor를 소비하고 bag을 모두
  사용하면 결정적인 다음 bag을 만든다.
- Host는 Map이 승인한 safe DropZone 중 하나를 결정적으로 고른다. Character Spawn, OOB edge,
  RecoveryBand, Lethal·DisplacementHazard와 물리 control travel을 침범하는 Zone은 후보가 아니다.
- Admission 시 Host는 각 Zone의 `LandingClearance`가 Character, 다른 Incoming·Loose·Held·SpentPendingCleanup Weapon과
  moving part에 겹치지 않는지 검사한다. 유효 Zone이 없으면 `NoSafeDropZone`으로 0개를 기록하고
  shuffle cursor·backlog·즉시 재시도를 만들지 않는다.
- `Incoming`은 Character Damage·Down·Knockback·Grab, 다른 Weapon 충돌, Lever·Crane·Hook·Panel과 Hazard
  작동 대상이 아니며 Camera subject에도 포함하지 않는다. Host가 착지를 확정한 뒤에만 `Loose`가 된다.
- Landing 순간 clearance를 다시 검사한다. 막혀 있으면 noninteractive `Incoming`을 `START 1~2초`의
  bounded clearance window 안에서만 유지한다. 끝까지 막혀 있으면 `LandingBlocked`로 제거하며 Collider,
  Damage·Knockback, backlog와 즉시 대체 투하를 만들지 않는다. 이미 admission된 Weapon의 bag cursor는
  되돌리지 않는다.
- Map의 Host-authoritative `WeaponCleanupBoundary`는 회수 불가능한 Loose Weapon을 제거한다. Held Weapon은
  긴 Collider 일부가 경계를 넘었다는 이유로 지우지 않고 owner elimination·유효 release 뒤에 평가한다.
  제거로 빈 capacity는 다음 정규 pulse에서만 보충한다.
- Alpha 표시는 낙하지점의 임시 primitive 또는 평문 안내만 사용하며 최종 투하 VFX·SFX를 요구하지 않는다.
- `Playing`이 끝나 Sudden Death 또는 Round Result로 넘어가면 새 pulse와 pending second wave를 취소한다.
  이미 admission된 `Incoming·Loose·Held·SpentPendingCleanup` Weapon은 Round reset까지 기존 lifecycle을
  계속하고 Spent cleanup deadline도 SuddenDeath에서 진행된다.
- Round reset은 pulse timer, shuffle cursor, pending wave, `Incoming·Loose·Held·SpentPendingCleanup` Weapon
  Instance, projectile·owner와 모든 Weapon combat residue를 제거하고 다음 Round profile을 새로 예약한다.
  이전 Round Generation 예약은 실행하지 않는다.

위 시간과 상한은 사용자 승인 `START` 값이며 최종 Lock이 아니다. 2·3·4인에서 주먹·Grab과 Weapon 사용이
함께 나타나는지 측정한 뒤 같은 표 단위로 조정한다.

### 1.4 승인 Firearm·Projectile·Recoil 기준선

| Functional ID | Fire mode | 총 Ammo | Reload |
|---|---|---:|---|
| `Pistol` | semi-auto, 승인 down edge당 최대 한 발 | `7` | 없음 |
| `LongGun` | full-auto, 유효 hold와 cadence 동안 반복 발사 | `30` | 없음 |

- Ammo와 발사 수락은 Host가 판정한다. 유효 Shot 하나가 Ammo를 정확히 1 줄이고 새
  `ShotSequence`와 Projectile `AttackActionId`를 만든다. Guest의 Ammo·Shot·Hit 결과 주장은 0이다.
- Firearm Fire와 Projectile combat은 `Playing`과 `SuddenDeath`에서 유효하다. `RoundResult` 진입은 새
  Fire를 거부하고 active Projectile을 모두 제거하며 다음 Round reset으로 이월하지 않는다. Supply가
  SuddenDeath에서 중단되는 규칙과 Firearm combat 지속 규칙을 혼동하지 않는다.
- Ammo가 0이 되는 Shot을 Host가 수락하면 해당 Weapon은 즉시 기존 Grip/owner relation을 forced
  release하고 `SpentPendingCleanup`으로 전환한다. 이 상태는 Collider·Pickup·fire·swing·Hit·Patch Trigger·map
  control을 만들지 않고 `START 2~4초` 뒤 제거된다.
- `SpentPendingCleanup`은 제거될 때까지 supply cap을 계속 차지한다. 제거가 capacity를 비워도 즉시
  보충하지 않고 다음 정규 pulse만 사용한다.
- Reload action, reserve ammo, magazine 교체와 ammo pickup은 0이다. Round reset은 Ammo, ShotSequence,
  spent timer와 모든 Projectile을 baseline으로 지운다.

각 유효 Firearm Shot은 Host가 소유하는 visible Projectile 하나를 만든다.

- Projectile은 fixed-step마다 이전 위치에서 다음 위치까지 bounded SphereCast sweep을 사용한다. Source
  Weapon과 발사 Actor 자신의 Collider는 승인 collision mask에서 제외한다.
- sweep의 첫 blocking Hit만 처리하고 그 tick에 Projectile을 제거한다. Character를 관통하거나 두
  Target을 맞히지 않으며 pierce·ricochet은 0이다.
- Map geometry와 Hazard/control Collider는 Projectile을 막을 수 있지만 Damage, Lever travel, Hazard
  phase와 map control activation을 만들지 않는다.
- Projectile gravity는 Alpha `START=0`이다. speed, radius와 TTL은 versioned Firearm profile의 tuning 값이다.
- TTL 만료, Projectile OOB, Round reset과 stale RoundGeneration은 Projectile을 제거하고 Hit를 만들지 않는다.
- Guest는 Projectile을 보간·표현할 수 있지만 spawn, path, sweep, first hit, Damage·Knockback과
  `TRG-ATTACK-HIT-CONFIRMED` 결과를 확정하지 않는다.

Recoil은 Host physics와 Presentation을 분리하는 Hybrid다.

- Host는 Shot마다 bounded Character/Weapon Rigidbody impulse·torque와 authority Muzzle 방향을 계산한다.
  Animator·camera cue는 이 semantic recoil state를 read-only로 따른다.
- Pistol은 좁은 base spread와 강한 single-shot recoil을 사용한다.
- LongGun은 연속 수락 Shot마다 bounded `RecoilAccumulator`와 `SpreadBloom`을 누적한다. Shot 방향은
  deterministic `ShotSequence`와 Host profile에서 계산하며 button release 또는 승인 gap 뒤에 회복한다.
- RecoilAccumulator, SpreadBloom, impulse·torque, muzzle deflection과 recovery는 상한을 가지며 Animation,
  Client frame rate와 packet arrival order가 값을 결정하지 않는다.

구현 소유권은 `FIR-001`의 ammo/fire-mode/spent lifecycle, `FIR-002`의 authority Projectile,
`FIR-003`의 recoil/spread, `WPN-005`의 Pistol·LongGun 전투 통합과 `ANP-003`의 read-only Firearm
presentation으로 분리한다.

Patch Runtime의 Toolchain reference는 Unity 6.3 LTS다. Patch 전용 presentation asset을 후속 제작할
때만 Blender 5.2 LTS를 reference로 사용한다. 문서의 LTS family 이름을 실제 설치 patch number로
간주하지 않으며, 정확한 Unity·Blender patch와 package lock은 Plan 2.5 `FDN-010` ToolchainProfile이 소유한다.

---

## 2. 공통 용어와 최소 Data 계약

### 2.1 Trigger 의미

Trigger는 raw Input이나 Client 주장이 아니라 AuthorityHost가 이미 유효하다고 확정한 gameplay 의미
Event다.

| Trigger ID | 발생 조건 | Actor | Target | 제외 |
|---|---|---|---|---|
| `TRG-JUMP-ACCEPTED` | Host가 Ground/Coyote/Buffer 규칙을 통과한 Jump를 수락한 순간 | 점프한 Character | Self 또는 Effect가 고른 주변 Character | 거부된 Jump, Client 예측 Jump, Presentation Animation |
| `TRG-ATTACK-HIT-CONFIRMED` | Host가 하나의 Attack과 Character Target의 유효 Hit를 확정한 순간 | 공격자 | 맞은 Character | Hazard contact, Patch impulse, prop 충돌, Client Hit 주장 |
| `TRG-PLAYER-GRAB-ESTABLISHED` | Host가 Character 대 Character Grab 관계를 실제 생성한 순간 | Grabber | 잡힌 Character | prop·Weapon·Map control Grab, GrabSeek, 실패·예측 Grab |
| `TRG-DOWN-EPISODE-START` | Match에서 Alive Character의 새 비치명 권한 Down/Ragdoll Episode가 시작된 순간 | Down된 Character | Self 또는 Effect가 고른 주변 Character | Lobby Ragdoll, 같은 Episode의 반복 Contact, Presentation Ragdoll, 같은 Tick에 확정된 OOB·Lethal elimination |
| `TRG-WEAPON-SUPPLY-SCHEDULED` | `Playing`의 유효한 정규 supply pulse를 Host가 연 순간 | AuthorityWorld | 현재 Supply Transaction | cap이 빈 순간, backlog, Patch 파생 wave, Sudden Death, 이전 Round 예약 |
| `TRG-WEAPON-HIT-CONFIRMED` | Host가 Firearm 또는 Melee Source Weapon과 Character Target의 유효 Hit를 확정한 순간 | 공격자 | 맞은 Character와 Source Weapon | Punch·Kick·Dropkick, loose Weapon 충돌, 단순 Fire·Swing request, Client Hit 주장, Patch 파생 충돌 |

`TRG-ATTACK-HIT-CONFIRMED`의 초기 SourceKind는 `Punch`, `Kick`, `Dropkick`이다. W1 승인과 실제 무기 전투 구현 뒤
`Firearm`과 `Melee`를 같은 Trigger에 추가한다. Trigger 이름이나 Patch ID를 공격 종류마다 복제하지 않는다.
같은 유효 Weapon Hit는 일반 공격 규칙용 `TRG-ATTACK-HIT-CONFIRMED`와 Weapon 관계용
`TRG-WEAPON-HIT-CONFIRMED`를 같은 Root Attack identity로 만들 수 있다. 후자는 유효 Source Weapon
Instance를 반드시 포함하며 Punch·Kick·Dropkick에는 발생하지 않는다.

Firearm Projectile은 Spawn 시 immutable attacker·Source Weapon·ShotSequence·AttackAction identity를
보존한다. 지연 Hit 전에 Source Weapon이 Ammo 0으로 `SpentPendingCleanup` 또는 remove됐거나 owner가
바뀌어도 Host가 확정한 Projectile Hit는 `TRG-ATTACK-HIT-CONFIRMED`와 `TRG-WEAPON-HIT-CONFIRMED`를
만들 수 있다. 다만 Patch12는 Hit 시점의 live held relation을 다시 확인하므로 source가 더 이상 같은
attacker에게 Held가 아니면 `NoEligibleTarget`이다.

### 2.2 Ground·Airborne 손 입력과 AttackAction

LMB/RMB는 Ground와 Airborne에서 같은 물리 손 입력을 사용하지만, AuthorityHost가 현재 Motion State와
입력 시간으로 다음 의미 Action을 하나만 확정한다.

| Context | 입력 | 권한 결과 |
|---|---|---|
| Grounded | L 또는 R을 `GrabHoldThreshold` 전에 release | 해당 손 `Punch` 한 번 |
| Grounded | L 또는 R을 threshold까지 hold | 해당 손 `GrabSeek`; Punch 0 |
| Airborne·non-Down | L 또는 R quick tap | 해당 쪽 발 `Kick` 한 번 |
| Airborne·non-Down | L·R down edge가 `DualClickChordWindow` 안에 들어옴 | 양발 `Dropkick` 한 번 |
| Airborne·non-Down | 한 손을 Grab threshold까지 hold | 해당 손 `GrabSeek`·ledge Grab; Kick 0 |

- `DualClickChordWindow` 비교 후보는 `60/80/100ms`, `START=80ms`다.
- 각 press sequence의 Ground/Air context는 Host가 해당 down edge 시점에 고정한다. Ground에서 시작한
  press가 takeoff 뒤 Kick으로 바뀌지 않으며, Dropkick은 두 down edge가 모두 Airborne·non-Down일 때만 성립한다.
- Air quick release는 첫 down edge의 chord window가 닫힐 때까지 Kick 후보로만 보존한다. 그 안에 반대쪽
  down edge가 들어오면 그 edge에서 Dropkick을 즉시 commit하고 두 Kick 후보를 소비한다.
- Grab threshold를 넘긴 손은 아직 commit되지 않은 single Kick 후보만 취소하고 GrabSeek로 확정한다.
  이미 commit된 Dropkick은 뒤이은 hold로 rollback하거나 Grab으로 바꾸지 않는다.
- Airborne이며 DownEpisode가 아닌 구간은 `AirAttackToken=1`을 가진다. 첫 유효 Kick 또는 Dropkick이
  token을 소비하고, 이후 tap은 Grounded·GetUp·Round reset 중 하나가 token을 복원할 때까지 공격 0이다.
  token이 없어도 유효 hold Grab·ledge Grab은 계속 가능하다.
- `KickAnchor_L/R`의 Host sweep만 Kick contact를 만들고, Dropkick은 두 Anchor가 하나의
  `AttackActionId`를 공유한다. 같은 Action·Target은 한 번만 Hit하며 양발 contact를 두 Hit로 세지 않는다.
- Dropkick은 Host가 bounded forward impulse와 reduced steering을 적용하고 기본 Kick보다 강한 승인
  knockback을 사용한다. 종료 뒤 bounded `DropkickRecovery`와 physics tumble을 거치지만 이는
  DownEpisode·DownCount·`TRG-DOWN-EPISODE-START`를 만들지 않는다. Grounded로 token이 복원돼도
  DropkickRecovery가 끝나기 전에는 새 Attack을 시작하지 않는다.
- Host가 action phase, Rigidbody·joint target, sweep, Hit와 impulse를 판정한다. Animator와 procedural
  pose는 semantic phase를 따라갈 뿐 PhysicsRoot·Collider·Anchor·Hit를 움직이거나 확정하지 않는다.
  gameplay root-motion authority와 Animation Event authority는 각각 0이다.
- W1 WeaponUse binding은 승인된 Air L/R quick-tap Kick과 L+R chord Dropkick을 덮어쓰지 않는다.
  Airborne WeaponUse 허용 여부, 별도 입력과 action 우선순위는 `UG-W1`에서 결정하며 그 전에는 임의로
  Air Kick/Dropkick을 Weapon action으로 재해석하지 않는다.

구현 소유권은 `AIR-001`의 Ground/Air tap-hold·chord resolver, `AIR-002`의 권한 Kick/Dropkick
physics·Hit·recovery, `ANP-001..003`의 presentation matrix·prototype·network/Ragdoll 통합으로 분리한다.

### 2.3 Patch Definition

한 Patch Definition은 다음 정보만 가진다.

- 안정적인 `PatchId`, `TriggerId`, `EffectId`
- 사용자에게 보여줄 한국어 문장과 이후 Localization에 사용할 Text Key
- Actor·Target 선택 규칙
- Versioned Tuning Profile과 `START` 범위
- Effect lifetime과 Modifier channel
- 호환·충돌 Tag와 제외 Domain
- 이후 Presentation이 사용할 수 있는 선택적 Cue Key

Protocol byte layout, hash preimage, 부동소수점 연산 순서와 직렬화 구현은 이 문서에서 정하지 않는다.

### 2.4 Runtime Instance

- 같은 Definition이 나중에 다시 선택될 수 있으므로 활성화할 때마다 새 `PatchInstanceId`를 만든다.
- Instance는 `PatchId`, 활성 Round와 FIFO 순서를 가진다.
- 동일 Definition의 outgoing Instance가 제거되고 새 Instance가 들어오면 두 Instance를 같은 것으로 취급하지 않는다.
- 모든 Peer가 자체적으로 Instance를 만들지 않고 Host가 보낸 권한 결과를 사용한다.

---

## 3. 첫 12개 Patch Catalog

### 3.1 한눈에 보는 사용자 문장

| Patch ID | Trigger | Effect ID | Alpha Text |
|---|---|---|---|
| `PATCH-PROT-001` | `TRG-JUMP-ACCEPTED` | `EFF-JUMP-HIGHER` | `점프하면 더 높이 뜹니다.` |
| `PATCH-PROT-002` | `TRG-JUMP-ACCEPTED` | `EFF-JUMP-PULSE` | `점프하면 주변의 다른 플레이어를 밀어냅니다.` |
| `PATCH-PROT-003` | `TRG-ATTACK-HIT-CONFIRMED` | `EFF-HIT-KNOCKBACK` | `공격을 맞히면 맞은 플레이어가 더 멀리 밀려납니다.` |
| `PATCH-PROT-004` | `TRG-ATTACK-HIT-CONFIRMED` | `EFF-ATTACKER-RECOIL` | `공격을 맞히면 공격한 플레이어도 뒤로 밀려납니다.` |
| `PATCH-PROT-005` | `TRG-PLAYER-GRAB-ESTABLISHED` | `EFF-THROW-RESISTANCE-LOW` | `플레이어를 잡으면 잡힌 플레이어를 잠시 더 쉽게 들어 던질 수 있습니다.` |
| `PATCH-PROT-006` | `TRG-PLAYER-GRAB-ESTABLISHED` | `EFF-GRIP-STRONGER` | `플레이어를 잡으면 현재 잡기가 잠시 더 강해집니다.` |
| `PATCH-PROT-007` | `TRG-DOWN-EPISODE-START` | `EFF-RAGDOLL-SLIDE` | `다운되면 바닥에서 더 멀리 미끄러집니다.` |
| `PATCH-PROT-008` | `TRG-DOWN-EPISODE-START` | `EFF-RAGDOLL-BOUNCE` | `다운되면 몸이 한 번 튀어 오릅니다.` |
| `PATCH-PROT-009` | `TRG-WEAPON-SUPPLY-SCHEDULED` | `EFF-WEAPON-SUPPLY-DOUBLE` | `보급 시간이 되면 무기 두 개가 동시에 떨어집니다.` |
| `PATCH-PROT-010` | `TRG-WEAPON-SUPPLY-SCHEDULED` | `EFF-WEAPON-SUPPLY-SECOND-WAVE` | `보급 시간이 되면 잠시 뒤 무기가 한 번 더 떨어집니다.` |
| `PATCH-PROT-011` | `TRG-WEAPON-HIT-CONFIRMED` | `EFF-VICTIM-HELD-WEAPON-FORCED-DROP` | `무기로 공격을 맞히면 맞은 플레이어가 들고 있던 무기를 놓칩니다.` |
| `PATCH-PROT-012` | `TRG-WEAPON-HIT-CONFIRMED` | `EFF-ATTACKER-SOURCE-WEAPON-FORCED-DROP` | `무기로 공격을 맞히면 공격한 플레이어도 사용한 무기를 놓칩니다.` |

문장은 Effect 결과를 숨기지 않고 한 문장으로 설명한다. Alpha Text Adapter는 위 문장을 그대로
표시하며 최종 Copywriting이나 Icon을 전제로 줄이거나 암호화하지 않는다.

첫 12개는 같은 Trigger를 공유하는 두 Patch를 상호 배타로 취급한다. 예를 들어
`PATCH-PROT-001`과 `PATCH-PROT-002`은 동시에 Active가 될 수 없다. retained active set에는
Trigger당 Patch Instance가 최대 하나이며, 서로 다른 Trigger의 Patch끼리는 함께 활성화할 수 있다.
이 `TriggerOccupancy` 규칙이 있어야 남은 Trigger마다 Effect 두 개를 그대로 제시할 수 있다.
새 충돌 규칙을 추가하려면 2×2 closure를 다시 검증해야 한다.

### 3.2 `PATCH-PROT-001`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-JUMP-ACCEPTED` Actor / Self |
| Effect | Host가 수락한 현재 Jump의 수직 Takeoff impulse만 높인다. 새 Jump를 만들지 않는다. |
| Lifetime | 현재 Jump takeoff 1회 |
| `START` 범위 | 승인 기본 Jump impulse의 `1.15x~1.30x` |
| Modifier channel | `AcceptedJumpVerticalImpulse` |
| 제외 Domain | Air Jump 추가, Jump count, Coyote·Buffer window, ClimbAssist impulse, Horizontal speed, Collider, Base mass |

이미 수락된 Jump 하나만 보정하므로 Patch 자체가 `TRG-JUMP-ACCEPTED`를 다시 만들지 않는다. 높은 Jump가
RecoveryBand를 완전히 무효화하거나 P00 상부 구조를 건너뛰는 값은 승인하지 않는다.

### 3.3 `PATCH-PROT-002`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-JUMP-ACCEPTED` Actor / 반경 안의 다른 Alive Character |
| Effect | Takeoff 위치에서 바깥 방향의 Character-only impulse를 한 번 적용한다. Actor는 제외한다. |
| Lifetime | 즉시 1회 |
| `START` 범위 | 반경 `1.0~1.75 CharacterUnit`, 세기 기본 Punch knockback의 `0.35x~0.65x` |
| Modifier channel | 없음, 권한 Character impulse Event |
| 제외 Domain | Damage, direct Down, prop·Weapon impulse, Lever·Crane·Hook·Panel activation, Hazard strength |

대상이 반경에 없으면 `NoEligibleTarget`으로 기록한다. 이는 숨은 No-op Definition이 아니라 발동 시점에
유효 대상이 없었던 결과다. 실제 impulse가 기존 Down 조건을 만들면 Host가 새 Down Episode를 한 번
생성할 수 있다.

### 3.4 `PATCH-PROT-003`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-ATTACK-HIT-CONFIRMED` 공격자 / 맞은 Character |
| Effect | 유효 Hit의 Character knockback만 높이고 기존 Damage 결과는 그대로 둔다. |
| Lifetime | Attack·Target pair당 즉시 1회 |
| `START` 범위 | 승인 Attack knockback의 `1.20x~1.45x` |
| Modifier channel | `ConfirmedHitKnockback` |
| 제외 Domain | Weapon Damage, Attack cadence, 중복 Hit 생성, Hazard·prop·Patch impulse, direct elimination |

같은 Attack이 같은 Target에 여러 Contact를 내도 Host의 기존 Hit 중복 방지가 하나의
`TRG-ATTACK-HIT-CONFIRMED`만 만든다. Punch·Kick·Dropkick, W1 이후 Firearm·Melee가 같은 보정 순서를
사용한다. Dropkick의 좌우 KickAnchor는 한 AttackAction이므로 같은 Target에 Patch03을 한 번만 적용한다.
Firearm은 Projectile 하나가 별도 AttackAction이며 swept first Hit의 Target에 Patch03을 최대 한 번만
적용한다. LongGun 연속 Shot은 서로 다른 ShotSequence이지만 cadence·Projectile·최종 knockback 상한을 우회하지 않는다.

### 3.5 `PATCH-PROT-004`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-ATTACK-HIT-CONFIRMED` 공격자 / Actor Self |
| Effect | 공격이 향한 반대쪽으로 공격자에게 Character-only recoil impulse를 한 번 적용한다. |
| Lifetime | 유효 Attack action당 즉시 1회 |
| `START` 범위 | 해당 Attack 기본 knockback의 `0.20x~0.40x` |
| Modifier channel | 없음, 권한 Character impulse Event |
| 제외 Domain | Target 추가 Damage, Weapon recoil stat 변경, prop·Map control impulse, Camera shake 강제 |

한 Attack이 여러 Target에 닿더라도 Recoil은 Attack action당 최대 한 번이다. 실제 Recoil이 기존 Down이나
OOB 조건을 만들 수는 있지만 Patch가 Down·탈락을 직접 선언하지 않는다. Kick·Dropkick도 Patch04를
AttackAction당 한 번만 적용하며 DropkickRecovery를 DownEpisode로 바꾸지 않는다.
Firearm은 Projectile AttackAction마다 Patch04를 최대 한 번 적용한다. LongGun full-auto의 반복 Hit도
Shot별 dedupe와 bounded final impulse를 사용하고 base `RecoilAccumulator/SpreadBloom`을 Patch가 직접 바꾸지 않는다.

### 3.6 `PATCH-PROT-005`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-PLAYER-GRAB-ESTABLISHED` Grabber / 잡힌 Character |
| Effect | 현재 Grab 관계에서 Target의 lift·throw resistance만 낮춘다. 실제 Rigidbody mass는 바꾸지 않는다. |
| Lifetime | 해당 Grab 종료 또는 `2~4초` 중 먼저 도달한 시점 |
| `START` 범위 | 승인 lift·throw resistance의 `0.60x~0.80x` |
| Modifier channel | `GrabTargetLiftThrowResistance` |
| 제외 Domain | Base mass, Gravity, Collider, Weapon·prop weight, 다른 Grab 관계, 영구 Carry |

Modifier는 `GrabId`에 묶는다. Host expiry, Release, Break, Target elimination, Round reset 중 하나가
발생하면 즉시 제거한다. 같은 Grab에 중복 Contact가 들어와도 세기가 누적되지 않는다.

### 3.7 `PATCH-PROT-006`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-PLAYER-GRAB-ESTABLISHED` Grabber / 현재 Grab 관계 |
| Effect | 현재 Grab의 break resistance만 높인다. 손 reach나 Target mass는 바꾸지 않는다. |
| Lifetime | 해당 Grab 종료 또는 `2~4초` 중 먼저 도달한 시점 |
| `START` 범위 | 승인 Grab break resistance의 `1.20x~1.50x` |
| Modifier channel | `GrabBreakResistance` |
| 제외 Domain | Hand reach, GrabSeek radius, Base grip permanent stat, 무한 Grab, Weapon·Map control Grab |

양손 Grab은 기존 양손 합성 규칙을 먼저 계산하고 Patch 보정을 한 번 적용한다. Patch가 한 손을 두 손으로
취급하거나 끊어진 Grab을 되살리지 않는다.

### 3.8 `PATCH-PROT-007`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-DOWN-EPISODE-START` Down Character / Self |
| Effect | 현재 Ragdoll Episode 동안 Character의 slide resistance를 낮춰 기존 힘에 더 멀리 미끄러지게 한다. |
| Lifetime | GetUp, elimination 또는 Round reset까지 |
| `START` 범위 | 승인 Ragdoll slide resistance의 `0.45x~0.70x` |
| Modifier channel | `RagdollSlideResistance` |
| 제외 Domain | Groggy time, DownCount, Map surface material, standing locomotion friction, self propulsion |

같은 Down Episode의 반복 Contact는 Modifier를 추가하지 않는다. Lobby Ragdoll은 Trigger 자체를 만들지
않으므로 이 Patch를 발동하지 않는다.

### 3.9 `PATCH-PROT-008`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-DOWN-EPISODE-START` Down Character / Self |
| Effect | Down 시작 시 위쪽으로 제한된 Character-only impulse를 한 번 적용한다. |
| Lifetime | Down Episode 시작 순간 1회 |
| `START` 범위 | 승인 기본 Jump impulse의 `0.20x~0.40x` |
| Modifier channel | 없음, 권한 Character impulse Event |
| 제외 Domain | Groggy time, DownCount 추가, GetUp 시작, Air control, Damage, Hazard·Map control impulse |

Bounce 뒤 Contact가 이어져도 같은 Down Episode에서는 다시 발동하지 않는다. Character가 실제로
GetUp을 마친 뒤 별개의 유효 충격으로 새 Down Episode가 시작된 경우에만 다시 발동한다.

### 3.10 `PATCH-PROT-009`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-WEAPON-SUPPLY-SCHEDULED` AuthorityWorld / 현재 Supply Transaction |
| Effect | 정규 pulse의 desired batch를 1개에서 별도 Weapon Instance 두 개로 바꾼다. |
| Lifetime | 현재 정규 pulse 1회 |
| `START` 범위 | 같은 Host pulse에서 추가 `1개`, 총 desired `2개` |
| Modifier channel | 없음, 권한 Supply Transaction admission |
| 제외 Domain | 기본 cadence·cap 증가, backlog, player 수에 따른 추가 배율, Character·Hazard 충돌, 파생 Trigger |

Host는 남은 capacity와 동적으로 유효한 Arrival Slot 수만 admission한다. 두 칸·두 Slot 이상이면 두
Weapon을 동시에 보낸다. capacity가 한 칸이면 한 개와 `CapacityLimited`, 유효 Slot이 부족하면 실제
수량과 `NoSafeDropZone`을 기록한다. 미생성분을 예약하거나 shuffle cursor를 소비하지 않으며 Patch가
기본 cap을 높이지 않는다.

### 3.11 `PATCH-PROT-010`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-WEAPON-SUPPLY-SCHEDULED` AuthorityWorld / 현재 Supply Transaction |
| Effect | 정규 pulse 뒤 같은 Round에 Weapon 한 개를 위한 Patch 파생 second wave를 한 번 예약한다. |
| Lifetime | 파생 wave 실행, `Playing` 종료, Patch retire 또는 Round reset 중 먼저 도달한 시점 |
| `START` 범위 | 정규 pulse 뒤 Host 시간 `6~10초` |
| Modifier channel | 없음, RoundGeneration에 묶인 권한 delayed supply Event |
| 제외 Domain | 기본 pulse timer 재시작, 반복 예약, cap 증가, Sudden Death admission, 다음 Round 이월 |

second wave는 실행 시점의 capacity를 다시 검사해 한 칸이 있을 때만 다음 shuffle Weapon을 admission한다.
가득 차면 `CapacityLimited`로 끝내며 재시도하지 않는다. 이 Event의 Origin은 `PatchDerivedSupply`라서
`TRG-WEAPON-SUPPLY-SCHEDULED`를 다시 만들지 않고 같은 Patch를 연쇄 예약하지 않는다.

### 3.12 `PATCH-PROT-011`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-WEAPON-HIT-CONFIRMED` 공격자 / 맞은 Character가 Hit 확정 시점에 Held한 Weapon Instance 전체 |
| Effect | Target의 유효 Weapon Grip relation을 기존 Authority forced-release 경로로 해제한다. |
| Lifetime | Attack·Target pair당 즉시 1회 |
| `START` 범위 | Held Weapon Instance별 forced release `1회` |
| Modifier channel | 없음, 기존 Weapon ownership release Event |
| 제외 Domain | 추가 Damage·Knockback·Down, Weapon 파괴, 임의 drop impulse, Source Weapon 강제 해제, 새 Hit |

Main+Support로 같은 Weapon을 잡았으면 하나의 Instance로 처리하되 그 Weapon의 Target grip은 모두 해제한다.
Target이 서로 다른 Weapon을 실제로 들고 있으면 각 Instance를 한 번씩 해제한다. 들고 있는 Weapon이 없으면
`NoEligibleTarget`이며 loose Weapon, Grab 후보와 이미 owner가 바뀐 Weapon은 건드리지 않는다.

### 3.13 `PATCH-PROT-012`

| 항목 | 계약 |
|---|---|
| Actor / Target | `TRG-WEAPON-HIT-CONFIRMED` 공격자 / 해당 Hit의 Source Weapon Instance |
| Effect | Source Weapon이 여전히 같은 공격자에게 Held라면 그 Weapon의 Grip relation을 forced release한다. |
| Lifetime | 유효 Attack action당 즉시 최대 1회 |
| `START` 범위 | Source Weapon forced release `1회` |
| Modifier channel | 없음, 기존 Weapon ownership release Event |
| 제외 Domain | 현재 다른 Weapon 해제, 새 owner의 relation 해제, Damage·Ammo·cadence 변경, 추가 drop impulse, 새 Hit |

한 Attack이 여러 Target을 맞혀도 Source Weapon은 action당 최대 한 번만 해제한다. 지연 Projectile이 맞기 전에
공격자가 이미 Weapon을 놓았거나, Ammo 0으로 Spent/remove됐거나, 다른 Player가 소유했다면
`NoEligibleTarget`으로 끝내고 현재 owner의 Weapon이나 공격자가 나중에 든 다른 Weapon을 대신 떨어뜨리지 않는다.

---

## 4. Host Authority와 Event 실행

### 4.1 권한 경계

- Host만 Trigger Event를 확정하고 Effect를 실행한다.
- Guest는 Position, Hit, Grab, Down, Effect 결과와 Patch activation을 권한 주장으로 보낼 수 없다.
- Guest가 보낼 수 있는 Patch gameplay command는 열린 Offer의 Trigger 또는 Effect Candidate 선택뿐이다.
- Host local 선택도 Guest와 같은 Offer, Author, Phase, Candidate와 Deadline 검증을 통과한다.
- Client는 Patch Effect를 로컬 물리에 재실행하지 않고 Host snapshot·event를 표현한다.
- Match local menu는 해당 Client의 presentation과 local command routing만 바꾼다. Menu를 열어도
  Host Simulation, Round clock, Patch Offer deadline과 다른 Player action은 멈추지 않으며 time scale,
  invulnerability, Patch commit·target·Effect를 바꾸는 authority는 0이다.

### 4.2 Root Event와 재진입 방어

Patch가 소비하거나 만드는 Event는 최소한 다음 의미를 보존한다.

- Root Event identity
- 현재 Round Generation
- Base gameplay 또는 Patch effect라는 Origin
- Actor, Target과 SourceKind
- 현재 Patch chain depth

같은 Root Event에서 동일한 `PatchInstance + affected Entity` 조합은 한 번만 실행한다. Patch 실행은
오래된 Active Instance 순서로 처리하고 여러 대상은 안정적인 Player 순서로 처리한다. Round Generation이
다른 Event와 이미 처리한 Event는 거부한다.

Runtime은 유한한 Patch chain depth와 처리 budget을 가진다. 정확한 내부 숫자와 자료구조는 구현
명세에 두되 첫 12개 정상 흐름이 budget에 닿아서는 안 된다. Guard가 작동하면 해당 Effect만 억제하고
Host 진단에 `Guarded` 결과를 남긴다.

Patch가 만든 물리 impulse는 기존 Host 물리 결과를 거쳐 자연스러운 새 Down Episode를 한 번 만들 수
있다. 그러나 Patch는 raw Input이나 `TRG-JUMP-ACCEPTED`, `TRG-ATTACK-HIT-CONFIRMED`,
`TRG-PLAYER-GRAB-ESTABLISHED`를 조작해 발동 횟수를 늘릴 수 없다. `TRG-DOWN-EPISODE-START`도 실제 GetUp 전
반복 Contact로 다시 만들 수 없다.

Patch가 만든 second wave, Incoming landing, forced release, OOB cleanup과 Round reset은
`TRG-WEAPON-SUPPLY-SCHEDULED` 또는 `TRG-WEAPON-HIT-CONFIRMED`를 만들지 않는다. Supply Trigger는 정규
Host pulse에만, Weapon Hit Trigger는 유효 Attack의 Source Weapon·Target pair에만 묶는다.

Host는 같은 Tick의 OOB·Lethal elimination을 Down Patch Trigger보다 먼저 확정한다. 해당 Tick에
Eliminated가 된 Character는 `TRG-DOWN-EPISODE-START`의 Actor가 아니며 Slide·Bounce를 적용하지 않는다.

---

## 5. 2×2 Candidate와 7초 선택

### 5.1 Offer 생성

Match가 끝나지 않은 Round Result가 확정되면 Host는 다음 순서로 Offer를 만든다.

1. Host는 elimination order에서 명시적 Leave와 reconnect grace timeout의 `Forfeit`를 제거한다.
2. 2인에서는 실제 gameplay로 패배한 Round loser, 3·4인에서는 최초 실제 gameplay elimination을
   Author로 정한다. Forfeit 뒤 Round가 계속되면 그 다음 실제 elimination이 최초 후보가 된다.
3. 최초 실제 elimination이 같은 Authority Tick이면 Match seed, Round와 안정적인 Player identity를 사용하는
   결정적 Tie-break로 한 명을 정한다.
4. 다음 Round에 존재할 projected Active Set을 계산한다.
5. projected set이 이미 점유한 Trigger 전체와 exact duplicate, 명시적 conflict, 지원하지 않는
   Domain과 재귀 위험 조합을 제외한다.
6. 유효 Effect가 2개 이상 남은 서로 다른 Trigger 중 결정적 순서로 2개를 고른다.
7. 각 Trigger에 대해 유효 Effect 2개를 고정하고 전체 Branch를 하나의 Offer로 동결한다.

Forfeit로 영구 참가자가 한 명만 남아 Score·Patch 없이 Lobby로 돌아가는 경로에는 Round Result용
Patch Offer를 만들지 않는다. Disconnect grace 중 Character가 유효 OOB·LethalHazard로 탈락한 것은
Forfeit가 아니라 실제 gameplay elimination이므로 위 Author 규칙에 포함한다.

Candidate 순서는 versioned deterministic random service를 사용한다. Match seed, 완료 Round, Patch
ordinal과 Catalog version은 사용할 수 있지만 벽시계, packet 도착 순서, Scene object 발견 순서와
Client collection 순서는 사용하지 않는다. Client는 Candidate를 재계산하지 않는다.

### 5.2 Active 3개일 때의 projected set

첫 Catalog는 6 Trigger×2 Effect로 정확히 12개다. Candidate filter는 다음 Round에 실제로 유지될
projected Active Set을 기준으로 한다.

- Active가 0~2개면 현재 Active Set이 projected set이다.
- Active가 3개면 FIFO로 나갈 oldest Instance를 제외한 두 Instance가 projected set이다.
- projected set의 각 Active Instance는 자신의 Trigger를 점유하며 그 Trigger의 두 Effect Branch를 모두 후보에서 닫는다.
- outgoing oldest와 같은 Definition은 후보로 다시 나올 수 있다.
- outgoing oldest가 점유했던 Trigger는 다시 열리므로 같은 Definition 또는 같은 Trigger의 다른 Effect를 선택할 수 있다.
- 다시 선택하면 기존 oldest가 유지되는 것이 아니라 제거되고 새 Instance가 newest로 들어간다.
- 다음 Round Active Set에는 exact duplicate와 중복 Trigger가 존재하지 않는다.

이 규칙으로 첫 12개만 사용하면 Active 0/1/2에서는 각각 6/5/4개의 비점유 Trigger가 남고,
Active 3에서는 outgoing oldest를 제외한 뒤 4개의 비점유 Trigger가 남는다. 따라서 어떤 정상 상태에서도
Trigger 2개와 선택 Trigger의 Effect 2개를 제공할 수 있다. Catalog 시작 검사와 자동 Test가 이 closure를
증명해야 한다.

### 5.3 선택과 Timeout

- 총 선택시간은 Offer가 열린 Host 시간 기준 7초다.
- Author는 Trigger 2개 중 하나를 고른 뒤 그 Branch의 Effect 2개 중 하나를 고른다.
- Trigger 선택 뒤 Timer를 다시 7초로 시작하지 않는다.
- Host는 Author, Offer identity, 현재 Step, 동결 Candidate membership과 Deadline을 다시 검증한다.
- 중복·역순·만료·다른 Player의 선택은 상태를 바꾸지 않는다.
- Trigger도 선택하지 못하면 동결된 첫 Trigger와 그 Branch의 첫 Effect를 적용한다.
- Trigger만 선택하고 Effect를 선택하지 못하면 해당 Branch의 첫 Effect를 적용한다.
- 실제 gameplay elimination으로 이미 Author가 된 Player의 network disconnect도 같은 동결 Offer,
  Deadline과 자동 선택을 사용하며 connection 상태만으로 Author를 다른 Player에게 넘기지 않는다.
  단, Forfeit event 자체를 근거로 새 Author나 Offer를 만들지는 않는다.
- Match Winner가 이미 확정된 경우 Offer를 만들지 않는다.

Catalog가 2×2를 만들 수 없는 상태는 안전한 No-op Patch로 숨기지 않는다. Session 시작 전 Catalog
검증에서 차단하고, Runtime 불변식이 깨지면 명시적 Content Error와 진단을 남긴다.

---

## 6. Commit, 다음 Round 활성화와 FIFO

```text
Round Result 확정
→ PatchOfferOpened
→ Trigger 선택
→ Effect 선택 또는 Timeout
→ PatchCommitted (Pending 1개)
→ Round transient reset
→ Active가 3개면 oldest Retired
→ Pending Patch Activated as newest
→ Active Patch 등록
→ 3초 Countdown
→ Playing
```

- Commit된 Patch는 Patch 선택 화면에서 gameplay Effect를 발동하지 않는다.
- 다음 Round 시작 Transaction에서만 Active가 된다.
- Active Patch order는 `oldest → newest`이며 최대 3개다.
- 네 번째를 활성화할 때 oldest retire와 newest activation은 하나의 Host 상태 전이로 처리한다.
- Patch History는 Match Result 확인용으로 유지하되 retired Patch는 Trigger를 받지 않는다.
- 같은 Match의 Score, Match seed, 선택 Map, Patch History와 Active Patch는 Round reset을 통과한다.
- 새 Match를 시작하거나 Lobby로 돌아가면 Active, Pending, Offer, Modifier와 Match Patch History를 비운다.

---

## 7. Modifier 합성·Refresh와 제거

### 7.1 공통 규칙

- Modifier identity는 최소한 `PatchInstance, Target, Channel, Scope`를 구분한다.
- 같은 Instance가 같은 Target·Channel·Scope에 다시 적용되면 magnitude를 더하지 않고 값을 교체하며
  허용된 lifetime만 refresh한다.
- `PATCH-PROT-001..008`은 서로 다른 배타 Modifier channel을 사용하고 `009..012`는 bounded Authority
  Event만 사용한다. 임의의 generic stat stack system을 만들지 않는다.
- 향후 서로 다른 Patch가 같은 Channel을 쓰려면 `Refresh`, `Strongest` 또는 상한이 있는 명시적 합성
  정책과 conflict 검증을 Definition에 추가해야 한다.
- Instant impulse는 Modifier stack이 아니라 Event당 한 번 Host impulse service를 호출한다.
- 서로 다른 Channel이 한 결과에 관여하면 base gameplay 계산 뒤 Patch channel을 승인 순서로 적용하고
  마지막에 기존 Character physics 상한을 적용한다.

예를 들어 Weapon 또는 Punch의 기본 Knockback을 먼저 계산하고 `EFF-HIT-KNOCKBACK`을 적용한다. Target이
Ragdoll이면 승인된 Ragdoll 물리 제한을 추가로 적용한 뒤 최종 impulse를 clamp한다. Patch는 clamp를
우회하지 않는다.

### 7.2 Scope 종료

| Scope | 반드시 제거하는 시점 |
|---|---|
| 현재 Jump | Takeoff impulse 적용 직후 |
| Timed Character | Host expiry, elimination 또는 Round reset |
| Grab | Host expiry, Release, Break, Grab Target 상실, elimination 또는 Round reset |
| Down Episode | GetUp, elimination 또는 Round reset |
| Pending second wave | `Playing` 종료, Patch retire, 실행 또는 Round reset |
| Patch Instance | Instance retire, 새 Match 또는 Session 종료 |

Round reset은 모든 일시 Modifier를 제거하고 Character, DownCount, Weapon, prop와 Hazard를 깨끗한
baseline으로 복원한다. 그 뒤 유지할 Active Patch의 Trigger registration만 oldest부터 다시 만든다.
이전 Round의 지연 Event와 Modifier expiry callback은 새 Round Generation에서 적용하지 않는다.

---

## 8. Reconnect와 Network 수렴

Host는 Patch gameplay state의 유일한 기준이다. Reconnect snapshot에는 최소한 다음 의미 상태가
포함되어야 한다.

- Catalog version과 현재 Round Generation
- Active Patch의 Instance, Definition과 FIFO order
- Pending Patch 또는 동결된 Offer, 현재 Step과 남은 Host 시간
- 현재 유효한 Timed·Grab·Down Modifier의 Target, Scope와 남은 Host 시간
- 현재 인원 profile, 다음 정규 pulse, shuffle bag cursor, Incoming·Loose·Held·SpentPendingCleanup count와 pending second wave
- Firearm Ammo·fire mode, ShotSequence, SpentPendingCleanup 남은 시간, active Projectile과
  RecoilAccumulator·SpreadBloom·recovery state
- 마지막으로 원자 적용한 Patch event sequence

Reconnect Client는 Offer를 다시 뽑거나 과거 Effect를 replay하지 않는다. Snapshot 전체를 검증해
원자 적용한 뒤 snapshot sequence보다 새로운 Event만 처리하고 그 뒤에 Input control을 재개한다.

- Guest가 연결을 잃은 뒤 최대 30초 grace 동안 Host는 그 Player의 gameplay Input을 Neutral로 유지한다.
  Character는 제거·정지·무적화하거나 안전 위치로 옮기지 않고 현재 물리·Alive/Down 상태를 계속
  Simulation한다. Alive라면 SharedGameplayCamera subject로 남고 다른 Player의 Hit, OOB와 Hazard 판정을
  그대로 받을 수 있다.
- grace 중 실제 gameplay elimination이 일어나면 정상 elimination order와 PatchAuthor 규칙을 적용한다.
  Reconnect 시점에 아직 Alive면 현재 권한 Alive state로, 이미 탈락했다면 현재 spectator state로
  복원하며 disconnect 전 Transform·Alive 상태로 되감지 않는다.
- Author가 7초 안에 reconnect하면 같은 동결 Offer와 남은 시간만 본다.
- Deadline 뒤 reconnect하면 Host가 이미 Commit한 자동 선택 결과를 본다.
- Active Effect가 있는 Character는 Host의 현재 권한 물리 상태와 함께 Modifier 표현 상태를 복원한다.
- Reconnect Client는 supply pulse·bag·second wave를 재계산하지 않고 Host의 RoundGeneration과 예약 상태를
  원자 적용한다. 이미 admission된 Incoming Weapon만 현재 위치와 상태를 이어서 표현한다.
- Reconnect·disconnect로 현재 Round의 frozen supply profile을 다시 고르지 않는다.
- Reconnect Client는 Projectile을 다시 Spawn·sweep하거나 ShotSequence·spread를 재계산하지 않고 Host
  snapshot 이후 event만 처리한다. Spent timer와 recoil recovery도 Host 시간에 수렴한다.
- Guest의 명시적 Leave 또는 30초 grace timeout은 `Forfeit`다. Forfeit는 PatchAuthor가 아니며,
  영구 참가자가 2명 이상이면 Round를 계속하고 한 명이면 Score·Patch·Offer 없이 Lobby로 돌아간다.
- Host Leave·Loss는 Session을 끝낸다. Patch state를 다른 Guest로 migration하거나 Lobby continuation으로
  가장하지 않는다.

---

## 9. P00·Weapon·인원수 호환

### 9.1 P00 `Construction Drop`

- Jump Higher는 승인 P00 높이, RecoveryBand, Crane clearance와 Camera framing 안에서 검증한다.
- Jump Pulse와 Attacker Recoil은 Character에만 힘을 주며 Crane Lever, Hook, Pad, moving panel, Weapon과
  loose prop을 원격 작동시키지 않는다.
- Patch로 움직인 Character가 유효한 hand relation을 유지한 채 Lever handle을 실제로 움직이면
  P00의 기존 physical control travel 규칙이 그 결과를 판정한다.
- Throw Resistance Low와 Grip Stronger는 Character Grab에만 적용하고 Crane·Hook·Lever Grab을 바꾸지 않는다.
- Ragdoll Slide와 Bounce는 OOB까지 자연스럽게 이동할 수 있지만 OOB boundary와 Crane lethal 판정을
  직접 만들거나 무효화하지 않는다.
- P00은 인원 profile의 상한과 `PATCH-PROT-009` 동시 두 개를 처리할 수 있는 겹치지 않는 safe Arrival
  Slot을 제공한다. Incoming 경로는 Crane Pad, Hook corridor, Lever, edge와 RecoveryBand를 피한다.
- Supply second wave와 forced-drop Weapon은 Crane·Hook·Lever·Panel을 원격 작동하거나 Hazard phase를
  바꾸지 않는다. 착지 뒤 Loose Weapon이 Character의 유효 손 관계로 실제 control을 움직이는 경우에만
  기존 map physical control 규칙을 사용한다.
- Firearm Projectile, recoil impulse·torque와 muzzle physics는 Crane·Hook·Lever·Panel control을 작동하거나
  Hazard phase·strength를 바꾸지 않는다. Map Collider는 Projectile을 first blocking Hit로 제거만 한다.
- 어느 Patch도 모든 OOB·LethalHazard 경로를 막거나 무한 Recovery, 영구 끼임과 안전지대를 만들 수 없다.
- Round reset 뒤 Patch가 만든 impulse, Grab scope와 Ragdoll modifier가 0개 남아야 한다.

### 9.2 Weapon

- W1 전 `TRG-ATTACK-HIT-CONFIRMED`는 Punch·Kick·Dropkick을 사용한다.
- W1 뒤 Pistol·LongGun의 유효 Firearm Hit와 Bat·Hammer의 유효 Melee Hit가 같은 Trigger adapter를 사용한다.
- `TRG-WEAPON-HIT-CONFIRMED`는 Source Weapon이 필수이므로 Kick·Dropkick과 일반 Punch를 제외한다.
- Pistol은 semi-auto 7발, LongGun은 full-auto 30발이며 둘 다 reload 0이다. Ammo 0이면 Host forced
  release→SpentPendingCleanup `START 2~4초`→remove를 사용하고 제거 전까지 supply cap에 포함한다.
- Firearm Hit는 visible Host Projectile의 swept SphereCast first Hit만 사용한다. pierce·ricochet·Projectile
  map-control activation과 Guest Hit 결과 주장은 0이다.
- Pistol은 narrow spread·strong single recoil, LongGun은 deterministic ShotSequence 기반 bounded
  RecoilAccumulator·SpreadBloom과 release/gap recovery를 사용한다. Recoil animation은 read-only다.
- 기본 반복 투하는 2인 `10/22초·cap2`, 3인 `8/16초·cap2`, 4인 `6/12초·cap3` START profile을 사용한다.
  Round마다 네 종류의 결정적 shuffle bag을 쓰고 실제 admission만 cursor를 소비한다.
- `EFF-HIT-KNOCKBACK`은 Weapon Damage를 바꾸지 않고 기존 Hit knockback 결과만 보정한다.
- `EFF-ATTACKER-RECOIL`은 Attack action당 한 번이며 한 Swing이 여러 Contact를 만들었다고 누적하지 않는다.
- `EFF-WEAPON-SUPPLY-DOUBLE`과 `EFF-WEAPON-SUPPLY-SECOND-WAVE`는 capacity를 넘지 않고 미생성분을
  backlog로 만들지 않는다. second wave는 Supply Trigger를 다시 만들지 않는다.
- `EFF-VICTIM-HELD-WEAPON-FORCED-DROP`과 `EFF-ATTACKER-SOURCE-WEAPON-FORCED-DROP`은 기존 forced-release와
  ownership replication만 사용하며 새 Damage·Hit·drop impulse를 만들지 않는다.
- unconfirmed Projectile contact, Swing, dropped Weapon, Hazard와 Patch impulse는 허위 Attack Trigger를 만들지 않는다.
- Weapon이 Patch impulse 결과로 떨어진다면 기존 강제 Release·ownership·replication 규칙을 사용한다.
- Round reset 뒤 Fire cooldown, Projectile, Swing, ownership과 Patch residue를 함께 제거한다.

### 9.3 2·3·4인

- Catalog, Patch strength, radius, duration, Candidate 규칙과 최대 Active 수는 인원수에 따라 바뀌지 않는다.
- 기본 Weapon supply cadence와 동시 존재 상한만 승인 profile에 따라 2·3·4인이 다르다. Geometry,
  Weapon pool, Patch desired count와 Hit·ownership 규칙은 동일하다.
- Pulse는 Actor를 제외한 현재 Alive Character만 대상으로 하며 2·3·4인 모두 같은 반경을 사용한다.
- 3·4인 PatchAuthor는 최초 탈락자 규칙을 사용하며 2인·4인 결과로 3인 검증을 대신하지 않는다.
- Disconnect grace 중 Alive Character는 인원수와 무관하게 물리·피격·OOB·Hazard·Camera 대상이다.
  Forfeit는 PatchAuthor 순서에서 제외하고, 영구 참가자가 2명 이상이면 현재 geometry·Patch·supply
  profile로 계속한다. 한 명만 남은 즉시 반환은 Score·Patch·Offer 0이다.
- Roster나 connection order가 Candidate 순서와 Effect target 순서를 바꾸지 않는다.

---

## 10. Alpha Text UI와 미래 Presentation 경계

### 10.1 Alpha에서 구현하는 것

Alpha Player surface의 Patch UI는 Round 사이에만 나타나는 transient Text Adapter다. 지속 노출되는
Match HUD, Active Patch HUD와 Ammo HUD는 0이며 상세 Runtime 상태는 developer debug에서만 본다.
Alpha Player copy는 승인 한국어 문장만 사용한다. English translation·font fallback과 Patch 전용
audio/music은 post-Alpha presentation이며 기능 Gate가 아니다.

- Author에게 Trigger 두 문장을 선택 가능한 평문 Control로 표시
- Trigger 선택 뒤 Effect 두 문장을 선택 가능한 평문 Control로 표시
- developer debug `START` binding은 `[1]`, `[2]`를 사용할 수 있지만 최종 입력과 Layout으로 고정하지 않음
- Host 기준 남은 초, Author 이름과 자동 선택 여부 표시
- 모든 Player에게 Commit 문장과 다음 Round 적용 문장을 transient 결과로 표시
- transient Patch 화면 또는 developer debug에서만 Active Patch를 FIFO 순서로 최대 3줄 표시
- developer debug에서 실제 Trigger, Target과 `Applied`, `NoEligibleTarget`, `CapacityLimited`,
  `NoSafeDropZone`, `LandingBlocked`, `Guarded` 결과를 한 줄로 표시
- Match local menu는 local-only·non-pausing이며 열려 있는 동안에도 Host Patch deadline과 Simulation을
  계속 진행하고 Patch authority를 갖지 않음

최종 Layout, Icon, 색 체계, 전환 Animation, VFX와 SFX를 Alpha Patch 완료 조건에 포함하지 않는다.
Alpha Catalog의 `IconKey`, `AnimationCueKey`, `VfxCueKey`, `SfxCueKey`는 `null/deferred`다.

### 10.2 Presentation Event

Simulation은 UI·Animation·VFX·SFX를 직접 호출하지 않고 다음 의미 Event와 Read Model만 제공한다.

| Event | Presentation에 전달하는 의미 |
|---|---|
| `PatchOfferOpened` | Author, 동결 Trigger Branch, Host Deadline |
| `PatchChoiceAccepted` | 승인된 Step과 Candidate, 다음 Step |
| `PatchCommitted` | 완성 문장과 다음 Round 적용 예정 상태 |
| `PatchActivated` | 새 Instance와 Active FIFO order |
| `PatchRetired` | 제거된 Instance와 제거 이유 |
| `PatchTriggered` | Trigger, Actor, Target 후보와 Root Event |
| `PatchEffectResolved` | Effect, 실제 Target과 `Applied/NoEligibleTarget/CapacityLimited/NoSafeDropZone/LandingBlocked/Guarded` 결과 |

Presentation Event는 gameplay Effect의 성공 여부를 결정하거나 Host state를 되돌리지 않는다. 현재 Text
Adapter와 미래 제품 UI가 같은 Event·Read Model을 소비해야 하며 UI 교체 때문에 Simulation을 수정하지
않는다.

---

## 11. 검증 Matrix

### 11.1 자동 검증

- 같은 Match seed, Round, Catalog와 Active Set은 같은 동결 2×2 Offer를 만든다.
- Author 외 선택, 만료 선택, 후보 외 ID, 중복·역순 선택은 state mutation 0건이다.
- 선택 없음, Trigger만 선택, Author disconnect가 각각 정해진 첫 유효 Candidate로 끝난다.
- 가능한 projected Active Set마다 서로 다른 Trigger 2개와 각 Effect 2개가 존재한다.
- Active `A,B,C` 뒤 `D` 활성화 결과는 `B,C,D`이며 retired `A`는 Trigger를 받지 않는다.
- outgoing `A`를 다시 선택하면 이전 `A`를 유지하지 않고 `B,C,new A`가 된다.
- 동일 Root Event·PatchInstance·Entity 재진입, 같은 Attack·Target 중복 Hit와 같은 Down Episode 반복 발동은 0건이다.
- 같은 Modifier 재발동은 magnitude stack 0, lifetime refresh 1회다.
- Round reset 뒤 일시 Modifier, delayed Event, Patch impulse callback과 이전 Generation mutation은 0건이다.
- Reconnect snapshot은 Offer·Pending·Active·Modifier를 원자 복원하고 과거 Effect replay 0건이다.
- Disconnect grace 동안 Guest input은 Neutral이고 Character 물리·피격·OOB·Hazard·Alive Camera 참여는
  계속된다. Reconnect는 현재 Alive 또는 spectator state로 수렴하며 과거 상태 rewind는 0건이다.
- 명시적 Leave·grace timeout Forfeit는 PatchAuthor가 아니고, 2명 이상 continuation은 다음 실제 gameplay
  elimination을 Author로 사용한다. 한 명만 남은 반환의 Score·Patch·Offer는 각각 0건이다.
- Match local menu를 열어도 Host Tick, Round/Patch timer와 다른 Player action이 계속되며 time scale,
  invulnerability와 Patch authority mutation은 0건이다.
- Alpha Player surface의 지속 Match/Active Patch/Ammo HUD는 0건이고 transient Patch UI와
  developer-only debug 정보가 서로 섞이지 않는다.
- Patch effect가 Input·Hit·Grab Event를 조작해 Trigger를 위조하는 경로는 0개다.
- Ground tap/hold와 Air tap/hold/chord는 press마다 Punch·Kick·Dropkick·Grab 중 최대 하나만 만들고,
  `60/80/100ms` chord 비교에서 `START 80ms` arbitration과 AirAttackToken 소비가 결정적이다.
- Kick은 좌우 Anchor별 Action, Dropkick은 양발이 공유하는 단일 AttackAction이며 같은 Action·Target의
  Hit와 Patch03 적용은 1회 이하, Patch04 recoil은 Action당 1회 이하다.
- DropkickRecovery·physics tumble이 DownEpisode·DownCount·Down Trigger를 만드는 경우와 Animator/root
  motion·Animation Event가 authority transform·Hit을 만드는 경우는 각각 0건이다.
- Pistol은 semi-auto accepted Shot 7개, LongGun은 full-auto accepted Shot 30개 뒤 Ammo 0이며 reload
  action은 0건이다. Ammo 0 forced release→SpentPendingCleanup→remove 순서와 cap count가 일치한다.
- SuddenDeath에서는 새 Supply 0이지만 existing Firearm Shot·Projectile과 Spent deadline이 계속되고,
  RoundResult 이후 새 Fire·active Projectile·다음 Round 이월은 0건이다.
- Projectile sweep은 fixed-step segment의 first Hit 하나만 만들고 pierce·ricochet·gravity drift·map control
  activation·Guest result mutation은 0건이다. TTL·OOB·reset·stale generation 뒤 Hit도 0건이다.
- Pistol single recoil과 LongGun RecoilAccumulator·SpreadBloom·ShotSequence는 Host 재실행에서 같고
  impulse·torque·spread 상한을 넘지 않는다. Animator·packet order가 recoil state를 바꾸는 경우는 0건이다.
- Projectile AttackAction·Target당 Patch03은 1회 이하, Patch04는 Projectile Action당 1회 이하다.
  LongGun 반복 Shot은 각 ShotSequence로 dedupe되고 delayed Hit 뒤 source spent/owner loss의 Patch12는
  `NoEligibleTarget`이며 다른 Weapon을 대신 해제하지 않는다.
- 2·3·4인 profile의 pulse timestamp와 cap admission이 Host 재실행에서 같고 cap 초과·backlog는 0건이다.
- shuffle bag은 같은 MatchSeed·Round에서 같은 admitted Weapon 순서를 만들며 skip·미생성분의 cursor 소비는 0건이다.
- `Incoming`이 Character·Weapon·map control·Hazard 결과를 만들거나 Camera bounds를 넓히는 경우는 0건이다.
- admission과 landing clearance가 막힌 Weapon은 각각 `NoSafeDropZone` 또는 `LandingBlocked`로 끝나며
  활성 Collider·즉시 재시도·backlog는 0건이다.
- 회수 불가능한 Loose Weapon은 Host WeaponCleanupBoundary에서 제거되고 Held Weapon의 일부 Collider만
  경계를 넘은 오탐 cleanup은 0건이며, 빈 capacity의 즉시 보충은 0건이다.
- `PATCH-PROT-009`의 desired 2개는 capacity만큼만 admission되고 제한 결과는 `CapacityLimited`로 남는다.
- `PATCH-PROT-010` second wave는 Root pulse당 한 번 이하이며 Sudden Death·reset·다른 Generation 실행과
  Supply Trigger 재진입은 0건이다.
- `PATCH-PROT-011`은 victim의 유효 Held Weapon만, `012`는 여전히 attacker가 Held한 Source Weapon만
  forced release하며 Damage·Knockback·Ammo·cadence mutation과 새 Hit는 0건이다.

### 11.2 2·3·4인 직접 검증

첫 12개 각각을 2인, 3인과 4인에서 최소 한 번 실제 발동한다. 모든 Active 3개 순열을 전수 시험하지
않고 다음 대표 조합을 사용해 상호작용을 확인한다.

- Jump Pulse, Hit Knockback과 Ragdoll Slide의 자연 Down chain
- Jump Higher, Attacker Recoil과 Grip Stronger
- Ground Punch, 좌우 Air Kick과 Dropkick에서 Patch03·04의 Action/Target dedupe
- Throw Resistance Low, Ragdoll Bounce와 Hit Knockback
- Supply Double, Victim Held Weapon Forced Drop과 Ragdoll Slide
- Supply Second Wave, Attacker Source Weapon Forced Drop과 Jump Pulse
- P00 중앙, edge, RecoveryBand와 Crane·Hook 근처
- Character `PATCH-PROT-001..008`은 Punch·Kick·Dropkick source pre-gate에서 먼저 닫는다.
- W1 뒤 Firearm·Melee source adapter, 반복 supply와 `PATCH-PROT-009..012`는 `WPN-008`과 P00 통합
  Evidence에서 검증하고 전체 `UG-PATCH12`를 닫는다.
- Pistol 7발 semi-auto, LongGun 30발 full-auto, spent cleanup, Projectile sweep과 recoil/spread는
  `FIR-001..003`, `WPN-005`, `ANP-003`에서 2·3·4인으로 검증한다.
- 각 인원수에서 disconnect grace 중 피격·OOB·Hazard elimination 뒤 Alive/spectator reconnect와
  explicit Leave/timeout Forfeit continuation을 검증한다. Forfeit로 한 명만 남는 경우에는 Score·Patch
  없이 Lobby로 돌아가고 Offer가 열리지 않아야 한다.

각 Run은 선택시간, 발동 횟수, 실제 대상, `NoEligibleTarget`, Round 종료시간, OOB·Hazard 판정, reset 잔여
상태와 Host·Guest divergence를 기록한다. 2인·4인 결과로 3인을 대신하지 않는다.

### 11.3 `UG-PATCH12-DESIGN` 승인 기록과 기능 Gate

사용자는 2026-08-25에 Patch12·Supply·Ground/Air Action을, 2026-08-26에 Firearm Runtime을 포함한
다음 설계 기준선을 승인했다.

- 첫 12개 Patch ID, 한국어 문장, Actor·Target과 제외 Domain
- 각 Effect의 `START` tuning 범위
- 2·3·4인 반복 supply 첫 시점·주기·cap START profile과 Host shuffle·safe DropZone·reset 경계
- next-round projected set과 outgoing oldest 재선택 규칙
- 7초 2×2 선택과 자동 선택
- 같은 Modifier의 refresh-only·magnitude non-stack 규칙
- Alpha Text UI only와 미래 Presentation seam
- Ground Punch·Grab과 Air Kick·Dropkick·Grab arbitration, AirAttackToken과 root-motion authority 0
- Pistol 7발 semi-auto·LongGun 30발 full-auto·reload 0, spent cleanup, visible Host Projectile과 Hybrid recoil
- Disconnect grace의 physical/vulnerable Character, current-state reconnect, Forfeit Author 제외와
  one-left Score·Patch 0 Lobby return
- 지속 Match/Ammo HUD 0, transient Patch Text UI·developer debug 분리와 local-only non-pausing Match menu

이 승인으로 `UG-PATCH12-DESIGN`과 Firearm Runtime 방향은 PASSED다. 문서 승인만 완료됐으며
구현·Build는 시작하지 않았다.
구현에서는 Character `001..008`을 먼저 기능 검증하고, W1 뒤 반복 supply·Firearm·Melee와 Weapon
`009..012`를 합쳐 2·3·4인 `UG-PATCH12`에서 실제 적용·재미·이해도와 tuning을 승인한다.

---

## 12. Patch 13~20 확장 원칙

Patch 13~20은 첫 12개의 Runtime을 우회하지 않고 다음 조건을 만족할 때만 Catalog에 추가한다.

1. Host가 확정할 수 있는 의미 Trigger와 Actor·Target context를 사용한다.
2. Effect마다 대상, lifetime, Modifier channel, refresh·stack 정책과 제외 Domain을 명시한다.
3. Active 3개 projected set에서도 2×2 Candidate closure를 자동 검증한다.
4. 같은 문장을 수치만 바꾼 중복 Patch로 후보 수를 채우지 않는다.
5. 특정 인원수, Spawn과 한 Map에서만 유효한 숨은 No-op을 만들지 않는다.
6. Patch chain은 유한하고 같은 Rule·Entity 재진입을 만들지 않는다.
7. Weapon SourceKind를 추가해도 Damage와 Hit authority를 Patch가 소유하지 않는다.
8. 2·3·4인과 P00·후속 Map의 기본 OOB·Hazard 종료 경로를 유지한다.
9. UI·VFX·SFX가 없더라도 Text와 gameplay 결과만으로 Trigger와 Effect를 검증할 수 있어야 한다.
10. Hazard timing·strength, Size·Collider·Base mass, Groggy time 또는 Weapon Damage를 직접 바꾸려면
    기존 Catalog 확장이 아니라 별도 설계 검토와 사용자 승인을 먼저 받는다.

Patch 13~20의 실제 목록과 수치는 첫 12개 `UG-PATCH12` 결과를 확인한 뒤 별도 Review Candidate로 만든다.
