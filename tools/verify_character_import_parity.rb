#!/usr/bin/env ruby

require "digest"
require "fileutils"
require "json"
require "open3"
require "optparse"
require "pathname"
require "rexml/document"
require "set"
require "tmpdir"
require "yaml"

class CharacterImportParityVerifier
  MANIFEST = "BlenderSource/Characters/C1B-005/GenerationManifest.yaml"
  REPORT = "BlenderSource/Characters/C1B-005/InteropComparisonReport.yaml"
  BLEND = "BlenderSource/Characters/C1B-004/CHR_MasterCharacter_C1B_PoseLineup_r02.blend"
  FBX = "Project hotfix/Assets/ProjectHotfix/Art/Characters/C1B-005/CHR_MasterCharacter_C1B_Neutral_r02.fbx"
  FBX_META = FBX + ".meta"
  PREFAB = "Project hotfix/Assets/ProjectHotfix/Art/Characters/C1B-005/CHR_MasterCharacter_C1B_Neutral_r02.prefab"
  PREFAB_META = PREFAB + ".meta"
  INSPECTION = "artifacts/evidence/G0/C1B-005/UnityImportInspection.json"
  CAPTURE_ROOT = "artifacts/evidence/G0/C1B-005/Captures/Unity"
  MODEL_PROFILE = "config/art/ModelInteropProfile-r02.yaml"
  QA_PROFILE = "config/art/AlphaVisualQAProfile-r02.yaml"
  EXPORTER = "tools/blender/export_c1b005_neutral_fbx.py"
  INSPECTOR = "tools/blender/inspect_c1b005_fbx.py"
  UNITY_CONTRACT = "Project hotfix/Assets/ProjectHotfix/Editor/C1B005/C1B005ImportContract.cs"
  UNITY_POSTPROCESSOR = "Project hotfix/Assets/ProjectHotfix/Editor/C1B005/C1B005ModelPostprocessor.cs"
  UNITY_PIPELINE = "Project hotfix/Assets/ProjectHotfix/Editor/C1B005/C1B005ParityPipeline.cs"
  UNITY_TEST = "Project hotfix/Assets/ProjectHotfix/Tests/EditMode/C1B005/C1B005ImportParityTests.cs"
  DEFAULT_BLENDER = "/Applications/Blender.app/Contents/MacOS/Blender"
  DEFAULT_UNITY = "/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity"
  BLEND_SHA = "83c2e100c74cf75a7faed11dd0ad65c3d07677684e02696e72455fdee4e17c2b"
  FBX_SHA = "e2049505f6be24508783710c83691445b28fa39cad892bcd88aa0e2ad4807d9d"
  FBX_META_SHA = "e6f13b6bd983deb56b3936b1dc2d1626a2113bac887f0cccbc33966caeecf15f"
  PREFAB_SHA = "ae76d0982e232f8e4344087f58e47f3331bf7b688abbe9ca73236b7706318c43"
  PREFAB_META_SHA = "30047b4953e7ed4da6b5c76b8e0ffe9c88452854374512bdf1de6519445915e6"
  INSPECTION_SHA = "94901f3b4d98e14e037e23348aac14dbfface273cbededa8dd5fac037d87bd71"
  REPORT_SHA = "ea377d660e91dd89c241ea6570a12d6092e005679e6f512d05c3190c359b0d23"
  UNITY_CAPTURE_BUNDLE_SHA = "70c98f5d70fd61a6ed5c8d07cc48125612761e1291e491357fa4fc36fdfedfe9"
  SOURCE_CAPTURE_BUNDLE_SHA = "e9697f1c104b1fccf9af0a12aaa2e028a031da2d7b4c062341b688dfb7ac4c43"
  ACTION_BUNDLE_SHA = "9f9cbe63a2cf5ec0cf606e89f5114ddc31c86e2a334a4853ae9ca4d467d24a1f"
  POINT_SHA = "cff38fcc751280c16e2efa77c061090bb8b5302b569b1f26ce202d674f39d3b6"
  SURFACE_SHA = "289d5f3e8f9105e54b5293d587c51e58a3f73d9766fab9c9cba7a5fe26ecc5f6"
  FILE_HASHES = {
    MODEL_PROFILE => "75c8df9e283268cc3a11fce70ce039107cd73d94a98f0f1831365133e1c6d4a0",
    QA_PROFILE => "358db218a34b7d09e41dbd6596ca2f79d0b74a3f0eea01d1ecd2bf562d0ed1ef",
    EXPORTER => "c5345f8cfc4bf32f88d054bdb82e656112df42bf1aa7f9d505143a575f486b73",
    INSPECTOR => "aa66ecf3e4b91cc0c1ddad4074c3a9a7fe712f91b5dda3ef2f3a0010566340b8",
    UNITY_CONTRACT => "bdf18c4502a459fd26c70a5e0c3f86c6b2ab6199c500cf49cf0925fa5ada9add",
    UNITY_POSTPROCESSOR => "1ad156958a2103f165cbfaed80ec68ccdd1061199a6d8cd2b567f4bafdd35e15",
    UNITY_PIPELINE => "c5e1ee37d8bf64471cb747b46ad0b5701fcb2dc92c4c9d1decc5c458a0484d13",
    UNITY_TEST => "586d68d2018c82a538f2cb9d2b95a8d4bbd20e2bb370b865bd79ad071341cb03",
  }.freeze
  VIEWS = %w[Back Front Side ThreeQuarter].freeze
  STYLES = %w[Neutral Silhouette].freeze
  CAPTURE_PATHS = STYLES.product(VIEWS).map do |style, view|
    "#{CAPTURE_ROOT}/CHR_MasterCharacter_C1B_Neutral_r02_#{style}_#{view}.png"
  end.sort.freeze
  MAX_YAML = 512 * 1024
  MAX_BINARY = 64 * 1024 * 1024

  def initialize(root, verify_blender: false, verify_unity: false, check_scope: false, blender: nil, unity: nil)
    @root = Pathname.new(root).expand_path
    @verify_blender = verify_blender
    @verify_unity = verify_unity
    @check_scope = check_scope
    @blender = blender || DEFAULT_BLENDER
    @unity = unity || DEFAULT_UNITY
    @violations = []
    @seen = Set.new
    @manifest = {}
    @report = {}
    @inspection = {}
    @capture_hash_matches = 0
    @capture_dimensions = 0
    @blender_verified = false
    @unity_tests = nil
  end

  def run
    manifest_doc = load_yaml(MANIFEST, "MANIFEST")
    report_doc = load_yaml(REPORT, "REPORT")
    @manifest = h(manifest_doc && manifest_doc["GenerationManifest"])
    @report = h(report_doc && report_doc["C1B005InteropComparisonReport"])
    expect(manifest_doc.is_a?(Hash) && manifest_doc.keys == ["GenerationManifest"], "MANIFEST_ROOT", MANIFEST)
    expect(report_doc.is_a?(Hash) && report_doc.keys == ["C1B005InteropComparisonReport"], "REPORT_ROOT", REPORT)
    load_inspection
    validate_manifest
    validate_report
    validate_files
    validate_profiles
    validate_meta
    validate_inspection
    validate_scope if @check_scope
    validate_blender if @verify_blender
    validate_unity if @verify_unity
    print_report
    @violations.empty? ? 0 : 1
  rescue StandardError => error
    add("VERIFIER_INTERNAL_ERROR", error.class.name)
    print_report
    1
  end

  private

  def validate_manifest
    expected_top = %w[schemaVersion manifestId state candidateStatus completionScope ownerTask sourceOwner recordedAtUtc identity stages inspection report generationTools parityContract sourceBoundary execution limitations]
    expect(@manifest.keys.sort == expected_top.sort, "MANIFEST_FIELD_SET", MANIFEST)
    expected = {"schemaVersion"=>1,"manifestId"=>"GM-CHR-MasterCharacter-C1B-Interop-r01","state"=>"START","candidateStatus"=>"CANDIDATE","completionScope"=>"C1B-005_STATIC_BLOCKOUT_INTEROP_PARITY_COMPLETE","ownerTask"=>"C1B-005","sourceOwner"=>"kjh4845"}
    expected.each { |k,v| expect(@manifest[k] == v, "MANIFEST_METADATA", k) }
    walk(@manifest) { |value| add("UNAPPROVED_LOCK", MANIFEST) if value == "LOCKED" }
    identity = h(@manifest["identity"])
    expect(identity["sourceSha256"] == BLEND_SHA && identity["fbxSha256"] == FBX_SHA && identity["unityPrefabRevision"] == "e4c57671925554af4aa4e36feea50f81", "IDENTITY_HASH_GUID", MANIFEST)
    expect(identity["modelInteropProfileId"] == "ModelInteropProfile-ART-001-r02" && identity["modelInteropProfileRevision"] == "r02" && identity["modelInteropProfileSha256"] == FILE_HASHES[MODEL_PROFILE], "IDENTITY_MODEL_PROFILE", MANIFEST)
    expect(identity["alphaVisualQaProfileId"] == "AlphaVisualQAProfile-ART-001-r02" && identity["alphaVisualQaProfileRevision"] == "r02" && identity["alphaVisualQaProfileSha256"] == FILE_HASHES[QA_PROFILE], "IDENTITY_QA_PROFILE", MANIFEST)
    stages = h(@manifest["stages"])
    expect(stages.keys.sort == %w[blend-source fbx-export reference-render unity-prefab animation].sort, "STAGE_SET", MANIFEST)
    blend = h(stages["blend-source"])
    expect(blend == {"status"=>"REFERENCE_COMPLETE","ownerTask"=>"C1B-004","path"=>BLEND,"bytes"=>151456,"sha256"=>BLEND_SHA,"mutatedByC1b005"=>false}, "BLEND_REFERENCE_STAGE", MANIFEST)
    fbx = h(stages["fbx-export"])
    expect(fbx["status"] == "COMPLETE" && fbx["path"] == FBX && fbx["bytes"] == 70268 && fbx["sha256"] == FBX_SHA && fbx["metaPath"] == FBX_META && fbx["metaSha256"] == FBX_META_SHA && fbx["guid"] == "250d071cf52954f0586c84d27ec778db", "FBX_STAGE", MANIFEST)
    expect(fbx["lfsState"] == "VERIFIED_REMOTE_ROUND_TRIP" && fbx["indexPointerVerified"] == true && fbx["remoteObjectRoundTripVerified"] == true, "FBX_LFS_ROUND_TRIP", MANIFEST)
    render = h(stages["reference-render"])
    expect(render["sourceReferenceCount"] == 8 && render["sourceReferenceOrderedBundleSha256"] == SOURCE_CAPTURE_BUNDLE_SHA && render["unityReferenceCount"] == 8 && render["unityReferenceOrderedBundleSha256"] == UNITY_CAPTURE_BUNDLE_SHA && render["dimensions"] == [2048,2048], "REFERENCE_STAGE", MANIFEST)
    outputs = render["outputs"]
    expect(outputs.is_a?(Array) && outputs.length == 8 && outputs.map { |x| x["path"] }.sort == CAPTURE_PATHS, "REFERENCE_OUTPUT_SET", MANIFEST)
    prefab = h(stages["unity-prefab"])
    expect(prefab["status"] == "COMPLETE" && prefab["path"] == PREFAB && prefab["sha256"] == PREFAB_SHA && prefab["metaPath"] == PREFAB_META && prefab["metaSha256"] == PREFAB_META_SHA && prefab["guid"] == "e4c57671925554af4aa4e36feea50f81" && prefab["identityOnly"] == true && prefab["gameplayComponents"] == 0, "PREFAB_STAGE", MANIFEST)
    expect(h(stages["animation"]) == {"status"=>"NOT_APPLICABLE_STATIC_BLOCKOUT","armatures"=>0,"animators"=>0,"actions"=>0,"motionNaturalnessClaimed"=>false}, "ANIMATION_STAGE", MANIFEST)
    expect(h(@manifest["inspection"]) == {"path"=>INSPECTION,"sha256"=>INSPECTION_SHA,"result"=>"PASS"}, "INSPECTION_REFERENCE", MANIFEST)
    expect(h(@manifest["report"]) == {"path"=>REPORT,"sha256"=>REPORT_SHA,"result"=>"PASS"}, "REPORT_REFERENCE", MANIFEST)
    parity = h(@manifest["parityContract"])
    expect(parity["meshObjects"] == 6 && parity["landmarks"] == 17 && parity["pointSignatureSha256"] == POINT_SHA && parity["surfaceSignatureSha256"] == SURFACE_SHA && parity["silhouetteMaskThreshold"] == 128 && parity["silhouetteBoundingBoxMaximumDriftH"] == 0.005 && parity["silhouetteIouApprovalThresholdApplied"] == false && parity["staticActionMotionNaturalnessClaimed"] == false, "PARITY_CONTRACT", MANIFEST)
    boundary = h(@manifest["sourceBoundary"])
    expect(boundary == {"c1bBlockoutUv0State"=>"DEFERRED_C1B_BLOCKOUT_ONLY","c1bBlockoutTangents"=>"None","globalProductionUv0AndTangentRulesPreserved"=>true,"armatureCreated"=>false,"animationCreated"=>false,"colliderCreated"=>false,"manualModelCorrectionCount"=>0,"userVisualApprovalRecorded"=>false,"productionLockRecorded"=>false}, "SOURCE_BOUNDARY", MANIFEST)
    execution = h(@manifest["execution"])
    expect(execution == {"fbxExports"=>1,"unityImports"=>1,"unityPrefabs"=>1,"unityCaptures"=>8,"c1b005EditModeTests"=>7,"fullEditModeTests"=>59,"playModeTests"=>0,"playerBuilds"=>0,"dockerExecutions"=>0,"deployExecutions"=>0}, "EXECUTION_SCOPE", MANIFEST)
    expected_limitations = [
      "C1B-005 proves static Blockout source-to-FBX-to-Unity scale, axis, landmark, bounds, surface-signature and four-view silhouette parity only.",
      "The C1BBlockout UV0/tangent exception is local to this static candidate; global production UV0 and tangent requirements remain unchanged.",
      "The identity Prefab contains no gameplay Rig, Animator, Collider, Anchor or manual model correction.",
      "C1B-004 action Pose images were rechecked as unchanged static evidence; motion or animation naturalness is not claimed.",
      "Neutral readability lighting is QA-only and does not approve product lighting, palette, material or visual lock.",
      "FBX LFS pointer, upload and fresh-fetch materialization are verified for core revision 2ce719415f3ebbb785396d82e93ce479bb3c28c9; Player Build, PlayMode, Docker and deployment were not executed.",
    ]
    expect(@manifest["limitations"] == expected_limitations, "LIMITATIONS", MANIFEST)
    expect(@manifest["recordedAtUtc"].to_s.match?(/\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\z/), "MANIFEST_RECORDED_AT", MANIFEST)
  end

  def validate_report
    expect(file_sha(REPORT) == REPORT_SHA, "REPORT_CANONICAL_SHA", REPORT)
    expect(@report["state"] == "START" && @report["candidateStatus"] == "CANDIDATE" && @report["result"] == "PASS" && @report["sourceOwner"] == "kjh4845", "REPORT_METADATA", REPORT)
    chain = h(@report["sourceChain"])
    expect(chain["c1b004BlendSha256"] == BLEND_SHA && chain["canonicalSourceMutated"] == false && chain["fbxSha256"] == FBX_SHA && chain["fbxGuid"] == "250d071cf52954f0586c84d27ec778db" && chain["prefabSha256"] == PREFAB_SHA && chain["prefabGuid"] == "e4c57671925554af4aa4e36feea50f81", "REPORT_SOURCE_CHAIN", REPORT)
    fbx = h(@report["fbxBlenderInspection"])
    expect(fbx["result"] == "PASS" && fbx["meshObjects"] == 6 && fbx["landmarks"] == 17 && fbx["maximumLandmarkTransportDeviationH"].to_f <= 0.005 && fbx["axisReversalCount"] == 0 && fbx["negativeScaleObjects"] == 0 && fbx["nonFiniteVertexCount"] == 0 && fbx["nonFiniteNormalCount"] == 0, "REPORT_FBX_INSPECTION", REPORT)
    unity = h(@report["unityImport"])
    expect(unity["inspectionSha256"] == INSPECTION_SHA && unity["importerSettingsMatched"] == true && unity["meshObjects"] == 6 && unity["landmarks"] == 17 && unity["maximumLandmarkDeviationH"] == 0.0 && unity["rootForward"] == [0.0,0.0,1.0] && unity["rootDeterminant"] == 1.0 && unity["exportRootForward"] == [0.0,0.0,1.0] && unity["exportRootDeterminant"] == 1.0 && unity["axisReversalCount"] == 0 && unity["negativeScaleCount"] == 0, "REPORT_UNITY_IMPORT", REPORT)
    signatures = h(@report["geometrySignatures"])
    expect(signatures["pointSha256"] == POINT_SHA && signatures["surfaceSha256"] == SURFACE_SHA && signatures["result"] == "PASS", "REPORT_GEOMETRY_SIGNATURE", REPORT)
    silhouette = h(@report["silhouetteParity"])
    views = silhouette["views"]
    expect(silhouette["maskThreshold"] == 128 && silhouette["dimensions"] == [2048,2048] && silhouette["boundingBoxMaximumDriftHThreshold"] == 0.005 && silhouette["iouApprovalThresholdApplied"] == false && views.is_a?(Array) && views.length == 4 && views.all? { |v| v["maximumBoundingBoxDriftH"].to_f <= 0.005 && v["intersectionOverUnionObserved"].to_f > 0 && v["result"] == "PASS" }, "REPORT_SILHOUETTE", REPORT)
    neutral = h(@report["neutralReadability"])
    expect(neutral["state"] == "QA_ONLY_OBSERVED" && neutral["productLighting"] == false && neutral["approvalThresholdApplied"] == false && neutral["fillLightCount"] == 3 && Array(neutral["foregroundVsBackgroundObserved"]).length == 4, "REPORT_NEUTRAL_READABILITY", REPORT)
    action = h(@report["staticActionReview"])
    expect(action["state"] == "READ_ONLY_SOURCE_EVIDENCE_RECONFIRMED" && action["files"] == 20 && action["orderedBundleSha256"] == ACTION_BUNDLE_SHA && action["animationActionsPresent"] == 0 && action["motionNaturalnessClaimed"] == false, "REPORT_STATIC_ACTION", REPORT)
    boundary = h(@report["scopeBoundary"])
    expect(boundary["c1bBlockoutUv0State"] == "DEFERRED_C1B_BLOCKOUT_ONLY" && boundary["c1bBlockoutImportTangents"] == "None" && boundary["globalProductionUv0RequiredPreserved"] == true && boundary["globalProductionTangentPolicyPreserved"] == true && boundary["animationStage"] == "NOT_APPLICABLE_STATIC_BLOCKOUT" && boundary["rigAuthored"] == false && boundary["colliderAuthored"] == false && boundary["manualModelTransformCorrectionCount"] == 0 && boundary["userVisualApprovalRecorded"] == false && boundary["productionLockRecorded"] == false, "REPORT_SCOPE_BOUNDARY", REPORT)
    execution = h(@report["execution"])
    expect(execution["playerBuilds"] == 0 && execution["playModeTests"] == 0 && execution["animationActions"] == 0 && execution["dockerExecutions"] == 0 && execution["deployExecutions"] == 0, "REPORT_EXECUTION", REPORT)
    walk(@report) { |value| add("REPORT_FAKE_APPROVAL", REPORT) if value == "LOCKED" || value == "APPROVED" }
  end

  def validate_files
    verify_binary(BLEND, 151456, BLEND_SHA, "BLEND")
    verify_binary(FBX, 70268, FBX_SHA, "FBX")
    verify_digest(FBX_META, FBX_META_SHA, "FBX_META")
    verify_digest(PREFAB, PREFAB_SHA, "PREFAB")
    verify_digest(PREFAB_META, PREFAB_META_SHA, "PREFAB_META")
    verify_digest(INSPECTION, INSPECTION_SHA, "INSPECTION")
    FILE_HASHES.each { |path,sha| verify_digest(path,sha,"TOOL_OR_PROFILE") }
    outputs = h(h(@manifest["stages"])["reference-render"])["outputs"]
    lines = []
    Array(outputs).sort_by { |x| x["path"].to_s }.each do |entry|
      path = entry["path"]
      before = @violations.length
      verify_binary(path, entry["bytes"], entry["sha256"], "CAPTURE")
      @capture_hash_matches += 1 if before == @violations.length
      if png_dimensions(path) == [2048,2048]
        @capture_dimensions += 1
      else
        add("CAPTURE_DIMENSIONS", path)
      end
      lines << "#{path}=#{entry["sha256"]}\n"
    end
    expect(Digest::SHA256.hexdigest(lines.join) == UNITY_CAPTURE_BUNDLE_SHA, "CAPTURE_BUNDLE_SHA", MANIFEST)
    actual = Dir[@root.join("#{CAPTURE_ROOT}/*.png")].map { |path| Pathname.new(path).relative_path_from(@root).to_s }.sort
    expect(actual == CAPTURE_PATHS, "CAPTURE_FILE_SET", CAPTURE_ROOT)
  end

  def validate_profiles
    model = h(load_yaml(MODEL_PROFILE,"MODEL_PROFILE")&.dig("ModelInteropProfile"))
    qa = h(load_yaml(QA_PROFILE,"QA_PROFILE")&.dig("AlphaVisualQAProfile"))
    expect(model["profileId"] == "ModelInteropProfile-ART-001-r02" && model["revision"] == "r02", "MODEL_PROFILE_ID", MODEL_PROFILE)
    export_override = model.dig("blenderExportPreset","assetClassOverrides","C1BBlockout") || {}
    import_override = model.dig("unityImporterPreset","assetClassOverrides","C1BBlockout") || {}
    expect(export_override["overrideId"] == "PHX-FBX-C1B-BLOCKOUT-r02" && export_override["settingsSha256"] == "21b50c577b30f79d5717806f0687550c267181ddb3e8d0b9b9213e6133a02f29" && export_override.dig("settings","reflectAxis") == "X" && export_override.dig("settings","bake_space_transform") == true && export_override.dig("settings","armatureAllowed") == false, "MODEL_EXPORT_OVERRIDE", MODEL_PROFILE)
    expect(import_override["overrideId"] == "PHX-UNITY-C1B-BLOCKOUT-r02" && import_override["settingsSha256"] == "6e7dc965bf635789ed6447e53c873dd25fab915a1698e54db936564ded24303d" && import_override.dig("settings","importTangents") == "None" && import_override.dig("settings","uv0State") == "DEFERRED_C1B_BLOCKOUT_ONLY" && import_override.dig("settings","globalProductionUv0RequiredPreserved") == true && model.dig("meshDataContract","invariants","uv0Required") == true, "MODEL_IMPORT_OVERRIDE", MODEL_PROFILE)
    expect(qa["profileId"] == "AlphaVisualQAProfile-ART-001-r02" && qa.dig("profileReferences","modelInteropProfileId") == model["profileId"] && qa.dig("fixedNeutralStage","ambientFill","totalRelativeIntensity") == 0.35 && qa.dig("fixedNeutralStage","keyLight","unityRay") == [0.321393818,-0.556670368,-0.766044438], "QA_PROFILE_CONTRACT", QA_PROFILE)
  end

  def validate_meta
    fbx = @root.join(FBX_META).read
    prefab = @root.join(PREFAB_META).read
    expect(fbx.match?(/^guid: 250d071cf52954f0586c84d27ec778db$/), "FBX_META_GUID", FBX_META)
    required = [/materialImportMode: 0$/, /globalScale: 1$/, /meshCompression: 0$/, /isReadable: 0$/, /addColliders: 0$/, /importBlendShapes: 0$/, /importCameras: 0$/, /importLights: 0$/, /generateSecondaryUV: 0$/, /useFileUnits: 1$/, /useFileScale: 1$/, /weldVertices: 1$/, /bakeAxisConversion: 0$/, /preserveHierarchy: 1$/, /normalImportMode: 0$/, /tangentImportMode: 2$/, /^  importAnimation: 0$/, /^  animationType: 0$/]
    expect(required.all? { |pattern| fbx.match?(pattern) }, "FBX_META_IMPORTER_SETTINGS", FBX_META)
    expect(prefab.match?(/^guid: e4c57671925554af4aa4e36feea50f81$/), "PREFAB_META_GUID", PREFAB_META)
    prefab_text = @root.join(PREFAB).read
    expect(!prefab_text.match?(/Animator|Collider|Rigidbody|MonoBehaviour/), "PREFAB_COMPONENT_SCOPE", PREFAB)
  end

  def load_inspection
    path = @root.join(INSPECTION)
    @inspection = path.file? && !path.symlink? ? JSON.parse(path.read) : {}
  rescue JSON::ParserError
    add("INSPECTION_JSON_INVALID", INSPECTION)
    @inspection = {}
  end

  def validate_inspection
    p = @inspection
    expect(p["result"] == "PASS" && p["state"] == "START" && p["candidateStatus"] == "CANDIDATE", "INSPECTION_METADATA", INSPECTION)
    expect(p["sourceSha256"] == FBX_SHA && p["modelGuid"] == "250d071cf52954f0586c84d27ec778db" && p["prefabGuid"] == "e4c57671925554af4aa4e36feea50f81", "INSPECTION_IDENTITY", INSPECTION)
    expect(p["importerSettingsMatched"] == true && p["importerOverrideId"] == "PHX-UNITY-C1B-BLOCKOUT-r02", "INSPECTION_IMPORTER", INSPECTION)
    expect(p["meshObjectCount"] == 6 && p["landmarkCount"] == 17 && p["maximumLandmarkDeviationH"] == 0.0 && p["combinedBoundsSize"] == {"x"=>0.5799999833106995,"y"=>1.0,"z"=>0.26499998569488525} && p["groundHeight"] == 0.0, "INSPECTION_GEOMETRY", INSPECTION)
    expect(p["rootForward"] == {"x"=>0.0,"y"=>0.0,"z"=>1.0} && p["rootDeterminant"] == 1.0 && p["exportRootForward"] == {"x"=>0.0,"y"=>0.0,"z"=>1.0} && p["exportRootDeterminant"] == 1.0 && p["negativeScaleCount"] == 0 && p["axisReversalCount"] == 0, "INSPECTION_AXIS", INSPECTION)
    signature = h(p["geometrySignature"])
    expect(signature["pointSha256"] == POINT_SHA && signature["surfaceSha256"] == SURFACE_SHA && signature["vertexCount"] == 1336 && signature["normalCount"] == 1336, "INSPECTION_SIGNATURE", INSPECTION)
    silhouettes = p["silhouetteParity"]
    expect(silhouettes.is_a?(Array) && silhouettes.length == 4 && silhouettes.all? { |v| v["threshold"] == 128 && v["width"] == 2048 && v["height"] == 2048 && v["maximumBoundingBoxDriftH"].to_f <= 0.005 && v["iouApprovalThresholdApplied"] == false && v["result"] == "PASS" }, "INSPECTION_SILHOUETTE", INSPECTION)
    neutral = p["neutralContrast"]
    expect(neutral.is_a?(Array) && neutral.length == 4 && neutral.all? { |v| v["approvalThresholdApplied"] == false && v["absoluteLuminanceDifferenceObserved"].to_f > 0 }, "INSPECTION_NEUTRAL", INSPECTION)
    action = h(p["actionStaticReview"])
    expect(action["fileCount"] == 20 && action["orderedBundleSha256"] == ACTION_BUNDLE_SHA && action["animationActionsPresent"] == 0 && action["motionNaturalnessClaimed"] == false, "INSPECTION_STATIC_ACTION", INSPECTION)
    expect(p["armatureCount"] == 0 && p["animatorCount"] == 0 && p["colliderCount"] == 0 && p["playerBuildsExecuted"] == 0 && p["playModeTestsExecuted"] == 0 && p["manualTransformCorrections"] == 0 && p["animationNaturalnessClaimed"] == false, "INSPECTION_SCOPE", INSPECTION)
  end

  def validate_blender
    unless File.executable?(@blender)
      add("BLENDER_UNAVAILABLE", @blender); return
    end
    out, err, status = Open3.capture3(@blender,"--background","--factory-startup","--python",@root.join(INSPECTOR).to_s,"--",@root.join(FBX).to_s)
    line = out.lines.find { |value| value.start_with?("C1B005_FBX_INSPECTION_JSON=") }
    payload = line && JSON.parse(line.split("=",2)[1])
    expect(status.success? && payload.is_a?(Hash) && payload["result"] == "PASS" && payload["errors"] == [] && payload.dig("file","sha256") == FBX_SHA && payload["maximumLandmarkPositionDeviationH"].to_f <= 0.005 && payload.dig("invalidGeometry","nonFiniteVertexCount") == 0 && payload.dig("invalidGeometry","nonFiniteNormalCount") == 0, "BLENDER_INSPECTION", err.lines.last.to_s)
    @blender_verified = status.success? && payload.is_a?(Hash) && payload["result"] == "PASS"
  rescue JSON::ParserError
    add("BLENDER_INSPECTION_JSON", FBX)
  end

  def validate_unity
    unless File.executable?(@unity)
      add("UNITY_UNAVAILABLE", @unity); return
    end
    Dir.mktmpdir("c1b005-unity-test") do |directory|
      isolated_root = Pathname.new(directory)
      isolated_project = isolated_root.join("Project hotfix")
      FileUtils.mkdir_p(isolated_project)
      %w[Assets Packages ProjectSettings].each do |name|
        FileUtils.cp_r(@root.join("Project hotfix", name), isolated_project)
      end
      isolated_evidence = isolated_root.join("artifacts/evidence/G0/C1B-005")
      FileUtils.mkdir_p(isolated_evidence.dirname)
      FileUtils.cp_r(@root.join("artifacts/evidence/G0/C1B-005"), isolated_evidence.dirname)
      xml = File.join(directory,"results.xml")
      log = File.join(directory,"unity.log")
      _out, _err, status = Open3.capture3(@unity,"-batchmode","-nographics","-projectPath",isolated_project.to_s,"-runTests","-testPlatform","EditMode","-assemblyNames","ProjectHotfix.C1B005.Tests.Editor","-testResults",xml,"-logFile",log)
      if status.success? && File.file?(xml)
        root = REXML::Document.new(File.read(xml)).root
        @unity_tests = root.attributes["total"].to_i
        expect(root.attributes["result"] == "Passed" && root.attributes["total"] == "7" && root.attributes["failed"] == "0" && root.attributes["skipped"] == "0", "UNITY_EDITMODE_RESULT", INSPECTION)
      else
        add("UNITY_EDITMODE_RUN", File.file?(log) ? File.readlines(log).last.to_s.strip : "missing log")
      end
    end
  end

  def validate_scope
    out, _err, status = Open3.capture3("git","-C",@root.to_s,"status","--porcelain","-z","--untracked-files=all")
    unless status.success?; add("SCOPE_GIT", @root); return; end
    allowed_prefixes = ["BlenderSource/Characters/C1B-005/","Project hotfix/Assets/ProjectHotfix/Art","Project hotfix/Assets/ProjectHotfix/Editor/C1B005","Project hotfix/Assets/ProjectHotfix/Tests/EditMode/C1B005","artifacts/evidence/G0/C1B-005/"]
    allowed = FILE_HASHES.keys + ["Project hotfix/Assets/ProjectHotfix/Editor.meta","tools/verify_character_import_parity.rb","tools/tests/verify_character_import_parity_test.rb","tools/verify_character_pose_lineup.rb","tools/verify_art_profiles.rb","tools/tests/verify_art_profiles_test.rb","docs/00_DOCUMENT_INDEX.md","docs/ART_DIRECTION.md","docs/CHARACTER_TECHNICAL_SPEC.md","docs/WEAPON_DESIGN.md","docs/03_IMPLEMENTATION_PLAN.md","docs/04_IMPLEMENTATION_TRACEABILITY.md","artifacts/reports/FOUNDATION_DECISION_RATIONALE.md","artifacts/reports/CHARACTER_FULL_AUDIT.md","config/repository/BinaryAssetPolicy.md","config/repository/BinaryAssetInventory.yaml","config/licenses/ThirdPartyInventory.yaml","tools/verify_lfs_repository.rb","tools/tests/verify_lfs_repository_test.rb","tools/verify_license_inventory.rb","tools/tests/verify_license_inventory_test.rb"]
    out.split("\0").each do |entry|
      path = entry.length >= 4 ? entry[3..] : ""
      next if allowed.include?(path) || allowed_prefixes.any? { |prefix| path.start_with?(prefix) }
      add("C1B005_SCOPE", path)
    end
    forbidden = Dir[@root.join("Project hotfix/Assets/ProjectHotfix/Art/Characters/C1B-005/*")].select { |path| File.file?(path) }.map { |path| File.extname(path).downcase }
    expect((forbidden & %w[.anim .controller .mat .asset .unity]).empty?, "C1B005_FORBIDDEN_ASSET", "C1B-005")
  end

  def load_yaml(relative,label)
    path=@root.join(relative); st=path.lstat rescue nil
    unless st; add("#{label}_MISSING",relative); return nil; end
    if st.symlink?; add("#{label}_SYMLINK",relative); return nil; end
    if st.size>MAX_YAML; add("#{label}_TOO_LARGE",relative); return nil; end
    text=path.binread; detect_duplicates(text,label,relative)
    YAML.safe_load(text,permitted_classes:[],permitted_symbols:[],aliases:false)
  rescue Psych::Exception=>error
    add("#{label}_YAML_INVALID","#{relative}:#{error.class}"); nil
  end

  def detect_duplicates(text,label,relative)
    walk_node=lambda do |node|
      if node.is_a?(Psych::Nodes::Mapping)
        keys=Set.new
        node.children.each_slice(2){|key,value| scalar=key.respond_to?(:value) ? key.value : nil; add("#{label}_YAML_DUPLICATE_KEY","#{relative}:#{scalar}") if scalar && !keys.add?(scalar); walk_node.call(value)}
      elsif node.respond_to?(:children) && node.children
        node.children.each{|child|walk_node.call(child)}
      end
    end
    walk_node.call(Psych.parse_stream(text))
  rescue Psych::Exception
    nil
  end

  def verify_binary(relative,bytes,sha,label)
    path=@root.join(relative); st=path.lstat rescue nil
    unless st; add("#{label}_MISSING",relative); return; end
    if st.symlink?; add("#{label}_SYMLINK",relative); return; end
    expect(st.file? && st.size<=MAX_BINARY,"#{label}_TYPE",relative)
    expect(st.size==bytes,"#{label}_SIZE",relative)
    expect(Digest::SHA256.file(path).hexdigest==sha,"#{label}_SHA",relative)
  end

  def verify_digest(relative,sha,label)
    path=@root.join(relative); st=path.lstat rescue nil
    unless st; add("#{label}_MISSING",relative); return; end
    if st.symlink?; add("#{label}_SYMLINK",relative); return; end
    expect(Digest::SHA256.file(path).hexdigest==sha,"#{label}_SHA",relative)
  end

  def png_dimensions(relative)
    data=@root.join(relative).binread(24)
    return nil unless data.byteslice(0,8)=="\x89PNG\r\n\x1a\n".b && data.byteslice(12,4)=="IHDR"
    data.byteslice(16,8).unpack("NN")
  rescue StandardError
    nil
  end

  def file_sha(relative); path=@root.join(relative); path.file? ? Digest::SHA256.file(path).hexdigest : nil; end
  def h(value); value.is_a?(Hash) ? value : {}; end
  def walk(value,&block); yield value; value.each_value{|x|walk(x,&block)} if value.is_a?(Hash); value.each{|x|walk(x,&block)} if value.is_a?(Array); end
  def expect(condition,rule,path); add(rule,path) unless condition; end
  def add(rule,path); key=[rule,path.to_s]; return unless @seen.add?(key); @violations<<key; end

  def print_report
    puts "CHARACTER_IMPORT_PARITY_AUDIT=C1B-005"
    puts "FBX_HASH_MATCH=#{file_sha(FBX)==FBX_SHA}"
    puts "PREFAB_HASH_MATCH=#{file_sha(PREFAB)==PREFAB_SHA}"
    puts "CAPTURE_COUNT=#{CAPTURE_PATHS.length}"
    puts "CAPTURE_HASH_MATCHES=#{@capture_hash_matches}"
    puts "CAPTURE_PNG_2048_MATCHES=#{@capture_dimensions}"
    puts "BLENDER_VERIFIED=#{@blender_verified}" if @verify_blender
    puts "UNITY_EDITMODE_TESTS=#{@unity_tests}" if @verify_unity
    puts "C1B005_SCOPE_CHECKED=#{@check_scope}"
    puts "TOTAL_VIOLATIONS=#{@violations.length}"
    @violations.sort.each{|rule,path|puts "VIOLATION rule=#{rule} path=#{path}"}
    puts "FINAL_RESULT=#{@violations.empty? ? "PASS" : "FAIL"}"
  end
end

options={verify_blender:false,verify_unity:false,check_scope:false}
OptionParser.new do |parser|
  parser.on("--root PATH"){|v|options[:root]=v}
  parser.on("--verify-blender"){options[:verify_blender]=true}
  parser.on("--verify-unity"){options[:verify_unity]=true}
  parser.on("--check-c1b005-scope"){options[:check_scope]=true}
  parser.on("--blender PATH"){|v|options[:blender]=v}
  parser.on("--unity PATH"){|v|options[:unity]=v}
end.parse!
root=Pathname.new(options.fetch(:root,Pathname.new(__dir__).join("..").expand_path.to_s)).expand_path
exit CharacterImportParityVerifier.new(root,verify_blender:options[:verify_blender],verify_unity:options[:verify_unity],check_scope:options[:check_scope],blender:options[:blender],unity:options[:unity]).run
