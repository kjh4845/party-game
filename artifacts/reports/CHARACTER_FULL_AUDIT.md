# Project Hotfix 캐릭터 전체 감사

## 0. 문서 정보

| 항목 | 내용 |
|---|---|
| 감사 범위 | `C1B-001` 방향 source부터 `C1B-005` Blender→FBX→Unity static Blockout parity까지 |
| 기준일 | 2026-08-30 KST |
| 감사 성격 | 현재 산출물의 독립 read-only 구조·수치·시각·권리 감사 |
| 결론 | `C1B-001..005`의 각 Task 범위는 통과. 전체 캐릭터는 여전히 `START/CANDIDATE`이며 `UG-C1B`, Animation, C2/C4는 미완료 |
| 자동 실행 제외 | Unity Player Build, PlayMode gameplay, Steam, Docker, Deploy |

이 문서는 “현재 만든 것이 무엇을 증명하는가”와 “아직 무엇을 증명하지 않았는가”를 한곳에서 확인하기 위한
재사용 감사 기록이다. `PASS`는 해당 행의 제한된 범위만 통과했다는 뜻이며 사용자 승인, production lock 또는
게임플레이 완성을 뜻하지 않는다.

판정 용어는 다음과 같다.

- `PASS`: 기록된 Evidence와 독립 검사가 해당 범위의 완료조건을 충족한다.
- `주의`: START blockout에서는 허용되지만 다음 단계에서 그대로 production 품질로 승인하면 안 된다.
- `후속`: 현재 Task가 의도적으로 소유하지 않으며 지정된 Task/Gate에서 검증해야 한다.

## 1. 결과 요약

| 감사 항목 | 판정 | 현재 증거 | 이 판정이 증명하지 않는 것 |
|---|---|---|---|
| C1a v0.13 방향 source | PASS | 승인 PNG SHA 일치, `DIRECTION_ONLY`, pixel 역산·복제 0 | C1b exact 수치, Mesh, Physics, production 권리 |
| C1B-002 normalized 비율 | PASS / START | H=1, bounds `1/.58/.265`, landmark·section `17/17`, envelope `11` | 사용자 수치 승인, gameplay meter, Collider/Rig |
| C1B-003 Blender Blockout | PASS / START | base Mesh6, render8, source 재렌더8, 최대 측정 편차 `0.000001167H` | Pose, FBX/Unity, watertight production topology |
| C1B-004 static Pose·lineup | PASS / START | Pose8, lineup2×4, render20, mirror deviation `0H` | Animation timing, deformation, physics feel, 2/3/4 runtime |
| 노출 open hole | PASS | 감사한 Neutral·Pose8·Unity four-view에서 background-through hole 0 | 모든 미래 Animation pose의 watertightness |
| 관절·관통 seam | 주의 | shoulder/hip의 닫힌 cap 또는 겹친 component 경계가 일부 view에서 보임 | production deformation·seam 품질 |
| C1B-005 FBX→Unity parity | PASS / START | Mesh6·landmark17, scale/axis/bounds/pivot/fingerprint, Unity capture8 | Rig·Animator·Collider·Material·UV production 품질 |
| Unity four-view silhouette | PASS | bbox drift 최대 `0.004103166H`, four-view 관찰 IoU 기록 | pixel-perfect renderer 동일성, 최종 조명/Shader 승인 |
| Animation/motion 자연스러움 | 후속 | Armature·Action·Animator 0, 정적 Pose만 존재 | locomotion·Punch·Kick·Dropkick 전환과 Ragdoll blend |
| LFS 복구성 | PASS | `.blend`2 + `.fbx`1, LFS `3/3`, private remote push·fresh fetch/checkout 왕복 | 향후 모든 대형 asset 복구성 |
| 권리/배포 경계 | PASS | first-party source/evidence/player-content 분류, license inventory 누락 0 | 실제 Player 포함 결과와 최종 release NOTICE |
| `UG-C1B` | 후속 | user approval/locked value 0 | exact C1b 사용자 승인 |

## 2. Evidence chain

