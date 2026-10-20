# 넉다운 누적·회복 대기·입력 잠금 검토

CHR-008의 확정된 상태 규칙을 구현하고 기존 회복 방향 판별과 연결했다.
이 단계는 **권한 상태와 조작 잠금 검토**다. 실제 다관절 Ragdoll, 피격에 의한 자동 Down, GetUp pose와 지지면/공간 physics query는 아직 연결하지 않았다.
따라서 기존 잡기 장면에서 캐릭터가 쓰러지는 연출까지 완성했다는 뜻은 아니다.

## 확정 규칙

- Match Round 첫 Down은 Base, 이후 새 Down마다 Increment를 더하고 Max에서 제한한다.
- 같은 episode의 중복 Contact는 Count와 대기 시간을 추가하지 않는다. 회복 중 다시 통보된 Down도 같은 episode다.
- Lobby Down은 항상 Base이며 Count0이다. Round reset은 Count·남은 시간·회복 phase를 초기화한다.
- 대기 시간이 끝나도 자동으로 일어나거나 조작을 풀지 않는다. 실제 지지·공간 확인→방향/side 정렬→GetUp 완료 순서를 기다린다.
- 4방향 판별과 side→공통 FaceUp/FaceDown 정책을 재사용한다. 정렬 중 지지 상실은 같은 episode로 다시 평가하며 Count/시간을 늘리지 않는다.
- DropkickRecovery는 별도 Kind로 Count·Groggy0을 유지한다. 실제 Down으로 전환될 때만 Down1회다.
- generation·episode·입력 sequence를 검사해 이전 Round의 Down이나 이전 GetUp 완료가 현재 episode를 바꾸지 못하게 한다.
- 넉다운 중 이동·Sprint 구동·새 점프·펀치·잡기·던지기를 막는다. 기존 자기 손 잡기는 Throw 없이 해제하고 대기 점프도 지운다.
- 동적 Rigidbody의 중력/충돌/기존 관성은 계속 계산한다. 메뉴는 Down clock을 멈추지 않는다.
- 회복 완료 후에도 마우스가 눌려 있으면 자동 재잡기하지 않는다. 두 버튼 up 관측 후 새 press가 필요하다. 열려 있는 메뉴는 그대로 유지한다.

## 코드와 검토 방법

- `Runtime/Simulation/TimedDownStateMachine.cs`: 명시적 timing profile, 누적/시간과 기존 방향 회복의 단일 진입 경로.
- `Runtime/Simulation/CharacterRecoveryStateMachine.cs`: Match Count 여부를 분리해 Lobby에서 Count를 올리지 않는다.
- `Prototypes/PhysicsJump/DownStateReview.cs`: opt-in 로컬 상태/입력 잠금 어댑터. 자동 피격 판정과 테스트용 시간을 기본 게임 설정에 주입하지 않는다.
- `PhysicsJumpMotor`, `HandStrikeReview`, `GrabPhysicsReview`: 권한 보유와 행동 잠금을 구분한다. Down 때문에 AuthorityEnabled를 바꾸지 않는다.

재현: Unity EditMode `TimedDownTests`, PlayMode `DownStateReviewTests`.
테스트가 사용하는 Base1.5초·Increment0.5초·Max3초는 누적/상한 검사용 값이다. 기본 게임 수치나 사용자 승인값이 아니다.
예: fixture에서 Match의 각 episode는1.5→2.0→2.5→3.0→3.0초이며 Lobby는 매번1.5초/Count0이다.
프로덕션 profile의 정확한 값은 문서 SRS-PHYS-011대로2/3/4인 플레이테스트 뒤 고정한다.

EditMode는 누적/상한, Lobby, 시간 만료 후 잠금, 지지/공간 gate,4방향 정렬/중단, Dropkick 분리, stale/reset, 무효 profile/clock을 검사한다.
PlayMode는 Match/Lobby, release 큐 직후 Down, Reset, 메뉴, 권한 중단6조건에서 실제 Rigidbody와 기존 grab/input 경로를 사용한다.
GetUp query 결과와 완료는 명시적으로 주입하는 fixture이며, 실제 Ragdoll/GetUp의 물리·시각 증거로 사용하지 않는다.

## 의견이 필요한 다음 연결

피격→Down 발동 기준이 기존 문서에 없으므로 사용자에게 질문했다.
이전 제안의 “강한 충격”에는 던진 뒤 충돌까지 포함되어 있었으나, 사용자 후속 결정으로 해당 일반 충돌의 **추가 Damage·피해 누적은0**으로 확정했다.
벽→바닥→재충돌의 연속 접촉에서도 피해를 추가하지 않는다. 물리 반응·기존 명시적 Hazard/OOB 및 별도 유효 공격의 계약은 유지한다.
공격별 Down 기준·임계값·누적량·감소율과 피해 없는 순수 물리 Down 여부는 미확정이다. 충돌 기반 자동 Down은 추가하지 않았다.
답변 후 해당 판정과 실제 Ragdoll/회복 연결을 진행한다. 현재 상태 기반만으로 CHR-008/009 전체 완료를 선언하지 않는다.

## 검증 결과

전체 EditMode108/108·PlayMode29/29 통과, skip0. 새 검사는 EditMode10개와 PlayMode6조건이다.
문서 baseline·workflow7개·diff check를 통과했고 원본/승인 후보4개 파일의 SHA-256도 유지됐다.
[구조화 결과와 소스 snapshot](R20_DOWN_STATE_REVIEW_20260906.json)을 참고한다.

후속 결정 반영 검증: 기존 local 코드에 충돌 Damage/자동 Down 연결이 없음을 확인했다. 이번 변경은 규칙 문서와 결정 기록이며 런타임 피해 구현 완료를 뜻하지 않는다.

후속: [실제 지지면·기립 공간 query](R20_RECOVERY_SUPPORT_REVIEW_20260906.md)가 위 상태 어댑터에 선택적으로 연결됐다. 위 원래6조건의 query 주입 검증은 당시 상태 검토 이력으로 보존한다.

2026-09-08 후속: [펀치 피격→Down 연결](R20_HIT_DOWN_INTEGRATION_REVIEW_20260908.md)에서 시간 감소 없는 피격 누적·Down 중 impulse만 적용·반복 Down의 타격 임계값 감소를 구현한다. 위 미확정 문단은 질문 당시 이력이다. 기존 timed state 자체에는 타격 임계값 감소가 없었다.
