# 래그돌·물리 회복 검토 후보

승인 모델을 그대로 사용해 **실제 다관절 래그돌→지지/공간 확인→측면 정렬→몸 세우기→기존 이동 복귀** 후보를 연결했다.
기존 Grab 검토의 기본 동작으로 자동 채택하지 않았으며, 회복의 속도와 느낌은 사용자 시각 검토 대상이다.
이번 후보의 GetUp은 힘/회전 보조로 몸을 세우고 시각 뼈가 물리 몸체를 따라가는 방식이다. 별도로 제작한 손 짚기 GetUp clip의 완료를 뜻하지 않는다.

## 검토 장면

Unity 메뉴 `Project Hotfix > Prototypes > Open Ragdoll Recovery Candidate`를 열고 Play한다.

- `1/2`: 앞/뒤로 넘어뜨리는 검토 명령
- `3/4`: 좌/우로 넘어뜨리는 검토 명령
- `R`: Reset, `WASD`/`Shift`/`Space`: 기존 이동·달리기·점프
- 기존 마우스 tap/hold/release 규칙은 유지한다. Down에 들어가면 자기 손을 Throw 없이 해제한다.

숫자 키는 검토용 명시적 Down 명령이다. 일반 충돌이나 공격으로 자동 Down을 발생시키는 게임 규칙이 아니다.
컴포넌트는 Play의 Start에서 초기화해 씬/도메인 재로드 전 생성된 관리 상태에 의존하지 않는다.

## 물리와 표현

- torso1·head1·upper arm/forearm4·thigh/calf/foot6, 총12 Rigidbody를 사용한다.
- 총 질량20kg을 유지한다. 후보 분배는 torso8, head3, 양쪽 upper arm/thigh 각각1.25, forearm/calf 각각0.75, foot 각각0.5kg이다.
- Down 시작 시 현재 표현 pose를 권한 root 좌표로 옮겨 body pose를 만들고 선운동량을 분배한다. 종료 시 선운동량을 합쳐 기존20kg locomotion body로 돌아간다.
- 물리 위치/회전을 시각 뼈가 읽는다. 원본 `.blend`·mesh·weight·리그 저장은0이다.
- collider는 capsule이며 dynamic MeshCollider0, 자기 ragdoll body끼리 collision은 제외한다. 자기 교차 전수 보장이 아니다.
- 팔/다리의 작은 Y/Z angular cone 대신 해당 축을 잠그고 X축 제한과 drive를 사용한다. Head는 작은 다축 제한을 사용한다.
  현재 upper arm5°,forearm25°,thigh8°,calf12°,foot6°,head5° START다. 해부학적 최종 joint profile이 아니다.
- 활성 ragdoll은 Continuous Speculative 충돌 감지, 회전 속도 최대4rad/s, side 정렬은 최대2rad/s다.
- contact offset0.002m, ragdoll friction0.2(Minimum combine), solver iteration96/velocity16은 검토 시작값이다.
  2/3/4인 성능 검증 전 고정 production profile로 승격하지 않는다.
- joint projection으로 몸을 순간 이동시키지 않는다. side 정렬은 지지면 위에서 제한 torque로 진행하고 목표10° 이내/각속도 조건 후 공통 회복으로 이어진다.
- 기립 보조는 총 질량 기준 힘과 제한 torque를 사용한다. 기립 시작 후에는 고정한 지지점 아래 지형과 기립 공간을 다시 확인한다.
  몸을 들어 올리는 회복 구간에서는 torso의 바닥 밀착을 매 프레임 요구하지 않는다. 바닥/공간 상실은 같은 episode의 대기로 돌아간다.
- 충분히 세워지고 속도가 낮은 상태가0.2초 유지된 뒤 회복을 완료한다. 위치/회전을 목표값으로 순간 지정하지 않는다. 명시적 Reset은 별도 경로다.
- 복귀 때 질량·collision mode·interpolation·solver·angular damping/속도·depenetration 설정을 원래 locomotion 값으로 복원한다.

## 검증 범위

