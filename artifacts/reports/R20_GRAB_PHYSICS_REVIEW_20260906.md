# 잡기·해제·하중 피로 시제품 — 2026-09-06

손 입력/타격 시제품의 다음 단계로 GrabSeek를 실제 접촉·ConfigurableJoint와 연결했다.
사용자는 [피로 정책](R20_GRIP_STRESS_DECISION_20260906.json)에서 **힘을 버틸 때만 피로 누적**을 선택했다.
현재 모델·리그·승인 접지와 펀치는 유지한다. 이것은 로컬 단일 capsule 기반 시제품이며 최종 게임용 physics profile 승인이 아니다.

## 구현한 동작

- 손마다150ms hold 뒤 GrabSeek,120ms reach를 거쳐 유효 접촉이 있으면 Grabbing으로 전환한다.
- 명시적으로 GrabEligible인 동적 Rigidbody만 잡는다. 같은 대상의 양손 잡기와 좌우 서로 다른 대상도 가능하다.
- 권한 물리 query가 접촉점을 선택한다. 팔 pose에서 contact나 joint를 역으로 만들지 않는다.
- 접촉 순간 서로 일치하는 두 local anchor를 연결한다. joint의 이동3축은 Locked, 회전3축은 Free,
  projection은 None이며 연결된 두 body 사이 collision을 유지한다. 대상 transform을 손에 붙이거나 순간이동하지 않는다.
- 손을 놓으면 즉시 constraint 축을 Free로 풀고 joint를 제거한다. 현재 속도를 지우거나 추가 Throw/Strike impulse를 넣지 않는다.
- 메뉴·Reset·손 제어권 상실·대상/접촉 collider 상실·강한 충격·도달 범위 이탈·피로 한계에서 해제한다.
- 강제로 놓친 뒤 버튼을 계속 누르고 있어도 자동으로 다시 잡지 않는다. 놓고 새로 눌러야 한다.
- 한 손으로 잡은 상태에서 반대 손 펀치와 점프가 가능하다. 점프의 물리 impulse는 기존 방식이다.

## 피로·물리 시작값

| 항목 | START |
|---|---|
| 탐색 위치/반경 | root 기준 x=±0.12m, y=0.54m, z=0.32m /0.05m |
| 손별 접촉 경로 | 자기 손 앞쪽 lane의 collider 표면을 선택하고 몸에서 가림을 재검사 |
| 접촉 도달 범위 | 권한 shoulder origin에서0.18~0.37m, 유지 중0.375m 초과 해제 |
| joint gap | 0.08m 초과 해제 |
| 순간 joint break force/torque | 2000N /2000N·m |
| 피로 기준 힘 | 250N, 실제 joint force/250의 비율 사용 |
| 피로 누적 | load ratio0.25 초과에서 `(ratio−0.25)×0.8/초` |
| 피로 회복 | 낮은 부하에서0.35/초. 누적값1이면 해제 |
| 잡은 하중의 이동 구동력 | 중복 제외한 대상 질량 비율만큼 가속 구동력을 보완하되 최대2배. 최고 이동 속도와 base mass 불변 |

가만히 잡고 있는 시간만으로 자동 해제하지 않는다. 작은 부하의 무시 범위와 누적/회복률·힘 수치는
최종 승인값이 아닌 START다. 순간 강한 충격의 joint break와 지속 하중의 피로 해제는 별개다.
Sprint가 joint strength나 mass를 바꾸지 않으며, 놓거나 초기화하면 이동 하중 보완도1배로 돌아간다.
기존 Walk0.70/Sprint1.05m/s 설정과20kg 검토 캐릭터 질량을 유지한다. C2의30kg 및 다관절 배분은 별도다.

## 팔 표현 보정

처음의 어깨 최근접점 선택은 손을 가슴 안쪽으로 모아 중간 pose의 표면 뒤집힘을 만들었다.
손별 앞쪽 접촉점과 낮춘 접촉 높이를 사용하고, 승인 펀치의 clavicle 방향을 참조한 약9° 전방 shoulder pose를
잡기 전용으로 적용했다. bind frame에서 팔 IK를 풀며, 뼈 길이나 source mesh/weights를 바꾸지 않았다.
물리 목표를 읽는 `GrabArmPose`는 해당 팔4bone만 소유한다. 반대 손 펀치를 덮어쓰지 않으며,
다른 동작이 소유권을 가져가면 쓰기를 중단한다. 놓을 때는 Idle/빈 팔 swing으로 부드럽게 돌아간다.