| 단계 | Canonical 입력/산출물 | 핵심 identity |
|---|---|---|
| C1B-001 | [C1a Hybrid Core v0.13](../review/character/C1_CHARACTER_HYBRID_CORE_v0.13_BELLY_CORRECTED_REVIEW.png) | SHA `c1def169cefd59f19339a5b5edbac2dfd0c8fe9a05eba9ee0afb1ae598bab616` |
| C1B-002 | [CharacterProportionProfile](../../config/character/CharacterProportionProfile.yaml) | `CharacterProportionProfile-C1B-002-r01`, measurement SHA `76c98ac…2722` |
| C1B-003 | [Blockout source](../../BlenderSource/Characters/C1B-003/CHR_MasterCharacter_C1B_Blockout_r01.blend) · [Manifest](../../BlenderSource/Characters/C1B-003/GenerationManifest.yaml) · [Measurement](../../BlenderSource/Characters/C1B-003/MeasurementReport.yaml) | source SHA `b0f4e10e…37cc`, `116437` bytes |
| C1B-004 | [Pose source](../../BlenderSource/Characters/C1B-004/CHR_MasterCharacter_C1B_PoseLineup_r02.blend) · [Manifest](../../BlenderSource/Characters/C1B-004/GenerationManifest.yaml) · [Pose report](../../BlenderSource/Characters/C1B-004/PoseLineupReport.yaml) | source SHA `83c2e100…17c2b`, `151456` bytes |
| C1B-005 | [Interop manifest](../../BlenderSource/Characters/C1B-005/GenerationManifest.yaml) · [Comparison report](../../BlenderSource/Characters/C1B-005/InteropComparisonReport.yaml) · [Unity inspection](../evidence/G0/C1B-005/UnityImportInspection.json) | FBX SHA `e2049505…d9d`, Prefab SHA `ae76d098…8c43` |
| C1B-005 profile | [ModelInterop r02](../../config/art/ModelInteropProfile-r02.yaml) · [AlphaVisualQA r02](../../config/art/AlphaVisualQAProfile-r02.yaml) | C1BBlockout-only handedness·UV/tangent override |

`C1B-003`과 `C1B-004`의 r01 profile reference는 당시의 역사 사실이다. `C1B-005`가 사용하는 r02 override가
이전 Manifest를 소급 변경하지 않는다. C1B-004 source도 C1B-005 export 과정에서 수정되지 않았다.

## 3. 방향 source와 exact 비율

### 3.1 C1a source

- v0.13은 rounded head, 짧고 넓은 torso, 낮은 중심, 짧고 굵은 연속 limb라는 큰 방향만 제공한다.
- Source role은 `DIRECTION_ONLY`다. 이미지 pixel을 geometry, Bone, Collider, Anchor, reach 또는 gameplay scale로
  역산하지 않았다.
- 참고 이미지의 고유 Mesh·Material·Animation·수치를 복제하지 않는다.

### 3.2 C1B-002 profile

- normalized height는 `H=1.0`, neutral bounds는 height `1.0H`, width `0.58H`, depth `0.265H`다.
- landmark와 exact-height front/side section은 `17/17`, silhouette envelope는 `11`이다.
- head height는 `0.20H`, forearm terminal bottom은 crotch보다 `0.045H` 위, lower-leg terminal bottom은 ground
  `0H`다.
- 별도 visible hand/finger/fist/foot/shoe/toe Mesh는 `0`이다.
- Profile은 `START/CANDIDATE`; `userApprovalRecorded=false`, `visualApprovalClaimed=false`, locked value `0`이다.

## 4. Blender Blockout 시각·구조 감사

### 4.1 Neutral silhouette

Front·Side·Back·ThreeQuarter의 Neutral/Silhouette 8장을 확인했다.

- width, depth, low center, head/body/limb 비례가 profile과 일치한다.
- View별 crop으로 shape 차이를 숨기지 않았고 같은 source/camera 계약을 사용한다.
- shoulder와 hip의 component 접합은 보이지만 외곽 silhouette를 끊는 background-through hole은 없다.
- knee는 별도 분리 joint가 아니라 한 leg Mesh 안의 폭 변화다. 따라서 열린 knee seam은 없다.
- forearm/lower-leg distal terminal은 contact face가 다소 평평하게 보일 수 있으나 주변 rounding ring을 가진
  START 형태다. 최종 terminal 조형·deformation 품질 승인이 아니다.

### 4.2 “seam”과 “hole”의 구분

이 구분은 이후 감사에서도 그대로 사용한다.

