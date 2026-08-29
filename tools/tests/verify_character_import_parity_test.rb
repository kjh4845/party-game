#!/usr/bin/env ruby

require "digest"
require "fileutils"
require "json"
require "minitest/autorun"
require "open3"
require "pathname"
require "rbconfig"
require "tmpdir"
require "yaml"

class VerifyCharacterImportParityTest < Minitest::Test
  ROOT = Pathname.new(__dir__).join("../..").expand_path
  VERIFIER = ROOT.join("tools/verify_character_import_parity.rb")
  MANIFEST = "BlenderSource/Characters/C1B-005/GenerationManifest.yaml"
  REPORT = "BlenderSource/Characters/C1B-005/InteropComparisonReport.yaml"
  FBX = "Project hotfix/Assets/ProjectHotfix/Art/Characters/C1B-005/CHR_MasterCharacter_C1B_Neutral_r02.fbx"
  FBX_META = FBX + ".meta"
  PREFAB = "Project hotfix/Assets/ProjectHotfix/Art/Characters/C1B-005/CHR_MasterCharacter_C1B_Neutral_r02.prefab"
  PREFAB_META = PREFAB + ".meta"
  INSPECTION = "artifacts/evidence/G0/C1B-005/UnityImportInspection.json"
  CAPTURES = Dir[ROOT.join("artifacts/evidence/G0/C1B-005/Captures/Unity/*.png")]
    .map { |path| Pathname.new(path).relative_path_from(ROOT).to_s }.sort.freeze
  SUPPORT = [
    MANIFEST, REPORT, FBX, FBX_META, PREFAB, PREFAB_META, INSPECTION, *CAPTURES,
    "BlenderSource/Characters/C1B-004/CHR_MasterCharacter_C1B_PoseLineup_r02.blend",
    "config/art/ModelInteropProfile-r02.yaml", "config/art/AlphaVisualQAProfile-r02.yaml",
    "tools/blender/export_c1b005_neutral_fbx.py", "tools/blender/inspect_c1b005_fbx.py",
    "Project hotfix/Assets/ProjectHotfix/Editor/C1B005/C1B005ImportContract.cs",
    "Project hotfix/Assets/ProjectHotfix/Editor/C1B005/C1B005ModelPostprocessor.cs",
    "Project hotfix/Assets/ProjectHotfix/Editor/C1B005/C1B005ParityPipeline.cs",
    "Project hotfix/Assets/ProjectHotfix/Tests/EditMode/C1B005/C1B005ImportParityTests.cs",
  ].freeze

  def test_current_static_bundle_passes
    out, err, status = run_verifier(ROOT)
    assert_equal 0, status, err + out
    assert_includes out, "FBX_HASH_MATCH=true"
    assert_includes out, "PREFAB_HASH_MATCH=true"
    assert_includes out, "CAPTURE_HASH_MATCHES=8"
    assert_includes out, "CAPTURE_PNG_2048_MATCHES=8"
    assert_includes out, "FINAL_RESULT=PASS"
  end

  def test_current_blender_inspector_passes
    skip unless File.executable?("/Applications/Blender.app/Contents/MacOS/Blender")
    out, err, status = run_verifier(ROOT, blender: true)
    assert_equal 0, status, err + out
    assert_includes out, "BLENDER_VERIFIED=true"
  end

  def test_current_unity_editmode_passes
    skip unless File.executable?("/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity")
    out, err, status = run_verifier(ROOT, unity: true)
    assert_equal 0, status, err + out
    assert_includes out, "UNITY_EDITMODE_TESTS=7"
  end

  def test_invalid_duplicate_and_oversized_manifest_fail_closed
    with_repo do |root|
      File.write(root.join(MANIFEST), "invalid: [")
      assert_rule(root, "MANIFEST_YAML_INVALID")
    end
    with_repo do |root|
      File.open(root.join(MANIFEST), "a") { |file| file.write("\nGenerationManifest: {}\n") }
      assert_rule(root, "MANIFEST_YAML_DUPLICATE_KEY")
    end
    with_repo do |root|
      File.open(root.join(MANIFEST), "a") { |file| file.write("#" * (512 * 1024)) }
      assert_rule(root, "MANIFEST_TOO_LARGE")
    end
  end

  def test_fbx_hash_size_and_meta_guid_drift_fail
    with_repo do |root|
      replace_copy(root, FBX)
      File.open(root.join(FBX), "ab") { |file| file.write("drift") }
      out, _err, status = run_verifier(root)
      assert_equal 1, status
      assert_includes out, "rule=FBX_SIZE"
      assert_includes out, "rule=FBX_SHA"
    end
    with_repo do |root|
      File.write(root.join(FBX_META), File.read(root.join(FBX_META)).sub("250d071cf52954f0586c84d27ec778db", "0" * 32))
      assert_rule(root, "FBX_META_GUID")
    end
  end

  def test_prefab_and_meta_hash_drift_fail
    with_repo do |root|
      File.open(root.join(PREFAB), "a") { |file| file.write("\n# drift") }
      assert_rule(root, "PREFAB_SHA")
    end
    with_repo do |root|
      File.write(root.join(PREFAB_META), File.read(root.join(PREFAB_META)).sub("e4c57671925554af4aa4e36feea50f81", "1" * 32))
      assert_rule(root, "PREFAB_META_GUID")
    end
  end

  def test_stale_missing_and_wrong_dimension_capture_fail
    with_repo do |root|
      File.delete(root.join(CAPTURES.first))
      assert_rule(root, "CAPTURE_MISSING")
    end
    with_repo do |root|
      replace_copy(root, CAPTURES.first)
      File.open(root.join(CAPTURES.first), "r+b") { |file| file.seek(16); file.write([1024].pack("N")) }
      out, _err, status = run_verifier(root)
      assert_equal 1, status
      assert_includes out, "rule=CAPTURE_SHA"
      assert_includes out, "rule=CAPTURE_DIMENSIONS"
    end
  end

  def test_left_right_and_forward_semantic_drift_fail
    with_repo do |root|
      mutate_inspection(root) do |inspection|
        inspection["landmarks"].find { |value| value["id"] == "LM_Shoulder_L" }["actual"]["x"] = 0.205
        inspection["maximumLandmarkDeviationH"] = 0.41
        inspection["rootForward"] = {"x"=>0.0,"y"=>0.0,"z"=>-1.0}
      end
      rewrite_manifest(root) { |manifest| manifest["inspection"]["sha256"] = Digest::SHA256.file(root.join(INSPECTION)).hexdigest }
      out, _err, status = run_verifier(root)
      assert_equal 1, status
      assert_includes out, "rule=INSPECTION_SHA"
      assert_includes out, "rule=INSPECTION_GEOMETRY"
      assert_includes out, "rule=INSPECTION_AXIS"
    end
  end

  def test_tangent_exception_cannot_leak_into_global_production_rule
    with_repo do |root|
      mutate_yaml(root, "config/art/ModelInteropProfile-r02.yaml", "ModelInteropProfile") do |profile|
        override = profile["unityImporterPreset"]["assetClassOverrides"]["C1BBlockout"]["settings"]
        override["importTangents"] = "Import"
        override["globalProductionUv0RequiredPreserved"] = false
        profile["meshDataContract"]["invariants"]["uv0Required"] = false
      end
      assert_rule(root, "MODEL_IMPORT_OVERRIDE")
    end
  end

  def test_report_reversal_and_fake_motion_approval_fail_even_with_updated_reference
    with_repo do |root|
      rewrite_report(root) do |report|
        report["state"] = "LOCKED"
        report["staticActionReview"]["motionNaturalnessClaimed"] = true
        report["scopeBoundary"]["userVisualApprovalRecorded"] = true
        report["scopeBoundary"]["productionLockRecorded"] = true
        report["execution"]["animationActions"] = 1
        report["execution"]["playerBuilds"] = 1
      end
      rewrite_manifest(root) { |manifest| manifest["report"]["sha256"] = Digest::SHA256.file(root.join(REPORT)).hexdigest }
      out, _err, status = run_verifier(root)
      assert_equal 1, status
      assert_includes out, "rule=REPORT_CANONICAL_SHA"
      assert_includes out, "rule=REPORT_STATIC_ACTION"
      assert_includes out, "rule=REPORT_SCOPE_BOUNDARY"
      assert_includes out, "rule=REPORT_EXECUTION"
      assert_includes out, "rule=REPORT_FAKE_APPROVAL"
    end
  end

  def test_inspection_fake_animation_build_and_manual_correction_fail
    with_repo do |root|
      mutate_inspection(root) do |inspection|
        inspection["animatorCount"] = 1
        inspection["animationNaturalnessClaimed"] = true
        inspection["playerBuildsExecuted"] = 1
        inspection["manualTransformCorrections"] = 1
      end
      assert_rule(root, "INSPECTION_SCOPE")
    end
  end

  def test_lfs_pending_state_and_flags_are_exact
    with_repo do |root|
      rewrite_manifest(root) do |manifest|
        stage = manifest["stages"]["fbx-export"]
        stage["lfsState"] = "VERIFIED_REMOTE_ROUND_TRIP"
        stage["indexPointerVerified"] = true
        stage["remoteObjectRoundTripVerified"] = true
      end
      assert_rule(root, "FBX_LFS_PENDING")
    end
    with_repo do |root|
      rewrite_manifest(root) do |manifest|
        manifest["stages"]["fbx-export"]["indexPointerVerified"] = false
      end
      assert_rule(root, "FBX_LFS_PENDING")
    end
  end

  def test_scope_rejects_unrelated_and_forbidden_unity_asset
    with_git_repo do |root|
      File.write(root.join("unrelated.txt"), "drift")
      forbidden = root.join("Project hotfix/Assets/ProjectHotfix/Art/Characters/C1B-005/fake.anim")
      File.write(forbidden, "animation")
      out, _err, status = run_verifier(root, scope: true)
      assert_equal 1, status
      assert_includes out, "rule=C1B005_SCOPE"
      assert_includes out, "rule=C1B005_FORBIDDEN_ASSET"
    end
  end

  private

  def run_verifier(root, blender: false, unity: false, scope: false)
    command = [RbConfig.ruby, VERIFIER.to_s, "--root", root.to_s]
    command << "--verify-blender" if blender
    command << "--verify-unity" if unity
    command << "--check-c1b005-scope" if scope
    out, err, status = Open3.capture3(*command)
    [out, err, status.exitstatus]
  end

  def assert_rule(root, rule)
    out, _err, status = run_verifier(root)
    assert_equal 1, status
    assert_includes out, "rule=#{rule}"
  end

  def with_repo
    Dir.mktmpdir("c1b005-import-") do |directory|
      root = Pathname.new(directory)
      SUPPORT.each do |relative|
        source = ROOT.join(relative); target = root.join(relative)
        FileUtils.mkdir_p(target.dirname)
        %w[.blend .fbx .png].include?(source.extname.downcase) ? File.link(source,target) : FileUtils.cp(source,target)
      end
      yield root
    end
  end

  def with_git_repo
    with_repo do |root|
      system("git","init","-q",root.to_s,exception:true)
      system("git","-C",root.to_s,"add",".",exception:true)
      system("git","-C",root.to_s,"-c","user.name=C1B Test","-c","user.email=c1b@example.invalid","commit","-qm","fixture",exception:true)
      yield root
    end
  end

  def rewrite_manifest(root)
    mutate_yaml(root,MANIFEST,"GenerationManifest") { |value| yield value }
  end

  def rewrite_report(root)
    mutate_yaml(root,REPORT,"C1B005InteropComparisonReport") { |value| yield value }
  end

  def mutate_yaml(root,relative,key)
    path=root.join(relative); document=YAML.safe_load(File.read(path),aliases:false)
    yield document.fetch(key); File.write(path,YAML.dump(document))
  end

  def mutate_inspection(root)
    path=root.join(INSPECTION); value=JSON.parse(File.read(path)); yield value
    File.write(path,JSON.pretty_generate(value)+"\n")
  end

  def replace_copy(root,relative)
    File.delete(root.join(relative)); FileUtils.cp(ROOT.join(relative),root.join(relative))
  end
end
