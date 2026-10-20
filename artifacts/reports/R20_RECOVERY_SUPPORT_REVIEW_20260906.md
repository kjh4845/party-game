# 회복 지지면·기립 공간의 실제 물리 query 검토

CHR-009의 지지면/공간 검사를 실제 Unity PhysicsScene query로 구현하고 opt-in Down 상태 어댑터에 연결했다.
이전 단계에서 테스트가 직접 넣던 supported/clear 조건을 실제 collider, torso pose와 상대 속도로 계산한다.
**회복 시작 가능 여부까지 구현한 단계**이며 실제 다관절 Ragdoll·side 정렬 운동·GetUp pose와 완료 연결은 후속이다.
기존 잡기 검토 장면에 자동 피격/넉다운이나 가짜 쓰러짐 모션을 추가하지 않았다.

## 판정 규칙

1. torso 중심 아래 SphereCast에서 본인 collider를 제외한 가장 가까운 표면을 찾는다. 너무 가파른 표면 아래 바닥을 대신 고르지 않는다.
2. 지지면 법선과 본인 실제 collider의 ClosestPoint를 확인한다. 바닥이 가까워도 몸과 간격이 허용치를 넘으면 지지 없음이다.
3. torso와 발판의 상대 선속도·상대 각속도가 제한 이내이고 같은 지지가 일정 시간 유지되어야 안정 상태다.
4. 발판 변경·상실·큰 접촉점 이동·법선 변화가 있으면 안정 시간을 다시 센다. 움직이는 발판의 접촉점은 발판 좌표로 추적한다.
5. 지지점 위에 실제 기립 capsule 크기를 적용해 OverlapCapsule로 머리·몸통 전체 공간을 검사한다. 본인 collider만 제외하며 다른 캐릭터도 공간을 막는다.
6. Down 대기 시간 만료+안정된 지지+공간 확보 뒤 실제 torso rotation으로 FaceUp/FaceDown 또는 side 정렬 phase에 들어간다.
7. 정렬/GetUp 중 지지나 공간이 사라지면 같은 episode의 PhysicsTumble로 돌아간다. Count와 episode를 늘리거나 타이머를 다시 시작하지 않는다.
8. 정렬 운동이나 GetUp 완료를 query만으로 합성하지 않는다. 회복 완료 통보 전 행동 잠금은 유지한다.

기립 공간 검사는 최종 서 있는 capsule 공간의 검사이며, 일어나는 중간 동작 궤적의 전수 충돌 검사는 후속이다.
query는 Rigidbody/Transform/Collider 위치를 바꾸지 않는다. 판정에 렌더된 뼈나 카메라를 사용하지 않는다.
일어설 capsule은 기존 locomotion처럼 world-up을 유지하며, 하단 구체만 지지면 법선에 맞춰 띄워 경사면 내부에서 시작하지 않게 한다.
SupportMask는 기본 Raycast layer(기존 검토 캐릭터의 Ignore Raycast layer 제외), ClearanceMask는 모든 layer다.
Trigger는 두 query 모두 제외한다. query 버퍼가 가득 차면 회복을 허용하지 않는다.

## 시제품 설정

현재 query 시작값은 ProbeRadius0.06m, ProbeDistance0.35m, 지지 간격 최대0.03m, 최대 경사50°,
안정 시간0.20초, 상대 속도0.15m/s 이하, 상대 각속도0.5rad/s 이하, 기립 공간 여유0.005m다.
기립 radius/height는 호출자가 제공한다. 검토는 기존 capsule과 같은0.17m/1.0m를 사용했다.
이는 최종 Ragdoll collider/profile·게임 밸런스 승인이 아니며 실제 몸통/팔다리 구성에 맞춘 후속 조정이 필요하다.

## 검증 범위

`RecoverySupportProbeTests`의 query15조건:
평지,25° 경사,70° 급경사,공중,접촉 없이 낮게 떠 있는 상태,천장,다른 캐릭터 layer의 장애물,
trigger 천장/바닥,torso 이동/회전,발판만 이동,torso와 발판의 같은 속도,지지 상실/교체.

- 실제 collider가 query 대상이며 움직임/회전의 상대 속도 조건은 fixture가 Rigidbody 값으로 제공한다.
  동적 이동 발판에 실제로 매달려 이동하는 장기 시뮬레이션이나 최종 Ragdoll 안정성의 증거는 아니다.
- 충분히 안정되기 전 시작0, 공중/과도한 간격/급경사/trigger 바닥은 지지0, 천장/다른 캐릭터는 clearance0.
- 같은 속도로 움직이는 발판과 torso는 상대적으로 안정, 발판만 움직이면 불안정이다.
- 각 query 전후 torso 위치/회전 불변을 검사한다.
- 별도 상태 통합 검사는 FaceUp/FaceDown/Left/Right4진입에서 실제 천장 query로 대기→장애물 제거 후 phase 진입→재차 막히면 중단→Reset을 확인한다.
  GetUp 완료를 자동 통보하지 않아 시간이 더 지나도 행동 잠금이 유지되는지 검사한다.

재현: Unity PlayMode 필터 `RecoverySupportProbeTests`.

## 후속

다음은 실제 Ragdoll 구성과 안정된 물리 side 정렬, 공통 FaceUp/FaceDown GetUp 동작 연결이다.
공격별 피해/Down 발동값은 여전히 미확정이다. 사용자 결정대로 던진 뒤 일반 충돌 Damage·피해 누적0을 유지한다.
이번 query에서 피해·Down 생성·기존 중력·잡기 점프 지원·던지기 세기를 변경하지 않는다.
원본 모델/리그 저장, Player Build와 commit/push는 수행하지 않는다.

전체 EditMode108/108·PlayMode31/31 통과, skip0. 문서 baseline·workflow7개·diff check 통과, 원본/승인 후보4개 SHA-256 유지.
[구조화 결과와 소스 snapshot](R20_RECOVERY_SUPPORT_REVIEW_20260906.json)을 참고한다.

후속 [래그돌·물리 회복 후보](R20_RAGDOLL_RECOVERY_REVIEW_20260906.md)가 회복 시작 판정 뒤의 실제 물리 정렬·기립 보조를 별도 검토 장면에 연결한다. 위 query-only 검사는 당시 범위로 보존한다.
