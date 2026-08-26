# Project Hotfix 맵 디자인 가이드

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| 문서 버전 | 1.8.0 Alpha Map·Participation·Minimal UI·Firearm Compatibility Baseline |
| 최종 수정일 | 2026-08-26 |
| 상위 기준 | `01_PRD.md` 1.8.0, `02_SRS.md` 1.8.0, `PATCH_DESIGN.md` 0.5.0 |
| 첫 개별 사양 | `MAP_P00_CONSTRUCTION_DROP.md` 0.7.0 |
| 목적 | 2·3·4인 공통 맵의 치수, 공간, Hazard, Camera, reset과 검증 원칙 정의 |

이 문서는 결과와 설계 경계를 설명한다. 바이너리 codec, byte ordering, hash preimage와 반복 fixture
수는 구현·테스트 사양으로 분리하며 맵 디자이너의 작업 기준으로 사용하지 않는다.

### 0.1 1.8.0 변경 요약

- Match Esc가 Host Simulation을 멈추지 않고 local input만 neutralize하며, 해당 Character는 계속
  physical·vulnerable인 맵 경계를 추가했다.
- unexpected Guest disconnect의 30초 grace 동안 Character·Camera·slot을 유지하고, 명시적 Leave와
  grace 만료의 Forfeit는 PatchAuthor를 만들지 않게 했다.
- permanent participant가 한 명만 남으면 해당 Round score·Patch 없이 `OpponentLeft` 뒤 Lobby로,
  Host Leave/Loss는 Host Migration 없이 Session 종료로 수렴하게 했다.
- Persistent Match/Ammo HUD 없이 Telegraph가 읽혀야 하며 Ammo는 developer debug에만 표시하게 했다.
- Unity 6.3 LTS·Blender 5.2 LTS 계열, versioned style/import profile과 기본 environment SFX·BGM 0의
  Alpha 제작 경계를 명시했다.

### 0.2 1.7.0 변경 요약

- Pistol semi-auto 7발, LongGun hold full-auto 30발과 no reserve/reload 구조의 맵 경계를 추가했다.
- Host swept SphereCast projectile의 world blocker·Character hit·TTL/OOB/reset과 no pierce/ricochet를 추가했다.
- projectile·recoil이 Lever·Hazard·moving part·prop을 원격 작동하거나 impulse를 주지 못하게 했다.
- ammo 0의 `SpentPendingCleanup`을 cap에 포함하고 deadline 제거 뒤 다음 정규 pulse만 보충하게 했다.

### 0.3 1.6.0 변경 요약

- AirKick·Dropkick을 공통 Character action으로 받아 edge·OOB·Hazard·Camera 호환 경계를 추가했다.
- `DropkickRecovery`는 action recovery이며 Down/Ragdoll Episode와 DownCount를 만들지 않게 했다.
- AirKick·Dropkick impact가 physical control을 원격 작동하지 않고 2·3·4인 같은 맵에서 검증되게 했다.
- 승인 Weapon functional archetype을 맵 collider·Camera 판독 기준에 연결했다.

### 0.4 1.5.0 변경 요약

- `라운드당 무기 1개`를 폐기하고 2·3·4인별 주기·동시 상한을 가진 AuthorityHost supply profile을 추가했다.
- 결정적 4종 shuffle, map-authored safe DropZone, Incoming 무해 상태, cap skip·OOB 보충·Round reset을 고정했다.
- Character Patch01..08과 Weapon Patch09..12를 합친 초기 Patch12의 맵 호환 경계를 추가했다.
- Weapon supply·forced drop이 Hazard timing·control·Weapon damage를 바꾸지 못하게 하고 최종 UI·VFX·SFX를 후속으로 유지했다.

---

## 1. 맵의 제품 역할

맵은 배경이 아니라 상대를 제거하는 두 번째 무기다. 기본 이동·Sprint·Strike·Grab·Throw와 무기는
모든 맵에서 일관되게 동작하고, 고유 Hazard는 같은 행동에 새로운 위치 결정·구출·역이용 기회를 제공한다.

모든 공식 맵은 다음 목표를 가진다.

- Round 시작 후 짧은 시간 안에 2·3·4인이 접촉·견제를 시작한다.
- 기본 `OutOfBounds`와 고유 `LethalHazard`가 모두 실전 탈락 경로다.
- 하나 이상의 `DisplacementHazard`가 전투 위치를 바꾼다.
- 기믹이 혼자 승패를 만들지 않고 Player 행동을 증폭한다.
- Patch와 Weapon이 없어도 기본 라운드가 성립한다.
- 안전 면, 추락 경로, 치명 구역과 작동 방향을 한 번 관찰한 사람이 설명할 수 있다.

---

## 2. 치수와 상태

맵 치수는 `CharacterUnit`을 우선 사용한다.

