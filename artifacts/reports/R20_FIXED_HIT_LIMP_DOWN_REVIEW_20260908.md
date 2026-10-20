# 고정 타격 수·대기 시간 증가·무력 Down

사용자 최종 선택에 따라 넉다운에 필요한 타격 수를 고정하고, 반복 넉다운의 대기 시간만 늘린다. 이전 타격 임계값 감소 방식은 비교 이력으로 보존하며 현재 runtime에서 제거했다.

## 현재 규칙

| 항목 | 현재 로컬 시제품 |
|---|---|
| 넉다운까지 필요한 펀치 수 | 매번 5회 START |
| 비피격 시간에 따른 감소 | 없음 |
| 새 Down 발생 | 현재 피격 누적만 소비, Round DownCount 유지 |
| Down 대기 시간 | 기존 START 1.5 → 2.0 → 2.5 → 최대 3.0초 |
| Down 중 추가 타격 | 물리 반응만 적용. 누적·새 Count·timer 연장 0 |
| Round reset | 피격 누적·Count 초기화 |

5회와 시간 수치는 검토 시작값이며 최종 balance는 플레이테스트에서 정한다. `PunchDownProfile`은 고정 `RequiredHits` 하나만 보유한다. Reduction/Minimum과 DownCount 입력을 누적 API에서 제거해 반복 Down이 타격 임계값을 바꾸지 못하게 했다.

## 힘 풀린 인형 표현

넉다운 대기 중에는 자세 spring/damper/maximumForce가 0이며, 게임 이동 구동과 GetUp animation을 적용하지 않는다. 실제 dynamic Rigidbody의 위치/회전을 보이는 뼈에 직접 전달한다. 관절 연결·회전 한도·중력·충돌과 수동 감쇠는 유지한다. 바닥에 특정 자세로 붙이거나 강제로 눕힌 pose를 재생하지 않는다.

회복 중 지지/공간을 잃어 다시 쓰러질 때도 이전 GetUp clip의 fade를 즉시 끊는다. 따라서 Down 구간에 회복 자세 보간이 남지 않는다. 대기 종료 후 지지/공간이 확보되면 기존 골반·하체 GetUp으로 일어난다. GetUp asset과 기립 후 handoff 순서는 유지한다.

관절을 크게 푼 첫 후보에서 측면 낙하 중 어깨/하체의 표면 뒤집힘을 발견해 채택하지 않았다. 작게 완화한 두 번째 후보도 표면/관절 간격 기준을 통과하지 못해, 최종 후보는 기존 검증된 관절 한도와 수동 감쇠를 유지한다. 최대한 넓은 각도 자체를 무력 상태의 기준으로 삼지 않는다.

고정5회 반복 피격 조건의 순간적인 joint gap을 확인했다. solver 증가 시도는 기립 후 흔들림이 생겨 채택하지 않았다. 최종 수정은 타격 임계 도달 시 진입을 예약하고 다음60Hz motor 구동 이전에 Ragdoll로 전환하는 것이다. 이미 예약된 이동 force/torque 아래에서 mass/inertia를 바꾸지 않으며, 해당 임계 타격 impulse는 전환 시 한 번만 적용한다. Reset/비활성/권한 해제에서는 예약 진입을 취소한다. 기존 solver 설정과 회복 handoff는 유지한다.

현재 10 body 구성의 몸통/발목 근사는 유지한다. 모든 척추/손가락을 개별 물리 body로 만든 해부학적 리그는 아니다. 원본 mesh/weight/rig를 변경하지 않는다.

## 실행·검증

Unity `Project Hotfix > Prototypes > Open Punch Down Review` → Play. 흰 캐릭터로 주황 표적을 때려 실제 넉다운과 회복을 확인한다. R로 초기화한다. 방향별 낙하 검토는 `Open Ragdoll Recovery Candidate`의 1/2/3/4를 사용한다.

- 실제 입력/sweep으로 네 episode 모두 5회와 대기 시간 1.5/2.0/2.5/3.0초를 검사한다.
- 첫 타격 뒤 10초 비피격 누적 유지, Down 중 추가 impulse와 timer/Count 불변, wall/hold/release Throw/일반 충돌 제외, Reset을 검사한다.
- 앞/뒤/좌/우·천장·중단/재시작 등 10조건에서 모든 무력 sample의 drive 0, clip playing/fade 0, 보이는 물리 뼈의 회전 일치를 검사한다.
- 표면 인접각 45° 미만, joint gap 15mm 미만, 회복/이동 복귀와 기립 직후 흔들림의 기존 기준을 유지한다.

최종 수치, 테스트 XML, 원본 보존 hash와 영상 출처는 [구조화 기록](R20_FIXED_HIT_LIMP_DOWN_REVIEW_20260908.json)에 보존한다. 로컬 시제품 검증이며 실제 peer·다인 수동 playtest·정식 게임 통합·최종 balance 완료를 뜻하지 않는다. Player Build·commit/push는 실행하지 않는다.

## 최종 검증 결과

- EditMode 117/117·PlayMode 42/42 통과, skip 0.
- 실제 네 episode의 타격 수 5/5/5/5, 대기 시간 1.5/2.0/2.5/3.0초와 네 번의 회복을 확인했다.
- 10조건의 무력 구간에서 drive0·animation/fade0·물리 뼈 회전 일치와 기존 변형 기준을 검사했다. 최대 인접 표면각 40.80°, joint gap 14.16mm였다.
- 10초 비피격 누적 유지·추가 타격/Throw/Reset·기립 후 흔들림/이동 검사를 통과했다.
- 원본/보존 Blender 네 파일과 두 회복 clip·표현 소스 hash 일치, 문서 baseline·workflow 7개·diff check 통과.
- 영상 원본 140프레임·20fps·7초, 정지/반복 안내를 포함한 GIF 8초를 확인했다.
