#!/usr/bin/env ruby

require "fileutils"
require "digest"
require "minitest/autorun"
require "open3"
require "pathname"
require "rbconfig"
require "tmpdir"
require "yaml"

class VerifyCharacterBlockoutTest < Minitest::Test
  REPOSITORY_ROOT = Pathname.new(__dir__).join("../..").expand_path
  VERIFIER = REPOSITORY_ROOT.join("tools/verify_character_blockout.rb")
  MANIFEST_PATH = "BlenderSource/Characters/C1B-003/GenerationManifest.yaml"
  REPORT_PATH = "BlenderSource/Characters/C1B-003/MeasurementReport.yaml"
  SOURCE_PATH = "BlenderSource/Characters/C1B-003/CHR_MasterCharacter_C1B_Blockout_r01.blend"
  RENDER_PATHS = Dir[REPOSITORY_ROOT.join("BlenderSource/Characters/C1B-003/Renders/*.png")]
    .map { |path| Pathname.new(path).relative_path_from(REPOSITORY_ROOT).to_s }.sort.freeze
  SUPPORT_PATHS = [
    MANIFEST_PATH,
    REPORT_PATH,
    SOURCE_PATH,
    *RENDER_PATHS,
    "config/character/CharacterProportionProfile.yaml",
    "tools/blender/create_c1b003_blockout.py",
    "tools/blender/inspect_c1b003_blockout.py",
    "tools/blender/rerender_c1b003_references.py",
    "tools/blender/compare_c1b003_render_pixels.py",
    "Project hotfix/ProjectSettings/ProjectVersion.txt",
    "Project hotfix/Packages/manifest.json",
    "Project hotfix/Packages/packages-lock.json",
    "artifacts/review/character/C1_CHARACTER_HYBRID_CORE_v0.13_BELLY_CORRECTED_REVIEW.png",
  ].freeze

  def test_current_static_bundle_passes
    stdout, stderr, status = run_verifier(REPOSITORY_ROOT)

    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "SOURCE_HASH_MATCH=true"
    assert_includes stdout, "REFERENCE_RENDER_COUNT=8"
    assert_includes stdout, "REFERENCE_RENDER_HASH_MATCHES=8"
    assert_includes stdout, "REFERENCE_RENDER_PNG_2048_MATCHES=8"
    assert_includes stdout, "FINAL_RESULT=PASS"
  end

  def test_current_blender_inspection_passes
    stdout, stderr, status = run_verifier(REPOSITORY_ROOT, blender: true)

    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "BLENDER_VERIFIED=true"
    assert_includes stdout, "LANDMARKS_VERIFIED=17"
    assert_includes stdout, "LANDMARK_MESH_CROSS_SECTIONS_VERIFIED=17"
    assert_includes stdout, "SECTION_INSTANCES_VERIFIED=16"
    assert_includes stdout, "RENDER_REPRODUCTION_PIXEL_MATCHES=8"
    assert_includes stdout, "FINAL_RESULT=PASS"
  end

  def test_current_c1b003_scope_passes
    stdout, stderr, status = run_verifier(REPOSITORY_ROOT, scope: true)

    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "C1B003_SCOPE_CHECKED=true"
    assert_includes stdout, "FINAL_RESULT=PASS"
  end

  def test_invalid_duplicate_and_oversize_manifest_fail
    with_repository do |root|
      File.write(root.join(MANIFEST_PATH), "invalid: [")
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=MANIFEST_YAML_INVALID"
    end

    with_repository do |root|
      File.open(root.join(MANIFEST_PATH), "a") { |file| file.write("\nGenerationManifest: {}\n") }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=MANIFEST_YAML_DUPLICATE_KEY"
    end

    with_repository do |root|
      File.open(root.join(MANIFEST_PATH), "a") { |file| file.write("#" * (512 * 1024)) }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=MANIFEST_TOO_LARGE"
    end
  end

  def test_missing_and_symlink_source_fail_closed
    with_repository do |root|
      File.delete(root.join(SOURCE_PATH))
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=BLEND_SOURCE_MISSING"
    end

    with_repository do |root|
      path = root.join(SOURCE_PATH)
      File.delete(path)
      File.symlink(REPOSITORY_ROOT.join(SOURCE_PATH), path)
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=BLEND_SOURCE_SYMLINK"
    end
  end

  def test_source_hash_and_size_drift_fail
    with_repository do |root|
      rewrite_manifest(root) do |manifest|
        source = manifest["stages"]["blend-source"]
        source["sha256"] = "0" * 64
        source["bytes"] = source["bytes"] + 1
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=BLEND_SOURCE_SIZE"
      assert_includes stdout, "rule=BLEND_SOURCE_SHA"
      assert_includes stdout, "rule=IDENTITY_SOURCE_SHA"
    end
  end

  def test_render_missing_hash_and_dimensions_fail
    with_repository do |root|
      path = RENDER_PATHS.first
      File.delete(root.join(path))
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=REFERENCE_RENDER_MISSING"
    end

    with_repository do |root|
      path = RENDER_PATHS.first
      replace_binary_copy(root, path)
      File.open(root.join(path), "r+b") do |file|
        file.seek(16)
        file.write([1024].pack("N"))
      end
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=REFERENCE_RENDER_SHA"
      assert_includes stdout, "rule=RENDER_PNG_DIMENSIONS"
    end
  end

  def test_fbx_and_unity_identity_must_remain_deferred
    with_repository do |root|
      rewrite_manifest(root) do |manifest|
        manifest["identity"]["fbxSha256"] = "1" * 64
        manifest["identity"]["unityPrefabRevision"] = "fake"
        manifest["stages"]["fbx-export"]["executed"] = true
        manifest["stages"]["unity-prefab"]["executed"] = true
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=IDENTITY_FBX_DEFERRED"
      assert_includes stdout, "rule=IDENTITY_UNITY_DEFERRED"
      assert_includes stdout, "rule=FBX_STAGE_DEFERRED"
      assert_includes stdout, "rule=UNITY_STAGE_DEFERRED"
    end
  end

  def test_lock_and_source_owner_drift_fail
    with_repository do |root|
      rewrite_manifest(root) do |manifest|
        manifest["state"] = "LOCKED"
        manifest["sourceOwner"] = "UNKNOWN"
        manifest["characterProportionProfile"]["userApprovalRecorded"] = true
        manifest["limitations"][0] = "Final user-approved FBX and Unity production lock is complete."
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=MANIFEST_STATE"
      assert_includes stdout, "rule=MANIFEST_SOURCE_OWNER"
      assert_includes stdout, "rule=MANIFEST_UNAPPROVED_LOCK"
      assert_includes stdout, "rule=CHARACTER_PROFILE_REFERENCE"
      assert_includes stdout, "rule=MANIFEST_LIMITATIONS"
    end
  end

  def test_reference_embedding_and_pixel_measurement_fail
    with_repository do |root|
      rewrite_manifest(root) do |manifest|
        boundary = manifest["sourceBoundary"]
        boundary["directionReferenceEmbeddedOrPacked"] = true
        boundary["pixelMeasurementsUsed"] = true
        boundary["externalImagesLinked"] = 1
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=SOURCE_BOUNDARY"
    end
  end

  def test_execution_cannot_claim_downstream_outputs
    with_repository do |root|
      rewrite_manifest(root) do |manifest|
        execution = manifest["execution"]
        execution["fbxExports"] = 1
        execution["unityImports"] = 1
        execution["poseOrLineupOutputs"] = 1
        execution["playerBuilds"] = 1
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=MANIFEST_EXECUTION"
    end
  end

  def test_measurement_report_cannot_claim_approval_or_downstream_work
    with_repository do |root|
      rewrite_report(root) do |report|
        report["state"] = "LOCKED"
        report["fbxSha256"] = "1" * 64
        report["reviewBoundary"]["userVisualApprovalRecorded"] = true
        report["reviewBoundary"]["lockedValueCount"] = 1
        report["reviewBoundary"]["notes"][0] = "user-approved final lock with FBX and Unity complete"
        report["landmarks"]["inspected"] = 0
        report["landmarks"]["maximumPositionDeviationH"] = 0.5
        report["silhouetteEnvelopes"]["missingSemanticEnvelopes"] = 11
        report["directionInvariants"]["result"] = "FAIL"
        report["execution"]["fbxExportsCreated"] = 1
      end
      rewrite_manifest(root) do |manifest|
        manifest["measurementEvidence"]["reportSha256"] = Digest::SHA256.file(root.join(REPORT_PATH)).hexdigest
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=MEASUREMENT_REPORT_CANONICAL_SHA"
      assert_includes stdout, "rule=MEASUREMENT_REPORT_FIELD_SET"
      assert_includes stdout, "rule=MEASUREMENT_REPORT_METADATA"
      assert_includes stdout, "rule=MEASUREMENT_REPORT_UNAPPROVED_LOCK"
      assert_includes stdout, "rule=MEASUREMENT_REPORT_LANDMARKS"
      assert_includes stdout, "rule=MEASUREMENT_REPORT_ENVELOPES"
      assert_includes stdout, "rule=MEASUREMENT_REPORT_DIRECTION"
      assert_includes stdout, "rule=MEASUREMENT_REPORT_APPROVAL_BOUNDARY"
      assert_includes stdout, "rule=MEASUREMENT_REPORT_EXECUTION"
    end
  end

  def test_generation_tool_hash_drift_fails
    with_repository do |root|
      rewrite_manifest(root) do |manifest|
        manifest["generationTools"]["generator"]["sha256"] = "0" * 64
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=GENERATION_TOOL_SHA"
    end
  end

  def test_lfs_state_and_flags_must_transition_together
    with_repository do |root|
      rewrite_manifest(root) do |manifest|
        source = manifest["stages"]["blend-source"]
        source["lfsState"] = "VERIFIED_REMOTE_ROUND_TRIP"
        source["indexPointerVerified"] = false
        source["remoteObjectRoundTripVerified"] = false
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=BLEND_STAGE_REMOTE_ROUND_TRIP_FLAG"
    end
  end

  def test_render_set_and_bundle_digest_are_exact
    with_repository do |root|
      rewrite_manifest(root) do |manifest|
        render = manifest["stages"]["reference-render"]
        render["outputs"].pop
        render["orderedBundleSha256"] = "0" * 64
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=RENDER_OUTPUT_EXACT_SET"
      assert_includes stdout, "rule=RENDER_STYLE_VIEW_EXACT_SET"
      assert_includes stdout, "rule=RENDER_BUNDLE_SHA"
    end
  end

  def test_manifest_measurement_metrics_are_bounded
    with_repository do |root|
      rewrite_manifest(root) do |manifest|
        evidence = manifest["measurementEvidence"]
        evidence["maximumBoundsDeviationH"] = 0.1
        evidence["landmarksInspected"] = 16
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=MEASUREMENT_EVIDENCE_METRICS"
    end
  end

  def test_hidden_c1b004_mesh_and_datablock_fail_blender_scope
    with_repository do |root|
      mutate_blend(root, <<~PYTHON)
        mesh = bpy.data.meshes.new('Hidden_C1B004_Pose_Mesh')
        obj = bpy.data.objects.new('Hidden_C1B004_Pose', mesh)
        bpy.data.collections['C1B003_Blockout'].objects.link(obj)
        obj.hide_render = True
      PYTHON
      stdout, _stderr, status = run_verifier(root, blender: true)

      assert_equal 1, status
      assert_includes stdout, "rule=BLENDER_OBJECT_INVENTORY"
      assert_includes stdout, "rule=BLENDER_DATABLOCK_INVENTORY"
    end
  end

  def test_material_and_light_drift_break_contract_and_render_reproduction
    with_repository do |root|
      mutate_blend(root, <<~PYTHON)
        material = bpy.data.materials['MAT_C1B003_NeutralWhite']
        material.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (1.0, 0.0, 0.0, 1.0)
        bpy.data.objects['QA_Key'].data.energy = 99.0
      PYTHON
      stdout, _stderr, status = run_verifier(root, blender: true)

      assert_equal 1, status
      assert_includes stdout, "rule=BLENDER_MATERIAL_CONTRACT"
      assert_includes stdout, "rule=BLENDER_LIGHT_CONTRACT"
      assert_includes stdout, "rule=RENDER_REPRODUCTION_PIXEL_MATCH"
    end
  end

  def test_scope_rejects_fbx_unapproved_blend_and_render
    with_git_repository do |root|
      File.write(root.join("out.fbx"), "not fbx")
      File.write(root.join("other.blend"), "not blend")
      File.write(root.join("other.png"), "not png")
      stdout, _stderr, status = run_verifier(root, scope: true)

      assert_equal 1, status
      assert_includes stdout, "rule=C1B003_DOWNSTREAM_OUTPUT"
      assert_includes stdout, "rule=C1B003_UNAPPROVED_BLEND"
      assert_includes stdout, "rule=C1B003_UNAPPROVED_RENDER"
    end
  end

  private

  def run_verifier(root, blender: false, scope: false)
    command = [RbConfig.ruby, VERIFIER.to_s, "--root", root.to_s]
    command << "--verify-blender" if blender
    command << "--check-c1b003-scope" if scope
    stdout, stderr, status = Open3.capture3(*command)
    [stdout, stderr, status.exitstatus]
  end

  def with_repository
    Dir.mktmpdir("c1b003-blockout-") do |directory|
      root = Pathname.new(directory)
      SUPPORT_PATHS.each do |relative|
        source = REPOSITORY_ROOT.join(relative)
        destination = root.join(relative)
        FileUtils.mkdir_p(destination.dirname)
        if %w[.blend .png].include?(source.extname.downcase)
          File.link(source, destination)
        else
          FileUtils.cp(source, destination)
        end
      end
      yield root
    end
  end

  def with_git_repository
    with_repository do |root|
      _stdout, stderr, status = Open3.capture3("git", "init", "-q", root.to_s)
      raise stderr unless status.success?
      [["git", "-C", root.to_s, "add", "."],
       ["git", "-C", root.to_s, "-c", "user.name=C1B Test", "-c",
        "user.email=c1b@example.invalid", "commit", "-qm", "fixture"]].each do |command|
        _out, command_error, command_status = Open3.capture3(*command)
        raise command_error unless command_status.success?
      end
      yield root
    end
  end

  def rewrite_manifest(root)
    path = root.join(MANIFEST_PATH)
    document = YAML.safe_load(File.read(path), aliases: false)
    yield document.fetch("GenerationManifest")
    File.write(path, YAML.dump(document))
  end

  def rewrite_report(root)
    path = root.join(REPORT_PATH)
    document = YAML.safe_load(File.read(path), aliases: false)
    yield document.fetch("C1B003MeasurementReport")
    File.write(path, YAML.dump(document))
  end

  def replace_binary_copy(root, relative)
    destination = root.join(relative)
    File.delete(destination)
    FileUtils.cp(REPOSITORY_ROOT.join(relative), destination)
  end

  def mutate_blend(root, mutation)
    replace_binary_copy(root, SOURCE_PATH)
    path = root.join(SOURCE_PATH)
    expression = "import bpy\n#{mutation}\nbpy.ops.wm.save_as_mainfile(filepath=#{path.to_s.inspect}, compress=True)"
    _stdout, stderr, status = Open3.capture3(
      "/Applications/Blender.app/Contents/MacOS/Blender",
      "--background", path.to_s, "--python-expr", expression
    )
    raise stderr unless status.success?
    rewrite_manifest(root) do |manifest|
      sha256 = Digest::SHA256.file(path).hexdigest
      manifest["identity"]["sourceSha256"] = sha256
      manifest["stages"]["blend-source"]["sha256"] = sha256
      manifest["stages"]["blend-source"]["bytes"] = File.size(path)
    end
  end
end