```text
1 CU = 승인 MasterCharacter Authority collider의 서 있는 높이
WorldMeters = MapDimensionCU × CharacterHeightMeters
```

캐릭터 높이나 physics profile이 바뀌면 Scene Root를 임의 scale하지 않는다. Greybox를 다시 생성하고
Collider, reach, Jump, Sprint, ClimbAssist, Camera와 Hazard 이동을 함께 재검증한다. Blender asset은
`1 Blender meter = 1 Unity unit`과 승인 `ModelInteropProfile`을 사용한다.

Foundation은 Unity 6.3 LTS·Blender 5.2 LTS 계열을 사용한다. 실제 설치한 exact patch, Unity package
manifest/lock과 Blender version은 `FDN-010`의 `ToolchainProfile`로 고정하고 자동 upgrade하지 않는다.
`ART-001`이 owner·version을 가진 `LowPolyStyleProfile`, `ModelInteropProfile`, `AlphaVisualQAProfile`을
처음 생산한 뒤에만 최종 map asset source→Unity 비교를 수행한다. 외부 package·font·audio·asset은
`LIC-001`의 license/NOTICE inventory에 등록한다.

수치 상태:

| 상태 | 의미 |
|---|---|
| `DECIDED` | 제품 구조·행동 결정 |
| `START` | 첫 Greybox·tuning 시작값 |
| `MEASURE` | 테스트 중인 값 |
| `LOCKED` | 측정과 사용자 승인을 마친 profile 값 |

다음 캐릭터 값을 Map Lock 전에 기록한다.

- Character 높이와 standing/Ragdoll footprint
- 기본 이동과 Sprint 속도
- 한 손·양손 reach
- Jump 수직·수평 거리
- 일반·강한 knockback 이동 거리
- Ledge grab 최저 Root 높이와 ClimbAssist 상승량
- Weapon held/swing/fire, projectile sweep와 recoil 공간 envelope
- AirKick·Dropkick 이동·impact envelope와 `DropkickRecovery` 제어 복귀 거리
- Camera `ActiveSubjectBounds`

---

## 3. P2P Authority 경계와 맵 선택

방장 Unity 프로세스인 AuthorityHost가 실시간 맵 상태를 최종 판정하며 별도 Server 프로세스는 없다.

- 현재 party가 보유한 호환 맵 중 하나를 Match 시작 시 선택한다.
- 선택한 맵과 seed는 같은 Match의 모든 Round에서 유지한다.
- Host는 Hazard phase·transform·contact, physical control, OOB, elimination, reset을 판정한다.
- Guest는 입력을 보내고 Host 상태를 보간·표현한다.
- Host local player도 Guest와 같은 입력 검증을 거친다.
- 맵 호환성 또는 준비가 실패하면 Match를 시작하지 않고 Lobby 상태를 유지한다.
- Match Esc는 Host Simulation·Round clock·Hazard·Camera를 멈추지 않는다. 해당 local Player의 gameplay
  input만 neutral이고 Character는 다른 Player와 같은 physical·vulnerable 상태를 유지한다.
- Host Leave/Loss에서는 다른 Peer가 맵 Authority를 승계하지 않고 Session을 종료한다.

Lobby의 시작 UI와 역할은 UI/Session 문서가 소유한다. 본 맵 문서는 Lobby 시작 장치를 정의하지
않는다. 단, 경기 맵 안의 Lever·handle·door 같은 물리 장치는 아래 손 조작 계약을 따른다.

---

## 4. 2·3·4인 동일 맵 계약

2·3·4인은 다음을 완전히 공유한다.

- geometry, Collider와 moving part
- PlayBounds, RecoveryBand와 모든 OOB
- Lethal·Displacement Hazard의 수, 위치, timing과 strength profile
- physical control 위치와 작동 조건
- Camera profile·zone·bounds·occluder
- Sudden Death schedule
- Weapon catalog, safe DropZone geometry, shuffle·admission·Incoming/Loose/Held/SpentPendingCleanup state rule

인원수에 따라 플랫폼을 끄거나 난간·장벽·투명벽을 추가하거나 Camera 영역을 줄이지 않는다.
달라질 수 있는 것은 검증된 Spawn 4개 중 사용할 위치·Player 배정과 아래에서 승인한 Weapon supply
timing·동시 상한 profile뿐이다. Weapon type, DropZone pool, damage·combat와 map Hazard는 인원별로 바꾸지 않는다.

- 2인: 대향하는 공정한 pair
- 3인: 빈 slot과 불리한 위치가 Round마다 특정 Player에 고정되지 않게 회전
- 4인: 네 Spawn 모두 사용

Weapon supply의 60초 Playing `START` profile은 다음과 같다.

| 인원 | 첫 pulse | 이후 주기 | `Incoming+Loose+Held+SpentPendingCleanup` 동시 상한 |
|---:|---:|---:|---:|
| 2인 | 10초 | 22초 | 2 |
| 3인 | 8초 | 16초 | 2 |
| 4인 | 6초 | 12초 | 3 |

