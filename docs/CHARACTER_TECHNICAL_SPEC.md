# Project Hotfix 캐릭터 기술 사양

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| 문서 버전 | 0.12.0 C1B Static Interop Baseline + 2026-09-04 Action Presentation Addendum |
| 최종 수정일 | 2026-09-10 |
| 상위 기준 | `01_PRD.md` 1.8.0 + 2026-09-04 Addendum, `02_SRS.md` 1.8.0 + 동일 Addendum |
| 목적 | `MasterCharacter`의 승인 방향, 제작 Gate, 물리·입력·Paint·Cosmetic·무기 통합 기준 정의 |
| 우선순위 | 게임 규칙은 PRD/SRS, 패치 콘텐츠는 `PATCH_DESIGN.md` 0.5.0, 시각 언어는 `ART_DIRECTION.md` 1.9.0, 구체 캐릭터 제작은 본 문서 |

이 문서는 결과와 승인 기준을 정의한다. 구현체의 바이너리 배치, 내부 직렬화 수식과 반복 fixture
개수는 구현·테스트 문서에서 관리하며 캐릭터의 제품 방향으로 취급하지 않는다.

### 2026-10-30 펀치 중 몸 묶임·옆뒤로 빠지기 결정

- 펀치를 뻗고 있는 동안 몸은 시선(상대) 쪽으로 초당 최대 90°까지만 돈다. 펀치를 시작할 때 몸이 향하던 방향에서
  출발하며, 카메라 시선은 그대로 자유롭게 돈다. 펀치가 끝나면 평소처럼 바로 시선을 따라간다. 연타하는 동안에는 거의 항상
  펀치 중이므로 몸이 내내 천천히 돈다(시제품: 연타하는 쪽은 99~100% 시간이 펀치 중).
- 펀치를 뻗고 있는 동안 걷기·달리기 속도는 절반이 된다.
- 바짝 붙었을 때의 훅은 몸 정면에 있는 상대에게만 나간다. 펀치를 시작할 때 상대가 몸 정면에서 20°를 넘으면 줄기
  시작해 35° 이상이면 훅이 아닌 기본 펀치가 나간다(시제품: 옆으로 도는 사람이 맞은 33대 중 27대가 0.36 m 안의 훅이었고
  정면에서 최대 약 45° 벗어나 있어도 맞았다).
- 연달아 세 번째로 맞았을 때의 밀어내기는 최소 4.0 m/s다(2.6 m/s에서 올림). 가슴에 맞으면 원래 약 5 m/s로 밀려
  1.8 m 떨어지지만, 위팔에 비스듬히 맞으면 2.6 m/s만 받아 0.8 m에서 멈추고 때린 쪽이 바로 다시 걸어 들어왔다. 4.0 m/s로
  밀린 사람이 넘어진 경우는 12판에서 없었다.
- 배경(10/29 이후 남은 과제 "옆으로 피해 빠져나가기"를 사용자가 B안·훅 제한·밀어내기 보강·이동 감속 순서로
  골랐다): 위 규칙 전에는 0.1초마다 연타하며 따라붙는 상대에게 옆으로 돌거나 대각선 뒤로 걸어도 가만히 서 있을 때와
  비슷하게 맞았다(10초에 9.5대, 4판 중 3~4판 기절). 뒤로 물러나기만 통했다.
- 결과(시제품 봇 측정, 공격자는 상대를 정확히 바라보고 0.75 m 안에서만 연타, 4판×10초): 대각선 뒤로 걷기는 걸어서 쫓는
  상대에게 7.8대에서 1.8대, 달려서 쫓는 상대에게 9.5대(3판 기절)에서 2.0대(기절 없음)로 줄었다. 뒤로 걷기는 달려서 쫓는
  상대에게 5.8대에서 1.8대가 됐다. 제자리에서 옆으로 원을 그리며 도는 것은 거리가 벌어지지 않아 탈출이 아니며 오히려 더
  맞았다(8대 안팎에서 10대 안팎, 4판 모두 기절). 즉 피하기는 옆과 뒤가 섞인 움직임이다. 옆으로 돌면서 맞받아치면 연타만
  하는 상대를 이기는 쪽이 됐다(멀어져도 계속 누르는 봇 기준).
- 남은 확인: 연타 중 몸이 늦게 돌고 느리게 걷는 느낌은 플레이로 본다. 물러나는 상대를 주먹으로는 거의 잡지 못하므로
  점프 발차기·달리기 점프 드롭킥이 이를 잡는 수단으로 충분한지 아직 측정하지 않았다. 달리다 누른 펀치가 미는 거리도
  다시 재지 않았다.

### 2026-10-29~30 싸움 흐름·점프 발차기·달리기 입력 결정

