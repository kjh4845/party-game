# 승인 펀치의 Unity 보행 조합 — 2026-09-06

접지 절충안 채택 이후의 다음 독립 프로토타입 작업으로 승인 펀치를 Unity에 연결했다.
승인된 팔꿈치 중심 L/R Action을 읽어 사용하며 새 펀치를 만들거나 준비 속도를 바꾸지 않았다.
**표현 통합 완료**이며 실제 타격 판정·피해·밀기·Grab 물리 완료를 뜻하지 않는다.

## 구현

- `export_c1b_rw020_approved_punch.py`가 승인 SHA256을 확인한 Blender 파일에서 좌우4bone
  (Clavicle·UpperArm·Forearm·HandLogical)의 local rotation을120Hz로 추출한다. 원본 저장0.
- 승인 하체 웨이트 transport와 뼈 rest position 오차0, rest rotation 일치를 확인했다.
- `ApprovedPunchPose`는 한 손당85표본·0.70초를 재생한다. 준비0.10초·팔 뻗기0.1333초를 유지한다.
- 시작40ms는 현재 보행 팔에서 연결한다. 준비 완료와 확장 시점은 지연하지 않는다.
- 빈 팔 swing은 공격 중인 같은 팔을 쓰지 않는다. 좌우 동시 재생은 서로 독립적이다.
- 사용 불가한 손의 시작을 거절하고, 다른 동작이 팔 제어권을 가져가면 그 팔을 더 이상 쓰지 않는다.
- Reset은 진행 중 펀치를 취소한다. 정상 종료 뒤 대기 자세와 빈 팔 보행 swing으로 돌아간다.
- 지상에서 시작한 펀치는 점프 중에도 이어진다. 공중에서 새 지상 펀치를 시작하지 않는다.

현재 입력은 검토용 **J=왼손, K=오른손**이다. Ground LMB/RMB tap→Punch·hold→Grab의
본게임 입력 계약을 대체하지 않는다. 새로운 Hit·Impulse·Root Motion 또는 물리 Collider를 만들지 않는다.

## 확인한 범위

| 검사 | 결과 |
|---|---|
| Blender→Unity 팔 head/tail | L/R170표본, 최대 오차 약3.17e-7m |
| 승인 동작 시점 | 조합8조건에서0.10초 팔꿈치 굽힘100° 초과, 확장 시25° 미만 |
| 조합 | 정지 왼손·걷기 왼손·달리기 오른손·양손·펀치 후 점프·오른손 점유·제어권 이전·Reset |
| 하체/물리 비교 | 무펀치 대조 캐릭터와 다리 회전 차이0°, Rigidbody 속도 차이 최대 약4.10e-15m/s |
| 팔 소유권 | 반대 손 점유 pose 보존, 사용 중 손 시작 거절, 제어권 이전 후 쓰기0 |
| 종료 | 진행 중 flag 해제, 정상 종료 후 팔 대기 자세 차이0° |
| 표면 | 걷기/달리기 조합은 각각43frame 전 구간, 다른5조건15표본씩. 인접 면45° 꺾임0·90° 뒤집힘0 |
| 표면 최대 각도 | 약42.42° |
| 발 | 도달 범위 보정0m, 표면 바닥 침범 최대 약6.72mm |

제어권 이전 시 외부에서 주입한 임의 pose는 소유권 검사에만 쓰고 표면 검토에서 제외했다.
표면 검사는 인접 삼각형 기준이며 비인접 자기 교차 전수 검사나 연속시간 보장이 아니다.
실제 Grab 고정이나 잡은 물체의 하중을 시험한 것으로 해석하지 않는다.

모든 캡처는 pose 적용 후 실제 player-loop frame을 넘긴 다음 촬영한다.
전체 EditMode85/85·PlayMode23/23 통과, skip0.
최종 검사 XML·소스 snapshot·원본/transport/영상 hash는 [구조화 기록](R20_UNITY_PUNCH_INTEGRATION_20260906.json)에 연결한다.

## 실행과 재현

Unity 메뉴 `Project Hotfix → Prototypes → Open Physics Jump Review`에서 Play한다.
WASD 이동·Shift 달리기·Space 점프·J/K 좌우 펀치 검토·R 초기화.
승인 접지 설정 Walk0.70/Sprint1.05m/s·주기3.0/4.5Hz는 유지한다.

승인 펀치 transport는 `.codex_tmp/punch-unity-20260906/ApprovedPunch.json`이다.
`Blender --background --factory-startup --python-exit-code 1 --python tools/blender/export_c1b_rw020_approved_punch.py -- <새 출력.json>`으로 재현한다.
기존 출력은 덮어쓰지 않는다. 검토 프로젝트가 읽는 경로는 `PhysicsJumpReview.PunchTransportPath`다.
이것은 local Editor 검토용 전달 방식이며 production FBX·Animator/AnimationClip 패키징은 후속이다.

Unity PlayMode 필터 `ApprovedPunchIntegrationTests`로 원본 일치와 조합 검사를 실행한다.
비교 영상은 `Review-20260905-194810/Media/PunchCombinations.gif` 및 `.mp4`다.
영상 상단은 걷기 왼손/달리기 오른손, 하단은 양손/점프 이어짐이다.

## 다음 연결 범위

표현 layer를 실제 Hand Intent·Attack phase와 연결하고, 두 캐릭터/표적의 Hit·Grab 물리와 조합한다.
J/K 검토 명령을 그대로 제품 손 입력으로 승격하거나 Animation frame에서 적중을 결정하지 않는다.
펀치의 기존 시각 승인은 유지한다. CHR-006/007·ANP-002 전체·UG-C1B/UG-C2와 게임 재미 검증은 미완료다.
원본 모델·리그·승인 펀치·승인 하체 웨이트 변경, Player Build·commit/push는 수행하지 않았다.

문서 baseline·workflow7/7·art profile 및 tests25/25(221 assertions)·`git diff --check`와 원본 r16/r20·승인 펀치/하체 웨이트 SHA256 보존도 확인했다.
