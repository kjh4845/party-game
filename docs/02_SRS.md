# Project Hotfix 간결 시스템 요구사항

## 0. 문서 정보

| 항목 | 값 |
|---|---|
| 문서 | 소프트웨어 요구사항 명세서 |
| 버전 | 1.8.0 Lean System Requirements + 2026-09-04 Action Presentation Addendum |
| 기준일 | 2026-09-05 |
| 제품 목표 | Alpha = Vertical Slice, 이후 G4 Steam 제품 통합과 G5 1.0 |
| 제품 연결 구조 | Host Client Authority P2P |
| 현재 캐릭터 결정 | r16 Neutral 방향 승인, r20 내부 리그 후보 보존. C1b exact·game Prefab·물리 결과 미승인 |
| 상세 패치 기준 | `docs/PATCH_DESIGN.md` 0.5.0 Approved Firearm Runtime Baseline |
| 문서 권한 | 이 문서는 제품·시스템 결과를 정의한다. Protocol byte layout과 test fixture 내부 구현은 별도 구현 명세에서 다룬다. |

이 버전은 기존의 구현 세부 중심 SRS를 대체한다. 정확한 byte layout, hash preimage 수식,
부동소수점 연산 순서, 암호 transcript 구성과 반복 fixture 횟수는 의도적으로 제거했다.
후속 Protocol·Asset Pipeline·Test 명세가 이를 구체화할 수 있지만, 아래 결과와 신뢰 경계를
약화해서는 안 된다.

### 2026-10-28 3인칭 카메라·마우스 시선·누르는 즉시 펀치 결정

