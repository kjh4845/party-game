#!/usr/bin/env ruby

require "digest"
require "json"
require "open3"
require "optparse"
require "pathname"
require "set"
require "tmpdir"
require "yaml"

class CharacterBlockoutVerifier
  MANIFEST_PATH = "BlenderSource/Characters/C1B-003/GenerationManifest.yaml"
  REPORT_PATH = "BlenderSource/Characters/C1B-003/MeasurementReport.yaml"
  PROFILE_PATH = "config/character/CharacterProportionProfile.yaml"
  SOURCE_PATH = "BlenderSource/Characters/C1B-003/CHR_MasterCharacter_C1B_Blockout_r01.blend"
  RENDER_ROOT = "BlenderSource/Characters/C1B-003/Renders"
  GENERATOR_PATH = "tools/blender/create_c1b003_blockout.py"
  INSPECTOR_PATH = "tools/blender/inspect_c1b003_blockout.py"
  RERENDER_PATH = "tools/blender/rerender_c1b003_references.py"
  PIXEL_COMPARATOR_PATH = "tools/blender/compare_c1b003_render_pixels.py"
  DEFAULT_BLENDER = "/Applications/Blender.app/Contents/MacOS/Blender"
  MAX_YAML_BYTES = 512 * 1024
  MAX_SCRIPT_BYTES = 1024 * 1024
  MAX_BINARY_BYTES = 64 * 1024 * 1024
  START_TOLERANCE_H = 0.005
  EPSILON = 0.000001
  EXPECTED_REPORT_SHA256 = "79c5ae3a62c91f1aad236d18c29cf8dd27dcfa90fba0d3450f3dcc8b4fe1e0ab"
  EXPECTED_LIMITATIONS = [
    "This manifest completes only the C1B-003 Blender source and Neutral four-view reference scope.",
    "The canonical referenceRenderSha256 identifies Neutral Front; orderedBundleSha256 binds all eight Neutral/Silhouette outputs.",
    "FBX export and Unity Prefab identity remain null until C1B-005 and must not be inferred from this source.",
    "Pose/action and four-player lineup outputs remain C1B-004 work.",
    "The H=1 source is normalized and does not approve gameplay height meters, collider, reach, rig or production topology.",
    "UG-C1B user approval and production lock have not been recorded.",
  ].freeze

  VIEWS = %w[Front Side Back ThreeQuarter].freeze
  STYLES = %w[Neutral Silhouette].freeze
  EXPECTED_RENDER_PATHS = STYLES.product(VIEWS).map do |style, view|
    "#{RENDER_ROOT}/CHR_MasterCharacter_C1B_Blockout_r01_#{style}_#{view}.png"
  end.sort.freeze
  EXPECTED_IDENTITY_FIELDS = %w[
    assetId assetVersion toolchainProfileId projectVersionSha256 packageManifestSha256
    packageLockSha256 lowPolyStyleProfileId lowPolyStyleProfileRevision modelInteropProfileId
    modelInteropProfileRevision blenderExportPresetId blenderExportPresetRevision
    blenderExportSettingsSha256 unityImporterPresetId unityImporterPresetRevision
    unityImporterSettingsSha256 sourceSha256 fbxSha256 referenceRenderSha256 unityPrefabRevision
  ].sort.freeze
  EXPECTED_TOP_FIELDS = %w[
    schemaVersion manifestId state completionScope ownerTask sourceOwner recordedAtUtc identity
    characterProportionProfile stages measurementEvidence generationTools sourceBoundary execution limitations
  ].sort.freeze
  EXPECTED_STAGE_FIELDS = %w[blend-source fbx-export reference-render unity-prefab].sort.freeze
  FORBIDDEN_OUTPUT_EXTENSIONS = %w[.fbx .glb .prefab .unity .asset .mat].freeze
  C1B003_ALLOWED_PATHS = [
    MANIFEST_PATH, REPORT_PATH, SOURCE_PATH, PROFILE_PATH,
    GENERATOR_PATH, INSPECTOR_PATH,
    RERENDER_PATH, PIXEL_COMPARATOR_PATH,
    "tools/verify_character_blockout.rb",
    "tools/tests/verify_character_blockout_test.rb",
    "config/repository/BinaryAssetPolicy.md",
    "config/repository/BinaryAssetInventory.yaml",
    "config/licenses/LicensePolicy.yaml",
    "config/licenses/ThirdPartyInventory.yaml",
    "tools/verify_lfs_repository.rb",
    "tools/tests/verify_lfs_repository_test.rb",
    "tools/verify_license_inventory.rb",
    "tools/tests/verify_license_inventory_test.rb",
    "docs/03_IMPLEMENTATION_PLAN.md",
    "artifacts/reports/FOUNDATION_DECISION_RATIONALE.md",
  ].freeze
  C1B003_ALLOWED_PREFIXES = [RENDER_ROOT + "/", "artifacts/evidence/G0/C1B-003/"].freeze

  def initialize(root, verify_blender: false, check_scope: false, blender_path: nil)
    @root = Pathname.new(root).expand_path
    @verify_blender = verify_blender
    @check_scope = check_scope
    @blender_path = blender_path || ENV["BLENDER_EXECUTABLE"] || DEFAULT_BLENDER
    @violations = []
    @violation_keys = Set.new
    @manifest = {}
    @profile = {}
    @report = {}
    @render_outputs = []
    @source_hash_match = false
    @render_hash_matches = 0
    @png_dimensions_matched = 0
    @render_reproduction_matches = 0
    @render_reproduction_exact_matches = 0
    @render_reproduction_max_channel_difference = 0
    @render_reproduction_max_changed_channel_ratio = 0.0
    @blender_verified = false
    @landmarks_verified = 0
    @landmark_mesh_sections_verified = 0
    @section_instances_verified = 0
    @maximum_deviation_h = 0.0
    @scope_paths_checked = 0
  end

  def run
    manifest_document = load_yaml(MANIFEST_PATH, "MANIFEST", MAX_YAML_BYTES)
    profile_document = load_yaml(PROFILE_PATH, "PROFILE", MAX_YAML_BYTES)
    report_document = load_yaml(REPORT_PATH, "REPORT", MAX_YAML_BYTES)
    @manifest = hash(manifest_document && manifest_document["GenerationManifest"])
    @profile = hash(profile_document && profile_document["CharacterProportionProfile"])
    @report = hash(report_document && report_document["C1B003MeasurementReport"])
    validate_document_roots(manifest_document, profile_document, report_document)
    validate_manifest_metadata
    validate_identity
    validate_character_profile_reference
    validate_stages
    validate_measurement_report
    validate_generation_tools
    validate_source_boundary_and_execution
    validate_repository_scope
    validate_blender_source if @verify_blender
    validate_change_scope if @check_scope
    print_report
    @violations.empty? ? 0 : 1
  rescue StandardError => error
    add("VERIFIER_INTERNAL_ERROR", error.class.name)
    print_report
    1
  end

  private

  def validate_document_roots(manifest_document, profile_document, report_document)
    expect(manifest_document.is_a?(Hash) && manifest_document.keys == ["GenerationManifest"],
      "MANIFEST_DOCUMENT_ROOT", MANIFEST_PATH)
    expect(profile_document.is_a?(Hash) && profile_document.keys == ["CharacterProportionProfile"],
      "PROFILE_DOCUMENT_ROOT", PROFILE_PATH)
    expect(report_document.is_a?(Hash) && report_document.keys == ["C1B003MeasurementReport"],
      "REPORT_DOCUMENT_ROOT", REPORT_PATH)
  end

  def validate_manifest_metadata
    expect(@manifest.keys.sort == EXPECTED_TOP_FIELDS, "MANIFEST_FIELD_SET", MANIFEST_PATH)
    expect(@manifest["schemaVersion"] == 1, "MANIFEST_SCHEMA_VERSION", MANIFEST_PATH)
    expect(@manifest["manifestId"] == "GM-CHR-MasterCharacter-C1B-Blockout-r01",
      "MANIFEST_ID", MANIFEST_PATH)
    expect(@manifest["state"] == "START", "MANIFEST_STATE", MANIFEST_PATH)
    expect(@manifest["completionScope"] == "C1B-003_SOURCE_AND_REFERENCE_RENDER_COMPLETE",
      "MANIFEST_COMPLETION_SCOPE", MANIFEST_PATH)
    expect(@manifest["ownerTask"] == "C1B-003", "MANIFEST_OWNER", MANIFEST_PATH)
    expect(@manifest["sourceOwner"] == "kjh4845", "MANIFEST_SOURCE_OWNER", MANIFEST_PATH)
    expect(@manifest["recordedAtUtc"].to_s.match?(/\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\z/),
      "MANIFEST_RECORDED_AT", MANIFEST_PATH)
    walk(@manifest) { |value| add("MANIFEST_UNAPPROVED_LOCK", MANIFEST_PATH) if value == "LOCKED" }
  end

  def validate_identity
    identity = hash(@manifest["identity"])
    expect(identity.keys.sort == EXPECTED_IDENTITY_FIELDS, "IDENTITY_FIELD_SET", MANIFEST_PATH)
    expected = {
      "assetId" => "CHR_MasterCharacter_C1B_Blockout",
      "assetVersion" => "0.1.0-start",
      "toolchainProfileId" => "project-hotfix-alpha-toolchain-r02",
      "projectVersionSha256" => "ee06b66c6e48dc22fe4771812f37f3817b17f9459d44b61202d45e2a44ea3509",
      "packageManifestSha256" => "88a1d01cf3abd14843cb3638a7692552e54f62a6d5de003c458a47a869edd741",
      "packageLockSha256" => "8708528393a5fce3dd9110fc14815ad2dbc21cefd0efec6102b79d86a62404aa",
      "lowPolyStyleProfileId" => "LowPolyStyleProfile-ART-001-r01",
      "lowPolyStyleProfileRevision" => "r01",
      "modelInteropProfileId" => "ModelInteropProfile-ART-001-r01",
      "modelInteropProfileRevision" => "r01",
      "blenderExportPresetId" => "PHX-FBX-MODEL-r01",
      "blenderExportPresetRevision" => "r01",
      "blenderExportSettingsSha256" => "5707996b33f8ac6773e309c60d05b236655bb4b17e4cc3261642f142a0062ce4",
      "unityImporterPresetId" => "PHX-UNITY-MODEL-IMPORT-r01",
      "unityImporterPresetRevision" => "r01",
      "unityImporterSettingsSha256" => "4a00f7ea259ef98d17a932948ca2ebfde7bc7173e632ac5f5b4ddc81fba94cf9",
    }
    expected.each { |field, value| expect(identity[field] == value, "IDENTITY_VALUE", field) }
    expect(identity.key?("fbxSha256") && identity["fbxSha256"].nil?, "IDENTITY_FBX_DEFERRED", MANIFEST_PATH)
    expect(identity.key?("unityPrefabRevision") && identity["unityPrefabRevision"].nil?,
      "IDENTITY_UNITY_DEFERRED", MANIFEST_PATH)
    verify_file_digest("Project hotfix/ProjectSettings/ProjectVersion.txt", identity["projectVersionSha256"], "IDENTITY")
    verify_file_digest("Project hotfix/Packages/manifest.json", identity["packageManifestSha256"], "IDENTITY")
    verify_file_digest("Project hotfix/Packages/packages-lock.json", identity["packageLockSha256"], "IDENTITY")
  end

  def validate_character_profile_reference
    reference = hash(@manifest["characterProportionProfile"])
    expected_fields = %w[path profileId version revision measurementSetSha256 state userApprovalRecorded]
    expect(reference.keys.sort == expected_fields.sort, "CHARACTER_PROFILE_REFERENCE_FIELDS", MANIFEST_PATH)
    expected = {
      "path" => PROFILE_PATH,
      "profileId" => "CharacterProportionProfile-C1B-002-r01",
      "version" => "0.1.0-start",
      "revision" => "r01",
      "measurementSetSha256" => "76c98acfe8cfbf01b51936b29c2f6ba2e78c26222dfd53c033fe84233e562722",
      "state" => "START",
      "userApprovalRecorded" => false,
    }
    expected.each { |field, value| expect(reference[field] == value, "CHARACTER_PROFILE_REFERENCE", field) }
    expect(@profile["profileId"] == reference["profileId"], "CHARACTER_PROFILE_ID_DRIFT", PROFILE_PATH)
    expect(@profile["measurementSetSha256"] == reference["measurementSetSha256"],
      "CHARACTER_PROFILE_DIGEST_DRIFT", PROFILE_PATH)
  end

  def validate_stages
    stages = hash(@manifest["stages"])
    expect(stages.keys.sort == EXPECTED_STAGE_FIELDS, "STAGE_FIELD_SET", MANIFEST_PATH)
    blend = hash(stages["blend-source"])
    expected_blend_fields = %w[
      status path bytes sha256 blenderVersion blenderBuildHash sourceAxis characterForwardAxis
      rootScale lfsState indexPointerVerified remoteObjectRoundTripVerified
    ]
    expect(blend.keys.sort == expected_blend_fields.sort, "BLEND_STAGE_FIELDS", MANIFEST_PATH)
    expect(blend["status"] == "COMPLETE" && blend["path"] == SOURCE_PATH,
      "BLEND_STAGE_STATUS_PATH", MANIFEST_PATH)
    expect(blend["blenderVersion"] == "5.2.0 LTS" && blend["blenderBuildHash"] == "fbe6228777e7",
      "BLEND_STAGE_TOOLCHAIN", MANIFEST_PATH)
    expect(blend["sourceAxis"] == "+Z Up" && blend["characterForwardAxis"] == "-Y" &&
      blend["rootScale"] == [1.0, 1.0, 1.0], "BLEND_STAGE_TRANSFORM", MANIFEST_PATH)
    expect(%w[PENDING_CORE_PUSH VERIFIED_REMOTE_ROUND_TRIP].include?(blend["lfsState"]),
      "BLEND_STAGE_LFS_STATE", MANIFEST_PATH)
    expected_round_trip = blend["lfsState"] == "VERIFIED_REMOTE_ROUND_TRIP"
    expect(blend["indexPointerVerified"] == true,
      "BLEND_STAGE_INDEX_POINTER_FLAG", MANIFEST_PATH)
    expect(blend["remoteObjectRoundTripVerified"] == expected_round_trip,
      "BLEND_STAGE_REMOTE_ROUND_TRIP_FLAG", MANIFEST_PATH)
    validate_binary_file(SOURCE_PATH, blend["bytes"], blend["sha256"], "BLEND_SOURCE")
    identity = hash(@manifest["identity"])
    expect(identity["sourceSha256"] == blend["sha256"], "IDENTITY_SOURCE_SHA", MANIFEST_PATH)
    @source_hash_match = file_sha(SOURCE_PATH) == blend["sha256"]

    fbx = hash(stages["fbx-export"])
    expect(fbx == {"status" => "DEFERRED_C1B-005", "path" => nil, "bytes" => nil,
      "sha256" => nil, "executed" => false}, "FBX_STAGE_DEFERRED", MANIFEST_PATH)
    unity = hash(stages["unity-prefab"])
    expect(unity == {"status" => "DEFERRED_C1B-005", "path" => nil,
      "revision" => nil, "executed" => false}, "UNITY_STAGE_DEFERRED", MANIFEST_PATH)

    render = hash(stages["reference-render"])
    expected_render_fields = %w[
      status canonicalReferencePath canonicalReferenceSha256 orderedBundleSha256
      decodedPixelHashAlgorithm localReproductionMatches reproductionMaximumChannelDifference
      reproductionMaximumChangedChannelRatio renderResolutionPixels projection
      orthoScale boundsPaddingRatio perViewManualFramingChanges outputs
    ]
    expect(render.keys.sort == expected_render_fields.sort, "RENDER_STAGE_FIELDS", MANIFEST_PATH)
    expect(render["status"] == "COMPLETE" && render["projection"] == "Orthographic" &&
      render["renderResolutionPixels"] == [2048, 2048] && number_equal?(render["orthoScale"], 1.2) &&
      number_equal?(render["boundsPaddingRatio"], 0.10) && render["perViewManualFramingChanges"] == 0,
      "RENDER_STAGE_SETTINGS", MANIFEST_PATH)
    expect(render["decodedPixelHashAlgorithm"] == "SHA256(RGB:WxH:+decoded RGB bytes)" &&
      render["localReproductionMatches"] == 8 && render["reproductionMaximumChannelDifference"] == 1 &&
      number_equal?(render["reproductionMaximumChangedChannelRatio"], 0.000001),
      "RENDER_STAGE_REPRODUCTION_METADATA", MANIFEST_PATH)
    outputs = render["outputs"]
    unless outputs.is_a?(Array)
      add("RENDER_OUTPUTS_INVALID", MANIFEST_PATH)
      return
    end
    @render_outputs = outputs
    paths = outputs.map { |entry| entry.is_a?(Hash) ? entry["path"] : nil }.compact
    expect(paths.sort == EXPECTED_RENDER_PATHS, "RENDER_OUTPUT_EXACT_SET", MANIFEST_PATH)
    expect(outputs.map { |entry| [entry["style"], entry["view"]] }.sort == STYLES.product(VIEWS).sort,
      "RENDER_STYLE_VIEW_EXACT_SET", MANIFEST_PATH)
    outputs.each do |entry|
      next unless entry.is_a?(Hash)
      expect(entry.keys.sort == %w[bytes decodedPixelSha256 path sha256 style view],
        "RENDER_OUTPUT_FIELDS", entry["path"])
      expect(entry["decodedPixelSha256"].to_s.match?(/\A[0-9a-f]{64}\z/),
        "RENDER_OUTPUT_PIXEL_SHA", entry["path"])
      validate_binary_file(entry["path"], entry["bytes"], entry["sha256"], "REFERENCE_RENDER")
      @render_hash_matches += 1 if file_sha(entry["path"]) == entry["sha256"]
      dimensions = png_dimensions(entry["path"])
      @png_dimensions_matched += 1 if dimensions == [2048, 2048]
      expect(dimensions == [2048, 2048], "RENDER_PNG_DIMENSIONS", entry["path"])
      expect(entry["bytes"].is_a?(Integer) && entry["bytes"] < 10 * 1024 * 1024,
        "RENDER_SIZE_BOUNDARY", entry["path"])
    end
    canonical = render["canonicalReferencePath"]
    canonical_entry = outputs.find { |entry| entry.is_a?(Hash) && entry["path"] == canonical }
    expect(canonical_entry && canonical_entry["style"] == "Neutral" && canonical_entry["view"] == "Front",
      "RENDER_CANONICAL_SELECTION", MANIFEST_PATH)
    expect(canonical_entry && render["canonicalReferenceSha256"] == canonical_entry["sha256"] &&
      identity["referenceRenderSha256"] == canonical_entry["sha256"],
      "RENDER_CANONICAL_SHA", MANIFEST_PATH)
    bundle_text = outputs.sort_by { |entry| entry["path"] }.map do |entry|
      "#{entry["path"]}=#{entry["sha256"]}"
    end.join("\n") + "\n"
    expect(render["orderedBundleSha256"] == Digest::SHA256.hexdigest(bundle_text),
      "RENDER_BUNDLE_SHA", MANIFEST_PATH)
  end

  def validate_measurement_report
    evidence = hash(@manifest["measurementEvidence"])
    expected_fields = %w[
      reportPath reportSha256 startingToleranceH boundsExpectedH boundsInspectedH
      maximumBoundsDeviationH authoredBoundsCenterBlender maximumOpticalAxisBoundsCenterDeviationH
      landmarksRequired landmarksInspected
      silhouetteEnvelopeSemanticCount result
    ]
    expect(evidence.keys.sort == expected_fields.sort, "MEASUREMENT_EVIDENCE_FIELDS", MANIFEST_PATH)
    expect(evidence["reportPath"] == REPORT_PATH && evidence["result"] == "PASS" &&
      number_equal?(evidence["startingToleranceH"], START_TOLERANCE_H),
      "MEASUREMENT_EVIDENCE_STATUS", MANIFEST_PATH)
    expect(evidence["reportSha256"] == EXPECTED_REPORT_SHA256,
      "MEASUREMENT_REPORT_CANONICAL_SHA", MANIFEST_PATH)
    verify_file_digest(REPORT_PATH, evidence["reportSha256"], "MEASUREMENT_REPORT")
    expect(evidence["boundsExpectedH"] == [1.0, 0.58, 0.265] &&
      evidence["maximumBoundsDeviationH"].to_f <= START_TOLERANCE_H &&
      evidence["authoredBoundsCenterBlender"] == [0.0, -0.0075, 0.5] &&
      evidence["maximumOpticalAxisBoundsCenterDeviationH"].to_f <= START_TOLERANCE_H &&
      evidence["landmarksRequired"] == 17 && evidence["landmarksInspected"] == 17 &&
      evidence["silhouetteEnvelopeSemanticCount"] == 11,
      "MEASUREMENT_EVIDENCE_METRICS", MANIFEST_PATH)
    expected_report_fields = %w[
      schemaVersion reportId state ownerTask assetId assetVersion sourceOwner sourcePath
      profilePath profileId measurementSetSha256 startingToleranceH evaluatedGeometry bounds
      landmarks silhouetteEnvelopes directionInvariants referenceRenders reviewBoundary
      execution recordedAtUtc
    ]
    expect(@report.keys.sort == expected_report_fields.sort, "MEASUREMENT_REPORT_FIELD_SET", REPORT_PATH)
    expect(@report["schemaVersion"] == 1 && @report["reportId"] == "C1B003-CHR-MasterCharacter-Blockout-r01" &&
      @report["state"] == "START" && @report["ownerTask"] == "C1B-003" &&
      @report["assetId"] == "CHR_MasterCharacter_C1B_Blockout" &&
      @report["assetVersion"] == "0.1.0-start" && @report["sourceOwner"] == "kjh4845" &&
      @report["sourcePath"] == SOURCE_PATH && @report["profilePath"] == PROFILE_PATH &&
      @report["profileId"] == "CharacterProportionProfile-C1B-002-r01" &&
      @report["measurementSetSha256"] == "76c98acfe8cfbf01b51936b29c2f6ba2e78c26222dfd53c033fe84233e562722" &&
      number_equal?(@report["startingToleranceH"], START_TOLERANCE_H),
      "MEASUREMENT_REPORT_METADATA", REPORT_PATH)
    walk(@report) { |value| add("MEASUREMENT_REPORT_UNAPPROVED_LOCK", REPORT_PATH) if value == "LOCKED" }
    geometry = hash(@report["evaluatedGeometry"])
    expect(geometry["meshObjects"] == 6 && geometry["allObjects"] == 33 &&
      geometry["meshDatablocks"] == 7 && geometry["materials"] == 3 &&
      geometry["cameras"] == 4 && geometry["lights"] == 4 && geometry["worlds"] == 1 &&
      geometry["vertices"] == 1158 &&
      geometry["polygons"] == 1152 && geometry["armatures"] == 0 &&
      geometry["colliderObjects"] == 0 && geometry["externalImages"] == 0 &&
      geometry["packedReferenceImages"] == 0 && geometry["externalLibraries"] == 0 &&
      geometry["externalFonts"] == 0 && geometry["externalSounds"] == 0 &&
      geometry["externalMovieClips"] == 0 && geometry["actions"] == 0 &&
      geometry["scenes"] == 1 && geometry["embeddedTextBlocks"] == 0 &&
      geometry["visibleSeparateHandFingerFistFootShoeToeObjects"] == 0 &&
      geometry["rootScale"] == [1.0, 1.0, 1.0] && geometry["negativeScaleObjects"] == 0,
      "MEASUREMENT_REPORT_GEOMETRY", REPORT_PATH)
    bounds_report = hash(@report["bounds"])
    expect(bounds_report["expectedH"] == {"height" => 1.0, "frontViewFullWidth" => 0.58,
      "sideViewTotalDepth" => 0.265} && bounds_report["inspectedH"] == {
      "height" => 1.0, "frontViewFullWidth" => 0.579999983,
      "sideViewTotalDepth" => 0.265000001} &&
      bounds_report["maximumAbsoluteDeviationH"].to_f <= START_TOLERANCE_H &&
      bounds_report["groundMinimumH"] == 0.0 && bounds_report["crownMaximumH"] == 1.0 &&
      bounds_report["result"] == "PASS", "MEASUREMENT_REPORT_BOUNDS", REPORT_PATH)
    landmarks_report = hash(@report["landmarks"])
    expect(landmarks_report["required"] == 17 && landmarks_report["inspected"] == 17 &&
      landmarks_report["missing"] == 0 && landmarks_report["exactHeightCrossSectionsRequired"] == 17 &&
      landmarks_report["exactHeightCrossSectionsInspected"] == 17 &&
      landmarks_report["maximumPositionDeviationH"].to_f <= START_TOLERANCE_H &&
      landmarks_report["maximumDeclaredCrossSectionDeviationH"].to_f <= START_TOLERANCE_H &&
      landmarks_report["result"] == "PASS", "MEASUREMENT_REPORT_LANDMARKS", REPORT_PATH)
    envelopes_report = hash(@report["silhouetteEnvelopes"])
    expect(envelopes_report["semanticEnvelopeCount"] == 11 &&
      envelopes_report["evaluatedSectionInstances"] == 16 &&
      envelopes_report["missingSemanticEnvelopes"] == 0 &&
      envelopes_report["maximumMeshSectionDeviationH"].to_f <= START_TOLERANCE_H &&
      envelopes_report["result"] == "PASS", "MEASUREMENT_REPORT_ENVELOPES", REPORT_PATH)
    direction_report = hash(@report["directionInvariants"])
    expect(direction_report["bilateralNeutralSymmetryMismatchCount"] == 0 &&
      direction_report["result"] == "PASS", "MEASUREMENT_REPORT_DIRECTION", REPORT_PATH)
    renders_report = hash(@report["referenceRenders"])
    expect(renders_report["renderResolutionPixels"] == [2048, 2048] &&
      renders_report["projection"] == "Orthographic" && number_equal?(renders_report["orthoScale"], 1.2) &&
      number_equal?(renders_report["boundsPaddingRatio"], 0.10) &&
      renders_report["authoredBoundsCenterBlender"] == [0.0, -0.0075, 0.5] &&
      renders_report["maximumOpticalAxisBoundsCenterDeviationH"].to_f <= START_TOLERANCE_H &&
      renders_report["views"] == VIEWS && renders_report["styles"] == STYLES &&
      renders_report["expectedFiles"] == 8 && renders_report["inspectedFiles"] == 8 &&
      renders_report["reproducedPixelMatches"] == 8 &&
      renders_report["decodedPixelHashAlgorithm"] == "SHA256(RGB:WxH:+decoded RGB bytes)" &&
      renders_report["reproductionMaximumChannelDifference"] == 1 &&
      number_equal?(renders_report["reproductionMaximumChangedChannelRatio"], 0.000001) &&
      renders_report["reproducedMaximumChannelDifference"] <= 1 &&
      renders_report["reproducedMaximumChangedChannelRatio"].to_f <= 0.000001 &&
      renders_report["perViewManualFramingChanges"] == 0 && renders_report["result"] == "PASS",
      "MEASUREMENT_REPORT_RENDERS", REPORT_PATH)
    expect(@report.dig("reviewBoundary", "userVisualApprovalRecorded") == false &&
      @report.dig("reviewBoundary", "lockedValueCount") == 0 &&
      @report.dig("reviewBoundary", "internalStructuralReview") == "PASS" &&
      @report.dig("reviewBoundary", "internalSilhouetteReadabilityReview") == "PASS" &&
      @report.dig("reviewBoundary", "pixelPerfectComparisonRequired") == false &&
      @report.dig("reviewBoundary", "notes").is_a?(Array) &&
      @report.dig("reviewBoundary", "notes").length == 3,
      "MEASUREMENT_REPORT_APPROVAL_BOUNDARY", REPORT_PATH)
    expected_execution = {
      "blenderSourceCreated" => 1, "neutralRendersCreated" => 4,
      "silhouetteRendersCreated" => 4, "fbxExportsCreated" => 0,
      "unityAssetsCreated" => 0, "poseOrLineupOutputsCreated" => 0,
      "playerBuildsExecuted" => 0,
    }
    expect(@report["execution"] == expected_execution, "MEASUREMENT_REPORT_EXECUTION", REPORT_PATH)
  end

  def validate_generation_tools
    tools = hash(@manifest["generationTools"])
    expect(tools.keys.sort == %w[generator inspector rerender pixelComparator].sort,
      "GENERATION_TOOL_FIELDS", MANIFEST_PATH)
    {"generator" => GENERATOR_PATH, "inspector" => INSPECTOR_PATH,
     "rerender" => RERENDER_PATH, "pixelComparator" => PIXEL_COMPARATOR_PATH}.each do |key, path|
      entry = hash(tools[key])
      expect(entry.keys.sort == %w[path sha256] && entry["path"] == path,
        "GENERATION_TOOL_ENTRY", path)
      verify_file_digest(path, entry["sha256"], "GENERATION_TOOL")
    end
  end

  def validate_source_boundary_and_execution
    boundary = hash(@manifest["sourceBoundary"])
    expected_boundary = {
      "directionReferencePath" => "artifacts/review/character/C1_CHARACTER_HYBRID_CORE_v0.13_BELLY_CORRECTED_REVIEW.png",
      "directionReferenceSha256" => "c1def169cefd59f19339a5b5edbac2dfd0c8fe9a05eba9ee0afb1ae598bab616",
      "directionReferenceEmbeddedOrPacked" => false,
      "pixelMeasurementsUsed" => false,
      "externalImagesLinked" => 0,
      "externalLibrariesLinked" => 0,
      "externalFontsOrAudioLinked" => 0,
    }
    expect(boundary == expected_boundary, "SOURCE_BOUNDARY", MANIFEST_PATH)
    verify_file_digest(boundary["directionReferencePath"], boundary["directionReferenceSha256"], "DIRECTION_SOURCE")
    execution = hash(@manifest["execution"])
    expected_execution = {
      "blenderSourceFiles" => 1, "neutralReferenceRenders" => 4,
      "silhouetteReferenceRenders" => 4, "fbxExports" => 0, "unityImports" => 0,
      "unityAssets" => 0, "poseOrLineupOutputs" => 0, "armatures" => 0,
      "colliderProfiles" => 0, "playerBuilds" => 0, "dockerExecutions" => 0,
      "deployExecutions" => 0,
    }
    expect(execution == expected_execution, "MANIFEST_EXECUTION", MANIFEST_PATH)
    expect(@manifest["limitations"] == EXPECTED_LIMITATIONS, "MANIFEST_LIMITATIONS", MANIFEST_PATH)
  end

  def validate_repository_scope
    source_root = @root.join("BlenderSource/Characters/C1B-003")
    files = if source_root.directory?
      Dir.glob(source_root.join("**/*").to_s).select { |path| File.file?(path) }
    else
      []
    end
    relative_files = files.map { |path| Pathname.new(path).relative_path_from(@root).to_s }.sort
    expected = ([MANIFEST_PATH, REPORT_PATH, SOURCE_PATH] + EXPECTED_RENDER_PATHS).sort
    expect(relative_files == expected, "SOURCE_DIRECTORY_EXACT_SET", "BlenderSource/Characters/C1B-003")
    backup_files = relative_files.select { |path| path.match?(/\.blend\d+\z/i) }
    expect(backup_files.empty?, "BLENDER_BACKUP_FILES", "BlenderSource/Characters/C1B-003")
    forbidden = relative_files.select { |path| FORBIDDEN_OUTPUT_EXTENSIONS.include?(File.extname(path).downcase) }
    expect(forbidden.empty?, "DOWNSTREAM_OUTPUT_PRESENT", "BlenderSource/Characters/C1B-003")
  end

  def validate_blender_source
    blender = Pathname.new(@blender_path)
    unless blender.file? && !blender.symlink?
      add("BLENDER_EXECUTABLE_MISSING", @blender_path)
      return
    end
    stdout, stderr, status = Open3.capture3(
      blender.to_s, "--background", @root.join(SOURCE_PATH).to_s,
      "--python", @root.join(INSPECTOR_PATH).to_s
    )
    unless status.success?
      add("BLENDER_INSPECTION_FAILED", SOURCE_PATH)
      return
    end
    line = stdout.lines.find { |entry| entry.start_with?("C1B003_INSPECTION_JSON=") }
    unless line
      add("BLENDER_INSPECTION_OUTPUT_MISSING", SOURCE_PATH)
      return
    end
    payload = JSON.parse(line.delete_prefix("C1B003_INSPECTION_JSON="))
    validate_blender_payload(payload)
    validate_render_reproduction(blender)
    @blender_verified = @violations.none? do |rule, _path|
      rule.start_with?("BLENDER_") || rule.start_with?("RENDER_REPRODUCTION_")
    end
    add("BLENDER_INSPECTION_STDERR", SOURCE_PATH) unless stderr.empty?
  rescue JSON::ParserError
    add("BLENDER_INSPECTION_JSON_INVALID", SOURCE_PATH)
  end

  def validate_render_reproduction(blender)
    Dir.mktmpdir("c1b003-render-reproduction-") do |directory|
      rerender_stdout, _rerender_stderr, rerender_status = Open3.capture3(
        blender.to_s, "--background", @root.join(SOURCE_PATH).to_s,
        "--python", @root.join(RERENDER_PATH).to_s, "--", directory
      )
      unless rerender_status.success? && rerender_stdout.include?("C1B003_RERENDER_RESULT=PASS")
        add("RENDER_REPRODUCTION_RENDER_FAILED", SOURCE_PATH)
        return
      end
      compare_stdout, _compare_stderr, compare_status = Open3.capture3(
        "python3", @root.join(PIXEL_COMPARATOR_PATH).to_s,
        @root.join(RENDER_ROOT).to_s, directory
      )
      unless compare_status.success?
        add("RENDER_REPRODUCTION_COMPARE_FAILED", SOURCE_PATH)
        return
      end
      line = compare_stdout.lines.find { |entry| entry.start_with?("C1B003_RENDER_REPRODUCTION_JSON=") }
      unless line
        add("RENDER_REPRODUCTION_OUTPUT_MISSING", SOURCE_PATH)
        return
      end
      payload = JSON.parse(line.delete_prefix("C1B003_RENDER_REPRODUCTION_JSON="))
      expected_names = EXPECTED_RENDER_PATHS.map { |path| File.basename(path) }.sort
      expect(payload["expectedNames"] == expected_names && payload["actualNames"] == expected_names,
        "RENDER_REPRODUCTION_FILE_SET", SOURCE_PATH)
      expect(payload["tolerance"] == {"maximumChannelDifference" => 1,
        "maximumChangedChannelRatio" => 0.000001}, "RENDER_REPRODUCTION_TOLERANCE", SOURCE_PATH)
      expect(payload["allMatch"] == true, "RENDER_REPRODUCTION_PIXEL_MATCH", SOURCE_PATH)
      outputs = Array(payload["outputs"]).to_h { |entry| [entry["name"], entry] }
      @render_outputs.each do |manifest_output|
        name = File.basename(manifest_output["path"])
        reproduced = hash(outputs[name])
        valid = reproduced["matches"] == true &&
          reproduced["expectedDimensions"] == [2048, 2048] &&
          reproduced["actualDimensions"] == [2048, 2048] &&
          reproduced["expectedPixelSha256"] == manifest_output["decodedPixelSha256"] &&
          reproduced["maximumChannelDifference"].to_i <= 1 &&
          reproduced["changedChannelRatio"].to_f <= 0.000001
        expect(valid, "RENDER_REPRODUCTION_OUTPUT", name)
        @render_reproduction_matches += 1 if valid
        @render_reproduction_exact_matches += 1 if reproduced["exactPixelMatch"] == true
        @render_reproduction_max_channel_difference = [
          @render_reproduction_max_channel_difference, reproduced["maximumChannelDifference"].to_i
        ].max
        @render_reproduction_max_changed_channel_ratio = [
          @render_reproduction_max_changed_channel_ratio, reproduced["changedChannelRatio"].to_f
        ].max
      end
    end
  rescue JSON::ParserError
    add("RENDER_REPRODUCTION_JSON_INVALID", SOURCE_PATH)
  end

  def validate_blender_payload(payload)
    scene = hash(payload["scene"])
    expected_scene = {
      "assetId" => "CHR_MasterCharacter_C1B_Blockout",
      "assetVersion" => "0.1.0-start", "ownerTask" => "C1B-003",
      "sourceOwner" => "kjh4845", "profileId" => "CharacterProportionProfile-C1B-002-r01",
      "profileRevision" => "r01",
      "measurementSetSha256" => "76c98acfe8cfbf01b51936b29c2f6ba2e78c26222dfd53c033fe84233e562722",
      "state" => "START", "candidateStatus" => "BLOCKOUT_CANDIDATE",
      "userVisualApprovalRecorded" => false, "lockedValueCount" => 0,
      "pixelMeasurementUsed" => false, "referenceReplicaAllowed" => false,
      "gameplayHeightMeters" => "DEFERRED_C1B006", "colliderProfile" => "DEFERRED_CHR002",
      "rigProfile" => "DEFERRED_CHR001", "renderResolution" => 2048,
      "orthographicScale" => 1.2, "boundsPaddingRatio" => 0.1,
      "authoredBoundsCenterBlender" => [0.0, -0.0075, 0.5],
    }
    expected_scene.each { |field, value| expect_approx_or_equal(scene[field], value, "BLENDER_SCENE", field) }
    expected_render_names = EXPECTED_RENDER_PATHS.map { |path| File.basename(path) }.to_set
    expect(scene["referenceRenderFiles"].is_a?(Array) && scene["referenceRenderFiles"].to_set == expected_render_names,
      "BLENDER_SCENE_RENDER_SET", SOURCE_PATH)
    expect(payload["unitSettings"] == {"scaleLength" => 1.0, "system" => "METRIC"},
      "BLENDER_UNIT_SETTINGS", SOURCE_PATH)
    expect(payload["renderSettings"] == {
      "engine" => "BLENDER_EEVEE", "resolutionX" => 2048, "resolutionY" => 2048,
      "resolutionPercentage" => 100, "filmTransparent" => false,
      "viewTransform" => "Standard", "look" => "None", "exposure" => 0.0, "gamma" => 1.0,
    }, "BLENDER_RENDER_SETTINGS", SOURCE_PATH)
    expect(payload.dig("root", "location") == [0.0, 0.0, 0.0] &&
      payload.dig("root", "scale") == [1.0, 1.0, 1.0], "BLENDER_ROOT_TRANSFORM", SOURCE_PATH)
    expect(payload["meshObjectCount"] == 6 && payload["meshVertexCount"].to_i > 0 &&
      payload["meshPolygonCount"].to_i > 0, "BLENDER_MESH_STRUCTURE", SOURCE_PATH)
    expect(payload["armatureCount"] == 0 && payload["colliderObjectCount"] == 0 &&
      payload["actionCount"] == 0 && payload["sceneCount"] == 1 && payload["textBlockCount"] == 0 &&
      payload["collectionNames"] == %w[C1B003_Blockout C1B003_Landmarks C1B003_QA],
      "BLENDER_SCOPE_BOUNDARY", SOURCE_PATH)
    expect(payload["externalImages"] == [] && payload["packedImageCount"] == 0 &&
      payload["externalLibraries"] == [] && payload["externalFonts"] == [] &&
      payload["externalSounds"] == [] && payload["externalMovieClips"] == [],
      "BLENDER_EXTERNAL_DATABLOCKS", SOURCE_PATH)
    validate_blender_inventory_and_qa(payload)

    bounds = hash(payload["bounds"])
    expected_bounds = hash(@profile["neutralBoundsH"])
    compare_measurement(bounds["heightH"], expected_bounds["height"], "BLENDER_BOUNDS_HEIGHT")
    compare_measurement(bounds["frontViewFullWidthH"], expected_bounds["fullWidthIncludingArms"],
      "BLENDER_BOUNDS_WIDTH")
    compare_measurement(bounds["sideViewTotalDepthH"], expected_bounds["totalDepth"],
      "BLENDER_BOUNDS_DEPTH")
    [0.0, -0.0075, 0.5].each_with_index do |expected, index|
      compare_measurement(Array(bounds["centerBlender"])[index], expected,
        "BLENDER_BOUNDS_CENTER", SOURCE_PATH)
    end

    actual_landmarks = hash(payload["landmarks"])
    expected_landmarks = @profile["landmarks"]
    expect(expected_landmarks.is_a?(Array) && actual_landmarks.keys.sort == expected_landmarks.map { |e| e["id"] }.sort,
      "BLENDER_LANDMARK_EXACT_SET", SOURCE_PATH)
    Array(expected_landmarks).each do |expected|
      actual = hash(actual_landmarks[expected["id"]])
      expect(actual["semantic"] == expected["semantic"] &&
        actual["crossSectionScope"] == expected["crossSectionScope"],
        "BLENDER_LANDMARK_SEMANTICS", expected["id"])
      %w[x y z].each_with_index do |axis, index|
        compare_measurement(actual.dig("positionH", index), expected.dig("positionH", axis),
          "BLENDER_LANDMARK_POSITION", expected["id"])
      end
      compare_measurement(actual["frontViewFullWidthH"], expected["frontViewFullWidthH"],
        "BLENDER_LANDMARK_WIDTH", expected["id"])
      compare_measurement(actual["sideViewTotalDepthH"], expected["sideViewTotalDepthH"],
        "BLENDER_LANDMARK_DEPTH", expected["id"])
      @landmarks_verified += 1 if actual.any?
    end

    sections = Array(payload["sections"])
    section_by_id = sections.group_by { |entry| entry["id"] }
    landmark_section_aliases = {
      "Chest" => "ChestBody",
      "Pelvis" => "PelvisBody",
      "Crotch" => "CrotchBridge",
    }
    Array(expected_landmarks).each do |expected|
      identifier = landmark_section_aliases.fetch(expected["id"], expected["id"])
      actuals = section_by_id[identifier] || []
      expect(actuals.length == 1, "BLENDER_LANDMARK_MESH_SECTION_INSTANCE", expected["id"])
      next unless actuals.length == 1
      actual = actuals.first
      compare_measurement(actual["heightH"], expected.dig("positionH", "y"),
        "BLENDER_LANDMARK_MESH_SECTION_HEIGHT", expected["id"])
      compare_measurement(actual["frontViewFullWidthH"], expected["frontViewFullWidthH"],
        "BLENDER_LANDMARK_MESH_SECTION_WIDTH", expected["id"])
      compare_measurement(actual["sideViewTotalDepthH"], expected["sideViewTotalDepthH"],
        "BLENDER_LANDMARK_MESH_SECTION_DEPTH", expected["id"])
      expect(actual["vertexCount"].to_i > 0, "BLENDER_LANDMARK_MESH_SECTION_VERTICES", expected["id"])
      @landmark_mesh_sections_verified += 1
    end
    Array(@profile["silhouetteEnvelopes"]).each do |expected|
      ids = expected["scope"] == "CORE" ? [expected["id"]] : ["#{expected["id"]}_L", "#{expected["id"]}_R"]
      ids.each do |identifier|
        actuals = section_by_id[identifier] || []
        expect(actuals.length == 1, "BLENDER_SECTION_EXACT_INSTANCE", identifier)
        next unless actuals.length == 1
        actual = actuals.first
        compare_measurement(actual["heightH"], expected["heightH"], "BLENDER_SECTION_HEIGHT", identifier)
        compare_measurement(actual["frontViewFullWidthH"], expected["fullWidthH"], "BLENDER_SECTION_WIDTH", identifier)
        compare_measurement(actual["sideViewTotalDepthH"], expected["totalDepthH"], "BLENDER_SECTION_DEPTH", identifier)
        expect(actual["vertexCount"].to_i > 0, "BLENDER_SECTION_VERTICES", identifier)
        @section_instances_verified += 1
      end
    end

    expected_cameras = {
      "Front" => [0.0, 1.0, 0.0], "Side" => [-1.0, 0.0, 0.0],
      "Back" => [0.0, -1.0, 0.0],
      "ThreeQuarter" => [-Math.sqrt(0.5), Math.sqrt(0.5), 0.0],
    }
    cameras = hash(payload["cameras"])
    expect(cameras.keys.sort == expected_cameras.keys.sort, "BLENDER_CAMERA_EXACT_SET", SOURCE_PATH)
    expected_cameras.each do |name, direction|
      camera = hash(cameras[name])
      expect(camera["type"] == "ORTHO", "BLENDER_CAMERA_TYPE", name)
      compare_measurement(camera["orthoScale"], 1.2, "BLENDER_CAMERA_SCALE", name)
      compare_measurement(camera["boundsPaddingRatio"], 0.1, "BLENDER_CAMERA_PADDING", name)
      [0.0, -0.0075, 0.5].each_with_index do |expected, index|
        compare_measurement(Array(camera["declaredTargetBlender"])[index], expected,
          "BLENDER_CAMERA_DECLARED_TARGET", name)
      end
      compare_measurement(camera["opticalAxisBoundsCenterDeviationH"], 0.0,
        "BLENDER_CAMERA_BOUNDS_CENTER_AXIS", name)
      Array(camera["declaredLookDirectionBlender"]).each_with_index do |value, index|
        compare_measurement(value, direction[index], "BLENDER_CAMERA_DECLARED_DIRECTION", name)
      end
      Array(camera["actualLookDirectionBlender"]).each_with_index do |value, index|
        compare_measurement(value, direction[index], "BLENDER_CAMERA_ACTUAL_DIRECTION", name)
      end
    end
  end

  def validate_blender_inventory_and_qa(payload)
    expected_objects = []
    VIEWS.each do |view|
      expected_objects << {"name" => "CAM_C1B003_#{view}", "type" => "CAMERA",
        "collections" => ["C1B003_QA"], "hideRender" => false, "parent" => nil}
    end
    %w[Arm_L Arm_R Head Leg_L Leg_R Torso].each do |name|
      expected_objects << {"name" => "CHR_C1B003_#{name}", "type" => "MESH",
        "collections" => ["C1B003_Blockout"], "hideRender" => false,
        "parent" => "CHR_C1B003_Root"}
    end
    expected_objects << {"name" => "CHR_C1B003_Root", "type" => "EMPTY",
      "collections" => ["C1B003_Blockout"], "hideRender" => false, "parent" => nil}
    Array(@profile["landmarks"]).each do |landmark|
      expected_objects << {"name" => "LM_#{landmark["id"]}", "type" => "EMPTY",
        "collections" => ["C1B003_Landmarks"], "hideRender" => true, "parent" => nil}
    end
    %w[QA_Fill_Back QA_Fill_Left QA_Fill_Right QA_Key].each do |name|
      expected_objects << {"name" => name, "type" => "LIGHT", "collections" => ["C1B003_QA"],
        "hideRender" => false, "parent" => nil}
    end
    expected_objects << {"name" => "QA_Ground", "type" => "MESH", "collections" => ["C1B003_QA"],
      "hideRender" => false, "parent" => nil}
    expect(Array(payload["objects"]) == expected_objects.sort_by { |entry| entry["name"] },
      "BLENDER_OBJECT_INVENTORY", SOURCE_PATH)

    expected_datablocks = {
      "meshes" => %w[
        CHR_C1B003_Arm_L_Mesh CHR_C1B003_Arm_R_Mesh CHR_C1B003_Head_Mesh
        CHR_C1B003_Leg_L_Mesh CHR_C1B003_Leg_R_Mesh CHR_C1B003_Torso_Mesh QA_Ground_Mesh
      ].sort,
      "materials" => %w[MAT_C1B003_NeutralWhite MAT_C1B003_Silhouette MAT_QA_Ground].sort,
      "cameras" => VIEWS.map { |view| "CAM_C1B003_#{view}_Data" }.sort,
      "lights" => %w[QA_Fill_Back_Data QA_Fill_Left_Data QA_Fill_Right_Data QA_Key_Data].sort,
      "worlds" => ["C1B003_QA_World"],
      "images" => ["Render Result", "Viewer Node"],
      "actions" => [], "texts" => [], "sounds" => [], "movieClips" => [],
      "fonts" => [], "libraries" => [],
    }
    expect(payload["datablocks"] == expected_datablocks, "BLENDER_DATABLOCK_INVENTORY", SOURCE_PATH)

    expected_materials = {
      "MAT_C1B003_NeutralWhite" => {
        "useNodes" => true, "useFakeUser" => false, "baseColor" => [0.82, 0.80, 0.76, 1.0],
        "metallic" => 0.0, "roughness" => 0.75, "specularIorLevel" => 0.25,
      },
      "MAT_C1B003_Silhouette" => {
        "useNodes" => true, "useFakeUser" => true, "baseColor" => [0.005, 0.005, 0.005, 1.0],
        "metallic" => 0.0, "roughness" => 1.0, "specularIorLevel" => 0.25,
      },
      "MAT_QA_Ground" => {
        "useNodes" => true, "useFakeUser" => false, "baseColor" => [0.12, 0.12, 0.12, 1.0],
        "metallic" => 0.0, "roughness" => 1.0, "specularIorLevel" => 0.25,
      },
    }
    expect_contract_map(payload["materials"], expected_materials, "BLENDER_MATERIAL_CONTRACT")

    expected_lights = {
      "QA_Key" => {"type" => "SUN", "energy" => 3.0, "color" => [1.0, 1.0, 1.0],
        "rotationEulerDegrees" => [50.0, -30.0, 0.0]},
      "QA_Fill_Back" => {"type" => "SUN", "energy" => 0.35, "color" => [1.0, 1.0, 1.0],
        "rotationEulerDegrees" => [130.0, 150.0, 180.0]},
      "QA_Fill_Left" => {"type" => "SUN", "energy" => 0.35, "color" => [1.0, 1.0, 1.0],
        "rotationEulerDegrees" => [80.0, 90.0, 0.0]},
      "QA_Fill_Right" => {"type" => "SUN", "energy" => 0.35, "color" => [1.0, 1.0, 1.0],
        "rotationEulerDegrees" => [80.0, -90.0, 0.0]},
    }
    expect_contract_map(payload["lights"], expected_lights, "BLENDER_LIGHT_CONTRACT")
    expected_world = {"name" => "C1B003_QA_World",
      "backgroundColor" => [0.18, 0.18, 0.18, 1.0], "backgroundStrength" => 1.05}
    expect_contract_map(payload["world"], expected_world, "BLENDER_WORLD_CONTRACT")
  end

  def expect_contract_map(actual, expected, rule, path = SOURCE_PATH)
    unless actual.is_a?(Hash) && actual.keys.sort == expected.keys.sort
      add(rule, path)
      return
    end
    expected.each do |key, expected_value|
      actual_value = actual[key]
      if expected_value.is_a?(Hash)
        expect_contract_map(actual_value, expected_value, rule, "#{path}:#{key}")
      elsif expected_value.is_a?(Array) && expected_value.all? { |value| value.is_a?(Numeric) }
        valid = actual_value.is_a?(Array) && actual_value.length == expected_value.length &&
          actual_value.zip(expected_value).all? { |left, right| contract_number_equal?(left, right) }
        add(rule, "#{path}:#{key}") unless valid
      elsif expected_value.is_a?(Numeric)
        add(rule, "#{path}:#{key}") unless contract_number_equal?(actual_value, expected_value)
      else
        add(rule, "#{path}:#{key}") unless actual_value == expected_value
      end
    end
  end

  def contract_number_equal?(actual, expected)
    finite_number?(actual) && (actual.to_f - expected.to_f).abs <= 0.00001
  end

  def validate_change_scope
    changed, changed_error, changed_status = Open3.capture3(
      "git", "-C", @root.to_s, "diff", "--name-only", "-z", "HEAD", "--"
    )
    untracked, untracked_error, untracked_status = Open3.capture3(
      "git", "-C", @root.to_s, "ls-files", "--others", "--exclude-standard", "-z"
    )
    unless changed_status.success? && untracked_status.success? && changed_error.empty? && untracked_error.empty?
      add("C1B003_GIT_SCOPE_UNAVAILABLE", ".")
      return
    end
    paths = (decode_git_paths(changed) + decode_git_paths(untracked)).uniq.sort
    @scope_paths_checked = paths.length
    paths.each do |path|
      allowed = C1B003_ALLOWED_PATHS.include?(path) ||
        C1B003_ALLOWED_PREFIXES.any? { |prefix| path.start_with?(prefix) }
      add("C1B003_SCOPE_PATH", path) unless allowed
      extension = File.extname(path).downcase
      add("C1B003_DOWNSTREAM_OUTPUT", path) if FORBIDDEN_OUTPUT_EXTENSIONS.include?(extension)
      if extension == ".blend" && path != SOURCE_PATH
        add("C1B003_UNAPPROVED_BLEND", path)
      elsif extension == ".png" && !EXPECTED_RENDER_PATHS.include?(path) &&
          !path.start_with?("artifacts/evidence/G0/C1B-003/")
        add("C1B003_UNAPPROVED_RENDER", path)
      end
    end
  end

  def validate_binary_file(relative, bytes, sha256, kind)
    absolute = safe_regular_file(relative, kind, MAX_BINARY_BYTES)
    return unless absolute
    expect(bytes.is_a?(Integer) && bytes == File.size(absolute), "#{kind}_SIZE", relative)
    expect(sha256.to_s.match?(/\A[0-9a-f]{64}\z/) && Digest::SHA256.file(absolute).hexdigest == sha256,
      "#{kind}_SHA", relative)
  end

  def verify_file_digest(relative, expected, kind)
    absolute = safe_regular_file(relative, kind, MAX_BINARY_BYTES)
    return unless absolute
    expect(expected.to_s.match?(/\A[0-9a-f]{64}\z/) && Digest::SHA256.file(absolute).hexdigest == expected,
      "#{kind}_SHA", relative)
  end

  def file_sha(relative)
    absolute = @root.join(relative)
    absolute.file? ? Digest::SHA256.file(absolute).hexdigest : nil
  end

  def png_dimensions(relative)
    absolute = safe_regular_file(relative, "PNG", MAX_BINARY_BYTES)
    return nil unless absolute
    header = File.binread(absolute, 24)
    return nil unless header.byteslice(0, 8) == "\x89PNG\r\n\x1A\n".b && header.byteslice(12, 4) == "IHDR"
    header.byteslice(16, 8).unpack("NN")
  rescue EOFError
    nil
  end

  def compare_measurement(actual, expected, rule, path = SOURCE_PATH)
    unless finite_number?(actual) && finite_number?(expected)
      add(rule, path)
      return
    end
    deviation = (actual.to_f - expected.to_f).abs
    @maximum_deviation_h = [@maximum_deviation_h, deviation].max
    add(rule, path) if deviation > START_TOLERANCE_H + EPSILON
  end

  def expect_approx_or_equal(actual, expected, rule, path)
    if expected.is_a?(Numeric)
      compare_measurement(actual, expected, rule, path)
    else
      expect(actual == expected, rule, path)
    end
  end

  def load_yaml(relative, kind, max_bytes)
    text = load_text(relative, kind, max_bytes)
    return nil unless text
    stream = Psych.parse_stream(text)
    detect_duplicate_yaml_keys(stream, kind, relative)
    document = YAML.safe_load(text, permitted_classes: [], permitted_symbols: [], aliases: false)
    unless document.is_a?(Hash)
      add("#{kind}_YAML_INVALID", relative)
      return nil
    end
    document
  rescue Psych::Exception
    add("#{kind}_YAML_INVALID", relative)
    nil
  end

  def load_text(relative, kind, max_bytes)
    absolute = safe_regular_file(relative, kind, max_bytes)
    return nil unless absolute
    bytes = absolute.binread
    text = bytes.dup.force_encoding(Encoding::UTF_8)
    unless text.valid_encoding?
      add("#{kind}_INVALID_UTF8", relative)
      return nil
    end
    text
  end

  def safe_regular_file(relative, kind, max_bytes)
    unless repository_relative_path?(relative)
      add("#{kind}_PATH_INVALID", relative.to_s)
      return nil
    end
    cursor = @root
    relative.split("/").each do |component|
      cursor = cursor.join(component)
      begin
        stat = File.lstat(cursor)
      rescue Errno::ENOENT, Errno::ENOTDIR
        add("#{kind}_MISSING", relative)
        return nil
      end
      if stat.symlink?
        add("#{kind}_SYMLINK", relative)
        return nil
      end
    end
    stat = File.lstat(cursor)
    unless stat.file?
      add("#{kind}_NOT_FILE", relative)
      return nil
    end
    if max_bytes && stat.size > max_bytes
      add("#{kind}_TOO_LARGE", relative)
      return nil
    end
    cursor
  end

  def detect_duplicate_yaml_keys(node, kind, relative)
    if node.is_a?(Psych::Nodes::Mapping)
      seen = Set.new
      node.children.each_slice(2) do |key_node, value_node|
        unless key_node.is_a?(Psych::Nodes::Scalar)
          add("#{kind}_YAML_COMPLEX_KEY", relative)
          next
        end
        add("#{kind}_YAML_DUPLICATE_KEY", relative) if seen.include?(key_node.value)
        seen << key_node.value
        detect_duplicate_yaml_keys(value_node, kind, relative)
      end
    elsif node.respond_to?(:children) && node.children.is_a?(Array)
      node.children.each { |child| detect_duplicate_yaml_keys(child, kind, relative) }
    end
  end

  def repository_relative_path?(relative)
    return false unless relative.is_a?(String) && !relative.empty?
    return false if relative.start_with?("/", "\\") || relative.include?("\0")
    return false if relative.match?(/\A[A-Za-z]:[\\\/]/)
    path = Pathname.new(relative)
    path.cleanpath.to_s == relative && !path.each_filename.include?("..")
  end

  def decode_git_paths(bytes)
    bytes.b.split("\0").reject(&:empty?).map do |raw|
      path = raw.dup.force_encoding(Encoding::UTF_8)
      normalized = path.valid_encoding? ? path.tr("\\", "/") : nil
      unless normalized && repository_relative_path?(normalized)
        add("C1B003_GIT_PATH_INVALID", "<invalid>")
        next
      end
      normalized
    end.compact
  end

  def hash(value)
    value.is_a?(Hash) ? value : {}
  end

  def finite_number?(value)
    value.is_a?(Numeric) && value.finite?
  end

  def number_equal?(actual, expected)
    finite_number?(actual) && (actual.to_f - expected.to_f).abs <= EPSILON
  end

  def nonempty_string?(value)
    value.is_a?(String) && !value.strip.empty?
  end

  def walk(value, &block)
    yield value
    case value
    when Hash
      value.each_value { |entry| walk(entry, &block) }
    when Array
      value.each { |entry| walk(entry, &block) }
    end
  end

  def expect(condition, rule, path)
    add(rule, path) unless condition
  end

  def add(rule, path)
    key = [rule, path.to_s]
    return unless @violation_keys.add?(key)
    @violations << key
  end

  def print_report
    blend = hash(hash(@manifest["stages"])["blend-source"])
    puts "CHARACTER_BLOCKOUT_AUDIT=C1B-003"
    puts "MANIFEST_LOADED=#{!@manifest.empty?}"
    puts "SOURCE_HASH_MATCH=#{@source_hash_match}"
    puts "REFERENCE_RENDER_COUNT=#{@render_outputs.length}"
    puts "REFERENCE_RENDER_HASH_MATCHES=#{@render_hash_matches}"
    puts "REFERENCE_RENDER_PNG_2048_MATCHES=#{@png_dimensions_matched}"
    puts "LFS_STATE=#{blend["lfsState"]}"
    puts "BLENDER_VERIFICATION_REQUESTED=#{@verify_blender}"
    puts "BLENDER_VERIFIED=#{@blender_verified}" if @verify_blender
    puts "LANDMARKS_VERIFIED=#{@landmarks_verified}" if @verify_blender
    puts "LANDMARK_MESH_CROSS_SECTIONS_VERIFIED=#{@landmark_mesh_sections_verified}" if @verify_blender
    puts "SECTION_INSTANCES_VERIFIED=#{@section_instances_verified}" if @verify_blender
    puts "RENDER_REPRODUCTION_PIXEL_MATCHES=#{@render_reproduction_matches}" if @verify_blender
    puts "RENDER_REPRODUCTION_EXACT_MATCHES=#{@render_reproduction_exact_matches}" if @verify_blender
    puts "RENDER_REPRODUCTION_MAX_CHANNEL_DIFFERENCE=#{@render_reproduction_max_channel_difference}" if @verify_blender
    puts format("RENDER_REPRODUCTION_MAX_CHANGED_CHANNEL_RATIO=%.12f",
      @render_reproduction_max_changed_channel_ratio) if @verify_blender
    puts format("MAXIMUM_MEASUREMENT_DEVIATION_H=%.9f", @maximum_deviation_h) if @verify_blender
    puts "C1B003_SCOPE_CHECKED=#{@check_scope}"
    puts "C1B003_SCOPE_PATHS=#{@scope_paths_checked}"
    puts "TOTAL_VIOLATIONS=#{@violations.length}"
    @violations.sort.each { |rule, path| puts "VIOLATION rule=#{rule} path=#{path}" }
    puts "FINAL_RESULT=#{@violations.empty? ? "PASS" : "FAIL"}"
  end
