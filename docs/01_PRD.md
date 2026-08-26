# Project Hotfix 제품 요구사항 문서 (PRD)

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| 문서명 | Product Requirements Document |
| 프로젝트명 | `Project Hotfix` (가칭) |
| 문서 버전 | 1.8.0 Product Baseline |
| 기준일 | 2026-08-26 |
| 개발 형태 | 솔로 개발, 고정 출시일 없음 |
| 현재 목표 | `Alpha = Vertical Slice` |
| 후속 문서 | `docs/02_SRS.md`, `docs/03_IMPLEMENTATION_PLAN.md` |
| 연계 문서 | `docs/PATCH_DESIGN.md`, `docs/UI_UX_FLOW.md`, `docs/CHARACTER_TECHNICAL_SPEC.md`, `docs/MAP_DESIGN_GUIDE.md`, `docs/WEAPON_DESIGN.md` |

이 문서는 제품이 무엇인지, Alpha에서 무엇을 완성하는지, 무엇을 만들지 않는지를 정하는
최상위 기준선이다. 하위 문서는 이 범위를 구체화할 수 있지만 다른 제품을 만들 수 없다.

### 0.1 1.8.0 개정 요약

- Match의 persistent HUD를 제거하고 transient `3·2·1`, Round 사이 평문 Patch 선택·결과,
  `OpponentLeft`·`HostLoss`·오류와 on-demand Tab만 Player UI로 허용했다.
- Match `Esc`를 simulation을 멈추지 않는 local-only menu로 고정하고 local input neutral·Mouse all-up
  재무장 경계를 추가했다.
- 명시적 Guest Leave, 예상치 못한 30초 Disconnect, timeout Forfeit, 남은 참가자 수와 Lobby 복귀 규칙을 고정했다.
- Alpha 품질 범위를 Greybox Lobby, 최소 placeholder Cosmetic, 한국어 단일 언어, BGM 0과 기본
  combat·weapon·environment SFX로 제한했다.

### 0.2 1.7.0 개정 요약

- Pistol은 press당 semi-auto 한 발·total7, LongGun은 hold full-auto·total30이며 reserve/reload는 0으로 확정했다.
- Ammo 0은 Host forced release→`SpentPendingCleanup 2~4초`→deadline remove이며 cap을 차지하고 replacement는 다음 Pulse만 사용한다.
- Host visible Projectile의 fixed-step swept SphereCast, first-hit·no-pierce/ricochet·gravity0 START·TTL/OOB/Result/reset을 확정했다.
- Pistol narrow spread·strong single recoil, LongGun deterministic `RecoilAccumulator/SpreadBloom`과 Host Unity physics+read-only recoil pose를 확정했다.

### 0.3 1.6.0 개정 요약

- Ground L/R tap은 해당 손 Punch, hold는 기존 Grab으로 유지하고 Airborne tap은 좌우 발 Kick으로 분기했다.
- Airborne 양 button chord의 Dropkick, episode당 `AirAttackToken` 1개, bounded recovery와 no-Down 경계를 확정했다.
- Kick·Dropkick을 Host-confirmed Attack/Patch source로 연결하고 Animation·root motion을 read-only 표현으로 분리했다.
- W1은 승인 Air attack mapping을 덮어쓰지 않으며 airborne WeaponUse 여부·별도 입력만 후속 결정하게 했다.
- 무기 기능 ID는 유지하면서 M1911-inspired Pistol, AK-47-inspired LongGun, baseball Bat와 construction
  sledgehammer의 brand-free low-poly reference role을 고정했다.

### 0.4 1.5.0 개정 요약

- 라운드당 무기 한 개만 공급하는 안을 폐기하고, 인원별 첫 Pulse·반복 간격·동시 존재 cap을 가진
  Host 권한 반복 무기 보급을 Alpha 기준으로 확정했다.
- 초기 승인 Catalog를 `PATCH-PROT-001..012`로 확장하고 무기 보급 2개와 Host-confirmed Weapon Hit
  강제 Drop 2개를 추가했다.
- supply cap은 `Incoming + Loose + Held`를 모두 세며, cap 부족·OOB·SuddenDeath·Round reset의
  backlog 없는 처리와 결정적 Weapon bag·Safe DropZone 경계를 고정했다.
- Alpha 패치 UI는 계속 평문 기능 화면만 사용하고 Guest `ReadyTeal + check + 상태 문구` 계약을 유지한다.

### 0.5 1.4.0 개정 요약

- 사용자 노출 명칭을 `패치`로 고정하고 당시 Character 후보 `PATCH-PROT-001..008`의 상세 source를
  `PATCH_DESIGN.md`로 분리했다. 현행 초기 Catalog 범위는 1.5.0의 Patch12가 대체한다.
- Alpha Patch 화면은 평문 2×2 선택·남은 시간·확정 결과·활성 목록과 실제 적용 검증으로 제한했다.
- 최종 icon·animation·VFX·SFX·layout은 후속으로 미루고 runtime의 semantic event/read-model 경계만 Alpha core에 둔다.

### 0.6 1.3.0 개정 요약

- Alpha 연결을 별도 계정·Party Backend 없이 LAN/direct endpoint 기반 2·3·4인 Host-authoritative 세션으로 단순화했다.
- Steam 인증·친구 초대·초대 코드·P2P/SDR는 Alpha 이후 G4 제품 통합으로 이동했다.
- Coordinator, 별도 Backend, DB, blob store, bake worker와 Dedicated Server를 제품 구성에서 제거했다.
- Host Ready와 Lobby 물리 StartLever를 제거하고, Host의 고정 action slot에 처음부터 Start Button을 배치했다.
- Guest Ready와 Host Start 조건, Lobby Cursor 복귀, Sprint, 반복 Down/Ragdoll groggy 누적을 확정했다.
- 외형 Preset을 local atomic save로, 외형 공유를 Host 검증 뒤 P2P relay로 변경했다.
- 실제 총기·근접 무기 전투와 W1 입력 결정을 Alpha 범위에 포함했다.
- 공개 매치메이킹, 랭크, MMR을 현재·향후 범위에서 제거했다.

---

## 1. 제품 정의

### 1.1 한 줄 설명

2~4명의 친구가 좌우 손을 각각 조작해 서로를 붙잡고 던지며, 맵 밖이나 맵 고유 치명
기믹으로 탈락시키는 3D 온라인 물리 난투 게임이다. 라운드의 패자는 다음 라운드부터
모두에게 적용되는 전역 패치를 만들어 익숙한 싸움의 규칙을 바꾼다.

### 1.2 제품 비전

친구가 만든 한 문장 때문에 익숙했던 싸움이 즉시 망가지고, 그 원인과 결과를 모두가
이해하며 웃을 수 있는 짧고 반복 가능한 물리 파티게임을 만든다.

### 1.3 핵심 재미

1. **기본 난투**: 이동, 점프, Sprint, 양손 Punch·Grab, 공중 Kick·Dropkick, 들기·던지기와 무기 사용만으로 재미가 난다.
2. **환경 활용**: 링아웃과 맵 내부의 고유 `LethalHazard`를 모두 공격 수단으로 사용한다.
3. **규칙 변화**: 2인에서는 패자, 3·4인에서는 최초 탈락자가 `Trigger → Effect` 패치를 만들고 최대 세 개가 누적된다.
4. **대기 없는 놀이**: 접속 직후 Lobby에서도 같은 캐릭터와 조작으로 친구와 논다.
5. **자기 표현**: 직접 그린 외형과 자유 배치한 3D 치장으로 한 캐릭터를 각자 다르게 만든다.

### 1.4 설계 원칙

- 패치가 없어도 기본 전투가 재미있어야 한다.
- 혼란은 허용하되 타격, 탈락과 패치 발동 원인은 읽혀야 한다.
- 모든 gameplay 판정은 방장 PC의 `AuthorityHost`가 내린다.
- 2인, 3인, 4인이 같은 맵·규칙·카메라 계약을 사용한다.
- UI는 전투 화면을 가리지 않고 현재 가능한 행동과 막힌 이유를 보여준다.
- 외형은 자유롭지만 Collider, 질량, reach, Camera bounds와 능력치를 바꾸지 않는다.
- 제품 기능을 위해 별도 운영 Backend나 Dedicated Game Server를 요구하지 않는다.

---

## 2. 사용자와 세션

### 2.1 대상 사용자

- 음성 채팅 등 외부 도구로 대화하며 비공개로 모이는 2~4인 친구 그룹
- 잡기, 던지기, 환경 킬과 물리 실수에서 재미를 느끼는 파티게임 플레이어
- 직접 그린 외형과 치장을 친구에게 보여주고 싶은 플레이어
- 1.0 이후 승인된 구성요소로 안전한 Workshop 맵을 만들 제작자

