# Project Hotfix 핵심 Foundation 선택 근거서

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| 문서 목적 | 기초 구현에서 무엇을 선택했고 왜 그렇게 선택했는지, 배제한 대안과 검증 근거를 한 문서에서 설명 |
| 기준 Git revision | `0eeceee` (`chore: enable Git LFS asset workflow`) |
| 포함 범위 | `FDN-001..011`, `ART-001`, `LIC-001`, `BLD-001` |
| 작성 기준 | 실제 repository, versioned config, test 결과와 Evidence를 우선 사용. 직접 기록되지 않은 선택 연결은 `추론/설계 해석`으로 표시 |
| 문서 성격 | 설명용 결정 근거서. PRD/SRS/분야별 사양을 대체하거나 새 제품 계약을 만들지 않음 |

> **범위 주의:** 핵심 기술 Foundation `FDN-001..011`, 첫 Art Profile `ART-001`, source 기준
> 라이선스/NOTICE inventory `LIC-001`과 Windows x64 Profile 준비 `BLD-001`은 완료됐다. 하지만 C1b와
> Gameplay는 구현되지 않았고 Player Build도 실행하지 않았다. 실제 Player 포함물 기준 최종 NOTICE 감사는
> 사용자가 Build한 뒤에만 가능하다. 따라서 이 문서는 “G0 전체 완료”, “게임 완성” 또는 “Steam 기능 구현”을
> 선언하지 않는다.

규범적 우선순위는 계속 [문서 인덱스](../../docs/00_DOCUMENT_INDEX.md)의
`최신 사용자 결정 → PRD → SRS → 분야별 사양 → 구현계획 → Evidence` 순서를 따른다.

---

## 1. 결론부터 요약

Foundation의 핵심 방향은 다음 한 문장으로 정리된다.

> **하나의 Unity 게임 클라이언트가 방장일 때 AuthorityHost가 되고, 별도 서버·Backend·DB·Docker 없이
> 친구 2~4명이 P2P로 플레이하도록 만들되, Gameplay 권한·물리·입력·전송·표현·로컬 저장·Art 반입을
> 서로 교체하고 검증할 수 있는 경계로 분리한다.**

이 방향 때문에 다음 선택이 서로 연결됐다.

| 제품 제약 | Foundation 선택 | 보호하려는 결과 |
|---|---|---|
| 방장 Client가 권한 Host | Simulation을 독립 모듈로 두고 Presentation이 직접 변경하지 못하게 함 | Host 판정의 단일 소유권 |
| Alpha는 LAN/direct, 이후 Steam P2P/SDR | low-level Unity Transport를 교체 가능한 Adapter 경계 뒤에 둠 | Alpha용 전송을 Steam 제품 경로로 교체 가능 |
| 별도 Backend·DB·Cloud 없음 | 향후 Settings/Preset이 사용할 bounded local atomic storage primitive 마련 | 서버 운영 없이 손상 복구 가능한 로컬 저장 기반 |
| Rigidbody·Ragdoll 중심 물리 난투 | Unity built-in PhysX와 60Hz fixed-step 사용 | Contact·Joint·Ragdoll을 동일 Physics stack에서 검증 |
| 복잡한 tap/hold/chord·Esc rearm 입력 | New Input System만 활성화 | 중복 Backend 없이 명시적 Action/Context 구현 가능 |
| 2·3·4인과 UI 없는 Simulation 검증 | 같은 runtime kernel을 Unit/EditMode/PlayMode에서 실행 | 화면이나 Scene 없이 Authority 로직 회귀 검증 |
| Blender와 Unity 결과의 품질 일치 | Toolchain exact lock + ModelInterop/VisualQA START profile | 에셋별 수동 보정과 품질 편차 방지 |
| `.blend/.fbx` production source 저장 | repository-local Git LFS + private `origin/main`, existing PNG migration 0 | 대형 source가 Git history를 비대하게 만들지 않도록 함 |
| Docker·Dedicated·자체 Backend 영구 제외 | 경로·코드·Package 기반 금지 인프라 Guard | 시간이 지나며 금지 구조가 조용히 재유입되는 것 방지 |
| 외부 Package·Asset의 출처/권리 누락 금지 | 58 Package와 18 review image의 fail-closed license inventory | 미등록 에셋의 빌드 반입과 NOTICE 누락 방지 |
| 사용자가 직접 Build | Windows x64 Development·Steam Reserved Profile과 수동 절차만 준비, Player Build 0 유지 | 코드 검증과 배포 권한을 분리 |

---

## 2. Foundation이 따라야 했던 고정 제품 계약

### 2.1 네트워크와 실행 구조

- 별도 Dedicated Server는 없다.
- 별도 Backend, Coordinator, Database, Blob Store, Bake Worker도 없다.
- Docker, OCI, Compose, Container Image를 개발·시험·배포 경로에 사용하지 않는다.
- 방장 Unity Client 안의 `AuthorityHost`가 Lobby와 Match의 최종 판정을 소유한다.
- Alpha는 통제된 LAN 또는 명시적 direct endpoint만 검증한다.
- Alpha 승인 뒤 Steam Auth, Friends Lobby, 친구 초대, 방 코드와 Steam P2P/SDR을 붙인다.
- 공개 매칭, Server Browser, Rank와 MMR은 후속 후보가 아니라 영구 비범위다.

이 계약 때문에 “일단 전용 서버나 Relay로 쉽게 만들고 나중에 P2P로 바꾸기”는 선택하지 않았다.
그렇게 시작하면 Authority 수명주기, 접속 식별자, Lobby와 배포 구조가 서버 중심으로 굳어져
나중에 제거하는 비용이 커진다.

### 2.2 게임 Simulation

- 게임은 Rigidbody, Collider, Joint, Grab, Throw, Ragdoll, Weapon recoil을 사용하는 물리 난투다.
- Host가 Position, Hit, Damage, Down, Score, Patch와 Weapon 결과를 확정한다.
- Input과 Presentation은 결과를 요청하거나 보여줄 수 있지만 권한 결과를 직접 만들지 않는다.
- 2인뿐 아니라 3인과 4인을 별도 검증해야 한다.
- Match UI가 없어도 Simulation이 실행돼야 한다.

따라서 Foundation에서부터 Rendering과 Authority logic을 한 Assembly에 섞지 않았고,
물리 tick과 Network cadence를 같은 수치로 가장하지 않았다.

### 2.3 제작·품질

- Unity `6000.3.9f1`, Blender `5.2.0 LTS`, URP `17.3.0`을 기준으로 한다.
- `1 Blender meter = 1 Unity unit = 1 meter`다.
- Character·Weapon·Map asset은 같은 Style/Interop/QA Profile을 사용한다.
- 승인된 C2PA review image는 사람이 디자인을 참고해 새 원본 Asset을 제작하는 용도로만 사용하고, 원본
  파일·재인코딩본·기계적 파생물을 Player/Steam media에 넣지 않는다.
- 새 외부 Asset은 source, 상업적 사용권과 의도한 형태의 재배포 권리를 증명하기 전까지 shipping을 차단한다.
- 최종 Palette, Bevel 폭, Product Shader/Lighting, Camera와 성능 예산은 실제 비교와 사용자 Gate 전에는
  `LOCKED`로 만들지 않는다.
- Player Build와 Steam 배포는 사용자가 직접 수행한다.

즉, “FBX가 생성됐다”거나 “Unity가 Import했다”는 사실만으로 Art 품질을 통과 처리하지 않는다.

---

## 3. 최종 모듈 경계와 의도

현재 Runtime Assembly 관계는 다음과 같다.

```text
ProjectHotfix.Contracts  (read-only/value boundary, no UnityEngine)
    ↑          ↑          ↑          ↑
Simulation  Presentation  Input      Transport
                              \        /
                     package ownership only
                     InputSystem     Unity Transport

ProjectHotfix.LocalStorage  (독립 leaf, Unity/Network 참조 0)
```

정확한 의미는 다음과 같다.

- `Simulation`, `Presentation`, `Input`, `Transport`는 `Contracts`만 Project reference로 가진다.
- `Presentation → Simulation` 직접·간접 경로는 없다.
- `LocalStorage`는 다른 Project Assembly와 UnityEngine, Transport를 참조하지 않는다.
- 현재 Composition Root는 아직 만들지 않았다. 실제 wiring은 첫 Runtime 기능 Task가 소유한다.
- 현재 `Transport`는 package 선택과 marker 경계만 있고 실제 socket Adapter는 `NET-001..003` 범위다.
- 현재 `SimulationKernel`은 Foundation cycle 경로를 검증하는 최소 kernel이다. 완성된 Gameplay가 아니다.