Host는 Round 초기 participating roster로 profile을 고정하고 Disconnect·Reconnect·Forfeit 변화는 다음
Round 초기화에서만 반영한다.

unexpected disconnect의 30초 grace와 local Match menu 동안에도 Character는 현재 Round의 physical
object이자 valid combat·OOB·Hazard target이다. 이 상태만으로 Spawn을 비활성화하거나 인원별 geometry,
Camera bounds 또는 현재 Round supply profile을 다시 고르지 않는다.

이 profile은 구조가 승인된 tuning 시작값이다. exact timing은 2·3·4인 측정 뒤 조정할 수 있지만
인원 차이를 제거하거나 `라운드당 1개` 규칙으로 되돌리는 것은 별도 범위 변경이다.

Spawn은 standing/Ragdoll 상태, 다른 Spawn, static geometry, OOB와 초기 Hazard contact가 없어야 한다.

---

## 5. 공간 구조

```text
StableCombatZone
→ TransitionRiskZone
→ RecoveryBand
→ OutOfBounds 또는 LethalHazard
```

### StableCombatZone

- Spawn 뒤 기본 전투와 무기 사용을 시작하는 중심 공간
- 즉시 탈락하지 않고 Strike·Grab·Throw를 시도할 여유
- 장시간 중앙 캠핑이 최적이 되지 않게 위험 구역과 연결

### TransitionRiskZone

- 상대를 추락 경계나 LethalHazard로 운반·밀어가는 공간
- 공격자도 반격받으면 같은 위험에 들어가는 양방향 구조
- Sprint가 즉시 OOB를 만드는 단순 직선 가속로가 되지 않게 측정

### RecoveryBand

- 실제 ledge·wall·beam을 잡아 제한된 `ClimbAssist`로 복귀
- 자동 복귀·공중 점프·보이지 않는 발판 없음
- GripStress와 높이·마찰로 무한 매달리기·캠핑 방지

### EliminationZone

- 명시적 OOB 또는 LethalHazard가 최종 판정을 만드는 공간
- Spawn과 즉시 겹치지 않고 최소 한 번의 대응 기회 제공

---

## 6. 탈락 경로와 복구

모든 공식 맵은 다음을 가진다.

1. `OutOfBounds` 경로
2. 고유 `LethalHazard`
3. `DisplacementHazard`

### OutOfBounds

- 발끝이 경계를 넘는 순간이나 보이지 않는 벽 접촉으로 탈락시키지 않는다.
- Authority core가 명시적 OOB volume에 들어간 Host tick에만 확정한다.
- 아직 Ledge를 잡을 수 있는 Alive Player를 Camera 편의로 조기 제거하지 않는다.
- 확정 tick에 Camera subject에서 제외하고 Damping으로 재구도한다.

### Ledge·ClimbAssist

- 승인된 grab surface와 Host contact가 필요하다.
- 최소 한 손으로 잡고 Jump했을 때만 Assist 후보다.
- impulse, 사용 횟수와 cooldown은 map/physics profile의 `START` 값으로 비교한다.
- 벽 전체 무한 climb, 공중 자유 비행과 OOB가 유효 Grab을 먼저 자르는 배치를 허용하지 않는다.

### LethalHazard

- Telegraph와 Escape window를 먼저 제공한다.
- `ActiveLethal`과 Host의 유효 contact 조건이 함께 성립할 때만 직접 탈락한다.
- 구출·회피·역이용이 가능하고 공격자도 위험을 받을 수 있다.

### DisplacementHazard

- 직접 탈락시키지 않고 위치·속도·GripStress를 바꾼다.
- 최종 탈락은 OOB 또는 다른 Lethal 조건으로 기록한다.
- Lethal보다 낮은 경고 강도와 명확한 이동 방향을 사용한다.

### AirKick·Dropkick 맵 경계

AirKick·Dropkick의 입력, 상태, impulse와 hit 규칙은 Character/Combat 상위 문서가 소유한다. Map은
action별 별도 geometry나 보이지 않는 보호벽을 만들지 않고 다음 결과만 보장한다.

- AirKick·Dropkick 중에도 Authority core가 OOB에 들어가면 기존 tick에 정상 탈락하며 action 전용 OOB 면역·복귀 teleport는 없다.
- LethalHazard의 phase·contact 조건은 action 여부와 무관하다. Dropkick이 valid lethal contact를 만들면 기존 Hazard가 판정한다.
- Character끼리의 승인 kick impact·knockback은 OOB·Hazard로 이어질 수 있지만 action이 직접 elimination이나 Hazard phase를 선언하지 않는다.
- `DropkickRecovery`는 발차기 뒤 제어 복귀 상태이며 Down, Stun, Ragdoll Episode 또는 groggy가 아니다.
  진입 자체로 DownCount를 증가시키거나 Camera subject에서 제외하지 않는다.
