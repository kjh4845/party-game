#!/usr/bin/env ruby

require "fileutils"
require "digest"
require "json"
require "minitest/autorun"
require "open3"
require "pathname"
require "rbconfig"
require "tmpdir"
require "yaml"

class VerifyArtProfilesTest < Minitest::Test
  REPOSITORY_ROOT = Pathname.new(__dir__).join("../..").expand_path
  VERIFIER = REPOSITORY_ROOT.join("tools/verify_art_profiles.rb")
  PROFILE_FILES = %w[
    LowPolyStyleProfile.yaml
    ModelInteropProfile.yaml
    AlphaVisualQAProfile.yaml
  ].freeze
  SUPPORT_FILES = [
    "config/toolchain/ToolchainProfile.yaml",
    "Project hotfix/ProjectSettings/ProjectVersion.txt",
    "Project hotfix/Packages/manifest.json",
    "Project hotfix/Packages/packages-lock.json",
    "docs/ART_DIRECTION.md",
    "docs/CHARACTER_TECHNICAL_SPEC.md",
    "docs/WEAPON_DESIGN.md",
    "docs/MAP_DESIGN_GUIDE.md",
    "docs/UI_UX_FLOW.md",
    "docs/03_IMPLEMENTATION_PLAN.md",
  ].freeze

  def test_current_profiles_pass
    stdout, stderr, status = run_verifier(REPOSITORY_ROOT)

    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "PROFILE_FILES_LOADED=3"
    assert_includes stdout, "PROFILE_IDS_UNIQUE=3"
    assert_includes stdout, "TOTAL_VIOLATIONS=0"
    assert_includes stdout, "FINAL_RESULT=PASS"
  end

  def test_isolated_art001_change_scope_passes_without_asset_outputs
    with_git_repository do |root|
      stdout, stderr, status = run_verifier(root, check_scope: true)

      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "ART001_SCOPE_CHECKED=true"
      assert_includes stdout, "FINAL_RESULT=PASS"
    end
  end

  def test_missing_profile_fails
    with_repository do |root|
      File.delete(root.join("config/art/LowPolyStyleProfile.yaml"))
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_MISSING"
      assert_includes stdout, "path=config/art/LowPolyStyleProfile.yaml"
    end
  end

  def test_invalid_yaml_and_oversize_profile_fail_closed
    with_repository do |root|
      File.write(root.join("config/art/AlphaVisualQAProfile.yaml"), "invalid: [")
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_YAML_INVALID"
    end

    with_repository do |root|
      File.write(root.join("config/art/AlphaVisualQAProfile.yaml"), "x" * (512 * 1024 + 1))
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_TOO_LARGE"
    end
  end

  def test_profile_symlink_is_not_followed
    with_repository do |root|
      profile = root.join("config/art/ModelInteropProfile.yaml")
      File.delete(profile)
      File.symlink(REPOSITORY_ROOT.join("config/art/ModelInteropProfile.yaml"), profile)

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_SYMLINK"
      assert_includes stdout, "path=config/art/ModelInteropProfile.yaml"
    end
  end

  def test_common_owner_id_state_and_approval_contracts_fail_when_mutated
    with_repository do |root|
      mutate(root, "LowPolyStyleProfile.yaml") { |profile| profile["ownerTask"] = "WPA-001" }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=COMMON_OWNER"
    end

    with_repository do |root|
      mutate(root, "AlphaVisualQAProfile.yaml") do |profile|
        profile["profileId"] = "LowPolyStyleProfile-ART-001-r01"
      end
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=COMMON_PROFILE_ID"
      assert_includes stdout, "rule=COMMON_PROFILE_SET"
    end

    with_repository do |root|
      mutate(root, "LowPolyStyleProfile.yaml") do |profile|
        profile["bevelClasses"]["B1_RIGID_READABILITY"]["state"] = "LOCKED"
        profile["visualApprovalClaimed"] = true
      end
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=COMMON_ILLEGAL_LOCKED_VALUE"
      assert_includes stdout, "rule=COMMON_VISUAL_APPROVAL"
    end
  end

  def test_document_root_source_set_and_deferred_decision_set_cannot_be_extended_or_shrunk
    with_repository do |root|
      path = root.join("config/art/LowPolyStyleProfile.yaml")
      document = YAML.safe_load(File.read(path), [], [], false)
      profile = document.fetch("LowPolyStyleProfile")
      profile["sourceDocuments"].pop
      profile["deferredDecisions"].delete_at(1)
      document["FinalLockedProfile"] = { "state" => "LOCKED" }
      File.write(path, YAML.dump(document))

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_DOCUMENT_ROOT_SET"
      assert_includes stdout, "rule=COMMON_SOURCE_DOCUMENT_SET"
      assert_includes stdout, "rule=COMMON_DEFERRED_DECISION_SET"
    end
  end

  def test_source_version_and_toolchain_path_must_be_exact
    with_repository do |root|
      mutate(root, "AlphaVisualQAProfile.yaml") do |profile|
        profile["sourceDocuments"][0]["version"] = "999.0"
        profile["toolchainReference"]["path"] = "missing.yaml"
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=COMMON_SOURCE_DOCUMENT_ENTRY"
      assert_includes stdout, "rule=COMMON_TOOLCHAIN_PATH"
    end
  end

  def test_toolchain_reference_and_actual_hash_drift_fail
    with_repository do |root|
      mutate(root, "ModelInteropProfile.yaml") do |profile|
        profile["toolchainReference"]["unityEditorVersion"] = "different"
      end
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=INTEROP_TOOLCHAIN_DRIFT"
    end

    with_repository do |root|
      File.open(root.join("Project hotfix/Packages/manifest.json"), "a") { |file| file.write("\n") }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=TOOLCHAIN_MANIFEST_HASH"
    end
  end

  def test_unit_axis_and_transform_mutations_fail
    with_repository do |root|
      mutate(root, "ModelInteropProfile.yaml") do |profile|
        profile["coordinateContract"]["blender"]["upAxis"] = "+Y"
        profile["coordinateContract"]["blender"]["lengthUnit"] = "centimeter"
        profile["coordinateContract"]["unity"]["lengthUnit"] = "centimeter"
        profile["coordinateContract"]["equality"] = "different"
        profile["coordinateContract"]["unity"]["unityUnitsPerMeter"] = 100.0
        profile["transformContract"]["negativeScaleAllowed"] = true
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INTEROP_BLENDER_UNIT_AXIS"
      assert_includes stdout, "rule=INTEROP_UNITY_UNIT_AXIS"
      assert_includes stdout, "rule=INTEROP_COORDINATE_CONTRACT"
      assert_includes stdout, "rule=INTEROP_TRANSFORM_CONTRACT"
    end
  end

  def test_palette_bevel_and_material_contracts_cannot_be_removed
    with_repository do |root|
      mutate(root, "LowPolyStyleProfile.yaml") do |profile|
        profile["palette"]["roles"].delete("Warning")
        profile["bevelClasses"].delete("B2_SOFT_HERO")
        profile["bevelClasses"]["B0_INTENTIONAL_NONE"]["segmentCount"] = 9
        profile["bevelClasses"]["B1_RIGID_READABILITY"]["segmentCount"]["comparisonCandidates"] = [999]
        profile["materialFamilies"].delete("MAT_Decal")
        profile["materialRules"]["finalShaderMapping"]["state"] = "START"
        profile["materialRules"]["finalShaderMapping"]["value"] = "Final/Shader"
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=LOW_POLY_PALETTE_ROLES"
      assert_includes stdout, "rule=LOW_POLY_BEVEL_CLASSES"
      assert_includes stdout, "rule=LOW_POLY_MATERIAL_FAMILIES"
      assert_includes stdout, "rule=LOW_POLY_BEVEL_B0"
      assert_includes stdout, "rule=LOW_POLY_BEVEL_SEGMENTS"
      assert_includes stdout, "rule=LOW_POLY_FINAL_SHADER_DEFERRED"
      assert_includes stdout, "rule=CROSS_PALETTE_ROLES"
      assert_includes stdout, "rule=CROSS_MATERIAL_FAMILIES"
    end
  end

  def test_export_import_settings_and_digests_cannot_drift
    with_repository do |root|
      mutate(root, "ModelInteropProfile.yaml") do |profile|
        profile["blenderExportPreset"]["settings"]["axis_forward"] = "+Z"
        profile["unityImporterPreset"]["settings"].delete("importNormals")
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INTEROP_EXPORT_SETTINGS"
      assert_includes stdout, "rule=INTEROP_EXPORT_DIGEST"
      assert_includes stdout, "rule=INTEROP_IMPORT_SETTINGS"
      assert_includes stdout, "rule=INTEROP_IMPORT_DIGEST"
    end
  end

  def test_full_export_settings_and_asset_class_overrides_are_enforced_even_with_recomputed_digest
    with_repository do |root|
      mutate(root, "ModelInteropProfile.yaml") do |profile|
        preset = profile["blenderExportPreset"]
        preset["settings"]["primary_bone_axis"] = "Z"
        preset["settingsSha256"] = settings_digest(preset["settings"])
        profile["unityImporterPreset"]["assetClassOverrides"]["SkinnedCharacter"]["animationType"] = "Humanoid"
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INTEROP_EXPORT_SETTINGS"
      refute_includes stdout, "rule=INTEROP_EXPORT_DIGEST"
      assert_includes stdout, "rule=INTEROP_ASSET_CLASS_OVERRIDES"
    end
  end

  def test_capture_matrix_views_and_cross_references_cannot_drift
    with_repository do |root|
      mutate(root, "AlphaVisualQAProfile.yaml") do |profile|
        profile["orthographicReferenceCamera"]["views"].delete("Back")
        profile["runtimeCaptureMatrix"]["participantCounts"] = [2, 4]
        profile["runtimeCaptureMatrix"]["aspectRatios"].delete_at(1)
        profile["profileReferences"]["modelInteropProfileId"] = "different"
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=QA_REFERENCE_VIEWS"
      assert_includes stdout, "rule=QA_PARTICIPANT_MATRIX"
      assert_includes stdout, "rule=QA_ASPECT_MATRIX"
      assert_includes stdout, "rule=CROSS_INTEROP_REFERENCE"
    end
  end

  def test_qa_vectors_neutral_stage_aspect_resolution_and_record_schema_are_exact
    with_repository do |root|
      mutate(root, "AlphaVisualQAProfile.yaml") do |profile|
        profile["orthographicReferenceCamera"]["views"]["Front"]["lookDirectionUnity"] = [9.0, 9.0, 9.0]
        profile["orthographicReferenceCamera"]["resolutionPixels"] = [1, 1]
        profile["fixedNeutralStage"]["keyLight"]["relativeIntensity"] = 9.0
        profile["shaderReference"]["qaReferenceShader"]["smoothness"] = 99.0
        profile["runtimeCaptureMatrix"]["aspectRatios"][0]["referenceResolution"] = [1, 1]
        profile["captureRecordSchema"]["requiredFields"].delete("sourceSha256")
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=QA_ORTHOGRAPHIC_CAMERA"
      assert_includes stdout, "rule=QA_NEUTRAL_STAGE"
      assert_includes stdout, "rule=QA_REFERENCE_SHADER"
      assert_includes stdout, "rule=QA_ASPECT_MATRIX"
      assert_includes stdout, "rule=QA_CAPTURE_RECORD_FIELDS"
    end
  end

  def test_mesh_material_skeleton_pivot_and_generation_trace_contracts_cannot_shrink
    with_repository do |root|
      mutate(root, "ModelInteropProfile.yaml") do |profile|
        profile["meshDataContract"]["invariants"]["uv0Required"] = false
        profile["meshDataContract"]["invariants"]["dynamicMeshColliderAllowed"] = true
        profile["meshDataContract"]["r01TechnicalSettings"]["lodsAreSeparateAuthoredMeshInputs"] = false
        profile["materialContract"]["r01TechnicalSettings"]["embeddedTexturesAllowed"] = true
        profile["materialContract"]["r01TechnicalSettings"]["remapRequired"] = false
        profile["skeletonContract"]["requiredLogicalBones"] = ["Root"]
        profile["skeletonContract"]["fingerBoneCount"] = 1
        profile["pivotAndSocketContract"]["weapon"]["state"] = "START"
        profile["pivotAndSocketContract"]["weapon"]["globalPivotValue"] = [1, 2, 3]
        profile["artifactTraceContract"]["generationManifestRequired"] = false
        profile["artifactTraceContract"]["requiredIdentityFields"] = ["assetId"]
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INTEROP_MESH_DATA"
      assert_includes stdout, "rule=INTEROP_MATERIAL_IMPORT"
      assert_includes stdout, "rule=INTEROP_SKELETON_BONES"
      assert_includes stdout, "rule=INTEROP_FINGER_TOE_BONES"
      assert_includes stdout, "rule=INTEROP_WEAPON_PIVOT_SOCKET"
      assert_includes stdout, "rule=INTEROP_GENERATION_MANIFEST"
      assert_includes stdout, "rule=INTEROP_TRACE_IDENTITY"
    end
  end

  def test_character_parity_tolerances_and_overlays_are_exact
    with_repository do |root|
      mutate(root, "ModelInteropProfile.yaml") do |profile|
        parity = profile["parityGate"]
        parity["requiredOverlays"] = []
        targets = parity["characterStartingTargets"]
        targets["groundPivotMaximumHeightRatio"] = 99
        targets["axisReversalCount"] = 5
        targets["negativeScaleCount"] = 5
        targets["boneHierarchyOrBindPoseMismatchCount"] = 5
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INTEROP_PARITY_OVERLAYS"
      assert_includes stdout, "rule=INTEROP_CHARACTER_TOLERANCE"
    end
  end

  def test_visual_approval_policy_scenarios_checklists_gates_and_worst_case_are_exact
    with_repository do |root|
      mutate(root, "AlphaVisualQAProfile.yaml") do |profile|
        profile["perceptualDecisionPolicy"]["automationMayGrantVisualApproval"] = true
        profile["stylePreflightChecklist"] = [{ "id" => "x", "check" => "x" }]
        profile["postImportConsistencyChecklist"] = [{ "id" => "x", "check" => "x" }]
        profile["runtimeCaptureMatrix"]["requiredScenarioGroups"] = [{ "id" => "x", "check" => "x" }]
        profile["runtimeCaptureMatrix"]["worstCaseStartingInputs"]["cosmeticsPerParticipantMaximum"] = 1
        profile["downstreamVisualGates"].delete_at(0)
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=QA_PERCEPTUAL_POLICY"
      assert_includes stdout, "rule=QA_PREFLIGHT_IDS"
      assert_includes stdout, "rule=QA_POST_IMPORT_IDS"
      assert_includes stdout, "rule=QA_SCENARIO_IDS"
      assert_includes stdout, "rule=QA_WORST_CASE_INPUTS"
      assert_includes stdout, "rule=QA_DOWNSTREAM_GATES"
    end
  end

  def test_deferred_owner_and_gate_are_required
    with_repository do |root|
      mutate(root, "LowPolyStyleProfile.yaml") do |profile|
        role = profile["palette"]["roles"]["WorldNeutralBase"]
        role["ownerTasks"] = []
        role["unlockGates"] = []
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=COMMON_DEFERRED_OWNER_GATE"
      assert_includes stdout, "rule=LOW_POLY_PALETTE_ROLE_CONTRACT"
    end

    with_repository do |root|
      mutate(root, "LowPolyStyleProfile.yaml") do |profile|
        role = profile["palette"]["roles"]["WorldNeutralBase"]
        role["ownerTasks"] = ["NOT-A-TASK"]
        role["unlockGates"] = ["UG-NOT-A-GATE"]
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=COMMON_DEFERRED_OWNER_TASK"
      assert_includes stdout, "rule=COMMON_DEFERRED_UNLOCK_GATE"
    end

    with_repository do |root|
      mutate(root, "AlphaVisualQAProfile.yaml") do |profile|
        profile["paletteSwatch"]["productSwatchValues"]["value"] = "#ff00ff"
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=COMMON_DEFERRED_VALUE"
    end
  end

  def test_asset_execution_or_capture_claim_fails
    with_repository do |root|
      mutate(root, "ModelInteropProfile.yaml") do |profile|
        profile["execution"]["blenderExportExecuted"] = true
        profile["execution"]["generatedFbxCount"] = 1
      end
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=COMMON_EXECUTION_ZERO"
    end

    with_repository do |root|
      mutate(root, "AlphaVisualQAProfile.yaml") do |profile|
        profile["execution"]["userVisualApprovalRecorded"] = true
      end
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=COMMON_EXECUTION_ZERO"
    end


    with_repository do |root|
      mutate(root, "ModelInteropProfile.yaml") do |profile|
        profile["execution"] = { "unityImportExecuted" => false }
      end
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=COMMON_EXECUTION_ZERO"
    end
  end

  def test_missing_coordinate_and_style_rule_sections_fail_with_structured_violations
    with_repository do |root|
      mutate(root, "ModelInteropProfile.yaml") { |profile| profile.delete("coordinateContract") }
      mutate(root, "LowPolyStyleProfile.yaml") do |profile|
        profile.delete("materialRules")
        profile["palette"]["rules"].delete("requiredCompanionCues")
        profile["shapeLanguage"]["character"]["rules"] = []
        profile["normalClasses"]["N0_FLAT"] = {}
        profile["forbiddenDetails"] = ["x"]
        profile["alphaStartingRanges"]["characterLod0TriangleCandidates"] = [1]
        profile["alphaStartingRanges"]["characterLod1RatioRange"] = [9, 9]
        profile["alphaStartingRanges"]["paintTexture"]["dimensions"] = [1, 1]
        profile["palette"]["rules"]["semanticColorMeaningMayChangePerMap"] = true
        profile["palette"]["rules"]["greyboxColorsAreFinalPalette"] = true
      end
      stdout, stderr, status = run_verifier(root)

      assert_equal 1, status, stderr + stdout
      assert_includes stdout, "rule=INTEROP_COORDINATE_SECTION"
      assert_includes stdout, "rule=LOW_POLY_MATERIAL_RULES"
      assert_includes stdout, "rule=LOW_POLY_COMPANION_CUES"
      assert_includes stdout, "rule=LOW_POLY_CHARACTER_RULES"
      assert_includes stdout, "rule=LOW_POLY_NORMAL_CLASS_CONTRACT"
      assert_includes stdout, "rule=LOW_POLY_FORBIDDEN_DETAILS"
      assert_includes stdout, "rule=LOW_POLY_SOURCE_CONTRACT_DIGEST"
    end
  end


  def test_qa_checklist_ids_cannot_hide_destroyed_descriptions
    with_repository do |root|
      mutate(root, "AlphaVisualQAProfile.yaml") do |profile|
        profile["stylePreflightChecklist"].each { |entry| entry["check"] = "x" }
        profile["postImportConsistencyChecklist"].each { |entry| entry["check"] = "x" }
        profile["runtimeCaptureMatrix"]["requiredScenarioGroups"].each { |entry| entry["check"] = "x" }
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=QA_CHECKLIST_CONTENT_DIGEST"
    end
  end

  def test_extra_profile_and_generated_art_asset_fail_art001_scope
    with_git_repository do |root|
      extra_profile = root.join("config/art/FinalLockedPalette.yaml")
      File.write(extra_profile, "FinalLockedPalette: {}\n")
      asset = root.join("Project hotfix/Assets/ProjectHotfix/Art/Test.fbx")
      FileUtils.mkdir_p(asset.dirname)
      File.binwrite(asset, "not-an-fbx")

      stdout, _stderr, status = run_verifier(root, check_scope: true)

      assert_equal 1, status
      assert_includes stdout, "rule=ART001_PROFILE_FILE_SET"
      assert_includes stdout, "rule=ART001_SCOPE_PATH"
      assert_includes stdout, "rule=ART001_ASSET_OUTPUT_PRESENT"
    end
  end

  def test_non_repository_root_is_a_usage_error
    Dir.mktmpdir("art001-empty-") do |directory|
      _stdout, stderr, status = run_verifier(Pathname.new(directory))

      assert_equal 1, status
      assert_empty stderr
    end
  end

  private

  def with_repository
    Dir.mktmpdir("art001-fixture-") do |directory|
      root = Pathname.new(directory)
      PROFILE_FILES.each { |filename| copy("config/art/#{filename}", root) }
      SUPPORT_FILES.each { |relative| copy(relative, root) }
      yield root
    end
  end

  def with_git_repository
    with_repository do |root|
      run_git(root, "init", "-q")
      run_git(root, "add", "-A", "--", ".")
      run_git(root, "-c", "user.name=ART001 Test", "-c", "user.email=art001@example.invalid", "commit", "-q", "-m", "baseline")
      yield root
    end
  end

  def run_git(root, *arguments)
    _stdout, stderr, status = Open3.capture3("git", "-C", root.to_s, *arguments)
    raise stderr unless status.success?
  end

  def copy(relative, root)
    source = REPOSITORY_ROOT.join(relative)
    destination = root.join(relative)
    FileUtils.mkdir_p(destination.dirname)
    FileUtils.cp(source, destination)
  end

  def mutate(root, filename)
    path = root.join("config/art", filename)
    document = YAML.safe_load(File.read(path), [], [], false)
    root_key = document.keys.fetch(0)
    yield document.fetch(root_key)
    File.write(path, YAML.dump(document))
  end

  def settings_digest(settings)
    Digest::SHA256.hexdigest(JSON.generate(canonicalize(settings)))
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

  def run_verifier(root, check_scope: false)
    arguments = [RbConfig.ruby, VERIFIER.to_s, "--root", root.to_s]
    arguments << "--check-art001-scope" if check_scope
    stdout, stderr, status = Open3.capture3(
      *arguments
    )
    [stdout, stderr, status.exitstatus]
  end
end
