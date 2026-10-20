# 개발 진행 상태 점검 — 2026-09-05

> **후속 실행 반영:** 아래 감사는 수정 전 상태를 보존한다. 이후 사용자 지시로 r16/r20 진행 문서,
> ANP 선행조건·static/animation·offline/network 경계와 Neck 적용 범위를 수정했다.
> 문서 workflow 회귀검사7/7·Art 회귀25/25 통과. 기존 r20으로 공식 Pose8·lineup2·추가 자세5,
> render32를 생성했으며 표본13 구조 검사는 PASS, 시각 승인은 대기다.
> 현재 결과는 [캐릭터 검토 기록](CHARACTER_REWORK_NEUTRAL_REVIEW.md)과
> [정적 검토 결과](R20_STATIC_POSE_REVIEW_20260905.json)를 따른다.
> 기존 에셋 inventory·C1B-005 역사 hash 불일치는 이 문서 수정으로 해결된 것이 아니며 후속 정리 대상이다.

현재는 Foundation 이후 캐릭터 제작 중이다. r16 Neutral 방향 승인과 r20 리그 후보가 실제로 존재하지만,
현행 문서의 r11 승인 대기 표기와 에셋 관리 목록이 이를 따라가지 못했다. 다음 기능 구현에 앞서
진행 기록·에셋 목록·검증 기준을 정리할 필요가 있다. 이 보고서는 점검 결과와 수정 제안이며 새 승인 기록이 아니다.

## 확인 범위

- `docs/` 현행 문서 12개: Index, PRD, SRS, Plan, Trace, Character, Art, UI, Map Guide, P00, Weapon, Patch.
- README, 기존 보고서 3개, Windows Build 안내, BinaryAssetPolicy, THIRD_PARTY_NOTICES, 문서 Evidence Markdown 2개.
- 요구사항·Task·선행관계·링크 검사, Evidence Manifest 49개 구조 검사, Unity 소스·테스트·Scene,
  r16 승인 기록 및 r20 리그·포즈 보고서, 저장소 검증 도구.
- 점검 시작 Git HEAD: `3748023`. 기존 미커밋 문서·프로필·검증기·복귀 상태 코드 변경을 포함한 작업 트리 기준.
- 기존 변경 파일을 수정하지 않았다. 신규 게임 기능, 캐릭터 제작, Player Build, commit/push는 수행하지 않았다.

## 실제 진행 상태

| 영역 | 확인 결과 | 남은 작업 |
|---|---|---|
| Foundation | Unity 6000.3.9f1, 모듈 경계, 저장 기반, 테스트, Build Profile 존재 | 현재 에셋 목록·검증 기록 정합성 복구 |
| 캐릭터 외형 | r16 Neutral 방향 승인 기록 존재. source와 QA report SHA-256 일치 | production topology·게임용 최종 비율 승인과 구분 |
| 캐릭터 리그 | r20 `HELPER_RIG_REVIEW`, 논리 Bone20+helper2, source hash·size 일치 | 리그 시각 검토와 Unity 전달 방식 확정 |
| 포즈·애니메이션 | r20 정적 시험 포즈8과 contact sheet 존재, 이미지 hash8/8 일치, 보고서 action/clip0 | 공식 C1b Pose8·lineup과 실제 동작 클립 제작·검증 |
| 게임 로직 | 최소 SimulationKernel·atomic storage와 미커밋 CharacterRecoveryStateMachine 존재 | 복귀 코드의 gameplay 연결, duration·clearance·Lobby 구분·권한 통합 |
| 입력·전투·카메라·패치·무기·네트워크 | 모듈/설계 기반. Runtime에 실제 캐릭터 controller 없음 | G1 이후 구현·통합·2/3/4인 검증 |
| Scene·실행물 | SampleScene 하나 | Lobby·Match·P00, 사용자 실행 Windows Build |

승인 근거는 [r16 NeutralApprovalRecord](../../BlenderSource/Characters/C1B-RW-016-preview/NeutralApprovalRecord.json)다.
`UG-C1B-NEUTRAL-r16-20260902`는 Neutral 시각 방향 및 다음 포즈 작업 착수를 승인하지만,
production topology·gameplay rig·Collider·Animation·FBX·Unity 반입 승인은 포함하지 않는다.
원본 승격과 포즈 제작 방법은 이 기록에서 별도 결정 대기로 남아 있다. 후속 도구나 기술 PASS 자체를 사용자 승인으로 해석하지 않았다.