- SharedGameplayCamera는 기존 `ActiveSubjectBounds`와 action velocity를 사용한다. 뻗은 발·Ragdoll limb를
  새 subject bounds로 쓰거나 개인 Camera·action 전용 snap/zoom을 만들지 않는다.
- AirKick·Dropkick hit, feet/body contact와 action impulse는 Lever·handle·door의 유효 hand relation 또는
  control travel을 대신하지 못하며 map physical control을 원격 activation하지 않는다.
- action 중 실제 손 Grab relation을 별도로 유지했다면 손과 handle의 기존 contact·travel만 평가하고 kick hit를 control input으로 합성하지 않는다.
- 2·3·4인은 동일한 action·OOB·Hazard·Camera·control 규칙을 사용하고 인원별 kick strength나 action 전용 map 보정을 두지 않는다.

---

## 7. Hazard와 물리 장치

기본 phase는 다음과 같다.

```text
Idle → Telegraph → ActiveNonLethal → ActiveLethal(Lethal만) → Recovery
```

맵 안의 Lever, crank, pull handle과 door는 실제 손 contact·grab·pull·push로만 작동한다.

- `E`나 proximity 버튼으로 원격 성공시키지 않는다.
- 실제 pivot, axis, travel, threshold와 return을 MapSpec에 기록한다.
- Host가 손 relation과 control travel을 판정한다.
- physical activation이 Telegraph·Escape window를 생략하지 않는다.
- 유효한 hand relation 없는 prop 충돌·Strike·간접 impulse만으로 activation을 만들지 않는다.
- 유효한 hand relation을 유지한 Character가 외부 힘으로 움직여 handle이 실제 travel하면 기존
  physical control 규칙으로 판정한다.

Lobby 시작 장치와 이 계약을 혼동하지 않는다. 본 절은 경기 맵의 gameplay control만 다룬다.

---

## 8. Round 흐름·non-pausing Match menu·Leave·Sprint·Down reset

Round 흐름은 Countdown, Opening, Standard, Escalation, Sudden Death, Result로 진행한다.
Sudden Death는 새 규칙을 갑자기 추가하지 않고 이미 보이던 surface와 Hazard를 강화한다.

### 8.1 Match Esc·Guest Leave·disconnect·Forfeit

- Match Esc는 local-only menu다. 해당 Client의 gameplay input만 neutralize하고 Host Simulation, Round timer,
  Hazard, Weapon, projectile과 다른 Player 입력은 계속 처리한다. 닫을 때 Mouse all-up 뒤 Hand를 재무장한다.
- local Match menu를 연 Character는 제자리 무적·비충돌·Camera 제외 상태가 되지 않는다. 공격, OOB,
  Lethal/DisplacementHazard와 정상 physical contact에 계속 취약하다.
- 명시적 Guest Leave는 즉시 Forfeit다. unexpected disconnect는 30초 동안 neutral input으로 같은
  physical·vulnerable Character, Camera subject와 slot을 유지하고 reconnect가 실패하거나 grace가 만료되면 Forfeit다.
- Disconnect 자체는 탈락이 아니다. grace 중 Host가 실제 Attack·OOB·Hazard 탈락 조건을 확정하면 기존
  elimination을 사용한다. Forfeit transition 자체는 elimination·score·Patch Trigger·PatchAuthor를 만들지 않는다.
- Forfeit 뒤 permanent participant가 2명 이상이면 같은 geometry·Hazard와 Round 시작 시 고정한 supply
  profile로 현재 Round를 계속한다. 다음 Round만 새 roster로 profile과 Spawn 배정을 선택한다.
- permanent participant가 한 명만 남으면 진행 중 Round를 중단하고 score·PatchAuthor·Patch 적용을 0으로
  유지한 채 `OpponentLeft`를 표시한 뒤 Lobby로 복귀한다.
- Host Leave/Loss는 Round Result가 아니라 Session 종료다. Host Migration, map state 승계와 새 score·Patch는 0이다.

### 8.2 Round reset

다음 Round 전에 Host가 복원한다.

- Character Spawn·rotation·velocity·Alive
- Stun·Ragdoll·Recovering·impact와 harmful status
- AirKick·Dropkick active hit·velocity residue와 `DropkickRecovery`
- `DownCount=0`과 적용 중인 down/groggy duration 제거
- Sprint request와 이동 modifier를 기본 상태로 정리
- 두 손 intent·contact·Grab relation·GripStress
- Weapon supply timer·pending derived wave·shuffle cursor와 Incoming/Loose/Held/SpentPendingCleanup Weapon
- projectile·TTL·recoil/spread, Weapon owner/combat residue와 round-only prop
- Hazard phase·moving part·control·Sudden Death surface
- ClimbAssist count·cooldown과 contact cache
- local Match menu·Forfeit의 stale input, pending relation과 participant-only cache

