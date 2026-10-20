# 몸통 물리 분할·관절 자유도 후보

사용자가 몸통 물리 분할과 관절 자유도 조정을 요청했다. 새 후보는 `ArticulatedTorso=true`인 별도 검토 장면으로 제공하며, 기존 기본 구성은 비교 기준으로 유지한다. 기존 단일 몸통 후보를 보존하고, 같은 r20 rig/mesh를 쓰는 opt-in 후보를 구현한다. 원본 `.blend`, geometry, bone hierarchy와 skin weight는 수정하지 않는다.

## 구성

| 부위 | 물리 질량 | 연결 |
|---|---:|---|
| 골반 역할의 기존 Actor Rigidbody | 3kg | 다리와 Spine의 기준 |
| 배/허리 `Spine` | 2.5kg | 골반 → Spine |
| 가슴 `Chest` | 2.5kg | Spine → Chest |
| 머리 | 3kg | Chest → Head |
| 양쪽 위팔/아래팔 | 합계 4kg | Chest → UpperArm → Forearm |
| 양쪽 허벅지/종아리·발 | 합계 5kg | 골반 → Thigh → Calf·Foot |

총20kg을 유지하며, 물리 body는10→12개, joint는9→11개다. 기존 몸통8kg을3/2.5/2.5로 나눴다. 내부 Neck bone과 기존 비물리 Clavicle/Hand/helper는 그대로이며 새 bone이나 원본 rig 수정은 없다.

골반 충돌체는 하체 쪽으로 줄이고 Spine/Chest 충돌체를 추가한다. 머리와 팔을 움직이는 가슴에 연결해 몸통의 실제 굽힘이 전달되도록 한다. 모드는 Bind/Play 전에 선택하고, 질량/구성 선택은 Bind 시 저장한다.

## 관절·표현

- Spine 전후 ±10°, Chest 전후 ±12°와 각각 옆 방향4°를 검토 시작값으로 사용한다. 몸통 축 비틀림은 잠근다.
- 머리 회전 범위를 기존10°에서20°로, 위팔의 추가 옆 방향 움직임은4°, 허벅지는2°로 한다. 기존 팔꿈치/무릎 굽힘 방향과 축 비틀림 방지는 유지한다.
- 넉다운 대기 구간에는 pose drive/animation/fade0을 유지하고 실제 물리 body의 뼈 pose를 표시한다.
- 넓은 몸통 굽힘을 사용한 첫 후보는 기존 skin에서 표면 뒤집힘이 생겨 채택하지 않았다. 현재 후보는 기존 skin으로 검증되는 범위를 사용한다.

## 회복 연결

분리된 가슴의 실제 rotation을 게임의 torso 축으로 변환해 회복 방향을 판정한다. actor root의 회전만으로 가슴 방향을 대신하지 않는다. 지지/속도 query도 가슴 body를 기준으로 한다.

회복 단계에서만 질량에 비례한 지지력을 각 부위에 나누고, 전체 관성에 맞춘 회전 보조를 분산한다. 작은 골반 body 하나에 전체 힘을 몰지 않는다. 회복용 몸통 joint drive도 별도로 적용하며 Down 대기 때는0이다.

회복 완료 직전에 서 있는 충돌체를 먼저 활성화해 안정화하고, 자신의 ragdoll과 충돌하지 않도록 ignore pair를 재적용한다. 이후 기존 momentum 전달과 입력 복귀를 수행한다. Root pose를 애니메이션으로 순간이동시키지 않는다.

## 검토 범위

- 앞/뒤/좌/우 낙하, 천장/제거, 측면 정렬, 회복 중단, Reset, 반복 회복.
- 실제 torso 변형량, 표면 인접각45° 미만, joint gap15mm 미만, root authority 불변.
- 회복 직후1초 흔들림, 즉시 이동, 실제 펀치 누적5회와 Down duration 증가/Reset.
- 피격 fixture는 사거리 확보용 위치 배치와 Down 후 공격자의 뒤로 이동을 사용한다. 상대에게 몸이 걸린 상태와 회복 공간이 있는 조건을 같은 증거로 취급하지 않는다.

정식 physics/성능 profile, peer·다인 조작·균형 검증은 후속이다. 더 큰 굽힘을 위해 skin/rig 수정이 필요하다면 별도 승인 대상으로 다룬다. Player Build·commit/push는 수행하지 않는다.

## 실행

- `Project Hotfix > Prototypes > Open Articulated Torso Candidate`: 1/2/3/4 방향별 Down, R Reset.
- `Project Hotfix > Prototypes > Open Articulated Punch Candidate`: 흰 캐릭터로 주황 표적을 때리는 실제 피격/회복 검토. WASD/Shift/Space, LMB/RMB tap/hold/release, R Reset.
- 기존 `Open Ragdoll Recovery Candidate`와 `Open Punch Down Review`는 이전 단일 몸통 비교 구성을 유지한다.

넉다운 타격 수 고정5회, 시간 감소 없음, Down 대기 시간1.5/2.0/2.5/최대3.0초의 START 규칙은 유지한다. 부위 질량이 바뀐 후보이므로 충격/Grab/Throw의 최종 체감과 다인 성능을 승인했다고 해석하지 않는다. 넉다운 진입 impulse는 기존의 실제 적용 body 질량 기반 속도 상한을 사용한다.

## 반복 전환 보완

캐릭터가 회복 후 방향을 바꾸면 비활성 proxy가 이전 world pose에 남아 있었다. 이를 그대로 활성화하면 새 joint가 이전 child pose와 현재 parent heading 사이에서 잘못된 각도 기준을 만들었다. 다음 Down 진입 전, 비활성 proxy를 현재 actor heading의 원래 bind pose로 준비하고 joint를 활성화한 뒤 실제 진입 pose를 적용하도록 수정했다. 이는 보이는 캐릭터 pose를 애니메이션으로 고정하는 동작이 아니다.

0/90/180/270/45/225°에서 순서대로 재활성화한 뒤, 외부 torque가 없는 자유낙하 첫 step에 설명되지 않는 회전이나 joint gap이 생기지 않는 회귀 검사를 추가했다. 넉다운 drive/animation0, mass20,12 body와 방향별/반복 회복 및 handoff 검사를 함께 사용한다.

검증 결과·영상 출처·최종 소스 snapshot은 [구조화 기록](R20_ARTICULATED_TORSO_REVIEW_20260908.json)에 보존한다.

## 최종 결과

- 최종 소스 기준 EditMode 117/117·PlayMode 52/52 통과, skip0. 기존 구성과 새 후보를 함께 회귀 검사했다.
- 10조건 최대 골반-가슴 상대 회전 14.27°, 최대 표면 인접각 30.93°, joint gap 14.77mm.
- 네 방향 기립 후 1초 yaw 변화0°, 높이 변화 최대 0.227mm, 이후 0.5초 이동 모두0.27m 이상이었다.
- 실제 입력/sweep으로 펀치5/5/5/5회와 대기 시간1.5/2.0/2.5/3.0초, 네 번의 회복·이동/Reset을 확인했다.
- 비교 GIF는 실제 source0~1.4초의29프레임을0.5배속으로 표시한다. 이 구간에는 회복 clip이 재생되지 않는다. 정지/반복 포함3.9초이며 전체 피격/회복 GIF는1배속·8초다.
- 원본/보존 Blender4개, GetUp clip2개와 표현 소스 hash 일치. 문서 baseline·workflow7개·diff check 통과.
