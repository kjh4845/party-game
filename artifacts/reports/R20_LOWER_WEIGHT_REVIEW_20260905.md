# r20 하체 웨이트 후보 검토 — 2026-09-05

사용자의 “만들어봐” 지시에 따라 하체 웨이트만 수정한 별도 후보를 제작했다.
검사한 92개 표본의 표면 무결성은 PASS다. 후속 사용자가 이 후보로 진행하도록 승인했다.
[후속 승인 기록](R20_LOWER_WEIGHT_VISUAL_APPROVAL_20260905.json)을 기준으로 Jump/착지 시제품의 작업 원본으로 채택한다.
원본 r16/r20과 승인 펀치 파일은 보존했다. 아래 검증 수치는 후보 제작 당시의 결과다.

## 산출물과 변경 범위

- 후보: `.codex_tmp/lower-weight-20260905/candidate-r02/R20_LowerWeightCandidate_r01.blend`
- SHA256: `21a2eac95095e04a441b86486511b9805509920dfba1b9b21b65f2ca469add47`
- 비교: 같은 폴더의 `LowerWeightBeforeAfter.png` — 왼쪽 원본, 오른쪽 후보.
- 도구: `tools/blender/create_c1b_rw020_lower_weight_candidate.py`
- [구조화 기록](R20_LOWER_WEIGHT_REVIEW_20260905.json)에 원본·도구·후보·원시 검사·이미지 hash를 기록했다.

기존 Thigh/Calf 영향을 함께 가진 정점에서 두 그룹의 합을 유지하며 분배만 조정했다.
수정은 원본 좌표 z<0.175m에 한정하고 깊이 방향을 반영해 무릎 뒤쪽 압축을 분산했다.
변경 정점은 35,714개, 변경 그룹은 Thigh_L/Calf_L/Thigh_R/Calf_R 4개다.
상체·Foot·Pelvis·HipDeform 등 나머지 그룹과 Head weights는 동일하다.
메시 좌표·연결 구조, bind skeleton, bone 개수, Armature modifier는 동일하다.
최대 영향 뼈 수4, 좌우 weight 최대 오차5.96e-8이며 저장 후 재검증했다.

승인된 준비0.10초 펀치 Action2를 변경 없이 복사했다. 저장 후 곡선 값도 동일하다.
L/R 각9개 frame에서 기존 승인 파일과 비교한 최대 정점 차이는1.67e-8m다.
Neutral 평가 결과의 최대 정점 차이는6.67e-8m로 부동소수점 수준이다.

## 검증 결과

| 검토 범위 | 표본 수 | 결과 |
|---|---:|---|
| FK Calf40/50/60/70/80° | 5 | PASS |
| 발 위치·방향 고정 IK, pelvis 하강15/25/35mm | 3 | PASS |
| Walk 전 주기 | 36 | PASS |
| Sprint 전 주기 | 24 | PASS |
| 승인 Punch L/R 평가 frame | 18 | PASS, 기존 pose와 일치 |
| Grab/Wall L/R/LR 정적 pose | 6 | PASS |

모든 표본에서 manifold PASS, 인접 면45° 이상 급격한 꺾임0, 90° 이상 foldover0,
비인접 삼각형 자기 교차0을 확인했다. Grab/Wall은 여기서 정적 회귀만 검사했다.

| 기존 실패 사례 | 원본 | 후보 |
|---|---|---|
| FK80° | foldover12, 자기 교차468쌍 | 둘 다0, 인접 면 최대15.61° |
| 발 고정·pelvis35mm 하강 | foldover14, 자기 교차490쌍 | 둘 다0, 인접 면 최대34.64° |

## 남은 검토와 다음 단계

1. 비교 이미지에서 깊게 굽힌 하체의 둥글게 압축되는 형태를 검토했고, 사용자가 후속 진행을 승인했다. 표면 검사 PASS와 사용자 결정은 각각 기록한다.
2. 발 고정·35mm 하강 자세에서 발 뼈 목표 오차는1.43e-7m지만, 표면 최저점은 Neutral 기준보다 **3.53mm 낮다**. 실제 바닥과의 접지·충돌 보정은 후속 제작에서 검증한다.
3. 깊은 자세의 edge 길이 비율은 원래 길이 대비 약0.166~2.885 범위다. 표면 교차는 해결됐지만 압축·늘어남 자체가 없다는 의미는 아니며, 별도 비율 합격 기준을 선언하지 않았다.
4. 채택된 후보를 별도 작업 원본으로 Jump/상승/정점/낙하/착지 시제품을 제작한다. 원본 r20을 덮어쓰지 않는다.

검사 각도는 표본 입력이며 모든 조합의 Joint limit 보증이 아니다. 새 뼈·geometry/corrective 변경,
Unity 반입·실제 물리 통합·Player Build·commit/push는 이번 범위에 포함하지 않았다.

재실행 예: Blender에서 `--background --factory-startup --python-exit-code 1 --python
tools/blender/create_c1b_rw020_lower_weight_candidate.py -- <새 출력 폴더>`.
기존 후보 폴더는 덮어쓰지 않는다. 전체 repository inventory/license 검증의 기존 미해결 항목은 이 표본 PASS와 별개다.

문서 반영 후 document baseline PASS, workflow tests7/7, art profile PASS와 tests25/25
(221 assertions), `git diff --check`를 확인했다. 후보·도구·원본 r16/r20·승인 펀치·증거 hash도 재확인했다.
이번 변경은 Blender 후보와 문서이므로 Unity 실행 검사는 새로 수행하지 않았다.
