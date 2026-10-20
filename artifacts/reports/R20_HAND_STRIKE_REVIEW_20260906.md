# 손 입력·권한 타격 시제품 — 2026-09-06

승인 펀치의 Unity 표현 통합 다음 단계로, 손별 탭/홀드 판별과 실제 물리 표적 타격을 연결했다.
**로컬 권한 시제품**이며 피해·Down·실제 Grab joint·Throw·네트워크 전투 완료를 뜻하지 않는다.

## 구현과 시작값

| 항목 | 현재 시제품 |
|---|---|
| 손 입력 | LMB 왼손, RMB 오른손. 누름은 Pending만 생성 |
| 탭/홀드 | PRD의 START150ms. 빠른 release는 Punch, threshold 도달은 Strike 없는 GrabSeek |
| 펀치 표현 | 승인한 준비0.10초·전체0.70초, 권한 상태의 재생 시각을 따라감 |
| 권한 타격 구간 | START0.10~0.2667초. Animation Event 또는 렌더 뼈를 읽지 않음 |
| 타격 공간 | root 기준 x=±0.12m, y=0.66m, z=0.12→0.38m, sweep 반경0.045m START |
| 충격 | 20N·s START, 대상 질량×1m/s로 한 번의 속도 변화 상한 제한 |
| 적중 제한 | 한 AttackActionId·Target 조합당1회. 같은 대상의 복수 collider도 중복 적중0 |

150ms는 기존 기획의 시작값이고 UG-HAND의 최종120/150/180ms 체감 승인을 대신하지 않는다.
타격 구간·반경·충격은 이번 검토의 START이며 최종 전투 밸런스로 확정하지 않는다.
양손 Ground tap은 별도 공격2개다. 따라서 같은 대상에 각각1회씩 맞을 수 있다.

`GroundHandActionStateMachine`이 press context·순서·hold·attack phase·target dedupe를 소유한다.
`HandStrikeReview`는 권한 물리 sweep·가림 검사와 bounded impulse를 담당하고,
Presentation은 `HandActionPresentationState`의 action/age를 표시한다.
물리 판정은 visual hand pose나 Animation Event를 호출하지 않는다.

## 입력·취소·벽 처리

- 손마다 독립된 상태를 사용하며 중복/과거 sequence는 공격을 다시 만들지 않는다.
- Ground에서 시작한 press는 이륙 뒤에도 Ground context를 유지한다. Air press를 Punch로 바꾸지 않는다.
- Air Kick/Dropkick/chord는 아직 연결하지 않았다. Air hold는 탐색 intent까지 구분한다.
- Esc는 local 입력을 취소·중립화하며 physics를 정지하지 않는다. 닫은 뒤 양쪽 버튼 모두 Up인 표본은
  공격 없이 재무장에만 사용한다. 장면 시작·Reset 뒤에도 같은 재무장 원칙을 적용한다.
- 한 render frame 안의 빠른 down/up도 보존한다. 다만 frame 자체가 hold threshold 이상 지연되어
  누른 길이를 알 수 없는 pair는 공격을 합성하지 않는다. 입력 이벤트 timestamp/reliable network 전달은 후속이다.
- 벽에 막힌 sweep이 다음 step에서 벽 뒤로 진행하더라도, 몸에서 대상까지 가림을 다시 검사해 벽 뒤 타격을 막는다.
- 준비/복귀·권한 해제·손 점유·대상 비활성/제거·Reset에서는 유효 공격이 아니면 충격을 만들지 않는다.

## 검사

순수 상태 검사8개에서120/150/180ms의 threshold 직전·정확한 경계·이후를 검사했다.
press 즉시 Punch0, Hold 후 release의 Punch0, 좌우 독립, stale/duplicate edge, context 고정,
target dedupe, 메뉴/Reset·all-up 재무장과 점유 손 거절을 확인했다.

실제 승인 모델과20kg Rigidbody 표적으로18조건·3,240physics step을 검사했다.

| 조건 | 적중 |
|---|---:|
| 왼손·오른손 탭 | 각각1 |
| 양손 탭 | 2, 손별1 |
| 정상 frame 내 down/up pair | 1 |
| 지연된 frame의 길이 불명 pair | 0 |
| Hold·벽·거리 밖·점유 손 | 0 |
| 메뉴 취소·Reset·권한 해제·비활성/제거된 대상 | 0 |
| 메뉴 닫고 all-up 뒤 새 탭 | 1 |
| Presentation 적용 없이 권한만 실행 | 1 |
| 별도 두 번째 공격 | 총2 |
| 한 대상의 여러 collider | 1 |

한 손 타격의 표적 최대 수평 속도 약0.902m/s·이동 약76.7mm, 동시 양손은 약1.902m/s·323.3mm였다.
이동량에는 검토 바닥의 마찰이 포함된다. 실제 피해량·Down 또는 플레이 재미의 측정값이 아니다.
표현 재생 시각과 권한 시각 오차는 검사에서0이었다. 반복 시험용 Reset은 표적 위치·속도·적중 count도 초기화한다.
수동 Mouse 장치·실제 피어 loss/reorder·최종 InputAction map의 완성 검사는 아니다.

전체 EditMode93/93·PlayMode24/24 통과, skip0. 이후 추가한 시작 재무장·지연 frame 보호는 영향받는18조건을 다시 실행해 통과했다.

## 실행과 이후 범위

Unity 메뉴 `Project Hotfix → Prototypes → Open Hand Strike Review`에서 Play한다.
흰 캐릭터를 WASD/Shift/Space로 움직이고, 주황색 물리 표적 앞에서 LMB/RMB를 짧게 눌렀다 놓는다.
길게 누르면 HUD가 GrabSeek를 표시한다. **현재 GrabSeek는 의도 판별이며 물체를 붙잡는 기능은 아직 없다.**
Esc로 입력 메뉴를 열고 닫으며 R로 캐릭터·표적을 초기화한다. 이 장면에서 J/K 시각 검토 입력은 사용하지 않는다.
기존 `Open Physics Jump Review`의 J/K 펀치 표현 검토와 승인 접지 설정은 유지한다.

다음 연결은 GrabSeek의 실제 접촉 대상 선택·제한 joint·Release/Break와 한 손 Grab+반대 손 Punch 조합이다.
피해·Down/회복과 Air Kick/Dropkick, 실제 두 플레이어 조작과 최종 체감 검토도 남아 있다.
벽 차단은 적중에 관한 것이며 팔 mesh의 벽 접촉 pose 보정까지 구현한 것은 아니다.
기존 발바닥 침범 잔차·지형 IK와 production Prefab/anchor·정적 parity·UG-C1B/UG-C2 Gate는 별도다.

원시18조건 report·영상·소스 snapshot·검사 XML·START 값은 [구조화 기록](R20_HAND_STRIKE_REVIEW_20260906.json)에 연결한다.
원본 모델·리그·승인 펀치/하체 웨이트, 이동0.70/1.05m/s 변경은0이며 Player Build·commit/push는 수행하지 않았다.

영상은 `.codex_tmp/hand-strike-20260906/Review-20260905-201626/Media/HandStrike.gif` 및 `.mp4`다. MP4 디코딩120frame·60fps·2초를 확인했다.

문서 baseline·workflow7/7·art profile 및 tests25/25(221 assertions)·`git diff --check`와 원본/승인 모델 파일 SHA256 보존도 확인했다.
