# MAP P00 — Construction Drop

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| MapId | `MAP_P00_CONSTRUCTION_DROP` |
| 문서 버전 | 0.7.0 Alpha Greybox·Participation·Minimal UI·Firearm Compatibility Baseline |
| 최종 수정일 | 2026-08-26 |
| 상위 기준 | `01_PRD.md` 1.8.0, `02_SRS.md` 1.8.0, `MAP_DESIGN_GUIDE.md` 1.8.0 |
| 패치 기준 | `PATCH_DESIGN.md` 0.5.0 |
| 상태 | 제작 전 — 모든 tuning 수치 `START`, 검증 결과 없음 |
| 지원 인원 | 2·3·4인, 동일 geometry·Bounds·Hazard·Camera |
| Topology | `CentralArena + VerticalPressure + FunnelAndControl` |

이 문서는 첫 Unity Greybox와 Alpha 제품 Art Pass의 outcome 기준이다. 내부 직렬화 byte, hash 수식과
반복 fixture 수는 구현·테스트 문서가 소유한다.

### 0.1 0.7.0 변경 요약

- Match Esc가 P00 Simulation을 멈추지 않고 local input만 neutralize하며 Character는 physical·vulnerable로
  남는 경계를 추가했다.
- unexpected Guest disconnect의 30초 Character·Camera·slot 보존, Leave/grace 만료 Forfeit와
  Forfeit PatchAuthor 0을 추가했다.
- permanent participant 한 명 잔존 시 score·Patch 없이 OpponentLeft→Lobby, Host Leave/Loss 시
  Host Migration 없이 Session 종료하도록 했다.
- Persistent Match/Ammo HUD 없이 P00 Telegraph·Weapon 방향을 검증하고 Ammo를 debug-only로 제한했다.
- locked Unity/Blender·art/license profile, 기본 environment SFX와 BGM 0의 Alpha 제작 범위를 연결했다.

### 0.2 0.6.0 변경 요약

- Pistol semi-auto 7발과 LongGun hold full-auto 30발, no reserve/reload의 P00 검증을 추가했다.
- Host swept SphereCast projectile blocker·Character hit·TTL/OOB/reset과 no pierce/ricochet를 고정했다.
- projectile·recoil의 Drop Lever·Crane·Hook·Hazard·prop remote impulse 0과 SuddenDeath fire를 추가했다.
- ammo0 ForcedRelease→SpentPendingCleanup 2~4초·cap 포함·다음 pulse 보충과 delayed last-shot 경계를 추가했다.

### 0.3 0.5.0 변경 요약

- AirKick·Dropkick의 edge·OOB·Crane/Hook·Camera 호환성과 2·3·4인 검증을 추가했다.
- `DropkickRecovery`를 non-Down action recovery로 고정해 DownCount·groggy·Camera subject를 바꾸지 않게 했다.
- kick impact·body/feet contact가 Drop Lever와 다른 map control을 원격 작동하지 못하게 했다.
- 승인 Weapon functional archetype의 P00 silhouette·occlusion 검증을 연결했다.

### 0.4 0.4.0 변경 요약

- `라운드당 무기 1개`를 폐기하고 P00의 인원별 주기 supply·동시 상한·safe DropZone을 승인했다.
- Incoming 무해 상태, Host 결정적 4종 shuffle, capacity skip·OOB 보충과 Round reset을 추가했다.
- Character Patch01..08과 Weapon Patch09..12를 합친 초기 Patch12 P00 검증을 추가했다.
- Patch supply·forced drop이 Crane, Hook, Lever, panel·Hazard timing과 Weapon damage를 바꾸지 못하게 했다.
- 공급·Patch 전용 최종 UI·VFX·SFX 없이 Greybox 기능을 승인하고 후속 표현 port만 남겼다.

---

## 1. 전투 설명과 고정 결정

> 열린 옥상 가장자리와 공사 벽을 붙잡아 추락에서 복귀하면서, 상대를 Crane 중량물의
> 낙하 Pad로 몰아 경기 맵의 물리 Lever를 손으로 당겨 압착하거나 Swing Hook으로 밀어내
> 옥상 밖으로 떨어뜨린다.

| 항목 | 결정 | 상태 |
|---|---|---|
| 테마 | 완공 전 고층 건물 옥상 공사장 | `DECIDED` |
| 인원 | 2·3·4인 동일 geometry·Bounds·Hazard·Camera | `DECIDED` |
| 외곽 | 네 방향 모두 벽·투명 barrier 없는 추락 경로 | `DECIDED` |
| 복구 | Lip·Facade·Beam을 실제 손으로 잡고 제한 ClimbAssist | `DECIDED` |
| 직접 탈락 | `HZ_CRANE_DROP` final crush | `DECIDED` |
| 보조 위험 | `HZ_SWING_HOOK` 비치명 밀치기·매달리기 | `DECIDED` |
| Sudden Death | 외곽 panel 기울기와 기존 Hazard 강화 | `DECIDED` |
| 경기 맵 장치 | `CTRL_DROP_LEVER`를 실제 손으로 grab·pull | `DECIDED` |
| Camera | 한 Zone, fixed rig와 공용 Dolly | `DECIDED` |
| AirKick·Dropkick | 기존 OOB·Hazard·Camera·hand-only control을 사용하는 공통 Character action | `DECIDED`, 수치 `START` |
| Weapon supply | W1 뒤 네 종류를 인원별 pulse/cap profile과 safe DropZone으로 공급 | 구조 `DECIDED`, 수치 `START` |

P00의 Drop Lever는 경기 맵 Hazard를 작동하는 gameplay control이다. Lobby 시작 UI나 Session 시작
방식과 연결하지 않는다.

---

## 2. 단위·축·전체 배치

```text
1 CU = 승인 MasterCharacter Authority collider의 서 있는 높이
WorldMeters = DimensionCU × CharacterHeightMeters

Origin = 중앙 옥상 상판 중심·윗면
+Y = Up
+X = Camera 화면 오른쪽
+Z = Camera 화면 위쪽 이동 방향
```

`CharacterHeightMeters`는 아직 미측정이다. Greybox는 CU parameter로 다시 생성하고 Scene Root를
임의 scale하지 않는다.

### 2.1 Top view

