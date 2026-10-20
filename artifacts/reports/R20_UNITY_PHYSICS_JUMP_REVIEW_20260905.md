# r20 Unity 물리 점프 시제품 — 2026-09-05

> **2026-09-06 영상 정정:** 당시 캡처의 Rigidbody 이동과 별개로 skinned mesh의 팔다리 갱신이 누락됐다.
> 해당 영상을 팔다리 동작의 시각 증거에서 제외하고 [정정된 캡처](R20_SKINNING_CAPTURE_FIX_20260906.md)를 사용한다.

**후속 진행:** [8방향 점프 전환 검토](R20_JUMP_TRANSITION_REVIEW_20260905.md)에서 보행/Sprint·방향 회전·착지 복귀를 연결했다.
아래는 첫 물리 점프 시제품의 기록이며 최신 코드·영상·source snapshot은 후속 검토를 따른다.

[사용자 전환 승인](R20_PHYSICS_JUMP_DECISION_20260905.json)에 따라 전용 Jump/착지 clip 제작을 중단했다.
기존 Blender Action5는 비교 자료로 보존하며 현재 시제품은 Unity Rigidbody·실제 접촉과 procedural 하체 pose로 작동한다.
원본 r16/r20·승인 하체 웨이트·승인 펀치 파일은 보존했다.

## 구현한 동작

- 입력 수락 시 다음 물리 step에서 즉시 힘을 적용한다. 애니메이션 준비가 끝날 때까지 기다리지 않는다.
- 중력에 의한 상승·낙하와 실제 collider 접촉으로 점프·착지를 처리한다.
- 입력 버퍼0.12초·가장자리 유예0.10초, 점프당 한 번의 도약, 소모된 점프의 착지 후 복구를 구현했다.
- 바닥 법선과 접촉 간격을 확인해 벽·천장·과도한 경사를 지지면으로 처리하지 않는다.
- 공중 다리 접기는 수직 속도, 착지 압축은 실제 하강 속도에서 얻은 충격 크기에 따른다.
- 지지가 확인되면 공중 발 들기를 해제한다. 원본의 중립 무릎 방향과 hip helper X축 추종을 유지한다.
- 하체 pose는 팔의 local rotation을 수정하지 않는다. 펀치·Grab·공통 총기 Grip의 제어 범위를 침범하지 않는다.

점프 높이0.25m, 월드축 이동1.5m/s, 가속도10m/s², 허용 경사50°, capsule mass20kg,
최대 하체 압축/다리 접기0.025m는 **로컬 검토 START 수치**다. 최종 게임 밸런스 승인이 아니다.

## 코드와 경계

| 파일 | 역할 |
|---|---|
| `Runtime/Simulation/PhysicsJumpStateMachine.cs` | Unity 컴포넌트와 독립적인 buffer/coyote·도약 소모·착지·입력 초기화 정책 |
| `Runtime/Contracts/JumpPresentationState.cs` | 수직 속도·접지·착지 경과·충격의 읽기 전용 값; UnityEngine 의존0 |
| `Prototypes/PhysicsJump/PhysicsJumpMotor.cs` | Rigidbody 힘 적용·contact callback·검토용 월드축 이동 adapter |
| `Runtime/Presentation/ProceduralJumpPose.cs` | 물리 결과를 따라 pelvis·leg·hip helper pose 계산; Rigidbody·팔 쓰기0 |
| `Prototypes/PhysicsJump/PhysicsJumpReview.cs` | 승인 skin transport 로드와 검토 입력 연결 |
| `Editor/PhysicsJump/PhysicsJumpReviewMenu.cs` | 독립 검토 장면 열기 |

위 경로의 기준은 `Project hotfix/Assets/ProjectHotfix/`다.
Simulation→Contracts, Presentation→Contracts 경계를 유지하며 Unity 접촉 컴포넌트는 Prototypes에 둔다.
제품 Runtime은 전역 fixedDeltaTime·Physics simulation mode를 바꾸지 않는다.

검토 모델은 `.codex_tmp/unity-physics-jump-20260905/ApprovedCharacter.bytes`에서 읽는다.
`tools/blender/export_c1b_rw020_physics_review.py`가 승인 파일의 neutral mesh·weights·Bone22를
원본 저장 없이 전달한다. Animation export0이며 정식 FBX/Prefab parity·production rig 승인으로 승격하지 않는다.
원본 발 최저점이 origin보다 약4.086mm 낮아, skin을 변경하는 대신 capsule 바닥을 그 기준에 맞췄다.