| 분류 | 정의 | 현재 판정 |
|---|---|---|
| 노출 open hole | 의도된 외피가 끊겨 배경 또는 내부 빈 공간이 직접 보임 | 관찰 0 |
| 숨은 proximal open boundary | Neutral base limb의 생산 전 경계가 torso 내부에 포함되어 현재 view에서 노출되지 않음 | 존재 가능, START 한정 허용 |
| 닫힌 cap seam | Action pose용 derivative가 내부 cap polygon으로 경계를 닫았지만 접합 원/초승달이 보임 | 일부 shoulder/hip에 존재, 주의 |
| interpenetration seam | torso와 limb가 겹치면서 생기는 faceted 경계; 배경이 통과하지 않음 | START 한정 허용 |
| production deformation seam | Rig/weight/Animation 중 갈라짐·뒤집힘·구멍 발생 | 아직 검증 불가, C2/C4 후속 |

따라서 현재 보이는 seam을 open hole이라고 보고해서는 안 된다. 반대로 “현재 view에서 hole이 없다”를 근거로
base topology가 watertight하거나 Animation에서도 안전하다고 주장해서도 안 된다.

## 5. Pose8·lineup 시각 감사

검토한 정확한 Pose ID는 `Neutral`, `BothHandsGrab`, `StrikeReady_L`, `StrikeReady_R`, `AirKick_L`,
`AirKick_R`, `Dropkick`, `AirHandReach`다.

| Pose | 판독 결과 | joint/seam 결과 |
|---|---|---|
| Neutral | profile silhouette와 terminal 위치 유지 | shoulder/hip 겹침 seam, 노출 hole 0 |
| BothHandsGrab | 두 forearm terminal이 전방으로 읽힘 | shoulder/torso 관통 seam, terminal hole 0 |
| StrikeReady L/R | 좌우 mirror와 active arm 방향이 구분됨 | 회전 shoulder의 proximal cap crescent/disc가 보이나 닫힌 면임 |
| AirKick L/R | active leg와 좌우 방향이 구분됨 | hip/torso 교차가 있으나 배경 관통 0 |
| Dropkick | 양 lower-leg terminal 전방, Down/Ragdoll과 구분 가능 | hip/torso 교차가 가장 크지만 static closed review 상태 |
| AirHandReach | 두 forearm terminal이 상향·전방으로 읽힘 | shoulder 접합 seam, 노출 hole 0 |

Action pose 7개는 Arm/Leg의 review-only derivative 4개를 사용한다. Base vertex 추가는 `0`, proximal cap polygon은
Mesh당 `+1`, invalid derivation은 `0`이다. 이 cap은 exposed boundary를 닫는 검토용 조치이며
`productionTopologyApproved=false`다.

`Lineup_Overlap`과 `Lineup_Spread`는 동일 profile 4개, root scale `(1,1,1)`을 사용한다. Overlap은 밀집 상황,
Spread는 네 참가자 개별 silhouette를 확인할 수 있다. 이 lineup은 4인 runtime Camera나 Player identity 검증이 아니다.

## 6. FBX·Unity parity

### 6.1 Profile과 import 경계

- Canonical Blender source는 `+X Right / -Y Forward / +Z Up`으로 유지됐다.
- `ModelInteropProfile-ART-001-r02`의 `C1BBlockout` override가 transient handedness transport를 수행한다.
- Unity 결과는 root/export root scale `(1,1,1)`, rotation `0`, determinant `+1`, forward `+Z`다.
- 개별 Unity wrapper rotation/scale/normal 보정은 `0`이다.
- C1BBlockout에만 `UV0=0`, tangent stream `0`, `importTangents=None`을 허용한다. 전역 production UV0/tangent
  계약은 유지되며 C4에서 다시 필수다.

### 6.2 수치·geometry

| 항목 | Unity 결과 | 계약 |
|---|---:|---:|
| Mesh object | `6` | `6` |
| Landmark | `17` | `17` |
| Bounds W/H/D | `0.579999983 / 1.0 / 0.264999986` | `0.58 / 1.0 / 0.265`, tolerance `0.005H` |
| Ground | `0H` | 최대 편차 `0.005H` |
| Landmark 최대 편차 | `0H` | 최대 `0.005H` |
| Negative scale / axis reversal | `0 / 0` | `0 / 0` |
| Imported Material / Rig / Animator / Collider | `0 / 0 / 0 / 0` | static Blockout scope |
| Imported vertex / normal | `1336 / 1336` | recorded geometry signature 일치 |

Position/normal quantized surface signature는 C1B-005 Manifest에 기록된 digest와 일치한다. FBX binary는 Blender의
UUID, CreationTimeStamp와 source path metadata 때문에 재-export byte-identical SHA를 보장하지 않는다. 해당 생성본
SHA는 identity로 기록하되, 재현 Gate는 hierarchy·geometry/topology·transform·silhouette semantic parity다.

