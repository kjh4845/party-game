# R20 유한 경기장·추락·카메라 검토 — 2026-09-10

이 경계 검토 뒤 정적 가장자리 잡기·복귀가 [후속 기록](R20_LEDGE_RECOVERY_REVIEW_20260910.md)으로 연결됐다. 아래 미연결 목록은 경계 검토 당시의 범위다.

상태: **사용자 승인·기본 시제품 채택 완료**. 승인된2배 이동 속도를 유지하고 P00 문서의 시작 크기에서 이동·추락·카메라를 연결했다. 사용자가 이 검토안 채택과 가장자리 복귀 진행을 선택했다. 기본 Shared Arena 2/3/4 Players 메뉴도 이 유한 경기장을 연다.

## 이번 범위

- 현재 시제품의 서 있는 authority capsule 높이1m를1CU로 측정한다. 제작용 C1B/Prefab의 최종 height LOCK은 아니다. Scene Root나 모델 scale을 숨겨 바꾸지 않는다.
- 상판8.8×6.2CU, 두께0.28CU. 네 방향에 벽·투명 barrier·자동 반발을 만들지 않는다.2·3·4인 모두 같은 geometry를 쓴다.
- 중앙에서1.85CU 떨어진 N/E/S/W spawn을 사용한다. 생성 시 layout index로2인 대향축,3인 빈 cardinal,4인 배정을 회전할 수 있다. R은 현재 layout으로 복귀하며 실제 Round별 배정 controller는 후속이다.
- Bottom Y=-2.8CU, Side X=±6.15CU/Z=±4.85CU는 문서 START로 구성한다. 활성 Root body가 소유하는 실제 non-trigger Collider가 이 OOB 영역에 겹치면 physics 직후1회 처리한다. 팔다리·표시 bone·타격용 trigger는 탈락을 결정하지 않는다.
- 탈락한 actor와 그 actor의 분리된 ragdoll/query 물리를 정지하고, 같은 tick에 camera subject에서 제외한다. 해당 대상과의 grip만 강제 해제하며 Throw를 만들지 않는다. 다른 대상과의 grip은 유지한다.
- R 초기화는 actor·일시 상태·camera 포함을 복구한다. 이전 추락 위치에서 새 spawn까지 시각 보간하지 않게 reset snapshot을 정리했다. 실제 Match/score/Round reset 시스템 완료는 아니다.

## 카메라 검토안

fixed yaw0°, pitch34°, FOV42°, 최소 거리8CU, 여백15%와 기존 damping을 유지하고 최대 거리만15→22CU로 비교한다. 항상22CU에 머무는 방식이 아니며, 살아 있는 대상들의 범위를 담는 데 필요한 만큼 물러난다.

상판만 보면 기존 범위가 가능하지만, 반대편과 추락 직전의 살아 있는 캐릭터를 함께 담으면 더 큰 거리가 필요했다. 살아 있는 대상을 미리 camera에서 빼거나, 탈락선을 올리거나, 이동 속도를 낮춰 해결하지 않았다.

| 검사 | 기존 상한15 | 검토안 상한22 |
|---|---:|---:|
|2/3/4인×16:9/16:10/21:9의9개 극단 회복 배치|9개 모두 상한 초과|9개 모두 여백 충족|
|검토안에서 사용한 정적 거리|—|17.67~20.62CU|
|4인 이동·넉다운·추락·초기화 시나리오|25 physics sample에서 안전 범위 이탈|안전 범위 이탈0|
|동적 최대 거리|15.00CU|21.45CU|
|동적 최소 viewport 여백|약-3.47%(화면 밖)|약15.00%|

동적 비교에서 탈락 tick은 양쪽 모두285/285/324로 같고, 남은1명은 회복 후 초기화에 참여한다. 이는 camera 차이가 물리 결과를 바꾸지 않았다는 시나리오 근거다. 상한을 넓히면 크게 흩어졌을 때 캐릭터가 작아지므로 사용자 가독성 의견을 확인한다.

## 빠른 이동의 접근 시간

실제 Unity collision callback으로 최초 player 접촉을 측정했다.20조건은2인2배치·3인4배치·4인4배치 각각의 걷기/달리기다. 시작 시 player 간 접촉은0이며 조건별 상판 geometry가 같다.

| 인원 | 걷기 첫 접촉 | 달리기 첫 접촉 |
|---|---:|---:|
|2인|1.35초|0.92초|
|3인|1.28초|0.88초|
|4인|1.28초|0.88초|

중앙으로 이동하도록 준 고정 입력의 측정이다. 실제 사람의 반응 시간이나 실제 라운드 종료 시간 측정은 아니다.

## 실행·영상

Unity에서 `Project Hotfix > Prototypes > Open Arena Boundary Review 2/3/4 Players`를 연다. F1~F4 선택, WASD, Shift, Space, 좌우 클릭/유지, Esc, R, F8은 기존 검토 입력과 같다. 탈락한 선택 actor는 입력을 받지 않으며 F키로 살아 있는 actor를 선택하거나 R로 초기화한다.

[기존15m→검토안22m 동작 비교](../../.codex_tmp/arena-boundary-20260910/Media/Comparison60.mp4)는 각각7초, native60fps다. 이동·충돌·명시적 Down1회·추락·초기화의 검토 입력이며 실제 사람 간 대전이 아니다.840프레임·14초를 실제 decode했고840개 프레임이 서로 다르며 시간 간격은1/60초다. [정적 회복 범위 비교](../../.codex_tmp/arena-boundary-20260910/Media/RecoveryFramingComparison.png)는 카메라 한계를 보기 위해 배치한 위치 표본이다.

## 검증과 다음 단계

- [PlayMode21/21](../../.codex_tmp/arena-boundary-20260910/Play-final.xml): 새 경계/카메라7개, 기존 입력/FPS3개, 잡고 이동/점프/Throw1개, ragdoll/grip10개.
- 분리 query 정리와 respawn snapshot 보강 뒤 [7/7 재검증](../../.codex_tmp/arena-boundary-20260910/Cleanup-Fps.xml). 앞21개와 겹치는 검사이며 신규7개로 더하지 않는다.
- [전체 EditMode139/139](../../.codex_tmp/arena-boundary-20260910/Edit.xml).
- 경계 시나리오의60/15/144FPS 상당 렌더 일정과200ms 지연에서 모든 OOB/포함/초기화 tick이 일치하고 Root·camera 위치 차이0이었다. 실제 native loop에서도 OOB 처리 후 같은 tick의 camera snapshot에서 제외되는 것을 확인했다.
- 원본 model·rig·weight·clip9개 자산 SHA-256 동일, Player Build·commit·push0.

아직 Lip/Facade/Beam 잡기와 ClimbAssist, Crane/Hook/Lever, panel tilt, Weapon DropZone·supply, 승패·3초 countdown·60초 timer·SuddenDeath와 peer가 없다. 따라서 P00-A/B 전체나 UG-CAM/UG-P00-GREY 완료로 승격하지 않는다.

사용자가 이 크기/카메라를 다음 시제품 기준으로 채택했다. 이어서 실제 손 접촉 기반 가장자리 잡기와 제한 ClimbAssist를 연결하고, 그다음 경기 흐름을 검토한다.

[구조화 결과](R20_ARENA_BOUNDARY_REVIEW_20260910.json)에 개별 수치, 전체 소스 snapshot9개와 원본 자산 hash를 보존한다.