## 실제 실행 검증

| 검사 | 결과 |
|---|---|
| 전체 Unity EditMode | 85/85 PASS |
| 전체 Unity PlayMode | 14/14 PASS |
| 신규 물리 시나리오 | 즉시 도약·단일 착지, 공중 중복 거부, 착지 전 buffer, coyote 허용/만료, 천장, 벽, 높이에 따른 충격, authority disable/reset, 경사 판별 PASS |
| 승인 모델 재생 | 60Hz 실제 PhysicsScene180step, 제자리/이동 점프2회, 렌더60장 |
| 장면 구성 | Bone22, Animator0·점프 clip0, dynamic MeshCollider0 |
| 자세 제어 | 팔 local rotation 불변, 발 뼈 target 오차1e-5m 미만 |
| 평지 발 표면 | 검사한 접지 step의 최대 절대 바닥 높이 오차 약0.062mm |

시제품 영상의 관측 도약 높이는 약0.232m다. 초기0.25m 설정에 대한60Hz 이산 물리 검토값이며
네트워크·모든 frame rate에서의 최종 tuning 보증이 아니다. 착지 count에는 최초 spawn 안정화1회가 포함된다.
Unity skin 전체의 면 뒤집힘·자기 교차 전수 검사는 이번에 추가 실행하지 않았다.
입력 skin의 이전 Blender 변형 QA, Unity 실제 재생·표면 높이·발 target·팔 불변 검사와 시각 확인을 구분한다.

[구조화 증거](R20_UNITY_PHYSICS_JUMP_REVIEW_20260905.json)에 source·transport·코드·검사 XML·영상 hash를 연결했다.
문서 baseline·workflow tests7/7, art profile·tests25/25(221 assertions), `git diff --check`와
원본/승인 파일·코드·증거 hash도 확인했다. ProjectSettings·Packages·기존 Art 파일 변경은0이다.
검토 영상은 `.codex_tmp/unity-physics-jump-20260905/UnityPhysicsJumpPreview.gif`,
정적 요약은 같은 폴더의 `UnityPhysicsJumpContactSheet.png`다.

## 직접 실행

1. Unity6000.3.9f1에서 `Project hotfix` 프로젝트를 연다.
2. `Project Hotfix → Prototypes → Open Physics Jump Review`를 선택한다. 별도 검토 장면이 열린다.
3. Play 후 **Space 점프, WASD 월드축 이동, R 초기화**로 확인한다.

새 환경에서는 먼저 아래 명령으로 검토 transport를 생성한다. 기존 출력이 있으면 덮어쓰지 않는다.

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python-exit-code 1 --python tools/blender/export_c1b_rw020_physics_review.py -- .codex_tmp/unity-physics-jump-20260905/ApprovedCharacter.bytes
```

Unity Test Runner의 PlayMode에서 `ProjectHotfix.Physics.Tests`를 실행하면 물리 검증과 검토 캡처를 재실행한다.
검토 transport가 없는 환경에서는 모델 캡처 시험만 명시적으로 skip하며 일반 물리 시험은 실행한다.
이번 로컬 실행은 transport가 있어 skip0이었다. 실제 키보드 조작의 수동 사용성 검토는 후속이다.

## 남은 통합 범위

현재 몸통은 회전이 고정된 단일 Rigidbody·capsule이고 하체는 시각 pose다. 완전한 다관절 Ragdoll이 아니다.
보행·Sprint·화면축 이동·자동 facing, 지형별 발 IK와 moving platform, Down/GetUp·무기·패치·멀티플레이 연동은 남아 있다.
따라서 CHR-005/ANP-002는 IN_PROGRESS이며 CHR-001/002·UG-C1B/C2 전체 완료로 처리하지 않는다.
새 Player Build·commit/push는0이며 기존 inventory/license 미해결 항목도 별개다.

실행 방식은 Unity의 [Rigidbody.AddForce](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody.AddForce.html)와
테스트용 [PhysicsScene.Simulate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PhysicsScene.Simulate.html)를 사용한다.