이 구조를 선택한 가장 큰 이유는 Alpha direct UTP를 나중에 Steam Networking Sockets/SDR로 바꾸더라도
Gameplay Simulation과 Presentation 계약을 다시 작성하지 않기 위해서다.

[현재 ModuleGraph](../../config/architecture/ModuleGraph.yaml)

---

## 4. Task별 선택 이유와 근거

## 4.1 `FDN-001` — 기존 자료를 보존한 단일 Root Repository

### 선택

- `party game` root에 Git repository를 하나만 둔다.
- Unity Project는 그 아래 `Project hotfix/`에 둔다.
- 초기화 전에 존재하던 문서·Evidence·Review 자료를 이동하거나 삭제하지 않는다.
- Remote, Unity Project 생성, Build는 이 Task에서 하지 않는다.

### 이유

다음은 별도 후보 비교 실험이 아니라 repository 추적 요구에서 도출한 **설계 해석**이다.
기획 문서, 승인 이미지, Evidence와 Unity Project가 서로 다른 Git repository로 갈리면
코드 revision과 결정 revision을 함께 추적하기 어렵다. 특히 Blender source, Unity Prefab과 승인 capture를
나중에 한 GenerationManifest로 연결하려면 최상위 repository가 하나여야 한다.

### 배제한 대안

- Unity Project 안에 중첩 `.git`을 두는 방식
- 기존 자료를 새 구조에 맞추기 위해 먼저 이동·정리하는 방식
- 초기화와 동시에 Remote나 LFS를 임의 구성하는 방식

### 실제 근거

- 초기·이후 파일 `75/75` 보존
- byte 수 `28,352,603` 동일
- tree SHA-256 동일
- root Git repository `1`, nested repository `0`, Remote `0`

[FDN-001 Evidence](../evidence/G0/FDN-001/EV-FDN-001-20260826-r01.yaml)

### 아직 증명하지 않은 것

Remote backup, 협업 workflow, LFS fetch/push와 Unity Project 동작은 이 Task의 범위가 아니었다.

---

## 4.2 `FDN-010` — Unity·Blender·Package Exact Version Lock

### 선택

- Unity `6000.3.9f1`, revision `7a9955a4f2fa`
- Blender `5.2.0 LTS`
- URP `17.3.0`
- 현재 채택 Package를 `manifest.json`과 `packages-lock.json` hash로 고정
- 자동·조용한 Upgrade 금지

[현재 ToolchainProfile](../../config/toolchain/ToolchainProfile.yaml)

### 이유

이 프로젝트는 물리와 Blender→Unity 반입 품질에 민감하다. “Unity 6 계열”, “Blender 5 계열”처럼
Family만 기록하면 다음 항목이 조용히 달라질 수 있다.

- ModelImporter option과 기본값
- PhysX 동작과 ProjectSettings schema
- Input/Transport package API
- URP Shader와 Material 동작
- Blender FBX Export option과 axis 처리

Exact patch와 lock hash를 기준으로 잡으면 Upgrade가 생겼을 때 “원인 불명의 품질 변화”가 아니라
명시적인 Profile revision으로 다룰 수 있다.

### 배제한 대안

- 항상 최신 LTS patch를 자동 적용
- `Unity 6.3 LTS`, `Blender 5.2 LTS` Family 문자열만 기록
- Package version은 manifest만 보고 transitive lock은 추적하지 않음

### 실제 근거

초기 Evidence는 이미 Input System `1.18.0`을 포함한 URP template의 direct package `45`, lock entry `57`을
기록했다. `FDN-004`는 이 기존 Input package를 New-only Backend로 채택했지만 lock hash를 바꾸지 않았고,
`FDN-005`도 built-in Physics를 선택해 package lock 변화가 `0`이었다. direct `46`, lock `58`과 새 hash가 된
직접 원인은 `FDN-006`에서 Unity Transport `2.6.0`을 추가한 것이다. 현재 ToolchainProfile r02는 이
명시적 UTP 추가 결과를 반영한다.

[FDN-010 Evidence](../evidence/G0/FDN-010/EV-FDN-010-20260826-r01.yaml)

### 아직 증명하지 않은 것

Windows Player, 실제 FBX Import parity와 향후 Package 호환성은 별도 Task에서 검증한다.

---

## 4.3 `FDN-011` — Unity 친화적 Repository·Binary 정책

### 선택

- Unity 생성 폴더 `Library`, `Temp`, `Obj`, `Logs`, `UserSettings`, Build output은 Git에서 제외
- `Assets/**/*.meta`, ProjectSettings와 Package lock은 추적
- Unity serialization은 text 기반으로 유지
- 현재 binary를 hash inventory로 기록
- 초기 r01에서는 Git LFS와 Remote가 없으므로 production binary commit을 금지
- 사용자 승인 뒤 r03에서 repository-local Git LFS와 private GitHub `origin/main`을 연결하고 향후
  `.blend/.fbx/.glb`·lossless source pattern `10개`를 LFS로 고정
- 기존 review PNG `18개`와 기존 Git history는 migration/history rewrite 없이 ordinary Git에 유지

[Binary 정책](../../config/repository/BinaryAssetPolicy.md) ·
[Binary inventory](../../config/repository/BinaryAssetInventory.yaml)

### 이유

Unity의 `Library`와 Cache는 재생성 가능하고 Machine마다 달라지지만, `.meta` GUID는 Asset reference의 일부다.
따라서 생성물을 commit하면 noise와 용량이 커지고, `.meta`를 무시하면 Scene·Prefab reference가 깨진다.

큰 `.blend`, `.fbx`, `.glb`를 LFS 설정 없이 먼저 commit하면 나중에 LFS를 붙여도 Git history가 이미
무거워진다. 그래서 “LFS가 없으니 그냥 일반 Git에 넣기”가 아니라 “연결되기 전 후보 commit 금지”를 택했다.
실제 production source를 만들기 직전에는 그 금지를 해제하는 대신 local filter·pre-push hook·private Remote와
검증기를 함께 설치했다. 현재 PNG는 최대 약 1.96 MiB이고 review-only이므로 14개 commit을 다시 쓰는 LFS migration
이득보다 history·Evidence identity 손실이 더 크다.

### 배제한 대안

- `Library`와 Build 결과까지 모두 commit
- `.meta` 전체 ignore
- 설치되지 않은 Git LFS filter를 설정 파일에만 적어 사용 가능한 것처럼 처리
- 기존 PNG까지 LFS migrate하고 14개 commit SHA를 전부 rewrite
- global/system 범위에 LFS 설정을 적용하거나 force-push를 허용
- Binary file을 이름만 기록하고 hash는 남기지 않음

### 실제 근거

- ignore boundary test `7/7`, tracked boundary test `7/7`
- generated status leak `0`
- binary `20개`, 총 `27,722,987 bytes`, unique hash `15`
- `10 MiB` 초과 binary `0`, 현재 LFS-required 후보 `0`
- Git LFS unavailable, Remote `0`, LFS 후보 commit 허용 `false`

[FDN-011 초기 Evidence](../evidence/G0/FDN-011/EV-FDN-011-20260826-r01.yaml) ·
[LFS·Remote 보충 Evidence](../evidence/G0/FDN-011/EV-FDN-011-20260829-r02.yaml)

위 `20개 / 27,722,987 bytes / unique hash 15 / LFS unavailable / Remote 0`은 FDN-011 완료 당시의 r01
역사값이다. `LIC-001`은 불필요·출처 불명 파일을 제거해 r02 `18개 / 26,990,163 bytes / unique hash 13`으로
정리했다. 사용자 승인 r03은 Git LFS `3.8.0`, LFS pattern `10`, private `origin/main`, initial push HEAD 일치,
current LFS file/candidate `0`, PNG migration/history rewrite `0`을 추가했다. 과거 Evidence는 당시 사실이므로
덮어쓰지 않는다.

### 아직 증명하지 않은 것

