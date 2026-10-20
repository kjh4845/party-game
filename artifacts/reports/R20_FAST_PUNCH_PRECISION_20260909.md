# 빠른 펀치·부위별 누적·판정 정밀화 후보

## 확정한 사용자 결정

2026-09-09: 몸통·팔다리1점, 머리2점, 첫 넉다운 기준10점. 반복 Down마다 기준이1점씩 감소해10→9→8→7→6에서 멈춘다. groggy 대기 시간 증가도 기존1.5→2→2.5→3초 상한을 유지한다. 이는 2026-09-08의 항상5회 고정 정책을 대체한다. 시간 경과에 따른 누적 감소는 없다. Down 재피격은 impulse만 적용하고 새 Down/누적/timer 연장은 없다. Round reset은 누적과 DownCount를 초기화한다.

## 구현된 후보

- 승인된 원본0.7초 clip을1.4배 속도로 재생해 동작0.5초, 팔꿈치 준비 약0.071초, 뻗기 약0.095초로 줄인다. 원본 pose·model·rig·weight는 수정하지 않는다.
- authority와 presentation이 동일한 rate를 사용한다. 재공격 가능 시점도 같은 비율로 짧아진다. 정밀 후보의 명중 구간은 source0.10~0.233초, 실제 약0.071~0.167초의 팔을 뻗는 구간으로 제한해 회수 구간의 새 명중을 금지한다. Tap/Hold 구분0.15초는 유지한다.
- 기존 `Open Articulated Punch Candidate` 및 `Open Fast Punch Candidate` 메뉴는 속도·정밀 판정·확정 누적 정책을 함께 적용한 후보를 연다. 이전12-body/5회 고정 비교는 `Open Previous Articulated Punch Comparison`으로 보존한다. rate는 scene 직렬화 필드로 보존한다.
- 부위별 누적과 DownCount별 기준 감소를 명시적 profile로 구현했다. 중복 action, Down 중 추가 누적, Round 이전 이벤트는 여전히 거부한다. 첫 기준10점은 몸통만10회 또는 머리만5회에 해당한다.
- 정밀 판정은 승인된 source의 손 중심 경로를 따라120Hz source 간격으로 나누어 sweep한다. 판정 반경은4.5cm에서3cm로 줄인다. 화면의 bone transform을 읽어서 Hit를 결정하지 않는다.
- 머리·몸통의 별도 query capsule을 만들고 이동용 전신 capsule을 정밀 타격 대상으로 사용하지 않는다. 이 둘의 판정은 물리 충돌·잡기 collider와 분리된다. 머리 판정은 머리 volume에 실제 접촉했을 때만2점이다.
- 팔다리에도 개별 query capsule을 둔다. 별도 물리 검사용 뼈가 동일한 걷기·펀치·잡기 pose 계산과 authority 입력을 사용한다. source bind data만 공유하고 화면의 bone transform은 읽지 않는다. 넉다운 중에는 실제 물리 부위 collider로 전환한다.

## 확인된 기존 문제

기존 Hit는 x=±0.12m, y=0.66m의 고정 직선과 반경0.045m를 사용한다. 실제 주먹 끝과 최대0.1131371m 차이가 비교 fixture에서 측정됐다. 이는 공격·상대의 방향/자세에 따라 허공 명중으로 보일 수 있는 근거다. 기존 전신 이동 capsule은 머리/몸통을 구별하지 못한다.

동일 높이에서 현재 펀치는 주로 몸통을 향한다. 머리 Hit는 실제 높이·자세 차이로 주먹이 머리에 닿아야 한다. 자동 머리 조준이나 무작위 head 보너스는 추가하지 않았다.

## 검증

- 속도 비교: 동일 입력 구간에서 기존5회, 후보7회. source pose와의 최대 회전 차이0도.
- body/arm/leg1·head2, 사거리 밖, 좌우 빗나감, 벽 차단, 무관한 trigger 무시:30/60/120Hz 물리 테스트.
- 첫 단발 판정은 여러 source segment를 지나도 한 번만 누적한다.
- 별도 query rig가 걷기·펀치에 따라 움직이고, 화면의 bone 수정에는 영향받지 않는지 검증했다.
- 고정 표적에 실제 sweep10회 → Down1 → 다음 기준9점·대기1.5초 → Round reset 기준10점 복원을 검증했다. 표적 위치 고정은 판정/상태 연결용 fixture이며 실전 손맛 검증은 아니다.
- 직렬화된 review 복제의 Play 초기화에서 punch/grab pose cache 복구와 rate1.4 보존을 검증했다. 수동 Unity UI 조작 검증을 대신하지 않는다.

증거 폴더: `.codex_tmp/punch-precision-20260909/`. 속도 비교는30fps 정상속도이며 마지막 프레임1초를 유지한다. 실제 다인 입력/peer/balance 승인은 별도다.

## 최종 검증 기록

- 팔·다리 query rig 추가 전 전체 PlayMode64/64 통과. 이후 변경된 상호작용/표현21/21 통과, 마지막 Play 초기화 보완 후 후보5/5 통과. 최종 메뉴 연결 뒤 전체 EditMode125/125 통과. 중복 실행을 신규 테스트 수로 합산하지 않는다.
- 후보의 판정 fixture는9조건×3물리 주기=27조건이며 팔 회수 중 뒤늦게 진입한 표적도 빗나간다. source pose 회전 오차0도, 검사용 뼈와 표시용 뼈 위치 차이3cm 미만을 검사했다. 표시용 bone를 이동시켜도 query 위치는 변하지 않는다.
- 문서 기준 PASS, workflow7/7, `git diff --check` 통과. 원본 Blender4개, GetUp clip2개, 승인 punch library·모델 transport SHA-256 일치.
- 수정 소스14개 snapshot, 테스트 XML, 정상속도 비교 GIF는 [구조화 기록](R20_FAST_PUNCH_PRECISION_20260909.json)에 연결했다.

판정은 source 기반 주먹 구체와 부위별 capsule 근사다. 정확한 skinned triangle 접촉이나 실제 다인 조작감·peer·최종 성능 승인을 뜻하지 않는다.
