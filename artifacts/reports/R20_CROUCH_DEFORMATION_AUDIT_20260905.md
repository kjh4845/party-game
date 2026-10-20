# r20 점프 준비·착지용 하체 변형 사전 검사

승인된 펀치 다음으로 Jump/착지 제작을 시작하기 전에 발 접지와 하체 굽힘을 검사했다.
더 큰 굽힘에서 다리 뒤쪽 표면 뒤집힘이 재현돼 당시 하체 수정 범위 확인을 요청했다.
후속 사용자 지시로 [weight-only 후보](R20_LOWER_WEIGHT_REVIEW_20260905.md)를 제작했고 92표본 표면 검사 PASS 후 사용자가 후보 채택을 승인했다.
아래 실패 수치와 원시 JSON은 원본 r20의 사전 검사 결과로 보존한다.
새 Jump/착지 clip은 아직 만들지 않았고 원본 r16/r20·승인 펀치 후보는 수정하지 않았다.

## 확인된 결과

| 방식 | 입력 | 결과 |
|---|---|---|
| 직접 FK 회전 | Calf40°, Thigh-20° | PASS |
| 직접 FK 회전 | Calf50°, Thigh-25° | PASS |
| 직접 FK 회전 | Calf60°, Thigh-30° | PASS |
| 직접 FK 회전 | Calf70°, Thigh-35° | FAIL: 90° 이상 surface foldover16, 45° hard edge48 |
| 직접 FK 회전 | Calf80°, Thigh-40° | FAIL: surface foldover12, self-overlap pair468 |
| 발 위치·방향 고정 IK | pelvis 하강0.015m | PASS |
| 발 위치·방향 고정 IK | pelvis 하강0.025m | FAIL: 45° hard edge28 |
| 발 위치·방향 고정 IK | pelvis 하강0.035m | FAIL: surface foldover14, self-overlap pair490 |

위 각도는 검토 입력이며 허용 Joint limit의 확정값이 아니다. 60°와70° 사이의 정확한 경계나
모든 조합을 시험한 것은 아니다. 거리도 현재 검토 원본 좌표 기준이다.

IK의 최대 발 목표 오차는 약1.48e-7m, Bone scale 최대 편차는 약3.58e-7이다.
다리를 늘려 목표에 맞춘 실패가 아니며, 별도의 FK 회전에서도 재현됐으므로 단순 IK 목표 오차만으로 설명되지 않는다.
뚜렷한 뒤집힘은 다리 뒤쪽 아래 구간에서 확인했다. 실제 수정해야 할 weight 범위는 후보 실험에서 결정해야 한다.

기존 보행 검토는 더 작은 Calf 굽힘을 사용했다. 그 범위의 기술 PASS를 소급해 실패로 바꾸지 않는다.
이번 검사는 더 깊은 준비·착지·회복 자세로 범위를 넓히면서 발견한 변형 제약이다.
원본을 유지한 접촉점-only 대안도 비교했으나 큰 굽힘과 지면 뜸을 해결하지 못해 채택하지 않았다.

## 권장 수정 범위 — 후속 후보 제작·채택 승인

1. 기존 파일을 보존하고 **하체 스킨 웨이트만 조정하는 별도 후보**를 먼저 평가한다.
2. Neutral Mesh/실루엣·Bone 개수/위치·상체 weight와 승인 펀치 Action은 유지한다.
3. 같은 FK40..80°·발 접지 표본으로 비교하고, 보행·펀치·Grab/Wall 회귀도 확인한다.
4. Weight 변경만으로 해결되지 않으면 Bone 추가·bind 수정·geometry/corrective shape 변경을 자동 실행하지 않고 다시 확인한다.
5. 후보 검토와 사용자 확인 뒤 Jump/착지 제작을 재개한다.

얕은 자세로 제한해서 시제품을 진행하는 대안은 가능하지만 이후 착지·GetUp 표현 범위를 제한할 수 있다.
이런 제한을 사용자에게 알리지 않고 새 최종 기준으로 고정하지 않는다.

## 증거와 재실행

[구조화 요약](R20_CROUCH_DEFORMATION_AUDIT_20260905.json)에 source·generator·raw report hash를 기록했다.
로컬 `CrouchAudit.json`, 전체·하체 뒤쪽 렌더16장과 `CrouchComparison.png`는
`.codex_tmp/jump-review-20260905/audit-r01/`에 있다.

Blender에서 `--background --factory-startup --python-exit-code 1 --python
tools/blender/audit_c1b_rw020_crouch.py -- <새 출력 폴더>`로 재실행한다.
이 도구의 `REVIEW_REQUIRED`는 예상된 결함 감사 결과이며 다음 제작 Gate의 PASS가 아니다.
원본 저장·weights/geometry 수정·Action 생성·FBX/Unity 반입·Player Build·commit/push는0이다.