```text
                           +Z
              background Crane rail/tower

      ┌─────────────────────────────────────┐
      │             SPAWN_N                 │
      │       SWING_HOOK corridor           │
      │                                     │
      │ SPAWN_W   [ CRANE DROP ]  SPAWN_E  │
      │              PAD A                  │
      │                     DROP_LEVER      │
      │             SPAWN_S                 │
      └─────────────────────────────────────┘

         four open edges
         Lip / Facade / Recovery Beam
         lower OOB volumes
```

### 2.2 Greybox 시작 치수

| 요소 | 중심·범위 CU | `START` 목적 |
|---|---|---|
| 전체 roof footprint | `8.8 × 6.2` | core+네 edge panel |
| 고정 core | `7.0 × 4.4` | 중앙 안정 전투 면 |
| N/S edge panel | 각 폭 `0.90`, 외곽 hinge | Sudden Death tilt |
| E/W edge panel | 각 폭 `0.90`, 외곽 hinge | Sudden Death tilt |
| Crane Pad | 중앙 `1.45 × 1.25` | direct lethal zone |
| Drop Lever | `(2.75, 0, -1.65)` 부근 | 손 조작 control |
| Hook corridor | 북쪽, X `5.0`, Z폭 `0.9` | displacement sweep |
| Weapon DropZone pool | stable core의 분리된 authored pocket 2개 이상 | 동시 2개 Incoming과 Hazard/control 분리 |
| Weapon Cleanup Boundary | reachable roof·Recovery surface 아래 회수 불가능 영역 | Host Loose Weapon cleanup, Held 부분 진입 제외 |
| RecoveryBand | roof 아래 `Y -0.10~-2.30` | ledge·beam 복구 |
| Bottom OOB | top `Y -2.80` | 최저 추락 탈락 |
| Side OOB | 각 edge 바깥 약 `1.75` | 고속 수평 투척 정리 |

이 값은 첫 Greybox 시작점이다. Sprint multiplier, reach, Jump와 knockback을 넣어 다시 측정한다.
Weapon DropZone의 exact 좌표·descent 높이·landing 시간도 Greybox `START` 값이며 Character Spawn,
Crane Pad, Hook corridor, Drop Lever, panel sweep, RecoveryBand와 OOB를 겹치지 않게 배치한다.

---

## 3. Spawn과 Opening

네 Spawn은 N/E/S/W에 중앙에서 `1.85CU` 떨어져 중앙을 바라본다.

- 2인: N/S 또는 E/W 대향 pair를 Round마다 공정하게 교대
- 3인: 비울 cardinal과 Player 배정을 Round마다 회전
- 4인: 네 Spawn 모두 사용하고 Player 배정 회전

어느 인원수에서도 roof, open edge, Lever, Pad, Hook, panel, Bounds와 Camera는 바뀌지 않는다.

Match local menu와 unexpected disconnect grace는 Spawn을 다시 배정하지 않는다. 해당 Alive Character는
현재 transform에서 neutral input으로 physical·vulnerable 상태와 Camera subject를 유지한다.

Spawn Gate:

- 승인 Character standing·Ragdoll bounds끼리 겹침 없음
- static geometry, OOB와 Hazard의 초기 contact 없음
- 2·3·4인 모든 active pair의 안전거리 충족
- 기본 이동과 Sprint 모두에서 최초 접촉·edge·Lever 접근 시간을 측정

### 3.1 Opening 보호

RoundCountdown 종료 뒤 `START 3초` 동안:

- Crane이 새 ActiveLethal에 들어가지 않는다.
- Lever activation은 버리지 않고 queue하되 Telegraph와 escape 시간을 줄이지 않는다.
- Hook은 낮은 `IdleSwing` presentation만 사용하고 Spawn corridor를 지나지 않는다.
- 보호 종료 뒤 개인 shield를 남기지 않고 모든 Player에게 같은 schedule을 적용한다.
- 승인 Weapon supply의 가장 빠른 첫 pulse는 4인 6초이므로 Opening 보호 중 Incoming Weapon은 0개다.

---

## 4. 상판·복구·OOB

- 중앙 core와 네 panel이 gap·overlap 없는 연속 전투 면을 만든다.
- 네 방향에 gameplay 난간·투명벽·자동 반발 Collider가 없다.
- 바닥 decal과 seam은 깊이를 설명하지만 발을 걸지 않는다.
- 배경 Crane tower·skyline에는 gameplay Collider가 없다.
- Render Mesh의 큰 모서리와 단순 Collider가 일치한다.

Recovery vertical flow `START`:

| 영역 | Y CU |
|---|---:|
| Roof top | `0.00` |
| Top lip | `0.00~-0.28` |
| Facade grab | `-0.28~-1.45` |
| Recovery Beam | 약 `-1.35` |
| Lowest grab geometry | `-1.65` |
| Clear fall buffer | `-1.65~-2.80` |
| Bottom OOB | `-2.80` 아래 |

`ClimbAssist`는 승인 surface를 한 손 이상 실제로 잡고 Jump했을 때만 Host가 적용한다. 같은 공중
상태당 2회, 350ms cooldown을 `START`로 비교한다. Beam 위에서 2초 이상 안정 캠핑이 가능하거나
ClimbAssist로 무한 상승하면 geometry·friction·profile을 조정한다.

`PATCH-PROT-001`의 Jump 수직 modifier는 일반 Jump에만 적용하고 `ClimbAssist` 상승 impulse·횟수·
cooldown에는 적용하지 않는다. `PATCH-PROT-002` pulse도 Recovery geometry나 Grab surface에 힘을
가하지 않는다.

Alive recovery Player는 Camera subject에 남는다. Authority core가 명시적 OOB에 들어간 tick에만
탈락시키고 같은 tick에 Camera subject에서 제외한다.

local Match menu 또는 reconnect grace 중인 Character도 같은 OOB·Crane·Hook·Attack 조건을 사용한다. 이 상태만으로
무적, collision 0, 자동 ledge snap, Camera 제외와 안전 Spawn 이동을 만들지 않는다.

### 4.1 AirKick·Dropkick edge·OOB 계약

- AirKick·Dropkick은 roof edge, panel, Pad와 RecoveryBand에서 같은 Character action 규칙을 사용한다.
- action 중 Authority core가 Bottom/Side OOB에 들어가면 기존 tick에 탈락한다. action 전용 면역,
  보이지 않는 반발벽, 자동 ledge snap과 복귀 teleport는 0이다.