### 2.2 권한 모델

- 방을 연 플레이어의 Unity 프로세스가 자기 캐릭터 실행과 모든 참가자의 최종 판정을 함께 맡는
  `AuthorityHost`다. 별도 Server 프로세스는 없다.
- Guest는 입력을 보내고 예측·보간·표현을 담당한다.
- Host는 이동, 손 상태, AirAttackToken·Kick·Dropkick, Grab, 충돌, Down/Ragdoll, 무기, 탈락, 점수,
  패치와 Scene 전환을 판정한다.
- P2P는 전송 방식이며 Peer 간 분산 권한을 뜻하지 않는다.
- Guest가 명시적으로 Leave하면 reconnect grace 없이 즉시 Forfeit한다. Lobby에서는 slot을 즉시 제거하고,
  Match에서는 아래 참가자 수 규칙을 적용한다.
- 예상치 못한 Guest disconnect에는 30초 grace를 적용한다. 이 동안 Input은 Neutral이지만 Character는
  물리 World에 남아 충돌·피해·Down·탈락 대상이고 Alive라면 Camera subject로 유지한다.
- Guest가 grace 안에 돌아오면 Host의 현재 Alive 또는 Spectator 상태로 복원한다. Timeout은 Forfeit이며
  Forfeit 자체는 PatchAuthor가 될 수 없다.
- Forfeit 뒤 permanent participant가 2명 이상이면 3·4인 Match를 계속한다. 1명만 남으면 해당 시점의
  score와 Patch를 추가하지 않고 `OpponentLeft`를 잠깐 표시한 뒤 같은 Lobby로 돌아간다.
- Host가 명시적으로 Leave하거나 연결을 잃으면 Host Migration 없이 Session을 종료한다.

### 2.3 제품이 사용하지 않는 서버 구성

다음 구성은 Alpha뿐 아니라 현행 제품 구조에 존재하지 않는다.

- 별도 Backend 또는 Game Coordinator
- 계정·Party·경기용 자체 Database
- 외형용 blob store 또는 bake worker
- 운영형 Dedicated Game Server와 server allocation
- gameplay relay를 수행하는 자체 서비스
- Docker, OCI, Compose와 container image

Steam 단계에서는 Steam의 Lobby·identity·P2P/SDR·Workshop 서비스를 사용하지만 자체 Backend를
추가하지 않는다.

---

## 3. 전체 플레이 흐름

```text
MainMenu
→ Host Session 생성 또는 참가
→ InteractiveLobby FreeRoam
→ C로 CharacterCustomizer 또는 Local Preset 적용
→ Host Appearance Validation / P2P Appearance Sync
→ Guest Ready · Host Start Gate
→ Host Start Button
→ LobbyLaunchSequence
→ Internal RandomMap Selection
→ MatchLoading
→ RoundIntro
→ RoundCountdown 3초
→ Playing
→ SuddenDeath (필요 시)
→ RoundResult
→ PatchSelection / PatchResult (최종 Round가 아닐 때, Alpha는 plain text)
→ 다음 RoundIntro
→ MatchResult
→ 같은 Session의 InteractiveLobby Return
→ Guest NotReady · Host Start Gate 재개
```

Lobby, MatchLoading, 모든 Round, MatchResult와 Lobby 복귀는 같은 AuthorityHost process와 active
connection을 유지한다. 경기마다 새 서버를 배정하거나 Host를 교체하지 않는다.

Match 중 명시적 Guest Leave 또는 disconnect timeout은 Forfeit다. 남은 permanent participant가 2명 이상이면
현재 Match를 계속하고, 1명뿐이면 Round score·Patch를 만들지 않고 `OpponentLeft` 뒤 Lobby로 복귀한다.
Host Leave·Loss는 어느 phase에서든 Session을 종료한다.

---

## 4. 조작과 캐릭터 상태

### 4.1 키보드·마우스 기본 조작

| 입력 | 행동 |
|---|---|
| `WASD` | 공용 카메라 화면 축 기준 이동과 이동 방향 자동 회전 |
| `Left Shift` hold | Lobby·Match 공통 Sprint |
| `Space` | Jump |
| `LMB` | Ground quick tap은 왼손 Punch, Airborne quick tap은 왼발 Kick, hold는 왼손 Grab/Release |
| `RMB` | Ground quick tap은 오른손 Punch, Airborne quick tap은 오른발 Kick, hold는 오른손 Grab/Release |
| `C` | Lobby 어디서든 CharacterCustomizer 진입 요청 |
| `E` | Guest만 자신의 Ready/CancelReady 전환 |
| `Tab` | Match의 점수·활성 패치 Overlay |
| `Esc` | Lobby Cursor Mode 또는 Match local-only menu |

`Q/E` 회전과 플레이어별 Camera 회전·Zoom은 제공하지 않는다. 이동 입력이 있으면 캐릭터가
그 방향으로 자연스럽게 회전하고, 입력이 없으면 마지막 방향을 유지한다.

Match `Esc` menu는 Pause가 아니다. 열려 있는 동안 AuthorityHost simulation, Round timer, Hazard와
다른 Player는 계속 진행하고 해당 local Player의 gameplay Input만 Neutral이다. Menu를 닫은 뒤
LMB/RMB가 모두 Release된 것을 확인할 때까지 Hand·Weapon Input을 재무장하지 않으며 held/up sample을
새 Punch·Kick·Grab·WeaponUse로 해석하지 않는다.

### 4.2 Sprint

- `Left Shift`를 누르는 동안만 Sprint가 적용된다.
- Lobby와 Match가 같은 Sprint 규칙을 사용한다.
- Stamina, 소모 게이지, 회복 대기와 Sprint 전용 HUD를 만들지 않는다.
- 이동 배율과 가속 보정은 `MovementTuningProfile`의 Alpha tuning 값이다.
- Sprint는 Grab·충돌·무기 판정의 AuthorityHost 소유권을 바꾸지 않는다.

### 4.3 Ground Punch와 Grab 의도 판별

손 버튼 down은 즉시 Punch를 발생시키지 않고 해당 손을 `HandIntentPending`으로 만든다.

- threshold 전에 놓으면 한 번의 Strike로 확정한다.
- threshold까지 유지하면 Strike 없이 GrabSeek로 전환한다.
- 유효 대상에 닿으면 Grabbing, 버튼을 놓으면 Release한다.
- 대상에 닿기 전 GrabSeek를 놓아도 Punch로 되돌리지 않는다.
- 시작 후보는 150ms이며 120·150·180ms를 Alpha에서 비교해 tuning profile에 고정한다.

따라서 잡기를 시도하기 전에 Punch가 한 번 나가지 않는다.

### 4.4 Air Kick·Dropkick과 Grab 우선순위

Airborne이면서 Down/Ragdoll Episode가 아닌 동안 같은 LMB/RMB는 발 공격 context를 사용한다.
`Airborne Episode`는 stable Grounded를 떠난 뒤 다시 stable Grounded가 될 때까지의 권한 구간이며
Down/Ragdoll Episode와 구분한다.

- threshold 전에 빠르게 놓은 LMB/RMB는 chord 대기 뒤 각각 왼발/오른발 Kick 후보가 된다. 반대
  button down edge 없이 `DualClickChordWindow`가 닫혀야 단일 Kick 한 번으로 확정한다.
- 반대 두 button의 down edge가 `DualClickChordWindow` 안에 들어오면 두 pending을 즉시 소비해
  Dropkick 한 번으로 확정한다. 두 button의 quick release를 추가로 요구하지 않는다.
- Chord 비교값은 60·80·100ms, Alpha `START`는 80ms다.
- chord가 성립하기 전에 하나라도 기존 `GrabHoldThreshold`를 넘겨 hold하면 해당 pending Kick을 취소하고
  그 손의 Hand/ledge GrabSeek로 전환한다. 확정된 Dropkick은 이미 두 pending을 소비한다.
- 같은 chord가 좌우 단일 Kick 두 번이나 Kick+Dropkick을 함께 만들면 실패다.

각 Airborne Episode는 `AirAttackToken` 한 개만 가진다. 좌우 Kick 또는 Dropkick 하나가 Token을 소비하며,
Token을 쓴 뒤의 quick tap/chord는 추가 공중 공격을 만들지 않지만 hold Grab은 계속 사용할 수 있다.
안정적인 Grounded, GetUp 완료 또는 Round reset에서만 Token을 1로 복원한다.