아직 실제 production `.blend/.fbx/.glb` LFS object가 없으므로 pointer upload/download 왕복은 증명하지 않았다.
첫 production source commit에서 working bytes, index pointer, LFS object upload와 fresh fetch를 다시 확인해야 한다.
Remote backup은 Player Build·Steam 배포 또는 public publication을 의미하지 않는다.

---

## 4.4 `FDN-002` — Unity 6.3 LTS URP Project 유지

### 선택

- 사용자가 만든 Unity Project를 `6000.3.9f1`로 Import
- URP `17.3.0` 유지
- PC Quality에 URP Pipeline Asset 연결
- Force Text serialization 유지
- Player Build는 실행하지 않고 Editor Import/C# compile만 검증

### 이유

Unity 사용은 사용자 확정 사항이다. 저폴리 Character·Weapon·Environment와 하나의 Shared Camera를 사용하는
Alpha에서 URP는 이미 생성된 Template과 일치하며, 필요한 Lit/Unlit·Decal·제한된 Post Effect를 제공한다.

다음은 실제 benchmark 후보 비교가 아니라, 승인 Toolchain과 Alpha 범위에서 도출한 **설계 해석상
채택하지 않은 대안**이다.

- HDRP: 저폴리 Party Game Alpha에 필요하지 않은 고급 Rendering·설정 복잡도 증가
- Built-in Render Pipeline로 회귀: 이미 해결된 URP Project와 Material 경계를 버리고 별도 Migration 발생
- 다른 Engine으로 변경: 승인된 Unity physics·toolchain·문서 전체와 충돌

### 실제 근거

- Unity batchmode exit `0`
- C# compiler error `0`, compile failure `0`
- URP Graphics/PC Quality Pipeline 연결 확인
- Source asset change `0`, Package lock hash change `0`
- Player Build `0`

[FDN-002 Evidence](../evidence/G0/FDN-002/EV-FDN-002-20260826-r01.yaml)

### 아직 증명하지 않은 것

실행 화면 품질, Player 성능, Windows Build와 Gameplay Scene은 검증하지 않았다.
Unity Personal entitlement는 정상 해석됐지만 token refresh와 종료 중 Curl 42 비차단 경고가 각각 `1회`
있었다. 원본 Editor log는 local IP·Machine·Licensing 식별자를 포함해 보존하지 않고 요약만 남겼다.

---

## 4.5 `FDN-003` — Compile-time Module Boundary

### 선택

초기에는 다음 다섯 Runtime Assembly를 만들었다.

- `ProjectHotfix.Contracts`
- `ProjectHotfix.Simulation`
- `ProjectHotfix.Presentation`
- `ProjectHotfix.Input`
- `ProjectHotfix.Transport`

이후 `FDN-008`이 독립 leaf인 `ProjectHotfix.LocalStorage`를 추가했다.

### 이유

- `Contracts`를 read-only/value 경계로 두면 Presentation과 Transport가 Simulation 구현에 결합하지 않는다.
- Presentation이 Simulation을 직접 참조하지 않으면 VFX/UI가 권한 상태를 변경하는 실수를 compile-time에 줄인다.
- Transport가 독립이면 Alpha UTP와 Steam Adapter가 같은 상위 Protocol/Simulation 계약을 사용할 수 있다.
- Input을 독립시키면 Lobby, Match, UI, Cursor, tap/hold/chord 상태가 Gameplay 구현과 뒤엉키지 않는다.

### 배제한 대안

FDN-003 Evidence에 정식 후보 비교표는 없다. 아래 항목은 확정된 ModuleGraph와 Guard가 막도록 설계된
경로를 역으로 정리한 **설계 해석**이다.

- 모든 코드를 하나의 Assembly-CSharp에 두는 방식
- Presentation이 Simulation concrete type을 직접 참조
- 모든 asmdef를 `autoReferenced:true`로 두고 실제 의존성을 숨기는 방식
- Foundation 단계에서 Gameplay interface를 추측해 미리 대량 생성

### 실제 근거

FDN-003 시점:

- Runtime Assembly `5`, Project reference edge `4`
- Cycle `0`
- Presentation→Simulation path `0`
- Runtime folder ownership mismatch `0`
- EditMode architecture test `4/4`
- Gameplay type `0`

현재 ModuleGraph는 LocalStorage를 포함한 `6`개 Runtime module을 추적한다.

[FDN-003 Evidence](../evidence/G0/FDN-003/EV-FDN-003-20260826-r01.yaml)

### 아직 증명하지 않은 것

향후 Command/ReadModel의 의미가 안전하다는 것까지 자동 증명하지 않는다. 새 public contract는 해당 Task에서
다시 검토해야 한다.

---

## 4.6 `FDN-004` — New Input System만 사용

### 선택

- `com.unity.inputsystem 1.18.0`
- Project Input backend를 `Input System Package (New)`로 고정
- Legacy Input Manager와 Both mode 비활성
- Input System package reference는 `ProjectHotfix.Input`만 소유
- 제품 Action Map은 아직 만들지 않음

[Input 결정](../../config/input/InputPackageDecision.yaml)

### 이유

이 게임은 단순 WASD만 필요한 것이 아니다.

- Lobby/Match/UI Context 전환
- L/R Mouse tap Punch와 hold Grab
- 공중 L/R Kick과 dual-click Dropkick chord
- Esc Cursor open/close와 Mouse all-up rearm
- Tab Hold/Toggle
- local-only non-pausing Match menu

두 Input backend를 같이 켜면 같은 물리 입력이 중복 처리되거나 Context별 edge가 달라질 수 있다.
New Input System 하나로 고정하고 Action Map과 resolver를 후속 Task에서 명시적으로 만드는 편이 안전하다.

### 배제한 대안

- Legacy Input API 유지
- Both mode로 전환 기간을 길게 가져감
- Template Action Asset을 곧바로 제품 Action Map으로 간주
- Foundation에서 tap/hold threshold와 모든 binding을 조기 확정

### 실제 근거

- New backend `true`, Legacy `false`, Both `false`
- Package direct Registry dependency 확인
- Runtime package owner `1`, 다른 module reference `0`
- Legacy runtime API hit `0`
- Input EditMode test `4/4`, 전체 Architecture test `8/8`
- 제품 Action Map·callback 구현 `0`

[FDN-004 Evidence](../evidence/G0/FDN-004/EV-FDN-004-20260826-r01.yaml)

### 아직 증명하지 않은 것

Legacy API 검사는 lexical guard이며 hostile alias를 해석하는 semantic analyzer가 아니다. 제품 Action Map,
callback, Input update timing, fixed-step 소비, rebinding 저장과 실제 Gameplay control·Player Build는 아직 없다.

---

## 4.7 `FDN-005` — Built-in PhysX, Physics 60Hz와 Authority 30Hz

### 선택

- Unity built-in 3D Physics/PhysX 사용
- Physics fixed-step `60Hz` (`1/60s`)
- Authority/Network cycle은 Physics `2 step`마다 `30Hz`
- Snapshot `20Hz`는 `3 step`, `15Hz`는 `4 step`
- `FixedUpdate` simulation, `autoSyncTransforms=false`
- DOTS Physics나 Havok 같은 두 번째 Physics stack 없음

[Physics 결정](../../config/physics/PhysicsPackageDecision.yaml)

### 이유

Character와 Weapon이 Rigidbody, Collider, ConfigurableJoint, Grab constraint와 Ragdoll을 함께 사용한다.
Unity GameObject 기반 PhysX는 이 요구와 직접 맞고, 이미 선택한 Unity Client 구조 안에서 Authoring과 Debug가 쉽다.

60Hz를 선택한 이유는 다음과 같다.

- 빠른 Punch/Kick/Weapon contact와 Joint를 50Hz보다 촘촘하게 관찰
- 30Hz Authority cycle을 정확히 `2` Physics step으로 구성
- 20Hz/15Hz Snapshot도 각각 `3`/`4` step으로 나눠 cadence를 섞지 않음

이것은 “네트워크 결정론”을 보장하려는 선택이 아니다. Host Authority에서 Contact와 Control의 기준 tick을
명확히 하기 위한 선택이다.

### 배제한 대안

다음 세 항목은 PhysicsPackageDecision에 기록된 공식 rejected option이다.

- Unity default 50Hz를 문서의 60Hz 계약과 다르게 유지
- Physics와 Network를 모두 30Hz로 낮춤
- DOTS/Havok을 추가해 두 Physics 세계를 유지