- action 도중 실제 손이 승인 Grab surface와 contact하고 별도 Grab 규칙을 만족한 경우에만 기존
  Ledge Grab·ClimbAssist 후보가 된다. 발·몸 contact나 `DropkickRecovery`만으로 edge를 잡지 않는다.
- `DropkickRecovery`는 action recovery이며 Down·Stun·Ragdoll Episode가 아니다. 이 상태에 들어간 것만으로
  DownCount·groggy duration을 늘리거나 Alive Camera subject에서 제외하지 않는다.
- kick impact가 상대를 OOB로 보내면 기존 OOB route와 attacker attribution을 사용하고 action이 직접
  elimination을 선언하지 않는다.

---

## 5. LethalHazard — Crane Drop

Payload의 측면 충돌이나 하강 중 스침은 Stun·Displacement일 뿐이다. 표시된 Pad와 Payload 사이
final crush가 성립할 때만 직접 탈락한다.

### 5.1 Phase `START`

| Phase | 시간 | 결과·표현 |
|---|---:|---|
| Idle | 8.0s | Payload 대기, Lever 사용 가능 |
| Telegraph | 1.25s | siren, beacon 형태, 커지는 floor shadow |
| ActiveNonLethal | 0.45s | Payload 하강, 측면 bounded impulse |
| ActiveLethal | 0.20s | final crush 조건 유효, 최고 대비 |
| Recovery | 2.10s | Payload 상승, lethal volume 비활성, Lever reset |

Escape window는 Telegraph+ActiveNonLethal의 `START 1.70s`다. network 지연이나 Lever 입력으로
짧아지면 안 된다.

### 5.2 Drop Lever

- 실제 hinge axis와 handle Collider를 가진다.
- Player 손이 handle을 잡고 `START 38°`까지 `START 300ms` 연속 travel해야 한다.
- `E`, proximity, UI click이나 Client의 단순 activation 보고는 수락하지 않는다.
- 상대가 조작자의 몸·팔을 밀거나 함께 handle을 잡아 방해할 수 있다.
- valid activation은 Idle을 끝내고 Telegraph를 시작한다.
- 진행 중 cycle을 중첩하거나 Telegraph를 생략하지 않는다.
- 아무도 조작하지 않으면 MaxIdle 뒤 자동 cycle로 control 독점을 방지한다.
- AirKick·Dropkick hit, feet/body contact와 action impulse는 valid activation이 아니며 handle을 원격 작동시키지 않는다.
- action 중 별도로 유지한 유효 손 Grab relation이 handle을 실제 threshold까지 움직인 경우에만 기존 hand travel을 평가한다.
- `PATCH-PROT-002..006`은 Drop Lever를 직접 target하거나 원격 activation force를 만들 수 없다.
  다만 Patch로 밀린 Character가 유효한 hand relation을 계속 유지해 handle을 실제로 움직였다면
  기존 physical control 규칙이 그 travel을 판정한다.
- Incoming과 `PATCH-PROT-009..012`의 supply/forced drop도 Drop Lever를 직접 target하거나 원격
  activation하지 않는다. 착지·release 뒤 Loose Weapon collision만으로 valid hand travel을 대신하지 않는다.

### 5.3 직접 탈락 조건

Host가 다음을 모두 확인한다.

1. ActiveLethal phase
2. Victim Authority core가 crush volume과 overlap
3. Payload bottom과 Pad top gap이 `START 0.32CU 이하`
4. Victim이 Pad support 영역과 PlayBounds 안에 있음
5. Victim이 Alive

색만으로 lethal을 설명하지 않고 Payload와 Pad가 닫히는 형태, chevron, shadow와 siren을 함께 사용한다.
AirKick·Dropkick·DropkickRecovery 중이어도 위 다섯 lethal 조건이 성립하면 동일하게 탈락하며 action 전용
Crane 면역·구출·phase 변경은 없다.

---

## 6. DisplacementHazard — Swing Hook

Hook은 북쪽 절반을 좌우로 움직이며 Player를 밀거나 매달리게 한다. 직접 lethal phase와 lethal
mask는 없다. 최종적으로 OOB에 들어갈 때만 탈락한다.

| Phase | `START` 시간 | 결과 |
|---|---:|---|
| Idle | 0.20s | endpoint 대기, 다음 방향 고정 |
| Telegraph | 0.45s | 방향 indicator와 cable tension |
| ActiveNonLethal | 2.15s | 반대 endpoint까지 sweep·grab 이동 |
| Recovery | 0.20s | 감속, 새 contact 차단 |

두 half-cycle의 기본 full period는 6초다. Hook body 지름 `0.34CU`, corridor X `5.0CU`를
시작값으로 사용한다.

- 충돌은 bounded push/Stun 후보
- 한 손·양손 Grab 가능
- motor는 Player가 잡아도 계속 이동
- GripStress로 무한 매달리기 방지
- `E` interaction 없음
- cable은 visual이며 별도 얇은 damage Collider 없음
- AirKick·Dropkick impact는 Hook phase·motor·direct-lethal mask를 바꾸지 않는다. Hook 또는 kick에 밀린
  Character의 최종 탈락은 기존 OOB/Crane 조건만 사용한다.

---

## 7. Authority Weapon Supply

P00의 Weapon 공급은 Round마다 한 번으로 제한하지 않는다. RoundCountdown 뒤 `Playing=0초`부터
AuthorityHost가 다음 60초 `START` profile을 실행한다.

| 인원 | 첫 정규 pulse | 이후 주기 | `Incoming+Loose+Held+SpentPendingCleanup` 동시 상한 | 60초 안의 pulse |
|---:|---:|---:|---:|---|
| 2인 | 10초 | 22초 | 2 | 10, 32, 54초 |
| 3인 | 8초 | 16초 | 2 | 8, 24, 40, 56초 |
| 4인 | 6초 | 12초 | 3 | 6, 18, 30, 42, 54초 |

Host는 Round 초기 participating roster로 profile을 고정하고 Disconnect·Reconnect·Forfeit는 다음
Round 시작에만 새 profile을 선택한다.