Dropkick은 승인 profile의 bounded forward impulse, 감소된 air steering과 일반 Kick보다 강한 bounded
knockback을 사용한다. 착지·공격 종료·빗나감 뒤에는 짧은 `DropkickRecovery`와 physics tumble을 거친다.
이 tumble은 Down/Ragdoll Episode가 아니며 `DownCount`를 늘리거나 `TRG-DOWN-EPISODE-START`를 만들지 않는다.
Token이 먼저 복원돼도 DropkickRecovery가 끝나기 전 새 Punch·Kick·Dropkick·Weapon Attack은 0이다.

Kick·Dropkick의 action, hit와 physics 결과는 AuthorityHost만 확정한다. 유효 hit는
`TRG-ATTACK-HIT-CONFIRMED`의 `Kick` 또는 `Dropkick` SourceKind가 된다. 좌우는 Action/Anchor context로
보존하며 별도 SourceKind로 나누지 않는다. 이 hit에는
`PATCH-PROT-003..004`를 적용할 수 있다. Weapon source가 아니므로 `TRG-WEAPON-HIT-CONFIRMED`와
`PATCH-PROT-011..012`는 발생하지 않는다.

W1은 이 Airborne L/R tap·dual chord·hold Grab mapping을 덮어쓸 수 없다. W1 전에는 Airborne에서
Kick/Dropkick이 우선하며 WeaponUse를 같은 tap/chord로 해석하지 않는다. Airborne WeaponUse 허용 여부와
별도 action·mode 입력은 W1 사용자 Gate에서 결정한다.

### 4.5 Down/Ragdoll과 groggy 누적

- AuthorityHost가 Match Round 안에서 Down 또는 Ragdoll 진입을 확정할 때마다 해당 플레이어의 groggy stack을 한 번만 증가시킨다.
- 첫 Down은 기본 시간을 사용하고, 같은 Round의 두 번째 Down부터 진입할 때마다 groggy·down duration이 증가한다.
- 기본 시간, 진입당 증가량과 최대 시간은 `PhysicsTuningProfile`의 Alpha tuning 값이다.
- 한 번의 상태 진입을 여러 frame이나 여러 충돌 callback으로 중복 계산하지 않는다.
- 다음 Round baseline reset에서 모든 플레이어의 stack을 0으로 만든다.
- Lobby Ragdoll은 항상 BaseDuration을 사용하고 Match용 groggy stack을 증가시키지 않는다.

### 4.6 공용 카메라

- Lobby와 Match는 한 개의 `SharedGameplayCamera`를 사용한다.
- Host가 active character Root와 bounds로 Focus와 Dolly 목표를 계산하고 Client가 부드럽게 보간한다.
- 플레이어와 관전자는 같은 구도를 본다.
- Ragdoll 팔다리 흔들림과 Guest의 예측 위치는 Camera 목표를 만들지 않는다.
- 탈락자와 Lobby 밖 추락자는 추적 대상에서 제외하고 재구도는 Damping한다.
- Camera profile의 정확한 수치는 2·3·4인 Alpha capture와 플레이테스트로 tuning한다.

### 4.7 Hybrid Animation·Presentation Matrix

Alpha는 다음 최소 상태를 같은 Authority action/state에서 읽어 표현한다.

- locomotion과 Jump takeoff·airborne·landing phase
- 왼손·오른손 Punch
- 왼발·오른발 Air Kick
- Dropkick·DropkickRecovery·physics tumble
- Grab·Lift·Throw
- Weapon Fire·Melee Swing
- Ragdoll·GetUp

Animation, visual pose와 blend는 gameplay action·hit·impulse·Collider·Down을 확정하지 않는다. Gameplay
root motion은 0이며 Host state와 physics 결과를 read-only로 따라간다. 2·3·4인 공용 Camera에서 좌우 공격,
Dropkick 방향, Grab과 Weapon attack 원인을 구분할 수 있어야 한다.

---

## 5. 전투, 무기와 탈락

### 5.1 기본 행동

- 이동·Sprint·Jump
- 좌우 손 Punch, Grab과 Release
- Airborne 좌우 Kick과 dual-click Dropkick
- 상대·소품·무기 들기와 이동 관성을 이용한 던지기
- 충격에 따른 Stun, Down과 Ragdoll
- 움직이는 소품과 맵 장치의 물리 조작

### 5.2 탈락 경로

- `OutOfBounds`: 권한 core body가 최종 OOB Volume에 들어가면 탈락한다.
- `LethalHazard`: 맵 안에서도 승인된 치명 구간과 조건이 성립하면 즉시 탈락한다.
- `DisplacementHazard`: 밀거나 날려 OOB를 유도하지만 자체로 직접 탈락시키지 않는다.
- 높은 맵은 벽·가장자리를 붙잡아 복구할 수 있는 RecoveryBand와 그 아래 최종 OOB를 분리한다.
- 치명 기믹은 작동 전에 시각·음향 예고와 회피·구출 구간을 제공한다.

### 5.3 실제 무기 전투

실제 총기와 근접 무기 전투는 Alpha 범위다. Character Grip Benchmark만 만들고 끝내지 않는다.

- Alpha 대표 기능 ID는 `Pistol`, `LongGun`, `Bat`, `Hammer`다. 실제 모델명은 사용자-facing 이름으로
  노출하지 않는다.
- `Pistol`은 M1911을 reference role로 삼은 brand-free low-poly 권총, `LongGun`은 AK-47을 reference
  role로 삼은 brand-free low-poly 장총이다. 정확 복제, 제조사 trade dress, logo·각인·serial은 사용하지 않는다.
- `Bat`은 logo 없는 low-poly baseball bat, `Hammer`는 logo 없는 low-poly construction sledgehammer다.
- 좌우 손 Main Grip과 필요한 Support Grip, Pickup, Held, Use, Drop, loose physics와 Reacquire를 검증한다.
- 발사·Swing·Impact·탄약·피해·Stun·Ragdoll·ringout attribution은 AuthorityHost가 판정한다.
- 무기는 Character collider, 기본 hand reach, mass baseline과 Camera subject bounds를 바꾸지 않는다.
- Round reset은 떨어진 무기, ownership과 일시 weapon state를 맵 baseline으로 복원한다.

Firearm의 승인 구조는 다음과 같다.

| 기능 ID | Fire mode | Spawn 총 Ammo | Reserve·Reload |
|---|---|---:|---|
| `Pistol` | Host가 수락한 WeaponUse press edge당 semi-auto 1발 | 7 | 없음 |
| `LongGun` | 유효 WeaponUse hold와 cadence 동안 full-auto | 30 | 없음 |

- 각 Spawn은 full Ammo로 시작하며 reserve ammo, magazine 교체, reload command·state와 ammo pickup은 0이다.
- 각 Shot은 Host가 owner·held·phase·cadence·Ammo를 검증하고 Ammo-1과 Projectile Spawn을 원자 처리한다.
- 마지막 Shot 뒤 Ammo가 0이면 Host가 Weapon을 forced release해 `SpentPendingCleanup`으로 전환한다.
  Spent는 `START 2~4초` cap을 차지한 뒤 deadline에 제거되며 replacement는 다음 정규 Supply Pulse만 사용한다.
- Fire와 Projectile은 `Playing + SuddenDeath`에서 유효하고 `RoundResult`부터 새 Fire 0·active Projectile 0이다.

Projectile은 Host가 muzzle에서 생성하는 실제 visible object다. Host fixed-step은 이전 위치→다음 위치를
swept SphereCast해 첫 Map blocker 또는 Character hit 하나에서 종료한다. Alpha Projectile은 gravity 0
`START`, pierce·ricochet·다중 Character hit 0이며 TTL·Projectile OOB·RoundResult·reset에서 제거한다.
Projectile·recoil은 Lever·Crane·Hook·Panel·Hazard phase와 prop을 원격 작동하거나 impulse로 움직이지 않는다.

Recoil은 Host Unity physics와 Presentation을 결합한다. Pistol은 좁은 base spread와 강한 single-shot
recoil을 사용한다. LongGun은 연사 중 bounded `RecoilAccumulator`와 `SpreadBloom`을 deterministic
`ShotSequence`로 누적해 지속 연사일수록 덜 정확해지고, release 또는 승인 gap에서 회복한다. Animator와
camera cue는 Host recoil state를 read-only로 표시하며 Shot 방향·Hit·Rigidbody authority를 바꾸지 않는다.

LMB/RMB의 Punch·Grab과 실제 WeaponUse·Drop을 구분하는 `W1` 입력 결정은 Alpha 안의 선행 Gate다.
`Context Hand`, `Weapon Mode`, `Separate Use` 비교 결과를 사용자에게 제시하고 하나를 승인받은 뒤
실제 무기 전투를 구현한다. W1은 Airborne L/R Kick·dual-click Dropkick·hold Hand/ledge Grab mapping을
덮어쓰지 않는다.
Airborne WeaponUse를 허용할지와 그 별도 action/mode 입력도 W1에서 승인하며, 그 전에는 Air attack이
우선이다. 승인 전에는 임의 입력을 확정하지 않는다.