현재 Round의 PatchModifier·발동 cache도 함께 제거한 뒤 유지된 active Patch를 깨끗한 map/character
baseline 위에 정해진 순서로 다시 등록한다. Patch는 score·selected map·Match seed와 함께 유지되지만
이전 Round의 modifier instance와 지연 event는 유지되지 않는다.

아직 만료되지 않은 reconnect grace의 deadline·slot reservation은 Session state이므로 정상 Round reset이
취소하지 않는다. 다음 Round가 먼저 시작되면 해당 Character를 새 Spawn의 neutral input·physical·vulnerable
상태로 복원하고 reconnect 때 Host의 현재 Alive/Spectator state만 전달한다.

유지하는 것은 score, selected map, Match seed와 active Patch다. 같은 Match 안에서 Round마다 맵을
재추첨하거나 Scene을 바꾸지 않는다.

---

## 9. SharedGameplayCamera

모든 참가자와 관전자는 Host가 계산한 한 개의 Camera를 공유한다.

- 개인 회전·Zoom·Split Screen 없음
- Authority core에 붙은 고정 `ActiveSubjectBounds` 사용
- Ragdoll limb, Cosmetic, Weapon과 Guest prediction을 subject bounds로 사용하지 않음
- Incoming Weapon도 Camera subject로 사용하지 않고 safe DropZone과 landing 결과만 기존 framing 안에서 읽히게 함
- Alive recovery Player 포함, 실제 탈락 tick에 제외
- local Match menu 또는 reconnect grace의 Alive Character는 Camera subject와 physical·vulnerable 상태를 유지하고,
  실제 탈락 또는 Forfeit transition에만 제외
- fixed vertical FOV, 지원 화면비에서 가로 확장
- foreground occluder는 제거·fade·dither하되 Hazard 자체를 숨기지 않음

Player-facing persistent Match HUD와 Ammo HUD는 0이다. Camera와 map Telegraph는 상시 Timer·Alive·Ammo에
의존하지 않고 읽혀야 한다. score·active Patch는 on-demand Tab에서만, Ammo·projectile 진단은
developer-only debug에서만 표시하며 debug surface는 Camera framing 승인 근거로 사용하지 않는다.

2·3·4인 각각 다음을 검증한다.

- 중앙 전투와 최대 분산
- Sprint 추격·교차와 edge 접근
- AirKick·Dropkick의 중앙 교차, edge/OOB 접근과 `DropkickRecovery` 중 최대 분산
- RecoveryBand Player와 반대편 전투 Player
- Lethal Telegraph와 Weapon fire/swing
- Pistol/LongGun projectile blocker·Character hit와 SuddenDeath fire
- Weapon Incoming→Loose와 동시 2개 supply
- down/Ragdoll과 GetUp
- 한 명 생존까지의 Damping 재구도
- local Match menu와 disconnect grace 중 physical·vulnerable Character를 포함한 framing, 실제 Forfeit 뒤 제외
- 16:9, 16:10, 21:9의 Min/Max Dolly와 HUD safe area

---

## 10. Weapon supply·Patch12·동적 오브젝트

W1 승인 뒤 Alpha 맵은 Pistol(M1911-inspired), LongGun(AK-47-inspired), Bat(baseball bat),
Hammer(sledgehammer)의 기능 silhouette와 실제 전투를 2·3·4인에서 검증한다. 괄호 안 reference는
제작용이며 사용자-facing 이름, logo·marking·serial과 exact replica를 맵에 표시하지 않는다.

- fire·swept SphereCast projectile path가 map blocker와 Character collider를 올바르게 읽음
- Bat/Hammer swing envelope가 좁은 지형에서 부당하게 막히거나 벽을 통과하지 않음
- damage·knockback이 OOB·Hazard attribution과 충돌하지 않음
- drop/reacquire와 Round reset이 승인 supply·cleanup rule을 사용
- ammo 7/30과 reserve/reload 0은 고정이며 cadence·damage·knockback은 Alpha tuning 값

### Firearm projectile·recoil·Spent 맵 경계

- Pistol은 Host-accepted press edge당 1발·total7의 semi-auto, LongGun은 valid hold 동안 total30의
  full-auto다. reserve·reload는 없으며 Map이 ammo를 추가하거나 reload pickup을 만들지 않는다.
- Host projectile은 이전→현재 위치의 swept SphereCast를 사용한다. authored static/moving world blocker와
  Character collider 중 첫 유효 hit 하나에서 종료하고 pierce·ricochet·gravity는 0이다.
- blocker에 맞은 projectile은 Character Damage·Knockback을 만들지 않는다. Character hit만 기존 Combat
  attribution을 사용하고 projectile이 OOB·TTL·RoundResult·Round reset에 도달하면 즉시 제거한다.