grace 중 neutral Character도 `LandingClearance`를 막는 physical object이고 valid combat·OOB·Hazard target이다.
Disconnect가 supply capacity를 비우거나 DropZone을 다시 고르게 하지 않는다. 3·4인에서 Forfeit 뒤
permanent participant가 2명 이상 남아 Round가 계속되면 시작 때 고정한 profile을 유지한다.

정규 pulse의 baseline desired batch는 1개다. Host는 pulse마다 현재 Incoming·Loose·Held·SpentPendingCleanup Weapon을
모두 세고 남은 capacity만 admission한다. full이면 `CapacityLimited` 0개로 끝내고 누락분을 backlog,
즉시 retry 또는 다음 pulse 가산으로 보존하지 않는다.

### 7.1 Type·DropZone 결정

- Round 시작마다 `MatchSeed + Round + WeaponCatalogVersion`으로 Pistol, LongGun, Bat, Hammer 한 개씩의
  결정적 shuffle bag을 만든다.
- 실제 admission된 spawn만 bag cursor를 소비한다. cap skip·미생성분은 소비 0이며 네 종류 소진 뒤
  같은 입력에서 deterministic next bag을 만든다.
- Host는 stable DropZone ID와 Round·pulse·spawn ordinal로 서로 다른 유효 Zone을 선택한다.
- DropZone은 Player Spawn·Crane Pad·Hook corridor·Drop Lever·panel sweep·RecoveryBand·OOB와 겹치지 않는다.
- P00은 `PATCH-PROT-009`의 동시 두 개를 수용하도록 서로 겹치지 않는 safe Zone 두 개 이상을 유지한다.
- 각 Zone의 `LandingClearance`는 admission과 landing에서 Character, 다른 Incoming·Loose·Held·SpentPendingCleanup Weapon과 moving
  part 겹침을 Host가 재검사한다. 유효 Zone 0은 `NoSafeDropZone`으로 spawn·cursor 소비·재시도 0이다.

### 7.2 Incoming·landing·OOB

`Incoming`은 Weapon 공급 표현 상태이며 다음 gameplay 결과가 모두 0이다.

- Character damage·Down·knockback·Grab, 다른 Weapon collision과 pickup
- Drop Lever·Crane·Hook·panel·Hazard contact/activation
- owner, fire/swing/use와 SharedGameplayCamera subject 참여

Host가 authored landing에 도달하고 clearance가 비었다고 확정한 뒤에만 `Loose`로 바꾸고 정상 Weapon
Collider·Grab·physics를 활성화한다. 막힌 landing은 noninteractive Incoming으로 `START 1~2초`만
기다리고 끝까지 막히면 `LandingBlocked`로 제거한다. active Collider·피해·bag rollback·즉시 대체는 0이다.

P00의 Host `WeaponCleanupBoundary`는 reachable roof·RecoveryBand 아래에서 회수 불가능한 Loose Weapon을
제거한다. Held Weapon은 owner가 유효한 동안 긴 Collider 일부만 경계를 넘었다고 제거하지 않으며 owner
elimination 또는 유효 release 뒤에 평가한다. cleanup 뒤 다음 정규 pulse만 남은 capacity를 보충한다.

Playing→SuddenDeath 또는 RoundResult 전환은 정규 pulse와 pending Patch-derived wave를 취소한다.
SuddenDeath 중 새 Weapon 공급은 0이며 Incoming·Loose·Held Weapon은 Round reset까지 유지한다.
`SpentPendingCleanup` deadline은 계속 진행해 만료 시 제거하고 빈 capacity는 next pulse만 사용한다.
위 exact 시간·주기·상한은 승인 구조 안의 `START` tuning이며 인원별 Gate 뒤에만 `LOCKED`한다.

---

## 8. Escalation·Sudden Death

새 함정을 추가하지 않고 처음부터 보이던 Crane, Hook과 hinge edge panel을 강화한다.

| Round 경과 시간 | 적용 결과 `START` |
|---|---|
| 0~3s | Opening 보호 |
| 3~40s | 기본 Crane·Hook, panel 고정 |
| 40s | Crane idle/recovery 단축, Hook period 5.0s |
| 60~61.5s | N/S panel Telegraph, 직전 상태 유지 |
| 61.5s | N/S panel 바깥쪽 25°, Hook 4.2s |
| 72~73.5s | E/W panel Telegraph, A 상태 유지 |
| 73.5s | E/W도 25°, Hook 3.6s, Crane recovery 단축 |
| 84~85.5s | 네 panel final Telegraph |
| 85.5s+ | 네 panel 40°, Hook 3.2s, Crane idle 2.5s |

Telegraph 시작과 실제 적용 시각을 구분한다. panel은 바깥으로 기울어 중앙 반대 방향으로
미끄러지게 하고 자체 lethal이 아니라 OOB 유도 surface다. 최종 중앙은 Crane Pad와 양방향 회피 폭을 유지한다.

Sudden Death 뒤 종료 p95 30초 이하를 첫 목표로 측정한다. Telegraph를 삭제하거나 즉사 범위를
화면 전체로 키워 목표를 맞추지 않는다.
Weapon supply는 60초 Playing profile에서 끝나며 SuddenDeath의 Hazard cadence와 서로 수정하지 않는다.

### 8.1 Match Esc·Guest Leave·disconnect·Forfeit

- Match Esc는 P00 Simulation, Round/Hazard/supply clock, projectile과 다른 Player를 멈추지 않는다. local
  gameplay input만 neutral이고 닫을 때 Mouse all-up 뒤 Hand를 재무장한다.
- local Match menu와 unexpected disconnect grace의 Alive Character는 physical·vulnerable 상태로 roof·Recovery,
  OOB, Crane, Hook, Weapon hit와 Camera 규칙을 그대로 사용한다.
- 명시적 Guest Leave는 즉시 Forfeit다. unexpected disconnect는 같은 Character·Camera·slot을 30초 유지하며
  reconnect 실패 또는 grace 만료 시 Forfeit다. Forfeit transition 자체는 elimination·score·Patch Trigger·
  PatchAuthor를 만들지 않는다.
- grace 중 실제 OOB·Crane final crush 또는 Attack 탈락이 Host에서 성립하면 disconnect와 무관하게 기존
  elimination을 사용한다.
- permanent participant가 2명 이상이면 P00을 계속한다. 한 명만 남으면 해당 Round의 score·PatchAuthor·
  Patch 적용은 0으로 두고 `OpponentLeft` 뒤 Lobby로 복귀하며 P00 round-only state를 정리한다.
