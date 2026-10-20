# 공용 카메라·화면축 이동 로컬 시제품

광배 가중치 r07을 후속 시제품 기준으로 사용한다. 펀치0.5초, 몸통·팔다리1점/머리2점, 기준10→6점과 Down 대기1.5→3초, 지지/공간이 있으면 잡힌 채 회복하는 정책을 유지한다.

## 이번 단계

- 동일한 검토 공간과 캐릭터 profile로2·3·4명을 생성한다. 실제 네트워크 멀티플레이나 게임패드 지원을 추가하는 작업은 아니다.
- 물리 Root 위치에 고정 범위(centerY0.45m, half extents0.45/0.55/0.45m)를 붙인다. 렌더 mesh·래그돌 팔다리·무기 등의 크기는 목표 계산에 들어가지 않는다.
- pitch34도, FOV42도, fixed yaw0도, Dolly8~15, 화면 여백15%를 문서의 START에서 사용한다. 이 로컬 모델의 서 있는 authority capsule 높이는1m이므로 이 검토에서는1CU=1m다. 최종 CHR/P00 높이 승인으로 승격하지 않는다.
- 전체 범위의8모서리를 고정 카메라 좌표로 변환하고 깊이까지 고려해 필요한 dolly를 계산한다. focus damping·dead zone0.08m·평균 수평 속도 look-ahead 최대0.35m를 적용한다.
- zoom-out 시 화면 안전 범위가 damping보다 우선한다. 대상 제거와 zoom-in은 부드럽게 처리한다.15CU로도 담을 수 없으면 범위 초과 flag를 켜며, 모든 대상이 없으면 마지막 구도를 유지한다.
- WASD는 카메라 방향에서 지면으로 투영한다. 대각선 속도 증가는 없으며, 캐릭터 회전은 기존 물리 torque를 사용한다.
- F1~F4는 검토 전용 조작 대상 전환이다. 이전 actor의 이동/점프 입력과 잡기를 안전하게 취소하고, 새 actor는 마우스 모두 해제 뒤 손 입력을 받는다. 전환/메뉴/Reset의 강제 해제는 Throw를 만들지 않는다.
- Esc는 선택 actor의 입력만 중단하고 시뮬레이션·Down timer·나머지 Root 추적은 계속한다. R은 모든 review actor를 초기 위치/일시 상태로 되돌린다. 정식 Match/Round reset 및 메뉴 UI 완료를 뜻하지 않는다.

## 사용

Unity `Project Hotfix > Prototypes > Open Shared Arena 2 Players` 또는3/4 Players에서 Play한다.

- F1~F4: 조작할 캐릭터 선택. P1흰색/P2주황/P3파랑/P4보라.
- WASD: 화면 방향 이동. Shift: 달리기. Space: 점프.
- 좌/우 클릭: 짧게 펀치, 유지하면 잡기, 마지막 본인 손을 놓으면 던지기.
- Esc: 입력 메뉴/복귀. 복귀 시 마우스 버튼을 모두 놓아야 손 입력 재무장.
- R: 모두 초기화. F8: 개발용 도움말 숨김. 카메라 판독 캡처는 도움말을 숨긴다.

조작하지 않는 캐릭터는 AI 상대가 아니라 입력이 없는 물리 대상이다. 이 공간은 P00 완성 맵이 아니며 탈락/승패/타이머/Hazard는 추가하지 않는다. 바닥 밖 이동에는 개발용 R 초기화를 사용한다.

## 검증 범위

2/3/4명×16:9/16:10/21:9의9조건에서 실제 넓게 떨어진 Root 범위를 검증한다. 렌더 bounds/limb 수정, 입력 메뉴, authority 입력 비활성만으로 대상이 빠지지 않는지 확인하고, 명시적 포함 mask 변경과 빈 roster/최대 거리 초과를 검사한다.

한 명이 F키로 조작 대상을 바꾸는 도구이므로 실제 두 사람이 동시에 조작했다는 증거가 아니다. 움직임 영상은 camera 검토용 입력 시나리오이며 jump 요청과 Down 주입을 포함한다. 실제 사람 간 전투 영상으로 표시하지 않는다.

실제 peer 상태 전송, Match Alive/Disconnect/Forfeit 연결, P00 장애물 가림 및 lethal telegraph framing, 생산용 InputAction asset과 사용자 편안함 Gate는 후속이다. CAM-001~003·CHR-003의 로컬 부분 구현으로만 추적한다.

## 최종 결과 — 2026-09-10 완료

- PlayMode18/18: 새 검토3개와 기존 빠른 펀치5개·래그돌 잡기10개. 전체 EditMode134/134 통과.
- 넓은 배치의9개 조합 모두 화면 여백을 만족했다. 사용된 dolly는10.245~12.236CU였으며, 동일한8~15CU profile을 사용했다.
- 4명 이동·점프·Down camera 영상은20fps 정상 속도이며 실제 물리 Root를 추적한다. 시나리오에서 거리8~11.373CU, subject4명을 계속 유지했다. 마지막 프레임1초 유지.
- 최대범위 초과, 빈 subject, 명시적 대상 제거, 렌더/limb 영향0, 조작 전환 강제 Throw0, 메뉴 중 시간 진행, Reset 위치·누적 복구를 검증했다.
- 문서 기준 PASS, workflow7/7, `git diff --check` 통과. 원본 에셋 hash와 채택한 광배 transport hash를 확인했다.
- [구조화 기록](R20_SHARED_ARENA_REVIEW_20260909.json)에 테스트 XML·소스 snapshot7개·영상·화면비 캡처를 연결했다.

![공용 카메라 검토 시나리오](../../.codex_tmp/shared-arena-20260909/Media/SharedCameraMotion.gif)

![인원·화면비 검증](../../.codex_tmp/shared-arena-20260909/Media/FramingMatrix.png)
