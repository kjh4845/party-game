# 점프 이후 8방향 모션 전환 검토 — 2026-09-05

> **2026-09-06 영상 정정:** 당시 Unity 캡처에서 skinned mesh 갱신이 누락됐다.
> 수치 검사와 구분해 당시 영상을 팔다리 표현의 시각 증거에서 제외한다.
> [정정된 캡처와 재검증](R20_SKINNING_CAPTURE_FIX_20260906.md)을 따른다.

사용자가 어느 방향으로 점프해도 후속 모션 전환이 어색하지 않기를 요청했다.
기존 Unity 물리 시제품에 8방향 자동 facing, 보행/Sprint와 점프·착지·정지 blend를 연결했다.
전용 Jump clip0 방침, 승인 모델·하체 웨이트·펀치 원본은 유지했다.

## 동작 변경

- 실제 수평 속도로 보행 방향·강도를 계산하고, 멈추면 보행 강도가 줄어 대기 자세로 돌아간다.
- Jump/착지 시 발 주기를 처음부터 재시작하지 않는다. 착지 압축 중에도 이동과 보행을 이어 간다.
- 공중 다리 접기는 무릎 각도에 가까운 공간에서 속도를 제한한다. 하강 시 바닥 거리로 미리 풀어 착지 순간의 급격한 접힘/펴짐을 줄인다.
- 몸의 yaw는 실제 이동 방향을 향하는 제한된 Rigidbody torque로 변경한다. 공중 반전에도 순간 회전은 사용하지 않는다.
- Shift hold는 이동 배율과 보행 리듬/진폭을 함께 전환한다. 기존 속도1.5m/s와 점프 설정은 유지하며 Sprint 배율1.5는 START다.
- 빈 팔의 작은 Walk swing·큰 Sprint swing은 별도 layer다. 점유/공격 중인 팔은 쓰지 않는다.
- Reset은 별도 version으로 전달해 이전 보행·공중 자세의 잔여 값을 비운다.
- 작은 발 정렬 각도가 소실되지 않도록 회전 계산을 보완했다. 다리 길이나 skin weights를 바꾸지 않는다.

지상/공중 회전 속도360°/240°/s, 최대 stride16/24mm, cadence1.65/2.5Hz와 blend 계수는 시제품 START다.
이는 최종 이동 속도·보폭 tuning이나 UG-C2 체감 승인을 대신하지 않는다.

## 실행한 조건

| 범위 | 실제 검토 |
|---|---|
| 방향8 | 앞·뒤·좌·우·대각선4, 이동 중 점프 → 착지 후 이동 → 정지 |
| 공중 제어3 | 180° 반전, 90° 방향 변경, 공중 입력 해제 |
| Sprint2 | 달리며 점프·착지, 착지 무렵 Walk→Sprint |
| 상태3 | 착지 직전 입력 buffer로 재점프, 팔 점유 시 보호, 공중 Reset |
| 화면 갱신률2 | 같은 전방 시나리오를30/120fps로 추가 실행; 나머지60fps |

총18조건·실제 PhysicsScene3,780step(60Hz), 전체 EditMode85/85·PlayMode15/15 통과, skip0이다.
PlayMode의 전환 시험1개가 위18조건을 반복 실행한다. 초기/기존 물리 시험도 함께 통과했다.

점프·착지 경계의 관절 변화, 물리 yaw 변화, 발 목표 오차, 보행 주기 증가량,
정지 후 대기 자세 복귀와 팔 소유권을 검사했다.30fps에서 물리 접촉이 pose 갱신 사이에 발생해도
다음 pose 갱신까지 경계를 보존해 검사한다. Reset의 의도된 순간 위치 변경은 연속 전환 판정에서 제외한다.

