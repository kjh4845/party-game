# r20 펀치·잡기·벽 매달리기 애니메이션 검토

> **펀치 후속 재작업:** 사용자가 타격 느낌과 팔꿈치 활용 부족을 지적했다. 아래 펀치의 기술 PASS는 역사로 보존하며,
> 현재 펀치는 [새 검토 결과](R20_ELBOW_PUNCH_REVIEW_20260905.md)를 따른다. Grab/Wall 검토는 이 피드백의 수정 대상이 아니다.

2026-09-05 계획 진행 지시에 따라 좌우 펀치와 한손/양손 Grab·Wall 시나리오를 제작했다.
이전 정지 자세에서 진입·유지·해제/복귀가 있는 동작 검토로 진행했으며,
좌우 펀치는 **실제 Blender Action2**를 별도 후보에 저장했다.

## 산출물

| 산출물 | 내용 |
|---|---|
| 후보 `.blend` | `R20_InteractionAnimationCandidate_r02.blend`, r20 bind rig·geometry 유지, 검토용 Action2 |
| Punch L/R | 준비→뻗기→확장 구간→복귀, 각25 keyframe sample, 24fps·1초 span START |
| Grab L/R/LR | Reach→고정 목표 유지→Release. 몸의 위치가 바뀌어도 손 목표를 유지하는 analytic IK |
| Wall L/R/LR | 위쪽 wall/ledge 목표에 Reach→매달린 자세 유지→Release. 검토용 wireframe surface |
| 시나리오 렌더 | 8개×25표본×2방향=400장, 640² |
| 저장 재생 검증 | 좌우 각49개 keyframe/half-frame, 총98샘플 |

좌우 펀치 Action은 `REVIEW_Punch_L_r01`, `REVIEW_Punch_R_r01`이다. 각각 해당 팔의
UpperArm·Forearm·HandLogical quaternion 회전12곡선만 가지며 반대 손·Root·scale/location 곡선은0이다.
다른 손의 Grab이나 보행과 조합하기 위한 mask 구조이며, 실제 조합 상태 통합은 후속이다.
후보에는 재개방 시 Action을 선택해 재생할 수 있도록 Quaternion 모드와 unkeyed Idle base pose를 저장했다.

Grab/Wall 유지 동작은 고정 clip으로 손을 붙여 놓는 방식이 아니다. 장면의 목표점까지 두 segment의
길이로 팔 자세를 풀고, 닿지 않는 경우 오류로 보고한다. 팔을 늘려 reach를 맞추지 않는다.
검토용 파란 wireframe과 주황색 anchor는 실제 collision·grab constraint가 아니다.

## 확인 결과

- 200표본 모두 manifold·자기교차·45° hard edge·90° foldover 검사 PASS.
- Anchor 고정 구간 최대 오차: Grab 약1.00e-7m, Wall 약1.09e-7m. 검토 한계0.0001m 이하.
- 저장한 후보를 다시 열어 98개 재생 sample의 surface 검사 PASS.
- 저장된 정수 keyframe 재생과 제작 중 pose의 vertex 최대 차이0, 펀치 시작/끝 형상 오차0.
- 재개방한 파일의 Quaternion 모드와 Idle base pose 확인. Source r16/r20 SHA 불변, Bone22·driver8 유지.
- 문서 baseline·Art profile·workflow 회귀7개와 whitespace 검사 통과.

위 판정은 해당 검토 sample 범위다. 면 사이 각도는 Bone의 관절 각도와 구분하며,
숫자 PASS를 최종 동작 자연스러움이나 사용자 시각 승인으로 표시하지 않는다.

## 아직 남은 것

- 실제 Authority Hit·target eligibility·Grab/Release/Break와 Rigidbody/Joint 연결
- 체중·충돌·마찰·중력·실제 추락 및 ClimbAssist
- 이동 중 Punch, 한손 Grab+반대 손 Punch, 서로 다른 대상 잡기와 강제 중단/target 제거
- Jump/착지, Lift/Throw, AirKick/Dropkick·실제 앞/뒤 GetUp clip의 추가 제작·통합
- Generic animation FBX/Unity 반입·hip helper bake, 게임용 hand mask와 animation/physics blend
- 사용자 시각·실제 전투 체감 검토. 1초 span과 구간 비율은 gameplay timing의 확정값이 아니다.

이번 작업은 ANP-002의 독립 검토용 선행 산출물이다. C1BRW static parity·UG-C1B와
CHR-002/006/007/011의 물리/게임플레이 선행조건을 완료한 것으로 취급하지 않는다.
Player Build·Unity 신규 반입·commit/push·원본 geometry/weights 수정은0이다.

## 파일과 재실행

[구조화 결과](R20_INTERACTION_ANIMATION_REVIEW_20260905.json)에 candidate와 source script·raw report hash를 기록했다.
로컬 검토 파일은 `.codex_tmp/interaction-review-20260905/full-r01/`에 있다.

- `R20_InteractionAnimationCandidate_r02.blend`: 현재 후보
- `Interactions.gif`, `Punches.gif`, `Grab.gif`, `Wall.gif`: 동작 검토
- `InteractionScenarios.png`: 여덟 시나리오의 대표 자세
- `FinalInteractionReview.json`, `SavedActionVerification-r02.json`: 현재 검증 결과

r01 후보는 저장 모드 정리 전 역사 결과다. RenderGeneratorSnapshot은 최초 렌더 제작 코드를 보존하고,
FinalInteractionReview는 후보 저장 모드 정리 이력을 별도로 기록한다. 원본 r20의 Action0을 바꾸지 않았다.

제작: `tools/blender/review_c1b_rw020_interactions.py -- <새 출력 폴더>`

재개방 검사: `tools/blender/verify_c1b_rw020_interaction_actions.py -- <후보.blend> <새 report.json>`

두 도구는 Blender의 `--background --factory-startup --python-exit-code 1 --python`으로 실행한다.
기존 출력은 덮어쓰지 않으며 게임용 clipping/Hit 검사를 대신하지 않는다.