## 검증

- 실제 물리22조건: 좌/우·양손, 끌기, 잡기+펀치, 잡은 채 점프, 서로 다른 대상, 벽/거리 밖,
  메뉴/Reset, 대상·collider 비활성/삭제/초기화, 손 점유, 거리/충격/피로 해제, 정지 유지,
  Presentation 없는 물리,2kg Box prop.
- 새 순수 상태 검사3개: 시간만으로 피로 증가0, 하중 누적·회복·해제, 관계 중복/재생성·놓기/강제 이탈의 Punch0.
- 10분의 무부하 상태 계산과10초의 정지 물리 유지에서 피로0·자동 해제0.
- 지속 하중 fixture에서 실제 joint force 약110N을 관측했고, 하중 시작 뒤 약6.7초에 피로1로 해제했다.
  이 fixture는 재현을 위해 actor 위치와 대상 회전을 고정하고 대상에200N을 가했다. 전체200N이 joint에 전달된다는 뜻은 아니다.
- 잡은 채 점프는 연결을 유지했고, 반대 손 펀치는 실제1회 적중했다. 강한 외부 속도 변화에서는 joint break를 확인했다.
- 끌기 구간의 표적 이동 약0.348m. Presentation 적용 여부에 따른 물리 이동 차이는 검사 허용값0.1mm 미만이었다.
- 손끝-접촉점 오차는 수치 오차 수준, 팔 도달 길이 보정0. 입력 release가 target velocity를 직접 바꾼 양0.
- 256개 표면 표본에서 인접 면45° 이상 꺾임0,90° 이상 뒤집힘0. 최대 인접 면 각도 약44.59°.
  끌기 구간과 놓는 전환, 잡은 채 점프를 연속 표본으로 검사했다. 비인접 자기 교차 전수 보장은 아니다.
- 전체 EditMode96/96·PlayMode25/25 통과, skip0. 이후 접촉 collider 비활성 해제를 추가하고 영향받는22조건을 다시 통과했다.

이 검사는 정해진 평지·입력/외력 범위다. 모든 지형·입력 방향·대상 형상에 대한 자연스러움이나 게임 재미를 보장하지 않는다.
Root capsule와 실제 skin 표면의 차이, 기존 발 접촉 잔차와 지형별 IK, 최종 Collider/Joint profile 검토는 남아 있다.

## 실행·다음 단계

Unity 메뉴 `Project Hotfix → Prototypes → Open Grab Physics Review`에서 Play한다.
WASD/Shift/Space 이동·점프, LMB/RMB 짧게 놓으면 펀치, 길게 누르면 해당 손 잡기, 버튼을 놓으면 해제한다.
Esc는 입력만 취소하며 physics를 멈추지 않는다. R은 캐릭터·표적·joint를 초기화한다.
주황색 캐릭터는 수동 물리 표적이며 두 번째 플레이어 조작/회피 AI는 아직 없다.

다음 범위는 실제 Lift·Throw, 피해/Down/회복, Air Kick/Dropkick과 실제 두 플레이어 입력·체감 연결이다.
현재 해제 시 이동 관성은 유지하지만 추가 던지기 impulse와 Lift 동작은 구현하지 않았다.
Static Wall/Ledge/Beam 매달리기·ClimbAssist, Patch Grip modifier·network·production Prefab/Gate도 후속이다.
원본 모델·리그 재작업은 지금 하지 않고, 사용자가 정한 첫 통합 플레이테스트에서 필요성을 다시 판단한다.

[구조화 기록](R20_GRAB_PHYSICS_REVIEW_20260906.json)에 START 값·원시22조건·소스 snapshot·영상·검사 XML을 연결한다.
모든 영상은 pose 적용 뒤 실제 player-loop를 넘겨 촬영한다. 원본/승인 source 저장·Player Build·commit/push는0이다.

영상: `.codex_tmp/grab-physics-20260906/Review-20260906-072349/Media/GrabPhysics.gif` / `.mp4`. MP4 디코딩120frame·60fps·2초를 확인했다. GIF에는 정지와 구간 반복 안내를 추가했다.

문서 baseline·workflow7/7·art profile 및 tests25/25(221 assertions)·`git diff --check`와 원본/승인 모델 파일 SHA256 보존도 확인했다.