### 5.4 반복 무기 보급

라운드당 무기 한 개만 배치·공급하는 안은 사용하지 않는다. 각 Round의 `Playing` 시작을 기준으로
AuthorityHost가 다음 `START` profile의 반복 Supply Pulse를 판정한다.

| 인원 | 첫 Pulse | 반복 간격 | 동시 존재 cap |
|---:|---:|---:|---:|
| 2인 | 10초 | 22초 | 2개 |
| 3인 | 8초 | 16초 | 2개 |
| 4인 | 6초 | 12초 | 3개 |

- Host는 Round 초기 participating roster로 supply profile을 한 번 선택해 그 Round의 SuddenDeath·Result까지
  고정한다. Disconnect·Reconnect·중도 Forfeit는 현재 Round profile을 바꾸지 않고 다음 Round에서만 반영한다.
- cap은 `Incoming + Loose + Held + SpentPendingCleanup` Weapon Instance를 모두 센다.
- Base Pulse는 빈 capacity가 있을 때 무기 한 개를 admission한다. cap이 가득 차면 0개와
  `CapacityLimited`를 기록하고 그 Pulse를 건너뛰며 backlog, catch-up과 즉시 재시도를 만들지 않는다.
- Weapon이 OOB로 제거돼 capacity가 생겨도 즉시 보충하지 않고 다음 정규 Pulse에서 다시 판정한다.
- Supply와 파생 Wave의 admission은 `Playing`에서만 가능하다. SuddenDeath 진입 또는 Round Result에서
  남은 정규 Pulse와 파생 Wave를 취소하되 이미 존재하는 Weapon, Spent deadline과 Firearm combat은
  RoundResult까지 유지한다.
- Host는 Round마다 `MatchSeed + Round + WeaponCatalogVersion`에 기반해 Pistol·LongGun·Bat·Hammer가
  한 번씩 들어 있는 결정적 shuffle bag을 만들고 검증된 Safe DropZone을 결정한다.
- 실제 admission된 Spawn만 bag cursor를 소비한다. cap 때문에 생성되지 않은 수량은 cursor를
  소비하지 않으며 bag이 끝나면 결정적인 다음 bag을 만든다.
- Host는 admission과 landing에 Character·Weapon·moving part가 없는 `LandingClearance`를 검사한다.
  admission 시 유효 Zone이 없으면 `NoSafeDropZone`으로 생성하지 않고, landing이 계속 막히면 bounded
  `START 1~2초` 뒤 `LandingBlocked`로 제거한다. 두 경우 모두 피해·backlog·즉시 재투하는 없다.
- `Incoming` Weapon은 Character Damage·Down·Knockback·Grab·Pickup, 다른 Weapon 충돌, Patch Trigger와
  Map control·Hazard interaction을 만들거나 Camera subject가 되지 않는다. 안전하게 착지한 뒤 `Loose`가
  되며, Round reset은 Incoming·Loose·Held·Spent, Ammo·Projectile·Recoil/Spread와 supply schedule을
  모두 baseline으로 복원한다.
- 각 Map은 회수 불가능한 Loose Weapon용 Host `WeaponCleanupBoundary`를 제공한다. Held Weapon은 owner가
  유효한 동안 Collider 일부만 경계를 넘었다고 제거하지 않으며 cleanup 뒤 보충은 다음 정규 Pulse에서만 한다.

---

## 6. Match, Round와 패치

### 6.1 승리 규칙

- Round의 마지막 생존자가 1점을 얻는다.
- 먼저 4점을 획득한 플레이어가 Match를 이긴다.
- 기본 Round 제한 시간은 60초다.
- 시간이 끝났을 때 생존자가 둘 이상이면 Sudden Death로 반드시 승자를 정한다.
- Match 승자가 결정된 뒤에는 새 패치를 만들지 않는다.

### 6.2 패치 작성자와 흐름

- 플레이어에게 보이는 공식 명칭은 `패치`다.
- 2인에서는 해당 Round 패자가 패치를 만든다.
- 3·4인에서는 가장 먼저 탈락한 플레이어가 패치를 만든다.
- 명시적 Leave 또는 disconnect timeout의 Forfeit는 PatchAuthor 선정 사건이 아니다. Grace 중 정상
  gameplay 탈락이 먼저 확정된 경우에는 그 탈락과 Forfeit를 구분한다.
- 작성자는 Trigger 후보 둘 중 하나를 고른 뒤 호환 Effect 후보 둘 중 하나를 고른다.
- 완성된 패치는 다음 Round부터 모든 플레이어에게 적용된다.
- 선택하지 못하면 AuthorityHost가 노출된 유효 후보 중 하나를 자동 선택한다.
- 자유 텍스트와 특정 플레이어만 대상으로 삼는 패치는 사용하지 않는다.
- 승인 Trigger·Effect, Patch12 조합, 대상 규칙과 충돌 tag의 상세 source는 `PATCH_DESIGN.md` 0.5.0이다.

Alpha Patch 화면은 기능 확인을 위한 다음 평문 정보만 요구한다.

- Trigger 후보 2개와 선택 뒤 Effect 후보 2개
- 남은 선택 시간
- 확정된 패치 한 문장
- 현재 활성 패치 최대 3개의 텍스트 목록

최종 icon, animation, 전용 VFX·SFX와 완성 layout은 Alpha 승인 조건이 아니다. Authority runtime은
선택·활성·발동·만료의 semantic event와 read-only state를 제공하고, Alpha text view와 후속 제품
presentation은 이를 소비할 뿐 gameplay state를 직접 바꾸지 않는다.

### 6.3 누적과 초기화

- 활성 패치는 최대 3개다.
- 네 번째 패치가 추가되면 가장 오래된 패치를 제거하는 FIFO를 사용한다.
- 후보 생성 시 활성 패치가 3개라면 다음 Round 직전에 FIFO로 빠질 가장 오래된 패치를 먼저 제외한
  `projected active set`을 사용한다. 이 집합을 기준으로 중복·충돌·무효 후보를 거른다.
- Round가 끝나면 캐릭터 transform·velocity, AirAttackToken·pending chord·DropkickRecovery, 손·Grab,
  Stun·Ragdoll, groggy stack, 충격, 소품·무기와 Hazard를 baseline으로 복원한다.
- 점수, MatchSeed, 선택 맵, 패치 이력과 활성 패치는 유지한다.
- 유지한 패치는 깨끗한 tuning baseline 위에 정해진 순서로 다시 적용한다.
- 이전 Round 입력·event가 다음 Round에 적용되지 않아야 한다.

### 6.4 초기 승인 Patch12

기존 `PATCH-PROT-001..008`의 Character 물리 조합에 다음 네 조합을 추가한다.

| Patch | Trigger → Effect | 사용자 문장 |
|---|---|---|
| `PATCH-PROT-009` | `TRG-WEAPON-SUPPLY-SCHEDULED` → `EFF-WEAPON-SUPPLY-DOUBLE` | `보급 시간이 되면 무기 두 개가 동시에 떨어집니다.` |
| `PATCH-PROT-010` | `TRG-WEAPON-SUPPLY-SCHEDULED` → `EFF-WEAPON-SUPPLY-SECOND-WAVE` | `보급 시간이 되면 잠시 뒤 무기가 한 번 더 떨어집니다.` |
| `PATCH-PROT-011` | `TRG-WEAPON-HIT-CONFIRMED` → `EFF-VICTIM-HELD-WEAPON-FORCED-DROP` | `무기로 공격을 맞히면 맞은 플레이어가 들고 있던 무기를 놓칩니다.` |
| `PATCH-PROT-012` | `TRG-WEAPON-HIT-CONFIRMED` → `EFF-ATTACKER-SOURCE-WEAPON-FORCED-DROP` | `무기로 공격을 맞히면 공격한 플레이어도 사용한 무기를 놓칩니다.` |

- `PATCH-PROT-009`는 원하는 batch를 별도 Weapon Instance 2개로 만들되 남은 capacity만 admission한다. 빈자리가 1개면
  1개, 0개면 0개를 Spawn하고 `CapacityLimited`를 기록하며 cap·backlog를 늘리지 않는다.
- `PATCH-PROT-010`의 두 번째 Wave는 첫 Base Supply 뒤 `START 6~10초`에 한 개를 다시 요청한다.
  그 시점 capacity가 없으면 `CapacityLimited`로 끝내고 queue·재시도하지 않는다.