추가로 60Hz Snapshot을 기본 전송 cadence로 삼지 않고 Runtime의 임의 `fixedDeltaTime` 변경을 막은 것은
승인 cadence를 보호하기 위한 **Guard 설계 해석**이다.

- 60Hz Snapshot을 보내 Bandwidth와 전송 결합을 키움
- Runtime code가 `Time.fixedDeltaTime`을 임의 변경하도록 허용

### 실제 근거

- built-in Physics direct package, second stack `0`
- fixed delta `0.016666668`, simulation mode FixedUpdate
- cadence `60/30`, step ratio `2/3/4`
- Physics guard `4/4`, 전체 EditMode `12/12`
- 격리 PlayMode Contact/Joint smoke `2/2`, Scene unload `2/2`

초기 실패 run은 PhysicsScene 구성·API 사용 문제를 드러냈고 최종 Evidence에는 superseded failure도 요약돼 있다.

[FDN-005 Evidence](../evidence/G0/FDN-005/EV-FDN-005-20260827-r01.yaml)

### 아직 증명하지 않은 것

Ragdoll feel, Character mass·joint limit, 실제 hit stability와 Network scheduler는 후속 Gameplay/NET Task다.

---

## 4.8 `FDN-006` — Low-level Unity Transport 2.6 Adapter 방향

### 선택

- `com.unity.transport 2.6.0`
- `ProjectHotfix.Transport`만 package를 참조
- NGO, Unity Relay, Unity Lobby, Multiplayer Services 사용 안 함
- Alpha는 UTP direct endpoint
- Steam 단계에서는 Adapter를 Steam Networking Sockets/SDR로 교체
- 이 Task에서는 Adapter 구현을 시작하지 않고 package·ownership만 확정

[Transport 결정](../../config/transport/TransportPackageDecision.yaml)

### 이유

UTP는 low-level transport이므로 Host-authoritative Protocol과 Replication을 직접 소유하면서도 socket backend를
나중에 교체할 수 있다. 반면 NGO를 먼저 채택하면 Object lifecycle과 RPC 의미가 Gameplay 구조에 빨리 스며들고,
Unity Relay/Lobby를 붙이면 사용자가 명시적으로 제외한 Hosted Service 의존성이 생긴다.

Alpha의 목적은 인터넷 제품 경로를 완성하는 것이 아니라, 같은 Simulation 계약으로 2·3·4인 direct P2P
vertical slice를 검증하는 것이다. 그래서 NAT traversal을 어설프게 자체 구현하거나 Alpha UTP를 Steam 출시
fallback으로 남기지 않는다.

### 배제한 대안

- NGO를 상위 Gameplay 구조까지 함께 채택
- Unity Relay/Lobby/Authentication으로 Alpha 연결을 구성
- 자체 NAT traversal, public server list나 coordinator 구현
- Steam SDK를 Alpha 기능 검증 전에 바로 통합
- raw socket 구현을 Gameplay module에 직접 넣음

### 실제 근거

- UTP `2.6.0`, Registry/direct dependency
- UTP Runtime owner `1`
- NGO/Multiplayer Services/Relay/Lobby package `0`
- public discovery, NAT traversal, 별도 server process 구현 `0`
- Transport EditMode test `4/4`, 전체 EditMode `16/16`
- Adapter implementation source `0`

[FDN-006 Evidence](../evidence/G0/FDN-006/EV-FDN-006-20260827-r01.yaml)

### 아직 증명하지 않은 것

Socket bind, Loopback, LAN, Packet protocol, loss/reorder, NAT와 Steam P2P/SDR은 아직 검증하지 않았다.

---

## 4.9 `FDN-007` — 같은 Runtime Kernel을 쓰는 Renderless Test/Evidence 기반

### 선택

- 최소 `SimulationKernel`과 read-only `SimulationSnapshot` 생성
- Unit/EditMode/PlayMode가 같은 Runtime kernel을 실행
- PlayMode는 빈 격리 Scene에서 root object `0`으로 실행
- Evidence manifest와 NUnit XML을 strict validator로 검증
- 실패·skip·not-run을 성공으로 취급하지 않음

[TestFoundation](../../config/testing/TestFoundation.yaml) ·
[EvidenceProfile](../../config/evidence/EvidenceProfile.yaml)

### 이유

Gameplay를 테스트 전용 모형으로 다시 구현하면 제품 코드와 테스트 코드가 서로 다른 결과를 낼 수 있다.
Foundation부터 같은 kernel을 UI·Rendering 없이 실행하면 다음 Gameplay Task가 같은 경로에 상태를 추가할 수 있다.

Evidence도 사람이 “통과한 것 같다”고 적는 방식 대신, 필수 field·raw path·UTC·test count를 구조적으로 검사해야
나중에 2·3·4인 결과와 Steam/Build 결과를 과장하지 않는다.

### 배제한 대안

- Scene/MonoBehaviour 안에서만 Simulation 실행
- 테스트 전용 Gameplay 복사본 사용
- Test 결과 XML의 `result=Passed`만 보고 skip/not-run을 무시
- 임시 Unity log 전체를 민감정보와 함께 repository에 보존

### 실제 근거

- 같은 Runtime kernel Unit `3/3`, EditMode harness `2/2`, PlayMode harness `1/1`
- 두 번의 `120` Authority cycle fingerprint 일치
- Presentation reference `0`, Rendering/UI dependency `0`
- 전체 EditMode `21/21`, PlayMode `3/3`
- 기존 Evidence `35`개와 strict manifest 구조 검증
- NUnit negative fixture `5`종 거부

[FDN-007 Evidence](../evidence/G0/FDN-007/EV-FDN-007-20260827-r01.yaml)

### 아직 증명하지 않은 것

현재 fingerprint는 Foundation counter 수준이다. Gameplay determinism, Physics determinism, Network impairment,
Player Build와 Steam을 증명하지 않는다.
Unit layer도 별도 native executable이 아니라 Unity EditMode runner가 실행한 순수 NUnit 경로다.
Evidence/NUnit validator는 schema·path·count를 확인하는 구조 Guard이며, 미래 Gameplay 의미 검증이나
적대적 Security review를 대신하지 않는다.

---

## 4.10 `FDN-008` — Backend 없는 Local Atomic Storage

### 선택

- `ProjectHotfix.LocalStorage`를 Unity/Network 참조 없는 leaf Assembly로 구성
- caller가 최대 payload byte를 명시하는 bounded raw-byte storage
- Envelope: magic `8 bytes` + format version + payload length + SHA-256 `32 bytes` + payload
- `current`, `last-good`, 같은 directory의 `pending` 파일
- 첫 저장은 flush+검증 뒤 move, 이후 정상 저장은 `File.Replace`
- current가 손상됐으면 기존 last-good을 덮지 않고 current만 교체
- Read는 current와 last-good 상태를 따로 반환하고 자동 self-heal write를 하지 않음
- 같은 schema validator를 Read/Write에 사용해 비호환 current가 정상 last-good을 덮지 않게 함
- 같은 process의 여러 repository instance는 `64`개 bounded striped lock으로 같은 주소를 직렬화

[LocalStorageProfile](../../config/storage/LocalStorageProfile.yaml)

### 이유

이 게임은 자체 계정·Cloud Save·Match History가 없고 친구 P2P만 사용한다. Settings와 Appearance Preset은
로컬 파일이면 충분하다. 다만 “그냥 JSON 파일 한 개 덮어쓰기”는 중간 종료나 손상 때 정상본을 잃을 수 있다.

SHA-256과 version/length envelope를 사용한 이유는 암호화가 아니라 손상·잘린 파일·과대 길이를 allocation 전에
검출하기 위해서다. `last-good`을 자동으로 current에 복사하지 않는 이유는 원본 손상을 보존해 사용자 복구·삭제
선택을 가능하게 하기 위해서다.

### 배제한 대안

- Backend/Cloud/Database에 Preset 저장
- Docker로 로컬 DB나 저장 Service 실행
- PlayerPrefs만 사용해 version/length/last-good 없이 저장
- `Delete + Move` 같은 비원자적 fallback
- Read 시 손상 파일을 조용히 자동 덮어쓰기
- FDN-008에서 Preset JSON schema, 최대 10개, list/rename/delete/UI까지 한꺼번에 구현

### 실제 근거

