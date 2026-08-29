# Windows x64 수동 Build 안내

## 현재 상태

- 소유 Task: `BLD-001`
- Unity: `6000.3.9f1 (7a9955a4f2fa)`
- Target: Windows x64 Client, Mono
- Player Build 실행 주체: 사용자만
- BLD-001 자동 Build/Build And Run/배포 실행: `0`
- 현재 Scene: `Assets/Scenes/SampleScene.unity` 한 개
- Scene 상태: `START_PLACEHOLDER`, release-ready 아님

이 문서는 Build 버튼을 자동으로 실행하는 Script가 아니다. Build Profile과 PlayerSettings를 확인하고 사용자가
Unity Editor에서 직접 Build하기 위한 절차만 제공한다.

## Profile 구분

### Windows x64 Development

- 용도: Alpha의 로컬/직접 연결 기능 검증
- Development Build: 켬
- Profile define: `PROJECTHOTFIX_BUILD_DEVELOPMENT`
- Unity 파생 define: `DEVELOPMENT_BUILD`
- Profiler 자동 연결, Deep Profiling, Script Debugging, Wait For Debugger: 모두 끔
- 수동 출력 경로: `Builds/Windows/Development/Project Hotfix.exe`

### Windows x64 Steam Reserved

- 현재 용도: `STM-001` 이후 Steam 통합을 받을 예약 Profile
- Development Build: 끔
- Profile define: `PROJECTHOTFIX_BUILD_STEAM_RESERVED`
- Steam SDK, App ID, Auth, Friends Lobby, Invite, Code, P2P/SDR: 아직 없음
- 현재 Build 금지: `STM-001`이 wrapper와 App ID를 확정하기 전에는 이 Profile로 Build하지 않는다.
- 예약 출력 경로: `Builds/Windows/Steam/Project Hotfix.exe`

`Steam Reserved`가 존재한다는 사실은 Steam 기능 구현이나 Steam 배포 가능을 뜻하지 않는다.

## Build 전 확인

1. Git working tree에 의도하지 않은 변경이 없는지 확인한다.
2. Unity Hub에서 Editor `6000.3.9f1`과 `Windows Build Support (Mono)`가 설치됐는지 확인한다.
3. Unity Console의 compile error가 `0`인지 확인한다.
4. `ruby tools/verify_build_profiles.rb --verify-local-windows-module`을 실행해 Profile/PlayerSettings/module 검증을 통과한다.
5. File > Build Profiles에서 `Windows x64 Development`를 선택한다.
6. Architecture가 `Intel 64-bit`, Development Build가 켜져 있는지 확인한다.
7. Scene은 공용 Scene List의 `Scenes/SampleScene` 한 개임을 확인한다.
8. 실제 Main/Lobby/Match Scene이 구현되기 전 SampleScene Build는 기술 smoke일 뿐 Alpha 완료 Evidence가 아님을 기록한다.

## 사용자가 직접 Build하는 절차

1. File > Build Profiles에서 `Windows x64 Development`를 선택한다.
2. 필요하면 `Switch Profile`을 눌러 Windows Profile을 활성화한다.
3. `Build And Run`이 아니라 `Build`를 누른다.
4. 저장 위치를 `Project hotfix/Builds/Windows/Development/Project Hotfix.exe`로 지정한다.
5. Build가 끝나면 Console warning/error, Unity version, Git revision과 Profile asset hash를 기록한다.
6. 생성된 exe, `UnityPlayer.dll`, managed/native assembly와 Data folder를 `LIC-001` source inventory에 대조한다.
7. 실제 포함 component에 필요한 Unity/package NOTICE를 확정하고 `ALP-001` Evidence에 연결한다.

`Builds/`는 Git에서 제외된다. Player 산출물, `steam_appid.txt`, 인증 Ticket, 개인 경로 또는 Steam credential을
repository에 추가하지 않는다.

## 결과 기록 최소 항목

```text
Git revision:
Unity version:
Build Profile asset path:
Build Profile SHA-256:
Output path:
Executable SHA-256:
Build started/finished time:
Warnings:
Errors:
2/3/4-player test scope:
NOTICE audit result:
```

## 금지 사항

- `Windows Server`, Dedicated Server 또는 Headless Profile을 선택하지 않는다.
- Docker, Container, Cloud Build, CI 자동 Build를 추가하지 않는다.
- Steam Reserved Profile을 Steam 기능이 구현된 것처럼 사용하지 않는다.
- Build 산출물을 Git에 강제 추가하지 않는다.
- Build 실패를 해결하기 위해 Unity/package version을 임의로 Upgrade하지 않는다.

제품명, Company Name 또는 Application Identifier를 임시값에서 바꿀 때는 로컬 저장 경로 변경과 Preset migration을
먼저 결정해야 한다.