### 6.3 Unity four-view

Unity에서 Neutral/Silhouette Front·Side·Back·ThreeQuarter 8장을 2048²로 생성했다.

| View | observed IoU | bbox drift | 시각 판정 |
|---|---:|---:|---|
| Front | `0.998791143` | `0H` | source silhouette 유지, 노출 hole 0 |
| Side | `0.932322920` | `0H` | depth·terminal 배치 유지, 노출 hole 0 |
| Back | `0.998792096` | `0H` | source silhouette 유지, 노출 hole 0 |
| ThreeQuarter | `0.946835392` | `0.004103166H` | `0.005H` 안, shoulder/hip seam 위치 유지 |

IoU는 관찰값이며 사용자 approval threshold로 사용하지 않는다. Pixel-perfect renderer 동일성도 요구하지 않는다.
Bounding box, landmark, geometry fingerprint와 독립 시각 검토를 함께 사용한다. Neutral Back은 QA 조명상 어둡지만
Silhouette가 별도 존재하며 product lighting 승인 대상이 아니다.

## 7. Animation·Physics 경계

현재 캐릭터에는 Armature, Blender Action, Unity Animator, Collider, gameplay Anchor, root motion이 없다.
C1B-004 Pose8은 object transform으로 만든 정지 검토본이다.

따라서 현재 감사가 확인할 수 있는 것은 다음뿐이다.

- 한 frame에서 action 방향과 terminal 판독
- 정적 silhouette, self-intersection, exposed boundary와 joint region
- Neutral Mesh의 Blender→FBX→Unity 전달 동등성

다음은 현재 승인하거나 PASS로 기록하면 안 된다.

- transition timing, anticipation, follow-through와 motion 자연스러움
- shoulder/hip/knee deformation, skin weight와 pose pop
- Animator↔Ragdoll blend와 GetUp
- Punch/Kick/Dropkick Hit phase와 physics feel

Alpha motion은 `ANP-001..003`, Character 체감은 `CHR-012 / UG-C2`, production clip·deformation은
`C4/ANM-001..004`가 소유한다.

## 8. LFS·license·shipping 경계

- Canonical `.blend` 2개와 C1B-005 `.fbx` 1개가 Git LFS-required candidate이며 tracked file도 `3/3`이다.
- FBX는 index pointer, private remote upload, fresh clone fetch/checkout 뒤 materialized SHA·size 일치를 확인했다.
- Existing PNG history migration, history rewrite, force-push는 `0`이다.
- C1B-003/004 `.blend`는 `PRODUCTION_SOURCE`, shipping false다.
- Blender·Unity reference PNG는 `PRODUCTION_EVIDENCE`, shipping false다.
- Unity `Assets` 안의 C1B-005 FBX는 first-party `PLAYER_CONTENT`, shipping allowed true 후보이며 sourceOwner는
  `kjh4845`다.
- 현재 first-party inventory는 source/evidence/player-content `39`개이며 license inventory 누락은 `0`이다.

`shippingAllowed=true`는 그 파일을 Player에 넣을 권리 경계를 뜻할 뿐 실제 Build에 포함됐음을 뜻하지 않는다.
Player Build가 없으므로 최종 Windows Player 포함물과 release NOTICE는 아직 확정하지 않는다.

## 9. 명시적 후속

1. `C1B-006 / UG-C1B`: profile ID/version/수치와 four-view/Pose/lineup을 사용자가 명시적으로 승인한다.
2. `CHR-001..012`, `AIR-001..002`, `ANP-001..003`: prototype Rig·Collider·Joint·Anchor, locomotion, Punch,
   Grab, AirKick, Dropkick, Ragdoll/GetUp과 motion 자연스러움을 검증한다.
3. `CAM-005`와 downstream QA: 실제 Unity 2/3/4인, 세 화면비, Min/Max gameplay Camera를 실행한다.
4. `C4-001..004`: production topology, UV0, tangents, weights, LOD, Material, cage와 deformation을 제작·승인한다.
5. `ANM-001..004`: production action Animation을 in-place/root-motion0 계약 안에서 polish한다.
6. 사용자 수동 Build 이후: 실제 Player 포함 asset과 NOTICE를 다시 감사한다.

## 10. 재사용 감사 체크리스트

### 10.1 Identity·history