- projectile, recoil physics와 visual recoil은 Lever·handle·Crane·Hook·panel·Hazard phase, prop과 다른
  Weapon을 원격 activation하거나 physics impulse로 움직이지 않는다.
- Map의 projectile blocker는 보이는 큰 static/moving mass와 일치하고 얇은 배경 장식·visual cable이
  보이지 않는 shot blocker가 되지 않는다.
- ammo 0의 Weapon은 마지막 shot 뒤 Host ForcedRelease로 `SpentPendingCleanup`이 된다. `START 2~4초`
  동안 cap에는 포함되지만 Collider·pickup·Grab·hit·map/Hazard interaction은 0이고 deadline/reset에 제거된다.
- Spent object 자체는 contact·Trigger 0이지만 마지막 shot의 이미 생성된 projectile은 immutable attacker/source
  snapshot으로 hit할 수 있다. source가 Spent/Removed면 Patch12 forced drop은 `NoEligibleTarget`이고 다른 Weapon을 대신 놓지 않는다.
- Spent 제거와 projectile OOB/TTL은 즉시 supply를 만들지 않는다. 비워진 capacity는 다음 정규 pulse만 사용한다.
- Pistol의 accurate/strong per-shot recoil과 LongGun의 deterministic cumulative spread bloom은 Host profile을
  사용하며 Camera·Map이 spread나 recoil 결과를 다시 계산하지 않는다.
- SuddenDeath는 새 supply만 중단한다. 이미 존재하는 총기의 fire와 projectile hit는 계속 유효하며 기존
  Hazard·OOB 판정과 정상 공존한다.
- 2·3·4인은 같은 projectile blocker·hit·Spent 규칙을 사용하고 인원별로 달라지는 것은 승인 supply profile뿐이다.

MapSpec은 안정적인 ID를 가진 safe DropZone pool을 제작한다.

- Player Spawn, OOB·RecoveryBand, Lethal/DisplacementHazard, physical control과 moving part sweep에서 분리
- `PATCH-PROT-009`의 동시 두 개 admission을 위해 서로 겹치지 않는 유효 Zone 두 개 이상 제공
- 각 Zone은 Character·Weapon·moving part 동적 겹침을 검사할 `LandingClearance`를 제공하고 Host는
  admission과 landing에서 각각 다시 검증
- Host가 Round·pulse·spawn ordinal의 결정적 순서로 Zone을 선택하고 Guest는 spawn을 주장하지 않음
- Incoming은 Character damage·Down·knockback·Grab/pickup, 다른 Weapon collision과 map control·Hazard interaction 0
- landing Host transition 뒤 Loose가 되고 그때부터 정상 Weapon physics·pickup·OOB cleanup 적용
- MapSpec은 reachable recovery surface와 분리된 Host `WeaponCleanupBoundary`를 제공한다. 회수 불가능한
  Loose Weapon만 제거하고 유효 Held owner의 긴 Collider 일부가 경계를 넘은 경우는 제거하지 않는다.
- admission 유효 Zone 0은 `NoSafeDropZone`, bounded landing clearance 실패는 `LandingBlocked`로 spawn 또는
  Incoming을 정리하며 활성 Collider·피해·backlog·즉시 재투하 0
- Weapon cleanup은 즉시 재투하하지 않고 다음 정규 pulse의 남은 capacity admission으로만 보충

정규 pulse는 baseline desired 1을 동시 상한까지 admission한다. Incoming·Loose·Held·SpentPendingCleanup을 모두 세며 full이면
`CapacityLimited` 0개, backlog·retry 0이다. Host의 결정적 4종 shuffle은 실제 admission만 소비한다.
Playing→SuddenDeath/Result는 새 pulse·pending wave를 취소하고 Round reset은 timer·shuffle·Weapon·owner·
projectile을 전부 제거한다.

첫 맵 Gate가 지원하는 초기 Catalog는 `PATCH_DESIGN.md` 0.5.0의 `PATCH-PROT-001..012`다.

| Patch | 맵 호환성 경계 |
|---|---|
| `PATCH-PROT-001` | 일반 Jump 수직 modifier만 사용하고 `ClimbAssist`·강제 launch를 키우지 않는다. |
| `PATCH-PROT-002` | 다른 생존 Character만 밀며 prop·Weapon·Lever·moving part·Hazard에는 impulse를 주지 않는다. |
| `PATCH-PROT-003..004` | 권한 `TRG-ATTACK-HIT-CONFIRMED`의 Character knockback/recoil만 처리하고 map control을 작동시키지 않는다. |
| `PATCH-PROT-005..006` | Player-to-Player Grab의 throw resistance/grip만 바꾸며 Ledge·Lever·prop·Weapon Grab은 그대로다. |
| `PATCH-PROT-007..008` | 비치명 Down episode의 friction/one-bounce만 처리하고 OOB·Lethal 판정과 groggy 시간을 바꾸지 않는다. |
| `PATCH-PROT-009` | 정규 Weapon pulse desired batch를 별도 Weapon Instance 2개로 만들되 base 동시 상한까지 admission하고 Hazard timing을 바꾸지 않는다. |
| `PATCH-PROT-010` | 정규 pulse 뒤 `START 6~10초` derived wave 1개를 예약하되 Playing 종료·full에서 취소/skip하고 재귀 Trigger를 만들지 않는다. |
| `PATCH-PROT-011` | 유효 Weapon hit victim의 Held Weapon을 기존 forced drop으로 놓게 하며 Character hit 결과·map control·동시 count를 바꾸지 않는다. |
| `PATCH-PROT-012` | 유효 Weapon hit attacker의 정확한 source Weapon만 forced drop하고 다른 Weapon·map object·supply count를 대신 바꾸지 않는다. |

