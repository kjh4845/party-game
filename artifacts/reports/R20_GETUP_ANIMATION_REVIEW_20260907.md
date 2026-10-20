# 앞·뒤 공통 회복 애니메이션 후보

사용자가 이전 물리 회복의 느낌은 맞다고 확인했고, 실제 회복 애니메이션을 추가하도록 요청했다.
기존 회복 방향 판별과 물리 보조를 유지하면서 **FaceUp/FaceDown 두 개의 Unity AnimationClip**을 제작하고 후보 장면의 기본 회복에 연결했다.
옆으로 누운 상태는 기존 물리 정렬 뒤 이 두 클립을 공유한다. 좌/우 전용 GetUp clip은0이다.

## 제작된 동작

- FaceUp: 몸통을 접고 몸 뒤쪽에 손을 짚는 준비→몸 일으키기→팔·다리 정리.
- FaceDown: 가슴 아래로 팔을 가져가 팔꿈치를 접고 바닥을 짚기→몸 밀어 올리기→기립·마무리.
- 두 클립은 각각1.9초·30fps·58키 시점,21개 뼈의84개 quaternion 채널을 갖는다.
- 쇄골/상완/팔꿈치·몸통/머리·허벅지/무릎/발·hip helper가 실제로 변한다. 끝 pose는 기존 기본 자세다.
- 바닥을 짚는0.45초 준비 뒤 물리 기립 목표를1.1초 동안 전개하고, 클립 종료와 실제 물리 안정 조건을 모두 만족해야 조작을 복원한다.
- 실제 ray query로 확인한 손 목표에 시각 IK를 적용한다. 도달 한계를 넘기 전에 접지를 풀며 팔 길이를 늘려 고정하지 않는다.
- 클립은 뼈 회전만 포함한다. gameplay Root translation/rotation curve·Animation Event·피해·추가 impulse는0이다.
- 몸의 위치/회전·충돌·회복 완료는 기존 authority 물리 코드가 담당한다. 시각 손 접지는 추가 물리 joint나 별도 손 추진력을 생성하지 않는다.
- 도중에 지지/공간을 잃으면0.18초에 걸쳐 시각 pose를 다시 ragdoll로 돌린다. Reset은 즉시 정리한다. 다시 허용되면 현재 pose에서 회복을 재시작한다.

## 파일과 실행

클립:
- `Project hotfix/Assets/ProjectHotfix/Prototypes/PhysicsJump/Resources/ReviewGetUp/GetUp_FaceUp.anim`
- `Project hotfix/Assets/ProjectHotfix/Prototypes/PhysicsJump/Resources/ReviewGetUp/GetUp_FaceDown.anim`

`GetUpAnimationPose.cs`는 clip 재생·시각 손 접지와 진입/중단 blend를 담당한다.
`GetUpClipAuthoring.cs`는 승인된 모델에서 두 clip의 키를 생성하는 제작 소스다. 런타임에서 clip을 재생성하지 않는다.
재제작은 Unity 메뉴 `Project Hotfix > Prototypes > Generate GetUp Animation Candidates` 또는 해당 Editor Build 메서드로 수행한다.

검토 장면은 기존 `Project Hotfix > Prototypes > Open Ragdoll Recovery Candidate`다. Play 후1/2/3/4로 낙하 방향을 시험하고 R로 Reset한다.
새 기본 후보는 `UseGetUpAnimation=true`다. 이전 물리-only 비교 검사는 명시적으로 false를 사용해 기존 기준을 보존한다.
이 옵션은 검토용이며 정식 gameplay 설정으로 노출하지 않는다.

## 검증

EditMode `GetUpClipTests`: 두 파일 존재/길이·84곡선·실제 관절 변화·서로 다른 앞뒤 몸통 준비·gameplay Root curve0·event0.
PlayMode `GetUpAnimationReviewTests`: 앞/뒤/좌/우 낙하, 천장, 천장 제거, Reset, 정지 측면 정렬2조건, 회복 중 장애물로 중단 후 재개를 검사한다.
정지 측면 fixture는 이전 물리 검토와 같이 준비 구간만 중력을 끄고 timer를 미리 소진한 뒤, alignment 진입부터 중력을 복원한다.
별도 Start/반복 검사는 실제 서 있는 시작·회복 후 이동·재진입·두 번째 회복·Reset을 검사한다.

- 회복 완료, 실제 팔꿈치 변화25° 초과, 접지 표본 존재, 완전 접지 표본의 손 목표 오차2mm 미만을 검사한다.
- animation 적용 전후 authority root 위치·회전·속도 불변, clip 시간 전 입력 잠금, 중단 시 fade와 재시작을 검사한다.
- 기존 surface45°·joint gap15mm·handoff15mm 기준을 유지한다. active 구간은2physics step마다 표면을 검사한다.
- 영상은 실제 skinned render를20fps로 캡처한다. 네 방향 낙하와 정지 측면 정렬은 별도 영상이다.

## 범위

원본 모델·mesh·weight·리그·승인 펀치/접지, 총 질량20kg과 던진 뒤 일반 충돌 Damage·피해 누적 제외 정책을 유지한다.
기존 물리 회복의 느낌을 폐기하거나 모델을 다시 만드는 작업이 아니다.
이번 클립의 자세·속도는 시각 검토 후보다. 지형별 손/발의 전체 skin 접촉, 잡힌 팔·공격 중 Down, 복잡한 장애물의 중간 궤적·2/3/4인·peer·정식 Animator 통합은 후속이다.
Player Build·원본 blend 저장·commit/push는 수행하지 않는다.

## 검증 결과

전체 EditMode110/110·PlayMode35/35 통과, skip0. 신규10조건의2,345표면 표본에서 최대 인접 면각24.73° 미만, 완전 접지 표본의 손 목표 오차0.33mm 미만이었다.
원본/승인 후보4개 SHA-256 유지, 문서 baseline·workflow7개·diff check 통과. 전체 영상2개는 각160프레임·20fps·8초로 확인했다. 확대 GIF는 회복4초 구간과 정지/반복 카드1초다.
[클립·영상·소스·구조화 검증 기록](R20_GETUP_ANIMATION_REVIEW_20260907.json)을 참고한다.

후속 사용자 피드백: 이 동작은 아직 어색하므로 자연스럽게 서도록 수정해야 한다. [무력 래그돌·단계별 회복 수정 후보](R20_NATURAL_GETUP_REVIEW_20260907.md)가 후속이며, 위 수치 PASS는 시각 승인이 아니다.
