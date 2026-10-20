# Unity 캐릭터 영상 캡처 정정 — 2026-09-06

사용자가 “바닥만 움직이고 캐릭터가 안 움직인다”고 지적했다. 확인 결과 **영상 캡처 코드의 오류**였다.
앞서 파일의 프레임 개수·전체 이미지 변화만으로 정상 재생이라고 판단한 것은 잘못이었다.
배경 이동만으로도 그 검사를 통과할 수 있었고, 캐릭터의 실제 표면 갱신을 검증하지 못했다.

## 원인과 재현

캡처 시험은 한 Unity player-loop frame 안에서 `PhysicsScene.Simulate`, 뼈 pose 계산,
`Camera.Render`를 여러 번 반복했다. 뼈 transform과 CPU `BakeMesh` 결과는 변했지만,
이 환경에서는 렌더용 skinned mesh가 이전 pose에 머물렀다. 따라가는 카메라와 바닥은 갱신돼
영상의 전체 프레임은 달라졌으나 캐릭터의 팔다리 표현은 멈춰 있었다.

배경 없는 캐릭터 mask를 사용해 두 pose를 비교했다.

| 조건 | 결과 |
|---|---|
| 같은 frame에서 pose A→B 계산 후 다시 촬영 | 화면 캐릭터 변화0% |
| 같은 frame 화면과 현재 CPU pose B 비교 | 실루엣 불일치 약6.24% |
| 뼈 pose B 계산 후 실제 player-loop frame을 넘겨 촬영 | CPU pose B와 실루엣 불일치0% |

새 frame 촬영은 renderer enable 토글이나 `BakeMesh` 호출보다 먼저 실행해,
실제 frame 경계만으로 갱신되는 것을 확인했다. 수치는 이 재현 장면의 foreground mask 기준이다.

## 수정

- 세 Unity 캡처 시험에서 pose 계산 뒤 `yield return null`로 실제 player-loop frame을 넘긴 후 촬영한다.
- 촬영 시 `Time.frameCount`가 pose 계산 때보다 증가했는지 검사한다.
- `SkinningCaptureTests`에 현재 CPU pose와 렌더된 캐릭터의 일치 검사를 추가했다.
- 걷기·달리기는 고정 카메라 영상도 추가해, 바닥과 캐릭터의 이동을 구별할 수 있게 했다.
- 원본 모델·웨이트·동작 설정과 게임 Runtime 코드는 이번 수정에서 변경하지 않았다.

수정 대상은 `FootContactComparisonTests`, `JumpTransitionTests`, `PhysicsJumpReviewCapture`와
새 `SkinningCaptureTests`다. 기준 폴더는 `Project hotfix/Assets/ProjectHotfix/Tests/PlayMode/Physics/`다.

## 정정된 증거

이전 Unity 물리 점프·8방향 전환·발 접지 비교 **영상은 팔다리 동작을 판단하는 시각 증거에서 제외한다.**
CPU 물리·뼈 수치와 영상의 유효성을 구분한다. Blender에서 직접 렌더한 펀치/이전 Blender 영상은 이 오류의 대상이 아니다.
이전 영상에 근거한 보행 스타일 선택 요청은 보류하며, 아직 어떤 후보도 기본값에 채택하지 않았다.

새 출력 루트는 `.codex_tmp/render-fix-20260906/`다.

- `Media/FixedCorrected.gif` / `.mp4`: 현재 기본 설정의 걷기·달리기, 고정 카메라.
- `Media/ComparisonCorrected.gif` / `.mp4`: 실제 skinned mesh가 갱신된 세 보행 후보 비교.
- `Transitions/`의 새 실행 폴더: 8방향 및 공중 변경의 재촬영 원본·비교 GIF.
- `Parity/`: 같은 frame의 잘못된 화면, 현재 CPU pose, frame 갱신 후 화면과 수치.

[구조화 기록](R20_SKINNING_CAPTURE_FIX_20260906.json)에 정정 출력과 검증 결과·소스 snapshot의 hash를 연결했다.
전체 PlayMode17/17 통과, 재현 검사는 추가 단독 실행에서도 통과했다.
영상은60fps·120frame MP4를 실제 디코딩해 확인했고, GIF는30fps 추출과 명시적인 반복 안내 화면을 사용한다.
이는 캡처 오류 수정이며 보행 후보의 최종 스타일 승인이나 게임 전체 통합 완료가 아니다.
