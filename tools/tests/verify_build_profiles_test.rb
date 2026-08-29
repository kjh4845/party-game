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

class VerifyBuildProfilesTest < Minitest::Test
  REPOSITORY_ROOT = Pathname.new(__dir__).join("../..").expand_path
  VERIFIER = REPOSITORY_ROOT.join("tools/verify_build_profiles.rb")
  POLICY_PATH = "config/build_profiles/WindowsBuildProfilePolicy.yaml"
  TOOLCHAIN_PATH = "config/toolchain/ToolchainProfile.yaml"
  PLAYER_SETTINGS_PATH = "Project hotfix/ProjectSettings/ProjectSettings.asset"
  EDITOR_BUILD_SETTINGS_PATH = "Project hotfix/ProjectSettings/EditorBuildSettings.asset"
  DEVELOPMENT_PROFILE = "Project hotfix/Assets/Settings/Build Profiles/Windows x64 Development.asset"
  STEAM_PROFILE = "Project hotfix/Assets/Settings/Build Profiles/Windows x64 Steam Reserved.asset"
  MANIFEST_PATH = "Project hotfix/Packages/manifest.json"
  LOCK_PATH = "Project hotfix/Packages/packages-lock.json"
  SUPPORT_FILES = [
    POLICY_PATH,
    "config/build_profiles/WINDOWS_BUILD_MANUAL.md",
    TOOLCHAIN_PATH,
    PLAYER_SETTINGS_PATH,
    EDITOR_BUILD_SETTINGS_PATH,
    DEVELOPMENT_PROFILE,
    "#{DEVELOPMENT_PROFILE}.meta",
    STEAM_PROFILE,
    "#{STEAM_PROFILE}.meta",
    "Project hotfix/Assets/Scenes/SampleScene.unity",
    "Project hotfix/Assets/Scenes/SampleScene.unity.meta",
    MANIFEST_PATH,
    LOCK_PATH,
    ".gitignore",
  ].freeze

  def test_current_repository_passes_without_building
    stdout, stderr, status = run_verifier(REPOSITORY_ROOT)

    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "PROFILES_CHECKED=2"
    assert_includes stdout, "RAW_TARGET_SUBTARGET_INTERPRETED=false"
    assert_includes stdout, "PACKAGE_IDS_HARDCODED=0"
    assert_includes stdout, "TOTAL_VIOLATIONS=0"
    assert_includes stdout, "FINAL_RESULT=PASS"
  end

  def test_local_windows_mono_module_check_is_explicit_and_hermetic
    with_repository do |root|
      Dir.mktmpdir("bld001-unity-") do |directory|
        unity_root = Pathname.new(directory)
        module_root = unity_root.join("PlaybackEngines/WindowsStandaloneSupport")
        required = %w[
          modules.asset
          UnityEditor.WindowsStandalone.Extensions.dll
          Variations/win64_player_development_mono/WindowsPlayer.exe
          Variations/win64_player_nondevelopment_mono/WindowsPlayer.exe
        ]
        required.each { |relative| write_file(module_root.join(relative), "fixture\n") }
        stdout, stderr, status = run_verifier(
          root,
          "--verify-local-windows-module",
          env: { "PROJECT_HOTFIX_UNITY_EDITOR_ROOT" => unity_root.to_s },
        )

        assert_equal 0, status, stderr + stdout
        assert_includes stdout, "LOCAL_WINDOWS_MODULE_REQUESTED=true"
        assert_includes stdout, "LOCAL_WINDOWS_MODULE_VERIFIED=true"
      end
    end
  end

  def test_policy_duplicate_key_invalid_yaml_and_oversize_fail_closed
    with_repository do |root|
      path = root.join(POLICY_PATH)
      text = path.read.sub("  schemaVersion: 1\n", "  schemaVersion: 1\n  schemaVersion: 1\n")
      path.write(text)
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=POLICY_YAML_DUPLICATE_KEY"
    end

    with_repository do |root|
      root.join(POLICY_PATH).write("BuildProfilePolicy: [\n")
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=POLICY_YAML_INVALID"
    end

    with_repository do |root|
      root.join(POLICY_PATH).write("x" * (512 * 1024 + 1))
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=POLICY_TOO_LARGE"
    end
  end

  def test_policy_symlink_is_not_followed
    with_repository do |root|
      path = root.join(POLICY_PATH)
      File.delete(path)
      File.symlink(REPOSITORY_ROOT.join(POLICY_PATH), path)
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=POLICY_SYMLINK"
    end
  end

  def test_policy_schema_and_execution_boundaries_reject_mutation
    with_repository do |root|
      mutate_policy(root) do |policy|
        policy["ownerTask"] = "ALP-001"
        policy["toolchain"]["automaticPlayerBuildAllowed"] = true
        policy["executionBoundary"]["playerBuildsExecutedByBld001"] = 1
        policy["steamBoundary"]["functionalityClaimed"] = true
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=POLICY_OWNER"
      assert_includes stdout, "rule=POLICY_TOOLCHAIN_AUTOMATICPLAYERBUILDALLOWED"
      assert_includes stdout, "rule=POLICY_EXECUTION_ZERO"
      assert_includes stdout, "rule=POLICY_STEAM_BOUNDARY"
    end
  end

  def test_temporary_identity_version_and_identifier_are_enforced
    with_repository do |root|
      path = root.join(PLAYER_SETTINGS_PATH)
      text = path.read
        .sub("  companyName: KJH4845", "  companyName: Unknown")
        .sub("  productName: Project Hotfix", "  productName: Wrong Product")
        .sub("  bundleVersion: 0.1.0", "  bundleVersion: 9.9.9")
        .sub("    Standalone: com.kjh4845.projecthotfix", "    Standalone: invalid.identifier")
      path.write(text)
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      %w[
        PLAYER_COMPANY_NAME PLAYER_PRODUCT_NAME PLAYER_BUNDLE_VERSION
        PLAYER_STANDALONE_IDENTIFIER
      ].each { |rule| assert_includes stdout, "rule=#{rule}" }
    end
  end

  def test_exact_profile_and_meta_file_set_rejects_missing_and_extra_profile
    with_repository do |root|
      File.delete(root.join(STEAM_PROFILE))
      write_file(root.join("Project hotfix/Assets/Settings/Build Profiles/Server.buildprofile"), "fixture\n")
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_DIRECTORY_EXACT_SET"
      assert_includes stdout, "rule=CUSTOM_PROFILE_EXACT_SET"
      assert_includes stdout, "rule=PROFILE_ASSET_MISSING"
    end
  end

  def test_profile_guid_must_match_policy_and_be_unique
    with_repository do |root|
      dev_meta = root.join("#{DEVELOPMENT_PROFILE}.meta")
      steam_meta = root.join("#{STEAM_PROFILE}.meta")
      dev_guid = dev_meta.read[/^guid:\s*([0-9a-f]{32})$/, 1]
      steam_meta.write(steam_meta.read.sub(/^guid:\s*[0-9a-f]{32}$/, "guid: #{dev_guid}"))
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_META_GUID_DRIFT"
      assert_includes stdout, "rule=PROFILE_META_GUID_UNIQUE"
    end
  end

  def test_profile_defines_debug_flags_and_settings_overrides_are_enforced
    with_repository do |root|
      path = root.join(DEVELOPMENT_PROFILE)
      text = path.read
        .sub("  - PROJECTHOTFIX_BUILD_DEVELOPMENT", "  - UNAPPROVED_DEFINE")
        .sub("        m_ConnectProfiler: 0", "        m_ConnectProfiler: 1")
        .sub("    m_Settings: []", "    m_Settings:\n    - key: qualityLevel")
      path.write(text)
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_DEFINE_EXACT_SET"
      assert_includes stdout, "rule=PROFILE_PLATFORM_M_CONNECTPROFILER"
      assert_includes stdout, "rule=PROFILE_PLAYER_SETTINGS_EMPTY"
    end
  end

  def test_raw_build_target_and_subtarget_numbers_are_left_to_unity_tests
    with_repository do |root|
      [DEVELOPMENT_PROFILE, STEAM_PROFILE].each do |relative|
        path = root.join(relative)
        path.write(path.read.sub("  m_BuildTarget: 19", "  m_BuildTarget: 9876")
          .sub("  m_Subtarget: 2", "  m_Subtarget: 5432"))
      end
      stdout, stderr, status = run_verifier(root)

      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "RAW_TARGET_SUBTARGET_INTERPRETED=false"
      assert_includes stdout, "FINAL_RESULT=PASS"
    end
  end

  def test_global_sample_scene_and_meta_guid_are_enforced
    with_repository do |root|
      settings = root.join(EDITOR_BUILD_SETTINGS_PATH)
      settings.write(settings.read.sub("  - enabled: 1", "  - enabled: 0"))
      meta = root.join("Project hotfix/Assets/Scenes/SampleScene.unity.meta")
      meta.write(meta.read.sub(/^guid:\s*[0-9a-f]{32}$/, "guid: 00000000000000000000000000000000"))
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=GLOBAL_SCENE_EXACT_SET"
      assert_includes stdout, "rule=SCENE_META_GUID"
    end
  end

  def test_manual_guide_required_markers_cannot_be_removed
    with_repository do |root|
      guide = root.join("config/build_profiles/WINDOWS_BUILD_MANUAL.md")
      guide.write(guide.read.gsub("steam_appid.txt", "removed-marker"))
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=GUIDE_REQUIRED_MARKER"
    end
  end

  def test_manifest_and_lock_hash_drift_fail_without_hardcoding_package_set
    with_repository do |root|
      manifest = JSON.parse(root.join(MANIFEST_PATH).read)
      manifest.fetch("dependencies")["com.example.safe-fixture"] = "1.2.3"
      root.join(MANIFEST_PATH).write(JSON.pretty_generate(manifest) + "\n")
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=MANIFEST_SHA256_DRIFT"
    end

    with_repository do |root|
      manifest = JSON.parse(root.join(MANIFEST_PATH).read)
      lock = JSON.parse(root.join(LOCK_PATH).read)
      manifest.fetch("dependencies")["com.example.safe-fixture"] = "1.2.3"
      lock.fetch("dependencies")["com.example.safe-fixture"] = {
        "version" => "1.2.3", "depth" => 0, "source" => "registry", "dependencies" => {},
      }
      root.join(MANIFEST_PATH).write(JSON.pretty_generate(manifest) + "\n")
      root.join(LOCK_PATH).write(JSON.pretty_generate(lock) + "\n")
      update_toolchain_hashes(root)
      stdout, stderr, status = run_verifier(root)

      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "PACKAGE_IDS_HARDCODED=0"
    end
  end

  def test_steam_appid_sdk_binary_and_package_are_rejected
    with_repository do |root|
      write_file(root.join("Project hotfix/Assets/Plugins/x86_64/steam_api64.dll"), "fixture\n")
      write_file(root.join("Project hotfix/steam_appid.txt"), "480\n")
      manifest = JSON.parse(root.join(MANIFEST_PATH).read)
      manifest.fetch("dependencies")["com.example.steamworks-sdk"] = "1.0.0"
      root.join(MANIFEST_PATH).write(JSON.pretty_generate(manifest) + "\n")
      update_toolchain_hashes(root, manifest_only: true)
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=STEAM_SDK_OR_APPID_FILE"
      assert_includes stdout, "rule=STEAM_SDK_PACKAGE"
    end
  end

  def test_automatic_build_and_server_or_headless_signatures_are_rejected
    with_repository do |root|
      write_file(
        root.join("Project hotfix/Assets/Editor/AutomatedPlayerBuild.cs"),
        "BuildPipeline.BuildPlayer(options);\n#if UNITY_SERVER\n#endif\n",
      )
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=AUTOMATIC_BUILD_ENTRYPOINT"
      assert_includes stdout, "rule=SERVER_OR_HEADLESS_BUILD_SIGNATURE"
    end
  end

  def test_ci_and_cloud_build_artifacts_are_rejected
    with_repository do |root|
      write_file(root.join(".github/workflows/windows-build.yml"), "jobs: {}\n")
      write_file(root.join("cloudbuild.yaml"), "steps: []\n")
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=CI_OR_CLOUD_BUILD_ARTIFACT"
    end
  end

  def test_tracked_build_outputs_are_rejected_even_when_gitignored
    with_repository do |root|
      output = root.join("Project hotfix/Builds/Windows/Development/Project Hotfix.exe")
      write_file(output, "not-an-executable\n")
      _stdout, stderr, status = Open3.capture3("git", "-C", root.to_s, "add", "-f", "--", output.to_s)
      raise stderr unless status.success?
      stdout, _stderr, verifier_status = run_verifier(root)

      assert_equal 1, verifier_status
      assert_includes stdout, "rule=TRACKED_BUILD_OUTPUT"
    end
  end

  private

  def with_repository
    Dir.mktmpdir("bld001-fixture-") do |directory|
      root = Pathname.new(directory)
      _stdout, stderr, status = Open3.capture3("git", "-C", root.to_s, "init", "-q")
      raise stderr unless status.success?
      SUPPORT_FILES.each { |relative| copy_file(REPOSITORY_ROOT.join(relative), root.join(relative)) }
      _stdout, stderr, status = Open3.capture3("git", "-C", root.to_s, "add", "-A", "--", ".")
      raise stderr unless status.success?
      yield root
    end
  end

  def copy_file(source, destination)
    FileUtils.mkdir_p(destination.dirname)
    FileUtils.cp(source, destination)
  end

  def write_file(path, content)
    FileUtils.mkdir_p(path.dirname)
    File.binwrite(path, content)
  end

  def mutate_policy(root)
    path = root.join(POLICY_PATH)
    document = YAML.safe_load(path.read, [], [], false)
    yield document.fetch("BuildProfilePolicy")
    path.write(YAML.dump(document))
  end

  def update_toolchain_hashes(root, manifest_only: false)
    path = root.join(TOOLCHAIN_PATH)
    document = YAML.safe_load(path.read, [], [], false)
    baseline = document.fetch("ToolchainProfile").fetch("packageBaseline")
    baseline["manifestSha256"] = Digest::SHA256.file(root.join(MANIFEST_PATH)).hexdigest
    baseline["lockSha256"] = Digest::SHA256.file(root.join(LOCK_PATH)).hexdigest unless manifest_only
    path.write(YAML.dump(document))
  end

  def run_verifier(root, *arguments, env: {})
    command = [RbConfig.ruby, VERIFIER.to_s, "--root", root.to_s, *arguments]
    stdout, stderr, status = Open3.capture3(env, *command)
    [stdout, stderr, status.exitstatus]
  end
end