- [ ] 승인 source path, bytes, SHA-256이 Evidence와 일치한다.
- [ ] 이전 Task의 Manifest/Profile revision을 새 Task가 소급 덮어쓰지 않았다.
- [ ] Canonical Blender source가 downstream export 과정에서 수정되지 않았다.
- [ ] sourceOwner와 first-party rights evidence가 현재 file hash에 묶여 있다.

### 10.2 Proportion·silhouette

- [ ] H/W/D와 profile tolerance가 일치한다.
- [ ] landmark/section `17/17`, envelope `11`이 누락되지 않았다.
- [ ] head, terminal-crotch, ground contact와 visible hand/foot0 규칙이 유지된다.
- [ ] Front·Side·Back·ThreeQuarter가 같은 source와 고정 framing에서 나온다.
- [ ] color render와 silhouette render를 함께 확인한다.

### 10.3 Hole·seam·joint

- [ ] 배경이 통과하는 exposed open hole이 0이다.
- [ ] 숨은 open boundary와 노출 hole을 구분해 기록한다.
- [ ] review cap이 있는 seam은 cap polygon·winding·normal을 검사한다.
- [ ] shoulder/hip/knee/terminal을 Neutral과 모든 required Pose에서 확인한다.
- [ ] interpenetration seam을 production deformation PASS로 오해하지 않는다.
- [ ] 좌우 mirror Pose의 joint/cap 차이가 허용편차 안이다.

### 10.4 Pose·lineup

- [ ] required Pose ID exact set과 누락/추가 0을 확인한다.
- [ ] Grab/Strike, L/R Kick, Dropkick/Down, AirReach 방향이 구분된다.
- [ ] terminal이 frame 밖으로 잘리거나 torso 뒤에서 완전히 사라지지 않는다.
- [ ] overlap/spread lineup의 participant count·root scale·profile identity가 동일하다.
- [ ] static Pose 결과를 Animation 자연스러움으로 기록하지 않는다.

### 10.5 FBX·Unity

- [ ] Toolchain/Profile/Preset/Override ID·revision·digest가 실제 importer/exporter와 일치한다.
- [ ] root와 export root의 position/rotation/scale, determinant, forward/up/right를 직접 검사한다.
- [ ] Mesh6·landmark17·bounds·ground·normal·geometry signature가 일치한다.
- [ ] manual per-file rotation/scale/normal correction이 0이다.
- [ ] C1BBlockout UV/tangent exception이 production asset으로 누출되지 않았다.
- [ ] Unity four-view capture hash/dimension과 silhouette bbox/parity 결과가 Manifest에 묶여 있다.
- [ ] final user visual approval와 QA 관찰값을 분리한다.

### 10.6 Animation·runtime boundary

- [ ] Armature/Action/Animator/Collider/Anchor 개수를 사실대로 기록한다.
- [ ] 존재하지 않는 Animation, Ragdoll, physics feel을 PASS로 주장하지 않는다.
- [ ] 실제 2/3/4인 runtime capture와 single-character import QA를 구분한다.
- [ ] Player Build, PlayMode, Steam, Docker, Deploy 실행 여부를 사실대로 기록한다.

### 10.7 LFS·license·Evidence

- [ ] `.blend/.fbx` index가 LFS pointer이고 working file SHA·size와 OID가 일치한다.
- [ ] normal push 뒤 private fresh clone에서 pointer→fetch→checkout SHA·size를 검증한다.
- [ ] Binary inventory exact set/count/hash가 현재 tree와 일치한다.
- [ ] Production source/evidence/player-content intended use와 shipping flag가 정확하다.
- [ ] Evidence manifest가 raw report, test result, profile hash와 limitation을 포함한다.
- [ ] 사용자 Gate가 없으면 `LOCKED`, `APPROVED`, motion naturalness를 기록하지 않는다.

## 11. 종료 판정

현재 캐릭터는 C1a 방향, C1B normalized proportion, same-source Blender Blockout, static Pose8·4인 lineup,
FBX→Unity static parity와 LFS/rights 경계까지 준비됐다. 이 범위 안에서는 blocking geometry·open-hole·axis·import
문제가 남아 있지 않다.

그러나 이는 exact C1b 사용자 승인도, 움직이는 캐릭터도, production character도 아니다. 다음 합법적인 순서는
`C1B-006 / UG-C1B`이고, motion 자연스러움은 C2/ANP, 최종 Mesh·UV·weight·deformation은 C4/ANM에서 검증한다.
