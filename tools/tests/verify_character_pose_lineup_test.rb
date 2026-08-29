#!/usr/bin/env ruby

require "digest"
require "fileutils"
require "minitest/autorun"
require "open3"
require "pathname"
require "rbconfig"
require "tmpdir"
require "yaml"

class VerifyCharacterPoseLineupTest < Minitest::Test
  ROOT = Pathname.new(__dir__).join("../..").expand_path
  VERIFIER = ROOT.join("tools/verify_character_pose_lineup.rb")
  MANIFEST = "BlenderSource/Characters/C1B-004/GenerationManifest.yaml"
  REPORT = "BlenderSource/Characters/C1B-004/PoseLineupReport.yaml"
  SOURCE = "BlenderSource/Characters/C1B-004/CHR_MasterCharacter_C1B_PoseLineup_r02.blend"
  RENDERS = Dir[ROOT.join("BlenderSource/Characters/C1B-004/Renders/*.png")].map { |p| Pathname.new(p).relative_path_from(ROOT).to_s }.sort.freeze
  SUPPORT = [
    MANIFEST, REPORT, SOURCE, *RENDERS,
    "BlenderSource/Characters/C1B-003/CHR_MasterCharacter_C1B_Blockout_r01.blend",
    "BlenderSource/Characters/C1B-003/GenerationManifest.yaml",
    "config/character/CharacterProportionProfile.yaml",
    "tools/blender/create_c1b004_pose_lineup.py",
    "tools/blender/inspect_c1b004_pose_lineup.py",
    "tools/blender/rerender_c1b004_references.py",
    "tools/blender/compare_c1b004_render_pixels.py",
    "Project hotfix/ProjectSettings/ProjectVersion.txt",
    "Project hotfix/Packages/manifest.json",
    "Project hotfix/Packages/packages-lock.json",
  ].freeze

  def test_current_static_bundle_passes
    stdout, stderr, status = run_verifier(ROOT)
    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "SOURCE_HASH_MATCH=true"
    assert_includes stdout, "REFERENCE_RENDER_COUNT=20"
    assert_includes stdout, "REFERENCE_RENDER_HASH_MATCHES=20"
    assert_includes stdout, "REFERENCE_RENDER_PNG_2048_MATCHES=20"
    assert_includes stdout, "FINAL_RESULT=PASS"
  end

  def test_current_blender_inspection_and_rerender_pass
    skip "Blender 5.2 unavailable" unless File.executable?("/Applications/Blender.app/Contents/MacOS/Blender")
    stdout, stderr, status = run_verifier(ROOT, blender: true)
    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "BLENDER_VERIFIED=true"
    assert_includes stdout, "POSES_VERIFIED=8"
    assert_includes stdout, "LINEUPS_VERIFIED=2"
    assert_includes stdout, "RENDER_REPRODUCTION_PIXEL_MATCHES=20"
    assert_match(/RENDER_REPRODUCTION_EXACT_MATCHES=\d+/, stdout)
  end

  def test_invalid_duplicate_and_oversized_yaml_fail_closed
    with_repo do |root|
      File.write(root.join(MANIFEST), "invalid: [")
      assert_rule(root, "MANIFEST_YAML_INVALID")
    end
    with_repo do |root|
      File.open(root.join(MANIFEST), "a") { |f| f.write("\nGenerationManifest: {}\n") }
      assert_rule(root, "MANIFEST_YAML_DUPLICATE_KEY")
    end
    with_repo do |root|
      File.open(root.join(MANIFEST), "a") { |f| f.write("#" * (512 * 1024)) }
      assert_rule(root, "MANIFEST_TOO_LARGE")
    end
  end

  def test_missing_and_symlink_source_fail_closed
    with_repo do |root|
      File.delete(root.join(SOURCE))
      assert_rule(root, "BLEND_SOURCE_MISSING")
    end
    with_repo do |root|
      File.delete(root.join(SOURCE))
      File.symlink(ROOT.join(SOURCE), root.join(SOURCE))
      assert_rule(root, "BLEND_SOURCE_SYMLINK")
    end
  end

  def test_source_size_and_hash_are_canonical
    with_repo do |root|
      replace_copy(root, SOURCE)
      File.open(root.join(SOURCE), "ab") { |f| f.write("drift") }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=BLEND_SOURCE_SIZE"
      assert_includes stdout, "rule=BLEND_SOURCE_SHA"
    end
  end

  def test_baseline_source_and_manifest_lineage_are_immutable
    with_repo do |root|
      rewrite_manifest(root) { |m| m["derivedFrom"]["sourceSha256"] = "0" * 64 }
      assert_rule(root, "BASE_LINEAGE")
    end
    with_repo do |root|
      path = "BlenderSource/Characters/C1B-003/GenerationManifest.yaml"
      File.open(root.join(path), "a") { |f| f.write("\n# drift") }
      assert_rule(root, "BASE_MANIFEST_SHA")
    end
  end

  def test_missing_pose_and_lineup_are_rejected
    with_repo do |root|
      rewrite_manifest(root) { |m| m["poseLineupContract"]["requiredPoseIds"].delete("Dropkick") }
      assert_rule(root, "POSE_LINEUP_CONTRACT")
    end
    with_repo do |root|
      rewrite_report(root) { |r| r["lineups"]["ids"].delete("Lineup_Spread") }
      assert_rule(root, "REPORT_LINEUPS")
    end
  end

  def test_lineup_participant_count_is_exactly_four
    with_repo do |root|
      rewrite_manifest(root) { |m| m["poseLineupContract"]["participantCountPerLineup"] = 3 }
      rewrite_report(root) { |r| r["lineups"]["participantCountPerLineup"] = 3 }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=POSE_LINEUP_CONTRACT"
      assert_includes stdout, "rule=REPORT_LINEUPS"
    end
  end

  def test_cap_derived_geometry_contract_cannot_be_reversed
    with_repo do |root|
      rewrite_manifest(root) do |m|
        m["sourceBoundary"]["proximalCapVerticesAddedPerMesh"] = 1
        m["sourceBoundary"]["proximalCapPolygonsAddedPerMesh"] = 0
        m["sourceBoundary"]["productionTopologyApproved"] = true
      end
      rewrite_report(root) do |r|
        r["poses"]["actionLimbGeometryMode"] = "BASE_LINKED"
        r["lineage"]["productionTopologyApproved"] = true
      end
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=SOURCE_BOUNDARY"
      assert_includes stdout, "rule=REPORT_POSES"
      assert_includes stdout, "rule=REPORT_LINEAGE"
    end
  end

  def test_approval_lock_and_lfs_remote_claims_are_rejected
    with_repo do |root|
      rewrite_manifest(root) do |m|
        m["state"] = "LOCKED"
        m["poseLineupContract"]["userApprovalRecorded"] = true
        m["poseLineupContract"]["lockedValueCount"] = 1
        m["stages"]["blend-source"]["lfsState"] = "VERIFIED_REMOTE_ROUND_TRIP"
        m["stages"]["blend-source"]["indexPointerVerified"] = true
        m["stages"]["blend-source"]["remoteObjectRoundTripVerified"] = true
        m["stages"]["blend-source"]["remoteProof"] = "unverified"
        m["limitations"][0] = "Production-ready final asset."
      end
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=MANIFEST_METADATA"
      assert_includes stdout, "rule=UNAPPROVED_LOCK"
      assert_includes stdout, "rule=CONTRACT_APPROVAL"
      assert_includes stdout, "rule=LFS_PENDING_STATE"
      assert_includes stdout, "rule=BLEND_STAGE_FIELD_SET"
      assert_includes stdout, "rule=LIMITATIONS"
    end

    with_repo do |root|
      rewrite_manifest(root) do |m|
        m["stages"]["blend-source"]["indexPointerVerified"] = false
      end
      assert_rule(root, "LFS_PENDING_STATE")
    end
  end

  def test_fbx_unity_rig_collider_and_build_scope_remain_zero
    with_repo do |root|
      rewrite_manifest(root) do |m|
        m["identity"]["fbxSha256"] = "1" * 64
        m["stages"]["fbx-export"]["executed"] = true
        m["execution"]["fbxExports"] = 1
        m["execution"]["unityAssets"] = 1
        m["execution"]["armatures"] = 1
        m["execution"]["colliderProfiles"] = 1
        m["execution"]["playerBuilds"] = 1
      end
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=IDENTITY_DOWNSTREAM_DEFERRED"
      assert_includes stdout, "rule=FBX_DEFERRED"
      assert_includes stdout, "rule=EXECUTION_SCOPE"
    end
  end

  def test_render_missing_hash_dimensions_and_bundle_fail
    with_repo do |root|
      File.delete(root.join(RENDERS.first))
      assert_rule(root, "REFERENCE_RENDER_MISSING")
    end
    with_repo do |root|
      replace_copy(root, RENDERS.first)
      File.open(root.join(RENDERS.first), "r+b") { |f| f.seek(16); f.write([1024].pack("N")) }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=REFERENCE_RENDER_SHA"
      assert_includes stdout, "rule=RENDER_PNG_DIMENSIONS"
    end
    with_repo do |root|
      rewrite_manifest(root) { |m| m["stages"]["reference-render"]["outputs"].pop }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=RENDER_OUTPUT_EXACT_SET"
      assert_includes stdout, "rule=ENCODED_BUNDLE"
    end
  end

  def test_render_material_light_camera_and_observation_semantics_fail
    with_repo do |root|
      rewrite_manifest(root) do |m|
        render = m["stages"]["reference-render"]
        render["outputs"].first["camera"] = "CAM_FAKE"
        render["localReproductionExactMatches"] = 0
        render["reproducedMaximumChannelDifference"] = 1
        m["poseLineupContract"]["camerasSha256"] = "0" * 64
        m["poseLineupContract"]["renderJobsSha256"] = "0" * 64
      end
      rewrite_report(root) do |r|
        r["cameras"]["result"] = "FAIL"
        r["renders"]["sourceRerenderExactMatches"] = 0
        r["renders"]["reproducedMaximumChannelDifference"] = 1
      end
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=RENDER_VIEW_CAMERA"
      assert_includes stdout, "rule=RENDER_REPRODUCTION_METADATA"
      assert_includes stdout, "rule=CONTRACT_DIGEST"
      assert_includes stdout, "rule=REPORT_RENDERS"
    end
  end

  def test_report_semantic_reversal_fails_even_if_reference_hash_is_updated
    with_repo do |root|
      rewrite_report(root) do |r|
        r["state"] = "LOCKED"
        r["reviewBoundary"]["userVisualApprovalRecorded"] = true
        r["reviewBoundary"]["lockedValueCount"] = 1
        r["execution"]["unityAssetsCreated"] = 1
      end
      rewrite_manifest(root) { |m| m["report"]["sha256"] = Digest::SHA256.file(root.join(REPORT)).hexdigest }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=REPORT_CANONICAL_SHA"
      assert_includes stdout, "rule=REPORT_METADATA"
      assert_includes stdout, "rule=REPORT_APPROVAL_BOUNDARY"
      assert_includes stdout, "rule=REPORT_EXECUTION"
    end
  end

  def test_hidden_pose_datablock_source_mutation_fails_closed
    skip "Blender 5.2 unavailable" unless File.executable?("/Applications/Blender.app/Contents/MacOS/Blender")
    with_repo do |root|
      mutate_blend(root, <<~PYTHON)
        mesh = bpy.data.meshes.new('Hidden_C1B004_Pose_Mesh')
        obj = bpy.data.objects.new('Hidden_C1B004_Pose', mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.hide_render = True
      PYTHON
      assert_rule(root, "BLEND_SOURCE_SHA")
    end
  end

  def test_geometry_drift_source_mutation_fails_closed
    skip "Blender 5.2 unavailable" unless File.executable?("/Applications/Blender.app/Contents/MacOS/Blender")
    with_repo do |root|
      mutate_blend(root, "bpy.data.meshes['C1B004_Arm_L_BasePlusProximalCap_Mesh'].vertices[0].co.x += 0.01")
      assert_rule(root, "BLEND_SOURCE_SHA")
    end
  end

  def test_generation_tool_hash_drift_fails
    with_repo do |root|
      rewrite_manifest(root) { |m| m["generationTools"]["inspector"]["sha256"] = "0" * 64 }
      assert_rule(root, "GENERATION_TOOL_REFERENCE")
    end
  end

  def test_forbidden_extra_blend_fbx_unity_and_render_fail_file_set
    with_repo do |root|
      %w[extra.blend extra.fbx extra.prefab extra.png].each { |name| File.write(root.join("BlenderSource/Characters/C1B-004", name), "forbidden") }
      assert_rule(root, "C1B004_FILE_SET")
    end
  end

  def test_scope_gate_rejects_unrelated_change
    with_git_repo do |root|
      File.write(root.join("unrelated.txt"), "drift")
      stdout, _stderr, status = run_verifier(root, scope: true)
      assert_equal 1, status
      assert_includes stdout, "rule=C1B004_SCOPE"
    end
  end

  def test_current_c1b004_scope_passes
    stdout, stderr, status = run_verifier(ROOT, scope: true)
    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "C1B004_SCOPE_CHECKED=true"
    assert_includes stdout, "FINAL_RESULT=PASS"
  end

  private

  def run_verifier(root, blender: false, scope: false)
    command = [RbConfig.ruby, VERIFIER.to_s, "--root", root.to_s]
    command << "--verify-blender" if blender
    command << "--check-c1b004-scope" if scope
    out, err, status = Open3.capture3(*command)
    [out, err, status.exitstatus]
  end

  def assert_rule(root, rule)
    stdout, _stderr, status = run_verifier(root)
    assert_equal 1, status
    assert_includes stdout, "rule=#{rule}"
  end

  def with_repo
    Dir.mktmpdir("c1b004-pose-") do |directory|
      root = Pathname.new(directory)
      SUPPORT.each do |relative|
        source = ROOT.join(relative); destination = root.join(relative)
        FileUtils.mkdir_p(destination.dirname)
        %w[.blend .png].include?(source.extname.downcase) ? File.link(source, destination) : FileUtils.cp(source, destination)
      end
      yield root
    end
  end

  def with_git_repo
    with_repo do |root|
      system("git", "init", "-q", root.to_s, exception: true)
      system("git", "-C", root.to_s, "add", ".", exception: true)
      system("git", "-C", root.to_s, "-c", "user.name=C1B Test", "-c", "user.email=c1b@example.invalid", "commit", "-qm", "fixture", exception: true)
      yield root
    end
  end

  def rewrite_manifest(root)
    path = root.join(MANIFEST); document = YAML.safe_load(File.read(path), aliases: false)
    yield document.fetch("GenerationManifest")
    File.write(path, YAML.dump(document))
  end

  def rewrite_report(root)
    path = root.join(REPORT); document = YAML.safe_load(File.read(path), aliases: false)
    yield document.fetch("C1B004PoseLineupReport")
    File.write(path, YAML.dump(document))
  end

  def replace_copy(root, relative)
    File.delete(root.join(relative)); FileUtils.cp(ROOT.join(relative), root.join(relative))
  end

  def mutate_blend(root, mutation)
    replace_copy(root, SOURCE)
    path = root.join(SOURCE)
    expression = "import bpy\n#{mutation}\nbpy.ops.wm.save_as_mainfile(filepath=#{path.to_s.inspect}, compress=True)"
    _out, error, status = Open3.capture3("/Applications/Blender.app/Contents/MacOS/Blender", "--background", path.to_s, "--python-expr", expression)
    raise error unless status.success?
    rewrite_manifest(root) do |manifest|
      sha = Digest::SHA256.file(path).hexdigest
      manifest["identity"]["sourceSha256"] = sha
      manifest["stages"]["blend-source"]["sha256"] = sha
      manifest["stages"]["blend-source"]["bytes"] = File.size(path)
    end
  end
end