- Host Leave/Loss는 P00 Round Result가 아니라 Session 종료다. Host Migration·P00 state 승계·새 score/Patch는 0이다.

### 8.2 Firearm projectile·recoil·Spent

- Pistol은 accepted press edge당 한 발·total7 semi-auto, LongGun은 valid hold 동안 total30 full-auto다.
  reserve·reload와 P00 전용 ammo pickup은 없다.
- P00은 roof/core·edge panel, Crane의 보이는 solid frame/payload와 승인 solid gameplay mass를 projectile
  blocker로 제공한다. background skyline, visual cable·decal과 얇은 비충돌 장식은 blocker가 아니다.
- Host projectile은 이전→현재 위치 swept SphereCast에서 첫 blocker 또는 Character hit 하나로 끝난다.
  pierce·ricochet·gravity·다중 Character hit는 0이며 speed·radius·TTL은 Firearm profile `START`다.
- Character hit만 Damage·Knockback과 attribution을 만든다. projectile·recoil은 Drop Lever, Crane, Hook,
  panel·Hazard phase, prop과 다른 Weapon을 원격 activation하거나 physics impulse로 움직이지 않는다.
- projectile은 hit·TTL·P00 projectile OOB·RoundResult·reset에서 제거한다. SuddenDeath는 새 supply만
  중단하고 기존 총기의 fire와 projectile hit는 정상 OOB·Crane/Hook·Hazard 규칙과 계속 공존한다.
- OpponentLeft→Lobby와 Host Leave/Loss 종료는 새 fire·supply를 수락하지 않고 active projectile,
  Spent와 owner relation을 다음 Scene/Session으로 넘기지 않는다.
- ammo 0의 마지막 shot 뒤 Host는 Weapon을 ForcedRelease해 `SpentPendingCleanup`으로 만든다. `START 2~4초`
  동안 cap에 포함하지만 Collider·pickup·Grab·Damage·Patch Trigger·P00 interaction은 0이며 deadline/reset에 제거한다.
- Spent 제거는 즉시 supply를 만들지 않고 다음 정규 pulse capacity만 비운다.
- 마지막 shot의 이미 생성된 projectile은 source가 Spent/Removed여도 immutable attacker/source snapshot으로
  hit할 수 있다. 이 delayed hit에서 Patch12 source forced drop은 `NoEligibleTarget`이고 다른 Weapon을 대신 놓지 않는다.
- Pistol은 accurate/strong per-shot recoil, LongGun은 Host shot ordinal 기반 cumulative deterministic spread
  bloom을 사용한다. visual recoil은 Camera/P00 authority·projectile 방향을 다시 계산하지 않는다.

---

## 9. Round reset

다음 Round 전에 Host가 복원한다.

- Crane trolley·payload transform, phase, cooldown과 queued activation
- Drop Lever angle, spring, grab와 activator
- Hook transform·velocity·phase와 grab
- 네 edge panel angle·velocity와 Sudden Death pending/applied stage
- Player Spawn·rotation·velocity·Alive·impact·harmful status
- AirKick·Dropkick active state·hit cache·velocity residue와 `DropkickRecovery`
- `DownCount=0`과 적용 중 down/groggy duration 제거
- Sprint request와 movement modifier 기본화
- 두 손 intent·contact·Grab relation·GripStress
- ClimbAssist count·cooldown
- Weapon supply timer·pending derived wave·shuffle bag/cursor
- Incoming·Loose·Held·SpentPendingCleanup Weapon, projectile·TTL·recoil/spread·owner/combat residue와 round-only prop
- OOB·Hazard contact cache
- local Match menu·Forfeit의 stale input, participant relation과 round-only cache

현재 Round의 PatchModifier·Trigger dedupe와 presentation event cache도 제거한다. active Patch ID와
순서는 유지하되 다음 Round의 깨끗한 Character·Weapon·P00 baseline 위에 다시 등록한다.

아직 만료되지 않은 reconnect grace의 deadline·slot reservation은 Session state로 유지한다. P00의 다음
Round가 먼저 시작되면 해당 Character는 새 Spawn에서 neutral input·physical·vulnerable 상태로 참가하고,
reconnect에는 Host의 현재 Alive/Spectator state만 복원한다.

score, selected P00, Match seed와 active Patch는 유지한다. 이전 Round의 늦은 input·contact·projectile이
새 Round에 적용되지 않아야 한다. 같은 Match 안에서 P00 Scene을 교체하거나 재추첨하지 않는다.

위 정상 Round reset과 이탈 중단을 혼동하지 않는다. permanent participant 한 명 잔존으로 Lobby에 복귀하는
중단 Round는 새 score·PatchAuthor·Patch를 만들지 않고, Host Leave/Loss는 Session state 자체를 폐기한다.

---

## 10. SharedGameplayCamera

P00은 한 Camera Zone을 사용한다.

- fixed yaw, `START` pitch 34°, vertical FOV 42°
- Dolly `START 8~15CU`
- gameplay safe margin `START 15%`
- Alive Character의 고정 ActiveSubjectBounds만 subject로 사용
- Ragdoll limb, hand, Cosmetic, Weapon, Hook cable과 Guest prediction 제외
- AirKick·Dropkick의 뻗은 발·visual pose로 subject bounds를 키우지 않고 `DropkickRecovery` 중 Alive Root를 계속 포함
- Incoming도 Camera subject에서 제외하고 DropZone·landing은 기존 gameplay framing 안에서만 표현
- recovery Player 포함, 실제 탈락 tick에 제외
- local Match menu 또는 reconnect grace의 Alive Character 포함, 실제 탈락이나 Forfeit transition에만 제외
- 한 명만 남아도 과도하게 Zoom하지 않음

Player-facing persistent Timer·Alive·Ammo HUD는 0이다. P00의 edge·Crane·Hook·Weapon direction은 상시 HUD에
의존하지 않고 읽혀야 한다. score·active Patch는 on-demand Tab에서만, Ammo·Projectile·Recoil/Spread는
developer-only debug에서만 확인하며 debug overlay를 Camera 판독 capture에는 사용하지 않는다.

2·3·4인 각각 16:9, 16:10, 21:9에서 다음을 capture한다.