- Runtime source `4`, Project/Unity/Network reference `0`
- Storage EditMode `19/19`, boundary `2/2`, PlayMode `1/1`
- 전체 EditMode `42/42`, PlayMode `4/4`
- current 손상 variant `4`, declared/physical length bound fixture 각 `1`
- 기존 backup이 있는 세 번째 write rotation, validator-aware last-good 보존, cross-instance 직렬화 검증
- pre-commit Replace failure에서 current/last-good 보존

[FDN-008 Evidence](../evidence/G0/FDN-008/EV-FDN-008-20260827-r01.yaml)

### 아직 증명하지 않은 것

- 실제 Client process 재시작과 `Application.persistentDataPath` binding
- Windows filesystem, 전원 손실과 directory fsync
- cross-process writer
- hostile local symlink/TOCTOU와 악의적 변조 방지
- Preset/Settings schema·migration·최대 10개·list/delete/UI

또한 public constructor에서 발생하는 OS 예외는 local absolute path를 포함할 수 있다. 향후 Composition은
이를 catch해 사용자용 오류로 mapping해야 하며, 원문 예외나 local path를 UI 또는 Network payload에
직접 노출하면 안 된다.

SHA-256은 인증·암호화가 아니라 accidental corruption 검출용이다.

---

## 4.11 `FDN-009` — 금지 인프라 재유입 방지 Guard

### 선택

- Git tracked + non-ignored untracked inventory를 검사
- Docker/Compose/OCI, DB artifact/dependency/API, Backend SDK/API, Dedicated build/profile/process를
  path·content·package rule로 검사
- 모든 policy rule을 negative fixture와 연결
- tracked missing, required manifest missing, symlink, oversized/invalid input을 fail-closed 처리
- UTP AuthorityHost, Steam P2P/SDR 표현과 Unity 기본 설정은 허용
- 일반 `server`, `listen`, `relay`, `socket` 단어 자체는 금지하지 않음

[금지 인프라 정책](../../config/infrastructure/ForbiddenInfrastructurePolicy.yaml) ·
[검증기](../../tools/verify_forbidden_infrastructure.rb)

### 이유

“서버 안 쓴다”는 문서만으로는 시간이 지나며 SDK, BuildProfile, CI workflow나 DB 파일이 다시 들어오는 것을
막지 못한다. 반대로 단순 금지어 grep은 다음 정상 항목을 오탐한다.

- 방장 `AuthorityHost`의 Bind/Listen
- Steam P2P/SDR와 Steamworks SDK 선언
- Unity의 `dedicatedServerOptimizations` 기본 field
- 문서와 Evidence의 `Docker=0`, `Backend=0` 설명
- Input/Physics/AssetDatabase에서 쓰는 일반적인 backend 용어

그래서 의미가 강한 artifact와 API signature만 검사하고, 정상 Host P2P 경계는 positive fixture로 고정했다.

### 배제한 대안

- repository 전체에서 `server`, `backend`, `docker` 단어를 무조건 금지
- 문서만 확인하고 실행 가능 파일·Package는 검사하지 않음
- Unity generated `Library/PackageCache`까지 current repository source인 것처럼 검사
- 금지 infra가 발견돼도 경고만 출력하고 성공 종료

### 실제 근거

FDN-009 자체 완료 Evidence snapshot은 Git-visible inventory `259`, content file `83`, package manifest `2`였다.
이후 ART-001 산출물을 포함해 같은 검증기를 다시 실행한 snapshot은 inventory `266`, content `88`, manifest `2`였다.
두 시점 모두 다음 결과가 같았다.

- Backend/Database/Dedicated/Container/Audit violation 모두 `0`
- Policy self-test `14/14`, assertion `245`
- Rule ID `37/37` unique·exercised
- 독립 adversarial fixture `12`종 거부
- matched content·absolute path 출력 `0`

[FDN-009 Evidence](../evidence/G0/FDN-009/EV-FDN-009-20260827-r01.yaml) ·
[ART-001 시점 재검사 Evidence](../evidence/G0/ART-001/EV-ART-001-20260827-r01.yaml)

### 아직 증명하지 않은 것

Git history, Remote, submodule 내부, ignored Build/Library, 외부 실행 System, 난독화 code와 이름을 바꾼 binary
내부 SDK까지 증명하지 않는다. Release artifact는 `ALP-002`, `STM-013`에서 다시 검사한다.

---

## 4.12 `ART-001` — Asset보다 먼저 고정한 Style·Interop·Visual QA Profile

### 선택

세 개의 versioned Foundation Profile을 만들었다.

1. [LowPolyStyleProfile](../../config/art/LowPolyStyleProfile.yaml)
2. [ModelInteropProfile](../../config/art/ModelInteropProfile.yaml)
3. [AlphaVisualQAProfile](../../config/art/AlphaVisualQAProfile.yaml)

모든 Profile은 `START/Foundation` 상태다.

- 문서에서 이미 확정된 invariant는 `DECIDED`
- 첫 비교를 위한 가역적 기술값은 `START`
- 사용자 시각 Gate가 필요한 최종값은 owner와 Gate를 가진 `DEFERRED/null`
- `LOCKED` 값, 시각 승인 claim과 실제 Asset 생성은 `0`

### 이유

Character, Weapon과 Map을 먼저 개별 제작하면 Blender file마다 scale, axis, bevel, normal, material import와
QA light가 달라질 수 있다. 그 상태에서 Unity 결과를 수동 rotation/scale/normal 보정하면 source 문제를
숨기고 Prefab 간 품질이 흔들린다.

Profile을 먼저 만든 이유는 최종 미술 취향을 조기에 확정하기 위해서가 아니라, 비교 방법과 책임자를 먼저
고정하기 위해서다.

### 주요 기술 선택

#### LowPolyStyle

- Silhouette/Motion 우선, 하나의 World scale·material 언어
- Bevel class `B0 intentional none`, `B1 rigid readability`, `B2 soft hero`
- 실제 final Bevel 폭은 AssetBrief와 후속 Gate 전까지 null
- Normal class `Flat`, `Hard Edge`, `Authored Smooth`
- Material family `6종`
- Palette는 semantic role만 고정하고 최종 swatch 값은 null
- Photo scan, exact weapon replica, logo/serial/marking, gameplay bounds를 바꾸는 deformation 금지

#### ModelInterop

- Blender source: `+Z Up`, Character `-Y Forward`
- FBX export mapping: `-Z Forward`, `+Y Up`
- Unity: `+Y Up`, `+Z Forward`, `+X Right`
- `1 meter = 1 Unity unit`, root scale `(1,1,1)`, negative/manual post-import correction `0`
- FBX Model export option `20개`를 preset digest로 고정
- Leaf bone `0`, explicit L/R logical bone, Finger/Toe bone `0`
- Unity material auto-import `0`, 승인 Material family remap
- GenerationManifest가 Toolchain/Profile/Preset/source/FBX/Prefab identity를 추적

#### AlphaVisualQA

- URP Lit을 **제품 최종 Shader가 아닌 QA reference START**로 사용
- Unity Linear color space, tone mapping/auto exposure 없이 neutral comparison
- Front/Side/Back/ThreeQuarter orthographic view
- `2/3/4인 × 16:9/16:10/21:9 × Min/Max gameplay camera` capture matrix
- L/R Punch/Kick, Grab/Lift/Throw, Dropkick, Ragdoll/GetUp, 4 Weapon과 전체 lifecycle, Lobby/Disconnect/Menu,
  Cosmetic/Hazard/Patch12/worst-case scenario ID 고정
- pixel-perfect·임의 DeltaE 자동 승인 대신 의미 있는 hue/value/specular/silhouette drift를 사람 Gate에서 검토

### 배제한 대안

- Profile 없이 Character/Weapon/Map을 먼저 양산
- FBX별 수동 rotation/scale/normal 보정
- Rejected image의 색이나 임의 Hex를 최종 Palette로 복사
- QA light/camera를 Product Lighting/Gameplay Camera로 확정
- Blender export 또는 Unity import 성공을 시각 승인으로 간주
- C1b 전 Character 비율, Weapon exact silhouette, final LOD/GPU budget을 `LOCKED` 처리

### 실제 근거

