# 팔꿈치 중심 펀치 재작업

## 준비 속도 후속 조정 — 현행 r02

**사용자 시각 승인 완료:** 준비0.10초의 팔꿈치 중심 펀치 r02를 사용자가 원하던 결과로 확인했다.
[승인 기록](R20_PUNCH_VISUAL_APPROVAL_20260905.json)을 따른다. 같은 동작 방향·준비 속도는 다시 미승인으로 돌리지 않는다.
실제 gameplay cadence·타격 판정과 Unity/물리 통합은 별도 검증이다.


사용자 요청으로 팔꿈치를 접는 준비만 **0.20초→0.10초**로 줄였다. 준비 이후의 모든 key는
3프레임 앞으로 옮겨 확장·회수의 pose 값·구간 길이·속도를 그대로 유지했다.
확장은 여전히 약0.133초이며 전체 후보 길이는0.8초→0.7초다. Gameplay 공격 cadence 변경은 아니다.
저장 재개방 뒤98개 시간 매핑의 곡선/pose 차이0, 준비·접속·대표 후속 구간16개 surface 검사 PASS.
원본 r16/r20·이전 후보와 bind Mesh는 유지했다.

[현재 조정 결과](R20_PUNCH_PREPARATION_REVIEW_20260905.json)에 후보/원시 report hash를 기록했다.
현재 후보는 `.codex_tmp/punch-preparation-20260905/review-r02/R20_ElbowPunchAnimationCandidate_r02.blend`다.
같은 폴더의 `FasterPreparation.gif`는0.7초 실제 후보 타이밍이고,
`PreparationBeforeAfter.gif`는 시작 시점을 맞춰 비교하도록 수정본 끝에만0.1초 Idle을 덧붙인 표시용 영상이다.

아래는 이전 r01의 자세 제작·검증 기록이다. 동작 모양은 유지하며 준비 timing만 위 r02가 대체한다.


사용자는 이전 펀치에 때리는 느낌이 부족하고 팔꿈치를 더 활용해야 한다고 피드백했다.
기존 Grab/Wall·공통 총기 Grip을 유지하고 좌우 펀치만 별도 후보로 재작업했다.

## 바뀐 동작

1. 손을 몸 앞으로 끌어오며 팔꿈치를 충분히 접어 준비한다.
2. 공격하는 쪽 어깨가 작게 먼저 전진하고, 접힌 전완은 잠시 뒤따른다.
3. 팔꿈치를 빠르게 펴서 끝점까지 보내고, 뻗은 자세의 정지는 짧게 둔다.
4. 팔꿈치를 다시 접어 회수한 다음 Idle로 돌아간다.

| 검토 항목 | 좌우 동일 결과 |
|---|---|
| 준비 때 팔꿈치 굽힘 | 약111.8° |
| 확장 때 팔꿈치 굽힘 | 약13.0°; 완전 과신전은 사용하지 않음 |
| 회수 때 팔꿈치 굽힘 | 약102.0° |
| 준비→확장 구간 | 4프레임/30fps, 약0.133초 |
| 후보 길이 | 24프레임 간격/30fps =0.8초, START |
| 새 Action | `REVIEW_ElbowPunch_L_r01`, `REVIEW_ElbowPunch_R_r01` |
| 기록한 곡선 | 해당 쪽 Clavicle·UpperArm·Forearm·HandLogical 회전16곡선/Action |
| Root·반대 손·scale/location 곡선 | 0 |

각도와 시간은 이 후보의 관찰/시작값이다. 실제 Hit 시점, 공격 cadence, 피해량, 입력 threshold나
Physics Joint limit을 바꾸거나 승인한 것은 아니다. 좌우는 같은 원리이며 반대 손 잡기와의 조합을 위한
mask 범위를 유지한다. 어깨 선행도 공격하는 쪽 Clavicle에만 기록했다.

## 검증

- 새 후보를 저장한 뒤 다시 열어 keyframe25+half-frame24를 좌우 모두 검사: 총98샘플 PASS.
- Manifold·자기교차·45° hard edge·90° foldover 검사 통과. 면 사이 최대 인접각 약21.02°.
- 정수 keyframe의 저장 재생 quaternion이 제작 입력과 일치하고 시작/끝 형상 오차0.
- 좌우 mirror vertex 최대 오차 약2.73e-7m.
- 원본 r16/r20 SHA 불변, Bone22·helper driver8, geometry·weights 변경0.
- 후보를 열면 왼쪽 Action이 선택되고 30fps 재생 범위가 설정된다. 오른쪽 Action도 같은 파일에 있다.
- 원시 render100장과 source/candidate hash를 기록했다. Unity 반입·물리 타격·사용자 시각 승인은 아직이다.

이전 펀치의 기술 PASS는 역사로 보존하며 최신 사용자 시각 기준에서는 `REWORK_REQUIRED / SUPERSEDED`다.
이 결과는 새 펀치 방향을 검토하는 자료이며 전체 ANP-002 완료를 뜻하지 않는다.

## 파일

[구조화 결과](R20_ELBOW_PUNCH_REVIEW_20260905.json)에 원시 결과와 후보 hash를 기록했다.
로컬 파일은 `.codex_tmp/elbow-punch-20260905/review-r01/`에 있다.

- `R20_ElbowPunchAnimationCandidate_r01.blend`: 현재 후보
- `PunchBeforeAfter.gif`: 이전 1초 후보와 새 0.8초 후보 비교
- `ElbowPunch.gif`: 새 동작의 사선·측면
- `ElbowPunchPhases.png`: 준비·어깨 선행·확장·회수의 자세 비교
- `ElbowPunchReview.json`: 98샘플 원시 검사와 타이밍/대칭 결과

재실행은 Blender에서 `--background --factory-startup --python-exit-code 1 --python
tools/blender/review_c1b_rw020_elbow_punch.py -- <새 출력 폴더>`를 사용한다.
기존 후보는 덮어쓰지 않는다. Player Build·신규 FBX/Unity 반입·commit/push는 실행하지 않았다.
