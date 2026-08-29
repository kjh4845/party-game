#!/usr/bin/env ruby

require "digest"
require "json"
require "open3"
require "optparse"
require "pathname"
require "set"
require "tmpdir"
require "yaml"

class CharacterPoseLineupVerifier
  MANIFEST_PATH = "BlenderSource/Characters/C1B-004/GenerationManifest.yaml"
  REPORT_PATH = "BlenderSource/Characters/C1B-004/PoseLineupReport.yaml"
  SOURCE_PATH = "BlenderSource/Characters/C1B-004/CHR_MasterCharacter_C1B_PoseLineup_r02.blend"
  RENDER_ROOT = "BlenderSource/Characters/C1B-004/Renders"
  BASE_SOURCE_PATH = "BlenderSource/Characters/C1B-003/CHR_MasterCharacter_C1B_Blockout_r01.blend"
  BASE_MANIFEST_PATH = "BlenderSource/Characters/C1B-003/GenerationManifest.yaml"
  PROFILE_PATH = "config/character/CharacterProportionProfile.yaml"
  GENERATOR_PATH = "tools/blender/create_c1b004_pose_lineup.py"
  INSPECTOR_PATH = "tools/blender/inspect_c1b004_pose_lineup.py"
  RERENDER_PATH = "tools/blender/rerender_c1b004_references.py"
  COMPARATOR_PATH = "tools/blender/compare_c1b004_render_pixels.py"
  DEFAULT_BLENDER = "/Applications/Blender.app/Contents/MacOS/Blender"
  SOURCE_SHA = "83c2e100c74cf75a7faed11dd0ad65c3d07677684e02696e72455fdee4e17c2b"
  BASE_SOURCE_SHA = "b0f4e10e208e60dd07bd91947ef46f09135f602b2ce695becff355cc662837cc"
  BASE_MANIFEST_SHA = "0b195dbc1add30270cc9a259441643cbabaaacacfb2cbbd7449d384f277a4c53"
  REPORT_SHA = "5bb0ee576732154794949c41b81e79310c87d75483ec7fa29751b37c1b0a56f6"
  ENCODED_BUNDLE_SHA = "9f9cbe63a2cf5ec0cf606e89f5114ddc31c86e2a334a4853ae9ca4d467d24a1f"
  PIXEL_BUNDLE_SHA = "5929398d425227feb0678915d504a1bb089dbe6ad4515513210888b903d92c40"
  POSES = %w[Neutral BothHandsGrab StrikeReady_L StrikeReady_R AirKick_L AirKick_R Dropkick AirHandReach].freeze
  LINEUPS = %w[Lineup_Overlap Lineup_Spread].freeze
  STYLES = %w[Neutral Silhouette].freeze
  DIGESTS = {
    "baseMeshesSha256" => "7a87482d2737d3d13594ec05aa1e0b7282940c7c115747e3ea7eb181cf8fe62e",
    "proximalCapMeshesSha256" => "d4c676ef6c400f4b489726717d57865296ab96ff57a8ad47892d4a663c584287",
    "posesSha256" => "f0bff530ffaf01f7b2545957d9eb398bb1b47ba79496ad308c5b5001ed6e0293",
    "lineupsSha256" => "b3f44a081438658c71bc8f1761d7dc4d33f77b6de1eb49999e57919a5d21afa3",
    "readabilitySha256" => "e43d3378bca8f20eb4878c65081e2169058ea6a6519a024aaf10c02651e735d2",
    "camerasSha256" => "876f4d40e648cad8d74e9c63c9aaaa48ebf955c1e6b17a93cd07a2c877c4a34f",
    "renderJobsSha256" => "2091d43fdf16da14a048149033b6840f2337e09807d6143baff73a8567a9bbc7",
    "countsSha256" => "f3b70f25d52629799a09db295b13e4531012c9e8ac2bdc51837017f26cafc899",
    "sceneSha256" => "091140b5a3dc2a3047c7c84caf5ee99ab97e4877782064095bac96590c6b9205",
  }.freeze
  TOOL_HASHES = {
    GENERATOR_PATH => "170a5b7144205221e578f2bc2899168cec8c042040f9dea220e57450c2608544",
    INSPECTOR_PATH => "8e996f81f5b47139a61e78ecd254f3369ffe0f85a413ad554b2a04df20154405",
    RERENDER_PATH => "d279b1882b3e0ac08fa5ea15fa52eb5af2def0f6ec0f94b1c2fa4bda53e35a18",
    COMPARATOR_PATH => "b3cf7a30f007d81db95223ee9699c6b1220af59bffdfa9c525e2727f742cceaa",
  }.freeze
  EXPECTED_TOP = %w[schemaVersion manifestId state candidateStatus completionScope ownerTask sourceOwner recordedAtUtc identity derivedFrom characterProportionProfile poseLineupContract stages report generationTools sourceBoundary execution limitations].sort.freeze
  EXPECTED_IDENTITY = %w[assetId assetVersion toolchainProfileId projectVersionSha256 packageManifestSha256 packageLockSha256 lowPolyStyleProfileId lowPolyStyleProfileRevision modelInteropProfileId modelInteropProfileRevision blenderExportPresetId blenderExportPresetRevision blenderExportSettingsSha256 unityImporterPresetId unityImporterPresetRevision unityImporterSettingsSha256 sourceSha256 fbxSha256 referenceRenderSha256 unityPrefabRevision].sort.freeze
  EXPECTED_REPORT_TOP = %w[schemaVersion reportId state candidateStatus ownerTask assetId assetVersion sourceOwner sourcePath sourceBytes sourceSha256 profilePath profileId measurementSetSha256 lineage contractDigests structure poses lineups readability cameras renders runtimeReadabilityCriteria reviewBoundary execution recordedAtUtc].sort.freeze
  EXPECTED_RUNTIME_CRITERIA = {
    "participantCounts" => [2, 3, 4],
    "state" => "PREPARED_NOT_EXECUTED",
    "runtimeCapturesExecuted" => 0,
    "checks" => [
      {"criterionId"=>"C1B004-RC-STRIKE-TERMINAL-LR", "check"=>"L/R Strike terminal readability"},
      {"criterionId"=>"C1B004-RC-KICK-TERMINAL-LR", "check"=>"L/R Kick terminal readability"},
      {"criterionId"=>"C1B004-RC-GRAB-VS-STRIKE", "check"=>"Grab-vs-Strike readability"},
      {"criterionId"=>"C1B004-RC-DROPKICK-VS-DOWN-RAGDOLL", "check"=>"Dropkick-vs-Down/Ragdoll readability"},
      {"criterionId"=>"C1B004-RC-FULL-BODY-TERMINAL-CROWDING", "check"=>"Full-body and terminal visibility under crowding"},
    ],
  }.freeze
  EXPECTED_LIMITATIONS = [
    "This manifest completes only C1B-004 static pose and four-player lineup review candidates.",
    "The C1B-003 base meshes remain unchanged; action limbs use review-only same-vertex derivatives with one internal proximal cap polygon, and production topology is not approved.",
    "No gameplay rig, collider, anchor, root motion, hit or physics semantics are authored.",
    "FBX export and Unity Prefab identity remain null until C1B-005.",
    "Pose transforms and lineup offsets remain START candidates until C1B-006 user review.",
    "Two-, three-, and four-player runtime action-readability criteria are prepared; runtime captures remain unexecuted.",
    "UG-C1B user approval and production lock have not been recorded.",
  ].freeze
  MAX_YAML = 512 * 1024
  MAX_BINARY = 64 * 1024 * 1024

  def initialize(root, verify_blender: false, check_scope: false, blender: nil)
    @root = Pathname.new(root).expand_path
    @verify_blender = verify_blender
    @check_scope = check_scope
    @blender = blender || ENV["BLENDER_EXECUTABLE"] || DEFAULT_BLENDER
    @violations = []
    @seen = Set.new
    @manifest = {}
    @report = {}
    @outputs = []
    @hash_matches = 0
    @dimension_matches = 0
    @blender_verified = false
    @rerender_matches = 0
    @rerender_exact = 0
  end

  def run
    manifest_doc = load_yaml(MANIFEST_PATH, "MANIFEST")
    report_doc = load_yaml(REPORT_PATH, "REPORT")
    profile_doc = load_yaml(PROFILE_PATH, "PROFILE")
    @manifest = h(manifest_doc && manifest_doc["GenerationManifest"])
    @report = h(report_doc && report_doc["C1B004PoseLineupReport"])
    profile = h(profile_doc && profile_doc["CharacterProportionProfile"])
    expect(manifest_doc.is_a?(Hash) && manifest_doc.keys == ["GenerationManifest"], "MANIFEST_ROOT", MANIFEST_PATH)
    expect(report_doc.is_a?(Hash) && report_doc.keys == ["C1B004PoseLineupReport"], "REPORT_ROOT", REPORT_PATH)
    expect(profile_doc.is_a?(Hash) && profile_doc.keys == ["CharacterProportionProfile"], "PROFILE_ROOT", PROFILE_PATH)
    validate_manifest(profile)
    validate_report
    validate_files
    validate_blender if @verify_blender
    validate_scope if @check_scope
    print_report
    @violations.empty? ? 0 : 1
  rescue StandardError => error
    add("VERIFIER_INTERNAL_ERROR", error.class.name)
    print_report
    1
  end

  private

  def validate_manifest(profile)
    expect(@manifest.keys.sort == EXPECTED_TOP, "MANIFEST_FIELD_SET", MANIFEST_PATH)
    expected = {"schemaVersion"=>1, "manifestId"=>"GM-CHR-MasterCharacter-C1B-PoseLineup-r02", "state"=>"START", "candidateStatus"=>"CANDIDATE", "completionScope"=>"C1B-004_STATIC_POSE_AND_FOUR_PLAYER_LINEUP_COMPLETE", "ownerTask"=>"C1B-004", "sourceOwner"=>"kjh4845"}
    expected.each { |k,v| expect(@manifest[k] == v, "MANIFEST_METADATA", k) }
    expect(@manifest["recordedAtUtc"].to_s.match?(/\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\z/), "MANIFEST_RECORDED_AT", MANIFEST_PATH)
    walk(@manifest) { |value| add("UNAPPROVED_LOCK", MANIFEST_PATH) if value == "LOCKED" }

    identity = h(@manifest["identity"])
    expect(identity.keys.sort == EXPECTED_IDENTITY, "IDENTITY_FIELD_SET", MANIFEST_PATH)
    exact_identity = {
      "assetId"=>"CHR_MasterCharacter_C1B_PoseLineup", "assetVersion"=>"0.2.0-start", "toolchainProfileId"=>"project-hotfix-alpha-toolchain-r02",
      "projectVersionSha256"=>"ee06b66c6e48dc22fe4771812f37f3817b17f9459d44b61202d45e2a44ea3509", "packageManifestSha256"=>"88a1d01cf3abd14843cb3638a7692552e54f62a6d5de003c458a47a869edd741", "packageLockSha256"=>"8708528393a5fce3dd9110fc14815ad2dbc21cefd0efec6102b79d86a62404aa",
      "lowPolyStyleProfileId"=>"LowPolyStyleProfile-ART-001-r01", "lowPolyStyleProfileRevision"=>"r01", "modelInteropProfileId"=>"ModelInteropProfile-ART-001-r01", "modelInteropProfileRevision"=>"r01",
      "blenderExportPresetId"=>"PHX-FBX-MODEL-r01", "blenderExportPresetRevision"=>"r01", "blenderExportSettingsSha256"=>"5707996b33f8ac6773e309c60d05b236655bb4b17e4cc3261642f142a0062ce4",
      "unityImporterPresetId"=>"PHX-UNITY-MODEL-IMPORT-r01", "unityImporterPresetRevision"=>"r01", "unityImporterSettingsSha256"=>"4a00f7ea259ef98d17a932948ca2ebfde7bc7173e632ac5f5b4ddc81fba94cf9", "sourceSha256"=>SOURCE_SHA,
    }
    exact_identity.each { |k,v| expect(identity[k] == v, "IDENTITY_VALUE", k) }
    expect(identity["fbxSha256"].nil? && identity["unityPrefabRevision"].nil?, "IDENTITY_DOWNSTREAM_DEFERRED", MANIFEST_PATH)

    derived = h(@manifest["derivedFrom"])
    expected_derived = {"taskId"=>"C1B-003", "coreRevision"=>"af11dd26d7ee1486a2d53d2332272c1de8f0a6b1", "sourcePath"=>BASE_SOURCE_PATH, "sourceBytes"=>116437, "sourceSha256"=>BASE_SOURCE_SHA, "manifestPath"=>BASE_MANIFEST_PATH, "manifestSha256"=>BASE_MANIFEST_SHA, "sameGeometryLineage"=>true}
    expect(derived == expected_derived, "BASE_LINEAGE", MANIFEST_PATH)
    ref = h(@manifest["characterProportionProfile"])
    expected_ref = {"path"=>PROFILE_PATH, "profileId"=>"CharacterProportionProfile-C1B-002-r01", "version"=>"0.1.0-start", "revision"=>"r01", "measurementSetSha256"=>"76c98acfe8cfbf01b51936b29c2f6ba2e78c26222dfd53c033fe84233e562722", "state"=>"START", "userApprovalRecorded"=>false}
    expect(ref == expected_ref, "PROFILE_REFERENCE", MANIFEST_PATH)
    expect(profile["profileId"] == ref["profileId"] && profile["measurementSetSha256"] == ref["measurementSetSha256"], "PROFILE_DRIFT", PROFILE_PATH)

    contract = h(@manifest["poseLineupContract"])
    expect(contract.keys.sort == (%w[state candidateStatus requiredPoseIds requiredLineupIds participantCountPerLineup userApprovalRecorded lockedValueCount productionTopologyApproved criteriaPrepared runtimeCapturesExecuted] + DIGESTS.keys).sort, "CONTRACT_FIELD_SET", MANIFEST_PATH)
    expect(contract["state"] == "START" && contract["candidateStatus"] == "CANDIDATE" && contract["requiredPoseIds"] == POSES && contract["requiredLineupIds"] == LINEUPS && contract["participantCountPerLineup"] == 4, "POSE_LINEUP_CONTRACT", MANIFEST_PATH)
    DIGESTS.each { |k,v| expect(contract[k] == v, "CONTRACT_DIGEST", k) }
    expect(contract["userApprovalRecorded"] == false && contract["lockedValueCount"] == 0 && contract["productionTopologyApproved"] == false, "CONTRACT_APPROVAL", MANIFEST_PATH)
    expect(contract["criteriaPrepared"] == true && contract["runtimeCapturesExecuted"] == 0, "RUNTIME_CRITERIA_PREPARATION", MANIFEST_PATH)

    stages = h(@manifest["stages"])
    expect(stages.keys.sort == %w[blend-source fbx-export reference-render unity-prefab].sort, "STAGE_SET", MANIFEST_PATH)
    blend = h(stages["blend-source"])
    expect(blend.keys.sort == %w[status path bytes sha256 blenderVersion blenderBuildHash sourceAxis characterForwardAxis rootScale lfsState indexPointerVerified remoteObjectRoundTripVerified].sort, "BLEND_STAGE_FIELD_SET", MANIFEST_PATH)
    expect(blend["status"] == "COMPLETE" && blend["path"] == SOURCE_PATH && blend["bytes"] == 151456 && blend["sha256"] == SOURCE_SHA, "BLEND_STAGE", MANIFEST_PATH)
    expect(blend["lfsState"] == "VERIFIED_REMOTE_ROUND_TRIP" && blend["indexPointerVerified"] == true && blend["remoteObjectRoundTripVerified"] == true, "LFS_ROUND_TRIP_STATE", MANIFEST_PATH)
    expect(blend["rootScale"] == [1.0,1.0,1.0] && blend["sourceAxis"] == "+Z Up" && blend["characterForwardAxis"] == "-Y", "BLEND_TRANSFORM", MANIFEST_PATH)
    expect(h(stages["fbx-export"]) == {"status"=>"DEFERRED_C1B-005","path"=>nil,"bytes"=>nil,"sha256"=>nil,"executed"=>false}, "FBX_DEFERRED", MANIFEST_PATH)
    expect(h(stages["unity-prefab"]) == {"status"=>"DEFERRED_C1B-005","path"=>nil,"revision"=>nil,"executed"=>false}, "UNITY_DEFERRED", MANIFEST_PATH)
    validate_render_stage(h(stages["reference-render"]), identity)

    report = h(@manifest["report"])
    expect(report == {"path"=>REPORT_PATH,"sha256"=>REPORT_SHA,"result"=>"PASS"}, "REPORT_REFERENCE", MANIFEST_PATH)
    tools = h(@manifest["generationTools"])
    expected_tool_keys = {"generator"=>GENERATOR_PATH,"inspector"=>INSPECTOR_PATH,"rerender"=>RERENDER_PATH,"pixelComparator"=>COMPARATOR_PATH}
    expect(tools.keys.sort == expected_tool_keys.keys.sort, "GENERATION_TOOL_FIELD_SET", MANIFEST_PATH)
    expected_tool_keys.each do |name,path|
      expect(h(tools[name]) == {"path"=>path,"sha256"=>TOOL_HASHES[path]}, "GENERATION_TOOL_REFERENCE", name)
    end
    boundary = h(@manifest["sourceBoundary"])
    expected_boundary = {"baseMeshPartsPreserved"=>6,"baseLinkedReviewMeshObjects"=>68,"proximalCapMeshDatablocks"=>4,"proximalCapDerivedReviewMeshObjects"=>28,"proximalCapVerticesAddedPerMesh"=>0,"proximalCapPolygonsAddedPerMesh"=>1,"linkedMeshMismatchObjects"=>0,"productionTopologyApproved"=>false,"visibleSeparateHandFingerFistFootShoeToeObjects"=>0,"negativeScaleObjects"=>0,"nonUnitReviewRoots"=>0,"externalOrPackedInputs"=>0,"armatures"=>0,"actions"=>0,"colliderObjects"=>0,"gameplayRigAuthored"=>false,"gameplayColliderAuthored"=>false,"gameplayAnchorAuthored"=>false,"rootMotionAuthored"=>false,"physicsOrHitSemanticsAuthored"=>false}
    expect(boundary == expected_boundary, "SOURCE_BOUNDARY", MANIFEST_PATH)
    expected_execution = {"blenderSourceFiles"=>1,"staticPoseCandidates"=>8,"fourPlayerLineups"=>2,"lineupParticipantInstances"=>8,"referenceRenders"=>20,"fbxExports"=>0,"unityImports"=>0,"unityAssets"=>0,"armatures"=>0,"actions"=>0,"colliderProfiles"=>0,"playerBuilds"=>0,"dockerExecutions"=>0,"deployExecutions"=>0}
    expect(h(@manifest["execution"]) == expected_execution, "EXECUTION_SCOPE", MANIFEST_PATH)
    expect(@manifest["limitations"] == EXPECTED_LIMITATIONS, "LIMITATIONS", MANIFEST_PATH)
  end

  def validate_render_stage(render, identity)
    expect(render.keys.sort == %w[status renderRoot canonicalReferenceFile canonicalReferenceSha256 orderedBundleSha256 decodedPixelHashAlgorithm localReproductionMatches localReproductionExactMatches reproducedMaximumChannelDifference reproducedMaximumChangedChannelRatio reproductionAllowedMaximumChannelDifference reproductionAllowedMaximumChangedChannelRatio renderResolutionPixels outputs].sort, "RENDER_STAGE_FIELD_SET", MANIFEST_PATH)
    expect(render["status"] == "COMPLETE" && render["renderRoot"] == RENDER_ROOT && render["renderResolutionPixels"] == [2048,2048], "RENDER_STAGE", MANIFEST_PATH)
    expect(render["orderedBundleSha256"] == ENCODED_BUNDLE_SHA && render["decodedPixelHashAlgorithm"] == "SHA256(RGB:WxH:+decoded RGB bytes)", "RENDER_DIGEST_METADATA", MANIFEST_PATH)
    expect(render["localReproductionMatches"] == 20 && render["localReproductionExactMatches"] == 5 && render["reproducedMaximumChannelDifference"] == 1 && render["reproducedMaximumChangedChannelRatio"] == 0.0000006357828776041666 && render["reproductionAllowedMaximumChannelDifference"] == 1 && render["reproductionAllowedMaximumChangedChannelRatio"] == 0.000001, "RENDER_REPRODUCTION_METADATA", MANIFEST_PATH)
    outputs = render["outputs"]
    unless outputs.is_a?(Array)
      add("RENDER_OUTPUTS", MANIFEST_PATH)
      return
    end
    @outputs = outputs
    expect(outputs.length == 20 && outputs.map { |x| x.is_a?(Hash) ? x["file"] : nil }.compact.uniq.length == 20, "RENDER_OUTPUT_EXACT_SET", MANIFEST_PATH)
    expected_pairs = (POSES.product(STYLES) + LINEUPS.product(STYLES)).sort
    actual_pairs = outputs.select { |x| x.is_a?(Hash) }.map { |x| [x["scenarioId"],x["style"]] }.sort
    expect(actual_pairs == expected_pairs, "RENDER_SCENARIO_STYLE_SET", MANIFEST_PATH)
    outputs.each do |entry|
      next unless entry.is_a?(Hash)
      expect(entry.keys.sort == %w[scenarioId style view camera file bytes sha256 decodedPixelSha256].sort, "RENDER_OUTPUT_FIELDS", entry["file"])
      scenario, style, view = entry.values_at("scenarioId","style","view")
      expect(entry["file"] == "CHR_MasterCharacter_C1B_PoseLineup_r02_#{scenario}_#{style}_#{view}.png", "RENDER_FILENAME_SEMANTICS", entry["file"])
      expected_view = scenario.start_with?("Lineup_") || scenario == "Neutral" ? "Front" : "ThreeQuarter"
      expected_camera = if scenario == "Neutral" then "CAM_C1B004_Pose_Front" elsif scenario == "Lineup_Overlap" then "CAM_C1B004_Lineup_Overlap_Front" elsif scenario == "Lineup_Spread" then "CAM_C1B004_Lineup_Spread_Front" elsif scenario == "StrikeReady_L" then "CAM_C1B004_Pose_ThreeQuarter_Mirror" else "CAM_C1B004_Pose_ThreeQuarter" end
      expect(view == expected_view && entry["camera"] == expected_camera, "RENDER_VIEW_CAMERA", entry["file"])
    end
    canonical = outputs.find { |x| x["file"] == render["canonicalReferenceFile"] }
    expect(canonical && canonical["sha256"] == render["canonicalReferenceSha256"] && identity["referenceRenderSha256"] == canonical["sha256"], "CANONICAL_RENDER", MANIFEST_PATH)
  end

  def validate_report
    expect(file_sha(REPORT_PATH) == REPORT_SHA, "REPORT_CANONICAL_SHA", REPORT_PATH)
    expect(@report.keys.sort == EXPECTED_REPORT_TOP, "REPORT_FIELD_SET", REPORT_PATH)
    expected = {"schemaVersion"=>1,"reportId"=>"C1B004-CHR-MasterCharacter-PoseLineup-r02","state"=>"START","candidateStatus"=>"CANDIDATE","ownerTask"=>"C1B-004","assetId"=>"CHR_MasterCharacter_C1B_PoseLineup","assetVersion"=>"0.2.0-start","sourceOwner"=>"kjh4845","sourcePath"=>SOURCE_PATH,"sourceBytes"=>151456,"sourceSha256"=>SOURCE_SHA,"profilePath"=>PROFILE_PATH,"profileId"=>"CharacterProportionProfile-C1B-002-r01","measurementSetSha256"=>"76c98acfe8cfbf01b51936b29c2f6ba2e78c26222dfd53c033fe84233e562722"}
    expected.each { |k,v| expect(@report[k] == v, "REPORT_METADATA", k) }
    DIGESTS.each { |k,v| expect(h(@report["contractDigests"])[k] == v, "REPORT_CONTRACT_DIGEST", k) }
    lineage = h(@report["lineage"])
    expect(lineage["baseSourceSha256"] == BASE_SOURCE_SHA && lineage["baseManifestSha256"] == BASE_MANIFEST_SHA && lineage["sameGeometryLineage"] == true && lineage["baseMeshPartsPreserved"] == 6 && lineage["baseLinkedReviewMeshObjects"] == 68 && lineage["proximalCapMeshDatablocks"] == 4 && lineage["proximalCapDerivedReviewMeshObjects"] == 28 && lineage["proximalCapVerticesAddedPerMesh"] == 0 && lineage["proximalCapPolygonsAddedPerMesh"] == 1 && lineage["linkedMeshMismatchObjects"] == 0 && lineage["productionTopologyApproved"] == false, "REPORT_LINEAGE", REPORT_PATH)
    poses = h(@report["poses"]); lineups = h(@report["lineups"])
    expect(poses["ids"] == POSES && poses["required"] == 8 && poses["inspected"] == 8 && poses["missing"] == 0 && poses["neutralGeometryMode"] == "BASE_LINKED" && poses["actionLimbGeometryMode"] == "BASE_PLUS_PROXIMAL_CAP" && poses["baseLinkedMeshObjects"] == 20 && poses["proximalCapDerivedMeshObjects"] == 28 && poses["invalidGeometryDerivations"] == 0 && poses["userApprovalRecorded"] == false && poses["lockedValueCount"] == 0 && poses["result"] == "PASS", "REPORT_POSES", REPORT_PATH)
    expect(lineups["ids"] == LINEUPS && lineups["participantCountPerLineup"] == 4 && lineups["totalParticipantInstances"] == 8 && lineups["geometryMode"] == "BASE_LINKED" && lineups["baseLinkedMeshObjects"] == 48 && lineups["result"] == "PASS", "REPORT_LINEUPS", REPORT_PATH)
    readability = h(@report["readability"])
    positive = %w[grabForwardDeltaLeftH grabForwardDeltaRightH strikeReadyBackDeltaLeftH strikeReadyBackDeltaRightH airKickForwardDeltaLeftH airKickForwardDeltaRightH dropkickForwardDeltaLeftH dropkickForwardDeltaRightH airReachHeightDeltaLeftH airReachHeightDeltaRightH]
    mirrors = %w[strikeMirrorMaximumDeviationH kickMirrorMaximumDeviationH grabMirrorMaximumDeviationH dropkickMirrorMaximumDeviationH airReachMirrorMaximumDeviationH]
    expect(positive.all? { |k| finite?(readability[k]) && readability[k] >= 0.10 } && mirrors.all? { |k| finite?(readability[k]) && readability[k] <= 0.000001 } && readability["result"] == "PASS", "REPORT_READABILITY", REPORT_PATH)
    renders = h(@report["renders"])
    expect(renders["expectedFiles"] == 20 && renders["inspectedFiles"] == 20 && renders["encodedHashMatches"] == 20 && renders["sourceRerenderMatches"] == 20 && renders["sourceRerenderExactMatches"] == 5 && renders["reproducedMaximumChannelDifference"] == 1 && renders["reproducedMaximumChangedChannelRatio"] == 0.0000006357828776041666 && renders["reproductionAllowedMaximumChannelDifference"] == 1 && renders["reproductionAllowedMaximumChangedChannelRatio"] == 0.000001 && renders["result"] == "PASS", "REPORT_RENDERS", REPORT_PATH)
    expect(h(@report["runtimeReadabilityCriteria"]) == EXPECTED_RUNTIME_CRITERIA, "RUNTIME_READABILITY_CRITERIA", REPORT_PATH)
    review = h(@report["reviewBoundary"])
    expect(review["internalStructuralReview"] == "PASS" && review["userVisualApprovalRecorded"] == false && review["lockedValueCount"] == 0 && review["productionTopologyApproved"] == false && review["notes"].is_a?(Array) && review["notes"].length == 5, "REPORT_APPROVAL_BOUNDARY", REPORT_PATH)
    expect(h(@report["execution"])["fbxExportsCreated"] == 0 && h(@report["execution"])["unityAssetsCreated"] == 0 && h(@report["execution"])["playerBuildsExecuted"] == 0 && h(@report["execution"])["armaturesCreated"] == 0 && h(@report["execution"])["actionsCreated"] == 0 && h(@report["execution"])["colliderProfilesCreated"] == 0, "REPORT_EXECUTION", REPORT_PATH)
    walk(@report) { |value| add("REPORT_UNAPPROVED_LOCK", REPORT_PATH) if value == "LOCKED" }
  end

  def validate_files
    verify_binary(SOURCE_PATH, 151456, SOURCE_SHA, "BLEND_SOURCE")
    verify_binary(BASE_SOURCE_PATH, 116437, BASE_SOURCE_SHA, "BASE_SOURCE")
    verify_digest(BASE_MANIFEST_PATH, BASE_MANIFEST_SHA, "BASE_MANIFEST")
    verify_digest("Project hotfix/ProjectSettings/ProjectVersion.txt", "ee06b66c6e48dc22fe4771812f37f3817b17f9459d44b61202d45e2a44ea3509", "PROJECT_VERSION")
    verify_digest("Project hotfix/Packages/manifest.json", "88a1d01cf3abd14843cb3638a7692552e54f62a6d5de003c458a47a869edd741", "PACKAGE_MANIFEST")
    verify_digest("Project hotfix/Packages/packages-lock.json", "8708528393a5fce3dd9110fc14815ad2dbc21cefd0efec6102b79d86a62404aa", "PACKAGE_LOCK")
    TOOL_HASHES.each { |path,sha| verify_digest(path, sha, "GENERATION_TOOL") }
    encoded_lines = []
    pixel_lines = []
    @outputs.sort_by { |x| x["file"].to_s }.each do |entry|
      path = "#{RENDER_ROOT}/#{entry["file"]}"
      before = @violations.length
      verify_binary(path, entry["bytes"], entry["sha256"], "REFERENCE_RENDER")
      @hash_matches += 1 if @violations.length == before
      dimensions = png_dimensions(path)
      if dimensions == [2048,2048]
        @dimension_matches += 1
      else
        add("RENDER_PNG_DIMENSIONS", path)
      end
      encoded_lines << "#{path}=#{entry["sha256"]}\n"
      pixel_lines << "#{entry["file"]}=#{entry["decodedPixelSha256"]}\n"
    end
    expect(Digest::SHA256.hexdigest(encoded_lines.join) == ENCODED_BUNDLE_SHA, "ENCODED_BUNDLE", MANIFEST_PATH)
    expect(Digest::SHA256.hexdigest(pixel_lines.join) == PIXEL_BUNDLE_SHA, "PIXEL_BUNDLE", MANIFEST_PATH)
    expected_files = [MANIFEST_PATH,REPORT_PATH,SOURCE_PATH] + @outputs.map { |x| "#{RENDER_ROOT}/#{x["file"]}" }
    actual_files = Dir[@root.join("BlenderSource/Characters/C1B-004/**/*")].select { |p| File.file?(p) || File.symlink?(p) }.map { |p| Pathname.new(p).relative_path_from(@root).to_s }.sort
    expect(actual_files == expected_files.sort, "C1B004_FILE_SET", "BlenderSource/Characters/C1B-004")
  end

  def validate_blender
    unless File.file?(@blender) && File.executable?(@blender)
      add("BLENDER_UNAVAILABLE", @blender)
      return
    end
    stdout, stderr, status = Open3.capture3(@blender, "--background", @root.join(SOURCE_PATH).to_s, "--python", @root.join(INSPECTOR_PATH).to_s)
    unless status.success?
      add("BLENDER_INSPECT_FAILED", stderr.lines.last.to_s.strip)
      return
    end
    line = stdout.lines.find { |x| x.start_with?("C1B004_INSPECTION_JSON=") }
    payload = line && JSON.parse(line.split("=",2)[1])
    unless payload.is_a?(Hash)
      add("BLENDER_INSPECTION_PAYLOAD", SOURCE_PATH)
      return
    end
    sections = {"baseMeshesSha256"=>payload["baseMeshes"],"proximalCapMeshesSha256"=>payload["proximalCapMeshes"],"posesSha256"=>payload["poses"],"lineupsSha256"=>payload["lineups"],"readabilitySha256"=>payload["readability"],"camerasSha256"=>payload["cameras"],"renderJobsSha256"=>payload["renderJobs"],"countsSha256"=>payload["counts"],"sceneSha256"=>payload["scene"]}
    sections.each { |name,value| expect(canonical_sha(value) == DIGESTS[name], "BLENDER_#{name.upcase}", SOURCE_PATH) }
    scene = h(payload["scene"]); counts = h(payload["counts"])
    expect(scene["state"] == "START" && scene["userVisualApprovalRecorded"] == false && scene["lockedValueCount"] == 0 && scene["productionTopologyApproved"] == false && scene["poseReviewGeometryMode"] == "C1B003_BASE_PLUS_INTERNAL_PROXIMAL_CAP", "BLENDER_SCENE_BOUNDARY", SOURCE_PATH)
    expect(counts["armatures"] == 0 && counts["actions"] == 0 && counts["colliderObjects"] == 0 && counts["separateHandFootMeshObjects"] == 0 && counts["negativeScaleObjects"] == 0 && counts["nonUnitReviewRoots"] == 0 && counts["proximalCapMeshDatablocks"] == 4 && counts["proximalCapDerivedReviewMeshObjects"] == 28, "BLENDER_COUNTS", SOURCE_PATH)
    expect(payload["linkedMeshMismatchObjects"] == [] && payload["externalImages"] == [] && payload["packedImages"] == [], "BLENDER_FORBIDDEN_SCOPE", SOURCE_PATH)
    validate_pose_payload(payload)
    Dir.mktmpdir("c1b004-rerender") do |dir|
      _out, err, rerender_status = Open3.capture3(@blender, "--background", @root.join(SOURCE_PATH).to_s, "--python", @root.join(RERENDER_PATH).to_s, "--", dir)
      unless rerender_status.success?
        add("BLENDER_RERENDER_FAILED", err.lines.last.to_s.strip)
        next
      end
      comparison, compare_err, compare_status = Open3.capture3("python3", @root.join(COMPARATOR_PATH).to_s, @root.join(RENDER_ROOT).to_s, dir)
      unless compare_status.success?
        add("PIXEL_COMPARISON_FAILED", compare_err.lines.last.to_s.strip)
        next
      end
      json_line = comparison.lines.find { |x| x.start_with?("C1B004_RENDER_REPRODUCTION_JSON=") }
      result = json_line && JSON.parse(json_line.split("=",2)[1])
      if result.is_a?(Hash)
        outputs = Array(result["outputs"])
        @rerender_matches = outputs.count { |x| x["matches"] == true }
        @rerender_exact = outputs.count { |x| x["exactPixelMatch"] == true }
        max_difference = outputs.map { |x| x["maximumChannelDifference"].to_i }.max || 999
        max_ratio = outputs.map { |x| x["changedChannelRatio"].to_f }.max || 1.0
        expect(result["allMatch"] == true && @rerender_matches == 20 && max_difference <= 1 && max_ratio <= 0.000001, "PIXEL_REPRODUCTION", SOURCE_PATH)
      else
        add("PIXEL_COMPARISON_PAYLOAD", SOURCE_PATH)
      end
    end
    @blender_verified = !@violations.any? { |rule,_| rule.start_with?("BLENDER_") || rule.start_with?("PIXEL_") }
  end

  def validate_pose_payload(payload)
    poses = Array(payload["poses"]); lineups = Array(payload["lineups"])
    expect(poses.map { |x| x["poseId"] } == POSES, "BLENDER_POSE_SET", SOURCE_PATH)
    poses.each do |pose|
      expect(pose["rootCount"] == 1 && pose["meshObjectCount"] == 6 && pose["rootScale"] == [1.0,1.0,1.0], "BLENDER_POSE_ROOT", pose["poseId"])
      h(pose["parts"]).each do |part,entry|
        mode = pose["poseId"] == "Neutral" || %w[Head Torso].include?(part) ? "BASE_LINKED" : "BASE_PLUS_PROXIMAL_CAP"
        expect(entry["mode"] == mode && entry["validBaseDerivation"] == true && (mode != "BASE_LINKED" || entry["linkedToBase"] == true) && (mode != "BASE_PLUS_PROXIMAL_CAP" || (entry["vertices"] == 193 && entry["polygons"] == 193)), "BLENDER_GEOMETRY_LINEAGE", "#{pose["poseId"]}/#{part}")
      end
    end
    expect(lineups.map { |x| x["lineupId"] } == LINEUPS, "BLENDER_LINEUP_SET", SOURCE_PATH)
    lineups.each do |lineup|
      expect(lineup["participantCountDeclared"] == 4 && lineup["rootCount"] == 4 && Array(lineup["instances"]).length == 4, "BLENDER_LINEUP_PARTICIPANTS", lineup["lineupId"])
      Array(lineup["instances"]).each { |instance| expect(h(instance["parts"]).values.all? { |x| x["mode"] == "BASE_LINKED" && x["linkedToBase"] == true && x["validBaseDerivation"] == true }, "BLENDER_LINEUP_GEOMETRY", lineup["lineupId"]) }
    end
  end

  def validate_scope
    out, _err, status = Open3.capture3("git", "-C", @root.to_s, "status", "--porcelain", "-z", "--untracked-files=all")
    unless status.success?
      add("SCOPE_GIT_STATUS", @root)
      return
    end
    allowed = [
      "tools/verify_character_pose_lineup.rb", "tools/tests/verify_character_pose_lineup_test.rb",
      GENERATOR_PATH, INSPECTOR_PATH, RERENDER_PATH, COMPARATOR_PATH,
      "config/repository/BinaryAssetPolicy.md", "config/repository/BinaryAssetInventory.yaml",
      "config/licenses/LicensePolicy.yaml", "config/licenses/ThirdPartyInventory.yaml",
      "tools/verify_lfs_repository.rb", "tools/tests/verify_lfs_repository_test.rb",
      "tools/verify_license_inventory.rb", "tools/tests/verify_license_inventory_test.rb",
      "docs/03_IMPLEMENTATION_PLAN.md", "artifacts/reports/FOUNDATION_DECISION_RATIONALE.md",
    ]
    out.split("\0").each do |entry|
      path = entry.length >= 4 ? entry[3..] : ""
      next if path.start_with?("BlenderSource/Characters/C1B-004/", "artifacts/evidence/G0/C1B-004/") || allowed.include?(path)
      add("C1B004_SCOPE", path)
    end
  end

  def load_yaml(relative, label)
    path = @root.join(relative)
    st = path.lstat rescue nil
    unless st
      add("#{label}_MISSING", relative); return nil
    end
    if st.symlink?
      add("#{label}_SYMLINK", relative); return nil
    end
    if st.size > MAX_YAML
      add("#{label}_TOO_LARGE", relative); return nil
    end
    text = path.binread
    detect_duplicate_keys(text, label, relative)
    YAML.safe_load(text, permitted_classes: [], permitted_symbols: [], aliases: false)
  rescue Psych::Exception => error
    add("#{label}_YAML_INVALID", "#{relative}:#{error.class}")
    nil
  end

  def detect_duplicate_keys(text, label, relative)
    walk_node = lambda do |node|
      if node.is_a?(Psych::Nodes::Mapping)
        keys = Set.new
        node.children.each_slice(2) do |key,value|
          scalar = key.respond_to?(:value) ? key.value : nil
          add("#{label}_YAML_DUPLICATE_KEY", "#{relative}:#{scalar}") if scalar && !keys.add?(scalar)
          walk_node.call(value)
        end
      elsif node.respond_to?(:children) && node.children
        node.children.each { |child| walk_node.call(child) }
      end
    end
    walk_node.call(Psych.parse_stream(text))
  rescue Psych::Exception
    nil
  end

  def verify_binary(relative, bytes, sha, label)
    path = @root.join(relative); st = path.lstat rescue nil
    unless st
      add("#{label}_MISSING", relative); return
    end
    if st.symlink?
      add("#{label}_SYMLINK", relative); return
    end
    expect(st.file? && st.size <= MAX_BINARY, "#{label}_TYPE_OR_SIZE", relative)
    expect(st.size == bytes, "#{label}_SIZE", relative)
    expect(Digest::SHA256.file(path).hexdigest == sha, "#{label}_SHA", relative)
  end

  def verify_digest(relative, sha, label)
    path = @root.join(relative); st = path.lstat rescue nil
    unless st
      add("#{label}_MISSING", relative); return
    end
    if st.symlink?
      add("#{label}_SYMLINK", relative); return
    end
    expect(Digest::SHA256.file(path).hexdigest == sha, "#{label}_SHA", relative)
  end

  def png_dimensions(relative)
    path = @root.join(relative)
    return nil unless path.file? && !path.symlink?
    data = path.binread(24)
    return nil unless data.byteslice(0,8) == "\x89PNG\r\n\x1a\n".b && data.byteslice(12,4) == "IHDR"
    data.byteslice(16,8).unpack("NN")
  rescue StandardError
    nil
  end

  def file_sha(relative)
    path = @root.join(relative)
    path.file? && !path.symlink? ? Digest::SHA256.file(path).hexdigest : nil
  end

  def canonical_sha(value)
    Digest::SHA256.hexdigest(JSON.generate(deep_sort(value)))
  end

  def deep_sort(value)
    case value
    when Hash then value.keys.sort.to_h { |key| [key, deep_sort(value[key])] }
    when Array then value.map { |entry| deep_sort(entry) }
    else value
    end
  end

  def finite?(value); value.is_a?(Numeric) && value.finite?; end
  def h(value); value.is_a?(Hash) ? value : {}; end
  def walk(value, &block)
    yield value
    value.each_value { |x| walk(x,&block) } if value.is_a?(Hash)
    value.each { |x| walk(x,&block) } if value.is_a?(Array)
  end
  def expect(condition, rule, path); add(rule,path) unless condition; end
  def add(rule,path)
    key = [rule,path.to_s]
    return unless @seen.add?(key)
    @violations << key
  end

  def print_report
    puts "CHARACTER_POSE_LINEUP_AUDIT=C1B-004"
    puts "SOURCE_HASH_MATCH=#{file_sha(SOURCE_PATH) == SOURCE_SHA}"
    puts "REFERENCE_RENDER_COUNT=#{@outputs.length}"
    puts "REFERENCE_RENDER_HASH_MATCHES=#{@hash_matches}"
    puts "REFERENCE_RENDER_PNG_2048_MATCHES=#{@dimension_matches}"
    puts "BLENDER_VERIFICATION_REQUESTED=#{@verify_blender}"
    puts "BLENDER_VERIFIED=#{@blender_verified}" if @verify_blender
    puts "POSES_VERIFIED=#{POSES.length}" if @blender_verified
    puts "LINEUPS_VERIFIED=#{LINEUPS.length}" if @blender_verified
    puts "RENDER_REPRODUCTION_PIXEL_MATCHES=#{@rerender_matches}" if @verify_blender
    puts "RENDER_REPRODUCTION_EXACT_MATCHES=#{@rerender_exact}" if @verify_blender
    puts "C1B004_SCOPE_CHECKED=#{@check_scope}"
    puts "TOTAL_VIOLATIONS=#{@violations.length}"
    @violations.sort.each { |rule,path| puts "VIOLATION rule=#{rule} path=#{path}" }
    puts "FINAL_RESULT=#{@violations.empty? ? "PASS" : "FAIL"}"
  end
end

options = {verify_blender: false, check_scope: false}
OptionParser.new do |parser|
  parser.banner = "usage: ruby tools/verify_character_pose_lineup.rb [--root PATH] [--verify-blender] [--check-c1b004-scope]"
  parser.on("--root PATH") { |path| options[:root] = path }
  parser.on("--verify-blender") { options[:verify_blender] = true }
  parser.on("--check-c1b004-scope") { options[:check_scope] = true }
  parser.on("--blender PATH") { |path| options[:blender] = path }
end.parse!
if ARGV.any?
  warn "CHARACTER_POSE_LINEUP_AUDIT=ERROR reason=USAGE"; exit 2
end
root = Pathname.new(options.fetch(:root, Pathname.new(__dir__).join("..").expand_path.to_s)).expand_path
unless root.directory?
  warn "CHARACTER_POSE_LINEUP_AUDIT=ERROR reason=INVALID_ROOT"; exit 2
end
exit CharacterPoseLineupVerifier.new(root, verify_blender: options[:verify_blender], check_scope: options[:check_scope], blender: options[:blender]).run