[r20 RigQAReport](../../BlenderSource/Characters/C1B-RW-020-helper-rig-preview/RigQAReport.json)는
Blender driver8, authored clip0, Unity용 helper curve bake 필요를 기록한다. 이번에는 파일 identity와 기존 보고서를
확인했으며 r20 deformation을 Blender에서 새로 실행하거나 Unity에 반입하지 않았다.
[기존 정적 포즈 보고서](../../BlenderSource/Characters/C1B-RW-020-helper-rig-preview/PoseTests/PoseTestReport.json)의
시험 포즈8은 공식 C1b 요구 Pose8과 다른 목록이다. 개수가 같다는 이유로 `C1BRW-004` 완료로 볼 수 없다.
Contact sheet의 Relaxed Idle도 팔을 벌린 시험 자세여서 9월 4일 결정의 arms-to-torso Idle 완성을 증명하지 않는다.

계획표를 기계 집계하면 178 Task·258.0 집중 개발일, PASSED20 / REWORK_REQUIRED5 / IN_PROGRESS1 /
USER_REVIEW1 / NOT_STARTED149 / BLOCKED1 / DEFERRED1이다. 이 상태표는 r16/r20을 반영하지 않았으므로
실제 개발 완료율로 사용하지 않는다. 258일 역시 남은 일정 추정치가 아닌 계획 총합이다.

## 이번 검증 결과

| 검사 | 결과 |
|---|---|
| 문서 baseline validator | PASS: 문서12, PRD31, SRS186, AT44, Patch12, Task178, 중복·누락·의존 순환0 |
| 검토 Markdown21개의 로컬 inline link 경로 | 누락0 |
| Evidence Manifest49 | 구조/경로 PASS; 각 역사 실행 결과를 재승인한다는 뜻은 아님 |
| Unity 전체 EditMode | 63/63 PASS, failed/skipped0 |
| Unity 전체 Editor PlayMode | 4/4 PASS, failed/skipped0 |
| Ruby 검증 도구 테스트10개 파일 | 191 runs, 1502 assertions, 6 failures, errors/skips0 |
| Art / Build Profile / 금지 인프라 | PASS |
| 역사 Character proportion / blockout / pose lineup / r02 Neutral 기본 검사 | PASS |
| LFS repository 검사 | FAIL: inventory exact set 및 현재 LFS 수 요약 불일치3 |
| License inventory 검사 | FAIL: 미등록 media62 + review media exact set 불일치1 |
| 역사 C1B-005 import parity 기본 검사 | FAIL: FBX meta·AlphaVisualQA r02 hash 불일치2 |

Unity의 복귀 관련 기존 테스트4개도 통과했다. 이 결과는 Down/Dropkick state 분리와 기본 reset을 검증하며,
실제 물리 회복·groggy duration·네트워크·플레이 가능 상태까지 검증한 것은 아니다.

Ruby 실패6개는 LFS 현재 repository 검사1, license 현재 repository 검사1, C1B-004 현재 변경 범위 검사1,
C1B-005 static/Blender/Unity 검사3이다. C1B-005의 underlying Unity 테스트7개와 Blender inspector는
실행됐지만 전체 validator는 위 해시 불일치로 실패했다. C1B-004 기본 bundle 검사는 통과했으며,
scope 검사는 기존 복귀 코드·9월 4일 문서 수정 등을 과거 포즈 작업 범위 밖으로 판정했다.

## 수정이 필요한 사항과 제안

1. **진행 문서 동기화.** Index·Plan·Trace·Character·Art와 기존 현황 보고서가 r11을 현재 후보로 표시한다.
   r16의 승인 범위와 r20의 검토 상태를 각각 기록하고, 역사 기술 PASS는 보존한다. 현재 복귀 코드도
   미통합 부분 구현으로 추적한다. r16 승인만으로 `UG-C1B`나 CHR-001 전체를 PASSED로 만들지 않는다.
