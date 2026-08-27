#!/usr/bin/env ruby

require "digest"
require "json"
require "open3"
require "optparse"
require "pathname"
require "set"
require "yaml"

class ArtProfileValidator
  MAX_PROFILE_BYTES = 512 * 1024
  EXPECTED_PROFILES = {
    "LowPolyStyleProfile.yaml" => ["LowPolyStyleProfile", "LowPolyStyleProfile-ART-001-r01"],
    "ModelInteropProfile.yaml" => ["ModelInteropProfile", "ModelInteropProfile-ART-001-r01"],
    "AlphaVisualQAProfile.yaml" => ["AlphaVisualQAProfile", "AlphaVisualQAProfile-ART-001-r01"],
  }.freeze
  MATERIAL_FAMILIES = %w[
    MAT_Character_Paintable
    MAT_Environment_Matte
    MAT_Hazard_Active
    MAT_Interactable
    MAT_Glass_Restricted
    MAT_Decal
  ].freeze
  PALETTE_ROLES = %w[
    WorldNeutralBase
    WorldNeutralLift
    Warning
    HazardActive
    Interactable
    SafeRecovery
    PlayerIdentityReserved
    UiGraphite
    UiOffWhite
    ActionAmber
    ReadyTeal
    ErrorCoral
  ].freeze
  ART001_ALLOWED_CHANGE_PATHS = [
    "config/art/LowPolyStyleProfile.yaml",
    "config/art/ModelInteropProfile.yaml",
    "config/art/AlphaVisualQAProfile.yaml",
    "tools/verify_art_profiles.rb",
    "tools/tests/verify_art_profiles_test.rb",
    "docs/03_IMPLEMENTATION_PLAN.md",
  ].freeze
  ART001_ALLOWED_CHANGE_PREFIXES = ["artifacts/evidence/G0/ART-001/"].freeze
  ART001_ASSET_EXTENSIONS = %w[.blend .fbx .prefab .mat .unity .png .jpg .jpeg .exr .hdr].freeze
  LOW_POLY_SOURCE_CONTRACT_SHA256 = "24906ddd41b8822d86c2a713027835c3ebe4ecf50656728a09b63a25bf2ef23a"
  QA_CHECKLIST_CONTENT_SHA256 = "4aa7143c8acb849ce1470079068f106ba4ed1b907c1b1018956525a8ed1c57c1"
  EXPECTED_SOURCE_DOCUMENTS = {
    "LowPolyStyleProfile" => {
      "docs/ART_DIRECTION.md" => "1.8.0",
      "docs/CHARACTER_TECHNICAL_SPEC.md" => "0.11.0",
      "docs/WEAPON_DESIGN.md" => "0.7.0",
      "docs/MAP_DESIGN_GUIDE.md" => "1.8.0",
      "docs/UI_UX_FLOW.md" => "1.8.0",
    },
    "ModelInteropProfile" => {
      "docs/ART_DIRECTION.md" => "1.8.0",
      "docs/CHARACTER_TECHNICAL_SPEC.md" => "0.11.0",
      "docs/WEAPON_DESIGN.md" => "0.7.0",
      "docs/MAP_DESIGN_GUIDE.md" => "1.8.0",
    },
    "AlphaVisualQAProfile" => {
      "docs/ART_DIRECTION.md" => "1.8.0",
      "docs/CHARACTER_TECHNICAL_SPEC.md" => "0.11.0",
      "docs/WEAPON_DESIGN.md" => "0.7.0",
      "docs/MAP_DESIGN_GUIDE.md" => "1.8.0",
      "docs/UI_UX_FLOW.md" => "1.8.0",
    },
  }.freeze
  EXPECTED_DEFERRED_FIELDS = {
    "LowPolyStyleProfile" => %w[
      finalPaletteValues
      characterFinalBevelAndNormalTreatment
      weaponFinalBevelAndNormalTreatment
      mapFinalBevelAndNormalTreatment
      alphaPlaceholderCosmeticExactShapeAndSize
      weaponExactProportionsMaterialsPaletteAndBevel
      finalPolygonTextureMaterialLodGpuBudgets
    ],
    "ModelInteropProfile" => %w[
      characterHeightMetersAndCuConversion
      animationClipExportPreset
      productionMeshOptimizationAndReadWriteOverrides
    ],
    "AlphaVisualQAProfile" => %w[
      finalPaletteShaderToneAndProductLighting
      finalGameplayCameraValues
      finalPolygonTextureMaterialLodGpuBudgets
    ],
  }.freeze

  def initialize(root, check_art001_scope: false)
    @root = root.realpath
    @check_art001_scope = check_art001_scope
    @violations = []
    @violation_keys = Set.new
    @profiles = {}
    @scope_paths_checked = 0
  end

  def run
    load_profiles
    load_toolchain
    load_plan_metadata
    validate_art001_scope if @check_art001_scope
    validate_common_metadata
    validate_toolchain
    validate_low_poly
    validate_interop
    validate_visual_qa
    validate_cross_references
    print_report
    @violations.empty? ? 0 : 1
  end

  private

  def load_profiles
    art_directory = resolve("config/art")
    if art_directory&.directory? && !art_directory.symlink?
      actual_yaml = art_directory.children
        .select { |path| path.basename.to_s.end_with?(".yaml") }
        .map { |path| path.basename.to_s }
        .sort
      expect(actual_yaml == EXPECTED_PROFILES.keys.sort, "ART001_PROFILE_FILE_SET", "config/art")
    else
      add("ART001_PROFILE_DIRECTORY", "config/art")
    end

    EXPECTED_PROFILES.each do |filename, (root_key, _profile_id)|
      relative = "config/art/#{filename}"
      document = load_yaml(relative, "PROFILE")
      next unless document
      expect(document.keys == [root_key], "PROFILE_DOCUMENT_ROOT_SET", relative)

      profile = document[root_key]
      unless profile.is_a?(Hash)
        add("PROFILE_ROOT_INVALID", relative)
        next
      end
      @profiles[root_key] = profile
    end
  end

  def validate_art001_scope
    changed, _stderr, changed_status = Open3.capture3(
      "git", "-C", @root.to_s, "diff", "--name-only", "-z", "HEAD", "--"
    )
    untracked, _stderr, untracked_status = Open3.capture3(
      "git", "-C", @root.to_s, "ls-files", "--others", "--exclude-standard", "-z"
    )
    unless changed_status.success? && untracked_status.success?
      add("ART001_GIT_SCOPE_UNAVAILABLE", ".")
      return
    end

    paths = (changed.b.split("\0") + untracked.b.split("\0")).reject(&:empty?).map do |path|
      path.force_encoding(Encoding::UTF_8)
      path.valid_encoding? ? path.tr("\\", "/") : "<invalid-git-path>"
    end.uniq.sort
    @scope_paths_checked = paths.length
    paths.each do |path|
      add("ART001_ASSET_OUTPUT_PRESENT", path) if ART001_ASSET_EXTENSIONS.include?(File.extname(path).downcase)
      allowed = ART001_ALLOWED_CHANGE_PATHS.include?(path) ||
        ART001_ALLOWED_CHANGE_PREFIXES.any? { |prefix| path.start_with?(prefix) }
      add("ART001_SCOPE_PATH", path) unless allowed
    end
  end

  def load_toolchain
    document = load_yaml("config/toolchain/ToolchainProfile.yaml", "TOOLCHAIN")
    @toolchain = document && document["ToolchainProfile"]
    add("TOOLCHAIN_ROOT_INVALID", "config/toolchain/ToolchainProfile.yaml") unless @toolchain.is_a?(Hash)
  end

  def load_plan_metadata
    relative = "docs/03_IMPLEMENTATION_PLAN.md"
    absolute = resolve(relative)
    unless absolute&.file? && !absolute.symlink?
      add("PLAN_MISSING", relative)
      @task_ids = Set.new
      @gate_ids = Set.new
      return
    end
    text = absolute.read
    @task_ids = text.scan(/^\| ([A-Z][A-Z0-9]*-\d{3}) \|/).flatten.to_set
    @gate_ids = text.scan(/\bUG-[A-Z0-9-]+\b/).to_set
    add("PLAN_TASK_SET_EMPTY", relative) if @task_ids.empty?
    add("PLAN_GATE_SET_EMPTY", relative) if @gate_ids.empty?
  end

  def load_yaml(relative, kind)
    absolute = resolve(relative)
    unless absolute && absolute.exist?
      add("#{kind}_MISSING", relative)
      return nil
    end
    if absolute.symlink?
      add("#{kind}_SYMLINK", relative)
      return nil
    end
    unless absolute.file?
      add("#{kind}_NOT_FILE", relative)
      return nil
    end
    if absolute.size > MAX_PROFILE_BYTES
      add("#{kind}_TOO_LARGE", relative)
      return nil
    end

    bytes = absolute.binread
    text = bytes.dup.force_encoding(Encoding::UTF_8)
    unless text.valid_encoding?
      add("#{kind}_INVALID_UTF8", relative)
      return nil
    end
    document = YAML.safe_load(text, [], [], false)
    unless document.is_a?(Hash)
      add("#{kind}_YAML_INVALID", relative)
      return nil
    end
    document
  rescue Psych::Exception
    add("#{kind}_YAML_INVALID", relative)
    nil
  end

  def validate_common_metadata
    EXPECTED_PROFILES.each do |filename, (root_key, expected_id)|
      profile = @profiles[root_key]
      next unless profile
      relative = "config/art/#{filename}"
      expect(profile["schemaVersion"] == 1, "COMMON_SCHEMA_VERSION", relative)
      expect(profile["profileId"] == expected_id, "COMMON_PROFILE_ID", relative)
      expect(profile["version"] == "0.1.0-start", "COMMON_VERSION", relative)
      expect(profile["revision"] == "r01", "COMMON_REVISION", relative)
      expect(profile["state"] == "START", "COMMON_STATE", relative)
      expect(profile["ownerTask"] == "ART-001", "COMMON_OWNER", relative)
      expect(profile["visualApprovalClaimed"] == false, "COMMON_VISUAL_APPROVAL", relative)
      expect(profile["lockedValueCount"] == 0, "COMMON_LOCK_COUNT", relative)
      expect(profile.dig("toolchainReference", "profileId") == "project-hotfix-alpha-toolchain-r02", "COMMON_TOOLCHAIN_REFERENCE", relative)
      expect(profile.dig("toolchainReference", "path") == "config/toolchain/ToolchainProfile.yaml", "COMMON_TOOLCHAIN_PATH", relative)
      expect(nonempty_array?(profile["sourceDocuments"]), "COMMON_SOURCE_DOCUMENTS", relative)
      actual_sources = Array(profile["sourceDocuments"]).select { |source| source.is_a?(Hash) }.to_h do |source|
        [source["path"], source["version"]]
      end
      expect(actual_sources == EXPECTED_SOURCE_DOCUMENTS.fetch(root_key), "COMMON_SOURCE_DOCUMENT_SET", relative)
      Array(profile["sourceDocuments"]).each do |source|
        valid = source.is_a?(Hash) && nonempty_string?(source["path"]) && nonempty_string?(source["version"])
        source_path = valid && resolve(source["path"])
        valid &&= source_path&.file? && !source_path.symlink?
        valid &&= source_path.read.include?(source["version"]) if valid
        expect(valid, "COMMON_SOURCE_DOCUMENT_ENTRY", relative)
      end
      expect(!contains_locked_state?(profile), "COMMON_ILLEGAL_LOCKED_VALUE", relative)
      validate_deferred_nodes(profile, relative)
      validate_deferred_decision_records(root_key, profile["deferredDecisions"], relative)
      validate_execution(root_key, profile["execution"], relative)
    end

    ids = @profiles.values.map { |profile| profile["profileId"] }
    expect(ids.length == EXPECTED_PROFILES.length && ids.uniq.length == ids.length, "COMMON_PROFILE_SET", "config/art")
  end

  def validate_toolchain
    return unless @toolchain
    relative = "config/toolchain/ToolchainProfile.yaml"
    expect(@toolchain["profileId"] == "project-hotfix-alpha-toolchain-r02", "TOOLCHAIN_PROFILE_ID", relative)
    expect(@toolchain.dig("unity", "editorVersion") == "6000.3.9f1", "TOOLCHAIN_UNITY_VERSION", relative)
    expect(@toolchain.dig("unity", "editorRevision") == "7a9955a4f2fa", "TOOLCHAIN_UNITY_REVISION", relative)
    expect(@toolchain.dig("blender", "version") == "5.2.0 LTS", "TOOLCHAIN_BLENDER_VERSION", relative)
    expect(@toolchain.dig("blender", "buildDate") == "2026-07-14", "TOOLCHAIN_BLENDER_BUILD_DATE", relative)
    expect(@toolchain.dig("packageBaseline", "universalRenderPipeline") == "17.3.0", "TOOLCHAIN_URP_VERSION", relative)
    verify_toolchain_hash("unity", "projectVersionPath", "projectVersionSha256", "TOOLCHAIN_PROJECT_VERSION_HASH")
    verify_toolchain_hash("packageBaseline", "manifestPath", "manifestSha256", "TOOLCHAIN_MANIFEST_HASH")
    verify_toolchain_hash("packageBaseline", "lockPath", "lockSha256", "TOOLCHAIN_PACKAGE_LOCK_HASH")
  end

  def verify_toolchain_hash(section, path_key, hash_key, rule)
    path = @toolchain.dig(section, path_key)
    expected_hash = @toolchain.dig(section, hash_key)
    absolute = nonempty_string?(path) && resolve(path)
    valid = absolute&.file? && !absolute.symlink? && Digest::SHA256.file(absolute).hexdigest == expected_hash
    expect(valid, rule, "config/toolchain/ToolchainProfile.yaml")
  end

  def validate_low_poly
    profile = @profiles["LowPolyStyleProfile"]
    return unless profile
    relative = "config/art/LowPolyStyleProfile.yaml"
    expected_principles = %w[
      SilhouetteFirst MotionFirst OneSharedWorld ReadableDanger
      ConstrainedExpression ReusableProduction ReferenceNotReplica
    ]
    expect(set_equal?(profile["principles"], expected_principles), "LOW_POLY_PRINCIPLES", relative)
    expect(profile.dig("shapeLanguage", "character", "state") == "DECIDED", "LOW_POLY_CHARACTER_SHAPE", relative)
    expect(profile.dig("shapeLanguage", "environment", "state") == "DECIDED", "LOW_POLY_ENVIRONMENT_SHAPE", relative)
    expect(profile.dig("shapeLanguage", "weapons", "state") == "DECIDED", "LOW_POLY_WEAPON_SHAPE", relative)
    expect(Array(profile.dig("shapeLanguage", "character", "rules")).length >= 4, "LOW_POLY_CHARACTER_RULES", relative)
    expect(Array(profile.dig("shapeLanguage", "environment", "rules")).length >= 5, "LOW_POLY_ENVIRONMENT_RULES", relative)
    expect(Array(profile.dig("shapeLanguage", "weapons", "rules")).length >= 5, "LOW_POLY_WEAPON_RULES", relative)

    bevels = profile["bevelClasses"]
    expect(bevels.is_a?(Hash), "LOW_POLY_BEVEL_SECTION", relative)
    if bevels.is_a?(Hash)
      expect(%w[B0_INTENTIONAL_NONE B1_RIGID_READABILITY B2_SOFT_HERO].all? { |key| bevels[key].is_a?(Hash) }, "LOW_POLY_BEVEL_CLASSES", relative)
      b0 = bevels["B0_INTENTIONAL_NONE"]
      valid_b0 = b0.is_a?(Hash) && b0["state"] == "DECIDED" && nonempty_string?(b0["purpose"]) &&
        b0["widthMeters"] == 0.0 && b0["segmentCount"] == 0
      expect(valid_b0, "LOW_POLY_BEVEL_B0", relative)
      %w[B1_RIGID_READABILITY B2_SOFT_HERO].each do |key|
        width = bevels.dig(key, "widthMeters")
        expect(width.is_a?(Hash) && width["state"] == "DEFERRED" && width["value"].nil?, "LOW_POLY_BEVEL_DEFERRED_WIDTH", relative)
        expect(nonempty_string?(bevels.dig(key, "purpose")), "LOW_POLY_BEVEL_PURPOSE", relative)
        expect(nonempty_string?(width && width["assignmentRule"]), "LOW_POLY_BEVEL_ASSIGNMENT", relative)
        segments = bevels.dig(key, "segmentCount")
        expected_candidates = key == "B1_RIGID_READABILITY" ? [1, 2] : [2, 3]
        valid_segments = segments.is_a?(Hash) && segments["state"] == "START" &&
          segments["comparisonCandidates"] == expected_candidates
        expect(valid_segments, "LOW_POLY_BEVEL_SEGMENTS", relative)
      end
    end

    normals = profile["normalClasses"]
    expect(normals.is_a?(Hash) && %w[N0_FLAT N1_HARD_EDGE N2_AUTHORED_SMOOTH].all? { |key| normals[key].is_a?(Hash) }, "LOW_POLY_NORMAL_CLASSES", relative)
    if normals.is_a?(Hash)
      expected_states = { "N0_FLAT" => "DECIDED", "N1_HARD_EDGE" => "DECIDED", "N2_AUTHORED_SMOOTH" => "START" }
      valid_normals = expected_states.all? do |key, state|
        normals[key].is_a?(Hash) && normals[key]["state"] == state && nonempty_string?(normals[key]["use"])
      end
      valid_normals &&= Array(normals["rules"]).length >= 2
      expect(valid_normals, "LOW_POLY_NORMAL_CLASS_CONTRACT", relative)
    end
    expect(set_equal?(profile["materialFamilies"], MATERIAL_FAMILIES), "LOW_POLY_MATERIAL_FAMILIES", relative)
    material_rules = profile["materialRules"]
    valid_material_rules = material_rules.is_a?(Hash) && material_rules["state"] == "DECIDED" &&
      material_rules["photoTexturePriority"] == false && material_rules["glassRestrictedToAuthoredExceptions"] == true &&
      set_equal?(material_rules["preferredSurfaceInputs"], %w[solid-color gradient mask decal])
    expect(valid_material_rules, "LOW_POLY_MATERIAL_RULES", relative)

    roles = profile.dig("palette", "roles")
    expect(roles.is_a?(Hash) && roles.keys.to_set == PALETTE_ROLES.to_set, "LOW_POLY_PALETTE_ROLES", relative)
    if roles.is_a?(Hash)
      roles.each_value do |role|
        valid = role.is_a?(Hash) && role["state"] == "DEFERRED" && role["value"].nil? &&
          nonempty_array?(role["ownerTasks"]) && nonempty_array?(role["unlockGates"])
        expect(valid, "LOW_POLY_PALETTE_ROLE_CONTRACT", relative)
      end
    end
    expect(profile.dig("palette", "rules", "mapPrimaryAndSecondaryAccentMaximum") == 2, "LOW_POLY_ACCENT_LIMIT", relative)
    expect(profile.dig("palette", "rules", "playerIdentityOnBroadBackgroundAllowed") == false, "LOW_POLY_PLAYER_COLOR_BACKGROUND", relative)
    expect(profile.dig("palette", "rules", "colorOnlyStateCueAllowed") == false, "LOW_POLY_COLOR_ONLY_CUE", relative)
    expect(set_equal?(profile.dig("palette", "rules", "requiredCompanionCues"), ["shape", "value", "motion-or-audio"]), "LOW_POLY_COMPANION_CUES", relative)
    expect(Array(profile["forbiddenDetails"]).length >= 6, "LOW_POLY_FORBIDDEN_DETAILS", relative)
    expect(profile.dig("materialRules", "finalShaderMapping", "state") == "DEFERRED" && profile.dig("materialRules", "finalShaderMapping", "value").nil?, "LOW_POLY_FINAL_SHADER_DEFERRED", relative)
    low_source_contract = {
      "shapeLanguage" => profile["shapeLanguage"],
      "normalClasses" => profile["normalClasses"],
      "paletteRules" => profile.dig("palette", "rules"),
      "alphaStartingRanges" => profile["alphaStartingRanges"],
      "forbiddenDetails" => profile["forbiddenDetails"],
    }
    expect(canonical_digest(low_source_contract) == LOW_POLY_SOURCE_CONTRACT_SHA256, "LOW_POLY_SOURCE_CONTRACT_DIGEST", relative)
  end

  def validate_interop
    profile = @profiles["ModelInteropProfile"]
    return unless profile
    relative = "config/art/ModelInteropProfile.yaml"
    reference = profile["toolchainReference"]
    if @toolchain && reference.is_a?(Hash)
      expected = {
        "unityEditorVersion" => @toolchain.dig("unity", "editorVersion"),
        "unityEditorRevision" => @toolchain.dig("unity", "editorRevision"),
        "blenderVersion" => @toolchain.dig("blender", "version"),
        "blenderBuildDate" => @toolchain.dig("blender", "buildDate"),
        "urpVersion" => @toolchain.dig("packageBaseline", "universalRenderPipeline"),
        "projectVersionSha256" => @toolchain.dig("unity", "projectVersionSha256"),
        "packageManifestSha256" => @toolchain.dig("packageBaseline", "manifestSha256"),
        "packageLockSha256" => @toolchain.dig("packageBaseline", "lockSha256"),
      }
      expect(expected.all? { |key, value| reference[key] == value }, "INTEROP_TOOLCHAIN_DRIFT", relative)
    else
      add("INTEROP_TOOLCHAIN_DRIFT", relative)
    end

    contract = profile["coordinateContract"]
    expect(contract.is_a?(Hash), "INTEROP_COORDINATE_SECTION", relative)
    expect(contract == required_coordinate_contract, "INTEROP_COORDINATE_CONTRACT", relative)
    expect(contract.is_a?(Hash) && contract.dig("blender", "unitScale") == 1.0 && contract.dig("blender", "upAxis") == "+Z" && contract.dig("blender", "characterForwardAxis") == "-Y", "INTEROP_BLENDER_UNIT_AXIS", relative)
    expect(contract.is_a?(Hash) && contract.dig("fbx", "forwardAxis") == "-Z" && contract.dig("fbx", "upAxis") == "+Y" && contract.dig("fbx", "globalScale") == 1.0, "INTEROP_FBX_UNIT_AXIS", relative)
    expect(contract.is_a?(Hash) && contract.dig("unity", "unityUnitsPerMeter") == 1.0 && contract.dig("unity", "upAxis") == "+Y" && contract.dig("unity", "forwardAxis") == "+Z" && contract.dig("unity", "rightAxis") == "+X", "INTEROP_UNITY_UNIT_AXIS", relative)

    transform = profile["transformContract"]
    valid_transform = transform.is_a?(Hash) && transform["exportedObjectScale"] == [1.0, 1.0, 1.0] &&
      transform["unityRootScale"] == [1.0, 1.0, 1.0] &&
      %w[negativeScaleAllowed sceneRootScaleCorrectionAllowed perFilePostImportRotationCorrectionAllowed perFilePostImportScaleCorrectionAllowed perFilePostImportNormalCorrectionAllowed].all? { |key| transform[key] == false }
    expect(valid_transform, "INTEROP_TRANSFORM_CONTRACT", relative)
    expect(profile.dig("pivotAndSocketContract", "character", "sourcePivot") == "Neutral pose midpoint between both feet at ground contact", "INTEROP_CHARACTER_PIVOT", relative)
    weapon_pivot = profile.dig("pivotAndSocketContract", "weapon")
    valid_weapon_pivot = weapon_pivot.is_a?(Hash) && weapon_pivot["state"] == "DEFERRED" && weapon_pivot["globalPivotValue"].nil? &&
      set_equal?(weapon_pivot["requiredMarkers"], %w[GripSocket_Main CombatSocket CenterOfMassSocket]) &&
      weapon_pivot["optionalMarkers"] == ["GripSocket_Support"] && nonempty_string?(weapon_pivot["rule"])
    expect(valid_weapon_pivot, "INTEROP_WEAPON_PIVOT_SOCKET", relative)
    map_pivot = profile.dig("pivotAndSocketContract", "mapModule")
    expect(map_pivot.is_a?(Hash) && map_pivot["state"] == "DEFERRED" && map_pivot["globalPivotValue"].nil? && nonempty_string?(map_pivot["rule"]), "INTEROP_MAP_PIVOT", relative)

    export_settings = profile.dig("blenderExportPreset", "settings")
    importer_settings = profile.dig("unityImporterPreset", "settings")
    export_metadata = profile["blenderExportPreset"]
    importer_metadata = profile["unityImporterPreset"]
    valid_export_metadata = export_metadata.is_a?(Hash) && export_metadata["presetId"] == "PHX-FBX-MODEL-r01" &&
      export_metadata["revision"] == "r01" && export_metadata["state"] == "START" &&
      export_metadata["operator"] == "bpy.ops.export_scene.fbx" && export_metadata["verifiedAgainstLocalRnaVersion"] == "5.2.0 LTS"
    valid_import_metadata = importer_metadata.is_a?(Hash) && importer_metadata["presetId"] == "PHX-UNITY-MODEL-IMPORT-r01" &&
      importer_metadata["revision"] == "r01" && importer_metadata["state"] == "START" &&
      importer_metadata["importerType"] == "UnityEditor.ModelImporter" && importer_metadata["verifiedAgainstEditorVersion"] == "6000.3.9f1"
    expect(valid_export_metadata, "INTEROP_EXPORT_PRESET_METADATA", relative)
    expect(valid_import_metadata, "INTEROP_IMPORT_PRESET_METADATA", relative)
    expect(export_settings == required_export_settings, "INTEROP_EXPORT_SETTINGS", relative)
    expect(importer_settings == required_importer_settings, "INTEROP_IMPORT_SETTINGS", relative)
    expect(profile.dig("unityImporterPreset", "assetClassOverrides") == required_asset_class_overrides, "INTEROP_ASSET_CLASS_OVERRIDES", relative)
    verify_settings_digest(profile["blenderExportPreset"], "INTEROP_EXPORT_DIGEST", relative)
    verify_settings_digest(profile["unityImporterPreset"], "INTEROP_IMPORT_DIGEST", relative)

    skeleton = profile["skeletonContract"]
    expect(skeleton.is_a?(Hash) && skeleton["extraLeafBonesAllowed"] == false, "INTEROP_LEAF_BONES", relative)
    expect(skeleton.is_a?(Hash) && skeleton["boneNamesParentsAndBindPoseMustMatch"] == true, "INTEROP_SKELETON_PRESERVATION", relative)
    expect(skeleton.is_a?(Hash) && set_equal?(skeleton["requiredLogicalBones"], required_logical_bones), "INTEROP_SKELETON_BONES", relative)
    expect(skeleton.is_a?(Hash) && skeleton["fingerBoneCount"] == 0 && skeleton["toeBoneCount"] == 0, "INTEROP_FINGER_TOE_BONES", relative)
    helper = skeleton && skeleton["helperBoneException"]
    expect(helper.is_a?(Hash) && helper["state"] == "DEFERRED" && helper["value"].nil? && nonempty_string?(helper["reason"]), "INTEROP_HELPER_BONE_POLICY", relative)
    expect(skeleton.is_a?(Hash) && skeleton.dig("maximumWeightsPerVertex", "state") == "START" && skeleton.dig("maximumWeightsPerVertex", "value") == 4, "INTEROP_SKIN_WEIGHT_LIMIT", relative)

    expect(profile["meshDataContract"] == required_mesh_data_contract, "INTEROP_MESH_DATA", relative)
    expect(profile["materialContract"] == required_material_contract, "INTEROP_MATERIAL_IMPORT", relative)
    expect(set_equal?(profile.dig("materialContract", "invariants", "allowedMaterialFamilyIds"), MATERIAL_FAMILIES), "INTEROP_MATERIAL_FAMILIES", relative)
    trace = profile["artifactTraceContract"]
    expect(trace.is_a?(Hash) && trace["generationManifestRequired"] == true, "INTEROP_GENERATION_MANIFEST", relative)
    expect(trace.is_a?(Hash) && set_equal?(trace["requiredStages"], %w[blend-source fbx-export reference-render unity-prefab]), "INTEROP_TRACE_STAGES", relative)
    expect(trace.is_a?(Hash) && set_equal?(trace["requiredIdentityFields"], required_manifest_identity_fields), "INTEROP_TRACE_IDENTITY", relative)
    expect(trace.is_a?(Hash) && trace["manualCorrectionLogMaySubstituteForProfileRevision"] == false, "INTEROP_TRACE_NO_MANUAL_SUBSTITUTE", relative)
    expect(profile.dig("parityGate", "views") == %w[Front Side Back ThreeQuarter], "INTEROP_PARITY_VIEWS", relative)
    expect(set_equal?(profile.dig("parityGate", "requiredOverlays"), required_parity_overlays), "INTEROP_PARITY_OVERLAYS", relative)
    expect(profile.dig("parityGate", "characterStartingTargets") == required_character_parity_contract, "INTEROP_CHARACTER_TOLERANCE", relative)
  end

  def validate_visual_qa
    profile = @profiles["AlphaVisualQAProfile"]
    return unless profile
    relative = "config/art/AlphaVisualQAProfile.yaml"
    expect(profile.dig("toolchainReference", "urpVersion") == "17.3.0", "QA_URP_VERSION", relative)
    expect(profile.dig("shaderReference", "qaReferenceShader") == required_qa_reference_shader, "QA_REFERENCE_SHADER", relative)
    expect(profile.dig("shaderReference", "productMaterialFamilyMapping", "state") == "DEFERRED", "QA_PRODUCT_SHADER_DEFERRED", relative)
    expect(set_equal?(profile.dig("paletteSwatch", "requiredRoles"), PALETTE_ROLES), "QA_PALETTE_ROLES", relative)
    expect(profile.dig("paletteSwatch", "productSwatchValues", "state") == "DEFERRED", "QA_PALETTE_VALUES_DEFERRED", relative)
    expect(profile.dig("paletteSwatch", "qaNeutralCalibration") == required_qa_neutral_calibration, "QA_NEUTRAL_SWATCH", relative)
    expect(profile.dig("textureColorSpace", "projectColorSpace") == "Linear", "QA_PROJECT_COLOR_SPACE", relative)
    expect(set_equal?(profile.dig("textureColorSpace", "srgbInputs"), %w[base-color character-paint emission-color ui-color]), "QA_SRGB_INPUTS", relative)
    expect(set_equal?(profile.dig("textureColorSpace", "linearDataInputs"), %w[normal metallic smoothness roughness ambient-occlusion mask height]), "QA_LINEAR_INPUTS", relative)
    expect(profile.dig("textureColorSpace", "paintTextureStart") == { "dimensions" => [512, 512], "format" => "RGBA8", "sourceColorSpace" => "sRGB" }, "QA_PAINT_TEXTURE_START", relative)
    expect(profile.dig("toneAndExposure", "unity", "autoExposureAllowed") == false, "QA_AUTO_EXPOSURE", relative)
    expect(profile.dig("toneAndExposure", "unity", "toneMapping") == "None", "QA_TONE_MAPPING", relative)
    expect(profile.dig("toneAndExposure", "unity") == required_unity_tone, "QA_UNITY_TONE_CONTRACT", relative)
    expect(profile.dig("toneAndExposure", "blender") == required_blender_tone, "QA_BLENDER_TONE_CONTRACT", relative)
    expect(profile["fixedNeutralStage"] == required_neutral_stage, "QA_NEUTRAL_STAGE", relative)

    camera = profile["orthographicReferenceCamera"]
    expect(camera == required_reference_camera, "QA_ORTHOGRAPHIC_CAMERA", relative)
    expect(camera.is_a?(Hash) && camera.fetch("views", {}).keys == %w[Front Side Back ThreeQuarter], "QA_REFERENCE_VIEWS", relative)
    matrix = profile["runtimeCaptureMatrix"]
    expect(matrix.is_a?(Hash) && matrix["state"] == "NOT_EXECUTED", "QA_CAPTURE_NOT_EXECUTED", relative)
    expect(matrix.is_a?(Hash) && matrix["participantCounts"] == [2, 3, 4], "QA_PARTICIPANT_MATRIX", relative)
    expect(matrix.is_a?(Hash) && matrix["aspectRatios"] == required_aspect_ratios, "QA_ASPECT_MATRIX", relative)
    expect(matrix.is_a?(Hash) && matrix["cameraStates"] == %w[MinGameplayDistance MaxGameplayDistance], "QA_CAMERA_STATE_MATRIX", relative)
    expect(matrix.is_a?(Hash) && matrix["persistentPlayerHudAllowed"] == false && matrix["developerDebugOverlayAllowedForReadabilityCapture"] == false, "QA_CAPTURE_OVERLAY_POLICY", relative)
    expect(nonempty_array?(matrix && matrix["requiredScenarioGroups"]), "QA_SCENARIO_MATRIX", relative)
    expect(nonempty_array?(profile["stylePreflightChecklist"]), "QA_PREFLIGHT_CHECKLIST", relative)
    expect(nonempty_array?(profile["postImportConsistencyChecklist"]), "QA_POST_IMPORT_CHECKLIST", relative)
    expect(checklist_ids(matrix && matrix["requiredScenarioGroups"]) == required_scenario_ids, "QA_SCENARIO_IDS", relative)
    expect(matrix.is_a?(Hash) && matrix["worstCaseStartingInputs"] == required_worst_case_inputs, "QA_WORST_CASE_INPUTS", relative)
    expect(checklist_ids(profile["stylePreflightChecklist"]) == required_preflight_ids, "QA_PREFLIGHT_IDS", relative)
    expect(checklist_ids(profile["postImportConsistencyChecklist"]) == required_post_import_ids, "QA_POST_IMPORT_IDS", relative)
    expect(set_equal?(profile.dig("captureRecordSchema", "requiredFields"), required_capture_record_fields), "QA_CAPTURE_RECORD_FIELDS", relative)
    expect(profile.dig("captureRecordSchema", "matchedContentOrSecretInFilenameAllowed") == false, "QA_CAPTURE_RECORD_SECRET_POLICY", relative)
    expect(profile["perceptualDecisionPolicy"] == required_perceptual_policy, "QA_PERCEPTUAL_POLICY", relative)
    expect(profile["downstreamVisualGates"] == required_downstream_gates, "QA_DOWNSTREAM_GATES", relative)
    qa_checklist_contract = {
      "stylePreflightChecklist" => profile["stylePreflightChecklist"],
      "postImportConsistencyChecklist" => profile["postImportConsistencyChecklist"],
      "requiredScenarioGroups" => matrix && matrix["requiredScenarioGroups"],
    }
    expect(canonical_digest(qa_checklist_contract) == QA_CHECKLIST_CONTENT_SHA256, "QA_CHECKLIST_CONTENT_DIGEST", relative)
  end

  def validate_cross_references
    low = @profiles["LowPolyStyleProfile"]
    interop = @profiles["ModelInteropProfile"]
    qa = @profiles["AlphaVisualQAProfile"]
    return unless low && interop && qa
    relative = "config/art/AlphaVisualQAProfile.yaml"
    expect(qa.dig("profileReferences", "lowPolyStyleProfileId") == low["profileId"] && qa.dig("profileReferences", "lowPolyStyleRevision") == low["revision"], "CROSS_LOW_POLY_REFERENCE", relative)
    expect(qa.dig("profileReferences", "modelInteropProfileId") == interop["profileId"] && qa.dig("profileReferences", "modelInteropRevision") == interop["revision"], "CROSS_INTEROP_REFERENCE", relative)
    expect(set_equal?(interop.dig("materialContract", "invariants", "allowedMaterialFamilyIds"), low["materialFamilies"]), "CROSS_MATERIAL_FAMILIES", relative)
    expect(set_equal?(qa.dig("paletteSwatch", "requiredRoles"), low.dig("palette", "roles")&.keys), "CROSS_PALETTE_ROLES", relative)
  end

  def required_export_settings
    {
      "global_scale" => 1.0,
      "apply_unit_scale" => true,
      "apply_scale_options" => "FBX_SCALE_UNITS",
      "use_space_transform" => true,
      "bake_space_transform" => false,
      "axis_forward" => "-Z",
      "axis_up" => "Y",
      "use_selection" => true,
      "object_types" => %w[ARMATURE EMPTY MESH],
      "use_mesh_modifiers" => true,
      "mesh_smooth_type" => "FACE",
      "use_tspace" => true,
      "add_leaf_bones" => false,
      "use_armature_deform_only" => true,
      "primary_bone_axis" => "Y",
      "secondary_bone_axis" => "X",
      "bake_anim" => false,
      "path_mode" => "STRIP",
      "embed_textures" => false,
      "colors_type" => "SRGB",
    }
  end

  def required_coordinate_contract
    {
      "state" => "START",
      "sourceAndTargetAxesState" => "DECIDED",
      "fbxTransportMappingState" => "START",
      "blender" => {
        "lengthUnit" => "meter",
        "unitScale" => 1.0,
        "upAxis" => "+Z",
        "characterForwardAxis" => "-Y",
      },
      "fbx" => {
        "forwardAxis" => "-Z",
        "upAxis" => "+Y",
        "globalScale" => 1.0,
      },
      "unity" => {
        "lengthUnit" => "meter",
        "unityUnitsPerMeter" => 1.0,
        "upAxis" => "+Y",
        "forwardAxis" => "+Z",
        "rightAxis" => "+X",
      },
      "equality" => "1 Blender meter = 1 Unity unit = 1 meter",
    }
  end

  def required_logical_bones
    %w[
      Root Pelvis Spine Chest Neck Head
      Clavicle_L Clavicle_R UpperArm_L UpperArm_R Forearm_L Forearm_R
      HandLogical_L HandLogical_R Thigh_L Thigh_R Calf_L Calf_R Foot_L Foot_R
    ]
  end

  def required_mesh_data_contract
    {
      "state" => "START",
      "invariants" => {
        "state" => "DECIDED",
        "uv0Required" => true,
        "invalidOrReversedNormalCount" => 0,
        "dynamicMeshColliderAllowed" => false,
        "automaticColliderGenerationAllowed" => false,
      },
      "r01TechnicalSettings" => {
        "state" => "START",
        "authoredNormalsRequired" => true,
        "tangentPolicy" => "Import FBX tangents for r01; a revision is required to calculate them in Unity",
        "lodsAreSeparateAuthoredMeshInputs" => true,
      },
    }
  end

  def required_material_contract
    {
      "state" => "START",
      "invariants" => {
        "state" => "DECIDED",
        "allowedMaterialFamilyIds" => MATERIAL_FAMILIES,
      },
      "r01TechnicalSettings" => {
        "state" => "START",
        "embeddedTexturesAllowed" => false,
        "automaticFbxMaterialImportAllowed" => false,
        "remapRequired" => true,
      },
    }
  end

  def required_manifest_identity_fields
    %w[
      assetId assetVersion toolchainProfileId projectVersionSha256 packageManifestSha256 packageLockSha256
      lowPolyStyleProfileId lowPolyStyleProfileRevision modelInteropProfileId modelInteropProfileRevision
      blenderExportPresetId blenderExportPresetRevision blenderExportSettingsSha256
      unityImporterPresetId unityImporterPresetRevision unityImporterSettingsSha256
      sourceSha256 fbxSha256 referenceRenderSha256 unityPrefabRevision
    ]
  end

  def required_parity_overlays
    %w[silhouette landmarks bounds pivot sockets collider material bevel-class normal-class]
  end

  def required_character_parity_contract
    {
      "scope" => "CharacterOnly",
      "state" => "START",
      "silhouetteLandmarkBoundsMaximumHeightRatio" => 0.005,
      "groundPivotMaximumHeightRatio" => 0.005,
      "invalidVertexOrNormalCount" => 0,
      "axisReversalCount" => 0,
      "negativeScaleCount" => 0,
      "boneHierarchyOrBindPoseMismatchCount" => 0,
    }
  end

  def required_asset_class_overrides
    {
      "StaticMesh" => {
        "animationType" => "None",
        "importBlendShapes" => false,
      },
      "SkinnedCharacter" => {
        "animationType" => "Generic",
        "avatarSetup" => "CreateFromThisModel",
        "importBlendShapes" => {
          "state" => "START",
          "value" => false,
        },
        "skinWeights" => {
          "state" => "START",
          "value" => "Standard",
        },
        "maxBonesPerVertex" => {
          "state" => "START",
          "value" => 4,
        },
      },
    }
  end

  def required_importer_settings
    {
      "globalScale" => 1.0,
      "useFileScale" => true,
      "bakeAxisConversion" => false,
      "importNormals" => "Import",
      "importTangents" => "Import",
      "meshCompression" => "Off",
      "isReadable" => false,
      "optimizeMeshPolygons" => false,
      "optimizeMeshVertices" => false,
      "weldVertices" => true,
      "preserveHierarchy" => true,
      "addCollider" => false,
      "importCameras" => false,
      "importLights" => false,
      "generateSecondaryUV" => false,
      "importMaterials" => false,
      "materialImportMode" => "None",
      "importAnimation" => false,
    }
  end

  def required_qa_neutral_calibration
    {
      "state" => "START",
      "productPalette" => false,
      "blackLinear" => 0.0,
      "middleGrayLinear" => 0.18,
      "whiteLinear" => 1.0,
    }
  end

  def required_unity_tone
    {
      "colorSpace" => "Linear",
      "toneMapping" => "None",
      "postExposureEv" => 0.0,
      "autoExposureAllowed" => false,
      "colorGradingAllowed" => false,
      "bloomAllowed" => false,
      "vignetteAllowed" => false,
    }
  end

  def required_blender_tone
    {
      "displayDevice" => "sRGB",
      "viewTransform" => "Standard",
      "look" => "None",
      "exposure" => 0.0,
      "gamma" => 1.0,
    }
  end

  def required_neutral_stage
    {
      "state" => "START",
      "productLighting" => false,
      "backgroundLinearGray" => 0.18,
      "keyLight" => {
        "type" => "Directional",
        "colorLinearRgb" => [1.0, 1.0, 1.0],
        "relativeIntensity" => 1.0,
        "eulerDegrees" => [50.0, -30.0, 0.0],
        "shadows" => true,
      },
      "ambientFill" => {
        "colorLinearRgb" => [1.0, 1.0, 1.0],
        "relativeIntensity" => 0.35,
      },
      "disabledEffects" => %w[fog depth-of-field motion-blur screen-space-effects auto-exposure],
      "matchingRule" => "Blender and Unity use the same direction, neutral colors and relative key/fill ratio; renderer response is judged perceptually, not pixel-perfectly",
    }
  end

  def required_reference_camera
    {
      "state" => "START",
      "projection" => "Orthographic",
      "target" => "Authored render bounds center",
      "boundsPaddingRatio" => 0.10,
      "resolutionPixels" => [2048, 2048],
      "views" => {
        "Front" => { "lookDirectionUnity" => [0.0, 0.0, -1.0], "upDirectionUnity" => [0.0, 1.0, 0.0] },
        "Side" => { "lookDirectionUnity" => [-1.0, 0.0, 0.0], "upDirectionUnity" => [0.0, 1.0, 0.0] },
        "Back" => { "lookDirectionUnity" => [0.0, 0.0, 1.0], "upDirectionUnity" => [0.0, 1.0, 0.0] },
        "ThreeQuarter" => { "lookDirectionUnity" => [-0.70710678, 0.0, -0.70710678], "upDirectionUnity" => [0.0, 1.0, 0.0] },
      },
      "perAssetManualFramingAllowed" => false,
      "framingRule" => "Fit authored bounds with the fixed padding; do not change focal or crop to hide drift",
    }
  end

  def required_aspect_ratios
    [
      { "id" => "16:9", "referenceResolution" => [1920, 1080] },
      { "id" => "16:10", "referenceResolution" => [1920, 1200] },
      { "id" => "21:9", "referenceResolution" => [2560, 1080] },
    ]
  end

  def required_capture_record_fields
    %w[
      captureId assetId assetVersion sourceSha256 fbxSha256 unityPrefabRevision
      lowPolyStyleProfileId modelInteropProfileId alphaVisualQaProfileId
      viewOrRuntimeScenario participantCount aspectRatio cameraState result reviewer recordedAtUtc
    ]
  end

  def required_qa_reference_shader
    {
      "state" => "START",
      "unityShaderName" => "Universal Render Pipeline/Lit",
      "purpose" => "Neutral QA reference only; not the final product shader",
      "metallic" => 0.0,
      "smoothness" => 0.25,
      "surfaceType" => "Opaque",
    }
  end

  def required_perceptual_policy
    {
      "state" => "DECIDED",
      "pixelPerfectComparisonRequired" => false,
      "arbitraryDeltaEThresholdRequired" => false,
      "humanReviewRequiredAtDownstreamGates" => true,
      "meaningfulDriftDimensions" => %w[hue value specular-response silhouette landmarks bounds],
      "automationMayGrantVisualApproval" => false,
    }
  end

  def required_preflight_ids
    %w[
      ASSET_PURPOSE_CLASS BOUNDS_SILHOUETTE MOVING_PART_PIVOT SOCKET_PURPOSE
      RENDER_COLLIDER_SPLIT BEVEL_NORMAL_CLASS FORBIDDEN_REFERENCE_DETAIL SCALE_LINEUP
    ]
  end

  def required_post_import_ids
    %w[
      FOUR_VIEW_PARITY SILHOUETTE_LANDMARK_BOUNDS APPROVED_SCALE_LINEUP
      MATERIAL_PALETTE_BEVEL_NORMAL POSE_PIVOT_SOCKET_COLLIDER INTEROP_PRESET_DIGESTS
      BLENDER_UNITY_SIDE_BY_SIDE GENERATION_MANIFEST_STRUCTURE
    ]
  end

  def required_scenario_ids
    %w[
      CHAR_NEUTRAL_LOCOMOTION CHAR_HAND_ACTIONS CHAR_AIR_ACTIONS CHAR_RAGDOLL_RECOVERY
      WEAPON_PISTOL WEAPON_LONGGUN WEAPON_MELEE WEAPON_LIFECYCLE_SUPPLY
      APPEARANCE HAZARD LOBBY_FLOW DISCONNECT_MENU_LEAVE WORST_CASE
      PATCH12_BASE_READABILITY LOCALIZATION_AUDIO
    ]
  end

  def required_worst_case_inputs
    {
      "state" => "START",
      "participantCount" => 4,
      "cosmeticsPerParticipantMaximum" => 16,
      "weaponSupplyCapMaximum" => 3,
    }
  end

  def required_downstream_gates
    [
      { "task" => "C1B-006", "gate" => "UG-C1B", "role" => "CharacterBlockoutLock" },
      { "task" => "CAM-005", "gate" => "UG-CAM", "role" => "GameplayCameraApproval" },
      { "task" => "WPA-003", "gate" => "UG-WEAPON-ART", "role" => "WeaponVisualLock" },
      { "task" => "C4-001", "gate" => "UG-C4-START", "role" => "ProductionStart" },
      { "task" => "P00-020", "gate" => "UG-P00-ART", "role" => "StylePreflight" },
      { "task" => "C4-004", "gate" => "UG-C4-LOCK", "role" => "CharacterProductionLock" },
      { "task" => "P00-023", "gate" => "UG-P00-ART-LOCK", "role" => "MapProductionLock" },
    ]
  end

  def checklist_ids(entries)
    return [] unless entries.is_a?(Array) && entries.all? { |entry| entry.is_a?(Hash) && nonempty_string?(entry["id"]) && nonempty_string?(entry["check"]) }
    entries.map { |entry| entry["id"] }
  end

  def verify_settings_digest(preset, rule, relative)
    valid = preset.is_a?(Hash) && preset["settings"].is_a?(Hash) &&
      preset["settingsSha256"] == canonical_digest(preset["settings"])
    expect(valid, rule, relative)
  end

  def canonicalize(value)
    case value
    when Hash
      value.keys.sort.each_with_object({}) { |key, result| result[key] = canonicalize(value[key]) }
    when Array
      value.map { |entry| canonicalize(entry) }
    else
      value
    end
  end

  def canonical_digest(value)
    Digest::SHA256.hexdigest(JSON.generate(canonicalize(value)))
  end

  def validate_deferred_nodes(value, relative)
    walk(value) do |node|
      next unless node.is_a?(Hash) && node["state"] == "DEFERRED"
      valid = nonempty_array?(node["ownerTasks"]) && nonempty_array?(node["unlockGates"])
      add("COMMON_DEFERRED_OWNER_GATE", relative) unless valid
      if valid
        add("COMMON_DEFERRED_OWNER_TASK", relative) unless node["ownerTasks"].all? { |task| @task_ids.include?(task) }
        add("COMMON_DEFERRED_UNLOCK_GATE", relative) unless node["unlockGates"].all? { |gate| @gate_ids.include?(gate) }
      end
      deferred_values = node.select { |key, _entry| key == "value" || key.end_with?("Value") }
      add("COMMON_DEFERRED_VALUE", relative) unless deferred_values.values.all?(&:nil?)
    end
  end

  def validate_deferred_decision_records(root_key, records, relative)
    valid = nonempty_array?(records) && records.all? do |record|
      record.is_a?(Hash) && nonempty_string?(record["field"]) && record["state"] == "DEFERRED" &&
        nonempty_array?(record["ownerTasks"]) && nonempty_array?(record["unlockGates"]) && nonempty_string?(record["reason"])
    end
    expect(valid, "COMMON_DEFERRED_DECISION_RECORDS", relative)
    fields = Array(records).select { |record| record.is_a?(Hash) }.map { |record| record["field"] }
    expect(fields == EXPECTED_DEFERRED_FIELDS.fetch(root_key), "COMMON_DEFERRED_DECISION_SET", relative)
  end

  def validate_execution(root_key, execution, relative)
    expected = {
      "LowPolyStyleProfile" => {
        "generatedBlendCount" => 0,
        "generatedFbxCount" => 0,
        "generatedUnityAssetCount" => 0,
        "captureCount" => 0,
      },
      "ModelInteropProfile" => {
        "blenderExportExecuted" => false,
        "unityImportExecuted" => false,
        "generatedBlendCount" => 0,
        "generatedFbxCount" => 0,
        "generatedUnityAssetCount" => 0,
        "parityCaptureCount" => 0,
      },
      "AlphaVisualQAProfile" => {
        "blenderRenderExecuted" => false,
        "unityCaptureExecuted" => false,
        "sideBySideComparisonExecuted" => false,
        "generatedQaSceneCount" => 0,
        "generatedCaptureCount" => 0,
        "userVisualApprovalRecorded" => false,
      },
    }.fetch(root_key)
    valid = execution == expected
    expect(valid, "COMMON_EXECUTION_ZERO", relative)
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

  def contains_locked_state?(value)
    found = false
    walk(value) { |entry| found = true if entry.is_a?(Hash) && entry["state"] == "LOCKED" }
    found
  end

  def resolve(relative)
    path = Pathname.new(relative)
    return nil if path.absolute? || path.cleanpath.to_s != relative
    absolute = @root.join(path).cleanpath
    prefix = @root.to_s + File::SEPARATOR
    return nil unless absolute.to_s.start_with?(prefix)
    absolute
  end

  def nonempty_array?(value)
    value.is_a?(Array) && !value.empty?
  end

  def nonempty_string?(value)
    value.is_a?(String) && !value.empty?
  end

  def set_equal?(actual, expected)
    actual.is_a?(Array) && actual.to_set == expected.to_set && actual.length == actual.uniq.length
  end

  def expect(condition, rule, path)
    add(rule, path) unless condition
  end

  def add(rule, path)
    key = [rule, path]
    return unless @violation_keys.add?(key)
    @violations << key
  end

  def print_report
    puts "ART_PROFILE_VALIDATION"
    puts "PROFILE_FILES_EXPECTED=#{EXPECTED_PROFILES.length}"
    puts "PROFILE_FILES_LOADED=#{@profiles.length}"
    puts "PROFILE_IDS_UNIQUE=#{@profiles.values.map { |profile| profile["profileId"] }.uniq.length}"
    puts "TOOLCHAIN_PROFILE_LOADED=#{@toolchain.is_a?(Hash)}"
    puts "ART001_SCOPE_CHECKED=#{@check_art001_scope}"
    puts "ART001_SCOPE_PATHS=#{@scope_paths_checked}"
    puts "TOTAL_VIOLATIONS=#{@violations.length}"
    @violations.sort.each { |rule, path| puts "VIOLATION rule=#{rule} path=#{path}" }
    puts "FINAL_RESULT=#{@violations.empty? ? "PASS" : "FAIL"}"
  end
end

options = {}
parser = OptionParser.new do |arguments|
  arguments.banner = "usage: ruby tools/verify_art_profiles.rb [--root PATH] [--check-art001-scope]"
  arguments.on("--root PATH", "Repository root containing config/art") { |path| options[:root] = path }
  arguments.on("--check-art001-scope", "Reject non-profile ART-001 changes and generated art assets") { options[:check_art001_scope] = true }
end

begin
  parser.parse!
  if !ARGV.empty?
    warn "ART_PROFILE_VALIDATION=ERROR reason=USAGE"
    exit 2
  end
  default_root = Pathname.new(__dir__).join("..").expand_path
  root = Pathname.new(options.fetch(:root, default_root.to_s)).expand_path
  unless root.directory?
    warn "ART_PROFILE_VALIDATION=ERROR reason=INVALID_ROOT"
    exit 2
  end
  exit ArtProfileValidator.new(root, check_art001_scope: options.fetch(:check_art001_scope, false)).run
rescue OptionParser::ParseError
  warn "ART_PROFILE_VALIDATION=ERROR reason=USAGE"
  exit 2
rescue StandardError
  warn "ART_PROFILE_VALIDATION=ERROR reason=UNEXPECTED"
  exit 2
end
