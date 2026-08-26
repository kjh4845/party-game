# EV-DOC-002 — 2026-08-26 결정 정합성 표

현행 12개 문서를 사용자 결정과 대조한 결과다. 삭제한 초기 아이디어 문서와 이전 Evidence revision은
제품 구현 근거로 사용하지 않는다.

| 영역 | 확정 결정 | 현행 소유 문서 | 상태 |
|---|---|---|---|
| Host 시작 | Host는 Ready가 없고 처음부터 같은 slot에 Start를 본다. 총 2~4명·모든 Guest 연결/Ready·전원 외형 확정일 때만 활성화 | PRD §7.2, SRS §4, UI §5.3~5.5, Plan LBY-004 | RESOLVED |
| Guest Ready 표시 | NotReady는 중립색, Ready는 ReadyTeal Button이며 check icon과 준비 완료/준비 취소 label을 함께 사용 | PRD §7.2, SRS LOBBY-002, UI §5.3 | RESOLVED |
| Lobby 시작 장치 | 물리 StartLever 제거. P00 Crane lever는 Match Hazard 장치로 유지 | PRD §7.2, UI §5.5, P00 §5.2 | RESOLVED |
| Sprint | Left Shift hold, Lobby·Match 공통, stamina 없음, multiplier는 Alpha tuning | PRD §4.2, SRS §6, Character §5.1 | RESOLVED |
| 반복 Down | Match 첫 Down=Base, 두 번째부터 Increment, Max cap, episode당 count 1회 | PRD §4.5, SRS §6, Character §5.3 | RESOLVED |
| Down reset | Match Round 시작에 DownCount=0. Lobby는 BaseDuration만 사용하고 Match count를 만들지 않음 | SRS §6·AT-019, Character §5.2, Plan CHR-008/010 | RESOLVED |
| Ground/Air action | Ground quick tap=L/R Punch, hold=Hand Grab. Air single quick release=chord close 뒤 L/R Kick, valid second down-edge chord=즉시 Dropkick, episode token 1 | PRD §4.4, SRS INPUT-008..009/PHYS-012..014, Character §5.2, Plan AIR-001..002 | RESOLVED |
| Dropkick recovery | bounded forward impulse·reduced steering·stronger knockback 뒤 non-Down DropkickRecovery. DownCount·TRG-DOWN 0, Recovery 종료 전 새 Attack 0 | PRD §4.4, SRS PHYS-013, Character §5.2, Plan AIR-002 | RESOLVED |
| Hybrid animation | Unity Rigidbody·Hit는 Authority, Animator/procedural pose는 read-only. Gameplay Root Motion·Animation Event authority 0, Alpha ANP와 후속 ANM 분리 | PRD §4.7, SRS APPEAR-022, Character §4·6, Plan ANP-001..003 | RESOLVED |
| Lobby Cursor | Esc로 열고 다시 Esc로 닫는다. held Mouse all-up 뒤 새 down부터 손 입력 재개 | PRD §7.3, SRS §6, UI §3.3 | RESOLVED |
| 미사용 자료 | 초기 아이디어 Markdown 6개와 폐기 Lobby v1 이미지를 workspace에서 제거 | Index, validator | RESOLVED |
| Alpha 연결 | Steam 전에는 신뢰된 LAN/direct endpoint에서 2·3·4인을 검증하고 Internet/NAT/제품 보안을 주장하지 않음 | PRD §10.2, SRS §3, Plan G2 | RESOLVED |
| Steam 순서 | Alpha 승인 뒤 Steam auth·비공개 친구 Lobby·invite·code·P2P/SDR 통합 | PRD §10.3, SRS §10, Plan G4 | RESOLVED |
| Steam code | SteamLobbyId의 checksum 포함 가역 표현, application server lookup 0 | PRD §10.3, SRS STEAM-003/004, Plan STM-004 | RESOLVED |
| 별도 서버 | Backend·Coordinator·DB·Blob·Bake Worker·Dedicated Server·자체 relay 0 | PRD §2.3, SRS SYS-006, Plan §1.1 | RESOLVED |
| Container | Docker·OCI·Compose·container image·배포 step 0 | SRS SYS-007, Plan FDN-009/ALP-002/STM-013 | RESOLVED |
| 외형 저장 | Preset local atomic save, Host가 bounded source 검증 후 P2P relay, 실패 player만 default | PRD §8.3, SRS §11, Plan APT-001..006 | RESOLVED |
| 실제 무기 | W1 입력 승인과 Pistol·LongGun·Bat·Hammer 실제 전투를 Alpha에 포함. W1은 Air Kick/Dropkick mapping을 덮어쓰지 않음 | PRD §5.3, SRS §7, Weapon §4~11, Plan WPN-001..008 | RESOLVED |
| 무기 시각 방향 | M1911-inspired Pistol, AK-47-inspired LongGun, baseball Bat, sledgehammer. 일반 UI 이름·logo·marking·serial·exact replica 0 | PRD §5.3, SRS APPEAR-021, Art §5, Weapon §1, Plan WPA-001..003 | RESOLVED · 방향 승인 2026-08-25 |
| 총기 Fire·Ammo | Pistol press당 semi-auto 1발·total7, LongGun hold full-auto·total30. Reserve·reload 0 | PRD §5.3, SRS WEAPON-014, Weapon §5.1, Plan FIR-001/WPN-005 | RESOLVED · 2026-08-26 |
| Ammo 소진 | 마지막 Shot 뒤 Host forced release→SpentPendingCleanup 2~4초 deadline remove. cap에는 포함하고 replacement는 다음 정규 pulse | PRD §5.3, SRS WEAPON-015, Weapon §5.1, Plan FIR-001/WPN-007 | RESOLVED |
| Projectile | Host visible projectile, fixed-step swept SphereCast first hit, no pierce·ricochet, gravity0 START, TTL/OOB/Result/reset 제거. Playing+SuddenDeath Fire 유지 | PRD §5.3, SRS WEAPON-016, Weapon §5.2, Plan FIR-002 | RESOLVED |
| Recoil·명중률 | Host bounded Unity impulse/torque+read-only recoil pose. Pistol narrow spread·strong shot recoil, LongGun deterministic cumulative RecoilAccumulator/SpreadBloom | PRD §5.3, SRS WEAPON-017, Weapon §5.3, Plan FIR-003 | RESOLVED |
| 반복 무기 투하 | 라운드당 1개 제한을 폐기. Playing 기준 2인 10/22초·cap2, 3인 8/16초·cap2, 4인 6/12초·cap3, Round-frozen profile, Host 결정적 bag·동적 safe DropZone, 착지 전 무해·cap full skip·Weapon cleanup | PRD §5.4, SRS WEAPON-009..012, Patch §1.3, Weapon §2.1, Plan WPN-007 | RESOLVED |
| Tab | Settings의 Hold/Toggle, Match score와 active Patch만 표시 | PRD §11.1, SRS INPUT-007/UI-004, UI §3.4 | RESOLVED |
| Match 상시 UI | Persistent HUD 0. 상시 timer·alive·ammo·killfeed·result panel을 두지 않고, transient countdown/Patch/status와 on-demand Tab만 허용. Ammo는 developer debug 전용 | PRD §11, SRS UI-005, UI §7, Plan UI-007 | RESOLVED · 2026-08-26 |
| Match Esc | Host Simulation을 멈추지 않는 local-only menu. Local gameplay input만 neutralize하고 닫은 뒤 Mouse all-up부터 재무장 | PRD §11, SRS UI-006, UI §3, Plan INP-006/UI-007 | RESOLVED · 2026-08-26 |
| Guest 명시 이탈 | 30초 grace 없이 즉시 Forfeit. Forfeit 참가자는 PatchAuthor가 아님 | PRD §10, SRS NET-013..015, Plan NET-015 | RESOLVED · 2026-08-26 |
| Guest 비정상 단절 | 30초 동안 neutral input의 physical·vulnerable Character와 slot/camera 유지, 재접속 시 현재 alive/spectator로 복귀, timeout 뒤 Forfeit | PRD §10, SRS NET-013..015, Plan NET-008/015 | RESOLVED · 2026-08-26 |
| 이탈 뒤 경기 결과 | 영구 참가자 2명 이상은 계속. 1명만 남으면 자동 승리·score·Patch 없이 `상대가 나갔습니다` 뒤 Lobby. Host Leave/Loss는 Session 종료 | PRD §10, SRS NET-015, UI-005..006, Plan NET-009/015/UI-007 | RESOLVED · 2026-08-26 |
| Alpha 표현 품질 | InteractiveLobby Greybox, 대표 EyeSet/Mustache/Headwear placeholder, Korean-only, BGM 0, 고정 개발 mix의 basic combat·weapon·environment SFX만 구현 | PRD §12, SRS APPEAR-023/NFR-011/SYS-027, Art·UI, Plan LBY-001/APT-007/AV-001..003 | RESOLVED · 2026-08-26 |
| Post-Alpha 표현·설정 | Production Lobby, full Cosmetic, English/StringTable/font fallback, music, Main key-help, key rebind/cursor/UI scale/shake/effect/subtitle/audio volume 등 확장 설정은 Alpha 뒤 별도 재분할 | PRD §12, SRS NFR-007/SYS-027, Plan DEF-001 | RESOLVED · DEFERRED |
| 도구 기준 | Unity 6.3 LTS와 Blender 5.2 LTS를 토대로 사용하고 exact installed patch/package는 Foundation에서 lock | Index, Art, Plan FDN-010 | RESOLVED · 2026-08-26 |
| Patch 명칭·Alpha UI | 사용자 노출명은 패치. Alpha는 평문 2×2·timer·결과·active 목록만 만들고 icon·Animation·VFX·SFX·최종 Layout은 후속 | PRD §6·11, SRS §8, UI §7.3, Patch §10 | RESOLVED |
| Patch12 승인 기준선 | PATCH-PROT-001..012. Trigger당 Active 1개, active3 outgoing oldest projected set, supply double/second wave와 victim/attacker Weapon forced drop 포함 | PATCH_DESIGN 0.5.0 | RESOLVED · UG-PATCH12-DESIGN 2026-08-25 |
| 인원 검증 | Gameplay·map·camera·appearance·weapon·direct·Steam을 2인·3인·4인 각각 검증 | SRS SYS-022, Trace AT-001..003 | RESOLVED |
| 과명세 | Byte layout·hash 수식·float 순서·고정 fixture 반복은 제품 문서에서 제거하고 구현/Test spec에 한정 | SRS §0, Plan §0·3 | RESOLVED |
| 영구 비범위 | 공개 matchmaking·server browser·quick match·Rank·MMR은 현재·G5 후보 모두에서 제외 | PRD §10.5, SRS SYS-005, Plan §10 | RESOLVED |

## 판정

- 미해결 P0/P1 문서 충돌: 0
- 구조 검증 목표: 현행 문서 12, PRD 31, SRS 186, AT 44, Patch 12, Plan 173 Task,
  dependency missing 0, cycle 0
- 사용자 승인: 문서 실행 기준선 `UG-DOC` PASSED(2026-08-26). 구현 뒤 기능 결과 `UG-PATCH12`는 별도 Gate
- 남은 제품 결정 질문: 0. 남은 조정 항목은 Sprint multiplier, Down Base/Increment/Max, C1b,
  Hand/Chord threshold, W1 입력·Airborne WeaponUse,
  Fire cadence·Projectile speed/radius/TTL·recoil/bloom·Spent delay·Weapon balance와 exact visual Lock,
  supply START profile, Camera·P00 tuning처럼 구현 결과 뒤 조정·승인할 값이다.