Character Patch01..08은 actual Character scale·Collider·base mass를 바꾸지 않는다. 초기 Patch12
전체는 Weapon damage·ammo·cadence, Hazard phase·timing·strength, physical control threshold와
SuddenDeath schedule을 수정하지 않는다. Patch impulse·supply·forced drop은 map control을 원격
activation하거나 direct lethal condition을 새로 만들 수 없다. Patch09·10, Patch11·12는 각각 같은
Trigger를 공유해 상호 배타이며 derived spawn은 supply Trigger를 다시 만들지 않는다.

모든 조합은 공통 geometry·Hazard·Camera와 승인된 인원별 supply profile로 2·3·4인을 각각 검증한다.

Patch는 탈락 경로를 모두 무효화하거나 무한 chain·영구 끼임·무한 안전 상태를 만들 수 없다.
동적 오브젝트는 gameplay purpose가 명확해야 하며 배경 debris에 불필요한 Rigidbody를 대량 사용하지 않는다.
Patch-specific icon·Animation·VFX·SFX와 최종 UI Layout은 이 Alpha map 기능 Gate의 필수 산출물이
아니며, Runtime 의미 presentation port가 map authority를 변경해서도 안 된다.

---

## 11. 제작 순서와 Art Gate

```text
CU 기반 Static Layout
→ Spawn·Recovery·OOB
→ Shared Camera
→ LethalHazard와 map physical control
→ DisplacementHazard
→ Escalation·Sudden Death·Round reset
→ 2·3·4인 기본 이동·Sprint·AirKick·Dropkick·down 검증
→ safe DropZone·인원별 supply·cap/OOB/reset 검증
→ Character Patch01..08 기능 검증
→ W1 뒤 Weapon source와 Weapon Patch09..12 검증
→ P2P impairment
→ Style Preflight
→ 최종 asset 제작과 post-import StyleConsistencyGate
```

Primitive와 단색 Material로 재미·공정성·판독성을 통과하기 전 최종 환경 asset을 양산하지 않는다.
최종 asset은 locked ToolchainProfile·art profile과 license inventory를 사용한다. Alpha Audio는 semantic
event에 연결된 기본 combat·weapon·environment SFX만 요구하고 BGM event·asset은 0이다. Production Lobby
art·ambience와 music은 post-Alpha이며, P00 환경 제품화 Gate를 대신하거나 지연시키는 범위로 해석하지 않는다.

---

## 12. 공통 승인 Gate

### 구조

- [ ] 2·3·4인의 geometry·Collider·Bounds·Hazard·Camera가 동일하다.
- [ ] 정확히 네 Spawn이 있고 active Spawn의 안전거리와 초기 contact가 유효하다.
- [ ] OOB, LethalHazard와 DisplacementHazard가 있다.
- [ ] 맵 physical control에 `E` remote path가 없다.
- [ ] safe DropZone 두 개 이상이 Spawn·OOB·Recovery·Hazard·control·moving part와 겹치지 않는다.
- [ ] 보이는 gameplay mass와 일치하는 projectile blocker가 있고 visual-only 장식의 false blocker가 없다.
- [ ] 각 DropZone의 LandingClearance와 회수 불가능한 Loose Weapon용 WeaponCleanupBoundary가 있으며
  Held Collider 부분 진입 오탐 제거가 없다.
- [ ] Round reset 뒤 Character·DownCount·Sprint·Weapon supply timer·instance·Hazard baseline이 복원된다.
- [ ] Round reset 뒤 AirKick·Dropkick·DropkickRecovery와 action hit cache가 남지 않는다.
- [ ] local Match menu·disconnect grace가 Character collider·vulnerability·Camera·Hazard를 끄지 않는다.

### 플레이