end

options = {verify_blender: false, check_scope: false}
parser = OptionParser.new do |arguments|
  arguments.banner = "usage: ruby tools/verify_character_blockout.rb [--root PATH] [--verify-blender] [--check-c1b003-scope]"
  arguments.on("--root PATH", "Repository root") { |path| options[:root] = path }
  arguments.on("--verify-blender", "Open the canonical source with Blender and compare geometry") do
    options[:verify_blender] = true
  end
  arguments.on("--check-c1b003-scope", "Reject out-of-scope C1B-003 changes") do
    options[:check_scope] = true
  end
  arguments.on("--blender PATH", "Blender executable") { |path| options[:blender_path] = path }
end

begin
  parser.parse!
  unless ARGV.empty?
    warn "CHARACTER_BLOCKOUT_AUDIT=ERROR reason=USAGE"
    exit 2
  end
  root = Pathname.new(options.fetch(:root, Pathname.new(__dir__).join("..").expand_path.to_s)).expand_path
  unless root.directory?
    warn "CHARACTER_BLOCKOUT_AUDIT=ERROR reason=INVALID_ROOT"
    exit 2
  end
  exit CharacterBlockoutVerifier.new(
    root,
    verify_blender: options[:verify_blender],
    check_scope: options[:check_scope],
    blender_path: options[:blender_path],
  ).run
rescue Errno::ENOENT, Errno::EACCES => error
  warn "CHARACTER_BLOCKOUT_AUDIT=ERROR reason=#{error.class}"
  exit 2
end