2. **에셋 inventory 갱신.** 검사상 LFS 실제 후보/추적 파일은21/21인데 목록 요약은5 기준이다.
   기존 source와 render를 경로·hash·제작 lineage로 분류하고 미등록62개를 대조한다.
   preview를 등록하는 작업과 production/shipping 승인 또는 원격 업로드를 구분한다.
3. **역사 검증과 현재 회귀검사 구분.** C1B-005의 기록된 hash와 현재 FBX meta·QA profile 변경 근거를 대조한다.
   역사 manifest의 당시 hash를 덮어써 PASS를 만들지 않고 현재 재검증 기록을 별도로 작성한다.
   C1B-004 scope 검사는 해당 작업용 격리 fixture에서 검증하도록 정리한다.
4. **Neck 외형/리그 구분 — 사용자 설명으로 해소.** 사용자가 2026-09-05 후속 대화에서 기존 Neck 제한은
   외형에 관한 것이라고 명확히 했다. 내부 Neck Bone의 존재는 그 외형 제한 위반이 아니다.
   이후 r20 `.blend`를 Blender 5.2에서 직접 읽어 Bone22, `Chest → Neck → Head`, Neck의
   `useDeform=true`, 몸통의 Neck weight 정점30,627개와 머리 전체6,050개의 Head weight1을 확인했다.
   원본 저장은 하지 않았고 검사 전후 SHA-256이 동일하다. 결과는
   `.codex_tmp/development-audit-20260905/r20-neck-inspection.json`에 있다.
   문서에는 외형 제한과 내부 Bone 규칙의 적용 범위를 명확히 적으면 된다. Neck 삭제나 리그 재제작 사유로 취급하지 않는다.
5. **다음 제작 Gate 명확화.** r20은 정적 시험 결과다. 실제 Idle/Walk/Sprint·공격·회복 클립 및
   helper bake·FBX/Unity parity를 시작하기 전에 리그 검토와 허용 범위를 기록해야 한다.

권장 다음 작업은 1~3의 문서·inventory·검증 정합성 수정과 4의 명확해진 적용 범위 반영이다.
그 뒤 r20 검토를 통해 다음 캐릭터 제작 범위를 확정한다. 사용자가 ‘수정 필요·애매함·승인 필요 시 다시 질문’하도록 요청했으므로
이 보고서에서 문제와 수정 범위를 제시하고 해당 변경 착수는 확인 대기로 둔다.

## 구현계획 후속 실행 가능성 재검토

2026-09-05 사용자 요청으로 Plan의 선언된 선행관계와 실제 작업 범위를 다시 대조했다.
결론은 **기존 r16/r20을 유지하며 계속 개발할 수 있지만, 현행 계획을 그대로 다음 단계 실행 기준으로
사용하기에는 아래 수정이 필요하다**는 것이다. Neck은 해소된 외형/내부 골격 구분이며 blocker가 아니다.

문서 baseline validator 재실행은 PASS다. Task178·258.0일, 참조 누락0·명시 의존 순환0이다.
다만 이 검사는 폐기된 Task의 current acceptance 여부나 작업 제목에만 나타나는 숨은 선행조건을 검사하지 않는다.

| 위치 | 확인된 문제 | 구체 수정안 |
|---|---|---|
| Plan §0·§5.2·§11·§12 및 Index/Trace | r11 USER_REVIEW가 r16 승인·r20 후보 상태와 불일치 | r16 Neutral 승인 날짜·범위를 반영하고 r20은 Blender 리그 부분 산출물로 별도 추적. CHR-001 Prefab 완료나 UG-C1B 승인으로 승격하지 않음 |
| ANP-001 | 선행 `C1B-004`는 역사/superseded Task이며 current 상태가 REWORK_REQUIRED | 현행 포즈 작업 `C1BRW-004`로 교체, `FDN-003` 유지. 옛 Task와 당시 Evidence는 보존 |
| Plan §11 순서8 | C1BRW-004..005에서 Pose·Animation·Unity parity를 수행한다고 적힘. 실제 행은 static pose와 Rig/Collider/Animation0 | 순서8을 정적 Pose8·lineup 및 Neutral FBX/Unity parity로 명확화. 게임용 Skeleton/Prefab은 CHR-001, 동작 animation prototype은 ANP-002..003 소유 유지 |
| ANP-003 | G1 offline 단계인데 제목에 network 통합까지 포함. 실제 transport는 NET-001에서 착수 | G1은 local authority phase·Ragdoll·offline 2/3/4 표현 검증으로 제한하고 실제 peer 동기화 검증은 NET-004·NET-007·NET-011..014에 명시 |