- 승인 Patch12의 같은 Trigger를 공유하는 두 Patch는 모두 상호 배타다. retained active set은 Trigger당
  Patch Instance를 최대 하나만 가지며, 예를 들어 `001↔002`, `003↔004`, `005↔006`, `007↔008`,
  `009↔010`, `011↔012`를 동시에 활성화하지 않는다.
- Supply Patch는 Base Supply root에서만 발동하고 파생 Wave가 같은 Supply Trigger를 다시 만들지 않는다.
- Weapon Hit Patch는 Host가 ownership·rate·hit·dedupe를 확인한 실제 Weapon hit에만 발동하며
  Damage·Ammo·cadence를 바꾸지 않는다.
- `PATCH-PROT-011`은 victim의 모든 Held Weapon Instance를 강제 Drop한다. Main·Support가 같은 Instance면
  한 번만 처리하고 Held Weapon이 없으면 `NoEligibleTarget`이다.
- `PATCH-PROT-012`는 attacker가 해당 hit에 사용했고 Effect 시점에도 같은 attacker가 Held 중인 source
  Weapon Instance만 강제 Drop한다. 이미 놓였거나 owner가 바뀌었으면 `NoEligibleTarget`이다.
- 강제 Drop과 Supply의 파생 Event가 새 Weapon Hit 또는 Supply Trigger를 만드는 재귀 경로는 0개다.

### 6.5 콘텐츠 목표

| 단계 | 검증된 패치 조합 |
|---|---:|
| Prototype | 12개 |
| Alpha | 20개 |
| Steam 제품 통합 | 30개 |
| 1.0 | 40개 이상 |

`검증된 조합`은 문법상 가능한 수가 아니라 실제 2·3·4인 기능 시험을 통과한 조합 수다. Prototype의
첫 12개는 `UG-PATCH12-DESIGN`에서 catalog·충돌·후보·보급 규칙을 승인했고 `UG-PATCH12`에서 실제
선택·다음 Round 적용·supply·forced drop·reset·FIFO를 검증한다. Patch13..20은 이 결과 뒤에 확장한다.

---

## 7. InteractiveLobby와 경기 시작

### 7.1 Lobby 역할

- Lobby는 메뉴 배경이 아니라 같은 Controller로 즉시 노는 작은 3D 공간이다.
- 이동·Sprint·Jump·Punch·Air Kick·Dropkick·Grab·던지기·Ragdoll과 승인된 소품을 사용할 수 있다.
- 벽과 투명한 낙하 방지 Barrier를 두지 않는다.
- Lobby OOB는 점수·탈락 없이 1~2초 뒤 하늘 재투입으로 끝난다.
- Lobby 상태, 위치·속도·Grab과 소품 상태는 Match로 가져가지 않으며 Lobby는 Match용 groggy stack을 만들지 않는다.
- Ready한 Guest도 이동과 물리 장난을 계속할 수 있다.

### 7.2 Guest Ready와 Host Start action slot

Lobby의 같은 개인 action slot을 역할별로 다르게 사용한다.

- Guest에게는 `Ready / CancelReady` Button을 표시하고 `E` 또는 Cursor click으로 전환한다.
- NotReady Button은 중립색, Ready 확정 뒤 Button은 `ReadyTeal`로 바꿔 상태를 즉시 읽게 한다.
  색만 사용하지 않고 check icon과 `준비 완료 / 준비 취소` 문구도 함께 바꾼다.
- Host에게 Ready 상태와 Ready Button을 만들지 않는다.
- Host에게는 Lobby 진입 순간부터 같은 위치에 `Start` Button을 항상 표시한다.
- Host Start는 Mouse Cursor로 누르는 UI action이며 world object, `E` action 또는 손 Grab이 아니다.
- Start 조건을 만족하지 않으면 Button을 disabled로 표시하고 가장 우선인 이유를 가까이에 보여준다.

Start는 아래 조건을 모두 만족할 때만 활성화되고 AuthorityHost가 click 시점에 다시 검증한다.

1. Host를 포함한 총 참가자가 정확히 2~4명이다.
2. 모든 Guest가 active connection 상태다.
3. 모든 Guest가 Ready다.
4. 모든 Guest의 외형이 확정됐다.
5. Host의 외형도 확정됐다.
6. Lobby phase이며 다른 Scene 전환이 진행 중이지 않다.

Host 혼자, NotReady Guest가 한 명이라도 있는 상태, 연결이 끊긴 Guest가 남아 있는 상태,
Host 또는 Guest 외형이 처리 중인 상태에서는 Start request 수락이 0이어야 한다.

Guest의 명시적 Lobby Leave는 30초 grace 없이 slot을 즉시 제거한다. 예상치 못한 disconnect는 slot과
Player state를 30초 보존하고 Start를 차단하며, timeout 뒤 Lobby reservation을 제거한다. Host Leave·Loss는
Lobby를 포함한 전체 Session을 종료한다.

Lobby 경기 시작용 물리 `StartLever`는 제품에서 완전히 제거한다. P00의 `Crane control lever`는
경기 중 크레인 Hazard를 조작하는 별도 맵 장치이며 Lobby Start UI와 이름·권한·용도가 다르다.

### 7.3 Lobby Cursor Mode

- `Esc`를 누르면 Lobby Cursor를 열고 local 이동·손 입력을 neutral로 만든다.
- Cursor가 열린 상태에서 다시 `Esc`를 누르면 Cursor를 닫고 캐릭터 조작으로 돌아간다.
- Cursor가 열려 있어도 AuthorityHost simulation, 다른 플레이어의 물리와 공용 Camera는 계속된다.
- Cursor 진입은 Pending·GrabSeek·Grabbing을 비타격 취소하고 추가 Throw·Strike impulse를 만들지 않는다.
- Cursor를 닫을 때 LMB/RMB가 held면 모든 Mouse button up을 확인할 때까지 손 입력을 재무장하지 않는다.
- 재무장 대기 중 held button이나 button-up을 새 Punch·Grab으로 해석하지 않는다.

### 7.4 Match 진입과 재경기

Host Start가 수락되면 입력을 잠그고 짧은 `LobbyLaunchSequence`를 실행한 뒤 내부 호환 맵 풀에서
맵 하나를 무작위 선택한다. 실패하면 Match로 이동하지 않고 Lobby에서 Guest를 NotReady로 되돌린다.

Match Scene 활성화 후 별도 3초 `RoundCountdown`을 실행한다. Lobby 전환 연출과 RoundCountdown을
같은 이름으로 부르지 않는다.

MatchResult 뒤에는 같은 Session의 Lobby로 돌아온다. Guest는 NotReady, Host는 다시 고정 Start
action slot 상태가 되며, 조건을 다시 만족해 Host가 Start를 눌러야 다음 Match가 시작된다.

MatchResult용 persistent panel은 없다. Match가 정상 종료되면 같은 Lobby로 전환하고, Guest
Forfeit로 permanent participant가 1명만 남은 경우에는 score·Patch를 추가하지 않은 채 transient
`OpponentLeft`만 표시하고 같은 경로로 Lobby에 복귀한다.

---

## 8. 캐릭터 외형과 Preset

### 8.1 기본 캐릭터

- 공통 캐릭터는 흰색 무안면 이족형 `Hybrid Core MasterCharacter` 한 종이다.
- C1a v0.13의 큰 방향은 둥근 타원형 머리, 짧고 넓은 몸통, 낮은 중심, 짧고 굵은 limb다.
- 배만 불룩한 체형, 긴 일반 인간형 비율과 별도 가시 Hand Mesh는 사용하지 않는다.
- 정확한 비율·Collider·reach·최종 Mesh는 C1b 이후 별도 Gate에서 승인한다.

### 8.2 직접 그리기와 치장

- `C`로 Lobby 어디서든 보호된 CharacterCustomizer에 진입한다.
- Brush·Erase와 게임 내부 색상만 사용하고 파일·clipboard image·URL·Sticker를 입력받지 않는다.
- 게임 제공 3D 치장은 authored 고정 색상·고정 크기를 사용한다.
- 고정 slot이나 부위별 금지 구역 없이 전신 외부 표면 어디든 위치·3축 회전한다.
- 중첩, 치장끼리의 겹침과 Character Mesh의 시각적 관통을 허용한다.
- 사용자 scale 조절은 제공하지 않는다.
- 치장은 gameplay collider·mass·hitbox·reach·Camera bounds와 능력치를 바꾸지 않는다.
- Alpha Cosmetic catalog는 `EyeSet`·`Mustache`·`Headwear`의 placeholder 대표 1개씩 또는 같은
  기능 범위를 검증하는 동등 최소 game-authored 집합이면 충분하다. 최종 조형 수량과 제품 품질
  catalog는 post-Alpha다.