- 바짝 붙어 누르면(두 몸 사이 0.36 m에서 0.30 m로 갈수록) 몸 펀치가 가슴 높이의 짧은 훅으로 바뀐다. 기본 펀치는 그
  거리에서 주먹이 몸 옆을 지나쳐 빗나갔다(시제품: 서 있는 상대를 계속 때리면 훅 없이는 10초에 3번, 훅이 있으면 27번
  맞음). 훅은 팔에 먼저 막혀 다가가는 속도가 낮게 나오므로 최소 0.8배 세기로 치고 1.3배 멀리 민다(사용자: "한손으로
  때리는건 0.8이 좋은 것 같은데"). 2026-10-30부터 몸 정면에서 35° 넘게 벗어난 상대에게는 훅이 나가지 않는다(위 결정).
- 한 사람이 받아치지 못하고 0.8초 안에 연달아 세 번째로 맞으면, 때린 쪽 반대 방향으로 최소 2.6 m/s(2026-10-30에 4.0 m/s로 올림)로 밀려나 싸움에서
  빠져나오고 연속 횟수는 0이 된다(사용자: "계속 싸움 안에 갇혀 있는 건 좀 별론데"). 그 뒤 0.4초 동안은 그 사람에게
  어떤 펀치도 맞지 않는다(이미 날아오던 반대 손 펀치가 바로 다음 연속을 시작했다). 맞는 쪽이 한 대라도 맞히면 상대의 연속
  횟수가 0이 된다. 같은 날 시험한 "팔끼리 부딪히면 서로 밀어내기"는 박자가 맞을 때 어색하게 보여 기각했다(사용자:
  "차라리 운인 편이 더 재밌겠는데").
- 끊기: 서 있는 사람이 맞으면, 뻗은 지 0.13초가 안 된 자기 펀치는 취소된다. 점프 중에 낸 공격은 끊기지 않는다.
- 흐물: 맞을 때마다 팔·머리(몸통은 절반)의 힘이 잠깐 빠져 흔들린다. 세기에 따라 0.35~0.6만큼 빠지고 초당 2.5씩
  돌아오며, 뻗고 있는 팔은 빠지지 않는다.
- 운 펀치: 쓰러뜨리지 않은 점수 펀치는 7% 확률로 상대를 굴러 날아가게 한다(3.4 m/s, 위로 1.8 m/s). 이 구르기는 Down이
  아니며 확률은 같은 입력이면 다시 돌려도 같게 나온다.
- 맞불: 두 사람이 0.06초 안에 서로 맞히면 둘 다 2 m/s로 떨어지며 크게 흔들린다. 0.03초로 좁혀도 둘 다 연타할 때 3판
  16번에서 13번으로만 줄어서(쿨다운 때문에 박자가 맞아 대부분 2프레임 안에 겹침) 0.06초를 유지한다.
- 점프 발차기(사용자 선택: 1점+크게 밀기): 서서 점프한 공중에서 좌·우 어느 버튼을 눌러도 같은 두 발 차기가 나간다. 몸이
  앞으로 1.6 m/s 튀어 나가며 두 다리를 엉덩이 높이로 쭉 펴고 상체를 35° 뒤로 젖힌다. 발이 0.24초 안에(맞으면 0.08초 뒤,
  땅에 닿으면 즉시) 다시 몸 밑으로 내려와 서서 착지한다. 발에 맞은 상대는 1점을 받고 3 m/s로 밀리며 크게 흔들리고 뻗던
  펀치가 끊긴다. 점수가 기준에 닿으면 이 발차기로도 쓰러진다. 시제품 측정: 0.35~0.65 m에서 맞고 0.8 m는 닿지 않으며,
  약 1.1 m 밀어낸다. 연타하는 상대에게 공중 펀치로 받아치면 3판 모두 졌고, 발차기로는 1승 1패 1무였다(27번 중 26번
  맞음, 너무 잘 맞는지는 플레이로 본다). 같은 날 리뷰("다리가 꺾여서 떨어진다")로 다리를 엉덩이를 축으로 편 채 휘두르게
  바꿔 무릎이 최대 103°에서 53°로, 착지 때 상체 기울기가 약 32°에서 2°로 줄었다.
- 달리기 입력(2026-10-20 "Sprint 중 손 입력은 Dropkick만"을 대체): 땅에서 달리는 중에 누르면 걸을 때와 같은 펀치·잡기다
  (Shift로 쫓다가 누르면 원치 않는 드롭킥이 나갔다). 달리다가 점프(Space)한 공중에서 누를 때만 Dropkick이 나가고, 누른 손
  쪽으로 돌아 날아간다(시제품: 왼쪽 -94°, 오른쪽 82°). 점프 없이 부딪혀 발이 잠깐 뜬 것은 점프로 치지 않는다(쫓기 6판 중
  1판에서 원치 않는 드롭킥이 나갔다). 공중 드롭킥은 점프하며 올라가던 속도에 1 m/s를 더해(1.2~2.4 m/s) 땅 드롭킥보다 높이
  난다(몸 높이 0.79 m 대 0.60 m). 달리며 때리는 펀치는 0.65~0.95 m에서 누르면 맞고(걷기 0.55~0.85 m) 0.45~0.55 m는 너무
  가까워 빗나간다. 걷기와 달리기 모두 12번 중 8~9번 맞으며, 몸이 같이 부딪혀 대체로 1.5배쯤 더 민다.
- 기상 정렬이 목표와 반대쪽으로 굴러 90° 넘게 어긋난 채 1초 멈추면, 지금 누운 방향에서 다시 정렬을 시작한다(공중
  드롭킥을 찬 쪽이 목표에서 173° 어긋난 채 끝까지 누워 있었다).

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
  발차기는 아직 없어 공중 클릭도 펀치다(2026-10-30 대체: 공중 클릭은 점프 발차기, 달리기 점프에서는 Dropkick).
- 옆·대각선·뒤로 걸을 때 보폭을 줄이지 않는다(기존 시제품은 몸 방향과 다른 이동에서 보폭을 0.65배로 줄여 발이
  미끄러졌다).
- 달리기 팔 모양(같은 날 리뷰: 3인칭 뒤에서 보면 "팔이 W 모양"): 팔꿈치가 74~84°로 굽은 채 위팔이 옆으로 12~22°
  벌어지고 뒤로 50°·앞으로 20°만 흔들려, 뒤로 간 팔뚝이 들리며 W자로 보였다. 팔꿈치는 기본 45°에 팔이 앞으로 올 때
  20°를 더 굽히고, 위팔은 몸 쪽으로 10° 붙이고, 흔들림을 앞쪽으로 12° 옮겼다. 측정: 위팔 뒤 36°~앞 33°, 옆 1~11°,
  팔꿈치 27~61°. 2026-10-20에 정한 "달리기는 팔을 굽혀 앞뒤로 흔든다"는 그대로다.
  2026-10-29 리뷰("뒤로도 팔꿈치를 굽힌 상태에서 뒤로도 팔이 가야", "뒷 각도는 R2", 앞으로 가는 각도는 줄여 10° 선택)로
  다시 바꿨다: 팔꿈치 기본 55°에 앞으로 올 때 10°, 뒤로 갈 때 35°를 더 굽히고, 흔들림 45° 중 앞쪽은 55%만, 앞쪽 치우침은
  2°다. 측정(달리기): 위팔 뒤 51°~앞 10°.
- 엉덩이 아랫단 피부(2026-10-29 리뷰: 3인칭 뒤에서 보면 다리를 뒤로 찰 때 몸통 모서리째 딸려 나옴): r46 메시가
  엉덩이 보조 뼈 HipDeform_L/R(시제품이 허벅지 회전의 1/4만 따라 돌림)에 가중치를 주지 않아, 관절(0.18 m) 위
  엉덩이까지 허벅지를 그대로 따라 돌았다. 다리는 가랑이 높이(0.20 m)까지 허벅지를 그대로 따르고, 그 위 0.20~0.30 m
  엉덩이 띠는 HipDeform을 따르며, 한가운데 이음매 0.04 m 안은 골반에 남긴다(빌드 `R43_HIP_HELPER=.12,.20,.20,.30,.04`).
  가랑이 아래까지 엉덩이 띠에 넣은 안(H1)은 엉덩이는 가장 안정했지만 허벅지 윗부분이 서 있는 채 아랫부분만 돌아
  다리가 ㄱ자로 꺾여(허벅지 뼈와 최대 44°) 기각했다. 채택안 측정(달리기): 허벅지 윗부분 어긋남 최대 4°(이전 3°),
  엉덩이 뒤쪽 출렁임 중앙값 3→2 cm(최대 4 cm 그대로). 더 줄이려면 가랑이를 깊게 하거나 엉덩이 보정 형태가 필요하다.
- 어깨 앞 주름(2026-10-29 리뷰: 달릴 때 팔이 앞으로 가면 어깨에서 팔로 떨어지는 앞쪽이 꺾임). 원인 확인: 보정을 끄면
  더 날카로워지고(보정 과다 아님), 오늘 이전 달리기 팔에도 있었으며, 쇄골을 같이 움직이면 더 나빠지고, 메시 둥글게·관절
  위치·가중치 경계 조정은 효과가 없거나 다른 주름을 만들었다. 팔 뿌리가 관절 3 cm 뒤부터 통째로 돌아 굽힘이 겨드랑이 앞
  좁은 띠에 몰리는 스키닝 구조의 한계이고, 달리기 앞쪽 흔들림 자세에 보정이 없었다. 해결(M3): 어깨 보정 기준 자세에
  앞 20°·35°를 더하고, 앞·옆 자세 보정에만 접힌 골을 매끄럽게 메우는 단계를 넣었다(주름 둘레 16칸에 걸쳐 서서히,
  점마다 최대 12 mm). 서 있을 때·팔이 뒤로 갈 때는 바뀌지 않는다. 측정(서 있을 때보다 25° 넘게 더 꺾인 모서리 수):
  달리기 왼쪽 p90 63→33·최대 90→34, 오른쪽 p90 101→49·최대 111→53, 걷기는 거의 같음(최대 7→11 / 21→13).
- 펀치 칠 때 다리(2026-10-29 리뷰: 3인칭에서 펀치마다 다리가 비틀리며 뒤로 빠져 보임 — 원래부터 같았고, 상체 비틀림의
  0.32를 골반이 따라 돌고 준비 동작에 골반이 1.2 cm 내려앉으며 몸이 앞으로 밀렸기 때문). 플레이 장면에서는 골반이 비틀림의
  0.10만 따라 돌고(빠진 몫은 척추가 맡아 상체 비틀림은 그대로), 내려앉지 않으며, 앞으로 밀기는 절반, 상체 숙임은 1.2배다
  (사용자 선택 '섞음 B'). 측정: 골반 비틀림 연타 최대 7.6°→2.4°, 내려앉음 1.2→0 cm, 0.32~0.55 m 양손 모두 맞음.
  검토용 메뉴 장면과 캡처는 기존 값 그대로다.
- 가랑이 폭과 다리 모양(2026-10-29 리뷰: 가랑이가 너무 넓어 보이고, 앞에서 보면 다리가 살짝 O자). 가랑이 아치를 좁혀(N12)
  바닥에서 두 다리 사이가 16.0→12.7 cm, 다리 뼈는 가운데서 0.127 m다. 뼈는 곧은데 피부가 무릎 높이에서 바깥으로 휘어
  있어, 뼈는 두고 다리 피부만 안쪽으로 옮겼다: 무릎 위(0.11 m)까지 다리 양쪽 가장자리를 1.6 cm 안으로, 안쪽 선은
  가랑이(0.19 m)까지, 바깥 선은 허리(0.42 m)까지 서서히 되돌린다(빌드 `LEG_TOE_IN=.016,.11,.19,.42`,
  `R43_LEG_X=.127`). 바깥 선도 가랑이에서 되돌린 안(T5)은 엉덩이 옆이 0.9 cm 튀어나와 바꿨고, 대신 엉덩이 옆이 가랑이
  높이에서 한쪽 1.4 cm 좁아졌다. 측정(무릎 높이 휜 정도, 서 있기 / 걷기 중앙값 / 달리기 중앙값·최대): 0.3 / 2.7 /
  7.0·11.1 mm, 이전 2.7 / 6.2 / 8.2·15.6 mm.
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
  별도 결정 전까지 유지한다. (2026-10-30 대체: 땅에서 달릴 때는 펀치·잡기이고, 달리다 점프한 공중에서만 Dropkick이다.)
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


### 0.0.1 2026-09-04 Action Presentation 결정

- Idle은 팔을 몸통에 붙이는 pose, Walk는 작은 앞뒤 팔 swing+보행 leg motion, Sprint는 큰 앞뒤 팔
  swing+빠른 leg cadence를 사용하며 모두 in-place다.
- Firearm은 기존 손 소유 관계를 유지한다. Pistol은 Main hand, LongGun은 Main+Support hand를 사용하되
  해당 팔을 앞으로 뻗는 단순 공통 Grip pose로 표현한다.
- 별도 authored Pistol·LongGun fire/recoil animation clip은 만들지 않는다. Host Unity Rigidbody/Joint
  recoil이 팔과 Weapon을 위로 움직이고 승인 spring/damping으로 복귀시킨다.
- `DropkickRecovery`와 `DownEpisode`는 별도 gameplay state다. physics tumble 뒤 실제로 누운 경우에는
  방향별 Ragdoll GetUp presentation을 공유할 수 있지만 DownCount·groggy·Down Trigger는 만들지 않는다.

### 0.0 2026-09-05 캐릭터 제작 현황

r16 Neutral 외형 방향은 2026-09-02 `UG-C1B-NEUTRAL` 승인 완료. r20은 기존 r16 geometry를 유지한 Blender Generic22(논리20+hip helper2) 리그 검토 후보다. 내부 Neck은 허용하며 가시 목0을 유지한다. 원본 r20의 Action0은 보존하고 별도 후보에서 공식 정적 Pose·보행/Grip·Grab/Wall IK와 팔꿈치 중심 Punch Action2를 검토했다. Unity 게임용 Prefab/Collider/animation 통합은 미완료다. 캐릭터 최종 Gate에는 `C1BRW-004` 공식 정적 Pose8·lineup 검토가 남아 있으며 `C1BRW-005` static parity와 `C1B-006 / UG-C1B`는 미완료다.

현재 상태의 공통 기록은 [캐릭터 검토 기록](../artifacts/reports/CHARACTER_REWORK_NEUTRAL_REVIEW.md)이다.
r01..r15와 old C1B 기술 결과는 역사로 보존한다. r16 approval JSON의 시각 방향 승인과
production topology·리그·애니메이션·Collider의 결과 승인을 구분한다.

### 0.1 0.12.0 변경 요약

- 첫 C1B static Blockout 반입에서 확인한 Blender/Unity handedness 차이를 `ModelInteropProfile-ART-001-r02`의
  `C1BBlockout` 전용 transient `ReflectX + reverse winding` export override로 고정했다.
- 이 override는 canonical `.blend`를 바꾸거나 Unity Prefab에 개별 Rotation·Scale correction을 남기지 않으며,
  Static Blockout에만 적용된다. Armature·Skinned Character와 base r01 preset에는 적용하지 않는다.
- C1B Blockout은 UV0가 없는 비생산 Mesh이므로 Unity tangent import를 `None`으로 두는 좁은 예외를 추가했다.
  전역 UV0·authored tangent 요구는 유지하며 C4 production source부터 다시 필수다.
- C1B-005는 static silhouette·landmark·bounds·pivot·관절 접합만 검증한다. Armature·Action·Animation·Collider와
  모션 자연스러움은 0이며 ANP/C2 이후 검증으로 남긴다.

### 0.2 0.11.0 변경 요약

- Guest 30초 Disconnect grace의 Neutral Input과 계속되는 Character physics·vulnerability·Alive/Camera
  참여, current Alive/spectator reconnect를 고정했다.
- explicit Leave·timeout Forfeit의 PatchAuthor 제외, 2명 이상 continuation과 one-left Score·Patch 0
  Lobby return, Host loss Session end를 추가했다.
- Match local menu를 local-only·non-pausing presentation으로 분리하고 지속 Match·Active Patch·Ammo HUD를 0으로 했다.
- Alpha Cosmetic은 EyeSet·Mustache·Headwear 각 placeholder 대표 1개 또는 같은 기능 범위의 동등 최소
  catalog만 사용하고 full catalog를 post-Alpha로 분리했다.
- ModelInteropProfile의 reference toolchain을 Unity 6.3 LTS·Blender 5.2 LTS로 정하고 실제 설치 patch
  version은 Plan 2.5 `FDN-010` ToolchainProfile이 소유하게 했다.

### 0.3 0.10.0 변경 요약

- Pistol 7발 semi-auto와 LongGun 30발 full-auto·reload 0, Ammo 0 spent cleanup lifecycle을 추가했다.
- Host visible Projectile의 fixed-step swept SphereCast·first-hit·no-pierce/no-ricochet와 TTL·OOB·reset을 정의했다.
- Host Rigidbody recoil impulse/torque·muzzle physics와 read-only recoil animation을 분리했다.
- Pistol single recoil과 LongGun RecoilAccumulator·SpreadBloom·deterministic ShotSequence pose/state를 Action Matrix에 추가했다.
- Projectile별 Patch03·04 dedupe와 source spent/owner loss 뒤 Patch12 NoEligibleTarget을 고정했다.

### 0.4 0.9.0 변경 요약

- Ground hand Punch·Grab과 Airborne foot Kick·Dropkick·hand/ledge Grab의 입력 arbitration을 고정했다.
- `AirAttackToken=1`, `KickAnchor_L/R`, Dropkick 단일 AttackAction·dedupe와 non-Down DropkickRecovery를 추가했다.
- Authority Rigidbody·Ragdoll gameplay와 Animator/procedural presentation을 분리하고 root-motion·Animation Event authority를 0으로 고정했다.
- Punch·Kick·Dropkick·Grab·Throw·Weapon의 action/animation matrix와 Animator↔Ragdoll 전환 정책을 정의했다.
- Alpha는 primitive/procedural pose로 기능을 검증하고 C4/ANM production polish가 gameplay 판정을 바꾸지 않게 했다.

### 0.5 0.8.0 변경 요약

- 승인 Catalog를 `PATCH-PROT-001..012`로 확장하고 Character modifier subset 001..008과
  Weapon Supply·Forced Drop subset 009..012의 책임을 분리했다.
- Host-confirmed Weapon hit만 Patch11·12를 발동하며 victim Held/source Weapon Forced Drop이 손·Grab,
  Damage·Ammo·cadence와 Character base profile을 바꾸지 않게 했다.
- `Incoming` Weapon은 Character contact·Pickup·combat·map control에서 제외하고 착지 뒤 `Loose`가 되며,
  Round reset에서 Incoming·Loose·Held와 supply schedule을 함께 지우게 했다.
- 인원별 반복 Supply `START`와 cap이 Character/Weapon 접촉·Camera·2·3·4인 Gate에 포함됐다.

### 0.6 0.7.0 변경 요약

- 승인된 `PATCH-PROT-001..008`을 캐릭터 물리에 연결하되 base profile과 Round 범위
  `PatchModifier`를 분리했다.
- 첫 8개 패치는 Jump, Character-only Pulse, Attack knockback/recoil, Grab throw resistance/grip,
  Ragdoll friction/one-bounce channel만 사용한다.
- 패치가 실제 Character size·Collider·base mass·groggy duration·Hazard timing·Weapon damage를
  바꾸는 경로를 금지했다.
- 패치 기능은 2·3·4인에서 같은 규칙으로 검증하고 전용 Animation·VFX·SFX는 후속 표현 단계로 분리했다.

---

## 1. 고정된 캐릭터 방향

모든 플레이어는 같은 `MasterCharacter`를 사용한다. Mesh, Skeleton, Ragdoll, Collider, mass,
reach와 gameplay Anchor는 플레이어별 외형에 따라 달라지지 않는다.

기본 외형은 다음과 같다.

- 흰색의 매끈한 무안면 이족형 Paint canvas
- 전체 높이의 약 1/5인 비교적 큰 완만한 둥근 타원형 머리
- 배만 불룩한 인상을 줄인 짧고 넓은 몸통
- 낮은 중심이 읽히는 짧고 굵은 다리
- Gang Beasts를 우선 참고한 짧아진 중립 팔 위치
- Party Animals를 보조 참고한 굵고 둥근 연속 limb
- 별도 가시 손 Shape·Mesh·구·손바닥·손가락·주먹이 없는 Forearm terminal
- 별도 Shoe·Toe block 없이 넓게 접지하는 lower-leg terminal

2026-08-15 승인된 `Hybrid Core v0.13`은 큰 시각 방향만 승인한 C1a 자료다. 정면 중립 Pose에서
팔 terminal 최하단이 가랑이 기준선보다 약 `0.04~0.05H` 위에 오는 인상을 C1b에서 다시 확인한다.
이 이미지의 pixel을 Bone, Collider, Anchor, reach 또는 생산 Mesh 치수로 역산하지 않는다.

참고작은 판독성·물리 인상·생산 방식의 축으로만 사용한다. 고유 Mesh, 실루엣, 얼굴, 색 배치,
Material, Animation 또는 수치를 복제하지 않는다. UI 기준 이미지 속 캐릭터도 비율·Rig 기준이 아니다.

---

## 2. 제작 Gate

| Gate | 목적 | 필수 산출물 | 승인 범위 |
|---|---|---|---|
| `C0 Reference Lock` | 참고할 인상과 복제 금지 기록 | reference-role checklist | 참고 방향 |
| `C1a Direction Review` | 큰 체형 방향 확인 | v0.13 front·side·3/4 sheet | 큰 시각 방향만 |
| `C1b Neutral Review` | Pose·FBX 전에 현재 Neutral 조형 승인 | one review object·visually continuous direct head overlap의 front·side·back·3/4 Neutral/Silhouette | `UG-C1B-NEUTRAL`, Pose 제작 입력 |
| `C1b Exact Proportion` | 한 체형의 정확한 수치 승인 | front·side·back·3/4, measurement, Pose, 4인 lineup | C2 Prototype 입력 |
| `C2 Physics` | Rig·Collider·Joint·Ground/Air 행동 검증 | Unity physics Prefab, action/physics profile, capture | 물리 Prototype |
| `C3 Integration` | Camera·Paint·Cosmetic·무기·action presentation 통합 | 2·3·4인 capture, UV/cage/Grip/action report | 통합 Prototype |
| `C4 Production Lock` | 최종 topology·UV·weights·LOD·material 확정 | `.blend`, FBX, Prefab, manifest, 사용자 승인 | Alpha 생산 캐릭터 |

Gate를 건너뛰지 않는다. C2·C3에서 비율이나 reach 문제가 발견되면 Physics 값을 억지로 보정하지
않고 C1b minor revision으로 돌아가 다시 승인한다.

`C1b Neutral Review`는 `C1b Exact Proportion`을 대신하지 않는다. r16 Neutral 방향 승인은 완료됐으며
C1BRW-004는 정적 Pose8·lineup, C1BRW-005는 정적 FBX/Unity parity를 소유한다. 이 단계의 animation claim은0이다.
임시 pose 평가에 기존 r20 리그를 사용할 수 있지만 r16/r20 원본 저장·수정은0이며 결과를 production rig로 승격하지 않는다.
마지막 `UG-C1B`에서 수치·Pose·Unity 결과를 함께 승인한 뒤 CHR-001 게임용 Prefab과 ANP 동작 구현으로 진행한다.

### 2.1 C1b 필수 자료

- 같은 source의 exact orthographic front·side·back·three-quarter
- Neutral, 양손 Grab, 왼손·오른손 Strike 준비, 좌우 foot Kick, Dropkick과 Air hand-reach Pose
- 흰색 기본 상태와 단색 silhouette
- 동일 profile 4인의 overlap·spread lineup
- Crown, Chin, Shoulder, Elbow, Forearm terminal, Chest, Pelvis, Crotch, Hip, Knee,
  lower-leg terminal의 landmark와 width/depth
- `CharacterProportionProfileId/Version`과 수정 전후 비교
- 사용자의 명시적 수치 승인

`H=1.0` normalized profile을 사용하고 profile과 Blockout의 주요 silhouette·landmark 오차는
`0.005H 이하`를 C1b/C2 시작 목표로 검증한다. 이 수치는 이미지 pixel 복제를 요구하는 값이 아니라
동일한 승인 source가 제작 단계에서 바뀌지 않았음을 확인하는 값이다.

### 2.2 Neutral rework 조형 계약

- Head는 faceted 또는 square mass가 아니라 둥근 mass로 읽혀야 한다.
- Visible neck과 별도 목 외형은 `0`이며, Head는 torso 상단에 직접 overlap/attachment되어야 한다.
  이 제한은 외형에 적용한다. 내부 `Chest → Neck → Head` Bone과 그 skin weight는 허용한다(2026-09-05 사용자 설명).
- Torso→shoulder→arm의 visible seam·groove·step·cap·detached boundary는 모든 필수 view에서 `0`이어야 한다.
- Torso는 과도한 egg/pear mass가 아니라 v0.13의 비교적 곧고 부드러운 몸통이어야 한다.
- Crotch는 torso와 양 leg를 잇는 연속 U 형태이며 hip cap이나 분리 leg block이 보이지 않아야 한다.
- Forearm/lower-leg terminal은 별도 hand/foot 없이 둥글게 닫히고 flat disc로 읽히지 않아야 한다.
- Neutral review object는 `1`을 사용한다. r02의 closed component `2`는 seamless body field와 closed round head의
  직접 overlap/attachment를 뜻하며 visible gap·neck·seam을 허용하지 않는다. 이는 production topology·UV·weight를
  미리 확정하는 규칙이 아니라 seam/cap 회피와 조형 검토를 위한 현재 단계 계약이다.

다음 failure class는 새 Neutral Gate에서 명시적으로 거부한다: `FACETED_HEAD`, `EGG_OR_PEG_BODY`,
`EXPOSED_PROXIMAL_CAP`, `DETACHED_LOOKING_LIMB`, `FLAT_TERMINAL_DISC`, `BACKGROUND_THROUGH_HOLE`.
사용자는 four-view 전체를 보고 승인하며, 한 view crop·조명 또는 Pose로 실패를 숨기지 않는다.

---

## 3. Blender → FBX → Unity 계약

| 항목 | 기준 |
|---|---|
| Blender | `+Z Up`, Character forward `-Y` |
| Unity | `+Y Up`, `+Z Forward`, `+X Right` |
| 단위 | `1 Blender meter = 1 Unity unit = 1 meter` |
| Source pivot | Neutral Pose 양발 사이 바닥 접점 |
| Unity root | scale `(1,1,1)`, 음수 scale 없음 |
| Reference toolchain | Blender `5.2 LTS`, Unity `6.3 LTS` |
| Exact patch lock | Plan 2.5 `FDN-010` ToolchainProfile의 실제 설치 version·package lock |

`ModelInteropProfileId/Version/hash` 하나로 Blender export preset과 Unity importer preset을 고정한다.
profile은 위 LTS family와 실제 설치 Blender·Unity patch version, 단위와 축, transform 적용, normals/tangents, Skeleton,
material import와 mesh 처리 설정을 추적한다. 개별 FBX의 수동 rotation·scale·normal 보정으로 문제를 숨기지 않는다.
문서의 `5.2 LTS`·`6.3 LTS` 표기만으로 exact patch를 추정하지 않고 Plan 2.5 `FDN-010`에서 고정한 profile과
project/package lock이 없으면 import parity를 승인하지 않는다.

`ModelInteropProfile-ART-001-r02`는 C1B static Blockout에 한해 좌표계 handedness를 다음처럼 운반한다.

- canonical Blender source의 `+X Right / -Y Forward / +Z Up`과 vertex는 수정하지 않는다.
- export용 transient copy에서만 X를 한 번 반사하고 polygon winding을 함께 뒤집어 outward normal을 보존한다.
- Blender FBX base axis `-Z Forward / +Y Up`에 `bake_space_transform=true`를 적용하고, Unity는
  `bakeAxisConversion=false`로 읽는다.
- Unity imported root와 export root는 identity rotation·scale1·positive determinant를 유지하고
  `+X Right / +Y Up / +Z Forward`가 되어야 한다.
- 이 처리는 versioned `C1BBlockout` asset-class override이며 개별 FBX post-import correction이 아니다.
  Armature·Skinned Character·Weapon·Map에 자동 확장하지 않는다.

C1B Blockout Mesh는 production UV를 만들기 전의 수치·실루엣 검토본이므로 `UV0=0`, tangent stream `0`,
Unity `importTangents=None`을 허용한다. Normals는 계속 `Import`하고 finite/outward 상태를 검사한다. 이 예외는
Paint, normal map, production Material이나 shipping topology 준비를 뜻하지 않으며 C4 source에서는 끝난다.
전역 `UV0 required`와 authored tangent 계약은 그대로 유지한다.

Blender 5.2 FBX는 생성 시각과 Python-hash UUID metadata 때문에 같은 semantic export의 binary SHA가 매번
같다고 보장하지 않는다. Manifest는 선택된 canonical FBX의 SHA를 기록하고, 재현 Gate는 source/preset hash와
Mesh position·surface topology·landmark·bounds semantic fingerprint를 함께 사용한다. Binary를 사후 patch하거나
vendor exporter를 monkeypatch해 hash만 맞추지 않는다.

첫 Unity 반입은 승인 C1b source와 동일한 네 방향에서 다음을 비교한다.

- silhouette·landmark·bounds 오차 `0.005H 이하`
- ground pivot 오차 `0.5%H 이하` 시작 목표
- 잘못된 vertex·normal, 음수 scale과 축 반전 0건
- Bone 이름·parent와 Bind Pose 일치
- profile hash와 source→FBX→Prefab version 추적 가능

C1B-005에서는 Skeleton·UV·tangent·Material 항목이 위 Blockout 예외 또는 `N/A`인지 명시하고, 대신
Neutral four-view mask bounds drift `0.005H 이하`, source/import geometry signature, L/R landmark와
`+Z Forward`를 직접 검사한다. 정적 Pose의 실루엣·관통·관절 접합은 검토하지만 Animation timing·transition·
deformation·무게감은 Armature/Action이 생기는 ANP/C2 전에는 승인하지 않는다.

불일치하면 Collider나 Joint로 보정하지 않고 source 또는 interop profile을 수정해 다시 반입한다.

---

## 4. Prefab·Rig·물리

```text
CHR_MasterCharacter
├── CharacterRootState
├── SimulationBody
│   ├── PhysicsRoot
│   ├── CoreBodies
│   ├── HandBodies
│   └── SecondaryRagdollBodies
├── VisualRoot
│   ├── SharedMesh
│   ├── Armature
│   ├── PaintableSurface
│   └── CosmeticAttachmentCage
├── GameplayAnchors
├── CosmeticRoot
├── ActiveSubjectBounds
└── Presentation
```

Simulation과 Visual을 분리한다. Squash & Stretch, secondary motion과 render 보간이 Authority
Collider, Root, Anchor 또는 접촉 판정을 바꾸면 실패다.

권장 Runtime은 `권한 gameplay action + Rigidbody/Joint physics + read-only animation presentation`의
Hybrid다.

- AuthorityHost의 `SimulationBody`가 PhysicsRoot transform·velocity, Joint target, Contact, Hit,
  Grab constraint, Throw·Jump·Dropkick·Firearm recoil impulse/torque, authority Muzzle과 Ragdoll을 판정한다.
- `VisualRoot`의 Animator·procedural pose·IK는 Host semantic action phase와 보간된 physics anchor를
  따라가며 gameplay Rigidbody·Collider·Anchor를 역으로 구동하지 않는다.
- locomotion, Punch, Kick, Dropkick, Grab, Throw, Weapon과 GetUp clip은 in-place가 원칙이다.
  전용 Jump/착지 clip은 제작하지 않는다. Unity 물리·실제 접촉과 procedural 하체 pose로 표현한다.
  `Animator.applyRootMotion` 또는 동등한 root transform 적용이 Authority PhysicsRoot를 이동시키는 경로는 0개다.
- Animation Event는 Audio/VFX cue를 요청할 수 있지만 Attack active window, sweep, Hit, Grab, Release,
  Shot, Projectile, recoil/spread, impulse, Down과 GetUp 완료를 확정하지 않는다.
- 완전 수동 Ragdoll contact만으로 의도 행동을 추론하지 않는다. Host action phase가 Rigidbody motor·joint
  target과 sweep 자격을 열고 실제 physics 결과가 최종 위치와 Contact를 만든다.

필수 Bone은 Root, Pelvis, Spine, Chest, Neck, Head, 좌우 Clavicle·UpperArm·Forearm·논리 Hand,
Thigh·Calf·Foot다. Finger·Toe bone은 만들지 않는다. helper bone은 deformation 시험이 필요성을
증명할 때만 추가한다. Vertex당 최대 4 Bone weight를 Prototype 시작 제한으로 둔다.

Collider는 승인 C1b profile 하나에서 도출한다.

- Pelvis, Chest, Head, UpperArm, Forearm, Thigh, Calf
- 가시 손이 아닌 `InvisibleTerminalContact`
- 별도 가시 발이 아닌 `LowerLegGroundContact`
- Dynamic MeshCollider 금지
- 일반 Pose의 visual↔Collider 표면 차이 `0.03H 이하` 시작 목표
- terminal↔Strike/Grab Anchor 지속 오차 `0.015H 이하` 시작 목표
- lower-leg terminal↔KickAnchor 지속 오차 `0.015H 이하` 시작 목표

전체 질량은 30kg을 첫 C2 시작값으로 하며 모든 플레이어에게 동일하다. 이 base mass는 승인 Patch12가
변경하지 않는다. 세부 질량·Joint spring,
damper와 limit는 `CharacterPhysicsProfile`에서 비교하고 승인 전에는 `START`다. Pose 모양보다 solver
안정성, 비폭발적 Joint와 재현 가능한 Host 판정을 우선한다.

---

## 5. 공통 이동 행동

Lobby와 Match는 같은 locomotion, Jump, Sprint, Punch, Air Kick·Dropkick, Grab, Ragdoll과 GetUp 동작을 사용한다.
화면 축 기준 WASD로 이동하고 이동 방향으로 자동 회전한다.

### 5.1 Sprint

- `Left Shift`를 누르고 있는 동안 Sprint를 요청한다.
- Sprint는 Lobby와 Match에서 동일한 캐릭터 행동이다.
- release 즉시 기본 이동으로 돌아간다.
- stamina, 소모 meter, 재충전과 피로 상태는 Alpha 범위에 두지 않는다.
- Sprint multiplier는 `MovementTuningProfile`의 Alpha `START` 값이며 2·3·4인 이동·Camera·맵
  측정 뒤 승인한다.
- Stunned, Ragdoll, Recovering, Customizing과 입력 잠금 상태에서는 Sprint가 적용되지 않는다.
- Sprint가 Grab strength, weapon damage, mass 또는 Collider를 암묵적으로 바꾸지 않는다.
- Sprint 추가로 Spawn 접촉 시간, edge 도달 시간, Camera Dolly와 Hazard 회피가 깨지면 multiplier와
  맵 수치를 함께 비교하고 한쪽만 몰래 보정하지 않는다.

### 5.2 Ground·Airborne 손 입력과 Air Attack

Grounded에서는 LMB/RMB가 해당 손의 행동을 결정한다.

- `GrabHoldThreshold` 전 quick release는 왼손/오른손 Punch다.
- threshold까지 hold하면 같은 손의 GrabSeek이며 Punch는 0이다.

Airborne이며 DownEpisode가 아닐 때는 같은 입력을 다음 순서로 중재한다.

1. 각 down edge를 `AirIntentPending`으로 기록한다.
2. 좌우 down edge가 `DualClickChordWindow` 안에 들어오면 두 번째 edge에서 두 Pending을 하나의 Dropkick으로 즉시 commit·소비한다.
3. quick release는 chord window가 닫힌 뒤 같은 쪽 foot Kick으로 확정한다.
4. Grab threshold까지 hold한 손은 commit되지 않은 single Kick 후보를 취소하고 hand/ledge GrabSeek로 확정한다.
   이미 commit된 Dropkick은 이후 hold로 rollback하지 않는다.

`DualClickChordWindow` 후보는 `60/80/100ms`, `START=80ms`다. Ground Punch/Grab의 threshold 후보
`120/150/180ms`와 별도 tuning 축으로 기록한다.

- Host는 각 down edge에서 Ground/Air context를 고정한다. Ground에서 시작한 press가 takeoff 뒤 Kick으로
  바뀌지 않고, Dropkick은 두 edge가 모두 Airborne·non-Down일 때만 성립한다.
- Ground를 떠나 시작한 non-Down Air episode는 `AirAttackToken=1`을 가진다.
- 첫 유효 Kick 또는 Dropkick이 token을 소비한다. 같은 Air episode의 이후 quick tap은 공격을 만들지
  않지만 hold Grab·ledge Grab은 가능하다.
- Grounded, GetUp 완료 또는 Round reset이 token을 1로 복원한다. DownEpisode 중에는 Air Attack을 시작하지 않는다.
- 왼쪽/오른쪽 Kick은 각각 `KickAnchor_L/R`의 Host-authoritative sweep을 사용한다.
- Dropkick은 두 KickAnchor가 하나의 `AttackActionId`를 공유하며 같은 Target을 한 번만 Hit한다.
- Host만 Attack phase, sweep, contact eligibility, Hit, Damage·Knockback과 impulse를 확정한다. passive foot
  collision, Animation pose와 Client sweep 주장은 Attack Hit가 아니다.
- Dropkick은 bounded forward impulse, reduced steering과 기본 Kick보다 강한 승인 knockback을 가진다.
  종료 뒤 별도 `DropkickRecovery`에서 bounded physics tumble을 허용하되 DownEpisode·DownCount와
  `TRG-DOWN-EPISODE-START`는 0이다. Grounded가 token을 복원해도 recovery 종료 전 새 Attack은 0이다.
- Kick·Dropkick Hit는 `TRG-ATTACK-HIT-CONFIRMED`의 SourceKind가 되고 Patch03은 Action·Target당,
  Patch04는 AttackAction당 최대 한 번 적용된다. `TRG-WEAPON-HIT-CONFIRMED`는 만들지 않는다.
- W1 WeaponUse binding은 승인된 Air L/R quick-tap Kick과 L+R chord Dropkick을 덮어쓰지 않는다.
  Airborne WeaponUse를 허용할지, 별도 입력과 Kick/Dropkick 대비 우선순위는 `UG-W1` 결정으로 남긴다.

### 5.3 Ragdoll·GetUp·반복 Down

`CharacterMotionState`는 Locomotion, Stunned, Ragdoll, Recovering을 구분한다. Ragdoll은 최소
face-up, face-down, left, right·대각선·임의 yaw·경사면에서 시험한다. stable support 또는 clearance가
없으면 기다린다. Left/Right는 가까운 FaceUp/FaceDown으로 연속 정렬한 뒤 같은 GetUp을 쓰며
좌우 전용 GetUp clip은0이다. 정렬 시작 시 방향을 고정해 중간 sample의 작은 흔들림으로 경로를 바꾸지 않는다.

같은 Round에서 반복 down을 다음처럼 누적한다.

```text
Round 시작: DownCount = 0
새 down/Ragdoll 진입:
  DownCount = DownCount + 1
  Duration = min(MaxDuration, BaseDuration + IncrementDuration × (DownCount - 1))
다음 Round 시작: DownCount = 0
```

따라서 첫 down은 BaseDuration, 이후 down/groggy는 점차 길어지고 cap을 넘지 않는다.
`BaseDuration`, `IncrementDuration`, `MaxDuration`은 Alpha tuning `START` 값이다.

- 하나의 연속 Ragdoll 안에서 상태 callback이 반복돼도 DownCount는 한 번만 증가한다.
- GetUp 뒤 새 유효 down/Ragdoll에 들어갈 때 다시 증가한다.
- Host가 DownCount와 적용 duration을 판정하고 모든 Peer가 같은 값을 표시한다.
- Round reset에서 반드시 0으로 돌아가며 Match score와 혼동하지 않는다.
- down 증가가 영구 행동 불능이나 무한 stunlock을 만들면 cap·impact threshold·recovery를 조정한다.
- Lobby Ragdoll은 항상 BaseDuration을 사용하고 Match Round의 DownCount를 만들거나 증가시키지 않는다.

[넉다운 상태 local 검토](../artifacts/reports/R20_DOWN_STATE_REVIEW_20260906.md)는 위 시간/누적 규칙과 행동 잠금·all-up 재무장·generation/episode 검사를 연결한다.
대기 시간 만료는 GetUp 시작 허용이며 실제 지지/공간·정렬·완료 전 잠금은 유지한다. 실제 Ragdoll body/pose와 피격→Down 판정은 후속이다.
검사용 timing profile은 기본 게임 설정이 아니다. 사용자 후속 결정으로 **던진 뒤 일반 충돌의 추가 Damage·피해 누적은0**이다.
예: 공격 적중→잡기/던지기→벽→바닥→재충돌에서 후속 일반 접촉마다 피해를 더하지 않는다. 기존 속도·중력·충돌 물리 반응은 유지한다.
충돌 세기를 자동 피해/피해 기반 Down 누적의 대체 입력으로 쓰지 않는다. 공격별 정확한 수치와 피해 없이 발생하는 일반 충돌의 순수 물리 Down은 미확정이다.

2026-09-09 확정 정책: [빠른 펀치·부위별 누적 후보](../artifacts/reports/R20_FAST_PUNCH_PRECISION_20260909.md)는 몸통·팔다리1점, 머리2점으로 누적한다. 넉다운 기준은10→9→8→7→6점에서 멈추며 기존 groggy 대기 시간 증가도 유지한다. 누적은 시간이 지나도 감소하지 않고 새 Down에서 소비하며, Round reset은 누적과 DownCount를 초기화한다. Down 중 추가 타격은 물리 반응만 적용하고 점수·Count·timer를 늘리지 않는다. 머리 보너스는 실제 머리 접촉에만 적용한다. 기존 항상5회 고정은 이전 비교 정책이다.

2026-09-09 [광배·옆구리 가중치 후보](../artifacts/reports/R20_LAT_WEIGHT_ADJUSTMENT_20260909.md)는 몸통의 어깨/상완 영향 일부를 기존 Spine/Chest 슬롯으로 옮긴다. 사용자 후속 진행 지시로 다음 로컬 시제품과 현재 검토 메뉴의 기준으로 채택했으며 원본 source·이전 transport·하체 가중치·리깅·동작 곡선을 보존한다. 펀치 속도와 피격/회복 정책 변경은 없다.

2026-09-09 [공용 카메라·화면축 이동 시제품](../artifacts/reports/R20_SHARED_ARENA_REVIEW_20260909.md)은2·3·4명과16:9/16:10/21:9에서 authority Root의 고정 범위를 사용한다. fixed yaw, pitch34도/FOV42도/Dolly8~15CU/여백15%를 START로 검토하며 local 화면축 WASD·개발용F1~F4 조작 전환·Esc 비정지 입력 중단·전체 review reset을 연결한다. 실제 PC별 동시 조작, peer 목표 전송, 최종 P00/occlusion·hazard framing·UG-CAM과 정식 InputAction/Match UI는 후속이다.

2026-09-10 사용자가 비교 영상의 [공용 카메라 보행 후보](../artifacts/reports/R20_CAMERA_GAIT_CANDIDATE_20260910.md)를 더 자연스럽다고 승인했다. 해당 설정을2·3·4인 Shared Arena의 기본 보행으로 채택한다. 당시의 속도0.70/1.05m/s와 주기2.4/3.6Hz는 이후 아래의2배 이동 속도 승인으로 갱신됐다. `Open Shared Arena 2/3/4 Players`가 채택 보행을 사용하며, `Open Previous Shared Gait 2/3/4 Players`는 이전 보행 비교를 제공한다. 고정60Hz 연산과 snapshot 보간을 유지하며, 원본 mesh/rig/weight와 최종 캐릭터 Gate 상태는 유지한다.

2026-09-10 프레임 독립성 요구를 반영해 [고정 시점 연산·렌더 보간 검증](../artifacts/reports/R20_FRAME_INDEPENDENCE_REVIEW_20260910.md)을 추가한다. 로컬 prototype 입력·이동·손 동작·Down/GetUp·보행 및 카메라 상태는 Physics60Hz에서 갱신하고, 렌더는 이전/현재 snapshot만 보간한다. 마우스 tap/hold는 원본 event timestamp를 사용한다. 렌더된 bone/root를 다음 물리 진입값으로 읽지 않는다. 서로 다른 렌더 일정에 같은 고정 입력을 재생해 gameplay 결과와 trajectory를 비교하고, 실기기 성능·peer 전송 검증은 해당 후속 Gate에서 수행한다.

2026-09-10 사용자가 [2배 이동 속도](../artifacts/reports/R20_MOVEMENT_SPEED_REVIEW_20260910.md)를 기본으로 선택했다.2·3·4인 Shared Arena는 걷기1.40m/s·달리기2.10m/s·가속도20m/s²와 검증한 보행 주기4.8/7.2Hz, 발 들림16mm·골반 bob12mm를 사용한다. 기본 factory의 고정60Hz·FPS 독립성과 잡고 이동/점프/Throw를 재검증했다. 사방 분산 시 기존 카메라 한계에 더 빨리 닿는 점은 맵/카메라 통합에서 계속 확인한다.

2026-09-10 사용자가 [8.8×6.2CU 경기장·최대22CU 카메라](../artifacts/reports/R20_ARENA_BOUNDARY_REVIEW_20260910.md)를 기본 시제품으로 채택하고 가장자리 복귀 진행을 승인했다. 동일 geometry의2/3/4 Spawn, 실제 core Collider OOB·같은 tick의 camera 제외·강제 grip 해제/초기화를 검토했다. 손 접촉 Ledge Grab·ClimbAssist, Hazard와 Round/peer 통합은 후속이며 최종 P00/Camera Gate 완료는 아니다.

2026-09-10 후속 [가장자리 복귀 시제품](../artifacts/reports/R20_LEDGE_RECOVERY_REVIEW_20260910.md)을 기본2/3/4인 장면에 연결했다. 정적 Lip/Facade의 실제 손 접촉과 클릭 유지, Space 보조(공중당2회·350ms), 하중 피로·놓기·재잡기·OOB/Reset을 검토한다. 상승은 옥상 위0.15CU를 목표로 필요한 만큼만,1회 최대1.25CU인 START다. 공중 벽 마찰에 의한 무입력 매달림을 제거하고 지상 마찰은 유지한다. 움직이는 표면·Beam·Hazard·Round/peer와 최종 체감 Gate는 후속이다.

2026-09-10 후속 [하중에 반응하는 매달림 자세](../artifacts/reports/R20_HANGING_LOAD_REVIEW_20260910.md)를 정적 가장자리 장면에 추가했다. 실제 손별 지지력을 읽어 한 손은 잡은 어깨 아래로 골반을 기울이고, 양손은 가운데로 처지며 힘을 나눈다. 손 전환·놓기·상승 시 고정60Hz에서 감쇠해 이어지며, 표면 접촉은 IK로 유지한다. 이 자세는 절차적 표현이고 기존 지지력·피로·도달 판정·복귀 예산은 그대로다. 원본 모델·리그·weight를 수정하지 않았으며 사용자 체감 확인은 남아 있다.


2026-09-08 이전 선택: [고정 타격·무력 Down 후보](../artifacts/reports/R20_FIXED_HIT_LIMP_DOWN_REVIEW_20260908.md)는 필요한 타격 수를 고정한다. 이전 임계값 감소 방식은 비교 이력으로 남기고 현재 profile/API에서 제거한다. START는 매번 펀치5회이며, DownCount는 기존 대기 시간 증가에만 사용한다. 피격 누적은 시간으로 줄지 않고 새 Down에서 소비한다. Down 재피격은 impulse만 적용하고 timer/Count를 늘리지 않는다. 넉다운 중에는 관절의 자세 drive와 authored animation/blend를 적용하지 않고 실제 물리 뼈를 그대로 표시한다. 팔/다리의 기존 관절 연결과 검증된 회전 한도는 유지한다. 회복 중 지지/공간을 잃어 다시 Down 대기로 돌아갈 때도 clip fade 없이 즉시 물리 pose를 따른다. 기존 GetUp clip·회복 gate·기립 후 handoff 보정은 유지한다.

후속 사용자 요청으로 [12 body 몸통 분할 후보](../artifacts/reports/R20_ARTICULATED_TORSO_REVIEW_20260908.md)를 추가한다. 골반 역할 root3kg + Spine2.5kg + Chest2.5kg으로 기존 몸통8kg을 분할하고 전체20kg을 유지한다. Head/Arm은 Chest에 연결하며 actual chest rotation으로 회복 방향을 판정한다. 이는 원본 mesh/weight/rig 변경이 없는 별도 physics 후보이며 기존10 body 기본 구성과 구분한다.

(2026-10-20 대체: 잡힌 채 회복하지 않고 버둥거린 뒤 빠져나온다. 상단 2026-10-20 결정 참조.) 2026-09-08 사용자 결정: [분할 래그돌 잡기/회복 연결](../artifacts/reports/R20_RAGDOLL_GRAB_INTEGRATION_20260908.md)은 잡힌 상태에서도 실제 지지/공간이 유효하면 회복하고 잡기를 유지한다. 부위별 접점과 전체20kg 하중을 구분하며, 점프/Throw 보정은 같은 캐릭터의 모든 동적 body에 중복 없이 분배한다. 충돌체 교체 시 현재 world 접점을 이어 붙이며 Reset/진짜 접촉 상실은 기존 강제 해제를 따른다. 이미 서 있는 자세는 timer 만료 뒤 물리 자세 안정화로 복귀한다. 외부 잡기 중 회복 표현은 물리 접점을 우선하고, 해제 시 현재 GetUp 시각으로 보간한다.
명시적 Hazard/OOB 판정은 기존 계약을 따른다. 이 결정은 던져진 캐릭터에게 별도 공격이 적중하는 것까지 무효로 만들지 않는다.
[실제 지지면·공간 query](../artifacts/reports/R20_RECOVERY_SUPPORT_REVIEW_20260906.md)는 몸-바닥 간격, 상대 선속도/각속도와 지지 유지 시간을 확인하고, 기립 capsule의 전체 공간을 검사한다.
타이머 만료와 실제 query를 모두 만족해야 회복을 시작하며, 정렬/GetUp 중 지지·공간 상실은 같은 episode의 재평가로 돌아간다. query 자체의 위치/회전 변경·GetUp 자동 완료는0이다.

### 5.4 Base profile과 Round-scoped PatchModifier

`MovementTuningProfile`, `CharacterPhysicsProfile`과 `PhysicsTuningProfile`은 승인된 캐릭터
baseline을 소유한다. 활성 패치는 이 profile 자체를 덮어쓰지 않고 현재 `RoundGeneration`에 묶인
`PatchModifier`로만 적용한다.

- Round 시작은 모든 임시 modifier·lease·발동 cache를 지운 뒤 active Patch를 정해진 순서로 다시 등록한다.
- Round 종료, Scene 전환과 stale generation event는 현재 Round의 modifier를 남기지 않는다.
- AuthorityHost만 Trigger와 modifier 결과를 확정하고 Guest는 같은 결과를 표시한다.
- 모든 패치는 전원에게 같은 규칙으로 적용하며 특정 사용자를 작성자가 지정하지 않는다. 실제 대상은
  Trigger actor, hit victim, grab target처럼 권한 event 문맥에서 정한다.

승인 Patch12 중 Character modifier를 소유하는 subset은 `PATCH_DESIGN.md` 0.5.0의
`PATCH-PROT-001..008`뿐이다.

| Patch | Character channel | 경계 |
|---|---|---|
| `PATCH-PROT-001` | 승인된 일반 Jump의 수직 impulse modifier | `ClimbAssist`와 강제 launch에는 적용하지 않음 |
| `PATCH-PROT-002` | Jump당 한 번의 Character-only radial pulse | 다른 생존 Character만 대상; prop·Weapon·map control·Hazard 제외 |
| `PATCH-PROT-003` | `TRG-ATTACK-HIT-CONFIRMED`의 victim knockback modifier | damage와 Down duration 불변 |
| `PATCH-PROT-004` | 같은 Attack event의 attacker recoil | 한 Attack action에서 중복 recoil 없음 |
| `PATCH-PROT-005` | Player Grab target의 lift/throw resistance modifier | 실제 Rigidbody base mass와 Collider 불변; 같은 Grab relation 중복 없음 |
| `PATCH-PROT-006` | Player Grab relation의 grip modifier | GripStress·break 경로 유지; ledge·prop·Weapon Grab 제외 |
| `PATCH-PROT-007` | 현재 Down episode의 Ragdoll ground friction modifier | GetUp·reset에서 제거; groggy duration 불변 |
| `PATCH-PROT-008` | 새 Down episode당 한 번의 bounded bounce | 직접 Lethal/OOB 뒤 발동 0; DownCount 추가 증가 0 |

승인 Patch12에서 실제 Character scale·Collider dimension·base mass, groggy `BaseDuration`·`IncrementDuration`·
`MaxDuration`, Hazard timing과 Weapon damage를 변경하는 PatchModifier는 0개다. Patch 전용
Animation·icon·VFX·SFX도 기능 승인 조건이 아니다. Runtime은 안정적인 Patch ID와 의미 발동 event를
표현 port로 내보내기만 하며 후속 표현 subscriber가 Authority state를 바꿀 수 없다.

`PATCH-PROT-009..012`는 Character tuning channel을 새로 만들지 않는다.

| Patch | Character·Held relation 경계 |
|---|---|
| `PATCH-PROT-009..010` | Supply가 만든 `Incoming` Weapon은 Character contact·Pickup·Hit·Camera subject가 아니며 착지 뒤 `Loose`부터 기존 Weapon relation을 사용한다. |
| `PATCH-PROT-011` | Host-confirmed Weapon hit victim의 모든 Held Weapon Instance를 기존 강제 Release 경로로 Drop한다. Main·Support가 같은 Instance면 한 번이며 대상이 없으면 `NoEligibleTarget`이다. |
| `PATCH-PROT-012` | 같은 Host-confirmed hit에서 attacker가 사용했고 Effect 시점에도 같은 attacker가 Held 중인 source Weapon Instance 하나만 기존 강제 Release 경로로 Drop한다. |

강제 Drop은 Hand Input, Punch, Grab, Weapon Hit와 새 Patch Trigger를 합성하지 않고 Character의 Damage,
DownCount·groggy, base mass, reach와 Collider를 바꾸지 않는다. 승인 Patch12는 같은 Trigger를 공유하는
두 Patch를 모두 상호 배타로 취급해 retained active set에서 Trigger당 Instance를 최대 하나만 허용한다.

각 조합은 2·3·4인에서 동일 tuning으로 Trigger 1회성, 대상 결정, 중첩 상한, Round reset과 Peer 수렴을
각각 검증한다.

---

## 6. 손·Grab·Grip

왼손과 오른손은 독립된 상태를 가진다. 아래는 Grounded hand sequence다.

```text
Press → HandIntentPending
threshold 전 Release → HandStrike 1회 → Recovering → Idle
threshold 통과 → Strike 없이 GrabSeek
GrabSeek + valid contact → Grabbing
동적 대상의 마지막 본인 손 Release → constraint 해제 + bounded Throw 1회 → Idle
같은 대상의 다른 본인 손이 유지됨 → 해당 손 constraint만 해제 → Idle
미획득/강제 취소 → 추가 impulse 없이 해제 → Idle
```

`GrabHoldThresholdMs`는 120/150/180ms를 비교하고 150ms를 `START`로 사용한다. Pending에서 hit를
만들지 않고 같은 입력에서 Strike와 Grab을 함께 만들지 않는다.

Airborne quick release는 이 `HandStrike` 분기를 사용하지 않고 5.2의 Kick 후보가 된다. Air hold가
threshold를 넘긴 시점부터만 같은 GrabSeek·Grabbing 경로를 재사용하며 그 press의 Kick은 0이다.

`PATCH-PROT-005..006`은 새 Player-to-Player Grab relation이 권한 확정된 뒤에만 해당 relation의
throw resistance 또는 grip channel을 승인된 bounded lifetime 동안 수정한다. 같은 Grab relation의
반복 contact로 magnitude를 누적하지 않으며 Release·Break·Target 상실·elimination·expiry·Round reset
중 먼저 성립한 시점에 제거한다. Character base mass·Collider, Ledge Grab, map control, prop과 Weapon
Grip에는 이 modifier를 재사용하지 않는다.

Gameplay Anchor는 CharacterForward, Hand L/R, Strike L/R, Grab L/R, WeaponHand L/R,
Kick L/R, DropkickReference와 Nameplate를 가진다. 가시 손 대신 Forearm 방향과 terminal 내부 Anchor가
공격·잡기·무기 방향을 정하고 lower-leg terminal의 KickAnchor가 Air Kick sweep을 정한다. 유효 Ledge를
한 손 이상 잡고 Jump했을 때만 제한된 `ClimbAssist` 후보가 된다.

### 6.1 Authority Action과 Animation Matrix

| Gameplay context | Host semantic action·physics | Presentation |
|---|---|---|
| Locomotion·Jump | 권한 movement motor, `JumpAccepted`와 Root impulse·Ground/Air state | in-place Idle·Walk·Sprint, 전용 Jump clip0; 수직 속도·실제 착지 충격을 따르는 procedural 하체 pose |
| Ground L/R tap | `HandStrike_L/R`, StrikeAnchor active sweep·contact, Action·Target dedupe | 해당 손 in-place Punch pose |
| Ground/Air L/R hold | `GrabSeek_L/R` → 권한 Hand contact·constraint | Hand reach·brace IK, constraint를 따라가는 pose |
| Air L/R quick tap | `AirKick_L/R`, 해당 KickAnchor sweep, AirAttackToken 소비 | 좌/우 foot Kick pose |
| Air L+R chord | 단일 `Dropkick`, 양발 shared Action, forward impulse·reduced steering | 양발 Dropkick pose와 non-Down recovery tumble |
| Held Grab·release Throw | 권한 constraint, 마지막 손 release와 bounded Throw impulse | Grab·Throw pose, Hand/Target anchor follow; 별도 Lift 조작0 |
| Pistol semi-auto | 권한 single Shot, Ammo-1, Projectile·strong bounded Rigidbody/Joint recoil | Main arm을 앞으로 뻗은 단순 Grip pose; authored fire/recoil clip 0, physics로 팔·Weapon 상승/복귀 |
| LongGun full-auto | 권한 hold cadence, ShotSequence, Ammo-1, RecoilAccumulator·SpreadBloom | Main+Support arm을 앞으로 뻗은 단순 Grip pose; authored fire/recoil clip 0, physics로 누적 상승/복귀 |
| Bat·Hammer Melee | 권한 WeaponAction·Weapon sweep·Hit | in-place swing·impact pose |
| Ammo 0 | 권한 forced release→SpentPendingCleanup→remove | spent release cue; pickup/reload pose 0 |
| DownEpisode | 권한 Down transition·Ragdoll bodies·DownCount | Animator에서 physics Ragdoll pose로 blend |
| GetUp | Host clearance·orientation·완료 state | front/back/left/right 진입, side 정렬→공통 face-up/down in-place GetUp, AuthorityRoot follow; Down/Dropkick state 분리 |

Animation clip과 procedural pose는 semantic phase의 결과다. Clip frame, foot/hand visual overlap 또는
Animation Event가 active sweep과 constraint를 열지 않는다.

### 2026-09-05 펀치 시각 피드백

후속 r02(준비0.10초)의 동작 모양·준비 속도는 사용자 시각 승인 완료다.
[승인 기록](../artifacts/reports/R20_PUNCH_VISUAL_APPROVAL_20260905.json)을 기준으로 유지하며 gameplay timing/통합 Gate와 구분한다.

후속 요청은 팔꿈치를 접는 준비만 빠르게 하는 것이다. 현재 r02 검토는 준비0.20→0.10초이며
확장·회수의 pose/속도를 유지한다. 수치는 animation review START이고 gameplay cadence를 바꾸지 않는다.

기존 검토용 펀치는 때리는 느낌이 부족하다는 사용자 피드백으로 시각 재작업한다.
팔꿈치를 충분히 접어 준비하고 어깨의 작은 선행 동작 뒤 전완을 빠르게 펴며, 뻗은 상태의 정지를 줄이고
다시 팔꿈치를 접어 회수한다. 좌우는 같은 원리로 만든다. 총기 Grip과 별도 동작이며 Grab/Wall은 유지한다.
변경은 animation presentation에 한정하고 Hit·피해·공격 cadence·입력 threshold·gameplay Root Motion을 바꾸지 않는다.
실제 구간 시간과 관절 각도는 검토 START이며 게임용 Joint limit이나 최종 combat timing 승인이 아니다.

### 6.1.1 행동별 시뮬레이션·애니메이션 제작 체크리스트

2026-09-05 후속 검토에 따라 같은 r20 검토 리그와 공통 검증 환경에서 행동별 시나리오를 분리한다.
아래의 `정의`는 제작·시험 범위이며 이미 실행했거나 완료했다는 뜻이 아니다. Blender pose/cycle 검토와
Unity 물리 접촉·게임 상태 검증을 별도 결과로 기록한다. 물리 결과·입력 판정은 공통 Authority 구현을 사용한다.

| 시나리오 | 확인할 전 과정·조합 | 현재 확인 수준 | 구현 소유 |
|---|---|---|---|
| Idle·Walk·Sprint | 시작·한 주기·정지·걷기↔뛰기, 손/발 반대 위상, 발 접지·미끄러짐 | 속도·주기를 유지한 ReadableGait로 발 들림·보폭·골반 체중 이동 재작업. Walk39/Sprint25frame 전 주기 표면 검사와 점프 복귀 검토; 월드 발 고정·최종 tuning/게임 통합은 후속 | CHR-003..004, ANP-002 |
| Jump·Landing | 도약·상승·정점·낙하·착지, 이동 중 점프, 낙하 높이에 따른 접지 | 전용 clip0 물리 시제품에 8방향·공중 반전/90°/정지·착지 후 이동/Sprint 전환 추가. 최종 지형 접지·다관절/게임 상태 통합은 후속 | CHR-005, ANP-002 |
| Punch L/R | 준비→팔 뻗기→권한 active 구간→복귀, 적중/빗나감, 이동 중·반대 손 Grab 중 공격 | 승인 Action·local 권한 tap/hold·sweep/impulse와 Grab 반대 손 조합 검토 완료. 피해/Down·최종 게임/peer 통합 후속 | CHR-006, ANP-002 |
| Grab L/R/Both | 손 뻗기·획득·유지·버티기·끌기·놓기·강제 해제, 물체/다른 Player 및 서로 다른 대상 | local 실제 contact/joint·하중 피로·팔 IK·좌우/양손·서로 다른 대상·동적 prop·해제/상실 검토 완료. 최종 physics profile·게임/peer 통합 후속 | CHR-006..007, ANP-002 |
| Grab release·Throw | 한손/양손 잡기 유지, 마지막 손 해제·던지기·복귀, 대상 상실/중단 | local 클릭 유지 Grab→자동 상향 들기(상호 잡기 차단)→마지막 손 release Throw 연결. 별도 Lift 입력/선행 조건0; 명시적 Lift API는 독립 검토용으로 보존. 전용 Throw 연출·통합은 후속 | CHR-007, ANP-002 |
| Wall·Ledge·Beam | 실제 surface를 한손/양손으로 붙잡기·매달리기·흔들림·손 하나 놓기·완전 해제·추락, Jump ClimbAssist 복귀 | 정적 Lip/Facade 한손/양손 실제 접촉·하중 피로·하중 반응 자세·물리 보조2회/350ms와 복귀/해제 검토; 움직이는 표면·Beam·정식 통합 후속 | CHR-011, ANP-002 |
| AirKick L/R·Dropkick | 준비·공격·복귀, 착지/빗나감/가장자리·벽 충돌, non-Down recovery | 정적 공격 자세만 존재 | AIR-001..002, ANP-002 |
| Down·Recovery | 앞/뒤/옆·대각선·임의 yaw·경사면, side 정렬→앞/뒤 공통 GetUp, 공간 부족/재피격/중단 | 방향 판별·정렬 target·state 시험만 실행; 실제 GetUp clip·ragdoll 접촉 미구현 | CHR-008..009, ANP-002..003 |
| 공통 Firearm Grip | template1을 L/R/LR에 적용, 이동 중 잡은 팔 유지·빈 팔 보행, physics recoil·forced release | 공통 팔 뻗기 변형 검토만 실행; Weapon physics 미구현 | FIR-003, ANP-003 |
| Bat·Hammer | 준비·swing·권한 impact 구간·복귀, 적중/빗나감/벽 충돌·release | 정의; W1 입력/timing 및 Weapon 결과 대기 | WPN-004..006, ANP-003 |

모든 시나리오에서 들어가기·유지하기·나오기뿐 아니라 Down, 잡기 해제, 대상 제거와 Round reset의
중단 전환을 확인한다. 한 손 Grab 중 다른 손 Punch처럼 좌우 독립 행동도 조합으로 시험한다.

Jump는 Unity Rigidbody·중력·접촉 판정으로 처리하고 수직 속도와 실제 착지 충격에 따라 하체 pose를 계산한다.
애니메이션의 준비 구간 종료를 기다려 `JumpAccepted`의 권한 impulse를 늦추지 않는다.
기존 Blender 1.47초 연결 영상과0.22m 이동 궤적은 비교 자료이며 gameplay 수치가 아니다.
공중·착지 하체 제어는 팔의 Punch/Grab/Grip pose를 덮어쓰지 않는다. Clip frame이 접촉을 판정하지 않는다.
점프 방향과 Ground/Air 전환 때문에 보행 주기를 처음부터 다시 시작하지 않는다. 착지 압축 중에도
이동을 막지 않고 현재 속도에 맞춰 Idle/Walk/Sprint로 연결한다. 공중 다리 접기는 착지 전 바닥 거리와
수직 속도에 따라 미리 풀고, 방향 반전은 실제 수평 속도와 제한된 yaw torque를 따라 연속적으로 처리한다.
보행 팔 swing은 별도 layer이며 사용 가능한 손에만 적용한다. 점유/공격 중인 팔은 쓰지 않는다.
현행 시제품의 Shift 배율1.5, 지상/공중 회전 속도360°/240°/s와 보폭·cadence는 START다.
2026-09-06 초기 재작업에서 보행을 ReadableGait로 만들었다. 발 기준점 들림 약19/21mm,
앞뒤 이동 폭 약81/104mm와 골반 체중 이동을 사용한다. [재작업 기록](../artifacts/reports/R20_READABLE_GAIT_REWORK_20260906.md)을 따른다.
이는 기존 속도/주기를 유지한 표현 수정이며 앞선 빠른 발·느린 이동 후보의 채택이 아니다.
발의 완전한 월드 고정을 위한 [스타일 비교](../artifacts/reports/R20_FOOT_CONTACT_STYLE_REVIEW_20260905.md)는
검토 후보이며 기본 속도/리듬 변경 승인이 아니다. 정속 직진의 낮은 발 기준점 이동 오차만으로
8방향·가감속·점프 전환·접촉면 전체 고정 완료를 선언하지 않는다.
후속 [월드 발 고정 도달 검사](../artifacts/reports/R20_WORLD_FOOT_PLANT_FEASIBILITY_20260906.md)에서 현재 지지 시작점 고정은
Walk약0.083초·Sprint약0.067초 뒤 도달 범위를 벗어났다. 실제 지지 구간은 약0.308/0.193초다.
사용자가2026-09-06 비교 후 속도·발놀림 절충안을 당시 프로토타입 기준으로 채택했다. 아래는 그 시점의 설정이며, 현재 Shared Arena는2026-09-10 승인한2배 속도를 사용한다.
[접지 절충 후보](../artifacts/reports/R20_FOOT_PLANT_CANDIDATE_20260906.md)의 설정은 Walk0.70/Sprint1.05m/s,
한 발 주기3.0/4.5Hz다. 안정된 평지 지지는 월드 발 기준점을 고정하고 전환·공중·Reset에서 해제한다.
정속 고정16조건과 기존 점프 전환18조건을 통과했으나, 발바닥 표면 침범 최대6.8mm와 지형별 보정은 후속이다.
첫 통합 프로토타입의 추격·회피·점프·기본 전투 체감을 사용자와 확인한 뒤 속도/보폭 또는 모델링/리깅 재작업 필요성을 판단한다. 현재 모델·리그를 유지한다.
[승인 펀치 Unity 조합](../artifacts/reports/R20_UNITY_PUNCH_INTEGRATION_20260906.md)은 한 손의 Clavicle/UpperArm/Forearm/HandLogical만 소유한다.
보행 팔 swing은 공격 중인 같은 팔을 쓰지 않으며, 다른 동작으로 소유권이 넘어가면 펀치를 중단한다. 준비0.10초·전체0.70초 유지.
J/K는 독립 표현 검토 명령이고 LMB/RMB tap/hold 계약이나 Authority Hit 판정을 대신하지 않는다.
후속 [손 타격 검토 장면](../artifacts/reports/R20_HAND_STRIKE_REVIEW_20260906.md)은 LMB/RMB150ms tap/hold를 권한 상태에 연결한다.
Pending/GrabSeek는 적중0, Punch active 구간만 별도 root 기준 sweep·Target dedupe·bounded impulse를 수행하며 표현은 권한 age를 읽는다.
Esc 취소·all-up 재무장·Reset과 벽 뒤 차단을 포함한다. GrabSeek는 intent이며 실제 joint·피해/Down·Air attack·최종 입력 map과 network는 후속이다.
그다음 [Grab 물리 시제품](../artifacts/reports/R20_GRAB_PHYSICS_REVIEW_20260906.md)에서 GrabSeek→contact→Grabbing과 실제 joint·Release/Break를 연결했다.
사용자 선택은 **하중이 걸릴 때만 피로 누적**이다. 시간만으로 해제하지 않으며 낮은 부하에서 회복한다. 피로·힘 수치는 START다.
팔은 물리 접촉점을 읽기 전용으로 따라가고, 그립 상실 뒤 held button으로 자동 재획득하지 않는다. 이전 검토에서는 Release impulse가0이었으며, 현재 사용자 결정은 아래 마지막 손 release Throw 규칙으로 대체한다.
동적 하중의 이동 구동력 보완은 최대2배이며 최고속도·base mass·Sprint의 grip strength는 바꾸지 않는다. 놓으면 보완값을 초기화한다.
[들기·던지기 공통 물리 검토](../artifacts/reports/R20_LIFT_THROW_PHYSICS_REVIEW_20260906.md)는 명시적인 권한 요청으로 joint 목표를 천천히 이동하고, 던지기 때 본인 손을 먼저 해제한 뒤 질량별 bounded impulse를1회 적용한다.
후속 사용자 결정은 **클릭 유지로 잡고 버튼을 놓으면 던지기**다. Ctrl와 Lift 완료 조건은 기본 입력에 사용하지 않는다. 초기 자동 들기 제외 결정은 아래 2026-09-11 결정으로 변경했다.
[Release Throw 입력 검토](../artifacts/reports/R20_RELEASE_THROW_REVIEW_20260906.md)에서 유효한 up edge만 던지기를 승인한다.
같은 대상은 마지막 본인 손 해제에만1회, 다른 대상은 각 손별1회이며 다른 플레이어의 joint는 제거하지 않는다.
미획득·오래된/중복 edge·메뉴·Reset·grip/대상/손 제어권 상실·거리 이탈에는 추가 Throw/Strike impulse0이다.
기존 명시적 Lift API는 이전 물리 비교용이며 기본 조작의 필수 단계가 아니다.

2026-09-17 사용자가 서 있는 상대·상호 잡기 후보를 시각 승인해 로컬2인 잡기 검토2단계를 마치고 다음 진행을 요청했다. [물리 팔 펀치·정밀 접촉 후보](../artifacts/reports/R20_PHYSICAL_PUNCH_CANDIDATE_20260917.md)는 승인된 빠른 펀치를 관절 힘으로 따라가며 실제 손끝 접촉으로만 명중을 기록한다. 짧은 클릭/유지 잡기/해제 던지기를 연결하고 머리2점·몸통/팔다리1점을 누적한다. 이 단계는 첫10점의 Down 요청까지이며 실제8→12몸체 Down·회복 전환과 잡기 유지 인계는 아직 연결하지 않았다. 기존 기본 장면과 정식 캐릭터/네트워크 Gate는 변경하지 않는다.

사용자는 이전 빠른 펀치를 ‘툭 건드리는’ 느낌으로 평가해 체감 승인을 하지 않았다. [준비·가격·힘 전달 재수정 후보](../artifacts/reports/R20_PUNCH_FOLLOW_THROUGH_20260917.md)는 팔꿈치를 먼저 깊게 접고 어깨를 준비한 뒤 빠르게 뻗으며, 명중 직후 시간을 건너뛰어 회수하던 동작과 손의 역방향 보조 impulse를 제거한다. 몸통의 작은 물리 목표·실제 손끝의 fixed-step 경로 판정·시작 거리로 고정하는 초근접 직선 궤적을 함께 검토한다. 제자리/이동 연타는 각각 첫10명중을 확인했다. 전체0.4초·머리2/몸통·팔다리1점·잡기 유지/해제 던지기는 유지한다. PlayMode19/19·EditMode146/146 및15/60/144FPS 결과 일치를 확인했다. 접촉 후 표시 피부 간격 최대6.97mm는 보완 대상으로 남기며, 사용자 체감 승인 전 다음 Down·회복 단계로 진행하지 않는다.

2026-09-18 최신 진행: 사용자가 흐물거리는 펀치의 재설계 진행을 요청했다. [1단계 참고 원형](../artifacts/reports/R20_PUNCH_REFERENCE_20260918.md)을 전용 무물리 장면으로 제작하고 정면/측면·좌우 단발을 검토한다. Rigidbody/Collider0, 대칭·접지·원위치 복귀와 공유 로더 경로3/3 검사를 통과했다. 기존 물리 펀치·판정·모델은 유지한다. 원형 사용자 승인 뒤 자세·속도 기반 물리 추종과 실제 피격 반응을 검토하며, 이번 결과로 전투 타격감 또는 통합 승인을 가정하지 않는다.

2026-09-17 사용자가 누운 상대 접촉 후보를 승인하고 다음 진행을 요청했다. [서 있는 상대·상호 잡기 접촉 후보](../artifacts/reports/R20_STANDING_SKIN_CONTACT_20260917.md)에서 양쪽 피부를 고정 물리 상태로 계산하고, 팔끼리의 같은 접촉이 중복 처리되지 않도록 연결한다. 손별 소유권을 유지해 한쪽 release가 상대 잡기를 없애지 않으며, 상호 자동 들기 차단과 유효 마지막 손 release Throw를 함께 확인한다. 별도 메뉴에서 검토하며 원본 모델·기본 Shared Arena·정식 캐릭터 Gate는 변경하지 않는다.

2026-09-17 사용자가 들기 후보를 시각/체감 승인하고 다음 진행을 요청했다. 승인한80% 보조·들기 목표·즉시 놓기 던지기를 기준으로 [손/피부 접촉 보완 후보](../artifacts/reports/R20_SKIN_CONTACT_CANDIDATE_20260917.md)를 별도 래그돌 검토 장면에 추가한다. 렌더 보간과 분리된 고정 자세에서 실제 피부 삼각형과 접점을 계산하고, 손·전완 주변 접촉면이 상대 물리 몸체에 힘을 전달한다. 원본 모델·팔 길이·래그돌 자세 drive는 바꾸지 않는다. 이 범위는 서 있는 상대/상호 잡기·새 물리 펀치·상태 전환·게임 전체 통합 완료를 뜻하지 않는다.

2026-09-17 사용자가 **잡아 들고 있는 동안만 부분 중력 보조**하는 방식을 승인했다. [래그돌 들기·놓기 던지기 후보](../artifacts/reports/R20_LIMP_LIFT_ASSIST_20260917.md)를 B 기반 별도 장면에 연결했다. 대상의20kg·관성·중력 설정과 자세 drive0을 유지하며, 유효 잡기/보유자 지지 조건에서만 대상당 한 번 보조하고 해제·상실·공중 전환에 중단한다. 상호 잡기는 추가 자동 상승을 차단한다. 현재80% 보조·팔 구동력·목표 높이는 체감 검토용 수치다. 이번 승인은 들기/해제 후보 범위이며 손/피부 접촉면 보완 승인이나 전체2단계 완료,3단계 전체 완료를 뜻하지 않는다. 기본 Shared Arena·승인 자산은 자동 교체하지 않는다.

2026-09-16 B 거리·높이 반응형 선택 후 [어깨 보완·낮은 래그돌 잡기 후보](../artifacts/reports/R20_SHOULDER_REPAIR_LOW_GRAB_20260916.md)를 구현했다. 별도 모델은 원래 메쉬와22개 뼈를 보존하고 `ShoulderBlend_L/R`를 더한24개 피부 뼈를 사용한다. 추가 물리 몸체는 없으며 서 있는 캐릭터8개·래그돌 검토 대상12개 몸체/20kg을 유지한다. 어깨 검사와 네 방향의 낮은 잡기·끌기는 통과했지만 손과 피부 간격이 최대 약3.6cm 남았다. 실제 피부를 따르는 국소 접촉면 보완을 제안했고 진행 여부를 확인 중이다. 전체2단계는 미완료이며 피해 기반 Down·잡기 유지 전환·던지기·회복·기믹 통합 완료가 아니다. 기존 승인 자산과 기본 Shared Arena는 유지한다.

2026-09-13 사용자 진행 승인으로 [잡기 접촉·벽 복귀 후보](../artifacts/reports/R20_GRAB_CONTACT_CANDIDATE_20260913.md)를 별도 검토 장면에 구현했다. 작은 손·팔 보정과 벽 매달림 간격/등반 종료 표현을 시험하며 기본 Shared Arena는 기존 승인본을 유지한다. 안정된 매달림은 개선했으나 잡기 직전·등반 전환·상호 잡기의 관통은 미해결이다. 상호 접촉을 손목·전완으로 바꾸는 선택과 벽 접근·복귀 때 빈팔을 위로 접어 피하는 전환 동작은 사용자 답변 대기이며 아직 규칙으로 채택하지 않는다.

2026-09-11 [접촉 공간 보정안](../artifacts/reports/R20_GRAB_CONTACT_CLEARANCE_20260911.md)은 모션을 과도하게 바꾼다는 사용자 지적으로 철회했다. 상호 접촉 높이14cm 분리, 추가 팔 경로/후퇴/빈 팔 자세와 관련 보정은 현재 규칙이 아니다. 직전 승인된 자동 들기·상호 들기 차단·상향 벽 잡기 버전으로 복원했고 관통 문제는 미해결이다. 충돌 수정은 승인된 모션의 형태와 동작 의도를 유지해야 하며, 이를 바꾸려면 적용 전에 사용자 확인이 필요하다.
2026-09-11 사용자가 [자동 상향 들기·상호 잡기 차단](../artifacts/reports/R20_AUTOMATIC_LIFT_REVIEW_20260911.md)을 승인했다. 기본 가장자리 시제품에서 접촉 뒤 클릭을 유지하면 0.65초에 걸쳐 최대 0.30CU 위/0.10CU 안쪽으로 joint 목표를 옮긴다. 양손 동일 대상은 한 번만 이동하고 마지막 손 release는 들기 완료를 기다리지 않는다. A→B와 B→A가 동시에 존재하면 해당 쌍 양쪽의 자동 상승을 멈춘다. 이미 진행된 offset은 그 값에서 멈춰 역방향 joint와 충돌하는 위치 보정을 만들지 않고, 상호 관계가 풀리면 이어서 올린다. 천장·장애물, 실제 하중 피로와 Down/Reset 경로를 함께 사용한다. 정적 벽은 위쪽 0.82CU의 실제 접촉을 찾아 잡고, 상승 중 손이 어깨 가까이 내려오면 접촉을 해제해 복귀로 넘긴다.
사용자 요청으로 [잡기 점프의 무게감 유지 보정](../artifacts/reports/R20_HELD_JUMP_WEIGHT_20260906.md)을 적용했다.
권한 jump 승인 시 유효한 held Rigidbody에75% 이내의 부분 지원을 제공한다. 대상별 지원 비율과 총 추가 운동량 예산을 함께 제한하고 양손 동일 대상은1회만 적용한다.
같은20kg 수동 상대 한 명은 약17.54cm, 둘은 약15.85cm로 solo23.19cm보다 낮다. 중력·질량·기본 JumpHeight0.25m와 모델·리그는 유지한다.
해제/무효 대상·공중 중복 요청에는 추가 보정0이다. 둘 다 직접 점프하는 경우의 협력 상승은 수동 하중 조건과 구분한다.75%는 최종 밸런스 확정이 아닌 현재 START다.
고정된 Grab/Wall 동작 clip만으로 손을 붙여 두지 않고, Host contact anchor·constraint를 표현하는
reach/brace/IK와 물리 추종을 함께 검증한다. 팔 길이를 늘리거나 순간이동해 접촉 오차를 숨기지 않는다.

제작은 prototype pose/clip → 전 주기 변형 검토 → Unity 접촉·상태 전환 통합 → 사용자 시각/체감 확인 순서다.
Unity 동작 영상은 뼈 계산 뒤 실제 player-loop frame을 넘겨 skinned mesh 갱신 후 촬영한다.
배경을 제외한 캐릭터 mask와 현재 CPU pose를 비교한다. 프레임 개수나 바닥 이동만으로
팔다리 애니메이션이 화면에 표시됐다고 판단하지 않는다. [캡처 정정 기록](../artifacts/reports/R20_SKINNING_CAPTURE_FIX_20260906.md)을 따른다.
현행 C1BRW-004/005의 static 범위와 UG-C1B 선행조건은 유지한다. Animation용 Generic import와
r20 hip helper curve bake는 기존 static FBX preset의 `bake_anim=false`로 대체할 수 없다.
총기별 Grip·authored fire/recoil·reload·좌우 전용 GetUp clip은 추가하지 않는다.

### 6.2 Animator↔Ragdoll 전환

- Locomotion·공격·Grab 동안 VisualRoot는 semantic phase와 SimulationBody anchor를 따라가며 root motion은 0이다.
- Host가 실제 DownEpisode를 시작할 때 진행 중 Attack을 닫고 SimulationBody의 현재 pose·velocity에서
  Authority Ragdoll을 시작한다. Visual skeleton은 그 결과로 blend하며 Animator가 Ragdoll body를 되감지 않는다.
- Ragdoll 동안 Host physics body가 위치·회전·contact의 기준이고 VisualRoot는 보간만 한다.
- GetUp은 Host clearance와 face-up/down orientation이 유효할 때만 시작한다. VisualRoot를 AuthorityRoot에
  맞추고 in-place GetUp을 재생하며 완료 state도 Host가 확정한다. Clip root delta로 순간이동하지 않는다.
- DropkickRecovery는 DownEpisode 전환을 사용하지 않는다. 별도 action/recovery phase와 bounded physics
  tumble을 사용한다. 실제 prone settle이면 같은 orientation-matched face-up/down GetUp presentation을
  재사용할 수 있지만 DownCount·groggy·Down Trigger는 0이고, upright settle이면 GetUp 없이 Airborne 또는
  Grounded locomotion으로 돌아간다.

[래그돌·물리 회복 local 후보](../artifacts/reports/R20_RAGDOLL_RECOVERY_REVIEW_20260906.md)는 총20kg의12 body와 실제 query, side 정렬 torque, 기립 보조 force/torque와 locomotion 복귀를 연결한다.
별도 검토 장면이며 사용자 시각 승인과 정식 Collider/Joint profile, GetUp 연출/게임 통합은 후속이다. source mesh/weight/rig와 충돌 Damage 제외 정책을 유지한다.

2026-09-07 후속 [회복 애니메이션 후보](../artifacts/reports/R20_GETUP_ANIMATION_REVIEW_20260907.md)는 FaceUp/FaceDown clip2를 제작하고 물리 회복 단계와 시각 손 접지에 연결한다.
준비→기립→마무리 동안 팔·몸통·다리를 표현하고, clip 종료와 실제 물리 안정 전 입력 잠금을 유지한다. Source rig/mesh 변경과 animation의 authority mutation은0이다.

후속 [자연스러운 회복 수정 후보](../artifacts/reports/R20_NATURAL_GETUP_REVIEW_20260907.md)는 무력 구간의 자세 drive0,10 body 구성,골반 중심의 상체/체중 이동/기립 분리와 발 도달 범위 보정을 적용한다.
회복 clip은2.35초·87채널이며 시각 Pelvis 위치 키를 포함하지만 gameplay Root motion은0이다. 몸통/발목 구성과 복잡한 접촉의 최종 검증은 후속이다.

2026-09-08 사용자 후속 요청으로 [골반·하체 회복 후보](../artifacts/reports/R20_PELVIS_GETUP_REVIEW_20260908.md)의 무릎 접기, 골반 체중 이동, 다리 폄을 보강한다. 두 clip의 길이와 물리 회복/완료 규칙, 기립 후 handoff 보정은 유지하며 시각 승인은 별도다.

### 6.3 Network·Prediction 경계

- Guest는 hand edge·Jump·movement input과 sequence만 보내고 Punch·Kick·Dropkick·Grab·Hit 결과를 주장하지 않는다.
- Host는 `AttackActionId`, action phase, AirAttackToken, MotionState, authority Root·Hand·Kick anchor 결과와
  Firearm Ammo·ShotSequence·Projectile·Spent timer·RecoilAccumulator/SpreadBloom, Hit·Grab·Throw event를
  snapshot/reliable semantic으로 제공한다.
- Local prediction은 root movement와 visual action anticipation에 한정하고 Hit, knockback, Down,
  constraint와 DropkickRecovery 종료는 Host 결과에 수렴한다.
- Remote Client는 전체 Ragdoll을 권한 재시뮬레이션하지 않고 Host의 core/필수 body state를 보간한다.
  Animation과 Ragdoll 전환 cue의 중복 재생은 Action/Event identity로 억제한다.
- Guest는 Projectile spawn/path/Hit, Ammo, recoil/spread와 spent cleanup 결과를 주장하지 않는다. Local
  muzzle/recoil anticipation은 Host ShotSequence와 state로 수렴하고 delayed Projectile을 중복 생성하지 않는다.
- Match local menu는 해당 Client의 gameplay command를 Neutral로 route하는 local presentation이다.
  Menu를 열어도 Host Tick·Round clock·physics와 다른 Player action은 계속되며 Character를 freeze,
  invulnerable, spectator 또는 authority owner로 바꾸지 않는다.

Implementation ownership은 다음과 같다.

- `AIR-001`: Ground/Air tap-hold와 `60/80/100ms` chord resolver
- `AIR-002`: 좌우 Kick·Dropkick 권한 physics·sweep·Hit·recovery와 2·3·4인 기능
- `ANP-001`: 현행 C1BRW-004 기반 Action Animation Matrix·semantic phase·root-motion authority 0
- `ANP-002`: body locomotion·hand·air Alpha presentation prototype
- `ANP-003`: Weapon action·Animator↔Ragdoll·local authority phase와 offline 2·3·4인 통합
- `NET-004`, `NET-007`, `NET-011..014`: 실제 peer action·recovery presentation 동기화와 인원별 검증
- `FIR-001`: Pistol/LongGun ammo·fire mode·SpentPendingCleanup
- `FIR-002`: visible authority Projectile·fixed-step swept SphereCast
- `FIR-003`: Host recoil impulse/torque·Muzzle·spread/bloom·ShotSequence recovery
- `WPN-005`: Firearm gameplay 통합, `ANP-003`: read-only Firearm presentation 통합

### 6.4 Disconnect grace·Forfeit와 현재 상태 복원

- Guest connection이 끊기면 Host는 최대 30초 동안 그 Player의 입력을 Neutral로 처리한다. Character는
  Scene에서 제거하거나 안전 위치로 이동하지 않고 현재 Transform·Velocity·MotionState·Alive 상태로
  계속 Authority physics를 받는다.
- grace 중 Alive Character는 다른 Player의 Punch·Kick·Grab·Weapon Hit와 knockback을 받을 수 있고,
  OOB·LethalHazard 판정도 그대로 적용된다. Alive인 동안 SharedGameplayCamera subject에도 남는다.
- reconnect는 disconnect 이전 상태를 rewind하지 않는다. Host snapshot 적용 시 아직 Alive면 현재
  physical Alive state로 조작을 재개하고, 이미 실제 gameplay로 탈락했다면 현재 spectator state로 복원한다.
- grace 중 OOB·LethalHazard로 발생한 elimination은 정상 gameplay elimination이다. 반면 명시적 Leave와
  30초 timeout은 `Forfeit`이며 탈락 순서와 PatchAuthor 후보에 포함하지 않는다.
- Forfeit 뒤 영구 참가자가 2명 이상이면 현재 Round를 계속한다. 한 명만 남으면 새 Score·Patch 또는
  Patch Offer를 만들지 않고 Lobby에 돌아간다.
- Host Leave·Loss는 Session을 종료한다. Host Migration, 다른 Guest의 authority 승계와 Character state
  continuation은 0이다.

---

## 7. Paint와 local Preset

Paint는 `SharedMesh`의 전신 unique UV0를 사용한다.

- UV0 `0~1`, 겹침·좌우 mirroring 없음
- 머리 전면·가슴 중앙·terminal 주면의 불필요한 seam 없음
- 관절 deformation에서 뒤집힘·다른 island bleed 없음
- 512×512 RGBA8 sRGB를 Alpha `START` 기준으로 사용
- texel density 전신 평균 `±10%`, limb가 평균의 85% 아래로 내려가지 않는 것을 시작 목표로 검증

Preset은 local profile에 최대 10개 저장한다. BaseColor, AccentColor, Brush Stroke와 Cosmetic 배치의
편집 가능한 source를 보존하며 atomic save로 마지막 정상본을 보호한다. Preview cache는 원본이 아니다.

Lobby/party에서 외형을 공유할 때는 Client가 편집 source의 크기·좌표·catalog item·배치 수를 검증하고,
AuthorityHost가 허용 version과 한도를 다시 확인한 뒤 P2P로 검증된 외형 manifest와 필요한 visual data만
relay한다. gameplay Snapshot에 per-frame Cosmetic transform이나 Paint pixel을 넣지 않는다. 잘못된 외형은
해당 플레이어만 승인된 기본 외형으로 대체한다.

외부 이미지, URL, clipboard image, 사용자 Mesh·Shader·script import는 허용하지 않는다.

Customization은 Lobby에서 `C`로 어디서든 진입한다. Booth 근접, 특정 trigger 또는 물리 충돌을
요구하지 않는다. Guest는 진입 시 자신의 Ready를 해제한다. Host에는 Ready가 없으므로 Appearance를
미확정 상태로 바꾸고 Start Gate를 잠근다. 두 역할 모두 편집 중 gameplay command를 중립화한다.

---

## 8. CosmeticAttachmentCage

Cosmetic은 render LOD와 분리된 숨은 전신 Cage의 stable surface point에 부착한다.

Alpha는 Cage·저장·P2P 검증 기능을 확인하기 위해 `EyeSet`, `Mustache`, `Headwear` 각 category의
placeholder 대표 Cosmetic을 1개씩, 총 3개 제공한다. Category 이름을 바꿀 경우에도 얼굴 부착형,
돌출 장식형, 머리 착용형의 같은 기능 범위를 가진 동등 최소 catalog를 명시해야 한다. 전체 Cosmetic
catalog와 production-quality Mesh 세트는 post-Alpha 범위이며 Alpha 완료를 막지 않는다.

- 머리·몸통·등·팔·terminal·관절·다리·발바닥을 포함한 모든 외부 surface 배치 가능
- 위치·3축 회전·복제·삭제 가능
- 사용자 scale·tint·material 교체 없음
- 같은 위치 중첩과 Character/Cosmetic 시각 관통 허용
- 자동 snap·밀어내기·삭제·부위 재배치 없음
- Alpha `START` 전역 상한 16개
- runtime scale `(1,1,1)`
- Collider, mass, hitbox, reach, Weapon relation과 Camera bounds 불변

Neutral, Grab, Strike, 좌우 Kick, Dropkick, Air Grab, crouch, Ragdoll, GetUp, Weapon 장착과 모든 LOD에서 같은 skin surface point를
따라야 한다. surface drift `0.002H 이하`를 C3 시작 목표로 측정하고 Cage revision 변경 시 Preset
migration 또는 명시적 incompatibility를 제공한다.

---

## 9. 무기 통합과 Alpha 전투

`Pistol`, `LongGun`, `Bat`, `Hammer` 네 종류를 유지한다. Character Gate에서는 먼저
GripCandidate→Held→ReleaseDrop→ReacquireGrip과 terminal/Socket 정렬을 검증한다.

W1 입력 사용자 승인 뒤에는 실제 전투가 Alpha 범위다.

- Pistol·LongGun: fire, hit, damage/knockback, release/drop, reacquire
- Bat·Hammer: swing, impact, damage/knockback, release/drop, reacquire
- Host authority의 owner·hit·damage·impulse 판정
- 2·3·4인 Camera와 network 상태 수렴 검증

### 9.1 Firearm Ammo·Projectile·Recoil

| Weapon | Fire mode | 총 Ammo | Reload |
|---|---|---:|---|
| `Pistol` | semi-auto, 유효 down edge당 최대 한 발 | `7` | 없음 |
| `LongGun` | full-auto, 유효 hold·cadence 동안 반복 | `30` | 없음 |

- Alpha Player surface에 지속 노출되는 Match HUD, Active Patch HUD와 Ammo HUD는 0이다. AmmoRemaining, FireMode,
  ShotSequence·Projectile·recoil/spread·spent state는 developer-only debug와 Evidence에서만 확인한다.
  transient Patch selection/result UI는 이 제한과 별개로 허용된다.
- Host가 Shot과 Ammo를 판정하고 유효 Shot마다 Ammo를 1 줄인다. Ammo 0 Shot 뒤 Weapon은 강제
  Release되어 `SpentPendingCleanup`으로 들어가며 `START 2~4초` 뒤 제거된다.
- Firearm Fire·Projectile combat은 Playing과 SuddenDeath에서 계속 유효하다. RoundResult부터 새 Fire는
  0이고 active Projectile은 제거되며 다음 Round로 이월하지 않는다. SuddenDeath에서 중단되는 것은
  새 Weapon Supply이지 이미 존재하는 Firearm combat이 아니다.
- Spent Weapon은 owner·Collider·Pickup·fire·swing·Hit·Patch Trigger·map control이 없지만 제거 전까지 supply
  cap에 포함된다. Reload, reserve magazine과 ammo pickup은 0이다.
- Host는 유효 Shot마다 visible Projectile과 immutable attacker·Source Weapon·ShotSequence·AttackAction을 만든다.
- Projectile은 Source Weapon·발사 Actor Collider를 제외하고 fixed-step 이전→다음 위치의 swept
  SphereCast 첫 blocking Hit만 처리한다. pierce·ricochet은 0이고 gravity는 `START=0`이며
  TTL·OOB·Round reset·stale generation에서 제거한다.
- Static/Map hit는 Projectile을 막지만 Lever·Hazard·map control을 작동시키지 않는다. Guest의 Projectile
  path·Hit·Damage·Knockback 주장은 0이다.
- Pistol은 narrow base spread와 strong single-shot recoil을 사용한다. Main arm은 앞으로 뻗은 단순 Grip
  pose를 유지하고 Host가 bounded Character/Weapon Rigidbody·Joint impulse/torque와 Muzzle 방향을 확정한다.
- LongGun은 Main+Support arm을 앞으로 뻗은 같은 단순 Grip pose를 사용한다. accepted Shot마다 deterministic
  ShotSequence로 bounded `RecoilAccumulator`와 `SpreadBloom`을 누적하고 button release 또는 승인 gap 뒤
  회복한다. 두 Firearm 모두 별도 authored fire/recoil animation clip은 0이며 Host physics가 팔과 Weapon을
  위로 움직이고 spring/damping으로 복귀시킨다. frame rate·Animation·packet order는 값을 바꾸지 않는다.

### 9.2 Weapon Supply

Weapon은 라운드당 하나만 공급하지 않고 `Playing` 동안 반복 Supply를 사용한다.

| 인원 | 첫 Pulse / 반복 간격 / cap |
|---:|---|
| 2인 | `10초 / 22초 / 2` |
| 3인 | `8초 / 16초 / 2` |
| 4인 | `6초 / 12초 / 3` |

- Host는 Round 초기 participating roster로 profile을 고정하고 Disconnect·Reconnect·Forfeit는 다음
  Round에서만 반영한다.
- cap은 `Incoming + Loose + Held + SpentPendingCleanup`을 모두 포함한다.
- Host는 Round마다 MatchSeed·Round·WeaponCatalogVersion으로 Pistol·LongGun·Bat·Hammer가 한 번씩 든
  shuffle bag을 만들고 Safe DropZone을 선택한다. 실제 admission된 Spawn만 bag cursor를 소비한다.
- `Incoming`은 Character·Weapon combat contact, Pickup, Patch Trigger와 Map control을 만들거나 Camera
  subject가 되지 않고 안전하게 착지한 뒤 `Loose`로 전환한다.
- admission·landing의 Character/Weapon clearance가 없으면 `NoSafeDropZone` 또는 `LandingBlocked`로
  Collider·피해 없이 끝낸다. WeaponCleanupBoundary는 회수 불가능한 Loose만 제거하고 유효 Held Weapon의
  Collider 일부 진입은 제거하지 않는다.
- cap full·capacity-limited Pulse, OOB 제거와 Patch10 파생 Wave는 backlog·즉시 재시도를 만들지 않는다.
- Patch09 desired batch2는 남은 capacity만 admission한다. Patch10 파생 Wave는 Base Pulse 뒤
  `START 6~10초`에 당시 capacity로 한 개만 시도하며 Base Supply root를 다시 만들지 않는다.
- Supply admission은 Playing에서만 가능하고 SuddenDeath·Round Result에서 pending을 취소한다. 기존
  Weapon은 reset까지 유지하며 다음 Round 시작에는 Incoming·Loose·Held·SpentPendingCleanup·Projectile,
  bag·timer·pending wave를 초기화한다.
- SpentPendingCleanup deadline은 SuddenDeath에서도 계속 진행되며 제거 전까지 cap에 포함된다.

### 9.3 Firearm·Patch Trigger 경계

`TRG-ATTACK-HIT-CONFIRMED`는 기본 전투 단계에서 승인된 Punch·Kick·Dropkick hit를 source로 사용한다. W1 뒤
Firearm·Melee가 같은 Host ownership·rate·hit·dedupe 검증을 통과하면 같은 의미 event source를
재사용할 수 있다. 이 연결은 `PATCH-PROT-003..004`의 knockback/recoil만 허용하며 Weapon damage,
ownership, fire/swing rate, drop과 Round reset을 우회하지 않는다.

Firearm은 Projectile마다 하나의 AttackAction을 사용한다. swept first Hit의 Action·Target에 Patch03을
최대 한 번, 같은 Projectile Action의 attacker에게 Patch04를 최대 한 번 적용한다. LongGun 반복 Shot은
각 ShotSequence를 독립 dedupe하되 cadence, accumulator와 최종 impulse 상한을 우회하지 않는다.

별도 `TRG-WEAPON-HIT-CONFIRMED`는 Host가 실제 Weapon ownership·rate·hit·dedupe를 모두 승인한
Weapon hit만 사용한다. `PATCH-PROT-011`의 victim Held Drop과 `PATCH-PROT-012`의 attacker source
Weapon Drop은 기존 Release·ownership·Loose replication을 재사용하며 Damage·Ammo·cadence를 바꾸거나
Forced Drop에서 새 Hit Trigger를 만들지 않는다. Patch12 source Weapon이 이미 놓였거나 owner가
바뀌었다면 다른 Weapon을 대신 해제하지 않고 `NoEligibleTarget`으로 끝낸다.
Punch·Kick·Dropkick은 Source Weapon이 없으므로 이 Weapon Trigger를 만들지 않는다.

Projectile은 Spawn 때 immutable source identity를 보존하므로 delayed Hit 전에 Source Weapon이
Spent/remove되거나 owner를 잃어도 Host-confirmed Attack/Weapon Hit event는 유효할 수 있다. 그러나
Patch12는 live held relation을 다시 확인하므로 이 경우 `NoEligibleTarget`이고 다른 Weapon을 대신 Drop하지 않는다.
Projectile static hit와 recoil/muzzle impulse는 hand-only Map control을 원격 작동시키지 않는다.

Pistol 7발, LongGun 30발과 reload 0은 확정이다. 정확한 fire cadence, Projectile speed/radius/TTL,
damage, knockback, recoil·spread·bloom/recovery, melee swing timing과 balance는 W1과 Alpha tuning에서
결정한다. W1 승인 전에는 임의 입력을 확정하거나 실제 combat를 구현 완료로 표시하지 않는다.

---

## 10. Camera·LOD·성능

공용 Camera는 render Mesh나 Ragdoll limb의 순간 bounds가 아니라 Authority core에 붙은 고정
`ActiveSubjectBounds`를 사용한다. Cosmetic, Weapon, Projectile, recoil pose와 visual deformation은 이를 키우지 않는다.

- 2·3·4인 모두 검증
- 16:9, 16:10, 21:9의 Min/Max Dolly 검증
- Disconnect grace의 Alive Character는 Camera subject에 남고 실제 elimination 뒤에만 spectator 처리되는지 검증
- Neutral, Sprint, Strike, 좌우 Kick, Dropkick·별도 DropkickRecovery, Air Grab, down/Ragdoll, shared GetUp,
  Pistol/LongGun forward-arm Grip·physics recoil·Spent release와 Weapon 상태 검증
- full-body silhouette, 양팔·양다리, terminal, Player marker와 무기 방향 판독

LOD0는 6k/10k/14k 후보, LOD1은 선택 LOD0의 약 50~60%, LOD2는 약 20~30%를 `START`
비교 범위로 둔다. 4인×각 16 Cosmetic×Supply cap3의 Incoming/Loose/Held/Spent Weapon×active Projectile×Ragdoll worst case에서 GPU frame, CPU skinning,
draw call, overdraw, memory와 LOD pop을 측정한다. 시각 차이가 없는 가장 낮은 후보를 기본으로 선택한다.

C4 source→FBX→Prefab Gate는 각 LOD의 silhouette·bounds, UV0, normal/tangent class,
material-slot mapping, Skeleton과 mount pivot을 비교한다.

---

## 11. UI 이미지 해석

- UI 이미지 속 Character 비율·손·Rig는 제작 기준이 아니다.
- Customizer 승인 이미지의 `크기` 도구는 비권위이며 실제 Cosmetic scale control은 없다.
- Lobby 이미지의 `[R] 준비`는 비권위이며 현재 Ready 입력 기준으로 사용하지 않는다.
- Legacy Private Lobby의 정적 lineup과 UI Start Button은 구현 근거가 아니다.
- Customization은 `C`로 어디서든 진입하며 Booth proximity를 요구하지 않는다.

---

## 12. 미승인·Alpha tuning 항목

- r16 Neutral 방향 승인 이후의 C1b exact 비율·공식 Pose·Unity parity와 전체 gameplay scale; Neutral 재승인 요구0
- Collider·mass·Joint·reach와 `GrabHoldThresholdMs`
- `DualClickChordWindow 60/80/100ms`, Kick·Dropkick impulse/knockback·steering과 DropkickRecovery tuning
- Sprint multiplier
- down `BaseDuration`, `IncrementDuration`, `MaxDuration`
- 최종 topology, UV padding, Polygon, Bone, LOD와 Material 예산
- Alpha EyeSet·Mustache·Headwear 각 placeholder 대표 1개 또는 동등 최소 catalog의 authored size와
  기능 검증; full Cosmetic catalog는 post-Alpha
- W1 무기 입력, fire cadence·Projectile speed/radius/TTL·damage/knockback·recoil/spread/bloom과 combat balance
- SpentPendingCleanup `START 2~4초`의 최종 cleanup timing; Pistol7·LongGun30·reload0은 확정
- `PATCH-PROT-001..008` Character modifier의 tuning과 승인 Patch12의 2·3·4인 기능 결과
- 반복 Supply 시간·cap과 Patch10 second-wave는 승인 `START`; 최종 weapon density·balance는 Alpha Evidence 대기
- Alpha procedural action pose와 production Punch·Kick·Dropkick·Grab·Throw·Spent release·Ragdoll/shared
  GetUp clip polish. Firearm authored fire/recoil clip은 0이고 forward-arm Grip+physics recoil을 검증한다.
- 후속 패치 icon·Animation·VFX·SFX 표현 방향
- 최종 Camera profile과 최소 GPU 기준

각 항목은 2·3·4인 측정, 비교 capture와 사용자 Gate 없이 `LOCKED`로 바꾸지 않는다.