- Profile `3`, unique ID `3`, ART-001 owner `3`
- `LOCKED` state `0`, visual approval claim `0`, generated Asset/Capture `0`
- Semantic mutation test `24/24`, assertion `206`
- ART-001 Git scope path `8`, 범위·asset output violation `0`
- Blender `5.2.0 LTS` FBX option `20`, missing `0`, invalid enum `0`
- Unity ModelImporter property `18`, missing `0`; SkinWeights enum `Standard/Custom`
- Unity 전체 EditMode `42/42`, PlayMode `4/4`

[ART-001 Evidence](../evidence/G0/ART-001/EV-ART-001-20260827-r01.yaml)

### 아직 증명하지 않은 것

- `.blend` export, 실제 FBX 내용, Unity Importer 실행과 Prefab
- Blender↔Unity side-by-side render parity
- 2·3·4인 실제 Camera capture
- 사용자 시각 승인
- 최종 Palette, Bevel 폭, Product Shader/Lighting, gameplay Camera와 성능 예산

---

## 4.13 `LIC-001` — Source Inventory와 Player Release Audit의 분리

### 선택

- 공식 Unity Package와 bundled dependency는 적용 조건과 NOTICE를 보존하는 조건으로 사용한다.
- `manifest.json` direct `46개`와 lock의 transitive `12개`, 합계 `58개`를 전수 inventory한다.
- Version, registry/builtin source, direct/transitive 관계, usage class, license family, NOTICE disposition과
  source evidence를 Package마다 기록한다.
- Unity built-in module `36개`는 존재하지 않는 per-package NOTICE를 꾸며내지 않고, Unity Editor Terms와
  aggregate legal notice가 현재 근거지만 module별 mapping은 없다는 한계를 남긴다.
- C2PA claim이 있는 review image `18개`는 경로·SHA-256별로 기록하고 모두 `shippingAllowed: false`로 둔다.
  사람이 사용자 승인 디자인을 보고 새 원본 Asset을 제작하는 것은 허용하지만, 원본·재인코딩본·기계적
  파생물을 Player asset이나 Steam media로 사용하지 않는다.
- 출처를 증명하지 못한 review 파일 `3개`와 제품에 필요 없는 Unity Tutorial/Readme 파일 `14개`는
  사용자 승인에 따라 제거했다.
- 새로 직접 제작하는 Asset은 외부 reference와 섞지 않고 `firstPartyProductionAssets`에 별도 등록한다.
  정확한 path·SHA-256·assetType·사용자 확인 `sourceOwner`·`PROJECT_AUTHORED`·intended use·`FIRST_PARTY`
  rights·NOTICE disposition과 source evidence가 모두 있어야 한다.
- canonical `.blend`는 `PRODUCTION_SOURCE`로 Unity `Assets` 밖에 두고 `shippingAllowed: false`로 유지한다.
  Unity가 실제로 소비할 derived/runtime file은 별도의 `PLAYER_CONTENT` record로만 shipping 후보가 될 수 있다.
- 현재 Package를 제거·추가·Upgrade하지 않았고 Player Build도 실행하지 않았다.
- 실제 Windows Player에 포함된 engine/package component와 최종 배포 NOTICE는 사용자가 Build한 뒤
  `BLD-001`/`ALP-001`에서 다시 대조한다.

[License Policy](../../config/licenses/LicensePolicy.yaml) ·
[전수 Inventory](../../config/licenses/ThirdPartyInventory.yaml) ·
[사람이 읽는 NOTICE index](../../THIRD_PARTY_NOTICES.md)

### 이유

Package가 Unity Editor에서 동작하는 것과 게임에 재배포할 권리·고지 의무가 정리됐다는 것은 다르다.
또한 `Library/PackageCache`는 Git에서 제외되므로, 한 개발 Machine의 절대 cache path나 hash만 기록하면 새
checkout에서 living inventory로 사용할 수 없다. 그래서 lock file과 repository-relative logical locator를
규범값으로 삼고, 현재 Machine의 PackageCache 검사는 Evidence observation으로 분리했다.

반대로 source package `58개`를 모두 최종 Player 포함물이라고 선언하는 것도 정확하지 않다. Unity stripping,
compiled shader와 platform module 구성은 실제 Build에 따라 달라진다. 사용자의 자동 Build 금지 결정을 지키면서
누락을 숨기지 않기 위해 source 분모를 먼저 완전하게 고정하고, 최종 배포 분모는 사용자 Build 뒤 좁히도록 했다.

### 배제한 대안

- 공식 Unity Package라는 이유만으로 license/NOTICE 검토를 생략
- PackageCache root LICENSE만 보고 nested/inline third-party notice를 없다고 판정
- C2PA provenance를 상업적 사용·재배포 license로 간주
- 출처 불명 파일을 “나중에 확인” 상태로 Unity `Assets`에 유지
- Editor/Test Package를 Build evidence 없이 최종 NOTICE에서 미리 제외
- LIC-001에서 Windows Player를 자동 Build하거나, Build 없이 final release NOTICE 완료를 주장

### 실제 근거

- Package `58/58`: direct `46`, transitive `12`, registry `14`, builtin `44`, builtin module `36`
- non-module resolved Package `22개`의 root license evidence와 root Third Party Notice `10개` 확인
- resolved-package file locator `40개`와 built-in module metadata logical locator `36개`,
  version/source/relationship 누락 `0`
- review-only image `18개`, Binary inventory 일치 `18/18`, `shippingAllowed: true` `0`
- first-party production asset `0개`, review path와 중복 `0`; 아직 만들지 않은 Asset의 권리 주장 `0`
- Font `0`, Audio `0`, 3D model `0`, project shader `0`, DLL/native plugin `0`
- 출처 불명 파일 `3개`와 Unity Tutorial/Readme 파일 `14개` 제거
- Package manifest/lock 변경 `0`, Player Build `0`, Docker/Deploy `0`
- living license guard mutation `22 runs / 176 assertions`, 실패·오류·skip `0`

[LIC-001 초기 Evidence](../evidence/G0/LIC-001/EV-LIC-001-20260828-r01.yaml) ·
[First-party seam 보충 Evidence](../evidence/G0/LIC-001/EV-LIC-001-20260829-r02.yaml)

### 아직 증명하지 않은 것

- 실제 Windows x64 Player에 포함된 managed/native assembly, engine module과 compiled shader 집합
- 그 실제 배포물에 필요한 최종 Unity engine/package NOTICE bundle
- 새로 제작할 Character·Weapon·Map·Audio Asset의 미래 license 상태
- 법률 의견 또는 출시 국가별 법적 적합성

---

## 4.14 `BLD-001` — 자동 Build 없이 분리한 Windows Development·Steam Reserved Profile

### 선택

- 사용자가 설치한 Unity `6000.3.9f1`의 `Windows Build Support (Mono)`로 Windows x64 **Client Player**
  Profile만 준비했다. Windows Server/Dedicated module과 Server subtarget은 사용하지 않는다.
- Unity 공식 Build Profiles UI가 두 Asset을 직접 생성했다. 내부 target/subtarget 숫자를 손으로 작성해
  Profile인 것처럼 꾸미지 않았다.
- `Windows x64 Development`는 Development Build를 켜고
  `PROJECTHOTFIX_BUILD_DEVELOPMENT` 하나만 가진다.
- `Windows x64 Steam Reserved`는 Development Build를 끄고
  `PROJECTHOTFIX_BUILD_STEAM_RESERVED` 하나만 가진다. Steam SDK, App ID와 기능은 `0`이며 `STM-001` 전에는
  Build하지 않는 예약 슬롯이다.
- 두 Profile은 임시 사용자 승인값 `KJH4845 / Project Hotfix / com.kjh4845.projecthotfix / 0.1.0`과
  Standalone Mono를 공유한다. Profile별 PlayerSettings, Quality와 Graphics override는 `0`이다.
- Scene은 두 Profile에 중복 복사하지 않고 global EditorBuildSettings의 `SampleScene` 한 개를 사용한다.
  이 목록은 `START_PLACEHOLDER`, release-ready `false`다.
- 자동 Player Build, Build And Run, Steam 배포와 Build 산출물 commit은 금지하고 사용자가 수행할 수동 절차만
  기록했다.

[Windows Build Profile Policy](../../config/build_profiles/WindowsBuildProfilePolicy.yaml) ·
[수동 Build 안내](../../config/build_profiles/WINDOWS_BUILD_MANUAL.md)

### 이유

