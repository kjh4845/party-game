# 실제 펀치 → 누적 Down → 회복 연결

후속 사용자 최종 결정으로 **타격 임계값 감소를 폐기**했다. 현재 규칙은 [고정 타격·무력 Down](R20_FIXED_HIT_LIMP_DOWN_REVIEW_20260908.md)이다. 아래5→4→3→2와 검사는 당시 비교 이력으로 보존하며 현재 기본값이 아니다.


2026-09-08 사용자 결정 세 가지를 로컬 시제품에 반영했다.

1. 유효 피격은 시간으로 감소하지 않고 누적한다.
2. Down 중 추가 피격은 물리 impulse만 적용한다. 새 피격 누적·DownCount 증가·groggy 대기 시간 연장은 없다.
3. 같은 Round의 DownCount가 늘수록 다음 Down까지 필요한 타격 수를 하한까지 줄인다.

## 누락 확인과 수정

이전 `TimedDownStateMachine`에는 `Base + Increment × (DownCount - 1)`의 **대기 시간 증가만** 있었다. PRD/캐릭터 사양도 그 시간 규칙만 기술했으며, 다음 Down까지의 타격 임계값 감소는 구현되어 있지 않았다. 이번에 별도의 `PunchDownAccumulation` 상태로 보완했다.

새 Down에서 현재 피격 누적을 소비하고 0으로 만든다. 같은 Round의 DownCount는 보존한다. Round reset은 피격 누적과 DownCount를 모두 초기화한다. 비피격 시간에 따른 감소 로직과 timer는 없다.

| 이미 발생한 DownCount | 다음 Down에 필요한 펀치 수 START | 해당 Down의 대기 시간 START |
|---:|---:|---:|
| 0 | 5 | 1.5초 |
| 1 | 4 | 2.0초 |
| 2 | 3 | 2.5초 |
| 3 이상 | 2 | 최대 3.0초 |

수치는 시제품 검토용이며 최종 전투 밸런스가 아니다. `max(minimumHits, firstHits - reductionPerDown × DownCount)`의 규칙과 timing profile을 분리했다. Lobby는 기존 Count 0·Base 시간 계약을 유지한다.

## 실제 연결

- `HandStrikeReview`의 권한 sweep·벽 가림·공격별 대상 중복 방지를 통과한 Punch만 받는다. 확인되지 않은 action, 자기 자신, 권한이 꺼진 공격자/대상은 거부한다.
- `PunchDownAccumulation`은 공격자별 64-action receipt window를 사용한다. 양손 공격이 action 순서와 반대로 닿아도 다른 적중으로 인정하고, 재전달은 거절한다. 이 로컬 경로는 최대 네 source와 Round generation을 구분한다.
- `PunchDownReview`가 임계 도달에서 기존 Ragdoll 진입을 한 번 호출하고, 실제 닿은 body에 질량 기반 상한의 impulse를 한 번 적용한다. Down 진입 impulse를 중복 추가하지 않는다.
- `StrikeReviewHurtbox`는 별도 계층의 래그돌 팔다리 collider를 원래 캐릭터에 연결한다. 여러 collider가 하나의 대상이며 자기 몸과 벽 가림 검사에도 동일한 매핑을 쓴다.
- 기존 표적 전용 장면은 유지하고 opt-in 캐릭터 피격 장면을 추가했다. 최신 골반·하체 GetUp clip과 기립 후 handoff 보정은 유지한다.

추가 타격은 groggy timer를 되감지 않는다. 다만 물리적으로 밀려 지지면이나 기립 공간을 잃었다면 기존 recovery gate가 재평가하므로 실제 기립 완료가 늦어질 수 있다. 이는 별도의 시간 패널티가 아니다.

## 실행

Unity 메뉴 `Project Hotfix > Prototypes > Open Punch Down Review` → Play.

- 흰 캐릭터: WASD 이동, Shift 달리기, Space 점프, LMB/RMB 짧은 탭 펀치, 유지 잡기, 놓기 던지기.
- 주황 캐릭터: 수동 물리 표적이며 반격 AI가 아니다. 유효 펀치로 누적 Down 후 실제 회복한다.
- R: 두 캐릭터와 피격 누적·DownCount 초기화. Esc: 기존 로컬 입력 억제/재무장.
- 누적과 임계값은 검토용 debug 표시다. 제품의 상시 HUD를 추가한 것이 아니다.

## 검사

`PunchDownAccumulationTests`는 임계값 감소/하한, Down 중 재피격, 공격자 분리·역순 양손·중복/오래된 action, reset generation, 무효 profile/ID를 검사한다.

`PunchDownReviewTests`는 다음을 실제 로컬 PhysicsScene에서 검사한다.

- 실제 입력→sweep 적중으로 네 episode에 5/4/3/2회의 펀치와 회복, 이후 이동을 확인한다.
- 첫 피격 뒤 10초 비피격 구간에서 누적 1이 그대로 남는다.
- 개별 Down 중 추가 타격 fixture는 실제 active action과 receiver를 사용한다. 이 fixture 자체는 sweep 검증이 아니며, 실제 sweep 검증은 첫 조건이 소유한다. 추가 impulse의 body 속도 변화와 timer/episode/Count/누적 불변을 검사한다.
- 모든 활성 래그돌 proxy collider의 대상 소유권, duplicate/위조 action/권한 차단을 검사한다.
- 실제 벽 뒤 펀치, hold→Grab→release Throw와 후속 바닥 충돌에서 누적/자동 Down 0을 확인한다.
- 네 회복 후 이동·joint gap/speed, Reset 뒤 초기 임계값 복원을 확인한다.

기존 전체 회귀 검사에는 손 입력·타격 가림·메뉴/Reset, Grab 중 Down 강제 해제, 방향별 회복/표면 변형, 기립 후 흔들림과 이동이 포함된다.

## 범위

로컬 캐릭터 표적을 대상으로 한 Punch 연결이다. Kick/Dropkick·무기별 피해표, 피해 없는 일반 충돌 자동 Down, 실제 peer/2·3·4인 조작과 최종 게임 balance는 구현 완료로 표시하지 않는다. capture fixture는 각 독립 펀치 전에 공격자를 사거리로 재배치하며, 적중/낙하/회복 운동은 실제 물리다. 이것을 두 사람의 수동 플레이 영상으로 설명하지 않는다.

원본 Blender 모델·리그·weight와 회복 animation asset은 변경하지 않았다. Player Build·commit/push는 실행하지 않았다.

검사 결과, 비교 영상과 최종 소스 snapshot은 [구조화 기록](R20_HIT_DOWN_INTEGRATION_REVIEW_20260908.json)에 보존한다.

## 최종 결과

- EditMode 117/117·전체 PlayMode 40/40 통과. 마지막 DownEpisode 종류 guard 보완 후 영향받는 피격 통합 4/4를 최종 소스로 다시 통과했다. skip 0.
- 실제 sweep 연결에서 타격 수 5/4/3/2, 총 네 Down/회복, 10초 비피격 누적 유지, 이후 이동 0.399m를 확인했다.
- 문서 baseline·workflow 7개·diff check 통과. 원본/보존 Blender 네 파일, 두 GetUp clip과 표현 소스의 SHA-256 일치.
- 캡처 140프레임·20fps·7초, GIF 정지/반복 안내 포함 8초를 확인했다.
