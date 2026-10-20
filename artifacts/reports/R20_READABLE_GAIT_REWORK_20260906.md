# 걷기·달리기 기본 동작 재작업 — 2026-09-06

사용자가 발 움직임이 잘 보이지 않고 어색하다고 지적했다. 발 들림·보폭이 작고 체중 이동이 부족한 것을 확인했고,
“다시 가보자” 지시에 따라 현재 이동 속도와 모델 비율을 유지하며 보행 표현을 재작업했다.
현재 기본 시제품은 `ReadableGait=true`다. 앞선 빠른 발놀림/느린 이동 후보는 선택하거나 적용하지 않았다.

## 바뀐 동작

- 지지 구간은 발을 뒤로 보내며 버티는 선형 궤적, 발을 옮기는 구간은 끝점 속도가 이어지는 Hermite 궤적으로 구분한다.
- 무릎을 접어 발을 더 높이 들고 앞으로 보내며, 골반은 지지 다리 쪽으로 조금 이동·기울인다.
- 양발 지지 전환 때 골반이 조금 낮아지고 한 발 지지 중에 올라온다. 빈 팔은 몸의 체중 이동을 따라간다.
- 착지 직후 작은 보행 강도에서는 자세 진입을 완만하게 해 무릎이 갑자기 접히는 것을 줄인다.
- 점프의 물리 이동·접촉 판정, 발 주기 유지, 사용 중인 팔 보호와 Reset 경로는 유지한다.

이동 설정은 Walk1.5m/s·Sprint2.25m/s, 기본 발 주기는1.65/2.5Hz 그대로다.
PhysicsRoot 속도·모델 mesh/weights·뼈 수를 바꾸지 않았다.

## 이전과 새 동작의 관측값

| 항목 | Walk 이전 → 재작업 | Sprint 이전 → 재작업 |
|---|---|---|
| 발 뼈 기준점 최고 들림 | 약4.7 → 18.7mm | 약7.7 → 21.0mm |
| 앞뒤 전체 이동 폭 | 약29.9 → 81.4mm | 약45.9 → 104.1mm |
| 골반 좌우 이동 폭 | 약0 → 7.5mm | 약0 → 13.4mm |
| 골반 상하 이동 폭 | 약0 → 2.8mm | 약0 → 3.8mm |
| 무릎 굽힘 범위 | 약29.3~42.6° → 34.2~67.6° | 약32.2~52.1° → 32.2~71.0° |

명목 foot lift는20/22mm, half-stride는40/50mm이며 실제 값에는 보행 강도와 곡선 형태가 반영된다.
골반 translation은±4/7mm, roll은±1.5/2.5°에 보행 강도를 곱한다. 모두 시제품 tuning값이다.
추가 발목 pitch는 높은 발 들림에서 표면 꺾임을 만들어 최종안에서 제거했다.
발목을 더 비트는 대신 무릎 굽힘과 발 위치로 들어 옮기는 동작을 표현한다.

## 검증

- 전체 EditMode85/85, PlayMode18/18 통과, skip0.
- 8방향, 공중 반전/90° 변경/정지, 착지 후 Sprint, 재점프, 팔 점유, Reset,30/120fps 비교 등 기존18조건 통과.
- 정지 후 하체가 초기 대기 자세로 복귀하고, 점프/착지에서 보행 주기를 재시작하지 않는 검사를 유지했다.
- 새 Walk39frame·Sprint25frame의 한 주기 전체에서 인접 삼각형 면45° 이상 꺾임0,90° 이상 뒤집힘0.
- 새 표면의 최대 인접 면 각도는 Walk약35.27°, Sprint약42.48°였다.
- 발 IK 목표 오차 최대 약5.29e-7m, 도달 범위 보정0m. 관측된 최대 바닥 침범은 Sprint약0.66mm였다.
- 실제 player-loop frame을 넘긴 뒤 촬영하고, 배경 없는 캐릭터 화면과 CPU pose 일치 검사도 통과했다.

고밀도 mesh의 작은 삼각형에 맞춰 면 법선은 `Vector3.normalized`의 작은 벡터 절단값을 사용하지 않고 계산했다.
위 검사는 인접 면 꺾임 검사이며 비인접 자기 교차 전수 검사는 아니다.
발 기준점 들림·도달 오차도 완전한 월드 발 고정이나 미끄러짐0의 증명이 아니다.

## 영상과 실행

[구조화 기록](R20_READABLE_GAIT_REWORK_20260906.json)에 결과·검사 XML·소스 snapshot·영상 hash를 연결했다.
출력 폴더는 `.codex_tmp/gait-rework-20260906/Review-20260905-171630/`다. 실행 폴더 시각은 UTC다.

- `Media/Comparison.gif` / `.mp4`: 왼쪽 이전, 오른쪽 재작업. 위 Walk, 아래 Sprint.
- `Media/Fixed.gif` / `.mp4`: 고정 카메라에서 보는 새 기본 보행.
- `Media/FootPhaseComparison.png`: 발 동작의 확대 비교.
- `SourceSnapshot/`: 검토 당시 변경 코드 원문.

MP4는 디코딩120frame·60fps·2초로 확인했다. GIF는30fps 추출에 정지/반복 안내를 추가했다.
Unity 메뉴 `Project Hotfix → Prototypes → Open Physics Jump Review`의 Play에서
WASD 이동·Left Shift 달리기·Space 점프·R 초기화로 확인할 수 있다.

현재는 독립 시제품이다. 완전한 stance foot 고정, 지형별 발 IK, 최종 이동/보폭 tuning,
실제 전투·다관절 Ragdoll·network 통합과 사용자 최종 체감 검토는 남아 있다.
원본 r16/r20·승인 하체 웨이트·펀치 파일, Jump clip0 방침을 유지했다. Player Build·commit/push는0이다.

문서 baseline·workflow tests7/7, art profile·tests25/25(221 assertions), `git diff --check`와
원본/승인 파일·코드 snapshot·검사/영상 hash도 확인했다. ProjectSettings·Packages·기존 Art 파일 변경은0이다.