Alpha direct 테스트와 후속 Steam 제품 경로는 debug flag와 compile define이 다르다. Profile을 하나만 두고
매번 checkbox와 define을 손으로 바꾸면 어떤 조합으로 만든 Player인지 재현하기 어렵다. 반대로 아직 Steam SDK도
없는 Profile을 `Steam` 완성본으로 부르면 구현 상태를 과장한다. 그래서 Development와 **Steam Reserved**를
분리하고, 후자는 `STM-001`이 실제 App ID와 wrapper를 소유할 때까지 잠갔다.

현재 `SampleScene`은 제품 Main/Lobby/Match가 아니다. 두 Profile에 같은 임시 목록을 복제하면 두 사본의 drift만
늘어나므로 global list 한 곳을 START source로 사용한다. 실제 Scene이 생길 때 목록과 상태를 함께 revision한다.

Company/Product 값은 Windows 실행 파일 이름뿐 아니라 향후 Unity local persistent data path에도 영향을 줄 수 있다.
그래서 Git 사용자 정보를 임의 제품명으로 확정하지 않고 사용자에게 임시값을 승인받았으며, 변경 시 local-save
migration 결정을 요구한다.

### 배제한 대안

- Windows module 없이 Build Profile YAML target/subtarget 숫자를 손으로 작성
- Development/Steam을 하나의 Profile checkbox 수동 전환으로 운영
- Steam SDK·App ID 없이 실제 Steam 기능이 있는 것처럼 define/Profile 이름 사용
- SampleScene을 release-ready Scene list로 선언하거나 같은 임시 목록을 두 Profile에 중복 저장
- Server, Dedicated, Headless, Cloud Build, Docker/Container build variant 추가
- 검증 과정에서 Player Build 또는 Build And Run 실행

### 실제 근거

- Unity-generated Build Profile `2`, unique Asset GUID `2`
- Unity API 기준 target `StandaloneWindows64`, subtarget `Player`, architecture `x64`, Standalone backend `Mono2x`
- Development flag `true/false`, custom define 상호배타 `2`, global define 누출 `0`
- global enabled Scene `1`, `SampleScene`, Profile scene override `0`, release-ready claim `0`
- Profile별 Quality/Graphics/PlayerSettings override `0`
- Windows Mono x64 development/nondevelopment variation `2`, Server variation `0`
- Steam SDK, App ID file, Auth/Lobby/Invite/P2P 기능 `0`
- Build Profile static mutation `17/17`, assertion `100`; Unity EditMode `52/52`, PlayMode `4/4`
- Package manifest/lock 변경 `0`, Player Build/Build And Run/Deploy/Docker 실행 `0`

### 아직 증명하지 않은 것

- Windows `.exe`, `UnityPlayer.dll`, Data folder 생성과 실제 Windows 실행
- 2·3·4인 direct P2P Match와 무기 전투
- 실제 Main/Lobby/Match Scene list와 최종 Company/Product identity
- Steam App ID, SDK, Auth, Friends Lobby, Invite, Code와 P2P/SDR
- 실제 Player 포함 component와 최종 NOTICE bundle

---

## 5. 왜 이 선택들이 함께 있어야 하는가

### 5.1 UTP 선택만으로 P2P 구조가 안전해지지는 않는다

Transport를 UTP로 골라도 Presentation이 Simulation을 바꾸거나 Input이 직접 Rigidbody를 움직이면 Host Authority가
흐려진다. 그래서 Transport Adapter, ModuleGraph, renderless kernel과 Physics cadence를 함께 고정했다.

### 5.2 Local 저장만으로 Backend 0이 보장되지는 않는다

Preset을 local file로 저장해도 나중에 Firebase SDK, Docker Compose나 Dedicated BuildProfile이 들어오면 제품 구조가
바뀐다. 그래서 AtomicLocalStorage와 ForbiddenInfrastructure Guard가 한 쌍이다.

### 5.3 Exact Toolchain만으로 Art 품질이 보장되지는 않는다

Unity와 Blender version이 같아도 exporter/importer option, material remap, camera framing이 다르면 결과가 달라진다.
그래서 ToolchainProfile 위에 LowPolyStyle, ModelInterop과 AlphaVisualQA Profile을 추가했다.

### 5.4 Test 개수만으로 완료를 증명할 수는 없다

`Passed` XML에 skip/not-run이 섞이거나, mock kernel이 제품 kernel과 다르면 숫자는 의미가 없다.
그래서 같은 Runtime code 실행, strict NUnit parser, task-specific Evidence와 독립 adversarial review를 함께 사용했다.

### 5.5 Art Profile만으로 Shipping Asset의 권리가 보장되지는 않는다

승인된 silhouette, palette role과 Blender→Unity preset을 그대로 따라도 사용한 source image, font, audio 또는
model의 권리가 불명확하면 배포할 수 없다. 반대로 review image를 Player에서 제외한다고 그 디자인 방향까지
버릴 필요는 없다. 그래서 Art Profile은 “무엇을 어떻게 재현할지”를, License Policy는 “어떤 source file을
어디까지 사용할 수 있는지”를 각각 소유하고 SHA inventory로 연결했다.

### 5.6 Build Profile이 있어도 Player가 검증된 것은 아니다

Profile은 target, define, Scene source와 identity를 재현하지만 compile 성공, Windows 실행, P2P 연결 또는
NOTICE 완전성을 대신하지 않는다. 그래서 BLD-001은 Build 실행 `0`을 유지하고, 사용자 Build 뒤의 artifact
identity·실행 결과·NOTICE 감사는 별도 Evidence로 남긴다.

---

## 6. 검증 결과를 해석하는 방법

마지막 기능성 Foundation Task인 BLD-001과 이후 저장소/LFS·first-party inventory 보충 Evidence의 핵심 수치는
다음과 같다. 각 Task의 역사 수치는 덮어쓰지 않는다. BLD-001은 실제 Profile 의미를 포함한 전체 EditMode와 기존
PlayMode를 임시 Project 복사본에서 재실행했으며, 원본 Editor의 미저장 Scene은 건드리지 않았다.

| 항목 | 결과 | 이 결과가 증명하지 않는 것 |
|---|---:|---|
| Windows Client Profile | `2/2`, unique GUID `2` | Windows Player가 compile·실행됨 |
| Profile static mutation | `17 runs`, `100 assertions`, 실패 `0` | Unity enum 의미 또는 Windows runtime 동작 |
| Unity regression | EditMode `52/52`, PlayMode `4/4` | 실제 Windows exe·P2P Match·무기 전투 성공 |
| Scene source | global `SampleScene` `1`, START | 최종 Main/Lobby/Match Scene 목록 |
| Steam Reserved | SDK/App ID/기능 `0` | Steam 통합 또는 배포 가능 |
| License source inventory | package `58/58`, review image `18/18` | 최종 Windows Player NOTICE가 완성됨 |
| First-party production seam | current asset `0`, mutation `22/176` | 아직 존재하지 않는 Asset의 저작자·권리·Player 포함 |
| Git LFS / Remote | local LFS `3.8.0`, pattern `10`, private `origin/main`, file/candidate `0/0` | 실제 production LFS object upload·fresh fetch |
| Forbidden infra | inventory `276`, content `97`, manifest `2`, violation `0` | Git history·ignored Build·외부 서비스 전체 부재 |
| Evidence manifest | `43`개 구조 검증 | 각 미래 Feature의 수동 체감 승인 |
| Player Build | `0` | Build 불가능을 뜻하지 않음. 사용자 요청에 따라 실행하지 않았음 |
| Blender export / Unity art import | `0 / 0` | Profile 정의 실패를 뜻하지 않음. 실제 source/import parity 검증이 후속임 |
| Docker / Deploy | `0 / 0` | 실제 명령은 실행하지 않았지만 ignored 영역·외부 System 전체 부재까지 증명하지는 않음 |

수치는 해당 Task가 주장한 범위 안에서만 읽어야 한다. 예를 들어 Physics PlayMode `2/2`는 Contact와 Joint
smoke를 뜻하지 Character combat feel 승인을 뜻하지 않는다. UTP package test `4/4`도 실제 LAN 연결 성공이 아니다.

---

## 7. 아직 남아 있는 Foundation·G0 작업

### 7.1 사용자 수동 Windows Player·NOTICE Evidence