독립9조건: FaceUp/FaceDown/Left/Right 낙하, 천장, 천장 제거 후 재개, 회복 도중 Reset, 정지 측면2방향의 명시적 정렬 경로.
각 조건은60Hz로15초 검사한다. active 구간은2step마다, 그 밖은15step마다 skin 표면을 검사한다.

정지 측면2조건은 초기에 바닥에 가까운 pose를 준비하고, 안정 진입 동안만 중력을 끈 fixture다.
Down timer를 미리 소진시킨 뒤 실제 query와 alignment phase 진입을 확인하고 즉시 중력을 복원한다.
이는 **정렬 자체의 물리 경로를 분리해 검사하는 조건**이다. 일반 네 방향 낙하는 처음부터 중력을 켠 독립 조건이다.
일반 측면 낙하는 안정되기 전에 자연스럽게 앞/뒤로 굴러갈 수 있으므로 최초 낙하 방향과 회복 진입 방향을 같은 것으로 가정하지 않는다.

별도 Start/반복 검사에서는 서 있는 상태에서 Down, 회복 뒤 이동, 다시 Down과 두 번째 회복, 이후 Reset을 실행한다.
정상 복귀·Count·총 질량·기존 solver/collision 설정 복원과 Throw0을 확인한다. 이 반복 검사는 별도의 skin 전수 검사 조건이 아니다.

수치 판정은 정상 복귀1회, 천장/Reset의 의도된 복귀0회, joint anchor gap0.015m 미만,
복귀 bone head 이동0.015m 미만, 인접 surface angle45° 미만과 speed8m/s 미만이다.
기립 완료까지 실제 물리로 이동하며, 시각 pose 복귀의 작은 잔차는 별도로 기록한다.

재현: PlayMode `RagdollRecoveryReviewTests`.
영상은 네 방향 낙하와 별도 정지 측면 정렬을 구분한다. 원본 캡처20fps·8초이며 GIF에는 마지막 정지/반복 카드가 추가된다.

## 남은 검토

- 물리로 몸을 세우는 느낌과 속도는 사용자 검토 후 다음 후보에 반영한다.
- 공격 중/강하게 잡힌 상태의 Down 전환, 이동 표면/경사·복잡한 장애물에서 GetUp 중간 궤적, 다른 캐릭터와의 상호작용·자기 교차·2/3/4인 성능은 추가 검증 대상이다.
- 현재 capsule들은 별도 검토 근사치다. mesh 전체의 지면 침범/충돌체 적합성 전수 검사와 정식 physics profile은 후속이다.
- 피격→Down 규칙, 피해/무기, peer/production Gate 완료를 뜻하지 않는다. 던진 뒤 일반 충돌의 추가 Damage·피해 누적0을 유지한다.
- 현재 기본 이동·접지·펀치 원본을 교체하거나 Player Build·commit/push하지 않는다.

관절의 매우 작은 angular limit과 질량비, 회전 중 충돌 감지 방식은 Unity의 [joint 안정성 설명](https://docs.unity3d.com/cn/2018.3/Manual/RagdollStability.html)과 [Unity6 충돌 감지 모드 설명](https://docs.unity3d.com/ja/6000.0/Manual/choose-collision-detection-mode.html)을 참고해 비교했다. 채택 판단은 위 프로젝트의 실제 검사 결과를 기준으로 한다.

## 검증 결과

전체 EditMode108/108·PlayMode33/33 통과, skip0. 독립9조건의1,917표면 표본에서 최대 인접 면각16.09° 미만, 최대 joint gap14.60mm 미만, 복귀 bone head 이동1.15mm 미만이다.
문서 baseline·workflow7개·diff check 통과. 원본/승인 후보4개 SHA-256 일치. 두 영상은 각각160프레임·20fps·8초로 디코딩 확인했다.
[구조화 결과·영상·소스 snapshot](R20_RAGDOLL_RECOVERY_REVIEW_20260906.json)을 참고한다.

후속 사용자 피드백(2026-09-07): 물리 회복 느낌은 맞으며 실제 회복 애니메이션이 필요하다고 확인했다. [앞뒤 GetUp clip2 후보](R20_GETUP_ANIMATION_REVIEW_20260907.md)가 이 누락을 보완한다. 위 physics-only 검사는 당시 범위의 이력이다.