ANP-003의 network 문구를 실제 접속 완료 조건으로 읽으면 실행 순서가 막힌다.
선행관계 방향으로 `NET-001 → QA-002 → QA-001 → ANP-003`이므로 ANP-003에 NET-001을 단순 추가하면
순환이 생긴다. G1 표현과 G2 peer 통합의 검증 범위를 분리해야 한다. 현재 그래프에 명시된 순환이 있다는 뜻은 아니다.

### 현재 작업별 착수 판단

| 작업 | 판단 |
|---|---|
| 문서·상태·inventory·검증 기준 정리 | 다음 권장 작업. 새 기능/제품 결정을 추가하지 않는 정합성 수정 |
| 기존 r20 리그 | 보존·검토 가능. Neck 때문에 삭제/재제작할 필요 없음 |
| C1BRW-004 공식 Pose8·lineup | 완료 처리 불가. r17 기술 보고서는 있지만 현재 폴더의 `.blend`0·render0이며 userPoseApprovalRecorded=false. r20 시험 포즈8은 다른 목록이므로 대체 불가 |
| C1BRW-005 현재 캐릭터 static FBX/Unity parity | 미완료. 현재 Assets의 C1B-005는 old geometry이며 r16/r20의 완료 증거가 아님 |
| CHR-001 게임용 Skeleton·Prefab | r20의 Blender 리그 부분은 존재. 선언된 선행 UG-C1B와 static parity, Unity Prefab·gameplay anchor 완료 증거는 아직 부족 |
| CHR-002 이후 물리·이동/전투 및 ANP-002 실제 animation prototype | 선언된 선행 Task가 충족되지 않아 지금 전체 구현 착수/완료 승인으로 해석할 수 없음 |
| INP-001 / RND-001 / PAT-001 | 개별 선행 Task 기준 충족. 입력 정의·Round state machine·Patch catalog의 독립 로직은 캐릭터 미완료와 분리할 수 있음. G0 이후 순서로 적힌 §11과 맞춰 병행 허용 범위를 명시할 것 |

권장 캐릭터 진행 순서는 다음과 같다.

1. 위 네 Plan 수정과 에셋 inventory·historical/current verifier 정합성을 정리한다.
2. 기존 승인 r16과 r20 후보를 보존하고 공식 Pose8·lineup의 현행 검토 자료를 준비한다.
   재생성 방법/범위는 현재 승인 기록의 미결정 항목과 맞춰 확정한다.
3. C1BRW-005의 정적 Neutral FBX/Unity parity를 수행할 허용 범위를 확인한 뒤 검증한다.
4. 수치·포즈·Unity 결과를 갖춘 UG-C1B 검토를 완료한다.
5. 기존 r20을 바탕으로 CHR-001 게임용 Prefab·anchor 검증, CHR-002 physics와 이후 gameplay/animation을 진행한다.

이 재검토에서는 Plan·상위 사양·에셋·게임 코드를 수정하지 않았다. 기존 검증 결과를 무효화하거나
사용자 리그/포즈 승인, 새 FBX 반입 승인 또는 전체 개발 착수 승인을 새로 만들지 않았다.

## 재확인 명령과 산출물

저장소 root에서 실행한다.

```sh
python3 artifacts/evidence/docs/verify_doc_baseline.py
ruby -e 'Dir["tools/tests/*_test.rb"].sort.each { |file| require File.expand_path(file) }'
ruby tools/verify_lfs_repository.rb
ruby tools/verify_license_inventory.rb
ruby tools/verify_character_import_parity.rb
```

Unity는 설치된 6000.3.9f1에 `-batchmode -nographics -runTests -testPlatform EditMode`와
`-testPlatform PlayMode`를 순차 실행했다. Player Build는 실행하지 않았다.
현재 점검의 로컬 XML·로그·validator 출력은 `.codex_tmp/development-audit-20260905/`에 있다.
이 임시 경로는 Git 제외 대상이며 보고서의 현재 결과를 재현하려면 동일 작업 트리에서 위 검사를 실행한다.
