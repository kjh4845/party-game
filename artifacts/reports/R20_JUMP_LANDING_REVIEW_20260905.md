# r20 점프·착지 애니메이션 시제품 — 2026-09-05

**후속 결정:** [물리 점프 전환 승인](R20_PHYSICS_JUMP_DECISION_20260905.json)으로 전용 clip 제작을 중단했다.
이 Action5와 아래 검증 수치는 비교 자료로 보존하며 현재 실행 방식은 Unity 물리+procedural 하체 pose다.

사용자가 승인한 하체 웨이트 후보를 기준으로 in-place Action5를 제작했다.
저장 후 표면 검사201표본, 접지 곡선 사이384표본을 통과했다. 애니메이션 시각 검토와 Unity 통합은 별도다.
하체 후보의 채택은 [사용자 승인 기록](R20_LOWER_WEIGHT_VISUAL_APPROVAL_20260905.json)에 반영했다.

## 산출물

- Blender: `.codex_tmp/jump-landing-20260905/review-r03/R20_JumpLandingAnimationCandidate_r01.blend`
- SHA256: `4af16ee803d5e71dc26e1095ead31e251e1e554d3985bc8b170de9949c7fee46`
- 영상: 같은 폴더의 `JumpLandingPreview.gif`, 두 시점·30fps·끝 자세0.5초 정지 포함.
- 정적 비교: `JumpLandingContactSheet.png`.
- 도구: `tools/blender/review_c1b_rw020_jump_landing.py`.
- [구조화 기록](R20_JUMP_LANDING_REVIEW_20260905.json)에 원본·후보·도구·검사·미디어 hash를 연결했다.

| Action | 검토 길이 | 표현 |
|---|---:|---|
| REVIEW_Jump_Takeoff_r01 | 0.333초 | 무릎 접어 준비 → 몸 펴며 도약 |
| REVIEW_Jump_Rise_r01 | 0.267초 | 팔을 앞에 두고 다리 접으며 상승 |
| REVIEW_Jump_Apex_r01 | 0.133초 | 정점 구간에서 다리 풀기 시작 |
| REVIEW_Jump_Fall_r01 | 0.267초 | 다리 펴며 접촉 준비 |
| REVIEW_Jump_Landing_r01 | 0.467초 | 발 접촉 → 무릎 압축 → 대기 자세 복귀 |

총1.467초는 연결 검토용이다. 0.22m 포물선은 렌더에만 적용하고 후보에 Root/객체 이동으로 저장하지 않았다.
Blender Action Editor에서 위 Action을 선택해 각각 frame1부터 길이×30+1까지 확인한다.
승인된 펀치 Action2도 변경 없이 함께 보존해 총 Action7이다.

## 검증과 접지 보정

- 원본 하체 웨이트·메시·bind skeleton 동일, Bone22·helper driver8 보존. 승인 펀치 곡선 동일.
- 재오픈한 phase 표본93, 키 사이 회전 보간99와 공통 Grip L/R/LR 조합9, 합계201표본의 manifold PASS,
  45° 이상 급격한 면 꺾임0, 90° 이상 foldover0, 비인접 삼각형 자기 교차0.
- phase4경계와 시작/종료 대기 자세의 최대 정점 차이0m.
- 좌우 대칭 최대 정점 차이2.7e-07m, 접지 발 뼈 XY 오차1.45e-07m.
- 기존 깊은 자세의3.53mm 접지 잔차는 이 clip의 pelvis 높이에서 보정했다. 원본 weight 파일은 수정하지 않았다.
- 회전 key 간격1/4 frame, 접지 pelvis 위치 key 간격1/16 frame. 별도의1/32 frame 위치에서
  접지 사이384표본을 검사해 최대 바닥 높이 오차 **0.02545mm**로 검토 기준0.1mm 이내다.

초기 실행 review-r01/r02에서 더 엄격한 접지 보간 검사의 초과를 확인했다.
최종 review-r03은 관절 회전 방향을 유지하고 pelvis 높이 곡선을 추가 보정한 결과다.
이전 실행의 실패 기록은 보존하며 최종 후보로 사용하지 않는다.

## Unity 연동 계약과 후속 체크리스트

1. Blender에서는 평지에서 접촉할 기본 자세를 맞춘다. 실제 지형의 바닥 감지, 발 IK와 물리 충돌은 Unity에서 구현한다.
2. 준비 애니메이션 완료를 기다려 권한 `JumpAccepted`의 impulse를 지연시키지 않는다. 준비 연출은 실제 상태에 맞춰 단축/생략할 수 있다.
3. 상승/정점/낙하/착지 선택은 권한 Ground/Air, 수직 속도, 실제 접촉을 따른다. 영상 frame이나 Animation Event가 접촉·impulse 판정의 원인이 되지 않는다.
4. 이동 중 점프, 경사·단차, 낙하 높이별 착지, 중단/Down/round reset과 연속 Grip layer 전환은 후속 scenario다. 이번9개 Grip 검사는 정적 조합이다.
5. 현재 Action은 Blender 시제품이다. FBX/Unity AnimationClip 변환, 실제 Controller/Collider/physics 통합,
   CHR-005/ANP-002 전체 완료, Player Build 또는 commit/push를 수행했다는 뜻이 아니다.

재실행: Blender의 `--background --factory-startup --python-exit-code 1 --python
 tools/blender/review_c1b_rw020_jump_landing.py -- <새 출력 폴더>`.
보간/Grip 추가 검사는 구조화 기록에 연결한 스크립트의 후보 경로를 새 폴더로 지정해 실행한다.

문서 반영 후 baseline PASS, workflow tests7/7, art profile PASS와 tests25/25(221 assertions),
`git diff --check`, 후보·도구·증거·원본 r16/r20·승인 펀치 hash를 확인했다.
이번 변경에는 Unity runtime 수정이 없어 Unity 실행 검사를 새로 수행하지 않았다.
기존 repository inventory/license 미해결 항목은 이번 Blender·문서 검증 PASS와 별개다.
