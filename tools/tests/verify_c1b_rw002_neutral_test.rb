#!/usr/bin/env ruby

require "fileutils"
require "minitest/autorun"
require "open3"
require "pathname"
require "rbconfig"
require "tmpdir"
require "yaml"

class VerifyC1BRW002NeutralTest < Minitest::Test
  ROOT = Pathname.new(__dir__).join("../..").expand_path
  VERIFIER = ROOT.join("tools/verify_c1b_rw002_neutral.rb")
  PROFILE = "config/character/CharacterProportionProfile-C1B-RW-001-r01.yaml"
  MANIFEST = "BlenderSource/Characters/C1B-RW-002/GenerationManifest.yaml"
  REPORT = "BlenderSource/Characters/C1B-RW-002/MeasurementReport.yaml"
  SOURCE = "BlenderSource/Characters/C1B-RW-002/CHR_MasterCharacter_C1B_NeutralRework_r01.blend"
  RENDERS = Dir[ROOT.join("BlenderSource/Characters/C1B-RW-002/Renders/*.png")]
    .map { |path| Pathname.new(path).relative_path_from(ROOT).to_s }.sort.freeze
  SUPPORT = [
    PROFILE, MANIFEST, REPORT, SOURCE, *RENDERS,
    "tools/blender/create_c1b_rw002_neutral.py",
    "tools/blender/inspect_c1b_rw002_neutral.py",
    "artifacts/review/character/C1_CHARACTER_HYBRID_CORE_v0.13_BELLY_CORRECTED_REVIEW.png",
  ].freeze

  def test_current_static_bundle_passes
    out, err, status = run_verifier(ROOT)
    assert_equal 0, status, err + out
    assert_includes out, "PROFILE_STATE=START"
    assert_includes out, "CANDIDATE_STATUS=USER_REVIEW"
    assert_includes out, "SOURCE_HASH_MATCH=true"
    assert_includes out, "RENDER_HASH_MATCHES=8"
    assert_includes out, "RENDER_PNG_2048_MATCHES=8"
    assert_includes out, "FINAL_RESULT=PASS"
  end

  def test_current_blender_inspection_passes
    skip unless File.executable?("/Applications/Blender.app/Contents/MacOS/Blender")
    out, err, status = run_verifier(ROOT, blender: true)
    assert_equal 0, status, err + out
    assert_includes out, "BLENDER_VERIFIED=true"
  end

  def test_invalid_duplicate_and_oversized_yaml_fail_closed
    with_repo do |root|
      File.write(root.join(PROFILE), "invalid: [")
      assert_rule(root, "PROFILE_YAML_INVALID")
    end
    with_repo do |root|
      File.open(root.join(MANIFEST), "a") { |file| file.write("\nGenerationManifest: {}\n") }
      assert_rule(root, "MANIFEST_YAML_DUPLICATE_KEY")
    end
    with_repo do |root|
      File.open(root.join(REPORT), "a") { |file| file.write("#" * (512 * 1024)) }
      assert_rule(root, "REPORT_TOO_LARGE")
    end
  end

  def test_missing_and_symlink_source_fail_closed
    with_repo do |root|
      File.delete(root.join(SOURCE))
      assert_rule(root, "SOURCE_MISSING")
    end
    with_repo do |root|
      File.delete(root.join(SOURCE))
      File.symlink(ROOT.join(SOURCE), root.join(SOURCE))
      assert_rule(root, "SOURCE_SYMLINK")
    end
  end

  def test_source_hash_and_size_drift_fail
    with_repo do |root|
      replace_copy(root, SOURCE)
      File.open(root.join(SOURCE), "ab") { |file| file.write("drift") }
      out, _err, status = run_verifier(root)
      assert_equal 1, status
      assert_includes out, "rule=SOURCE_SIZE"
      assert_includes out, "rule=SOURCE_SHA"
    end
  end

  def test_render_missing_hash_and_dimensions_fail
    with_repo do |root|
      File.delete(root.join(RENDERS.first))
      assert_rule(root, "RENDER_MISSING")
    end
    with_repo do |root|
      replace_copy(root, RENDERS.first)
      File.open(root.join(RENDERS.first), "r+b") { |file| file.seek(16); file.write([1024].pack("N")) }
      out, _err, status = run_verifier(root)
      assert_equal 1, status
      assert_includes out, "rule=RENDER_SHA"
      assert_includes out, "rule=RENDER_DIMENSIONS"
    end
  end

  def test_profile_approval_and_gate_cannot_be_bypassed
    with_repo do |root|
      mutate_yaml(root, PROFILE, "CharacterProportionProfile") do |profile|
        profile["state"] = "LOCKED"
        profile["candidateStatus"] = "APPROVED"
        gate = profile["neutralVisualGate"]
        gate["state"] = "APPROVED"
        gate["userVisualApprovalRecorded"] = true
        gate["poseGenerationAllowed"] = true
        gate["fbxExportAllowed"] = true
        gate["unityImportAllowed"] = true
      end
      out, _err, status = run_verifier(root)
      assert_equal 1, status
      assert_includes out, "rule=PROFILE_METADATA"
      assert_includes out, "rule=PROFILE_VISUAL_GATE"
      assert_includes out, "rule=PROFILE_ILLEGAL_APPROVAL"
    end
  end

  def test_superseded_history_cannot_become_geometry_input
    with_repo do |root|
      mutate_yaml(root, PROFILE, "CharacterProportionProfile") do |profile|
        boundary = profile["supersessionBoundary"]
        boundary["rewritePriorArtifactsAllowed"] = true
        boundary["inheritOldSixPartGeometryAllowed"] = true
        boundary["inheritOldPoseCapGeometryAllowed"] = true
      end
      rewrite_manifest(root) do |manifest|
        manifest["supersessionBoundary"]["priorArtifactsRewritten"] = true
        manifest["supersessionBoundary"]["inheritedOldGeometryCount"] = 6
      end
      out, _err, status = run_verifier(root)
      assert_equal 1, status
      assert_includes out, "rule=PROFILE_SUPERSESSION"
      assert_includes out, "rule=MANIFEST_SUPERSESSION"
    end
  end

  def test_mesh_topology_and_symmetry_contract_cannot_drift
    with_repo do |root|
      rewrite_report(root) do |report|
        geometry = report["geometry"]
        geometry["renderMeshObjects"] = 6
        geometry["connectedComponents"] = 2
        geometry["boundaryEdges"] = 1
        geometry["positionSha256"] = "0" * 64
        report["symmetry"]["missingMirroredVertices"] = 1
      end
      out, _err, status = run_verifier(root)
      assert_equal 1, status
      assert_includes out, "rule=REPORT_CANONICAL_SHA"
      assert_includes out, "rule=REPORT_GEOMETRY"
      assert_includes out, "rule=REPORT_SYMMETRY"
    end
  end

  def test_prohibited_rig_uv_weights_lod_and_collider_claims_fail
    with_repo do |root|
      rewrite_manifest(root) do |manifest|
        boundary = manifest["sourceBoundary"]
        boundary["armatures"] = 1
        boundary["actions"] = 1
        boundary["colliders"] = 1
        boundary["uvLayers"] = 1
        boundary["weightedVertexAssignments"] = 1
        boundary["lodObjects"] = 1
      end
      assert_rule(root, "MANIFEST_BOUNDARY")
    end
  end

  def test_pose_fbx_unity_and_build_execution_stay_blocked
    with_repo do |root|
      rewrite_manifest(root) do |manifest|
        manifest["stages"]["pose-generation"]["status"] = "COMPLETE"
        manifest["stages"]["pose-generation"]["outputs"] = 8
        manifest["stages"]["fbx-export"]["executed"] = true
        manifest["stages"]["unity-import"]["executed"] = true
        manifest["execution"]["poseOutputs"] = 8
        manifest["execution"]["fbxExports"] = 1
        manifest["execution"]["unityImports"] = 1
        manifest["execution"]["playerBuilds"] = 1
      end
      out, _err, status = run_verifier(root)
      assert_equal 1, status
      assert_includes out, "rule=MANIFEST_POSE_BLOCK"
      assert_includes out, "rule=MANIFEST_FBX_BLOCK"
      assert_includes out, "rule=MANIFEST_UNITY_BLOCK"
      assert_includes out, "rule=MANIFEST_EXECUTION"
    end
  end

  def test_generation_tool_hash_drift_fails
    with_repo do |root|
      rewrite_manifest(root) { |manifest| manifest["generationTools"]["inspector"]["sha256"] = "0" * 64 }
      assert_rule(root, "MANIFEST_TOOLS")
    end
  end

  def test_lfs_round_trip_state_and_flags_are_exact
    with_repo do |root|
      rewrite_manifest(root) do |manifest|
        stage = manifest["stages"]["blend-source"]
        stage["status"] = "COMPLETE_LOCAL"
        stage["lfsState"] = "PENDING_CORE_PUSH"
        stage["remoteObjectRoundTripVerified"] = false
      end
      assert_rule(root, "MANIFEST_BLEND_STAGE")
    end
    with_repo do |root|
      rewrite_manifest(root) do |manifest|
        manifest["stages"]["blend-source"]["indexPointerVerified"] = false
      end
      assert_rule(root, "MANIFEST_BLEND_STAGE")
    end
  end

  def test_extra_old_cap_hand_foot_or_downstream_file_fails_exact_set
    with_repo do |root|
      File.write(root.join("BlenderSource/Characters/C1B-RW-002/CHR_C1B004_BasePlusProximalCap_Hand_Foot.fbx"), "forbidden")
      assert_rule(root, "RW002_FILE_SET")
    end
  end

  private

  def run_verifier(root, blender: false)
    command = [RbConfig.ruby, VERIFIER.to_s, "--root", root.to_s]
    command << "--verify-blender" if blender
    out, err, status = Open3.capture3(*command)
    [out, err, status.exitstatus]
  end

  def assert_rule(root, rule)
    out, _err, status = run_verifier(root)
    assert_equal 1, status
    assert_includes out, "rule=#{rule}"
  end

  def with_repo
    Dir.mktmpdir("c1brw002-") do |directory|
      root = Pathname.new(directory)
      SUPPORT.each do |relative|
        source = ROOT.join(relative); target = root.join(relative)
        FileUtils.mkdir_p(target.dirname)
        %w[.blend .png].include?(source.extname.downcase) ? File.link(source,target) : FileUtils.cp(source,target)
      end
      yield root
    end
  end

  def rewrite_manifest(root, &block); mutate_yaml(root,MANIFEST,"GenerationManifest",&block); end
  def rewrite_report(root, &block); mutate_yaml(root,REPORT,"C1BRW002MeasurementReport",&block); end
  def mutate_yaml(root,relative,key)
    path=root.join(relative); document=YAML.safe_load(File.read(path),aliases:false)
    yield document.fetch(key); File.write(path,YAML.dump(document))
  end
  def replace_copy(root,relative); File.delete(root.join(relative)); FileUtils.cp(ROOT.join(relative),root.join(relative)); end
end