- 카메라를 공용 고정 구도에서 플레이어별 3인칭(TPS)으로 바꾼다(사용자 결정: "마우스로 바라보게 하고 움직이게
  하자, tps 전투가 좋을 것 같다"). 마우스로 시선(좌우·위아래)을 돌리고, 캐릭터 몸은 항상 시선의 좌우 방향을
  바라본다(벽을 잡고 있을 때만 벽을 본다). `WASD`는 시선 기준으로 움직이며 옆·뒤로 걸어도 시선 방향을 유지한다.
  PRD 4.1의 "공용 카메라 화면 축 기준 이동과 이동 방향 자동 회전, 플레이어별 Camera 회전 없음", PRD 4.6 공용
  카메라와 SRS-PHYS-001·SRS-CAM-001~003은 이 결정으로 대체한다. 관전자·탈락자 카메라와 네트워크로 시선을 보내는
  방식은 아직 정하지 않았다.
- 카메라는 캐릭터 오른쪽 어깨 너머, 머리 높이 위에서 본다(시제품 거리 2.2 m, 어깨 쪽 0.32 m, 발에서 0.95 m).
  캐릭터와 카메라 사이에 벽·바닥·다른 플레이어 등 무엇이 오든 그 앞으로 바로 당겨져 캐릭터가 항상 보이고,
  가린 것이 없어지면 천천히 원래 거리로 돌아간다. 어깨 쪽으로 비켜 서는 거리도 캐릭터 머리에서 검사해 벽 너머로
  나가지 않는다.
- 몸이 시선을 따라가는 정도(시제품 측정): 마우스를 4틱에 걸쳐 90° 돌리면 멈춘 뒤 약 0.27초 안에 3° 이내로 맞고,
  360°/s로 계속 돌릴 때 평균 5°, 90°/s에서 2° 이내로 따라간다. 기존 상대 바라보기 회전으로는 360°/s에서 66°
  뒤처졌다. 회전 감쇠는 가벼운 몸통 뿌리 하나가 아니라 몸 전체의 회전 속도로 잰다(뿌리 속도로 감쇠를 키우면
  매 틱 ±35°씩 떨렸다).
- 펀치는 버튼을 누르는 순간 나간다(사용자: "펀치를 막 때려야 되는데 안 나간다"). 0.15초 넘게 누르고 있으면 뻗던
  손이 그대로 잡기로 바뀐다. 펀치 중에 다시 누르면 그 펀치의 타격 구간이 끝나는 즉시 다음 펀치가 나가서 같은 손으로
  초당 약 4번 칠 수 있다. 발이 바닥에 있든 턱 위나 공중에 있든 나가고, 벽에 매달린 동안에만 누르면 잡기다. 이전에는
  짧게 눌렀다 뗄 때 나갔고, 몸 높이가 0.065 m를 넘으면(벽 위) 공중으로 판정해 아무것도 나가지 않았다. 공중
  발차기는 아직 없어 공중 클릭도 펀치다.
- 옆·대각선·뒤로 걸을 때 보폭을 줄이지 않는다(기존 시제품은 몸 방향과 다른 이동에서 보폭을 0.65배로 줄여 발이
  미끄러졌다).
- 직접 해 보는 장면: `Assets/ProjectHotfix/Scenes/FightTest`·`ClimbTest_LowWall`·`ClimbTest_HighWall`. 검토용
  메뉴 장면과 캡처 테스트는 기존 고정 카메라와 떼면 나가는 펀치를 그대로 쓴다.

### 2026-10-26 r46 벽 오르기 결정

- 벽은 보정 없이 플레이어가 직접 오른다. 서 있거나 공중에서 앞쪽 벽면(발 기준 0.3 m 이상)을 잡으면
  매달리고, 몸은 쓰러진 사람을 들어 올리는 힘을 거꾸로 써서 잡은 손 쪽으로 끌려 올라간다. 매달린 채
  `Space`를 누르면 무릎을 웅크렸다 펴며 위로 뛰고(시제품 3.2 m/s), 플레이어가 잡기 버튼을 놓았다가 다시
  눌러 더 높은 곳을 잡는다. 뛰어오르는 동안에는 손이 벽에서 살짝 떨어져 있다가 점프 꼭대기에서 잡는다.
  벽 점프 횟수 제한은 아직 없다(나중에 추가).
- 꼭대기 0.15 m 안을 잡고 있을 때 `Space`를 누르면 꼭대기를 넘는 점프가 된다. 판정은 누르는 순간에
  하고, 위로는 지금 높이에서 꼭대기 +0.17 m까지 오를 만큼(최소 3.2 m/s), 안쪽으로는 벽 쪽 이동 입력에
  비례해 최대 1.4 m/s(입력 절반 이상이면 최대)를 0.2초 동안 준다. 올라서려면 플레이어가 손을 놓고 벽 쪽으로
  움직여야 한다. 시제품에서 손 놓기는 `Space` 뒤 낮은 턱 약 0.18초·1.6 m 벽 약 0.28초 안이면 되고, 이동
  입력은 약 0.2초 안에 시작해야 한다.
- 손으로 몸을 넘겨 주는 동작은 모두 기각했다(2026-10-17~26: 미끄러져 올라가는 보정, 한 번 누르면 한 칸 오르기,
  팔꿈치를 접고 매달리는 자세, 시소처럼 넘어가기, 배로 굴러 넘어간 뒤 엎드려 일어나기). 벽 점프의
  웅크림→폄→다시 굽힘 다리 동작은 사용자 승인 동작이라 유지한다.
- 시제품 확인: 0.55 m 턱은 바로 넘어 약 2.2초에 올라서고, 1.6 m 벽은 벽 점프를 서너 번 이어 간 뒤 꼭대기를 넘어 약 4.8초에
  올라선다. 직접 해 보는 장면은 `Assets/ProjectHotfix/Scenes/ClimbTest_LowWall`·`ClimbTest_HighWall`이다.
  점프 속도·넘기 판정 거리·입력 시간 창은 Alpha tuning 값이다.

### 2026-10-24 r46 누운 상대 때리기 결정

- 기절해 누워 있는 상대는 따로 입력 없이 평소 펀치로 때릴 수 있다. 앞쪽 약 0.8 m 안에 누운 몸이 있으면
  펀치를 누르는 순간 허리를 숙이고(시제품 상체 약 45°, 펀치 뻗기 전 약 0.1초 안에) 팔을 아래로 내려
  가장 가까운 몸통(없으면 가장 가까운 부위)을 친다. 맞으면 서 있는 상대를 칠 때처럼 히트스톱·이펙트·소리가
  나고(번쩍임은 약하게, 화면 흔들림 없음) 맞은 부위가 크게 들썩이고 멀수록 약하게 따라 튀어 오르며(옆으로는
  밀지 않는다. 튀어 오른 몸이 옆으로 미끄러져 한두 대 만에 손이 닿지 않았다), 손발과 머리가 짧게 움찔(약
  0.1초 몸 쪽 위로 움츠렸다가 약 0.2초에 걸쳐 다시 풀어짐)한다. 움찔은 관절 힘이 아니라 바깥 힘이라
  기절한 몸이 완전히 풀어져 있다는 2026-10-17 결정은 그대로다. 위에서 내리칠 때는 팔뚝 아래쪽이 닿아도
  맞은 것으로 본다. 처음에는 일어나는 중인 몸처럼 절반 힘에 작은 이펙트·소리만 주었으나 때리는 맛이 없어
  바꿨다(2026-10-24 확인). 점수·Down 누적·콤보·다시 쓰러짐은 없다(일어나는 중인 몸을 칠 때와
  같은 규칙, 2026-10-21 결정). 일어나기 시작한 몸에는 숙이지 않고 서 있는 높이로 친다. 시제품
  확인(가슴 옆·머리 쪽·엉덩이 옆에서 좌우 4번씩, 12번): 8번 맞고(맞을 때마다 누운 자세가 바뀌어
  실행마다 다르다) 하나도 세지 않았다. 숙여서 치는 동안 다리가 누운 몸에 닿으면 미는 사람을 떼어 낼 때처럼
  튕겨 내지 않고 몸 쪽으로 가는 움직임만 멈춘다(시제품 확인: 펀치마다 다리가 누운 몸 종아리에 박혔다 튕겨
  나가 두 몸이 떨렸다. 공격자 흔들림 약 40% 감소). 숙이는 깊이·범위는 Alpha tuning 값이다.

### 2026-10-22 r46 던지기 거리·쓰러진 몸 충돌 모양·부위별 잡기 결정

- 잡은 대상 던지기는 링아웃을 맵 가장자리 근처에서 마무리하는 동작이다. 들고 가는 동안 빠져나가거나 끼어드는 과정이 남도록,
  평지에서 멀리 날려 보내지 않는다. 시제품 기본값은 던질 때 더하는 속도 3 m/s(기존 4.5 m/s)이고, 몸통을 잡아 머리 위로 든 채
  제자리에서 던지면 놓은 지점에서 약 1.8 m(기존 약 2.6 m) 날아간다. 던지는 사람이 움직이고 있으면 그 속도가 더해진다. 맵이
  나온 뒤 다시 정하는 Alpha tuning 값이다.
- 쓰러진 몸(기절·기상 중)의 충돌 모양은 보이는 몸(피부)에 맞춘다. 주먹·발·몸이 쓰러진 몸의 피부 안으로 파고들어 보이면 안
  된다. 몸통은 앞뒤 두께를 그대로 두고 옆 폭만 피부에 맞추며(누운 높이는 변하지 않는다), 골반·머리 정수리·허벅지·종아리도
  피부에 맞춘다. 시제품 기준 피부와 충돌 모양의 틈은 상위 10%가 4.9 cm에서 2.6 cm로, 쓰러진 몸을 친 주먹이 파고드는 깊이는
  약 5 cm에서 2 cm 이하로 줄었다. 일어나는 애니메이션이 재생되는 짧은 구간(약 0.6초)은 보이는 몸과 충돌 몸이 달라 주먹이 아직
  파고들 수 있다(후속 과제).
- 쓰러진 몸은 어느 부위(몸통·머리·팔·다리)를 잡아도 들어 올려 던질 수 있어야 한다. 잡히는 시점과 던진 거리는 잡은 부위에 따라
  다르다(시제품: 몸통 약 1.8 m, 다리 약 1.6 m, 머리 약 0.9 m, 한 손으로 팔 약 0.6 m). 들고 있는 동안 기절 시간이 끝나면 지금처럼
  버둥거려 빠져나간다. 손을 놓은 직후 0.4초 동안은 던진 사람을 쓰러진 몸에서 떼어 내지 않는다(던진 몸이 아직 머리 위에 있다).
- 서 있는 캐릭터가 기절해 누운 몸을 걷거나 뛰어 밀 때 두 몸 사이에 보이지 않는 벽이 있으면 안 된다. 피부끼리 닿아 보이고
  (보이는 틈 약 1 cm 이하), 실제로 닿은 부위만 밀며, 어느 방향에서 밀든 어떤 자세로 누워 있든 같은 느낌이어야 한다(2026-10-21
  결정의 "아주 조금씩 밀린다"를 대체). 기존 시제품은 충돌 모양 바깥 5 cm에서 서 있는 사람을 되밀고 그때마다 밀려남 상태(걷는
  힘 30%)로 만들어, 6~8 cm 허공에서 멈추고 몸은 거의 밀리지 않았다. 시제품은 두 캐릭터의 피부를 직접 비교해 떼어 놓고(밀려남
  상태 없음), 피부보다 약 5 cm 큰 서 있는 몸통 원통 대신 다리 피부 안쪽의 작은 상자가 누운 몸과 부딪친다. 시제품 확인(누운
  자세 5종 × 방향 8개 × 걷기·달리기): 누운 몸이 밀린 거리는 걷기 11~42 cm·달리기 11~40 cm(약 2초 밀기, 기존 4~52 cm), 밀면서
  2 cm 넘는 틈이 오래 남은 경우는 걷기 40가지 중 7(기존 26)·달리기 3(기존 23)이다. 1 cm 넘게 파고드는 경우가 걷기 2가지(처음
  닿는 순간, 최대 1.3 cm)·달리기 8가지(최대 3 cm: 엎드린 등을 누르는 손, 누운 팔 위로 지나는 엉덩이) 남아 있다(후속 과제).
  미는 사람의 자세도 같은 기준이다. 누운 몸에 막혀도 미는 사람의 상체가 뒤로 접히면 안 되고 몸 쪽으로 살짝 기대어
  보여야 한다(2026-10-23 확인: 머리가 골반보다 약 7 cm 뒤로 처져 접혀 보였다). 시제품은 떼어 놓는
  힘을 상체가 아니라 엉덩이(루트)에만 주되 몸 전체 운동량의 75%로 계산하고, 누운 몸 쪽으로 미는 걷는 힘을
  8 m/s²로 줄인다. 확인(80가지): 머리가 골반보다 평균 약 2 cm 앞(기존 약 7 cm 뒤), 1 cm 넘게
  파고드는 경우 걷기 1가지(최대 1.8 cm)·달리기 12가지(최대 2.7 cm), 누운 몸이 밀린 거리 걷기
  12~44 cm·달리기 7~47 cm. 엉덩이 몫만큼만 떼어 놓으면 상체 무게에 끌려 다리가 파고들었고(달리기
  20가지 중 17), 몸 전체만큼 떼어 놓으면 미는 사람이 튕겨 나갔다. 일어나는 동안에는 지금처럼 서 있는 사람을
  비켜내며 15 cm 간격을 둔다. 미는 세기는 Alpha tuning 값이다.

### 2026-10-21 r46 바로 누움 기상·기상 공간·기상 보호 결정

- 바로 누움 GetUp은 두 손을 엉덩이 옆 바닥에 짚고(팔꿈치는 뒤) 상체를 일으킨 뒤, 손으로 바닥을 밀고 팔을 몸 옆으로
  내리며 발 위로 체중을 실어 일어선다. 엎드림 GetUp은 바꾸지 않는다. 일어선 직후 상대 쪽으로 몸을 돌리지 않고, 플레이어가
  이동·손 입력을 할 때부터 다시 상대를 바라본다.
- 일어날 공간(서는 자리·두 손 짚을 자리·몸통과 팔이 올라가는 길)에 무언가 있으면 짧게 기다리며 공간을 만든다. 서 있는
  캐릭터는 살짝 밀어내고(점수·Down·경직 없음), 움직이는 물체는 밀어내며, 고정 물체 옆에서는 누운 몸이 스스로 비켜난다.
  손은 빈 바닥에만 짚는다. 끝내 공간이 없으면 손을 짚지 않고 팔을 몸에 붙인 채 일어난다. 밀어내는 거리·대기 시간은 맵이
  나온 뒤 다시 정하는 Alpha tuning 값이다(현재 시제품 약 0.3 m·최대 1.2초). 대기 시간이 끝나도 서는 자리를 막는 것이
  캐릭터뿐이면 계속 밀어내며 그대로 일어난다(붙어 서서 계속 못 일어나게 막을 수 없다). 고정 물체가 막고 있을 때만 다시 눕는다.
  서 있는 캐릭터는 툭 치지 않고 공간을 침범한 깊이만큼 매 순간 살짝 비켜나게 한다(초당 0.3~0.8 m, 총 이동 약 0.3 m).
  공간 대기 중에 누운 몸은 누운 자세 그대로 두고(다리를 펴지 않는다), 기절 시간이 끝나는 즉시 비켜내기를 시작한다.
- 기절해 누워 있는 동안 서 있는 캐릭터가 걷거나 뛰어 몸에 닿은 채 밀고 들어오면 누운 몸이 아주 조금씩 밀린다(걸어서 1.5초
  밀면 약 0.1~0.15 m). 서 있는 캐릭터와 쓰러진 몸은 서로 관통하지 않는다(피부 기준 2 cm 이상 겹치지 않음). 잡으려고 뻗는 손과
  잡고 있는 손은 예외로 몸에 닿을 수 있다.
- 기상 동작(굴러 정렬·공간 대기·일어서기)이 시작되면 끝날 때까지 펀치·Dropkick은 맞지만 아무것도 쌓이지 않는다(점수·Down
  누적·콤보·다시 쓰러짐 없음). 맞으면 평소의 절반 정도만 밀리고, 맞은 표시는 작은 이펙트와 작은 소리뿐이다(히트스톱·번쩍임·
  화면 흔들림 없음). 잡기는 통하지 않고, 누가 끼어들어도 기상이 취소되지 않는다. 누워 대기하는 동안은 지금처럼 맞고 잡힌다.
  2026-10-20 결정의 GetUp 직후 보호 시간은 유지하되 기본값을 0.3초로 줄인다. 같은 날 처음엔 완전 무적(맞지 않음)으로
  정했다가, 맞는 반응은 보이고 카운트만 하지 않는 쪽으로 바꿨다. 낭떠러지가 있는 맵에서 기상 중 밀림으로 떨어뜨릴 수
  있는지는 맵이 나온 뒤 다시 본다.

### 2026-10-20 r46 달리기·날아차기·기절 회복 결정

- Walk와 Sprint는 다르게 읽혀야 한다. Walk는 몸통을 세우고 편 팔을 작게 앞뒤로 흔든다. Sprint는 상체를 앞으로
  기울이고 굽힌 팔을 몸 뒤까지 크게 흔들며 무릎을 높이 든다. 상대 가까이에서 걸을 때는 팔 흔들기를 줄여 펀치 시작 자세를 유지한다.
- Sprint 중(Sprint 입력+이동 속도 기준 이상+지면 지지)에는 손 입력이 Punch·Grab을 만들지 않는다. 좌/우 손 버튼의 새 누름은
  Dropkick 하나를 만들고, 누른 손 쪽을 바라보도록 몸을 옆으로 돌려 발부터 날아간다. 기존 Airborne Kick/Dropkick 입력 규칙은
  별도 결정 전까지 유지한다.
- 다리가 실제로 닿은 Dropkick 명중은 서 있는 대상을 즉시 Down시키고 피격 누적을 소비한다. 이 Down은 일반 DownEpisode로
  DownCount·groggy를 늘린다. 차는 쪽의 착지는 DropkickRecovery이며 Count·대기는 0이다.
- GetUp은 발을 놓인 자리에 고정한 채 그 위로 일어선다. 엎드림은 팔로 밀고 일어서며, 바로 누움은 상체를 먼저 일으킨다.
  옆으로 누웠거나 애매한 자세로 잠시 멈추면 가까운 앞/뒤 자세로 굴러 정렬한 뒤 일어난다.
- 2026-09-08 "잡힌 채 회복" 결정을 대체한다. 잡혀 있는 동안(들림·바닥 모두) Down 대기가 끝나면 GetUp 대신 짧게 버둥거린 뒤
  잡은 손을 끊고 빠져나온다. 이 해제는 Throw가 아니며 groggy timer를 늘리지 않는다. 버둥거리는 동안 잡은 쪽은 던질 수 있고,
  빠져나온 대상은 일어설 때까지 다시 잡을 수 없다.
- Down 뒤 GetUp을 마친 직후 짧은 보호 시간 동안은 어떤 공격으로도 새 Down이 시작되지 않고 물리 반응만 받는다. 보호 중
  직접 Punch·Grab·Dropkick을 시작하면 보호는 즉시 끝난다. 버둥거림·보호 시간은 Alpha tuning 값이다(현재 시제품 1초·0.3초, 2026-10-21에 0.6초에서 변경).

### 2026-09-05 공통 Grip·전 주기 변형·다방향 회복 결정

- 총기 Grip은 무기 종류와 좌우에 공통인 팔 뻗기 template 하나다. 동일 template를 실제 잡은 팔에
  적용하며 왼손·오른손 어느 쪽도 가능하다. 손 점유와 Main/Support 역할은 gameplay 정보이며
  Pistol/LongGun별 pose나 좌우별 별도 authored clip을 만드는 근거가 아니다. fire/recoil·reload clip0 유지.
- 리깅 QA는 걷기·뛰기 한 주기의 전 sample frame과 loop 경계에서 어깨·팔꿈치·골반·무릎의 꺾임,
  자기교차·면 뒤집힘·국소 압축/늘어남과 육안 자연스러움을 함께 확인한다. 한 정지 자세의 PASS로 대체하지 않는다.
- 회복 진입은 공격 방향이 아니라 Host가 확인한 실제 torso 자세와 지지면 normal을 사용한다.
  앞·뒤·좌·우 및 대각선·임의 yaw·경사면을 지원하고, 안정 지지나 clearance가 없으면 기다린다.
- 사용자가 권장안을 선택했다. 옆으로 누우면 현재 몸의 수평 방향을 보존하면서 가까운 앞/뒤 자세로
  정렬한 뒤 공통 face-up/face-down GetUp을 사용한다. 좌우 전용 GetUp clip은 추가하지 않는다.
  정렬과 GetUp은 순간이동·즉시 방향 반전 없이 연결하고 Host만 단계·완료를 판정한다.
- DropkickRecovery와 DownEpisode는 위 시각 경로를 공유하되 gameplay state·DownCount·groggy·Down Trigger는 분리한다.
  Upright Dropkick settle은 일어나기 없이 locomotion 복귀가 가능하며 떠 있거나 head-down이면 먼저 물리 안정화를 기다린다.


### 2026-09-05 외형·내부 리그 적용 범위

사용자가 기존 Neck 제한은 외형에 관한 것임을 명확히 했다. 보이는 목과 별도 목 외형은 0이며,
머리를 지지하는 내부 `Chest → Neck → Head` Bone은 허용한다. r20의 내부 Neck은 이 외형 계약의 위반이 아니다.
9월 4일 Idle/Walk/Sprint·Firearm clip0·Dropkick/shared GetUp 결정은 유지한다.
이번 문서 동기화·후속 작업 지시는 리그/포즈 결과의 시각 승인이나 UG-C1B·Player Build 승인을 대신하지 않는다.

### 0.1 2026-09-04 Action Presentation 결정

- Idle은 양팔을 몸통에 붙이는 pose, Walk는 작은 앞뒤 팔 swing+보행 leg motion, Sprint는 큰 앞뒤 팔
  swing+빠른 leg cadence를 사용하며 모두 in-place여야 한다.
- Firearm은 기존 Pistol Main hand와 LongGun Main+Support hand 관계를 유지하되 해당 팔을 앞으로 뻗는
  단순 공통 Grip pose를 사용하고, 별도 authored fire/recoil animation clip은 만들지 않는다.
- Host의 bounded Rigidbody/Joint recoil이 Grip pose의 팔과 Weapon을 위로 움직이고 승인된 spring/damping으로
  복귀시킨다. Animation frame은 Shot·Projectile·Hit·recoil authority를 갖지 않는다.
- `DropkickRecovery`와 `DownEpisode`는 별도 gameplay state다. 두 경로는 physics settle 뒤 방향별
  face-up/face-down GetUp presentation을 공유할 수 있지만 Dropkick 경로의 DownCount·groggy·Down Trigger는 0이다.

## 1. 제품 개요

Project Hotfix는 친구 2~4명이 함께하는 온라인 물리 난투 Party Game이다. 방을 만든 플레이어의
Client가 권한 simulation을 실행하고, 나머지 플레이어는 그 Host Client에 직접 연결한다.
별도 Dedicated Game Server와 Backend 서비스는 없다.

Host는 이동 검증, 물리, 잡기, 전투, Knockback, Down 상태, 탈락, 점수, Round, Patch,
Map Hazard, 공용 Camera 목표와 Scene 전환을 소유한다. Guest Client는 제한된 Input만 제출하고
Host 결과를 표시한다. Host 자신의 local Input도 Guest와 같은 gameplay validator를 통과한다.

Alpha는 통제된 LAN/direct 연결로 게임을 검증한다. Steam은 Alpha 이후 G4에서 통합한다.
Steam 인증, 비공개 친구 Lobby, 친구 초대, 사람이 읽을 수 있는 방 코드와
Steam Networking Sockets P2P/SDR은 모두 같은 Host Client로 연결된다.
G5에서는 안전한 data-only Workshop Map을 추가할 수 있다.
Public Matchmaking, Rank, MMR과 공개 방 검색은 제품 전 단계에서 영구 제외한다.

승인 초기 Catalog는 `PATCH-PROT-001..012`다. 무기는 라운드당 한 개가 아니라 `Playing` 동안 인원별
반복 Supply Pulse로 공급하며 AuthorityHost가 capacity, 결정적 Weapon bag, Safe DropZone, Weapon Hit와
Patch Effect를 판정한다. Alpha Patch 화면은 계속 평문 기능 UI만 사용한다.

Ground L/R tap은 해당 손 Punch, Airborne non-Down single tap은 chord close 뒤 해당 발 Kick이며 두
button down edge의 승인 chord는 즉시 Dropkick 하나를 만든다. Hold Grab이 unmatched pending Kick보다
우선하고 episode당 `AirAttackToken`은 하나다. Host가 action·hit·physics를 판정하며 Animation은
gameplay root motion 없이 read-only로 표현한다.

Alpha Match Player UI는 persistent HUD 없이 transient countdown·between-round Patch·연결/오류 문구와
on-demand Tab만 사용한다. Match Esc menu는 local-only이고 simulation을 멈추지 않는다. 명시적 Guest
Leave와 disconnect timeout은 Forfeit이며 Host Leave·Loss는 Session 종료다. Alpha 품질 범위는 Greybox
Lobby, 최소 placeholder Cosmetic, 한국어 단일 언어와 기본 SFX다.

## 2. 우선순위와 검증 표기

| 표기 | 의미 |
|---|---|
| P0 | Prototype 기반 |
| P1 | Alpha Vertical Slice |
| P2 | G4 Steam Demo |
| P3 | G5 1.0 |
| OUT | 영구 비범위 |
| I | Source·설정·산출물 검사 |
| T | 자동 또는 반복 가능한 Test |
| M | 수동 시연 또는 Playtest |
| A | 측정과 분석 |

## 3. Architecture와 범위 요구사항

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-SYS-001 | 하나의 Host Client는 정확히 하나의 권한 Session Simulation을 소유하고 Guest Client 1~3명을 지원해야 한다. | P1 | T/I | PRD 1.8.0 |
| SRS-SYS-002 | P2P는 전송 구조만 의미하며 Authority를 분산하거나 Peer 투표로 gameplay 결과를 결정해서는 안 된다. | P1 | T/I | PRD 1.8.0 |
| SRS-SYS-003 | Guest가 제출한 위치, 속도, 충돌, 잡기 성공, Kick·Dropkick action/hit, 피해, Down, 탈락, 점수와 Patch 결과를 권한 주장으로 수락해서는 안 된다. | P0 | T/I | PRD 1.8.0 |
| SRS-SYS-004 | Host local Input도 Guest Input과 동일한 Action·범위·상태 전이 검증을 사용해야 한다. | P1 | T/I | PRD 1.8.0 |
| SRS-SYS-005 | Public Matchmaking, Rank, MMR, 공개 방 검색과 공개 Server Discovery는 어떤 제품 단계에서도 구현하지 않아야 한다. | OUT | I | 사용자 결정 2026-08-24 |
| SRS-SYS-006 | 별도 Backend, Coordinator, Database, Blob Service, Bake Worker, Allocation Service와 Dedicated Game Server의 Runtime Instance와 필수 배포 산출물은 각각 0이어야 한다. | P1 | I/T | 사용자 결정 2026-08-24 |
| SRS-SYS-007 | 개발·Test·Release Workflow의 Docker, OCI, Compose, Container Image와 Container 배포 Step은 각각 0이어야 한다. | P1 | I | 사용자 결정 2026-08-24 |
| SRS-SYS-008 | Alpha Network는 통제된 LAN/direct endpoint Test로 제한하고 Internet NAT Traversal 제품 경로라고 표시해서는 안 된다. | P1 | I/M | 사용자 결정 2026-08-24 |
| SRS-SYS-009 | G4는 Steam 인증, 비공개 친구 Lobby와 Steam Networking Sockets P2P/SDR을 출시 Multiplayer 경로로 사용해야 한다. | P2 | T/M | 사용자 결정 2026-08-24 |
| SRS-SYS-010 | Game Protocol과 Replication Layer는 Transport Adapter 뒤에 두어 Alpha direct Transport와 G4 Steam Transport가 같은 Simulation 계약을 사용해야 한다. | P1 | I/T | PRD 1.8.0 |
| SRS-SYS-011 | Simulation, Presentation, Input, Transport, Steam Integration, Appearance와 Content Validation을 독립 Module 경계로 분리해야 한다. | P1 | I | PRD 1.8.0 |
| SRS-SYS-012 | Simulation은 Rendering과 UI 없이 실행할 수 있어야 하며 Multiplayer·Impairment Test도 제품과 같은 Gameplay Code를 사용해야 한다. | P1 | T/I | PRD 1.8.0 |
| SRS-SYS-013 | 하나의 Session은 하나의 Host Identity, 하나의 Active Gameplay World와 하나의 Active Content Scene만 가져야 한다. | P1 | T/I | PRD 1.8.0 |
| SRS-SYS-014 | Host 결과와 local 진단을 Anti-cheat 증명 또는 신뢰 가능한 경쟁 기록으로 설명해서는 안 된다. | P1 | I | PRD 1.8.0 |
| SRS-SYS-015 | G5 Workshop을 구현할 경우 Data-only로 제한하고 Script, DLL, 외부 Bundle과 실행 코드를 추가해서는 안 된다. | P3 | T/I | PRD 1.8.0 |

## 4. Session, Lobby와 시작 요구사항

Session 수명주기는 Hosting → InteractiveLobby → PreparingMatch → InMatch → MatchResult →
InteractiveLobby 또는 Closed다.

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-LOBBY-001 | Host는 혼자 InteractiveLobby를 만들 수 있지만 Match 시작에는 총 2~4명이 필요하다. | P1 | T/M | PRD 1.8.0 |
| SRS-LOBBY-002 | Ready는 Guest별 명시 상태이며 승인된 E Key 또는 Lobby UI Action으로 바꿔야 한다. Guest Ready Button은 Ready 확정 시 ReadyTeal로 바뀌고 check icon·준비 완료/준비 취소 문구를 함께 표시해야 한다. Host에는 Ready 상태와 Ready Control이 없어야 한다. | P1 | T/I/M | 사용자 결정 2026-08-25 |
| SRS-UI-001 | Host Start Button은 InteractiveLobby의 같은 Action Slot에 항상 보여야 한다. | P1 | M | 사용자 결정 2026-08-24 |
| SRS-LOBBY-003 | Host Start Button은 InteractiveLobby이고 총원이 2~4명이며 모든 Guest가 Connected·Ready이고 모든 Player Appearance가 Finalized일 때만 활성화해야 한다. | P1 | T/M | 사용자 결정 2026-08-24 |
| SRS-LOBBY-004 | Host 혼자, Ready하지 않은 Guest, Disconnected Guest, 처리 중 Appearance 또는 InteractiveLobby 밖의 Start 요청은 Match를 0회 시작해야 한다. | P1 | T | 사용자 결정 2026-08-24 |
| SRS-UI-002 | Start Button과 Action Slot의 위치는 활성 상태와 차단 이유가 바뀌어도 이동하지 않아야 한다. | P1 | M | 사용자 결정 2026-08-24 |
| SRS-UI-003 | 비활성 Start Button은 인원 부족, Disconnected Guest, NotReady Guest 또는 미완료 Appearance 중 가장 유용한 차단 이유를 보여야 한다. | P1 | T/M | PRD 1.8.0 |
| SRS-LOBBY-005 | 물리 Lobby StartLever는 없어야 하며 Match 시작을 위해 Object를 잡기·당기기·타격·충돌할 필요가 없어야 한다. | P1 | I/T/M | 사용자 결정 2026-08-24 |
| SRS-LOBBY-006 | Start 요청은 해당 Launch의 Roster와 Finalized Appearance를 고정해야 하며 관련 값이 바뀌면 준비를 취소하고 InteractiveLobby로 돌아가야 한다. | P1 | T | PRD 1.8.0 |
| SRS-LOBBY-007 | Match 활성화 전에 Build, Content, Map과 Appearance 호환성을 검증해야 한다. | P1 | T | PRD 1.8.0 |
| SRS-LOBBY-008 | MatchResult 뒤 Rematch는 같은 InteractiveLobby로 돌아가 모든 Guest를 NotReady로 만들고 Host Start Gate를 다시 요구해야 한다. | P1 | T/M | PRD 1.8.0 |
| SRS-LOBBY-009 | 문서화된 Lobby 안전 예외를 제외하면 Lobby는 Match와 같은 이동·손·물리 반응·Ragdoll Simulation을 사용해야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-LOBBY-010 | Lobby 추락은 탈락·PatchAuthor·점수를 만들지 않고 1~2초 안에 재투입해야 한다. | P1 | T/M | PRD 1.8.0 |

## 5. Match와 Round 요구사항

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-GAME-001 | Match는 정확히 2명, 3명 또는 4명의 고유 Player를 지원해야 한다. | P0 | T | PRD 1.8.0 |
| SRS-GAME-002 | 일반 Round 시작 시간은 설정 가능한 60초여야 한다. | P0 | T/I | PRD 1.8.0 |
| SRS-GAME-003 | 단독 생존자는 1점을 받고 먼저 4점에 도달한 Player가 Match에서 승리해야 한다. | P0 | T | PRD 1.8.0 |
| SRS-GAME-004 | 일반 시간이 끝났을 때 둘 이상이 남아 있으면 단계적인 Sudden Death로 전환해야 한다. | P1 | T/M | PRD 1.8.0 |
| SRS-GAME-005 | 모든 생존자가 같은 Authority Tick에 탈락하면 마지막 동률 집합만 축소된 Tie-break를 반복해 정확히 한 명의 Winner를 정해야 한다. | P1 | T | PRD 1.8.0 |
| SRS-GAME-006 | 탈락은 권한 OutOfBounds 또는 검증된 LethalHazard 경로로만 발생하며 Victim·Authority Tick당 최대 1건이어야 한다. | P0 | T | PRD 1.8.0 |
| SRS-GAME-007 | Round Result는 Winner, 탈락 순서와 확인 가능한 Player 또는 환경 Source를 포함해야 한다. | P1 | T/I | PRD 1.8.0 |
| SRS-ROUND-001 | Round 시작은 Character Transform·Velocity·Motion, AirAttackToken·pending single/chord·DropkickRecovery, Active Hand Intent·Grab, Incoming·Loose·Held·Spent Weapon, Ammo·Fire cadence·Projectile·Recoil/Spread, Supply bag·timer·pending wave, Hazard와 Round 범위 Delayed Effect를 초기화해야 한다. | P0 | T | PRD 1.8.0 |
| SRS-ROUND-002 | Match Identity, 선택 Map, Score, Seed와 Active Patch History는 Round Reset에서 유지해야 한다. | P0 | T | PRD 1.8.0 |
| SRS-ROUND-003 | Round 시작은 Round Generation을 증가시키고 이전 Round의 지연 Input과 Event를 거부해야 한다. | P0 | T | PRD 1.8.0 |
| SRS-ROUND-004 | 각 Round는 별도 3초 Countdown으로 시작하고 Lobby Match 준비와 다른 상태·문구·Telemetry를 사용해야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-ROUND-005 | Countdown의 Input Resume 시점 전 Gameplay Input은 Neutral이어야 하며 나중 적용하도록 Queue해서는 안 된다. | P0 | T | PRD 1.8.0 |
| SRS-ROUND-006 | Packet Loss 또는 Reconnect 복구 뒤에도 Host와 모든 Guest의 Score·Elimination·Match 진행이 일치해야 한다. | P1 | T | PRD 1.8.0 |

## 6. Input, 이동, 손과 Down 상태 요구사항

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-INPUT-001 | Core Input은 WASD 이동, Space Jump, Left Shift Sprint와 Ground/Air context를 공유하는 독립 LMB/RMB Action을 제공해야 한다. | P0 | T/M | 사용자 결정 2026-08-25 |
| SRS-PHYS-001 | 이동은 공용 Gameplay Camera 축을 기준으로 하고 별도 Facing Command 없이 유효 이동 방향으로 Character를 자동 회전해야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-INPUT-002 | Left Shift를 누르고 있는 동안 유효한 이동은 Lobby와 Match 모두에서 Sprint를 활성화해야 한다. | P1 | T/M | 사용자 결정 2026-08-24 |
| SRS-INPUT-003 | Sprint에는 Stamina Meter, 소모, Recharge와 Exhaustion 상태가 없어야 한다. | P1 | T/I | 사용자 결정 2026-08-24 |
| SRS-PHYS-002 | Sprint 속도 또는 가속도는 Versioned Profile Multiplier를 사용하고 Authority, Collider 크기와 Gameplay Reach를 바꾸지 않아야 한다. | P1 | T/I/A | 사용자 결정 2026-08-24 |
| SRS-PHYS-003 | Grounded 새 Hand Press는 피해가 없는 Intent 상태로 시작하고 한 Press에서 짧은 Release의 해당 손 Punch 또는 Hold의 GrabSeek 중 하나만 만들어야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-PHYS-004 | 왼손과 오른손은 독립적으로 행동하고 서로 다른 유효 Target을 잡을 수 있어야 한다. | P0 | T | PRD 1.8.0 |
| SRS-PHYS-005 | Grab 생성·Release·Break는 Host Contact, Target 규칙과 제한된 Grip Profile로 판정해야 한다. 동적 대상의 클릭 유지 Grab 뒤 마지막 본인 손 release는 별도 Lift 조건 없이 bounded Throw를1회 적용하고, 다른 본인 손 유지·미획득·강제 취소에는 추가 Throw가 없어야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-PHYS-006 | Jump는 제한된 Input Buffer와 Coyote Time을 지원하고 Ledge Assist는 무제한 비행이나 매달리기가 되지 않아야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-INPUT-004 | Esc는 Lobby Cursor Mode를 열고 닫아야 한다. 열린 동안 Local 이동·Jump·Hand Command는 Neutral이지만 Host Simulation과 외부 충격은 계속되어야 한다. | P1 | T/M | 사용자 결정 2026-08-24 |
| SRS-INPUT-005 | Cursor Mode 진입은 Punch·Kick·Dropkick·Throw·추가 Impulse 없이 pending single/chord 또는 Active Local Hand Intent를 취소해야 한다. | P1 | T | 사용자 결정 2026-08-25 |
| SRS-INPUT-006 | Cursor Mode를 닫은 뒤 두 Mouse Button이 모두 Release된 것을 확인할 때까지 Hand Input을 Disarm하고 그 Release Sample을 Hand Action으로 해석하지 않아야 한다. | P1 | T/M | 사용자 결정 2026-08-24 |
| SRS-INPUT-007 | Tab 동작은 사용자 설정에서 Hold와 Toggle Mode를 제공해야 한다. | P1 | T/M | 사용자 결정 2026-08-24 |
| SRS-UI-004 | Tab Overlay는 현재 Score와 Active Patch만 표시하고 Map 선택, 숨은 진단과 무관한 Room Control을 포함하지 않아야 한다. | P1 | T/I/M | 사용자 결정 2026-08-24 |
| SRS-UI-005 | Alpha Match의 persistent timer·alive·ammo·killfeed·result panel은 각각 0이어야 한다. `SRS-UI-006`의 local Esc menu를 제외한 Player-facing gameplay UI는 transient 3·2·1 Countdown, Round 사이 평문 Patch 선택·결과, transient OpponentLeft·HostLoss·오류와 on-demand Tab Score·Active Patch로 제한해야 하며 Ammo·FireMode·Projectile·Supply 진단은 developer debug에만 표시해야 한다. | P1 | T/I/M | 사용자 결정 2026-08-26, PRD 1.8.0 |
| SRS-UI-006 | Match Esc menu는 local-only이고 Authority simulation·Round timer·Hazard·외부 physics를 멈추지 않아야 한다. 열린 동안 local gameplay Input은 Neutral이어야 하며 닫은 뒤 모든 Mouse Button Up을 확인할 때까지 Hand·Weapon Input을 Disarm하고 held/up sample을 새 Action으로 해석하지 않아야 한다. | P1 | T/M | 사용자 결정 2026-08-26, PRD 1.8.0 |
| SRS-PHYS-007 | Player별 Match Round 범위 DownCount는 새 권한 Down 또는 Ragdoll Episode 시작 시 1부터 정확히 한 번 증가하고 다음 Round 시작에 0으로 Reset되어야 한다. Lobby Ragdoll은 이 Count를 만들거나 증가시키지 않아야 한다. | P1 | T | 사용자 결정 2026-08-24 |
| SRS-PHYS-008 | 같은 Down Episode의 중복 Contact와 계속되는 Simulation은 DownCount를 다시 증가시키지 않아야 한다. | P1 | T | 사용자 결정 2026-08-24 |
| SRS-PHYS-009 | 첫 번째 Match Down과 모든 Lobby Ragdoll의 Groggy Duration은 BaseDuration이어야 한다. 같은 Match Round의 두 번째 Down부터 매번 Increment를 더하고 결과는 MaxDuration을 넘지 않아야 한다. | P1 | T/A | 사용자 결정 2026-08-24 |
| SRS-PHYS-010 | DownCount와 남은 Groggy 상태는 권한 상태로 Replicate하고 Reconnect에서 복원해야 한다. | P1 | T | 사용자 결정 2026-08-24 |
| SRS-PHYS-011 | Sprint Multiplier와 DownState BaseDuration·Increment·MaxDuration의 정확한 수치는 2·3·4인 Playtest Evidence 뒤에 고정해야 한다. | P1 | A/M | 사용자 결정 2026-08-24 |
| SRS-INPUT-008 | Grounded에서 LMB/RMB를 GrabHoldThreshold 전에 quick release하면 각각 왼손/오른손 Punch 한 번을 만들고, threshold를 넘겨 hold하면 Strike 없이 해당 손 GrabSeek로 전환해야 한다. | P0 | T/M | 사용자 결정 2026-08-25, PRD 1.8.0 |
| SRS-INPUT-009 | Airborne non-Down에서 single quick release는 반대 down edge 없이 DualClickChordWindow가 닫힌 뒤 해당 좌/우 발 Kick으로 확정해야 한다. 반대 두 button down edge가 같은 window 안이면 두 pending을 즉시 소비해 Dropkick 한 번을 commit하고 quick release를 추가 요구해서는 안 된다. Hold threshold는 아직 commit되지 않은 single Kick pending만 취소해 해당 손/ledge GrabSeek로 전환하며 이미 commit된 Dropkick rollback은 0이어야 한다. | P0 | T/M | 사용자 결정 2026-08-25, PRD 1.8.0 |
| SRS-PHYS-012 | Player는 Airborne Episode당 Authority `AirAttackToken` 하나만 가져야 하며 Kick 또는 Dropkick commit이 이를 한 번 소비해야 한다. Token은 stable Grounded, GetUp 완료 또는 Round Reset에서 1로 복원되고 소비 뒤 quick tap/chord는 추가 Air attack을 만들지 않지만 hold Grab은 계속 허용해야 한다. | P0 | T | 사용자 결정 2026-08-25, PRD 1.8.0 |
| SRS-PHYS-013 | Dropkick은 bounded forward impulse, 감소된 air steering, Kick보다 강한 bounded knockback과 짧은 DropkickRecovery/physics tumble을 사용해야 한다. DropkickRecovery는 DownEpisode와 별도 state이고 Landing·종료·빗나감의 recovery/tumble은 DownCount·Groggy와 TRG-DOWN-EPISODE-START를 만들지 않아야 한다. 실제 prone settle 뒤에는 Down/Ragdoll 경로의 face-up/face-down GetUp presentation을 공유할 수 있지만 gameplay state는 합치지 않아야 한다. AirAttackToken이 먼저 복원돼도 Recovery 종료 전 새 Punch·Kick·Dropkick·Weapon Attack은 0이어야 한다. | P1 | T/A/M | 사용자 결정 2026-08-25·2026-09-04, PRD 1.8.0 Addendum |
| SRS-PHYS-014 | AuthorityHost만 Kick·Dropkick action, hit, impulse와 recovery를 확정해야 한다. 유효 Air hit는 좌우를 Action/Anchor context로 보존한 `TRG-ATTACK-HIT-CONFIRMED` SourceKind `Kick` 또는 `Dropkick`이어야 하고 Patch003/004를 적용할 수 있지만 TRG-WEAPON-HIT-CONFIRMED와 Patch011/012는 0이어야 한다. Animation·Presentation은 read-only이고 gameplay root motion은 0이어야 한다. | P0 | T/I/M | 사용자 결정 2026-08-25, PRD 1.8.0 |

| SRS-PHYS-015 | Host는 실제 torso rotation과 지지면 normal에서 front/back/left/right 회복 진입을 판별해야 한다. 임의 yaw·대각선·경사면을 지원하고 stable support·clearance 전에는 회복을 시작하지 않아야 한다. Side는 수평 방향을 보존한 연속 정렬 뒤 공통 face-up/down GetUp을 사용하며 좌우 전용 clip과 순간이동은 0이다. 정렬 중 지지/공간 상실은 재평가하고 Down/Dropkick의 episode 의미와 Count를 유지해야 한다. | P1 | T/A/M | 사용자 결정 2026-09-05 |

후속 사용자 결정 — 던진 뒤 일반 충돌: 추가 Damage와 피해 누적은0이다. 벽/바닥/일반 물체·캐릭터에 대한 반복 접촉에서도 동일하다.
물리 반응과 기존 명시적 Hazard/OOB 계약은 유지한다. 예: 벽 충돌 뒤 바닥에서 두 번 튕겨도 후속 접촉 Damage는 모두0이다.
2026-09-09 확정 정책: [빠른 펀치·부위별 누적 후보](../artifacts/reports/R20_FAST_PUNCH_PRECISION_20260909.md)는 몸통·팔다리1점, 머리2점으로 누적한다. 넉다운 기준은10→9→8→7→6점에서 멈추며 기존 groggy 대기 시간 증가도 유지한다. 누적은 시간이 지나도 감소하지 않고 새 Down에서 소비하며, Round reset은 누적과 DownCount를 초기화한다. Down 중 추가 타격은 물리 반응만 적용하고 점수·Count·timer를 늘리지 않는다. 머리 보너스는 실제 머리 접촉에만 적용한다. 기존 항상5회 고정은 이전 비교 정책이다.

2026-09-08 이전 사용자 결정: Down용 피격 누적은 시간 감소가 없고 새 Down에서 소비한다. 필요한 총 타격 수는 고정이며, DownCount는 groggy 대기 시간 증가에만 사용한다. Down 중 재피격은 물리 impulse만 허용하고 누적·새 episode·Count 증가·timer 연장은 0이다. 넉다운 대기 구간은 자세 drive/animation blend 0의 수동 ragdoll이며 유효 지지/공간과 timer 종료 뒤에만 GetUp을 시작한다. Round reset은 누적과 Count를 초기화한다. 구체적인 타격 수/시간과 공격별 피해표는 tuning이며 일반 충돌 자동 Down은 별도 미확정이다.
(2026-10-20 대체: 잡힌 대상은 timer 종료 뒤 버둥거린 다음 Grab을 끊고 빠져나온다. 상단 2026-10-20 결정 참조.) 잡힌 대상의 timer 종료·지지/clearance가 유효하면 회복을 허용하고, 상태 표현/충돌체 전환만으로 Grab을 해제하지 않는다. 접점은 연결된 물리 부위를 따르며 하중·점프 보정·Throw impulse 한도는 대상 전체 질량으로 계산한다. 같은 대상의 여러 부위를 잡아도 대상 단위 보정/Throw 중복은0이고, Reset/실제 break는 기존 강제 해제 정책을 따른다.

2026-09-17 사용자 결정 — Down 들기 보조(SRS-PHYS-005의 후속): 실제 유효 Grab과 지지된 보유자의 들기/유지 조건에서만 Down 대상에 부분 중력 보상 힘을 적용한다. 질량·관성·중력 사용 설정과 Down 자세 drive0은 보존하고, 동일 대상의 양손/복수 접점 보조는 중복하지 않는다. 마지막 손 release·강제 취소·대상/접점 상실·공중 전환은 다음 물리 계산 전에 보조0이어야 한다. 유효 마지막 release만 기존 bounded Throw1회를 만들며, 상호 잡기 자동 상승 차단과 held jump의 무게 패널티를 유지한다. 예: 양손으로 들다가 한 손만 놓으면 들기는 유지하고, 마지막 손을 놓는 tick에는 보조0·Throw1회다. 로컬 후보 검증과 정식 Host/Guest 통합 완료는 구분한다.

## 7. Weapon Combat 요구사항

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-WEAPON-001 | Weapon Combat은 Firearm, Melee Weapon, Hold, Drop, Damage, Knockback과 반복 Supply를 포함한 Alpha 범위여야 한다. | P1 | T/M | 사용자 결정 2026-08-25 |
| SRS-WEAPON-002 | W1은 Production Combat 구현 전에 Weapon Use·Fire·Melee·Drop Input을 고정하는 사용자 승인 Gate여야 한다. | P1 | I/M | 사용자 결정 2026-08-24 |
| SRS-WEAPON-003 | W1 승인 전에는 Grip Socket·Pose와 기술 가능성을 검증할 수 있지만 최종 Combat Binding을 임의로 선택해서는 안 된다. | P1 | I/M | 사용자 결정 2026-08-24 |
| SRS-WEAPON-004 | Firearm Test는 유효 Fire Input, Authority Ownership, Rate Limit, Hit 또는 Projectile 판정, Damage와 Knockback을 포함해야 한다. | P1 | T/M | 사용자 결정 2026-08-24 |
| SRS-WEAPON-005 | Melee Test는 Swing 또는 Contact 조건, 중복 Hit 방지, Damage, Knockback과 Down 상태 상호작용을 포함해야 한다. | P1 | T/M | 사용자 결정 2026-08-24 |
| SRS-WEAPON-006 | Drop Test는 명시적 Drop, 강제 Release, Patch11·12의 지정 Instance Forced Drop, Owner 해제, Transform Replication과 다른 Player의 재획득을 포함해야 한다. | P1 | T/M | 사용자 결정 2026-08-25 |
| SRS-WEAPON-007 | Weapon Appearance, Paint와 Cosmetic은 Weapon Authority, Damage, Mass와 Reach를 바꾸지 않아야 한다. | P1 | T/I | PRD 1.8.0 |
| SRS-WEAPON-008 | Pistol, Long Gun, Bat와 Hammer를 최소 Grip·Camera 판독 Benchmark로 사용해야 한다. | P1 | T/M | Character 기술 기준 |
| SRS-WEAPON-009 | 라운드당 무기 한 개만 공급하는 규칙은 없어야 한다. 각 Round Playing 시작 뒤 2인은 첫 10초·22초 간격·cap2, 3인은 8초·16초·cap2, 4인은 6초·12초·cap3의 반복 Supply `START` profile을 사용해야 한다. Host는 Round 초기 participating roster로 profile을 한 번 선택하고 Disconnect·Reconnect·중도 Forfeit로 현재 Round 값을 재계산하지 않으며 다음 Round에서만 새 roster를 반영해야 한다. | P1 | T/I/A/M | 사용자 결정 2026-08-25 |
| SRS-WEAPON-010 | Supply cap은 Incoming·Loose·Held·SpentPendingCleanup Instance를 모두 세어야 한다. capacity 부족 시 가능한 수량만 admission하고 `CapacityLimited`로 끝내며 cap 상향, backlog, catch-up과 즉시 재시도를 만들지 않아야 한다. OOB·Spent 제거 뒤에도 replacement는 다음 정규 Pulse까지 기다려야 한다. | P1 | T | 사용자 결정 2026-08-26 |
| SRS-WEAPON-011 | Host는 Round마다 MatchSeed·Round·WeaponCatalogVersion으로 Pistol·LongGun·Bat·Hammer가 한 번씩 든 결정적 shuffle bag을 만들고 검증된 Safe DropZone만 선택해야 한다. 실제 admission된 Spawn만 bag cursor를 소비하고 미생성 수량은 소비하지 않으며 bag 소진 시 결정적 next bag을 만들어야 한다. Admission과 landing에서 Character·Weapon·moving part `LandingClearance`를 검사하고 유효 Zone 0은 `NoSafeDropZone`, bounded `START 1~2초` landing 대기 뒤에도 막힌 Incoming은 `LandingBlocked`로 제거해야 하며 피해·backlog·즉시 재투하는 0이어야 한다. | P1 | T/I | 사용자 결정 2026-08-25 |
| SRS-WEAPON-012 | Supply와 derived wave는 Playing에서만 admission해야 한다. Incoming은 Character Damage·Down·Knockback·Grab·Pickup, 다른 Weapon 충돌, Patch Trigger와 Map control·Hazard interaction을 만들거나 Camera subject가 되지 않고 착지 뒤 Loose가 되어야 한다. 각 Map의 Host `WeaponCleanupBoundary`는 회수 불가능한 Loose Weapon을 제거하되 유효 Held owner의 부분 진입은 제거해서는 안 된다. SuddenDeath/Result는 pending supply를 취소하고 Incoming·Loose·Held는 reset까지 유지하되 Spent deadline은 SuddenDeath에도 진행·제거해야 한다. 제거 capacity는 next pulse만 사용하고 Round reset은 모든 Weapon state·schedule을 초기화해야 한다. | P1 | T/M | 사용자 결정 2026-08-26 |
| SRS-WEAPON-013 | W1은 승인된 Airborne L/R Kick, dual-click Dropkick와 airborne hold Hand/ledge Grab mapping을 덮어써서는 안 된다. W1 승인 전 Airborne tap/chord는 Kick/Dropkick이 우선하고 WeaponUse로 해석하지 않아야 하며, Airborne WeaponUse 허용 여부와 별도 action·mode 입력은 W1에서 명시적으로 결정해야 한다. Grounded Weapon input은 이 Air mapping을 훼손하지 않는 범위에서 W1이 결정한다. | P1 | I/T/M | 사용자 결정 2026-08-25, PRD 1.8.0 |
| SRS-WEAPON-014 | Pistol은 Host가 수락한 WeaponUse press edge당 semi-auto 한 발과 Spawn total Ammo 7을, LongGun은 valid WeaponUse hold·cadence의 full-auto와 total Ammo 30을 사용해야 한다. 모든 Spawn은 full Ammo로 시작하며 ReserveAmmo, ReloadCommand, Reloading, magazine 교체와 ammo pickup은 각각 0이어야 한다. | P0 | T/I/M | 사용자 결정 2026-08-26, PRD 1.8.0 |
| SRS-WEAPON-015 | 마지막 유효 Shot 뒤 Ammo가 0이면 Host는 해당 Weapon의 Fire를 원자 중지하고 Grip/owner를 한 번 forced release해 `SpentPendingCleanup`으로 전환해야 한다. Spent는 `START 2~4초` cap에 포함되지만 Collider·pickup·Grab·fire·hit·map/Hazard interaction은 0이고 deadline/reset에서 제거해야 하며 replacement는 즉시가 아니라 다음 정규 Pulse만 사용해야 한다. | P0 | T/I | 사용자 결정 2026-08-26, PRD 1.8.0 |
| SRS-WEAPON-016 | Host는 owner·held·phase·cadence·Ammo를 검증해 Ammo-1과 visible Projectile Spawn을 원자 처리해야 한다. Projectile은 immutable attacker·SourceWeapon·ShotSequence·AttackAction을 보존하고 fixed-step swept SphereCast의 첫 Map blocker 또는 Character hit 하나에서 끝나며 gravity `START 0`, pierce·ricochet·다중 hit 0이고 TTL·OOB·RoundResult·reset에서 제거해야 한다. Character hit의 Patch03은 Action·Target당, Patch04는 Projectile Action당 최대 한 번이어야 한다. 지연 last-shot Hit 때 source가 Spent/owner-loss면 Patch12는 `NoEligibleTarget`이며 다른 Weapon을 대신 해제해서는 안 된다. Guest Ammo·Projectile·Hit 결과 주장은 0이어야 한다. | P0 | T/I/A | 사용자 결정 2026-08-26, PRD 1.8.0 |
| SRS-WEAPON-017 | Host는 Shot마다 bounded recoil impulse·torque와 Projectile direction을 확정해야 한다. Pistol은 narrow spread·strong single recoil, LongGun은 deterministic ShotSequence 기반 capped RecoilAccumulator·SpreadBloom과 release/gap recovery를 사용해야 한다. 총기 종류·좌우에 공통인 팔 뻗기 template 하나를 실제 Held arm에 적용하고 별도 authored fire/recoil animation clip은 0이어야 한다. Host Rigidbody/Joint recoil이 해당 팔과 Weapon을 위로 움직이고 bounded spring/damping으로 복귀시켜야 하며 projectile·recoil의 Lever·Crane·Hook·Panel·Hazard phase·prop remote activation/impulse는 0이어야 한다. Fire·Projectile은 Playing과 SuddenDeath에서 유효하고 RoundResult부터 새 Fire·active Projectile은 0이어야 한다. | P1 | T/I/A/M | 사용자 결정 2026-08-26·2026-09-04, PRD 1.8.0 Addendum |

## 8. Patch 요구사항

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-PATCH-001 | Match가 끝나지 않은 Round 뒤 지정 PatchAuthor는 `PATCH_DESIGN.md`에서 승인한 Patch12의 Trigger와 호환 Effect를 순서대로 선택해야 한다. 플레이어 노출 명칭은 `패치`여야 한다. | P0 | T/M | PRD 1.8.0, PATCH_DESIGN 0.5.0 |
| SRS-PATCH-002 | 2인에서는 Round Loser, 3~4인에서는 최초 gameplay 탈락자가 PatchAuthor가 되며 동시 탈락은 결정적인 Tie-break를 사용해야 한다. 명시적 Leave와 disconnect timeout의 Forfeit 자체는 PatchAuthor가 되어서는 안 된다. | P0 | T | PRD 1.8.0 |
| SRS-PATCH-003 | AuthorityHost는 다음 Round의 FIFO 제거를 먼저 투영한 `projected active set`을 기준으로 유효 Trigger 평문 후보 2개와 선택 Trigger에 대한 Effect 평문 후보 2개를 순서대로 제시해야 한다. | P0 | T/M | PRD 1.8.0, PATCH_DESIGN 0.5.0 |
| SRS-PATCH-004 | Patch 선택은 총 7초 제한, 남은 시간과 확정 결과 평문을 제공해야 한다. Author가 완료하지 않으면 Host가 노출된 유효 후보 안에서 결정적인 자동 선택을 적용해야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-PATCH-005 | Candidate Filter는 projected active set과 승인 catalog의 compatibility·conflict tag를 사용해 비호환, 유지 집합의 중복, 숨은 No-op 또는 재귀적으로 위험한 Rule을 표시 전에 제외해야 한다. 승인 Patch12의 retained active set은 `TriggerOccupancy`를 사용해 같은 Trigger의 Patch Instance를 최대 하나만 허용해야 한다. | P0 | T/I | PRD 1.8.0, PATCH_DESIGN 0.5.0 |
| SRS-PATCH-006 | Root Patch Event Chain은 제한되어야 하고 같은 Rule이 같은 Entity와 원인 Episode에 무한 재진입해서는 안 된다. Patch가 만든 impulse·Forced Drop은 새 gameplay Input·Hit을 만들지 않고, Base Supply root만 Supply Patch를 발동하며 derived wave의 Supply 재발동은 0이어야 한다. | P1 | T | PRD 1.8.0, PATCH_DESIGN 0.5.0 |
| SRS-PATCH-007 | Match는 Active Patch를 최대 3개 유지하고 네 번째 활성화 전에 가장 오래된 Patch를 제거하는 FIFO를 사용해야 한다. Alpha 화면은 이 활성 목록을 평문으로 표시해야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-PATCH-008 | Active Patch는 다음 Round부터 모든 Player에게 하나의 결정적 순서로 적용하고 Reconnect와 Round Reset에서 유지해야 한다. Authority runtime은 선택·활성·발동·만료·Supply·Forced Drop semantic event와 read-only model을 제공하고 Presentation은 이를 소비할 뿐 gameplay state를 변경해서는 안 된다. | P0 | T/I | PRD 1.8.0, PATCH_DESIGN 0.5.0 |
| SRS-PATCH-009 | 승인 Patch12 Catalog는 지원하는 모든 projected active set에서 실제 유효 2×2 선택을 제공해야 하며 실패를 중복 Candidate나 숨은 No-op으로 처리해서는 안 된다. Patch09의 별도 Weapon Instance desired batch2는 capacity만큼 admission하고 Patch10의 derived second wave는 `START 6~10초` 뒤 당시 capacity로 한 개만 시도하며 둘 다 backlog·재시도 0이어야 한다. Patch11은 Host-confirmed Weapon hit victim의 모든 Held Weapon Instance를 Forced Drop하되 Main·Support가 같은 Instance면 한 번만 처리하고 대상이 없으면 `NoEligibleTarget`이어야 한다. Patch12는 같은 hit의 attacker source Weapon이 Effect 시점에도 같은 attacker에게 Held일 때만 Forced Drop하고 아니면 `NoEligibleTarget`이어야 한다. 두 Effect는 Damage·Ammo·cadence를 바꾸지 않아야 하며 최종 icon·animation·VFX·SFX·layout은 Alpha 기능 Gate가 아니어야 한다. | P1 | T/I/M | PRD 1.8.0, PATCH_DESIGN 0.5.0 |

## 9. Map, Hazard와 Camera 요구사항

(2026-10-28 대체: SRS-CAM-001~003과 SRS-PHYS-001의 공용 Camera·이동 방향 자동 회전은 플레이어별 3인칭 카메라와 마우스 시선으로 대체한다. 상단 2026-10-28 결정 참조.)

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-MAP-001 | 모든 공식 Map은 동일한 Gameplay Geometry, Bounds, Hazard와 Camera 설정으로 2·3·4인을 지원해야 한다. | P0 | T/I/M | PRD 1.8.0 |
| SRS-MAP-002 | Player Count는 Active Spawn·Player 배정과 승인된 Weapon supply `START` profile만 바꿀 수 있다. Gameplay Geometry, Platform 개폐, Barrier, Hazard Timing·Strength와 Camera rule은 2·3·4인에서 동일해야 한다. | P0 | T/I | PRD 1.8.0 |
| SRS-MAP-003 | 모든 공식 Map은 승인 Character Physics Bounds를 사용하는 안전한 2·3·4인 Spawn 배치와 Incoming Weapon을 OOB·Lethal·Player initial overlap 없이 Loose로 전환할 수 있는 Safe DropZone을 가져야 한다. DropZone pool은 현재 최대 admitted batch인 서로 다른 Weapon 2개를 겹치지 않게 동시에 수용할 distinct Arrival Slot을 최소 2개 제공해야 한다. | P0 | T | PRD 1.8.0 |
| SRS-MAP-004 | Spawn 배정은 Match Seed, Round와 Roster로 결정하고 Scene 발견 순서에 의존하지 않아야 한다. | P1 | T | PRD 1.8.0 |
| SRS-MAP-005 | 모든 공식 Map은 OutOfBounds, 직접 LethalHazard 하나 이상과 DisplacementHazard 하나 이상을 가져야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-MAP-006 | LethalHazard는 Host가 확인한 Phase와 Character Contact를 요구하고 판독 가능한 Telegraph와 회피 기회를 제공해야 한다. Player 조작 Map Control은 Generic Interact 성공 주장이 아니라 권한 Hand Contact 또는 물리 Force를 요구해야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-MAP-007 | Sudden Death는 단계적이고 판독 가능한 변화를 적용하며 Presentation-only Object가 권한 Damage를 만들지 않아야 한다. | P1 | T/M | PRD 1.8.0 |
| SRS-MAP-008 | 높은 Ledge는 실제 OutOfBounds 위에 제한된 RecoveryBand를 둘 수 있으며 실제 경계를 넘을 때까지 Alive를 유지해야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-MAP-009 | Match Map은 Start Gate 뒤 모든 Peer에서 유효한 Content 중 내부 선택하고 Lobby에 Map 선택 Control을 노출하지 않아야 한다. | P1 | T/I/M | PRD 1.8.0 |
| SRS-MAP-010 | 선택 Map은 Match 전체에서 유지해야 한다. | P1 | T | PRD 1.8.0 |
| SRS-CAM-001 | Host는 모든 Active Player와 Spectator가 공유하는 하나의 SharedGameplayCamera 목표 상태를 계산해야 한다. | P0 | T/I/M | PRD 1.8.0 |
| SRS-CAM-002 | Gameplay는 개인 Camera Rotation, 개인 Zoom, 자유 Spectator Camera와 Split Screen을 제공하지 않아야 한다. | P0 | T/I/M | PRD 1.8.0 |
| SRS-CAM-003 | 공용 Camera Framing은 권한 Player Bounds를 사용해 모든 Alive Player를 포함하고 제한된 Damping, Look-ahead와 Dolly를 사용해야 한다. | P0 | T/A/M | PRD 1.8.0 |
| SRS-CAM-004 | Eliminated Player는 Camera Snap 없이 Subject에서 제거하고 Alive 상태의 Disconnected Guest는 탈락 또는 Forfeit 전까지 포함해야 한다. | P0 | T/M | PRD 1.8.0 |
| SRS-CAM-005 | Camera Occlusion 처리와 Safe Framing은 16:9, 16:10, 21:9에서 Player와 Lethal Telegraph를 판독 가능하게 유지해야 한다. | P1 | T/M | PRD 1.8.0 |
| SRS-CAM-006 | Camera·Map 검증은 2인, 3인, 4인의 기능과 편안함을 각각 Test해야 한다. | P1 | T/A/M | 사용자 결정 2026-08-24 |

## 10. Network, Steam, Reconnect와 Security 요구사항

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-NET-001 | Host는 Unity 3D Physics를 60Hz fixed-step으로 실행하고 Authority/Network cadence는 정확히 두 Physics step마다 1회인 30Hz를 목표로 해야 한다. 제한된 Snapshot은 20Hz면 세 Physics step마다, 15Hz면 네 step마다 보내며 Physics와 Network cadence를 같은 값으로 가장해서는 안 된다. | P1 | T/A | 사용자 결정 2026-08-27, PRD 1.8.0 |
| SRS-NET-002 | Input, Snapshot, 신뢰성이 필요한 상태 전이와 Recovery Data는 Loss·Ordering 필요에 맞는 Transport Semantic을 사용해야 한다. | P1 | T/I | PRD 1.8.0 |
| SRS-NET-003 | Input은 단조 Sequence와 제한된 값을 사용하고 중복·과거·불가능·과도한 Input은 권한 State 변경 없이 거부해야 한다. | P1 | T | PRD 1.8.0 |
| SRS-NET-004 | Client는 Local Presentation을 Prediction할 수 있지만 크거나 불가능한 차이는 Host State로 수렴해야 한다. | P1 | T/M | PRD 1.8.0 |
| SRS-NET-005 | Session, Scene과 Round Generation은 이전 수명주기의 Packet을 거부해야 한다. | P1 | T | PRD 1.8.0 |
| SRS-NET-006 | Lobby→Match와 Match→Lobby 전환은 같은 Host Connection을 유지하고 필수 Peer가 호환 Content 준비를 보고한 뒤에만 새 Scene을 활성화해야 한다. | P1 | T | PRD 1.8.0 |
| SRS-NET-007 | Recovery State는 크기가 제한되고 Checksum 검증되며 Local Control 재개 전에 원자적으로 적용해야 한다. | P1 | T/I | PRD 1.8.0 |
| SRS-NET-008 | Disconnected Guest는 같은 Host Session·Player Slot에 최대 30초 동안 Reconnect할 수 있어야 한다. | P1 | T | PRD 1.8.0 |
| SRS-NET-009 | Reconnect는 Input 재개 전에 Score, Participation, Transform, Down, Hand/AirAction, 현재 Round Supply와 Incoming·Loose·Held·Spent Weapon, Ammo·FireMode/cadence·ShotSequence·active Projectile·RecoilAccumulator/SpreadBloom·Spent deadline, Patch, Hazard, Scene과 Appearance를 복원해야 한다. Reconnect로 action·Shot·Projectile·supply profile을 다시 생성·선택해서는 안 된다. | P1 | T | PRD 1.8.0 |
| SRS-NET-010 | Grace 만료 뒤 Reconnect는 안전하게 실패하고 Player를 Forfeit 처리하거나 Lobby Reservation을 제거해야 한다. | P1 | T | PRD 1.8.0 |
| SRS-NET-011 | Host Loss는 Session을 끝내고 Guest를 Main Menu로 돌려보내야 하며 Host Migration과 Peer Authority 승계는 0이어야 한다. | P1 | T/M | PRD 1.8.0 |
| SRS-NET-012 | Alpha Direct Transport는 Development 설정에서만 사용하고 Release Fallback으로 노출하지 않아야 한다. | P1 | I/T | 사용자 결정 2026-08-24 |
| SRS-NET-013 | 예상치 못한 Guest disconnect의 30초 grace 동안 Host는 해당 Player Input을 Neutral로 만들되 Character를 physical·vulnerable 상태와 Alive Camera subject로 유지해야 한다. Character는 충돌·피해·Down·탈락할 수 있고 reconnect는 Host의 현재 Alive 또는 Spectator 상태를 중복 Action 없이 원자 복원해야 한다. | P1 | T/M | 사용자 결정 2026-08-26, PRD 1.8.0 |
| SRS-NET-014 | Guest의 명시적 Leave는 reconnect grace를 건너뛰어 즉시 Forfeit하고 예상치 못한 disconnect timeout도 Forfeit해야 한다. Forfeit event는 PatchAuthor를 만들지 않아야 하며 permanent participant가 2명 이상이면 Match를 계속하고 1명이면 새 Score·Patch를 0건 생성한 채 OpponentLeft 뒤 같은 Lobby로 돌아가야 한다. | P1 | T/M | 사용자 결정 2026-08-26, PRD 1.8.0 |
| SRS-NET-015 | InteractiveLobby의 명시적 Guest Leave는 slot을 즉시 제거하고 예상치 못한 disconnect는 slot을 30초 예약한 뒤 timeout에 제거해야 한다. Host의 명시적 Leave 또는 Loss는 phase와 무관하게 Session을 종료하고 Guest를 MainMenu로 보내며 Host Migration은 0이어야 한다. | P1 | T/M | 사용자 결정 2026-08-26, PRD 1.8.0 |
| SRS-STEAM-001 | G4 Host는 비공개 친구 Steam Lobby를 만들고 같은 Session의 Steam P2P Listen 경로를 열어야 한다. | P2 | T/M | 사용자 결정 2026-08-24 |
| SRS-STEAM-002 | G4 Guest는 Steam 친구 초대 또는 방 코드 중 하나로 같은 Host Session에 참가해야 한다. | P2 | T/M | 사용자 결정 2026-08-24 |
| SRS-STEAM-003 | G4 방 코드는 SteamLobbyId의 Versioned·가역 표현과 오류 검출 Checksum을 포함하고 Application Server Lookup 없이 Local Decode되어야 한다. | P2 | T/I | 사용자 결정 2026-08-24 |
| SRS-STEAM-004 | 방 코드는 Password나 Identity Proof가 아닌 Locator로 취급하고 잘못된 Checksum은 Steam Join 시도 전에 거부해야 한다. | P2 | T/M | 사용자 결정 2026-08-24 |
| SRS-STEAM-005 | Steam은 Local User와 Remote Peer를 검증하고 Game은 Gameplay Input 허용 전에 의도한 비공개 Lobby Member인지 확인해야 한다. | P2 | T | PRD 1.8.0 |
| SRS-STEAM-006 | Steam Lobby Metadata를 Gameplay State, Score, Map Result 또는 Authority Transfer 근거로 신뢰해서는 안 된다. | P2 | T/I | PRD 1.8.0 |
| SRS-STEAM-007 | G4는 2·3·4인에서 Direct P2P, 강제 SDR Relay와 제한 NAT 상황을 Test해야 한다. | P2 | T/A/M | 사용자 결정 2026-08-24 |
| SRS-STEAM-008 | Release Network는 Steam P2P/SDR만 사용하고 Direct-IP, Alpha Transport와 Public Discovery Fallback을 노출하지 않아야 한다. | P2 | I/T | 사용자 결정 2026-08-24 |
| SRS-SEC-001 | Message, Collection과 Recovery Payload는 명시적 Size·Rate·Count 제한을 갖고 Malformed Traffic은 Host Crash가 아니라 해당 Peer 격리로 처리해야 한다. | P1 | T | PRD 1.8.0 |
| SRS-SEC-002 | Steam Ticket, Session Secret과 Raw Network Endpoint를 일반 Log와 사용자 Error에 기록하지 않아야 한다. | P2 | T/I | PRD 1.8.0 |
| SRS-SEC-003 | 유지보수되는 Platform 또는 Crypto Library를 사용하고 Custom Encryption Primitive와 Plaintext Fallback을 추가하지 않아야 한다. | P2 | I/T | Security 기준 |
| SRS-SEC-004 | Room Join, Reconnect와 Recovery 시도는 Rate Limit해야 하며 Replay가 중복 Player·Action·Control을 만들지 않아야 한다. | P1 | T | PRD 1.8.0 |
| SRS-SEC-005 | Host 변조는 Anti-cheat 보장 밖이지만 Guest는 Malformed State와 안전하지 않은 Content를 거부하고 임의 Data를 실행하지 않아야 한다. | P1 | T/I | PRD 1.8.0 |

## 11. Appearance, Preset과 Art Pipeline 요구사항

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-APPEAR-001 | Appearance Preset은 편집 가능한 제한 Source Data로 Local 저장하고 Atomic File Replace를 사용해 Client 재시작 뒤에도 유지해야 한다. | P1 | T/I | 사용자 결정 2026-08-24 |
| SRS-APPEAR-002 | Appearance Source는 승인 Color, 제한된 Stroke Command와 제한 Transform의 승인 Cosmetic 참조만 포함할 수 있다. | P1 | T/I | 사용자 결정 2026-08-24 |
| SRS-APPEAR-003 | 임의 File, 사용자 Texture·Image·URL·Clipboard Image·Mesh·Shader·Script와 실행 가능한 Appearance Content를 거부해야 한다. | P1 | T/I | 사용자 결정 2026-08-24 |
| SRS-APPEAR-004 | Host는 Appearance를 Finalized로 만들기 전에 Schema, Limit, Catalog Reference와 Content Hash를 검증해야 한다. | P1 | T | 사용자 결정 2026-08-24 |
| SRS-APPEAR-005 | Host는 검증된 제한 Appearance Source와 Finalization 상태를 기존 P2P Session으로 Relay해야 하며 Backend, Blob Store와 Bake Worker를 요구해서는 안 된다. | P1 | T/I | 사용자 결정 2026-08-24 |
| SRS-APPEAR-006 | 각 Client는 같은 Versioned Source Rule로 Visual Appearance를 만들고 공지 Hash와 일치하지 않는 Source 또는 결과를 거부해야 한다. | P1 | T | PRD 1.8.0 |
| SRS-APPEAR-007 | Invalid·Unavailable·Incompatible Appearance는 Versioned Built-in Default로 수렴해 한 Player가 Lobby를 무기한 차단하지 않아야 한다. | P1 | T | PRD 1.8.0 |
| SRS-APPEAR-008 | C Key 또는 Lobby UI로 Customization 진입, 편집 또는 Preset Load를 시작하면 해당 Guest는 NotReady가 되어야 한다. Apply 성공은 Appearance를 Finalize하지만 Guest를 자동 Ready로 만들지 않아야 한다. | P1 | T/M | PRD 1.8.0 |
| SRS-APPEAR-009 | Host에는 Ready가 없지만 Host Appearance도 Start 활성화 전에 Finalized여야 한다. | P1 | T/M | 사용자 결정 2026-08-24 |
| SRS-APPEAR-010 | Appearance는 Collider, Mass, Gameplay Reach, Hitbox, Weapon Stat과 Shared Camera Bounds를 바꾸지 않아야 한다. | P1 | T/I | PRD 1.8.0 |
| SRS-APPEAR-011 | Cosmetic Mesh는 Game-authored Fixed Color·Fixed Size를 사용하고 승인 Attachment Surface에서 위치·회전할 수 있지만 Tint·Scale을 제공해서는 안 된다. | P1 | T/M | PRD 1.8.0 |
| SRS-APPEAR-012 | Cosmetic Overlap과 시각적 관통을 허용하고 Gameplay 또는 Security 실패로 처리하지 않아야 한다. | P1 | T/M | PRD 1.8.0 |
| SRS-APPEAR-013 | Appearance Limit은 Versioned이며 Preset Count, Serialized Byte, Stroke Complexity와 Cosmetic Instance를 제한하되 Body-part Slot을 만들어서는 안 된다. | P1 | T/I/A | PRD 1.8.0 |
| SRS-APPEAR-014 | C1a Hybrid Core v0.13은 C1b Orthographic View, Measurement, Model과 Physics Profile이 별도 승인될 때까지 Visual Direction 승인으로만 취급해야 한다. | P0 | I/M | Character 결정 |
| SRS-APPEAR-015 | Character는 별도 Visible Hand Mesh 대신 Rounded Forearm Terminal과 Invisible Logical Hand·Strike·Grab·Grip Anchor를 사용해야 한다. | P0 | T/I/M | Character 기술 기준 |
| SRS-APPEAR-016 | Blender Export와 Unity Import는 Unit, Axis, Transform, Skeleton, Normal, Tangent, Material과 Import 설정을 포함한 하나의 Versioned Model Interop Profile을 사용해야 한다. | P1 | I/T | 사용자 품질 요구 |
| SRS-APPEAR-017 | Asset은 개별 수동 Rotation·Scale·Normal 보정 0회로 승인 Interop Profile을 통과해야 한다. | P1 | I/T | 사용자 품질 요구 |
| SRS-APPEAR-018 | 승인 Source, FBX와 Unity Prefab은 C1b에서 승인한 Profile 허용오차로 Orthographic Silhouette, Landmark와 Bounds Evidence를 비교해야 한다. | P1 | T/A/M | 사용자 품질 요구 |
| SRS-APPEAR-019 | UV, Paint Mask, Material Slot, Attachment Anchor와 필수 LOD는 Neutral, Grab, Punch, Air Kick, Dropkick/Recovery, Weapon, Down, Ragdoll, GetUp Review Pose에서 일관되어야 한다. | P1 | T/A/M | Character 기술 기준 |
| SRS-APPEAR-020 | 2·3·4인 Shared Camera Capture에서 Character, Limb Terminal, 좌우 Punch·Air Kick, Dropkick 방향, Hand State, Weapon 종류·방향과 Player Identity를 판독할 수 있어야 한다. | P1 | A/M | 사용자 품질 요구 |
| SRS-APPEAR-021 | 기능 ID `Pistol/LongGun/Bat/Hammer`는 각각 M1911-inspired brand-free low-poly Pistol, AK-47-inspired brand-free low-poly LongGun, logo 없는 baseball Bat와 construction sledgehammer reference role을 사용해야 한다. 실제 모델명·제조사명은 사용자-facing Text로 노출하지 않고 logo·marking·serial과 정확 복제는 0이어야 한다. | P1 | I/A/M | 사용자 결정 2026-08-25, PRD 1.8.0 |
| SRS-APPEAR-022 | Alpha Hybrid Presentation Matrix는 in-place Idle(팔을 몸통에 붙임)·Walk(작은 앞뒤 팔 swing+보행 leg)·Sprint(큰 앞뒤 팔 swing+빠른 leg cadence), Jump physics+procedural pose(전용 clip0, 8방향·공중 방향 변경·착지 후 Idle/Walk/Sprint 연속 전환), L/R Punch, L/R Air Kick, Dropkick·별도 non-Down DropkickRecovery, Grab·release Throw(별도 Lift 조작0), Firearm forward-arm Grip+physics recoil, Melee Swing과 Ragdoll/shared GetUp을 포함해야 한다. 별도 authored Firearm fire/recoil 및 전용 Jump/착지 clip은 0이어야 한다. 모든 clip·blend·pose·physics presentation은 Authority state를 표시하고 gameplay root motion·Collider·hit·impulse·Down mutation을 새로 만들지 않아야 하며 2·3·4인 Camera에서 원인과 방향을 구분할 수 있어야 한다. | P1 | I/A/M | 사용자 결정 2026-08-25·2026-09-04, PRD 1.8.0 Addendum |
| SRS-APPEAR-023 | Alpha Cosmetic catalog는 `EyeSet`·`Mustache`·`Headwear` placeholder 대표 1개씩 또는 fixed size·color, 전신 배치·3축 회전, local save와 Host P2P 검증의 같은 기능 범위를 가진 동등 최소 game-authored 집합이어야 한다. 최종 조형 수량과 production-quality catalog는 Alpha Gate가 아니어야 한다. | P1 | I/T/M | 사용자 결정 2026-08-26, PRD 1.8.0 |

## 12. Local 저장, 진단과 Error 요구사항

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-SYS-016 | 필수 영구 Data는 Local Setting, Local Preset과 제한된 Local Diagnostic Evidence로 한정해야 한다. | P1 | I/T | 사용자 결정 2026-08-24 |
| SRS-SYS-017 | Gameplay는 Database, Online Profile, Remote Object Storage와 Remote Match History를 요구하지 않아야 한다. | P1 | I/T | 사용자 결정 2026-08-24 |
| SRS-SYS-018 | Local Preset 손상 또는 Schema 비호환 시 다른 정상 Preset을 유지하고 조용한 Overwrite 없이 복구·삭제 선택을 제공해야 한다. | P1 | T/M | PRD 1.8.0 |
| SRS-SYS-019 | Local Diagnostic은 Build, Session, Peer, Scene, Round, Map과 Profile Context를 포함한 Structured Record를 사용하고 Secret을 제외해야 한다. | P1 | I/T | PRD 1.8.0 |
| SRS-SYS-020 | Host Diagnostic은 Tick Time, Input Age, Snapshot Gap, Correction, Queue Pressure, Fire reject·Ammo·ShotSequence·Projectile first-hit/cleanup·Recoil/Bloom·Spent, Reconnect, Scene, Supply admission, Patch와 Termination 결과를 포함해야 한다. | P1 | T/A | PRD 1.8.0 |
| SRS-SYS-021 | Diagnostic Write 실패는 Simulation을 Block하거나 Crash시키지 않고 제한된 Local 저장 예산 안에 머물러야 한다. | P1 | T | PRD 1.8.0 |
| SRS-ERR-001 | Error는 Steam, Lobby, Connect, Content, Scene, Reconnect, Appearance, Preset과 Host Loss의 안정적인 사용자 범주를 사용해야 한다. | P1 | I/T | PRD 1.8.0 |
| SRS-ERR-002 | 사용자 Error는 내부 Exception, Path, Secret과 Peer 존재 세부를 숨기고 Retry·수정·Main Menu 복귀 중 가능한 다음 행동을 보여야 한다. | P1 | T/M | PRD 1.8.0 |
| SRS-ERR-003 | Guest Reconnect Countdown과 HostLost는 서로 다른 Message와 다음 행동을 사용해야 한다. | P1 | T/M | PRD 1.8.0 |

## 13. Performance, Usability와 Accessibility 요구사항

아래 값은 검증할 목표이며 달성 결과 주장이 아니다.

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-NFR-001 | 4인·Active Patch 3개·Supply cap3의 Incoming/Loose/Held/Spent Weapon과 승인 cadence의 bounded Projectile worst case에서 두 60Hz Physics step을 포함한 30Hz Authority/Network cycle p95는 20ms 이하를 목표로 하고 p99는 33.3ms cycle budget을 지속 초과하지 않아야 한다. | P1 | T/A | 사용자 결정 2026-08-27, PRD 1.8.0 |
| SRS-NFR-002 | RTT 120ms, Jitter 20ms, Packet Loss 5%에서 2·3·4인 10분 Run은 인원별 완료율 95% 이상과 완료 Run의 Critical Authority Divergence 0건을 목표로 해야 한다. | P1 | T/A | 사용자 결정 2026-08-24 |
| SRS-NFR-003 | 느리거나 Malformed인 Guest 하나가 설정 예산을 넘어 Host Simulation 또는 다른 Guest Snapshot Stream을 지연시키지 않아야 한다. | P1 | T/A | PRD 1.8.0 |
| SRS-NFR-004 | 지원 Preset 상한에서 Local Preset Save·Load·Delete p95는 각각 500ms 이하를 목표로 해야 한다. | P1 | T/A | PRD 1.8.0 |
| SRS-NFR-005 | Appearance Validation과 Peer Sync는 제한된 Lobby Timeout 안에 Finalize 또는 실패하고 Timeout 뒤 안전한 Default로 전환해야 한다. | P1 | T/A | PRD 1.8.0 |
| SRS-NFR-006 | Player Identity, Ready, Down State와 Hazard Warning은 Color만으로 전달해서는 안 된다. | P1 | I/M | PRD 1.8.0 |
| SRS-NFR-007 | Key Rebinding, Cursor Sensitivity·UI Scale, Camera Shake·Motion/Effect Intensity, Subtitle, 별도 Color-vision Marker, Patch 문장 재확인과 Master/SFX/UI/Music volume control은 post-Alpha 재분할 항목이어야 하며 Alpha Gate를 막아서는 안 된다. Alpha Setting은 SRS-INPUT-007의 Tab Hold/Toggle만 요구해야 한다. | P2 | I/M | PRD 1.8.0 |
| SRS-NFR-008 | 사용자 노출 Text는 Localization Key를 사용하고 G4 Steam Persona 등 Platform 사용자 입력을 안전하게 표시해야 한다. | P2 | I/T | PRD 1.8.0 |
| SRS-NFR-009 | HUD Layout은 16:9, 16:10, 21:9에서 중앙 Play Area를 판독 가능하게 유지하고 상태 변화로 Host Start Action Slot을 이동시키지 않아야 한다. | P1 | M/A | PRD 1.8.0 |
| SRS-NFR-010 | Alpha Camera Profile 승인 전에 2·3·4인의 Play와 Spectating Camera 편안함을 각각 측정해야 한다. | P1 | A/M | 사용자 결정 2026-08-24 |
| SRS-NFR-011 | Alpha 사용자-facing 언어는 `Korean-only`(한국어 한 종)이고 Localization StringTable·추가 언어·fallback font와 MainMenu key help는 post-Alpha여야 한다. Alpha Audio는 BGM 0곡과 고정 개발 mix의 기본 combat·weapon·environment SFX로 제한하고 사용자-facing audio channel control과 Music·UI·Patch·Supply audio polish를 Alpha Gate로 요구해서는 안 된다. | P1 | I/M | 사용자 결정 2026-08-26, PRD 1.8.0 |

## 14. 검증과 Release 경계 요구사항

| ID | 요구사항 | 우선순위 | 검증 | 근거 |
|---|---|---:|---|---|
| SRS-SYS-022 | Core Functional, Network, Camera와 Map Test Matrix는 2·3·4인을 각각 포함하고 2인·4인 결과로 3인을 대신해서는 안 된다. | P1 | T/A/M | 사용자 결정 2026-08-24 |
| SRS-SYS-023 | Alpha 승인은 Direct Network Vertical Slice, Weapon W1 완료, Character C1b 승인, Map·Camera Evidence와 아래 P1 Acceptance를 요구하며 Steam 완료를 주장해서는 안 된다. | P1 | I/A/M | 사용자 결정 2026-08-24 |
| SRS-SYS-024 | G4 승인은 실제 Steam Account로 친구 초대, 가역 방 코드, Direct P2P, 강제 SDR, 제한 NAT, Reconnect와 Host Loss를 2·3·4인에서 각각 검증해야 한다. | P2 | T/A/M | 사용자 결정 2026-08-24 |
| SRS-SYS-025 | G5를 진행할 경우 Host와 모든 Guest가 Data-only Workshop Content를 Scene Activation 전에 검증해야 한다. | P3 | T/I/M | PRD 1.8.0 |
| SRS-SYS-026 | Release 검사는 Backend Service, Dedicated Server, Database, Blob·Bake Worker, Development Transport Fallback, Public Matchmaking 기능과 Container Artifact가 각각 0임을 증명해야 한다. | P2 | I/T | 사용자 결정 2026-08-24 |
| SRS-SYS-027 | Alpha InteractiveLobby는 gameplay·Ready·Start·Customizer 흐름을 검증하는 Greybox 품질이어도 통과할 수 있어야 하며 production Lobby art lock을 Alpha 완료 조건으로 요구해서는 안 된다. | P1 | I/M | 사용자 결정 2026-08-26, PRD 1.8.0 |

## 15. Acceptance Scenario

| ID | 시나리오 | 통과 조건 | 단계 |
|---|---|---|---:|
| AT-001 | 2인 전체 Match | Lobby, Start Gate, Round, Patch와 4점 결과를 완료하고 Host·Guest State가 일치한다. | P1 |
| AT-002 | 3인 전체 Match | 2인 또는 4인 대체 없이 모든 Gameplay, Network, Camera와 Map 기능을 완료한다. | P1 |
| AT-003 | 4인 전체 Match | 모든 Player가 일관된 Score, Elimination, Patch와 Hazard State로 Match를 완료한다. | P1 |
| AT-004 | Host Start Gate Matrix | Start Button은 한 Slot에 유지되고 2~4명·모든 Guest Ready·모든 Player Connected·모든 Appearance Finalized·InteractiveLobby인 정확한 경우에만 활성화된다. 모든 실패 Case는 Match 시작 0회다. | P1 |
| AT-005 | Host Ready·StartLever 제거 | Host Ready State·Control과 물리 Lobby StartLever가 없고 Host는 Persistent Start Button으로만 시작한다. | P1 |
| AT-006 | Alpha Direct Session | 통제된 LAN/direct 환경의 2·3·4인이 하나의 Host에 연결해 Lobby→Match→Lobby를 완료한다. | P1 |
| AT-007 | Steam 친구 초대 | 실제 Steam 친구 초대가 2·3·4인의 같은 비공개 Lobby와 Host P2P Session에 참가한다. | P2 |
| AT-008 | Steam 방 코드 Round Trip | SteamLobbyId가 방 코드로 Encode되고 Application Server Lookup 0회로 Local Decode·Checksum 검증되며 잘못된 코드는 Join 전에 실패한다. | P2 |
| AT-009 | Steam 비공개 방 경계 | Outsider, 잘못된 Lobby Member와 Public Discovery는 참가하지 못하고 유효 Invite·Code는 같은 Host로 수렴한다. | P2 |
| AT-010 | Authority 공격 | 위조 Position, Hit, Damage, Score, Down과 Elimination 주장은 Authority Mutation 0건이다. | P1 |
| AT-011 | Guest Reconnect | 예상치 못한 disconnect 30초 안에는 neutral input·physical/vulnerable Character를 유지하고 현재 Alive/Spectator State로 복원하며 경계 뒤에는 안전하게 Forfeit한다. | P1 |
| AT-012 | Host Leave·Loss | Host의 명시적 Leave 또는 Loss에서 모든 Guest가 transient HostLoss를 보고 Main Menu로 돌아가며 Host Migration은 0회다. | P1 |
| AT-013 | Scene 왕복 | Lobby→Match→Lobby가 하나의 Host Session을 유지하고 Ready Content만 활성화하며 과거 Lifecycle Packet을 거부한다. | P1 |
| AT-014 | Network Matrix | 2·3·4인 Local, Typical, Target, Edge, Reorder Run이 완료율, Divergence, Correction과 Queue 결과를 기록한다. | P1 |
| AT-015 | Sprint | Left Shift Hold는 Lobby와 Match에서 Stamina State 없이 승인 Profile Multiplier로 Sprint한다. | P1 |
| AT-016 | Esc 안전 Mouse Rearm | Esc가 Cursor Mode를 열고 닫으며 Strike·Throw 없이 Hand를 취소하고 두 Button Up 뒤에만 새 Hand Action을 허용한다. | P1 |
| AT-017 | Tab Mode | Hold·Toggle 설정이 모두 동작하고 Overlay에는 Score와 Active Patch만 있다. | P1 |
| AT-018 | Ground Tap, Hold와 Grab | Loss·Reorder 상황에서도 Grounded LMB/RMB 한 Press는 해당 손 Punch 또는 Grab 경로 하나만 만든다. | P1 |
| AT-019 | DownCount와 Groggy | Match Round 시작 DownCount=0, 첫 새 Episode는 1과 BaseDuration, 두 번째부터 Increment 추가, MaxDuration 상한, 중복 증가 0, Reconnect 복원과 다음 Round Reset을 검증한다. Lobby Ragdoll은 항상 BaseDuration이고 Match Count를 바꾸지 않는다. | P1 |
| AT-020 | Weapon W1과 Firearm | 승인된 W1 Binding으로 권한 Fire, Hit·Projectile, Damage, Knockback과 Rate Limit Test를 통과한다. | P1 |
| AT-021 | Melee·Drop·반복 Supply | Melee 중복 Hit 방지·Damage·Knockback, 명시·강제 Drop과 재획득을 통과하고 2·3·4인 Supply START·cap·bag·Safe DropZone·Incoming→Loose·OOB next-pulse·SuddenDeath cancel·reset이 backlog 없이 수렴한다. | P1 |
| AT-022 | Patch Functional Lifecycle | 승인 Patch12가 2·3·4인에서 projected active set 기반 평문 2×2 선택, Timer·Timeout·결과, 다음 Round 전원 활성화, Patch09·10 capacity admission·derived wave, Patch11·12 Host-confirmed forced drop, semantic presentation state, 재귀 방어, Reset·Reconnect와 최대 3개 FIFO를 통과한다. 최종 icon·animation·VFX·SFX·layout은 통과 조건이 아니다. | P1 |
| AT-023 | Map Matrix | 하나의 Map Build가 동일 Geometry·Hazard, Safe Spawn·Safe DropZone, 결정적 배정과 유효 탈락 경로로 2·3·4인을 지원한다. | P1 |
| AT-024 | Shared Camera Matrix | 2·3·4인의 Play·Spectating이 지원 화면비에서 하나의 판독 가능한 Camera를 공유한다. | P1 |
| AT-025 | Local Appearance Relay | Local 저장한 제한 Source를 Host가 검증·P2P Relay하고 모든 Peer가 같은 Final Hash로 표시한다. | P1 |
| AT-026 | Appearance 공격 | 임의 Texture·File·URL·Script, 초과 Source, Invalid Catalog·Transform을 거부하고 Lobby를 막지 않는 안전 Default로 수렴한다. | P1 |
| AT-027 | Cosmetic 불변성 | Fixed Size·Color 자유 배치, Overlap과 Pose·LOD 추적이 동작하고 Collider, Reach, Mass와 Camera Bounds는 불변이다. | P1 |
| AT-028 | Blender→Unity 동등성 | 승인 Model Interop Profile로 수동 보정 0회 Import하고 C1b Silhouette, Landmark, Bounds, UV, Material과 Pose·LOD Evidence를 통과한다. | P1 |
| AT-029 | Performance | 필수 인원에서 Host Tick, Target Impairment, Peer 격리, Preset Latency와 Appearance Timeout 목표를 측정한다. | P1 |
| AT-030 | Alpha 최소 접근성 | Identity·Ready·Down·Hazard가 색 이외 신호로 구분되고 Tab Hold/Toggle이 동작한다. Key Rebinding·Cursor/UI Scale·Shake/Motion/Effect·Subtitle·별도 Color-vision·Patch 문장·audio volume 설정은 Alpha Gate에 없다. | P1 |
| AT-031 | Error와 Diagnostic | Reconnect, HostLost, Steam Join, Scene, Appearance와 Preset 실패가 안전한 행동과 Secret 없는 Local Evidence를 제공한다. | P1 |
| AT-032 | 영구 비범위 검사 | Source, 설정과 Release Artifact의 Backend Service, Database, Dedicated Server, Container, Public Matchmaking, Rank와 MMR 경로가 각각 0이다. | P2 |
| AT-033 | G5 Data-only UGC | Script, DLL, 외부 Bundle, Traversal과 Invalid Content는 Activation 전에 실패하고 유효 Content는 공식 Map Runtime을 사용한다. | P3 |
| AT-034 | Ground/Air Tap·Hold·Chord·Token | Ground L/R quick tap은 해당 손 Punch, Airborne non-Down single quick release는 chord close 뒤 해당 발 Kick, 60/80/100ms 비교의 valid dual down-edge는 즉시 Dropkick 한 번이다. Hold threshold는 미commit single Kick만 Grab으로 취소하고 AirAttackToken은 episode당 공격 1회이며 stable Grounded·GetUp·reset에서 복원된다. | P1 |
| AT-035 | Dropkick Authority·Patch·No-Down | Host만 Kick·Dropkick action/hit/physics를 확정하고 Dropkick impulse·air steer·knockback·recovery를 bounded하게 적용한다. DropkickRecovery는 DownEpisode와 별도이고 DownCount·TRG-DOWN은 0이다. Prone settle 뒤 방향별 GetUp 시각 모션 공유는 허용하지만 상태 합성은 0이며 SourceKind Kick/Dropkick은 Patch003/004만 발동하고 Weapon Patch011/012는 0이다. | P1 |
| AT-036 | Hybrid Presentation Matrix | Idle arms-to-torso, Walk small arm swing+leg motion, Sprint large arm swing+fast leg cadence, Jump physics+procedural pose(전용 clip0, 8방향·공중 방향 변경·착지 후 Idle/Walk/Sprint 연속 전환), L/R Punch·Air Kick, Dropkick/별도 Recovery, Grab/release Throw(별도 Lift 조작0), Firearm forward-arm Grip+physics recoil, Melee Swing, Ragdoll/shared GetUp이 2·3·4인 Camera에서 읽힌다. authored Firearm fire/recoil·전용 Jump/착지 clip·Animation gameplay root motion·authority mutation은 각각 0이다. | P1 |
| AT-037 | Weapon Archetype·W1 Air Mapping | 네 기능 ID가 brand-free M1911-inspired Pistol, AK-47-inspired LongGun, baseball Bat, construction sledgehammer로 판독되고 사용자-facing 실제 모델명·logo·marking은 0이다. W1은 승인 Air Kick/Dropkick/airborne Grab mapping을 유지하며 Airborne WeaponUse는 별도 승인 action/mode 없이는 0이다. | P1 |
| AT-038 | FireMode·Ammo·Spent·Supply | Pistol press당 semi-auto 1발·total7, LongGun valid hold full-auto·total30, reserve/reload0이 동작한다. 마지막 Shot 뒤 Host forced release→SpentPendingCleanup 2~4초 deadline remove가 cap을 유지하고 replacement는 다음 정규 Pulse만 사용한다. | P1 |
| AT-039 | Projectile Authority·Collision·Lifecycle | Host만 Ammo-1과 visible Projectile을 원자 생성하고 swept first hit, no pierce/ricochet, TTL·OOB·Result·reset을 통과한다. Projectile별 Patch03 Action/Target·Patch04 Action dedupe가 1회이고 delayed last-shot source Spent/owner-loss의 Patch12는 NoEligibleTarget이다. Guest Ammo/Projectile/Hit 주장과 remote impulse는 0이다. | P1 |
| AT-040 | Recoil·Spread·Phase·Debug | 좌우 어느 손이든 같은 팔 뻗기 Grip template을 적용하고, Host Rigidbody/Joint recoil만 해당 팔·Weapon을 위로 움직여 복귀시킨다. authored fire/recoil clip은 0이며 Pistol narrow spread·strong single recoil과 LongGun deterministic cumulative RecoilAccumulator/SpreadBloom이 2·3·4인에서 구분된다. Fire/Projectile은 Playing+SuddenDeath에서 유효하고 RoundResult부터 0이며 Ammo·FireMode·Projectile state는 developer debug에서 검증되고 Player HUD에는 0이다. | P1 |
| AT-041 | Alpha Match UI 허용 목록 | AT-042의 local Esc menu를 제외한 gameplay UI에는 persistent timer·alive·ammo·killfeed·result panel이 각각 0이고 transient 3·2·1·between-round Patch·OpponentLeft/HostLoss/error와 on-demand Tab만 나타난다. Tab은 Score·Active Patch 외 정보를 0개 표시하고 Ammo는 developer debug에만 있다. | P1 |
| AT-042 | Match Esc Non-pausing Menu | Host와 Guest 각각 Match menu를 열어도 Authority simulation·Round timer·Hazard·외부 physics가 계속되고 local gameplay Input만 Neutral이다. 닫은 뒤 Mouse all-up 전 Hand·Weapon 오발은 0이다. | P1 |
| AT-043 | Guest Leave·Disconnect·Forfeit Matrix | Lobby와 Match의 2·3·4인에서 explicit Leave는 grace 없이, unexpected disconnect는 30초 physical/vulnerable grace 뒤 timeout Forfeit로 처리된다. Reconnect는 Alive/Spectator state로 수렴하고 Forfeit는 PatchAuthor 0이며 permanent participant 2명 이상은 계속, 1명은 Score·Patch 0과 OpponentLeft 뒤 Lobby로 복귀한다. | P1 |
| AT-044 | Alpha 품질 범위 | Greybox InteractiveLobby, 최소 placeholder Cosmetic catalog, Korean-only, BGM 0과 고정 개발 mix의 기본 combat·weapon·environment SFX만으로 Alpha 기능 Gate를 통과하며 StringTable·fallback font·MainMenu key help, audio channel control과 production Lobby/Cosmetic/Audio polish는 요구하지 않는다. | P1 |

| AT-045 | 다방향 회복·전 주기 변형 | Front/back/left/right·대각선·360° yaw·경사면에서 Host 진입 판별, support/clearance 대기, side 정렬→shared GetUp·방향 보존·중복 진입 방지·중단/reset·Down/Dropkick Count 분리를 검증한다. Walk/Sprint 전 sample frame·loop와 양손 공통 Grip에서 관절 과굴곡·자기교차·뒤집힘을 검사하고 실제 동작을 육안 확인한다. | P1 |

## 16. 실제 미결정 사항

다음 항목만 지정 조사 또는 사용자 Gate에서 결정한다.

- Steamworks C# Wrapper와 Steam Networking Sockets Adapter 구현체
- Replication에 유지보수되는 Unity Package를 사용할지 Adapter 뒤의 소규모 Custom Layer를 사용할지
- W1 Weapon Input Binding, Airborne WeaponUse 허용 여부·별도 action/mode와 Firearm·Melee·Drop Action Map
- Sprint Multiplier와 DownState BaseDuration·Increment·MaxDuration의 정확한 수치
- Hand Threshold, DualClickChordWindow 60/80/100ms 최종값, Air Kick·Dropkick·Recovery, Grip, Jump,
  Movement, Camera와 Network Tuning Profile의 최종 수치
- C1b 정확 Character Measurement, Final Mesh, Collider, Mass와 Reach Profile
- Patch13..20 Trigger·Effect 확장 Catalog와 Authored Conflict 조합
- 승인 Patch12의 개별 물리 `START` tuning 값; 반복 Supply의 인원별 시간·cap과 Patch10 second-wave 범위는 승인 시작값
- Pistol/LongGun fire cadence, Projectile speed·SphereCast radius·TTL, recoil magnitude/recovery,
  RecoilAccumulator·SpreadBloom 증가·상한·decay와 SpentPendingCleanup 2~4초 시작값
- SteamLobbyId 가역 Decode와 Server Lookup 0 조건을 유지하는 방 코드 Alphabet·Version Marker·Checksum Algorithm
- Local Preset File Format의 Migration 정책
- post-Alpha 추가 지원 언어·StringTable·fallback font와 G5 Gamepad 포함 여부; Alpha 한국어 한 종은 확정
- 네 승인 Weapon archetype의 exact low-poly proportion·material·LOD와 silhouette Lock

다음은 미결정 사항이 아니다. Host Authority, Backend 0, Dedicated Server 0, Database·Blob·Bake Worker 0,
Container 0, Public Matchmaking·Rank·MMR 영구 제외, Alpha 뒤 Steam, Steam Code·Friend Invite가 하나의
비공개 Host P2P Session으로 수렴, Host Ready 없음, Persistent Host Start Button, Lobby StartLever 없음,
Stamina 없는 Shift Sprint, Tab Hold·Toggle, 확정된 DownCount 산식, Host가 검증하는 제한 P2P Appearance Source,
Ground/Air tap·hold·dual chord mapping, AirAttackToken 1, Dropkick no-Down, Hybrid Animation root motion 0,
brand-free 네 Weapon reference role, Pistol7 semi-auto·LongGun30 full-auto·reserve/reload0,
Host swept Projectile와 hybrid recoil/spread 구조, 2·3·4인 개별 Test는 고정 결정이다.
Persistent Match HUD 0, developer-only Ammo debug, local-only non-pausing Match menu, Guest
Leave·disconnect·Forfeit와 Host Leave·Loss 경계, Greybox Lobby·placeholder Cosmetic·한국어·기본 SFX의
Alpha 품질 범위도 고정 결정이다.

## 17. 완전성 선언

- 요구사항 ID: 보존 Category Prefix 전체에서 총 187개이며 Prefix별 001부터 연속·Unique다.
  SYS 27, GAME 7, INPUT 9, PHYS 15, CAM 6, ROUND 6, PATCH 9, MAP 10, NET 15,
  LOBBY 10, APPEAR 23, WEAPON 17, UI 6, STEAM 8, SEC 5, NFR 11, ERR 3이다.
- Acceptance ID: AT-001..045, 연속·Unique 45개다.
- 모든 요구사항 행은 우선순위, 검증과 Source를 선언한다.
- 정확한 Protocol 직렬화, Crypto Transcript, Hash 수식과 반복 Fixture 횟수는 이 Lean SRS에서 의도적으로 제외한다.
