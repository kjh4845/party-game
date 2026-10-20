# r20 보행·공통 Grip·다방향 회복 검토

2026-09-05 사용자 지시에 따라 총기별 자세를 분리하지 않고 팔 뻗기 template 하나를 좌·우에 적용했다.
걷기·뛰기는 한 정지 자세가 아닌 전체 주기를 검사했다. 사용자가 선택한 회복 방식은
**옆 자세 정렬 → 공통 앞/뒤 GetUp**이며 좌우 전용 일어나기 클립은 추가하지 않는다.

## 실제 변경

- `review_c1b_rw020_motion_cycles.py`: r20 원본을 저장하지 않고 Walk36·Sprint24프레임과
  공통 Grip의 L/R/LR 적용을 평가한다. 총기 종류에 따른 pose 분기는 없다.
- `RecoveryOrientationResolver`: 실제 torso rotation·지지면 normal로 front/back/left/right를 판별한다.
  upright/head-down·잘못된 입력은 lying GetUp 진입으로 처리하지 않는다. Side는 가까운 앞/뒤 방향과
  현재 몸의 수평 장축 방향을 유지하는 정렬 target을 계산한다.
- `CharacterRecoveryStateMachine`: stable support·clearance 확인 뒤 side alignment에 진입하고,
  정렬 도달·지지·공간을 다시 확인한 뒤 shared GetUp으로 전환한다. 중복 시작은 방향을 바꾸지 않고,
  중단 시 physics tumble로 돌아가 재평가한다. Down/Dropkick의 Count·episode 의미는 유지한다.
- PRD/SRS·Character/Art/Weapon·Plan/Trace·QA profile에 동일 계약을 반영했다.
  요구사항 `SRS-PHYS-015`, 인수 조건 `AT-045`를 추가해 SRS187·AT45로 연결했다.

## 검증 결과

| 항목 | 결과 |
|---|---|
| Walk | 36프레임, 1.2초/주기 START, loop 형상 오차0 |
| Sprint | 24프레임, 0.8초/주기 START, loop 형상 오차0 |
| 보행 위상 | 왼팔/왼발·오른팔/오른발의 앞뒤 궤적은 반대 위상. 반대편 팔과 다리가 함께 전진 |
| 손 앞뒤 이동 폭 | Walk 양쪽 약0.132m, Sprint 약0.321m. 검토 원본 좌표 기준이며 gameplay 이동 속도가 아님 |
| 구조 검사 | 63표본의 manifold PASS, 자기교차·45° hard edge·90° foldover0 |
| 면 사이 최대 인접각 | Walk11.80°, Sprint13.92°, Grip29.80°. 관절 Bone 각도와 다른 surface 측정값 |
| 공통 Grip | template1, L/R/LR 적용3. 상완·전완·말단 방향 오차0°, 좌우 mirror vertex 최대 오차 약2.0e-7m |
| Render | 720²×2방향×63표본 =126장, 원시 report에 image hash 보존 |
| 원본 보존 | r16/r20 SHA 불변, Action0·driver8, 원래 rest pose 복원 |
| Unity EditMode | 85/85 PASS, failed/skipped0 |
| Unity Editor PlayMode | 4/4 PASS, failed/skipped0 |
| 문서·Art 검사 | 문서 baseline PASS, workflow 회귀7/7, Art 회귀25/25 PASS |

회복 테스트에는 네 진입 방향×24개 yaw×평지/경사면의192조합, 대각선 판별,
side→front/back 정렬96조합과 수평 방향 보존, 단계적 quaternion 보간,
support/clearance 부재·정렬 미도달·중복·중단·실제 Down 전환·Round reset이 포함된다.
이는 수학적 방향 판별과 상태 전환 시험이다. 실제 물리 캐릭터의192회 넘어지기 시험은 아니다.

## 시각 판단과 한계

최종 r03의 quarter-cycle 측면·사선 이미지에서 보행의 팔·다리 방향과 Sprint의 더 큰 팔 움직임을 확인했다.
현재 sample 범위에서 리그를 다시 만들어야 할 자기교차나 면 뒤집힘은 검출되지 않았다.
다만 T-pose bind 대비 국소 압축·늘어남은 존재한다. Sprint의 edge ratio p01 최소0.642,
p99 최대1.488로 측정됐다. 이 수치와 구조 PASS만으로 최종 피부 변형·애니메이션 자연스러움을 승인하지 않는다.

초기 검토용 구현에는 낮춘 Idle에서 Grip 방향을 풀면서 축 twist가 섞이는 문제와
팔 swing을 작은 수평 성분에만 적용하는 Euler 축 문제가 있었다. 이를 bind frame 기준 Grip solve와
실제 sagittal swing으로 수정했다. 기존 r20의 geometry·weights·Bone은 수정하지 않았다.
최종 판정은 재실행한 `cycles-r03`만 사용한다. r01/r02의 중간 결과는 현행 검토 입력이 아니다.

아직 구현·검증하지 않은 것은 다음과 같다.

- 실제 지면 contact/clearance query, Rigidbody/Joint motor, root 이동과 발 접지
- 실제 앞/뒤 GetUp clip, ragdoll 자세 매칭 및 Unity Animator 연결
- 네트워크 snapshot/peer 복구, game Prefab·2/3/4인 Camera와 gameplay feel
- 프레임 사이 연속 자기교차 보장 및 최종 속도·보폭·blend duration tuning

회복 코드는 **방향 판별·정렬 target·상태 전환 기반**이다. caller가 Host physics의 안정 지지·공간 결과를
전달하고 실제 정렬 완료를 확인해야 한다. 시각 pose/Animation Event가 authority의 완료를 결정해서는 안 된다.
이 작업으로 CHR-009·ANP-002 전체나 UG-C1B·UG-C2가 완료된 것은 아니다.

## 검토 파일과 재실행

[구조화 요약](R20_MOTION_RECOVERY_REVIEW_20260905.json)에 원시 report·source/code hash와 검사 결과를 기록했다.
로컬 GIF·프레임·원시 JSON은 `.codex_tmp/motion-review-20260905/cycles-r03/`에 있다.
`WalkSprintComparison.gif`는 같은 시점에 걷기와 뛰기를 비교하고, `Walk.gif`·`Sprint.gif`는 측면도 함께 표시한다.
`SharedGrip.png`의 세 이미지는 무기별 pose가 아니라 동일 template의 L/R/LR 적용이다.

```sh
python3 artifacts/evidence/docs/verify_doc_baseline.py
python3 -m unittest discover -s tools/tests -p 'test_document_workflow.py'
ruby tools/tests/verify_art_profiles_test.rb
```

Blender 재실행은 `--background --factory-startup --python-exit-code 1 --python
tools/blender/review_c1b_rw020_motion_cycles.py -- <새 출력 폴더>`를 사용한다.
기존 출력 폴더는 덮어쓰지 않는다. Player Build·FBX/Unity 신규 반입·commit/push는 수행하지 않았다.
