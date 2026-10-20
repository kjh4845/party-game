# R20 프레임 독립성 검증 — 2026-09-10

상태: **로컬 prototype 검증 PASS**. 사용자 요구는 렌더 FPS가 이동·타격·회복 결과를 바꾸지 않도록 연산 경로를 확인하는 것이다. 기존 Physics60Hz 설정을 유지하고, 발견된 렌더 의존 경로를 수정했다.

## 발견 및 수정

| 구간 | 확인한 문제 | 반영한 동작 |
|---|---|---|
| 입력 | Update에서 버튼을 읽고 렌더 프레임 길이로 같은 프레임의 tap을 판단 | Input System fixed update에서 원본 timestamp와 순서가 있는 mouse edge를 소비. 양손의 timed release를 먼저 반영해 처리 순서가 다른 손의 tap을 hold로 바꾸지 않게 함 |
| 보행·팔·회복 표현 | LateUpdate마다 상태·blend가 누적 | native PhysicsFixedUpdate 직후 틱당 한 번 계산하고 이전/현재 snapshot 저장 |
| 물리 Root | Rigidbody 표시 보간이 물리 계산의 transform 읽기와 섞일 여지 | Rigidbody interpolation=None, 시각 Pose 자식만 snapshot 보간. LateUpdate는 gameplay state를 진행하지 않음 |
| Down 진입 | 마지막으로 화면에 그린 bone을 physical link 초기값으로 사용 | 마지막 고정 시점의 actor-space bone snapshot을 현재 물리 Root에 배치 |
| GetUp 지지 | 표시 root에서 authored hand 위치를 월드로 변환 | Rigidbody 위치·회전과 authored local target으로 지지 query 계산. 진입 blend도 고정 자세에서 시작 |
| 정밀 판정 rig | dt=0 재동기화에서도 보행 pose를 재평가 | 실제 tick의 양수 dt에서만 보행 상태 갱신 |
| 공용 카메라 | LateUpdate에서 damping 상태 누적 | 고정 시점에서 authority bounds·focus·dolly 갱신, 렌더는 camera snapshot 보간. 빈 참가자 목록은 마지막 구도 유지 |

핵심 구현: `ReviewFixedLoop.cs`, `ReviewInputClock.cs`, `FixedReviewPose.cs`. 연결 코드는 `PhysicsJumpReview`, `SharedArenaReview`, `HandStrikeReview`, `RagdollRecoveryReview`, `SharedCameraReview`, `PunchHitRig`, `GroundHandActionStateMachine`, `GetUpAnimationPose`다. 변경 파일14개의 최종 사본과 SHA-256은 [기계 판독 보고서](R20_FRAME_INDEPENDENCE_REVIEW_20260910.json)에 있다.

## 검증 결과

Unity 6000.3.9f1 / 프로젝트의 기존 PhysX 설정, 고정 step 1/60초에서 검사했다.

- **렌더 일정11종**: 15·20·30·60·90·120·144·240FPS, 불규칙 간격, 주기적인200ms 지연, 표시 생략.
- 일정마다 **2인 / 1,080 physics tick / simulation18초**의 같은 입력을 재생했다. 가속·걷기·뛰기·회전·점프·정지, hold→release throw, 실제 펀치10회→Down→GetUp을 포함한다.
- 매 tick의 입력 결과·점수·Down 단계/시간이 같고, 각 일정 모두 **펀치10 / 피격10 / Throw1 / Down1 / 회복1**이다.
- Root 위치·속도·회전, 보행 phase, 카메라 focus/dolly, 모든22개 리깅 bone의 기준 위치·회전, 활성 물리 부위의 위치·회전·선속도·각속도에서 **측정된 최대 차이0**이다. 표시 호출 전후 물리 Root와 보행 phase가 변하지 않는 것도 검사했다.
- 별도 Input System test runtime에서 timestamp가 있는 양손 native mouse state event를 큐에 넣었다. 15·20·30·60·120·144·240FPS 일정에서 양손147ms tap·153ms hold·10ms tap을 동일하게 분류했다. 150ms 정확한 경계, 좌우 처리 순서, 잘못된 timestamp 이후 상태 보존은 단위 검사로 확인했다.
- 실제 Unity PlayerLoop에서 frame clock을15/120FPS로 바꾸고, **Motor tick당 pose 계산1회**, **물리 실행 이후 snapshot**, **빈 참가자 camera freeze**를 확인했다. 임의 accumulator replay와 native loop 검사를 구분해 수행했다.