### 8.3 Local Preset과 P2P 동기화

- 사용자는 local device에 최대 10개의 AppearancePreset을 저장·불러오기·덮어쓰기·삭제한다.
- Preset save는 임시 파일 작성과 교체를 포함한 local atomic save로 처리해 중단 시 기존 정상본을 보존한다.
- Preset을 불러오면 같은 frame의 local Preview에 적용되고 Guest라면 NotReady가 된다.
- `ApplyAndReturn`은 bounded appearance source를 AuthorityHost에 제출한다.
- Host는 Brush source, 색상, game catalog 치장, instance 예산과 금지된 외부 입력 여부를 검증한다.
- 검증 성공 시 Host가 승인된 appearance source와 revision을 다른 Peer에 P2P relay한다.
- 실패하면 해당 플레이어만 안전한 기본 외형으로 확정한다.
- 별도 DB, blob store, upload URL, bake worker와 Backend appearance pipeline은 사용하지 않는다.
- Ready와 Start 판단은 Host가 확정한 외형 상태만 사용한다.

---

## 9. 맵과 환경 기믹

### 9.1 공통 맵 계약

- 2·3·4인은 같은 `MapDefinition`, geometry, PlayBounds, Recovery, Hazard와 Camera profile을 사용한다.
- 인원수에 따라 달라지는 것은 검증된 SpawnPoint 사용·Player 배정과 승인 Weapon supply `START`
  profile뿐이다.
- 공식 맵은 OOB, 고유 LethalHazard와 보조 DisplacementHazard를 제공한다.
- 맵 장치는 손으로 잡을 수 있지만 Lobby Start UI와 혼동되는 generic start lever를 사용하지 않는다.
- P00 `Construction Drop`의 Crane lever는 크레인 압착 Hazard를 조작하는 경기 장치로 유지한다.

### 9.2 공식 맵 목표

| 단계 | 공식 맵 목표 |
|---|---|
| Prototype | Greybox 1개 |
| Alpha | 제품 수준 1개 + Greybox 2개, 모두 2·3·4인 검증 |
| Steam 제품 통합 | 공식 맵 3개 |
| 1.0 | 공식 맵 6개 |

초기 테마 후보는 Construction Drop, Last Train, Compactor, Cargo Tilt, Factory Recall과 Tank
Rupture다. 이름과 시각 테마는 제작 Gate에서 바뀔 수 있지만 탈락·가독성 계약은 유지한다.

### 9.3 Workshop

1.0 Workshop 맵은 Steam Workshop과 공식 Editor를 사용한다.

- 승인된 prefab·data만 사용하고 실행 코드, DLL, native plugin과 새 network message를 금지한다.
- Host와 Guest가 같은 item identity·version을 확인한 뒤에만 시작한다.
- Workshop은 별도 제작자 Backend, 공개 matchmaking 또는 랭크를 요구하지 않는다.

---

## 10. 연결과 플랫폼 단계

### 10.1 Prototype

- 한 개발기에서 AuthorityHost와 2개 이상 Client process 검증
- Greybox Lobby·Map, Ground/Air 전투·Dropkick, Camera, 반복 무기 보급과 승인 Patch 12개의 plain-text 선택·실제 적용
- Character C1b/C2/C3와 무기 Grip Benchmark
- Steam 기능 없음

### 10.2 Alpha (`Vertical Slice`)

- LAN 또는 명시적으로 입력한 direct endpoint로 방장 PC의 AuthorityHost에 연결
- 신뢰된 개발·테스트 Network에서만 사용하며 민감한 계정 credential을 전송하지 않고 Internet 인증·암호화·NAT traversal 완료를 주장하지 않음
- 서로 다른 실제 PC에서 2인·3인·4인 전부 전체 흐름 검증
- 공개 discovery, 초대 코드, Steam auth, Steam Lobby, 친구 초대와 Steam P2P/SDR 없음
- Lobby, 외형 local save·Host 검증·P2P relay, Guest Ready·Host Start와 Match→Lobby 완료
- 제품 수준 맵 1개와 Greybox 2개, Patch 20개의 기능 검증
- 실제 Pistol·LongGun·Bat·Hammer 전투와 Alpha 안의 W1 입력 승인·구현
- Hybrid animation matrix와 brand-free 네 low-poly Weapon archetype의 2·3·4인 silhouette 검증
- 2·3·4인 반복 Supply Pulse·cap·Safe DropZone·OOB·SuddenDeath·reset과 Patch12 기능 검증
- Guest 30초 재접속, Host 종료 처리와 네트워크 진단
- InteractiveLobby는 gameplay·Ready·Start·Customizer 검증용 Greybox 품질로 허용
- Cosmetic은 최소 placeholder catalog로 Paint·배치·저장·P2P relay 기능을 검증
- 사용자-facing 언어는 `Korean-only`(한국어 한 종)이며 Localization StringTable·fallback font와 MainMenu key help는 post-Alpha
- BGM은 0곡이고 기본 combat·weapon·environment SFX만 포함
- 별도 Backend·DB·blob·worker·Dedicated Server·container 0

Alpha 완료는 Steam 제품 통합 완료나 출시를 뜻하지 않는다.

### 10.3 G4 Steam 제품 통합

- Steam auth와 persona
- 검색 비노출 Steam Friends Lobby
- Steam 친구 초대
- Steam Networking Sockets P2P와 SDR
- Steam Lobby 참가 코드
- 실제 Windows Steam 계정 2·3·4인 검증

Steam 참가 코드는 별도 Backend에 저장하는 임의 code가 아니다. `SteamLobbyId`를 사람이 공유할
수 있는 형태로 가역 표현하고 checksum을 포함해 오타를 검출한다. Client는 code에서 LobbyId를
복원한 뒤 Steam Lobby membership과 Steam이 인증한 remote identity를 검증한다. Code 자체를
비밀 credential이나 권한 증명으로 취급하지 않는다.

### 10.4 1.0

- 공식 맵 6개, 검증된 Patch 40개 이상
- Steam Workshop Map Editor·구독·Host/Guest 콘텐츠 확인
- 온보딩, 설정, 오류 처리와 접근성 마감
- MainMenu key help, Localization StringTable·fallback font와 추가 언어
- Host-authoritative Steam P2P/SDR 유지

### 10.5 영구 비범위

- 공개 matchmaking, 공개 Lobby 검색, quick match
- Rank, MMR, leaderboard 기반 경쟁 진행
- 운영형 Dedicated Game Server와 Host Migration
- 5명 이상 플레이
- 자체 account/backend/database 서비스
- 게임 내 음성 채팅
- Console·mobile crossplay
- AI bot과 싱글플레이 캠페인
- 유료 능력치, 장비 파밍과 캐릭터 성장
- 실행 코드 기반 Workshop mod

위 항목은 “향후 검토”에도 두지 않는다. 다시 제안하려면 별도 제품 범위 변경과 사용자 승인이
필요하다.

---

## 11. UI, 아트, 오디오와 접근성

### 11.1 UI

- MainMenu는 Alpha에서 `Host Direct Session`과 `Direct Endpoint Join`을 주 행동으로 제공한다.
- G4 Steam build에서는 `Steam Lobby 만들기`, `Steam 친구 초대`, `Steam 코드로 참가`를 제공한다.
- Lobby 중앙은 실제 3D play view를 우선하고 맵 선택 UI를 표시하지 않는다.
- Guest action slot은 Ready, Host action slot은 처음부터 Start다.
- Start disabled reason은 참가자 수, Guest 연결·Ready·외형과 Host 외형 상태 중 실제 원인을 표시한다.
- `TabOverlay`는 Match 점수와 활성 패치만 표시한다.
- Settings에서 `TabOverlayMode = Hold | Toggle`을 선택하며 기본값은 Hold다.
- Alpha Match의 persistent timer·alive·ammo·killfeed·result panel은 각각 0이다.
- Local Esc menu를 제외한 Player-facing Match gameplay UI는 transient `3·2·1`, Round 사이의 평문
  Patch 선택·결과, transient `OpponentLeft`·`HostLoss`·오류와 on-demand Tab score·active Patch뿐이다.
- Ammo, FireMode, Projectile, Supply와 Patch 진단은 Alpha developer debug에서만 표시하고 Player HUD에는 0이다.
- Match `Esc`는 local-only non-pausing menu이며 local gameplay Input neutral과 close 후 Mouse all-up rearm을 사용한다.
- Alpha Patch 선택 화면은 Trigger·Effect 평문 후보, 남은 시간, 결과와 활성 목록만 제공한다.
- 반복 Supply와 Patch09..12의 기능 상태는 Alpha developer debug와 between-round Patch 결과로만 확인하고 전용 Player HUD·연출을 만들지 않는다.
- 최종 Patch icon·animation·VFX·SFX·layout은 Alpha 범위에 포함하지 않는다.