- 중앙 기본 이동·Sprint 전투
- 중앙·cardinal edge의 AirKick·Dropkick 교차와 `DropkickRecovery`
- cardinal edge 최대 분산
- South recovery와 반대편 edge
- Crane Telegraph와 final crush
- Hook grab·sweep
- 인원별 pulse, 동시 2개 Incoming, landing→Loose와 cap skip
- Weapon fire·swing·drop
- 반복 down/Ragdoll·GetUp
- Character Patch01..08의 Jump·pulse·attack·Grab·Down 결과
- Weapon Patch09..12의 supply·CapacityLimited·forced drop 결과
- Pistol semi-auto recoil/projectile, LongGun full-auto spread bloom과 SpentPendingCleanup
- local Match menu와 reconnect grace 중 physical·vulnerable Character, Forfeit 뒤 Camera 제외와 OpponentLeft 전환
- 탈락 직후 Damping 재구도

Crane tower는 배경에 두고 foreground opaque cable/sheet를 만들지 않는다. South recovery facade는
필요할 때 dither할 수 있지만 Payload 자체를 투명화하지 않는다.

---

## 11. Alpha Weapon·Patch12 검증

W1 승인 뒤 P00에서 네 Weapon을 2·3·4인 모두 검증한다.

| Weapon | P00 검증 |
|---|---|
| Pistol / M1911-inspired | compact silhouette, press당 1발·total7, accurate/strong recoil, blocker/Character hit·Spent |
| LongGun / AK-47-inspired | two-hand reach·stock/barrel 방향, hold full-auto·total30, deterministic spread bloom·Spent |
| Bat / baseball bat | taper·swing envelope, edge knockback, Lever·Hook 오작동 없음 |
| Hammer / sledgehammer | head 방향·swing/impact, 큰 knockback, Pad·panel 끼임 없음 |

괄호 안 archetype은 제작 reference이고 Pistol·LongGun·Bat·Hammer도 internal/debug functional ID다.
일반 Player UI에는 Weapon 이름을 노출하지 않으며 P00 asset에는 logo·marking·serial과 exact real-world replica를 사용하지 않는다.

Weapon damage·knockback이 Crane direct lethal이나 OOB attribution을 덮어쓰지 않는다. Round reset 뒤
projectile, swing state, ammo·spread/recoil, Spent, ownership과 drop velocity가 남지 않는다. Ammo 7/30,
reserve/reload 0과 projectile 구조는 고정이고 cadence·recoil/spread 수치, damage·knockback은 Alpha tuning이다.

P00은 `PATCH_DESIGN.md` 0.5.0의 초기 `PATCH-PROT-001..012`를 검증한다.

| Patch | P00 기능 확인 |
|---|---|
| `PATCH-PROT-001` | 일반 Jump 수직 modifier가 roof 전투에서 동작하고 ClimbAssist·Recovery 높이를 우회하지 않는다. |
| `PATCH-PROT-002` | Jump pulse가 다른 생존 Character만 밀고 Lever·Hook·Crane·panel·Weapon·prop에는 힘을 주지 않는다. |
| `PATCH-PROT-003` | 확정 Attack의 victim knockback이 bounded이고 OOB·Crane attribution을 덮어쓰지 않는다. |
| `PATCH-PROT-004` | Attack action당 attacker recoil이 한 번이며 edge에서 중복 Collider로 증폭되지 않는다. |
| `PATCH-PROT-005` | Player Grab target의 throw resistance만 낮추고 실제 base mass·Collider와 map grab은 바꾸지 않는다. |
| `PATCH-PROT-006` | Player Grab relation의 grip만 강화하고 Ledge·Lever·Hook·Weapon GripStress/조작을 바꾸지 않는다. |
| `PATCH-PROT-007` | Down episode 동안 Ragdoll friction만 바뀌고 groggy duration·panel friction·Hazard surface는 그대로다. |
| `PATCH-PROT-008` | 비치명 Down episode당 bounce 한 번만 적용되고 Crane final lethal·OOB를 취소하지 않는다. |
| `PATCH-PROT-009` | 정규 pulse desired를 별도 Weapon Instance 2개로 바꾸되 capacity만 admission하고 제한분 backlog·retry 0이다. |
| `PATCH-PROT-010` | 정규 pulse 뒤 `START 6~10초` wave 1개를 예약하되 full·Playing 종료·stale Round에서 spawn 0이다. |
| `PATCH-PROT-011` | Weapon hit victim의 모든 Held Weapon Instance를 dedupe해 forced drop하고 hit·damage·knockback·동시 count는 유지한다. |
| `PATCH-PROT-012` | Weapon hit attacker가 사용한 정확한 source Weapon만 forced drop하고 다른 Weapon·supply count를 대신 바꾸지 않는다. |

`TRG-ATTACK-HIT-CONFIRMED`는 처음에는 권한 Punch·Kick·Dropkick hit로 시험한다. W1 뒤 Firearm·Melee가 같은
ownership·rate·hit·dedupe 검증을 통과하면 같은 source contract를 재사용하되 Patch는 Weapon damage,
fire/swing rate, ownership, drop과 reset을 우회하지 않는다.

`TRG-WEAPON-SUPPLY-SCHEDULED`는 정규 base pulse만 만들고 Patch09·10은 상호 배타다. Patch09의
capacity 1/0 결과는 각각 1/0 spawn과 `CapacityLimited`이며 base 상한을 올리지 않는다. Patch10 derived
wave는 Trigger를 다시 만들지 않고 actual admission만 shuffle cursor를 소비한다.

`TRG-WEAPON-HIT-CONFIRMED`는 Host가 승인한 Firearm/Melee Character hit만 만들고 Patch11·12는 상호
배타다. Patch11은 victim의 모든 Held instance를 놓되 같은 Main+Support Weapon을 한 번만 처리한다.
Patch12는 hit source instance의 owner relation이 이미 없으면 `NoEligibleTarget`이며 새 Weapon을 대신
놓지 않는다. 두 Effect는 기존 hit 결과 뒤 forced release→owner 해제→Loose 경로만 사용한다.