| 검사 | 결과 | 근거 |
|---|---:|---|
| 기존 보행·분할 torso·회복·타격·잡기·공용 카메라 및 초기 FPS 검사 | 30/30 | [Play-r04.xml](../../.codex_tmp/frame-independence-20260910/Play-r04.xml) |
| 양손 입력 보강 후 전투·회복·공용 카메라·점프 회귀 | 32/32 | [Play-final.xml](../../.codex_tmp/frame-independence-20260910/Play-final.xml) |
| bone/물리 부위 비교까지 강화한 최종 FPS 검사 | 3/3 | [Fps-final.xml](../../.codex_tmp/frame-independence-20260910/Fps-final.xml) |
| 전체 EditMode | 139/139 | [Edit-final.xml](../../.codex_tmp/frame-independence-20260910/Edit-final.xml) |

위 PlayMode 실행에는 겹치는 검사가 있으며, 서로 다른 검사 수는 **40개**다. [일정별 실제 측정값](../../.codex_tmp/frame-independence-20260910/Replay.json)을 함께 보존한다. 첫 테스트 어댑터는 Input System 내부 API 접근으로 컴파일 오류가 나 수정했고, 그래픽 캡처를 포함한 초기 no-graphics 실행은 중단 후 graphics 실행으로 재검증했다. 최종 통과 결과로 대체하되 초기 로그는 이력으로 남긴다.

## 유지한 범위와 한계

- Walk0.70/Sprint1.05m/s, 펀치0.5초와150ms hold 경계, 부위 점수1/2·기준10→6·Down 시간 증가, 잡기/Throw/점프 하중 정책은 유지한다. 메인 보행과 별도 카메라 보행 후보의 구분도 유지한다.
- 원본 모델·리깅·weight·punch/GetUp clip 등9개 자산의 SHA-256이 이전 보고서와 일치한다. Player Build·commit·push는 하지 않았다.
- 이 결과는 **같은 고정 입력에 대한 로컬 연산 독립성**이다. 실기기의 화면 부드러움, 입력이 OS에 도착하는 지연, 모니터 표시 지연, 서로 다른 하드웨어의 PhysX bitwise 일치까지 보장하지 않는다.
- 기존 Maximum Allowed Timestep은 약333ms다. 이번200ms hitch는 그 안의 조건이다. 333ms를 넘는 멈춤이나 CPU가 지속적으로60Hz 물리를 감당하지 못하는 상황은 실제 시간 대비 simulation이 느려질 수 있다. 실기기 성능/최악 상황 부하와 peer 연동은 후속 Gate에서 검증한다.

## 후속 구현 시 적용 예시

새 움직임을 추가할 때 `FixedUpdate`에서 시간 기반 상태·힘·판정을 진행하고, `ReviewFixedLoop`의 post-physics 시점에서 표시용 snapshot을 만든다. `LateUpdate`는 snapshot을 읽어 보간만 한다. 예를 들어 발의 표시 위치로 착지를 판단하지 않고 실제 contact를 사용하며, 새 Down 부위도 표시 bone 대신 고정 snapshot에서 초기화한다.

회귀 시 `FixedInputClockTests;FrameIndependenceTests` PlayMode filter와 전체 EditMode를 실행한다. 실제 그래픽 캡처가 포함된 기존 회귀군에는 `-nographics`를 사용하지 않는다.
