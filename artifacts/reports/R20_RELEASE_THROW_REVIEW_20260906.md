# 잡기 버튼 release 던지기 — 로컬 입력 검토

사용자 결정: **클릭을 유지해 잡고, 놓으면 던진다.** 별도 들기 키·자동 들기·Lift 완료 조건은 없다.
기존 Lift API 검토 이후 남았던 입력 선택을 이 결정으로 대체했다.

## 현재 조작과 구현

- LMB/RMB 짧은 클릭은 기존 해당 손 Punch다. 유지하면 GrabSeek→실제 접촉→Grabbing으로 진행한다.
- 유효한 동적 대상을 잡은 손 버튼을 놓으면 joint를 즉시 풀고 bounded Throw impulse를 적용한다.
- 같은 대상의 양손 중 하나를 놓으면 그 손만 해제한다. 마지막 손을 놓을 때1회 던진다.
  예: LMB+RMB 유지→LMB 해제는 오른손 유지, 이후 RMB 해제는 Throw1회다. 동시 해제도1회다.
- 서로 다른 대상은 각 손의 release로 독립적으로 던진다. 다른 플레이어의 joint는 제거하지 않는다.
- 메뉴·Reset·대상/손 제어권 상실·joint break·도달 거리 이탈·강제 정리·Grab 미획득은 Throw0회다.
  up edge가 먼저 큐에 들어갔어도 같은 권한 tick에서 취소/무효화를 우선한다.
- 이전 sequence의 release가 새 Grab을 던지지 않도록 입력 admission을 확인한다.
- Unity 메뉴 `Project Hotfix > Prototypes > Open Grab Physics Review`의 기존 마우스 입력에 연결했다.

물리 START: impulse30N·s, 추가 속도 변화 최대3m/s, 현재 전방+위쪽0.35의 정규화 방향.
현재 이동 속도·중력·질량을 유지한다. 동일 체급20kg은30N·s,2kg proxy는6N·s다.
잡은 대상 점프의75% 부분 지원·하중에만 누적되는 GripStress·접지 절충 후보·승인 Punch는 그대로다.

## 검증

`ReleaseThrowReviewTests`는 명시적 `TryThrow` 호출 없이 실제 `SubmitButtons`→입력 큐→권한 tick 경로를 검증했다.
21조건: 좌우, 양손 동시/순차 해제, 서로 다른 대상,2kg, 공중/이동 중, 메뉴와 큐 뒤 메뉴,
주체/대상 Reset, 대상/손/권한 상실, 거리 이탈, joint 파괴, 강제 Grip 정리, 잡기 실패, 짧은 클릭, Presentation 없는 실행.

- 정상 던지기 조건1회, 서로 다른 두 대상2회. 미획득/강제 취소0회. 짧은 클릭만 Punch1회.
- 모든 조건 자동 Lift0. 최종 남은 잡기0. 주요 정상 조건 손-접촉점 오차는0.015m 미만.
- Presentation 유무에 따른 정지 던지기 최종 z 변위 차이는0.1mm 이내.
- 기존 Grab/jump/명시적 Lift 비교 검사는 새 release gesture를 꺼서 종전 물리 기준을 별도로 유지했다.
  현재 기본 입력은 새21조건이 담당한다. 기존 순수 admission 검사는 stale release와 reset/menu 재무장을 담당한다.
- 실제 하중 피로 누적은 기존 GripStress/Grab 검사에서 검증한다. 새 강제 Grip 정리 조건은 해당 해제 경로 뒤 release의 오발 방지를 검사한다.
- 영상은 고정 카메라에서 실제 렌더 프레임으로 캡처했다. 한 손·양손 순차·공중·이동4조건,120프레임·60fps·2초.
  GIF는30fps로 변환하고 마지막 정지/반복 카드를 추가했다.

## 범위와 후속

정지20kg 표적의 최종 전방 z 변위는 약0.235m, 이동 던지기는 약0.521m였다.
이는 이 검사 구간의 변위이며 던지기 전체 비행거리나 최종 재미 기준이 아니다.
현재는 release impulse와 팔의 기존 복귀까지 연결했다. 전용 던지기 몸통/팔 연출·Down/ragdoll 반응·최종 세기 조정은 후속이다.
벽/ledge 고정 잡기, WeaponUse, 정식 InputAction map, 실제2인/peer 및 production Gate 완료를 뜻하지 않는다.
원본 모델/리그 수정, Player Build, commit/push는 수행하지 않았다.

[구조화 검증·영상·소스 기록](R20_RELEASE_THROW_REVIEW_20260906.json)을 참고한다.

검증 결과: 전체 EditMode98/98·PlayMode28/28, skip0. 실제 표적 이동 여부를 추가 확인한 최종21조건 재검사도 통과했다.
문서 baseline·workflow7·art profile25(221 assertions)·diff check 통과. 원본/승인 후보4개 SHA-256 일치.