### 11.2 아트와 오디오

- 캐릭터는 부드럽고 읽기 쉬운 무안면 paint canvas다.
- 플레이어 식별은 색상만 사용하지 않고 marker·outline·label을 함께 쓴다.
- 위험 구간은 색, 형태, 움직임과 경고음으로 함께 알린다.
- Punch, Grab, 무기, 충격과 추락은 서로 구분되는 feedback을 가진다.
- Weapon visual은 기능 ID를 유지한 채 M1911-inspired Pistol, AK-47-inspired LongGun, baseball Bat,
  construction sledgehammer로 구분한다. 실제 모델명·제조사명·logo·각인은 사용자에게 노출하지 않는다.
- Low-poly silhouette는 공용 Camera에서 Pistol의 한손 짧은 body, LongGun의 긴 양손 body, Bat의
  tapered barrel과 Sledgehammer의 큰 head를 서로 구분하게 한다.
- Hybrid animation은 locomotion/jump, 좌우 Punch·Air Kick, Dropkick/Recovery, Grab/Lift/Throw,
  Weapon fire/swing와 Ragdoll/GetUp을 포함하고 Authority state를 read-only로 표현한다.
- 패치 전용 icon·animation·VFX·SFX와 보급 전용 완성 연출은 Alpha 기능 검증 뒤 semantic event를 사용해 후속 제작한다.
- Alpha Audio는 BGM 0곡과 기본 combat·weapon·environment SFX만 사용한다. UI·Patch·Supply 전용
  audio polish와 Music은 post-Alpha다.
- 탈락은 비현실적이고 비폭력적으로 표현한다.

### 11.3 접근성과 설정

Alpha 사용자-facing Text는 한국어 한 종이다. Localization StringTable, 추가 언어와 fallback font는
post-Alpha이며 Alpha 완료 조건이 아니다. MainMenu key help도 post-Alpha로 미룬다.

- Alpha Setting은 명시적으로 승인된 Tab Hold/Toggle만 제공한다.
- Player Identity·Ready·Down·Hazard는 색만으로 구분하지 않고 shape·label·motion·기본 SFX 중
  해당 상태에 맞는 보조 신호를 사용한다.
- 키 재지정, UI Cursor 감도·scale, Camera shake·motion/effect 강도, 자막, 별도 색각 marker,
  패치 문장 재확인과 Master/SFX/UI/Music volume control은 post-Alpha에서 재분할한다.

Alpha는 BGM과 사용자-facing audio channel control이 0이고 승인된 기본 combat·weapon·environment SFX를
고정된 개발 mix로 사용한다.

---

## 12. 제품 요구사항 기준선

| ID | 요구사항 | 목표 단계 |
|---|---|---|
| `PRD-F-001` | Match는 총 2~4인이며 2·3·4인을 각각 검증한다. | Alpha |
| `PRD-F-002` | WASD, Left Shift Sprint, Space와 Ground/Air context의 좌우 tap·hold·dual chord input을 Lobby·Match에서 제공한다. | Prototype |
| `PRD-F-003` | Ground tap은 좌우 Punch, Air single tap은 chord close 뒤 좌우 Kick, Air dual down-edge chord는 즉시 Dropkick, chord 전 hold threshold는 Grab이며 episode당 Air attack은 1회다. | Prototype |
| `PRD-F-004` | AuthorityHost가 실제 Down/Ragdoll 진입별 groggy stack을 판정하되 DropkickRecovery/tumble은 Down이 아니며 Round reset에서 모두 baseline으로 복원한다. | Prototype |
| `PRD-F-005` | 한 개의 Host 계산 SharedGameplayCamera를 플레이어·관전자가 사용한다. | Prototype |
| `PRD-F-006` | OOB와 맵 고유 LethalHazard가 모두 기본 탈락 경로로 성립한다. | Prototype |
| `PRD-F-007` | Round는 60초·Sudden Death를 사용하고 4점 선취로 Match 승자를 정한다. | Prototype |
| `PRD-F-008` | 2인 Round 패자 또는 3·4인 최초 탈락자가 승인 Patch12의 평문 Trigger와 Effect를 골라 다음 Round 전역 패치를 만들고 실제 적용 결과를 확인한다. | Prototype |
| `PRD-F-009` | 활성 패치는 최대 3개이며 FIFO로 만료된다. | Prototype |
| `PRD-F-010` | Round reset은 AirAction·groggy, Incoming/Loose/Held/Spent Weapon, Ammo·Projectile·Recoil/Spread·Supply schedule·Hazard를 복원하고 점수·seed·map·patch를 보존한다. | Prototype |
| `PRD-F-011` | Lobby는 본게임 Controller, Sprint, 물리 장난과 무점수 하늘 재투입을 제공한다. | Alpha |
| `PRD-F-012` | Guest에게 Ready를, Host에게 Ready 없이 같은 slot의 상시 Start Button을 제공한다. | Alpha |
| `PRD-F-013` | 총 2~4명, 모든 Guest 연결·Ready·외형 확정과 Host 외형 확정일 때만 Start를 수락한다. | Alpha |
| `PRD-F-014` | Host 혼자 또는 NotReady·단절·외형 처리 중 Guest가 있으면 Start 수락은 0이다. | Alpha |
| `PRD-F-015` | Lobby StartLever를 사용하지 않으며 P00 Crane lever는 별도 경기 Hazard control로 유지한다. | Alpha |
| `PRD-F-016` | Lobby Esc Cursor와 Match local-only non-pausing menu를 제공하고 local input neutral·held Mouse all-up 이후 Hand/Weapon rearm을 적용한다. | Alpha |
| `PRD-F-017` | C로 Lobby 어디서든 보호된 CharacterCustomizer에 들어간다. | Alpha |
| `PRD-F-018` | 외형 Preset은 local atomic save하고 Host가 source를 검증해 P2P relay한다. | Alpha |
| `PRD-F-019` | 외부 이미지·사용자 scale 없이 최소 placeholder catalog의 고정 크기 3D 치장을 전신에 자유 배치한다. | Alpha |
| `PRD-F-020` | AuthorityHost가 Lobby·Match·Kick·Dropkick·무기·점수·패치와 Scene을 최종 판정하고 Animation root motion은 권한을 갖지 않는다. | Alpha |
| `PRD-F-021` | Alpha는 LAN/direct endpoint로 실제 PC 2·3·4인 전체 흐름을 Greybox Lobby·한국어·기본 SFX 범위에서 완료한다. | Alpha |
| `PRD-F-022` | 별도 Backend, Coordinator, DB, blob, bake worker, Dedicated Server와 container를 사용하지 않는다. | Alpha·제품 공통 |
| `PRD-F-023` | 네 brand-free Weapon, Pistol7 semi-auto·LongGun30 full-auto·reload0, Host Projectile·recoil/spread·Spent와 Air mapping을 보존하는 W1·반복 보급을 Alpha에서 완료한다. | Alpha |
| `PRD-F-024` | 정상 MatchResult 또는 1명 잔존 `OpponentLeft` 뒤 persistent result panel 없이 같은 Lobby로 돌아가 Guest NotReady와 Host Start Gate를 다시 거친다. | Alpha |
| `PRD-F-025` | 2·3·4인은 같은 map geometry·Bounds·Hazard를 사용한다. | Alpha |
| `PRD-F-026` | persistent Match HUD 없이 Settings의 Hold/Toggle에 따른 Tab만 점수·활성 패치를 표시하며 Ammo는 developer debug에만 둔다. | Alpha |
| `PRD-F-027` | G4에서 Steam auth·친구 Lobby·친구 초대·P2P/SDR를 통합한다. | G4 |
| `PRD-F-028` | Steam code는 SteamLobbyId의 checksum 포함 가역 표현이며 별도 Backend를 사용하지 않는다. | G4 |
| `PRD-F-029` | 공개 matchmaking·rank·MMR 경로를 제품과 향후 계획에 두지 않는다. | 전 단계 |
| `PRD-F-030` | Workshop은 승인 data/prefab만 사용하고 Host·Guest 콘텐츠 확인 뒤 시작한다. | 1.0 |
| `PRD-F-031` | 예상치 못한 Guest disconnect는 30초 동안 neutral·physical·vulnerable 상태를 보존하고 명시적 Leave/timeout은 Forfeit한다. Forfeit는 PatchAuthor가 아니며 1명만 남으면 score·Patch 없이 Lobby로, Host Leave·Loss는 Session 종료로 수렴한다. | Alpha |

---