Character Patch01..08에서 actual Character size·Collider·base mass와 groggy duration을 바꾸는 경로는
0개다. 초기 Patch12 전체에서 Hazard phase·timing·strength, Crane/Hook/panel schedule, Drop Lever
threshold, Weapon damage·ammo·cadence와 supply 동시 상한을 바꾸는 경로는 0개다. P00의 모든 Hazard
control과 direct lethal 조건은 Patch가 없는 baseline과 동일하다.

각 조합은 2·3·4인에서 동일 tuning으로 중앙·edge·RecoveryBand·Lever·Crane·Hook·Sudden Death를 각각
검증한다. 무한 chain·영구 끼임·무한 안전·원격 activation과 모든 탈락 경로 무효화는 0건이어야 한다.
Supply/Patch 전용 최종 icon·Animation·VFX·SFX와 UI Layout은 P00 Alpha 기능 Gate에 필요하지 않다. Runtime은
후속 표현이 구독할 의미 event port만 제공하고 presentation object가 P00 권한 판정을 만들 수 없다.

---

## 12. Greybox hierarchy

```text
MAP_P00_CONSTRUCTION_DROP
├── GEO_STATIC / EDGE_PANELS
├── RECOVERY / OOB_VOLUMES
├── SPAWN_N/E/S/W
├── HZ_CRANE_DROP
├── HZ_SWING_HOOK
├── CTRL_DROP_LEVER
├── PROJECTILE_BLOCKERS
├── CAMERA
├── WEAPON_SUPPLY
│   ├── DROP_ZONES_SAFE
│   └── SUPPLY_PROFILE_2P_3P_4P
├── LIGHTING_GREYBOX
└── MAP_P00_DEFINITION
```

핵심 gameplay object와 문서 ID를 일치시킨다. 배경을 하나의 Blender Scene으로 가져오지 않고
Crane moving part와 EnvironmentKit module을 Prefab으로 나눠 Unity에서 조립한다.

Greybox 색은 임시 판독용이다.

- stable: 중립 회색
- transition: 흑백 방향 stripe
- grab: 청록+형태 marker
- displacement: Amber+chevron
- lethal telegraph: 고대비 shape+audio
- active lethal: 가장 높은 value contrast와 닫히는 형태

최종 palette가 아니며 색 하나만으로 상태를 구분하지 않는다.

---

## 13. 제작 Gate

### P00-A Static·Spawn·Camera

- 2·3·4인 동일 geometry
- 네 Spawn, 네 open edge
- 서로 분리된 safe Weapon DropZone 두 개 이상
- standing/Ragdoll safety
- 기본 이동·Sprint의 접촉·edge 시간
- AirKick·Dropkick의 중앙/edge 이동 envelope와 세 화면비 Camera framing
- 세 화면비 capture

### P00-B Recovery·OOB

- 네 방향 Lip·Facade·Beam 복구
- 유효 Grab 중 OOB 선행 제거 없음
- Beam 캠핑과 무한 ClimbAssist 없음
- 탈락과 Camera subject 제외 tick 일치
- DropkickRecovery의 non-Down·DownCount 0 증가와 action 중 정상 OOB 확인

### P00-C Hazard·Reset

- 손으로만 Drop Lever 작동
- Crane nonlethal side contact와 final lethal 구분
- Hook direct lethal 0
- AirKick·Dropkick hit/body/feet contact의 Drop Lever·Crane·Hook·panel 원격 activation 0
- Sudden Death 적용 전 Telegraph
- 반복 reset 뒤 stale DownCount·Sprint·Grab·Weapon supply timer/wave/instance·Hazard 0

### P00-D 2·3·4인 재미

각 인원수 최소 20 Round를 첫 목표로 한다.

- 최초 접촉 기본/Sprint 비교
- AirKick·Dropkick 적중·회피·edge risk와 DropkickRecovery 재피격/제어 복귀 기록
- 일반 Round 중앙값 30~45초 목표
- Sudden Death 후 종료 p95 30초 이하 목표
- OOB와 Crane lethal 반복 사용
- 처음 본 참가자의 Crane lethal 설명률 80% 이상 목표
- 무한 안전·도주·stunlock 0
- 2/3/4인의 정규 pulse·동시 상한과 Incoming→Loose, full skip·OOB 다음 pulse 보충 일치
- `PATCH-PROT-001..012`을 각각 켠 2·3·4인 Trigger·대상·capacity·forced drop·reset 기능 일치
- Pistol7·LongGun30 no-reload, swept projectile·blocker/Character hit·recoil/spread·Spent cleanup 일치
- Patch pulse·attack·Grab·Weapon supply/forced drop에 의한 Crane/Hook/Lever/panel 원격 제어 0

### P00-E P2P impairment

- 2·3·4인 Host+Guest
- RTT 0/60/120ms, loss 0/2/5% `START` matrix
- Hazard·Lever·Hook·Recovery·OOB·DownCount·Sprint·AirKick·Dropkick·DropkickRecovery·Weapon supply/shuffle/state·ammo/projectile/recoil/spread/Spent·Patch12·reset 수렴
- local non-pausing Esc, explicit Leave 즉시 Forfeit, unexpected disconnect 30초 physical·vulnerable grace와 reconnect/timeout 수렴
- permanent participant 2명 이상 continue·1명 OpponentLeft→Lobby와 Host Leave/Loss Session 종료 수렴
- 4인 cap3·LongGun 최대 승인 cadence·동시 projectile pool·SpentPendingCleanup worst-case Host tick/pool 측정

### P00-F Art

P00-A~E 뒤 StylePreflight를 통과해야 final asset 제작을 시작한다. Unity 6.3 LTS·Blender 5.2 LTS의
exact installed patch/package는 `FDN-010`, source/binary policy는 `FDN-011`, versioned LowPoly/Interop/QA
profile은 `ART-001`, 외부 asset·font·audio license/NOTICE는 `LIC-001` 결과를 사용한다. 제작된 각
Crane/module/background/warning/Weapon asset은 source→FBX→Unity StyleConsistencyGate와 2·3·4인 Camera
capture를 통과해야 Prefab과 P00 Art Lock에 포함된다.

Alpha Audio는 Crane/Hook Telegraph와 기본 combat·weapon·environment SFX만 요구하고 BGM event·asset은
0이다. Korean-only Player UI를 사용하되 P00 Hazard는 글자를 읽지 않아도 형태·방향·기본 SFX로 구분한다.
Production Lobby art·ambience와 music은 post-Alpha이며 P00 제품화 Gate의 대체 산출물이 아니다.