`BLD-001`의 Profile, 임시 identity와 수동 절차는 완료됐지만 Player는 만들지 않았다. 실제 Main/Lobby/Match
Scene이 준비된 뒤 사용자가 `Windows x64 Development`로 Build하고, Git revision·Profile hash·exe hash·warning/
error와 Windows 실행 결과를 기록해야 한다. 그 산출물의 managed/native assembly, engine module과 compiled
shader를 `LIC-001` source inventory에 대조해야 release NOTICE를 확정할 수 있다.

`Windows x64 Steam Reserved`는 `STM-001` 전 Build 금지다. Steam App ID와 wrapper가 정해지기 전에는
Steam 실행 Evidence나 배포 가능을 주장하지 않는다.

### 7.2 C1b

`Hybrid Core v0.13`은 Character 방향 승인이지 exact 비율·Collider·Reach·Mesh 승인이 아니다.
다음 실제 제작 단계는 `C1B-002..006`의 orthographic measurement와 사용자 `UG-C1B`다.

`C1B-002`의 수치 후보 문서화는 현재 Profile 안에서 진행할 수 있다. Git LFS와 private `origin`도 준비돼
`C1B-003`의 `.blend/.fbx` 제작 전제는 해제됐다. 다만 첫 production binary는 source/license/hash inventory,
LFS index pointer, object upload와 fresh fetch를 확인한 뒤에만 commit 완료로 처리한다. 첫 record의
`sourceOwner` 표기는 GitHub handle이나 임시 Company 값으로 추론하지 않고 C1B-003 착수 전에 사용자에게 확인한다.

### 7.3 구현되지 않은 핵심 Runtime

- 제품 Action Map과 tap/hold/chord resolver
- Character Rigidbody controller, Grab, Ragdoll, Down 누적
- Shared Camera
- Round, Score, Patch12, Weapon combat와 Supply
- 실제 UTP Adapter, Protocol, Lobby와 reconnect
- Steam Auth/Friends Lobby/Invite/Code/P2P/SDR
- Preset schema·migration·UI와 persistentDataPath composition

---

## 8. 결정 변경이 필요한 경우

Foundation 선택은 영원히 수정 불가한 코드가 아니라, 변경 비용과 검증 책임을 명확히 만든 기준선이다.

| 변경 | 필요한 조치 |
|---|---|
| Unity/Blender patch 변경 | ToolchainProfile revision, Package/Project hash 재기록, compile·Physics·Interop 회귀 |
| Package 추가·교체 | 소유 module 명시, manifest/lock 동시 변경, License inventory revision, Architecture·license test |
| Company/Product/Application ID 변경 | local persistent-data path 영향 검토, Preset migration 결정, Build policy revision |
| Build Profile target/backend/define 변경 | Windows client·Server 금지·Profile 상호배타·전체 Unity 회귀 재검증 |
| 실제 Scene list 전환 | SampleScene START 제거, Main/Lobby/Match 순서와 모든 Profile 유효 Scene 재검증 |
| Physics cadence 변경 | PhysicsProfile revision, Contact/Joint와 Gameplay·Network cadence 전체 재검증 |
| UTP 이외 Alpha transport | Transport Adapter 계약과 trusted-direct 범위 재검토 |
| Steam transport 도입 | Alpha UTP fallback과 분리, 실제 Steam 계정 2·3·4인 수동 검증 |
| Backend·DB·Docker·Dedicated 도입 | 단순 구현 변경이 아니라 제품 범위 변경. 사용자 승인과 PRD/SRS 선행 수정 필요 |
| 최종 Palette/Bevel/Shader/Camera Lock | 해당 downstream 사용자 Gate와 실제 Unity capture 필요 |
| 새 외부 Asset 추가 | source·version/hash·상업사용·재배포·NOTICE 증명, 미충족 시 shipping 차단 |
| 큰 Binary 추가 | Binary/license inventory 갱신, LFS attr·index pointer·object upload/fresh fetch 확인; 임의 history rewrite·force-push 금지 |

특히 금지 인프라를 도입하는 변경은 “개발 편의를 위한 내부 구현”으로 조용히 처리할 수 없다.
현재 제품의 운영비·접속 구조·보안 경계를 바꾸므로 상위 계약을 먼저 변경해야 한다.

---

## 9. Commit·Evidence 빠른 색인

아래 Commit은 Task 실행 중 Evidence가 가리킨 임시 revision이 아니라, 해당 Task 산출물이 현재 Git history에
최초 포함된 Commit의 축약 hash다. `FDN-001` Evidence 작성 시점의 Git 상태는 아직 `UNBORN`이었고,
그 산출물은 뒤이은 `b36bceb` 초기 Commit에 함께 포함됐다.

| 범위 | Commit | 주요 근거 |
|---|---|---|
| `FDN-001`, `010`, `011`, `002` | `b36bceb` | [FDN-001](../evidence/G0/FDN-001/EV-FDN-001-20260826-r01.yaml), [FDN-010](../evidence/G0/FDN-010/EV-FDN-010-20260826-r01.yaml), [FDN-011](../evidence/G0/FDN-011/EV-FDN-011-20260826-r01.yaml), [FDN-002](../evidence/G0/FDN-002/EV-FDN-002-20260826-r01.yaml) |
| `FDN-003` | `6ce290f` | [Module Evidence](../evidence/G0/FDN-003/EV-FDN-003-20260826-r01.yaml) |
| `FDN-004` | `093022d` | [Input Evidence](../evidence/G0/FDN-004/EV-FDN-004-20260826-r01.yaml) |
| `FDN-005` | `38f583b` | [Physics Evidence](../evidence/G0/FDN-005/EV-FDN-005-20260827-r01.yaml) |
| `FDN-006` | `6256a40` | [Transport Evidence](../evidence/G0/FDN-006/EV-FDN-006-20260827-r01.yaml) |
| `FDN-007` | `e46f220` | [Test Foundation Evidence](../evidence/G0/FDN-007/EV-FDN-007-20260827-r01.yaml) |
| `FDN-008` | `9e3ce6c` | [Local Storage Evidence](../evidence/G0/FDN-008/EV-FDN-008-20260827-r01.yaml) |
| `FDN-009` | `9a7018d` | [Infrastructure Guard Evidence](../evidence/G0/FDN-009/EV-FDN-009-20260827-r01.yaml) |
| `ART-001` | `2faee33` | [Art Profile Evidence](../evidence/G0/ART-001/EV-ART-001-20260827-r01.yaml) |
| `LIC-001` | `d5f19a9` | [License Inventory Evidence](../evidence/G0/LIC-001/EV-LIC-001-20260828-r01.yaml) |
| `BLD-001` | `4b47284` | [Windows Build Profile Evidence](../evidence/G0/BLD-001/EV-BLD-001-20260829-r01.yaml) |
| `FDN-011` LFS·Remote 보충 | `0eeceee` | [Repository LFS Evidence](../evidence/G0/FDN-011/EV-FDN-011-20260829-r02.yaml) |
| `LIC-001` first-party 보충 | `0eeceee` | [First-party Inventory Evidence](../evidence/G0/LIC-001/EV-LIC-001-20260829-r02.yaml) |

---

## 10. 최종 판단

현재까지의 Foundation은 기능을 많이 만든 단계가 아니라 **앞으로 기능을 잘못된 방향으로 만들지 않게 한 단계**다.

- Server 중심 구조로 미끄러지지 않게 했다.
- Authority, Input, Physics, Transport와 Presentation 책임을 분리했다.
- 손상 가능한 로컬 저장에 복구 경계를 만들었다.
- Test가 제품 코드와 분리된 모형이 되지 않게 했다.
- Blender와 Unity 에셋 품질을 수동 보정으로 숨기지 않게 했다.
- 아직 하지 않은 Build, Steam, Gameplay와 시각 승인을 완료했다고 과장하지 않게 했다.

Foundation 표의 기술·Art·license·Build Profile과 production binary 저장 전제까지 닫혔다. 다음 구현 순서는
`C1B-002`의 exact 비율 후보 문서화다. `C1B-003`의 첫 `.blend/.fbx`부터는 활성 LFS 정책과 source/license
inventory를 실제로 적용하고, 그 직전에 first-party `sourceOwner` 표기를 사용자에게 확인한다. Windows Player
수동 Build는 실제 Scene과 Alpha 기능이 준비된 뒤 별도 Evidence로 수행한다.