## 13. 품질 Gate

- 2인, 3인, 4인 각각 Lobby→Match→Lobby 전체 흐름을 완료한다.
- Host 혼자, NotReady·단절 Guest와 외형 처리 중 상태의 모든 Start 시도가 거부된다.
- Left Shift Sprint가 Lobby·Match에서 동작하고 Stamina UI·상태가 생기지 않는다.
- 반복 Down/Ragdoll이 같은 Round에서 groggy를 늘리고 다음 Round에 stack 0으로 돌아간다.
- Grab 시도 전에 Punch가 발생하지 않는다.
- Ground L/R quick tap은 해당 손 Punch, Airborne single quick tap은 chord close 뒤 해당 발 Kick이며 chord 전 hold threshold는 Grab이다.
- Airborne dual-click은 승인 chord 안에서 Dropkick 한 번만 만들고 AirAttackToken은 episode당 공격 1회로 제한된다.
- DropkickRecovery·physics tumble은 DownCount·groggy·`TRG-DOWN-EPISODE-START`를 만들지 않는다.
- Kick·Dropkick hit는 `PATCH-PROT-003..004`만 적용할 수 있고 Weapon Patch11·12를 발동하지 않는다.
- Animation은 승인 Hybrid matrix를 표시하되 gameplay root motion과 authority mutation이 0이다.
- 승인 Patch12의 평문 2×2 선택, timeout, 다음 Round 전원 적용, 최대 3개 FIFO가 2·3·4인에서 동작한다.
- 인원별 Supply `10/22/cap2`, `8/16/cap2`, `6/12/cap3`와 Patch09·10 batch admission,
  OOB next-pulse, SuddenDeath cancel, reset이 backlog 없이 Host·Guest에서 일치한다.
- Patch11·12는 Host-confirmed Weapon hit에서만 지정 Held/source Weapon을 강제 Drop하고 Damage·Ammo·cadence와
  파생 Trigger를 바꾸지 않는다.
- Patch 전용 최종 icon·animation·VFX·SFX·layout이 없어도 Alpha 기능 검증을 완료할 수 있다.
- Pistol은 accepted press당 1발·total7, LongGun은 valid hold full-auto·total30이며 reserve/reload가 없다.
- Ammo0 forced release·Spent deadline remove와 next-pulse replacement가 구분되고 Spent가 제거 전 cap을 차지한다.
- Host Projectile은 swept first-hit이며 pierce·ricochet·Client Ammo/Projectile/Hit authority가 0이다.
- Pistol recoil/accuracy와 LongGun deterministic cumulative bloom이 2·3·4인에서 역할을 구분하며
  Playing+SuddenDeath Fire와 RoundResult cleanup이 일치한다.
- 실제 총기·근접 무기 전투가 기본 손 입력, Round reset과 충돌하지 않는다.
- M1911/AK-47은 reference-only이며 사용자-facing 실제 모델명·logo·제조사 marking 없이 네 Weapon
  silhouette가 2·3·4인 Camera에서 구분된다.
- Lobby Cursor open/close 중 held Mouse가 의도하지 않은 Punch·Grab·Throw를 만들지 않는다.
- 외형 save 중 중단돼도 이전 local Preset이 손상되지 않는다.
- 잘못된 외형 source가 Host 검증을 통과하거나 다른 Peer에 relay되지 않는다.
- 120ms RTT와 순간 5% packet loss에서 핵심 이동·Grab·무기·탈락 상태를 계속 검증할 수 있다.
- Host 종료는 Host Migration을 가장하지 않고 모든 Guest를 명확히 Session 종료로 보낸다.
- Match의 persistent timer·alive·ammo·killfeed·result panel이 각각 0이고 Esc menu 외 gameplay UI가 transient
  countdown·between-round Patch·OpponentLeft/HostLoss/error와 on-demand Tab으로 제한된다.
- Match local-only menu 중 simulation은 계속되고 local gameplay Input은 Neutral이며 close 뒤 Mouse
  all-up 전 오발이 0이다.
- 명시적 Guest Leave는 grace를 건너뛰어 Forfeit하고, unexpected disconnect 30초 동안 Character는
  물리·피해·탈락·Camera 규칙을 유지하며 reconnect가 Alive/Spectator 상태로 수렴한다.
- Forfeit는 PatchAuthor가 아니며 permanent participant 2명 이상은 계속, 1명은 score·Patch 0으로
  `OpponentLeft` 뒤 Lobby 복귀, Host Leave·Loss는 Session 종료로 수렴한다.
- Alpha Lobby Greybox, 최소 placeholder Cosmetic, 한국어 단일 언어, BGM 0과 기본
  combat·weapon·environment SFX가 범위대로 검증된다.

---

## 14. 단계별 승인 사항

### Prototype·Alpha 중 확정할 것

- Punch/Grab threshold 120·150·180ms 중 최종 tuning 값
- DualClickChordWindow 60·80·100ms 중 최종값과 `START 80ms` 검증
- Air Kick·Dropkick impulse·knockback·air steer·DropkickRecovery의 최종 tuning
- Sprint multiplier와 가속 보정
- groggy base duration, per-entry increment와 cap
- SharedCamera damping과 2·3·4인 framing
- 승인 Patch12의 개별 물리 `START` tuning과 Patch13..20 확장 조합
- 반복 Supply profile은 승인 `START` 값으로 시험하고 최종 balance는 2·3·4인 Evidence 뒤 고정
- W1 Weapon input안, Airborne WeaponUse 허용 여부·별도 action/mode와 Drop·근접 공격 세부 규칙
- Pistol/LongGun fire cadence, Projectile speed·SphereCast radius·TTL, recoil magnitude/recovery,
  RecoilAccumulator·SpreadBloom 증가·상한·decay와 SpentPendingCleanup 2~4초 시작값
- C1b exact 캐릭터 비율·Collider·reach
- Alpha 공식 맵 1개와 Greybox 2개의 최종 승인

### 제품 후반에 확정할 것

- 최종 게임명과 브랜드
- Steam 가격과 수익 모델
- Steam transport wrapper의 정확한 version
- Workshop 신고·차단·추천 운영 정책

이 목록의 미결정 수치는 명시적 tuning·사용자 Gate 전까지 완료값으로 주장하지 않는다.

---

## 15. 승인 체크리스트

- [x] 2~4인 Host-authoritative 물리 난투와 4점 선취 규칙이 정의됐다.
- [x] Sprint, 손별 Pending 판별, groggy 누적과 Round reset이 정의됐다.
- [x] Ground Punch/Air Kick·Dropkick, hold Grab 우선순위, AirAttackToken과 no-Down recovery가 정의됐다.
- [x] Hybrid animation matrix와 gameplay root motion 0이 정의됐다.
- [x] Host Ready 제거, Guest Ready와 Host Start Button 조건이 정의됐다.
- [x] Lobby StartLever 제거와 P00 Crane lever의 분리가 정의됐다.
- [x] Esc Cursor close·복귀와 held Mouse rearm이 정의됐다.
- [x] Local atomic Preset, Host appearance 검증과 P2P relay가 정의됐다.
- [x] Alpha direct endpoint와 G4 Steam 제품 통합이 분리됐다.
- [x] 별도 Backend·DB·worker·Dedicated Server·container 사용 0이 정의됐다.
- [x] 실제 무기 전투와 W1 결정이 Alpha 범위에 포함됐다.
- [x] Pistol7 semi-auto·LongGun30 full-auto·reserve/reload0, Spent deadline·next-pulse replacement가 정의됐다.
- [x] Host swept Projectile과 bounded recoil·deterministic LongGun SpreadBloom의 Authority 경계가 정의됐다.
- [x] M1911/AK-47 reference-only low-poly archetype, baseball Bat, sledgehammer와 user-visible model name/marking 0이 정의됐다.
- [x] 사용자 노출 명칭 `패치`, 승인 Patch12와 Alpha plain-text 기능 범위, 후속 presentation 경계가 정의됐다.
- [x] 반복 무기 보급의 인원별 START, cap, 결정적 bag·Safe DropZone, OOB·SuddenDeath·reset 경계가 정의됐다.
- [x] 공개 matchmaking·rank·MMR이 현재·향후 범위에서 제거됐다.
- [x] 2·3·4인 검증이 모든 주요 Alpha Gate에 포함됐다.
- [x] persistent Match HUD 0, developer-only Ammo debug와 허용 transient/on-demand UI가 정의됐다.
- [x] Match local-only non-pausing menu와 Guest Leave·disconnect·Forfeit·HostLoss 경계가 정의됐다.
- [x] Alpha Lobby·Cosmetic·언어·Audio 품질 범위와 post-Alpha 항목이 정의됐다.