- [ ] 기본 이동과 Sprint 모두에서 전투 밀도와 edge 위험이 의도대로다.
- [ ] AirKick·Dropkick이 OOB·Lethal 규칙을 우회하지 않고 DropkickRecovery가 DownCount를 증가시키지 않는다.
- [ ] kick hit·body/feet contact가 map physical control을 원격 작동시키는 경우가 0건이다.
- [ ] 2·3·4인에서 특정 Spawn·control·Weapon이 지속 우위를 만들지 않는다.
- [ ] OOB와 고유 Lethal이 실제 탈락 경로로 사용된다.
- [ ] 무한 도주·매달리기·stunlock·control 독점이 없다.
- [ ] Sudden Death가 이미 배운 규칙으로 교착을 끝낸다.
- [ ] explicit Leave 즉시/abnormal disconnect 30초 뒤 Forfeit가 PatchAuthor를 만들지 않고, permanent
  participant 1명 잔존 시 score·Patch 없이 OpponentLeft→Lobby로 끝난다.
- [ ] Host Leave/Loss가 Host Migration·map 승계·Round score/Patch 없이 Session 종료로 끝난다.
- [ ] 2/3/4인의 첫 pulse·주기·동시 상한이 승인 profile과 일치하고 full skip·no backlog가 성립한다.
- [ ] Incoming은 착지 전 damage·Down·knockback·Grab·map interaction 0이며 착지 뒤에만 Loose다.
- [ ] Pistol press당 1발/7발, LongGun hold full-auto/30발과 no reserve/reload가 맵에 따라 바뀌지 않는다.
- [ ] projectile은 첫 blocker/Character hit에서 종료하고 pierce·ricochet·Lever/Hazard/prop remote impulse가 0이다.
- [ ] SuddenDeath에서도 기존 총기 fire·projectile hit가 정상 Hazard·OOB와 공존하고 새 supply만 0이다.
- [ ] ammo0 SpentPendingCleanup은 cap에 포함되고 2~4초/Reset 제거 뒤 다음 pulse 전 즉시 보충이 0이다.
- [ ] 동적 clearance가 없으면 NoSafeDropZone/LandingBlocked로 안전하게 끝나고 backlog·즉시 대체 0이다.
- [ ] Weapon OOB 뒤 즉시 respawn 0, 다음 정규 pulse capacity admission만 사용한다.
- [ ] `PATCH-PROT-001..012` 각각에서 OOB·Lethal·Displacement 경로가 계속 성립한다.
- [ ] Patch pulse·attack·Grab이 map physical control을 원격 작동시키는 경우가 0건이다.
- [ ] Patch supply·forced drop이 동시 상한·Hazard timing·phase·strength와 Weapon damage·ammo·cadence를 바꾸지 않는다.

### Camera·네트워크

- [ ] 2·3·4인과 세 화면비에서 Player·Incoming·Loose/Held/Spent Weapon·Telegraph가 읽힌다.
- [ ] 2·3·4인과 세 화면비에서 AirKick·Dropkick 방향과 DropkickRecovery가 Camera snap 없이 읽힌다.
- [ ] Target network impairment에서 supply pulse·shuffle·admission·state·phase·elimination·reset 결과가 Peer 간 수렴한다.
- [ ] 2·3·4인 projectile·ammo·recoil/spread·Spent state와 blocker/Character hit 결과가 Peer 간 수렴한다.
- [ ] 4인 cap3, LongGun 최대 승인 cadence, 동시 projectile pool·SpentPendingCleanup worst case를 측정한다.
- [ ] Host와 Guest가 같은 map state와 elimination 원인을 표시한다.
- [ ] Player-facing persistent Timer·Alive·Ammo HUD 없이 Hazard·OOB·Weapon 방향을 판독하고 Ammo는 debug에서만 확인한다.
- [ ] Alpha BGM은 0이고 기본 environment/combat/weapon SFX가 의미 event와 중복 없이 동작한다.

---

## 13. 미승인·Alpha tuning 항목

- CharacterHeightMeters와 CU→meter
- Sprint multiplier를 반영한 이동 시간 목표
- RecoveryBand·ClimbAssist·GripStress
- Hazard timing·impulse·Escape window
- Down duration profile과 map stunlock 영향
- AirKick·Dropkick 이동·impact envelope와 edge/OOB 비율, DropkickRecovery 중 재피격·제어 복귀 체감
- Camera pitch·FOV·Dolly·focus bounds
- Weapon damage·knockback과 firearm cadence
- Firearm projectile speed·SphereCast radius·TTL, Pistol recoil과 LongGun cadence·spread bloom
- SpentPendingCleanup 2~4초 시작값과 supply cap density
- 승인된 인원별 supply 첫 pulse·주기·동시 상한의 최종 tuning과 map별 DropZone·landing 시간
- Character Patch01..08 modifier의 공통 2·3·4인 tuning
- Weapon Patch09..12 second-wave delay·capacity·forced-drop 체감
- 후속 Patch icon·Animation·VFX·SFX와 map 연출 방향
- 최종 LowPoly asset, palette, lighting과 성능 예산

각 값은 2·3·4인 측정과 사용자 검토 전에는 `LOCKED`가 아니다.