- 일반60fps 조건의 최대 점프/착지 경계 관절 변화: 약13.20°/pose update.
- 30fps 전방 조건의 경계 변화: 약19.35°/pose update;120fps는 약6.46°/pose update.
- 물리 yaw 최대 변화: 약5.99°/60Hz step. 실제 heading을 즉시 대입하지 않는다.
- 18조건 모두 정지 후 하체 local rotation이 초기 대기 자세로 복귀했다(검사값0°).
- 발 IK 목표 오차 최대 약2.49e-7m, 검사한 stride의 도달 범위 보정0m.
- 매3번째 물리 step에서 측정한 body surface의 최대 바닥 침범은 약2.88e-7m였다.

위 수치는 국소 전환 검사값이다. **발을 월드 위치에 고정했다거나 미끄러짐이 없다는 측정은 아니다.**
현재 보행은 in-place 시각 표현이며 최종 속도와 보폭의 일치·stance foot 고정은 후속 tuning이다.
Unity 전체 면 뒤집힘/자기 교차 전수 검사는 이번 범위에 포함하지 않았다.

## 산출물·실행

[구조화 증거](R20_JUMP_TRANSITION_REVIEW_20260905.json)에 원시 시나리오 기록, 검사 XML, 원본 승인,
소스 snapshot과 영상 hash를 연결했다. 실제 검토 폴더는
`.codex_tmp/jump-transitions-20260905/Review-20260905-123535/`다.

- `EightDirectionTransitions.gif`: 8방향과 공중 반전 비교.
- `AirAndSprintTransitions.gif`: 공중180°/90° 변경·정지·Sprint 착지 비교.
- `TransitionContactSheet.png`: 착지 직후 정적 비교.
- `SourceSnapshot/`: 검증 당시 C# 원문. 이후 작업 트리가 바뀌어도 검토 입력을 확인할 수 있다.

영상은20fps로 추출한70시점이며 마지막 정지와 반복 안내 화면을 붙였다.
반복 안내 뒤의 초기 위치 복귀는 검토 영상의 새 실행이며 gameplay 전환을 뜻하지 않는다.

Unity 메뉴 `Project Hotfix → Prototypes → Open Physics Jump Review`를 열고 Play한다.
Space 점프, WASD 월드축 이동, Left Shift 달리기, R 초기화다. 실제 키보드 수동 사용성 검토는 별도다.
모델 transport 준비와 프로젝트 버전은 [물리 점프 시제품 기록](R20_UNITY_PHYSICS_JUMP_REVIEW_20260905.md)을 따른다.

현재 핵심 변경은 `Runtime/Presentation/ProceduralJumpPose.cs`, `ProceduralFreeArmSwing.cs`,
`Runtime/Contracts/JumpPresentationState.cs`, `Runtime/Simulation/PhysicsJumpStateMachine.cs`와
`Prototypes/PhysicsJump/`다. 경로 기준은 `Project hotfix/Assets/ProjectHotfix/`다.
Simulation/Presentation 모듈 경계와 Runtime 전역 물리 cadence 불변 검사도 통과했다.
문서 baseline·workflow tests7/7, art profile·tests25/25(221 assertions), `git diff --check`와
원본/승인 파일·코드 snapshot·결과/영상 hash를 확인했다. ProjectSettings·Packages·기존 Art 파일 변경은0이다.

## 남은 범위

평지의 독립 시제품이다. 화면축 본게임 입력·camera, stance foot의 완전한 월드 고정,
경사/단차/moving platform 발 IK, 실제 hand action 소유권 연결, 다관절 Ragdoll·Down/GetUp·network 통합은 후속이다.
CHR-003/004/005·ANP-002는 IN_PROGRESS이며 전체 게임/최종 모델 Gate 완료로 승격하지 않는다.
원본 r16/r20·승인 하체 웨이트·펀치 변경, 새 Jump clip·Player Build·commit/push는0이다.

중간 회귀 실행1회는 테스트 시작 전 Unity Mono native crash로 결과가 생성되지 않았다.
실패 실행을 PASS로 사용하지 않았으며 이후 재실행과 최종 전체 시험 결과만 위 증거로 채택했다.