Supply/Patch-specific icon·Animation·VFX·SFX는 P00-F의 Alpha 필수 asset이 아니다. 후속 표현을 추가할 때는
Crane Telegraph, Hook 방향, Player·Weapon silhouette와 Recovery 판독을 가리지 않는지 별도로 검토한다.

---

## 14. 핵심 인수 조건

- [ ] 2·3·4인에서 geometry·Bounds·Hazard·Camera·Weapon catalog/state rule이 동일하고 supply profile만 승인값으로 다르다.
- [ ] Spawn은 standing/Ragdoll/static/OOB/Hazard 초기 contact가 없다.
- [ ] 네 방향에서 intentional ring-out과 가능한 ledge recovery가 성립한다.
- [ ] Drop Lever는 손 grab·pull로만 작동하고 `E`/UI remote path가 없다.
- [ ] Crane side contact는 직접 lethal이 아니며 final crush 조건만 lethal이다.
- [ ] Hook은 모든 phase에서 direct lethal이 아니다.
- [ ] Sudden Death panel은 warning 뒤 바깥으로 기울고 자체 lethal이 아니다.
- [ ] AirKick·Dropkick은 edge/OOB·Crane/Hook 조건을 우회하지 않고 DropkickRecovery는 DownCount를 증가시키지 않는다.
- [ ] kick hit·feet/body contact가 Drop Lever·Crane·Hook·panel을 원격 작동시키는 경우가 0건이다.
- [ ] projectile은 첫 solid blocker/Character hit에서 끝나고 pierce·ricochet·Crane/Hook/Lever/Hazard/prop impulse가 0이다.
- [ ] SuddenDeath에서도 기존 총기 fire·projectile hit가 정상 Hazard·OOB와 공존하고 새 supply만 0이다.
- [ ] 2/3/4인 pulse가 각각 10/22/cap2, 8/16/cap2, 6/12/cap3 `START` profile을 사용한다.
- [ ] Incoming·Loose·Held·SpentPendingCleanup을 함께 세고 full pulse skip·backlog 0, OOB/Spent 즉시 respawn 0이다.
- [ ] Incoming은 damage·Down·knockback·Grab·control/Hazard interaction 0이고 landing 뒤에만 Loose다.
- [ ] admission·landing clearance 실패는 NoSafeDropZone/LandingBlocked이며 활성 Collider·즉시 대체 0이다.
- [ ] WeaponCleanupBoundary는 회수 불가능한 Loose만 제거하고 유효 Held Collider 부분 진입은 제거하지 않는다.
- [ ] Playing 종료는 pending supply를 취소하고 Round reset은 timer·shuffle·Weapon·Grab·Hazard를 baseline으로 복원한다.
- [ ] Pistol press당 1발/total7, LongGun hold full-auto/total30, reserve/reload 0과 ammo0 Spent 2~4초·cap 포함·next pulse가 일치한다.
- [ ] 마지막 shot projectile은 source Spent 뒤에도 immutable attribution으로 유효하고 Patch12는 NoEligibleTarget/다른 Weapon drop0이다.
- [ ] `PATCH-PROT-001..012`의 Trigger·대상·modifier·capacity·one-shot guard가 2·3·4인에서 각각 동작한다.
- [ ] Patch가 Character actual size·Collider·base mass, groggy duration, Weapon damage·ammo·cadence와 P00 Hazard timing을 바꾸지 않는다.
- [ ] Patch pulse·attack·Grab·bounce·Weapon supply/forced drop이 Drop Lever, Hook, Crane과 panel을 원격 작동시키지 않는다.
- [ ] 2·3·4인과 세 화면비에서 Player·Incoming/Loose/Held/Spent Weapon·projectile·Telegraph·recovery가 읽힌다.
- [ ] 2·3·4인과 세 화면비에서 AirKick·Dropkick 방향과 DropkickRecovery가 Camera snap 없이 읽힌다.
- [ ] Target impairment에서 Host·Guest의 supply pulse·type·Zone·state·phase·hit·elimination·reset 결과가 수렴한다.
- [ ] 같은 Match의 여러 Round에서 P00이 유지되고 재추첨되지 않는다.
- [ ] Match Esc와 30초 disconnect grace가 P00 Simulation을 멈추거나 Character의 physical·vulnerable·Camera 상태를 끄지 않는다.
- [ ] explicit Leave/grace 만료 Forfeit가 PatchAuthor를 만들지 않으며 1명 잔존은 score·Patch 0 OpponentLeft→Lobby다.
- [ ] Host Leave/Loss가 Host Migration·P00 승계·Round score/Patch 없이 Session 종료로 끝난다.
- [ ] persistent Timer·Alive·Ammo HUD 없이 P00과 Weapon 방향이 읽히고 Ammo는 developer debug에만 있다.
- [ ] Alpha BGM event·asset은 0이고 기본 Crane/Hook·combat·weapon SFX가 color-only cue 없이 동작한다.

---

## 15. 미승인·Alpha tuning

- CharacterHeightMeters와 CU→meter
- Sprint multiplier를 반영한 roof·Spawn·Lever 접근 시간
- ClimbAssist·GripStress·Recovery 깊이
- Crane size·speed·gap·timing
- Drop Lever size·spring·angle·travel
- Hook size·period·impulse
- Sudden Death angle·cadence
- down Base/Increment/Cap과 P00 stunlock 영향
- AirKick·Dropkick 이동·impact·edge/OOB 비율과 DropkickRecovery 제어 복귀 체감
- Camera pitch·FOV·Dolly·focus
- Firearm cadence·projectile speed/SphereCast radius/TTL, Pistol recoil과 LongGun spread bloom
- SpentPendingCleanup 2~4초 시작값, Weapon damage와 knockback
- supply 첫 pulse·주기·상한의 최종값, safe DropZone exact 위치와 descent/landing 시간
- Character Patch01..08의 공통 2·3·4인 modifier tuning
- Weapon Patch09..12의 second-wave delay·capacity·forced drop 체감
- 후속 Supply/Patch icon·Animation·VFX·SFX와 P00 가독성
- 최종 asset, palette, lighting, audio와 성능 예산

각 값은 2·3·4인 측정표와 비교 capture를 남긴 뒤에만 `LOCKED`한다.
