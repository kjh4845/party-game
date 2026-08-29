#!/usr/bin/env ruby

require "digest"
require "json"
require "open3"
require "optparse"
require "pathname"
require "set"
require "yaml"

class BuildProfileAuditError < StandardError; end
class BuildProfileDuplicateJsonKeyError < StandardError; end

class BuildProfileUniqueJsonHash < Hash
  def []=(key, value)
    raise BuildProfileDuplicateJsonKeyError, key if key?(key)

    super
  end
end

class BuildProfileVerifier
  POLICY_PATH = "config/build_profiles/WindowsBuildProfilePolicy.yaml"
  GUIDE_PATH = "config/build_profiles/WINDOWS_BUILD_MANUAL.md"
  TOOLCHAIN_PATH = "config/toolchain/ToolchainProfile.yaml"
  PLAYER_SETTINGS_PATH = "Project hotfix/ProjectSettings/ProjectSettings.asset"
  EDITOR_BUILD_SETTINGS_PATH = "Project hotfix/ProjectSettings/EditorBuildSettings.asset"
  MANIFEST_PATH = "Project hotfix/Packages/manifest.json"
  LOCK_PATH = "Project hotfix/Packages/packages-lock.json"

  MAX_YAML_BYTES = 512 * 1024
  MAX_UNITY_TEXT_BYTES = 8 * 1024 * 1024
  MAX_GUIDE_BYTES = 512 * 1024
  MAX_JSON_BYTES = 4 * 1024 * 1024
  MAX_SCAN_BYTES = 2 * 1024 * 1024

  TEMPORARY_IDENTITY = {
    "companyName" => "KJH4845",
    "productName" => "Project Hotfix",
    "standaloneApplicationIdentifier" => "com.kjh4845.projecthotfix",
    "bundleVersion" => "0.1.0",
  }.freeze

  EXPECTED_PROFILE_CONTRACTS = {
    "windows-x64-development" => {
      "assetPath" => "Project hotfix/Assets/Settings/Build Profiles/Windows x64 Development.asset",
      "displayName" => "Windows x64 Development",
      "developmentBuild" => true,
      "customDefines" => ["PROJECTHOTFIX_BUILD_DEVELOPMENT"],
      "unityDerivedDefines" => ["DEVELOPMENT_BUILD"],
      "manualBuildPolicy" => "USER_ONLY",
      "outputPath" => "Builds/Windows/Development/Project Hotfix.exe",
    },
    "windows-x64-steam-reserved" => {
      "assetPath" => "Project hotfix/Assets/Settings/Build Profiles/Windows x64 Steam Reserved.asset",
      "displayName" => "Windows x64 Steam Reserved",
      "developmentBuild" => false,
      "customDefines" => ["PROJECTHOTFIX_BUILD_STEAM_RESERVED"],
      "unityDerivedDefines" => [],
      "manualBuildPolicy" => "BLOCKED_UNTIL_STM_001",
      "outputPath" => "Builds/Windows/Steam/Project Hotfix.exe",
    },
  }.freeze

  POLICY_FIELDS = %w[
    schemaVersion profileId ownerTask status approvedAt approvalBasis toolchain
    sharedPlayerSettings sceneList profiles steamBoundary executionBoundary
    prohibited limitations
  ].sort.freeze
  TOOLCHAIN_FIELDS = %w[
    unityEditorVersion unityEditorRevision target subtarget architecture scriptingBackend
    requiredModule moduleInstalledAtBld001 serverModuleRequired automaticPlayerBuildAllowed
  ].sort.freeze
  IDENTITY_FIELDS = %w[
    scope valueStatus companyName productName standaloneApplicationIdentifier bundleVersion
    profilePlayerSettingsOverridesAllowed migrationWarning
  ].sort.freeze
  SCENE_LIST_FIELDS = %w[
    source profileOverrideEnabled status releaseReady scenes replacementOwner
  ].sort.freeze
  SCENE_FIELDS = %w[path guid enabled].sort.freeze
  PROFILE_FIELDS = %w[
    id assetPath assetGuid displayName developmentBuild customDefines unityDerivedDefines
    connectProfiler deepProfiling scriptDebugging waitForManagedDebugger compression
    qualityOverride graphicsOverride manualBuildPolicy outputPath
  ].sort.freeze
  STEAM_FIELDS = %w[
    integrationStatus appId appIdOwner wrapperOwner steamSdkPresent steamAppIdFileAllowed
    functionalityClaimed
  ].sort.freeze
  EXECUTION_FIELDS = %w[
    playerBuildsExecutedByBld001 buildAndRunExecutedByBld001 steamDeploymentsExecutedByBld001
    dockerExecutionsByBld001 trackedPlayerOutputsAllowed manualGuidePath
    postBuildLicenseAuditOwners
  ].sort.freeze

  SCENE_CONTRACT = {
    "path" => "Assets/Scenes/SampleScene.unity",
    "guid" => "99c9720ab356a0642a771bea13969a05",
    "enabled" => true,
  }.freeze

  PROFILE_CLASS_MARKER = "UnityEditor.dll::UnityEditor.Build.Profile.BuildProfile"
  SOURCE_SCAN_EXTENSIONS = %w[
    .asset .bat .buildprofile .cmd .cs .js .mjs .ps1 .py .rb .sh .ts .yaml .yml .zsh
  ].to_set.freeze
  CONTENT_SCAN_EXCLUSIONS = %w[
    tools/verify_build_profiles.rb
    tools/tests/verify_build_profiles_test.rb
  ].to_set.freeze

  def initialize(root, verify_local_windows_module: false)
    @root = Pathname.new(root).expand_path
    @verify_local_windows_module = verify_local_windows_module
    @violations = []
    @violation_keys = Set.new
    @active_paths = Set.new
    @tracked_paths = Set.new
    @loaded_text = {}
    @policy = nil
    @toolchain = nil
    @profiles_checked = 0
    @forbidden_paths = Set.new
    @local_module_verified = false
  end

  def run
    load_git_inventory
    policy_document = load_yaml(POLICY_PATH, "POLICY")
    toolchain_document = load_yaml(TOOLCHAIN_PATH, "TOOLCHAIN")
    @policy = policy_document && policy_document["BuildProfilePolicy"]
    @toolchain = toolchain_document && toolchain_document["ToolchainProfile"]

    validate_policy_schema(policy_document)
    validate_toolchain_and_package_hashes
    validate_player_settings
    validate_profiles
    validate_global_scene
    validate_manual_guide
    validate_prohibited_artifacts
    validate_local_windows_module if @verify_local_windows_module
    print_report
    @violations.empty? ? 0 : 1
  end

  private

  def validate_policy_schema(document)
    expect(document.is_a?(Hash) && document.keys == ["BuildProfilePolicy"],
      "POLICY_DOCUMENT_ROOT_SET", POLICY_PATH)
    unless @policy.is_a?(Hash)
      add("POLICY_ROOT_INVALID", POLICY_PATH)
      return
    end

    expect(@policy.keys.sort == POLICY_FIELDS, "POLICY_FIELD_SET", POLICY_PATH)
    expect(@policy["schemaVersion"] == 1, "POLICY_SCHEMA_VERSION", POLICY_PATH)
    expect(@policy["profileId"] == "project-hotfix-windows-build-profile-r01",
      "POLICY_PROFILE_ID", POLICY_PATH)
    expect(@policy["ownerTask"] == "BLD-001", "POLICY_OWNER", POLICY_PATH)
    expect(@policy["status"] == "START_PROFILES_READY_PLAYER_NOT_BUILT",
      "POLICY_STATUS", POLICY_PATH)
    expect(nonempty_string?(@policy["approvedAt"]), "POLICY_APPROVED_AT", POLICY_PATH)
    expect(nonempty_string?(@policy["approvalBasis"]), "POLICY_APPROVAL_BASIS", POLICY_PATH)

    validate_toolchain_policy(@policy["toolchain"])
    validate_identity_policy(@policy["sharedPlayerSettings"])
    validate_scene_policy(@policy["sceneList"])
    validate_profile_policy(@policy["profiles"])
    validate_steam_policy(@policy["steamBoundary"])
    validate_execution_policy(@policy["executionBoundary"])
    expect(nonempty_array?(@policy["prohibited"]), "POLICY_PROHIBITED", POLICY_PATH)
    expect(nonempty_array?(@policy["limitations"]), "POLICY_LIMITATIONS", POLICY_PATH)
  end

  def validate_toolchain_policy(toolchain)
    valid = toolchain.is_a?(Hash)
    expect(valid, "POLICY_TOOLCHAIN_SECTION", POLICY_PATH)
    return unless valid

    expect(toolchain.keys.sort == TOOLCHAIN_FIELDS, "POLICY_TOOLCHAIN_FIELD_SET", POLICY_PATH)
    expected = {
      "target" => "StandaloneWindows64",
      "subtarget" => "Player",
      "architecture" => "x64",
      "scriptingBackend" => "Mono2x",
      "requiredModule" => "Windows Build Support (Mono)",
      "moduleInstalledAtBld001" => true,
      "serverModuleRequired" => false,
      "automaticPlayerBuildAllowed" => false,
    }
    expected.each do |field, value|
      expect(toolchain[field] == value, "POLICY_TOOLCHAIN_#{field.upcase}", POLICY_PATH)
    end
    expect(nonempty_string?(toolchain["unityEditorVersion"]),
      "POLICY_TOOLCHAIN_UNITY_VERSION", POLICY_PATH)
    expect(nonempty_string?(toolchain["unityEditorRevision"]),
      "POLICY_TOOLCHAIN_UNITY_REVISION", POLICY_PATH)
  end

  def validate_identity_policy(identity)
    valid = identity.is_a?(Hash)
    expect(valid, "POLICY_IDENTITY_SECTION", POLICY_PATH)
    return unless valid

    expect(identity.keys.sort == IDENTITY_FIELDS, "POLICY_IDENTITY_FIELD_SET", POLICY_PATH)
    expect(identity["scope"] == "GLOBAL", "POLICY_IDENTITY_SCOPE", POLICY_PATH)
    expect(identity["valueStatus"] == "TEMPORARY_USER_APPROVED",
      "POLICY_IDENTITY_STATUS", POLICY_PATH)
    TEMPORARY_IDENTITY.each do |field, value|
      expect(identity[field] == value, "POLICY_IDENTITY_#{field.upcase}", POLICY_PATH)
    end
    expect(identity["profilePlayerSettingsOverridesAllowed"] == false,
      "POLICY_IDENTITY_PROFILE_OVERRIDE", POLICY_PATH)
    expect(nonempty_string?(identity["migrationWarning"]),
      "POLICY_IDENTITY_MIGRATION_WARNING", POLICY_PATH)
  end

  def validate_scene_policy(scene_list)
    valid = scene_list.is_a?(Hash)
    expect(valid, "POLICY_SCENE_SECTION", POLICY_PATH)
    return unless valid

    expect(scene_list.keys.sort == SCENE_LIST_FIELDS, "POLICY_SCENE_FIELD_SET", POLICY_PATH)
    expect(scene_list["source"] == "GLOBAL_EDITOR_BUILD_SETTINGS",
      "POLICY_SCENE_SOURCE", POLICY_PATH)
    expect(scene_list["profileOverrideEnabled"] == false,
      "POLICY_SCENE_PROFILE_OVERRIDE", POLICY_PATH)
    expect(scene_list["status"] == "START_PLACEHOLDER", "POLICY_SCENE_STATUS", POLICY_PATH)
    expect(scene_list["releaseReady"] == false, "POLICY_SCENE_RELEASE_READY", POLICY_PATH)
    scenes = scene_list["scenes"]
    valid_scenes = scenes.is_a?(Array) && scenes.length == 1 && scenes.first.is_a?(Hash) &&
      scenes.first.keys.sort == SCENE_FIELDS && scenes.first == SCENE_CONTRACT
    expect(valid_scenes, "POLICY_SCENE_EXACT_SET", POLICY_PATH)
    expect(nonempty_string?(scene_list["replacementOwner"]),
      "POLICY_SCENE_REPLACEMENT_OWNER", POLICY_PATH)
  end

  def validate_profile_policy(profiles)
    unless profiles.is_a?(Array)
      add("POLICY_PROFILE_SECTION", POLICY_PATH)
      return
    end
    expect(profiles.length == EXPECTED_PROFILE_CONTRACTS.length,
      "POLICY_PROFILE_COUNT", POLICY_PATH)
    ids = profiles.map { |profile| profile["id"] if profile.is_a?(Hash) }.compact
    expect(ids.sort == EXPECTED_PROFILE_CONTRACTS.keys.sort && ids.uniq.length == ids.length,
      "POLICY_PROFILE_ID_SET", POLICY_PATH)

    profiles.each do |profile|
      unless profile.is_a?(Hash)
        add("POLICY_PROFILE_ENTRY", POLICY_PATH)
        next
      end
      expect(profile.keys.sort == PROFILE_FIELDS, "POLICY_PROFILE_FIELD_SET", POLICY_PATH)
      expected = EXPECTED_PROFILE_CONTRACTS[profile["id"]]
      unless expected
        add("POLICY_PROFILE_ID_SET", POLICY_PATH)
        next
      end
      expected.each do |field, value|
        expect(profile[field] == value, "POLICY_PROFILE_#{field.upcase}", POLICY_PATH)
      end
      expect(profile["assetGuid"].is_a?(String) && profile["assetGuid"].match?(/\A[0-9a-f]{32}\z/),
        "POLICY_PROFILE_GUID", POLICY_PATH)
      %w[connectProfiler deepProfiling scriptDebugging waitForManagedDebugger qualityOverride graphicsOverride].each do |field|
        expect(profile[field] == false, "POLICY_PROFILE_#{field.upcase}", POLICY_PATH)
      end
      expect(profile["compression"] == "Default", "POLICY_PROFILE_COMPRESSION", POLICY_PATH)
    end
    guids = profiles.map { |profile| profile["assetGuid"] if profile.is_a?(Hash) }.compact
    expect(guids.length == EXPECTED_PROFILE_CONTRACTS.length && guids.uniq.length == guids.length,
      "POLICY_PROFILE_GUID_UNIQUE", POLICY_PATH)
  end

  def validate_steam_policy(steam)
    valid = steam.is_a?(Hash)
    expect(valid, "POLICY_STEAM_SECTION", POLICY_PATH)
    return unless valid

    expect(steam.keys.sort == STEAM_FIELDS, "POLICY_STEAM_FIELD_SET", POLICY_PATH)
    expected = {
      "integrationStatus" => "RESERVED_NOT_IMPLEMENTED",
      "appId" => nil,
      "appIdOwner" => "STM-001",
      "wrapperOwner" => "STM-001",
      "steamSdkPresent" => false,
      "steamAppIdFileAllowed" => false,
      "functionalityClaimed" => false,
    }
    expect(steam == expected, "POLICY_STEAM_BOUNDARY", POLICY_PATH)
  end

  def validate_execution_policy(execution)
    valid = execution.is_a?(Hash)
    expect(valid, "POLICY_EXECUTION_SECTION", POLICY_PATH)
    return unless valid

    expect(execution.keys.sort == EXECUTION_FIELDS, "POLICY_EXECUTION_FIELD_SET", POLICY_PATH)
    %w[playerBuildsExecutedByBld001 buildAndRunExecutedByBld001 steamDeploymentsExecutedByBld001 dockerExecutionsByBld001].each do |field|
      expect(execution[field] == 0, "POLICY_EXECUTION_ZERO", POLICY_PATH)
    end
    expect(execution["trackedPlayerOutputsAllowed"] == false,
      "POLICY_TRACKED_OUTPUTS", POLICY_PATH)
    expect(execution["manualGuidePath"] == GUIDE_PATH, "POLICY_GUIDE_PATH", POLICY_PATH)
    expect(execution["postBuildLicenseAuditOwners"] == %w[BLD-001 ALP-001],
      "POLICY_LICENSE_AUDIT_OWNERS", POLICY_PATH)
  end

  def validate_toolchain_and_package_hashes
    unless @toolchain.is_a?(Hash)
      add("TOOLCHAIN_ROOT_INVALID", TOOLCHAIN_PATH)
      return
    end
    toolchain = @policy.is_a?(Hash) ? @policy["toolchain"] : nil
    if toolchain.is_a?(Hash)
      expect(toolchain["unityEditorVersion"] == @toolchain.dig("unity", "editorVersion"),
        "TOOLCHAIN_UNITY_VERSION_DRIFT", TOOLCHAIN_PATH)
      expect(toolchain["unityEditorRevision"] == @toolchain.dig("unity", "editorRevision"),
        "TOOLCHAIN_UNITY_REVISION_DRIFT", TOOLCHAIN_PATH)
    end

    baseline = @toolchain["packageBaseline"]
    unless baseline.is_a?(Hash)
      add("TOOLCHAIN_PACKAGE_BASELINE", TOOLCHAIN_PATH)
      return
    end
    verify_package_hash(baseline, "manifestPath", "manifestSha256", MANIFEST_PATH, "MANIFEST")
    verify_package_hash(baseline, "lockPath", "lockSha256", LOCK_PATH, "LOCK")
  end

  def verify_package_hash(baseline, path_field, hash_field, expected_path, kind)
    expect(baseline[path_field] == expected_path, "#{kind}_PATH", TOOLCHAIN_PATH)
    text = load_text(expected_path, kind, MAX_JSON_BYTES)
    return unless text

    begin
      document = JSON.parse(text, object_class: BuildProfileUniqueJsonHash)
      expect(document.is_a?(Hash), "#{kind}_JSON_INVALID", expected_path)
      @package_documents ||= {}
      @package_documents[expected_path] = document
    rescue JSON::ParserError, BuildProfileDuplicateJsonKeyError
      add("#{kind}_JSON_INVALID", expected_path)
    end
    expected_hash = baseline[hash_field]
    valid_hash = expected_hash.is_a?(String) && expected_hash.match?(/\A[0-9a-f]{64}\z/) &&
      Digest::SHA256.hexdigest(text.b) == expected_hash
    expect(valid_hash, "#{kind}_SHA256_DRIFT", expected_path)
  end

  def validate_player_settings
    text = load_text(PLAYER_SETTINGS_PATH, "PLAYER_SETTINGS", MAX_UNITY_TEXT_BYTES)
    return unless text

    expected = @policy.is_a?(Hash) ? @policy["sharedPlayerSettings"] : nil
    expected = TEMPORARY_IDENTITY unless expected.is_a?(Hash)
    validate_unique_scalar(text, 2, "companyName", expected["companyName"],
      "PLAYER_COMPANY_NAME", PLAYER_SETTINGS_PATH)
    validate_unique_scalar(text, 2, "productName", expected["productName"],
      "PLAYER_PRODUCT_NAME", PLAYER_SETTINGS_PATH)
    validate_unique_scalar(text, 2, "bundleVersion", expected["bundleVersion"],
      "PLAYER_BUNDLE_VERSION", PLAYER_SETTINGS_PATH)
    validate_mapping_scalar(text, "applicationIdentifier", "Standalone",
      expected["standaloneApplicationIdentifier"], "PLAYER_STANDALONE_IDENTIFIER")
  end

  def validate_profiles
    expected_assets = EXPECTED_PROFILE_CONTRACTS.values.map { |contract| contract["assetPath"] }.to_set
    expected_members = expected_assets.flat_map { |path| [path, "#{path}.meta"] }.to_set
    directory_prefix = "Project hotfix/Assets/Settings/Build Profiles/"
    actual_members = @active_paths.select { |path| path.start_with?(directory_prefix) }.to_set
    expect(actual_members == expected_members, "PROFILE_DIRECTORY_EXACT_SET",
      "Project hotfix/Assets/Settings/Build Profiles")

    discovered = Set.new
    @active_paths.each do |path|
      if path.downcase.end_with?(".buildprofile")
        discovered << path
      elsif path.downcase.end_with?(".asset")
        absolute = safe_regular_file(path, "PROFILE_DISCOVERY", Float::INFINITY, inventory_required: true)
        next unless absolute
        prefix = absolute.open("rb") { |file| file.read(64 * 1024) || "" }
        discovered << path if prefix.include?(PROFILE_CLASS_MARKER)
      end
    end
    expect(discovered == expected_assets, "CUSTOM_PROFILE_EXACT_SET", "Project hotfix/Assets")

    profiles = @policy.is_a?(Hash) && @policy["profiles"].is_a?(Array) ? @policy["profiles"] : []
    by_id = profiles.select { |entry| entry.is_a?(Hash) }.to_h { |entry| [entry["id"], entry] }
    actual_guids = []
    EXPECTED_PROFILE_CONTRACTS.each do |id, contract|
      policy_profile = by_id[id]
      path = contract["assetPath"]
      text = load_text(path, "PROFILE_ASSET", MAX_UNITY_TEXT_BYTES)
      meta = load_text("#{path}.meta", "PROFILE_META", MAX_YAML_BYTES)
      next unless text && meta && policy_profile

      @profiles_checked += 1
      expect(text.include?(PROFILE_CLASS_MARKER), "PROFILE_CLASS_MARKER", path)
      validate_unique_scalar(text, 2, "m_Name", contract["displayName"], "PROFILE_DISPLAY_NAME", path)
      validate_unique_scalar(text, 2, "m_OverrideGlobalSceneList", "0", "PROFILE_GLOBAL_SCENE_LIST", path)
      validate_unique_scalar(text, 2, "m_Scenes", "[]", "PROFILE_SCENE_OVERRIDE_EMPTY", path)
      validate_profile_defines(text, contract["customDefines"], path)
      validate_unique_scalar(text, 4, "m_Settings", "[]", "PROFILE_PLAYER_SETTINGS_EMPTY", path)
      expect(!text.match?(/^\s*m_(Quality|Graphics)Settings\s*:/),
        "PROFILE_SETTINGS_OVERRIDE_PRESENT", path)

      serialized = {
        "m_Development" => contract["developmentBuild"] ? "1" : "0",
        "m_ConnectProfiler" => "0",
        "m_BuildWithDeepProfilingSupport" => "0",
        "m_AllowDebugging" => "0",
        "m_WaitForManagedDebugger" => "0",
      }
      serialized.each do |field, value|
        validate_unique_scalar(text, 8, field, value, "PROFILE_PLATFORM_#{field.upcase}", path)
      end
      expect(text.scan(/UnityEditor\.WindowsStandalone\.Extensions/).length == 1,
        "PROFILE_WINDOWS_PLATFORM_REFERENCE", path)

      guid_matches = meta.scan(/^guid:\s*([0-9a-f]{32})\s*$/).flatten
      expect(guid_matches.length == 1, "PROFILE_META_GUID", "#{path}.meta")
      if guid_matches.length == 1
        actual_guids << guid_matches.first
        expect(guid_matches.first == policy_profile["assetGuid"],
          "PROFILE_META_GUID_DRIFT", "#{path}.meta")
      end
    end
    expect(actual_guids.length == EXPECTED_PROFILE_CONTRACTS.length &&
      actual_guids.uniq.length == actual_guids.length,
      "PROFILE_META_GUID_UNIQUE", "Project hotfix/Assets/Settings/Build Profiles")
  end

  def validate_profile_defines(text, expected, path)
    lines = text.lines
    indexes = lines.each_index.select { |index| lines[index] == "  m_ScriptingDefines:\n" }
    if indexes.length != 1
      add("PROFILE_DEFINE_SECTION", path)
      return
    end
    values = []
    index = indexes.first + 1
    while index < lines.length && lines[index].start_with?("  - ")
      values << lines[index].sub("  - ", "").strip
      index += 1
    end
    expect(values == expected, "PROFILE_DEFINE_EXACT_SET", path)
  end

  def validate_global_scene
    scene_list = @policy.is_a?(Hash) ? @policy["sceneList"] : nil
    scene = scene_list.is_a?(Hash) && scene_list["scenes"].is_a?(Array) ? scene_list["scenes"].first : SCENE_CONTRACT
    scene = SCENE_CONTRACT unless scene.is_a?(Hash)
    text = load_text(EDITOR_BUILD_SETTINGS_PATH, "EDITOR_BUILD_SETTINGS", MAX_UNITY_TEXT_BYTES)
    if text
      entry_pattern = /^  - enabled:\s*([01])\s*\n    path:\s*(\S+)\s*\n    guid:\s*([0-9a-f]{32})\s*$/
      entries = text.scan(entry_pattern).map do |enabled, path, guid|
        { "enabled" => enabled == "1", "path" => path, "guid" => guid }
      end
      declared_entries = text.scan(/^  - enabled:/).length
      valid = declared_entries == 1 && entries.length == 1 && entries.first == scene
      expect(valid, "GLOBAL_SCENE_EXACT_SET", EDITOR_BUILD_SETTINGS_PATH)
    end

    unity_path = "Project hotfix/#{scene["path"]}"
    scene_asset = safe_regular_file(unity_path, "SCENE_ASSET", MAX_UNITY_TEXT_BYTES)
    meta = load_text("#{unity_path}.meta", "SCENE_META", MAX_YAML_BYTES)
    expect(scene_asset&.file?, "SCENE_ASSET_PRESENT", unity_path)
    if meta
      guids = meta.scan(/^guid:\s*([0-9a-f]{32})\s*$/).flatten
      expect(guids == [scene["guid"]], "SCENE_META_GUID", "#{unity_path}.meta")
    end
  end

  def validate_manual_guide
    guide_path = @policy.is_a?(Hash) ? @policy.dig("executionBoundary", "manualGuidePath") : GUIDE_PATH
    unless guide_path == GUIDE_PATH
      add("GUIDE_POLICY_PATH", POLICY_PATH)
      guide_path = GUIDE_PATH
    end
    text = load_text(guide_path, "GUIDE", MAX_GUIDE_BYTES)
    return unless text

    markers = [
      "BLD-001",
      "Player Build 실행 주체: 사용자만",
      "BLD-001 자동 Build/Build And Run/배포 실행: `0`",
      "START_PLACEHOLDER",
      "ruby tools/verify_build_profiles.rb --verify-local-windows-module",
      "Build And Run",
      "steam_appid.txt",
      "STM-001",
    ]
    if @policy.is_a?(Hash)
      markers.concat([
        @policy.dig("toolchain", "unityEditorVersion"),
        @policy.dig("toolchain", "requiredModule"),
        @policy.dig("sceneList", "scenes", 0, "path"),
      ])
      Array(@policy["profiles"]).each do |profile|
        next unless profile.is_a?(Hash)
        markers.concat([
          profile["displayName"],
          *Array(profile["customDefines"]),
          profile["outputPath"],
        ])
      end
    end
    markers.compact.uniq.each do |marker|
      expect(text.include?(marker), "GUIDE_REQUIRED_MARKER", guide_path)
    end
  end

  def validate_prohibited_artifacts
    @tracked_paths.each do |path|
      if path.match?(%r{(?:\A|/)(?:build|builds)/}i)
        add("TRACKED_BUILD_OUTPUT", path)
        @forbidden_paths << path
      end
    end

    @active_paths.each do |path|
      lower = path.downcase
      if ci_or_cloud_build_path?(lower)
        add("CI_OR_CLOUD_BUILD_ARTIFACT", path)
        @forbidden_paths << path
      end
      if steam_sdk_path?(lower)
        add("STEAM_SDK_OR_APPID_FILE", path)
        @forbidden_paths << path
      end
      next unless source_scan_path?(path)

      absolute = safe_regular_file(path, "AUTOMATION_SCAN", MAX_SCAN_BYTES, inventory_required: true)
      next unless absolute
      text = absolute.binread.force_encoding(Encoding::UTF_8)
      unless text.valid_encoding?
        add("AUTOMATION_SCAN_INVALID_UTF8", path)
        next
      end
      if text.match?(/BuildPipeline\s*\.\s*BuildPlayer/)
        add("AUTOMATIC_BUILD_ENTRYPOINT", path)
        @forbidden_paths << path
      end
      if text.match?(/\bUNITY_SERVER\b|BuildOptions\s*\.\s*EnableHeadlessMode|StandaloneBuildSubtarget\s*\.\s*Server|-standaloneBuildSubtarget\s+Server/i)
        add("SERVER_OR_HEADLESS_BUILD_SIGNATURE", path)
        @forbidden_paths << path
      end
    end

    (@package_documents || {}).each do |path, document|
      dependencies = document.is_a?(Hash) ? document["dependencies"] : nil
      next unless dependencies.is_a?(Hash)
      steam_present = dependencies.any? do |package_id, value|
        package_id.to_s.match?(/steamworks|steam[-_.]?sdk/i) ||
          value.to_s.match?(/steamworks|steam[-_.]?sdk/i)
      end
      if steam_present
        add("STEAM_SDK_PACKAGE", path)
        @forbidden_paths << path
      end
    end
  end

  def ci_or_cloud_build_path?(lower)
    lower.start_with?(".github/workflows/", ".circleci/", ".buildkite/") ||
      lower.match?(%r{(?:\A|/)(?:jenkinsfile|\.gitlab-ci\.ya?ml|azure-pipelines(?:\.[^/]+)?\.ya?ml|cloudbuild\.ya?ml)\z})
  end

  def steam_sdk_path?(lower)
    lower.match?(%r{(?:\A|/)(?:steam_appid\.txt|(?:lib)?steam_api(?:64)?\.(?:dll|dylib|lib|so)|steamworks(?:\.net)?\.(?:dll|dylib|so|unitypackage))\z}) ||
      lower.match?(%r{(?:\A|/)(?:steamworks[._-]?sdk)(?:/|\z)})
  end

  def source_scan_path?(path)
    return false if CONTENT_SCAN_EXCLUSIONS.include?(path)
    return false if path.start_with?("tools/tests/")
    return false if path.start_with?("docs/", "artifacts/", "config/")

    SOURCE_SCAN_EXTENSIONS.include?(File.extname(path).downcase)
  end

  def validate_local_windows_module
    return unless @policy.is_a?(Hash) && @toolchain.is_a?(Hash)

    version = @policy.dig("toolchain", "unityEditorVersion")
    base = ENV["PROJECT_HOTFIX_UNITY_EDITOR_ROOT"]
    candidates = if nonempty_string?(base)
      [Pathname.new(base).expand_path]
    else
      editor_root = Pathname.new("/Applications/Unity/Hub/Editor").join(version.to_s)
      [editor_root, editor_root.join("Unity.app/Contents")]
    end
    required = %w[
      modules.asset
      UnityEditor.WindowsStandalone.Extensions.dll
      Variations/win64_player_development_mono/WindowsPlayer.exe
      Variations/win64_player_nondevelopment_mono/WindowsPlayer.exe
    ]
    module_root = candidates.map { |candidate| candidate.join("PlaybackEngines/WindowsStandaloneSupport") }
      .find { |candidate| candidate.directory? && !candidate.symlink? }
    unless module_root
      add("LOCAL_WINDOWS_MODULE_MISSING", version.to_s)
      return
    end
    valid = required.all? do |relative|
      path = module_root.join(relative)
      path.file? && !path.symlink? && path.size.positive?
    end
    expect(valid, "LOCAL_WINDOWS_MONO_VARIATIONS", module_root.to_s)
    @local_module_verified = valid
  end

  def validate_unique_scalar(text, indent, key, expected, rule, path)
    pattern = /^#{" " * indent}#{Regexp.escape(key)}:\s*(.*?)\s*$/
    values = text.scan(pattern).flatten
    expect(values == [expected.to_s], rule, path)
  end

  def validate_mapping_scalar(text, section, key, expected, rule)
    lines = text.lines
    headers = lines.each_index.select { |index| lines[index] == "  #{section}:\n" }
    unless headers.length == 1
      add(rule, PLAYER_SETTINGS_PATH)
      return
    end
    values = []
    index = headers.first + 1
    while index < lines.length && (lines[index].strip.empty? || lines[index].start_with?("    "))
      match = lines[index].match(/^    #{Regexp.escape(key)}:\s*(.*?)\s*$/)
      values << match[1] if match
      index += 1
    end
    expect(values == [expected.to_s], rule, PLAYER_SETTINGS_PATH)
  end

  def load_git_inventory
    inventory, inventory_error, inventory_status = Open3.capture3(
      "git", "-C", @root.to_s, "ls-files", "--cached", "--others", "--exclude-standard", "-z"
    )
    tracked, tracked_error, tracked_status = Open3.capture3(
      "git", "-C", @root.to_s, "ls-files", "--cached", "-z"
    )
    deleted, deleted_error, deleted_status = Open3.capture3(
      "git", "-C", @root.to_s, "ls-files", "--deleted", "-z"
    )
    unless inventory_status.success? && tracked_status.success? && deleted_status.success? &&
      inventory_error.empty? && tracked_error.empty? && deleted_error.empty?
      raise BuildProfileAuditError, "GIT_INVENTORY_FAILED"
    end
    deleted_paths = decode_git_paths(deleted).to_set
    @active_paths = (decode_git_paths(inventory) - deleted_paths.to_a).to_set
    @tracked_paths = (decode_git_paths(tracked) - deleted_paths.to_a).to_set
  end

  def decode_git_paths(bytes)
    bytes.b.split("\0").reject(&:empty?).map do |raw|
      value = raw.dup.force_encoding(Encoding::UTF_8)
      raise BuildProfileAuditError, "INVALID_GIT_PATH" unless value.valid_encoding?
      normalized = value.tr("\\", "/")
      raise BuildProfileAuditError, "INVALID_GIT_PATH" unless repository_relative_path?(normalized)
      normalized
    end.uniq
  end

  def load_yaml(relative, kind)
    text = load_text(relative, kind, MAX_YAML_BYTES)
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

  def load_text(relative, kind, max_bytes)
    absolute = safe_regular_file(relative, kind, max_bytes)
    return nil unless absolute

    bytes = absolute.binread
    text = bytes.dup.force_encoding(Encoding::UTF_8)
    unless text.valid_encoding?
      add("#{kind}_INVALID_UTF8", relative)
      return nil
    end
    @loaded_text[relative] = text
    text
  end

  def safe_regular_file(relative, kind, max_bytes, inventory_required: false)
    unless repository_relative_path?(relative)
      add("#{kind}_PATH_INVALID", relative.to_s)
      return nil
    end
    if inventory_required && !@active_paths.include?(relative)
      add("#{kind}_NOT_IN_INVENTORY", relative)
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
    if stat.size > max_bytes
      add("#{kind}_TOO_LARGE", relative)
      return nil
    end
    cursor
  end

  def repository_relative_path?(relative)
    return false unless relative.is_a?(String) && !relative.empty?
    return false if relative.start_with?("/", "\\") || relative.include?("\0")
    return false if relative.match?(/\A[A-Za-z]:[\\\/]/)
    parts = relative.split("/")
    !parts.empty? && parts.none? { |part| part.empty? || part == "." || part == ".." }
  end

  def nonempty_string?(value)
    value.is_a?(String) && !value.empty?
  end

  def nonempty_array?(value)
    value.is_a?(Array) && !value.empty? && value.all? { |entry| nonempty_string?(entry) }
  end

  def expect(condition, rule, path)
    add(rule, path) unless condition
  end

  def add(rule, path)
    safe_path = path.to_s.encode(Encoding::UTF_8, invalid: :replace, undef: :replace, replace: "?")
      .gsub(/[[:cntrl:]]/, "?")
    key = [rule, safe_path]
    return unless @violation_keys.add?(key)
    @violations << key
  end

  def print_report
    puts "WINDOWS_BUILD_PROFILE_AUDIT=BLD-001"
    puts "POLICY_LOADED=#{@policy.is_a?(Hash)}"
    puts "EXPECTED_PROFILE_COUNT=#{EXPECTED_PROFILE_CONTRACTS.length}"
    puts "PROFILES_CHECKED=#{@profiles_checked}"
    puts "RAW_TARGET_SUBTARGET_INTERPRETED=false"
    puts "PACKAGE_IDS_HARDCODED=0"
    puts "LOCAL_WINDOWS_MODULE_REQUESTED=#{@verify_local_windows_module}"
    puts "LOCAL_WINDOWS_MODULE_VERIFIED=#{@local_module_verified}" if @verify_local_windows_module
    puts "FORBIDDEN_ARTIFACT_FILES=#{@forbidden_paths.length}"
    puts "TOTAL_VIOLATIONS=#{@violations.length}"
    @violations.sort.each { |rule, path| puts "VIOLATION rule=#{rule} path=#{path}" }
    puts "FINAL_RESULT=#{@violations.empty? ? "PASS" : "FAIL"}"
  end
end

options = { verify_local_windows_module: false }
parser = OptionParser.new do |arguments|
  arguments.banner = "usage: ruby tools/verify_build_profiles.rb [--root PATH] [--verify-local-windows-module]"
  arguments.on("--root PATH", "Git worktree to audit") { |path| options[:root] = path }
  arguments.on("--verify-local-windows-module", "Verify the locally installed Windows x64 Mono player module") do
    options[:verify_local_windows_module] = true
  end
end

begin
  parser.parse!
  raise BuildProfileAuditError, "UNEXPECTED_ARGUMENT" unless ARGV.empty?
  default_root = Pathname.new(__dir__).join("..").expand_path
  root = Pathname.new(options.fetch(:root, default_root.to_s)).expand_path
  raise BuildProfileAuditError, "INVALID_ROOT" unless root.directory?
  raise BuildProfileAuditError, "ROOT_SYMLINK" if File.lstat(root).symlink?

  exit BuildProfileVerifier.new(
    root,
    verify_local_windows_module: options[:verify_local_windows_module],
  ).run
rescue OptionParser::ParseError
  warn "WINDOWS_BUILD_PROFILE_AUDIT=ERROR reason=USAGE"
  exit 2
rescue BuildProfileAuditError => error
  warn "WINDOWS_BUILD_PROFILE_AUDIT=ERROR reason=#{error.message}"
  exit 2
rescue StandardError
  warn "WINDOWS_BUILD_PROFILE_AUDIT=ERROR reason=UNEXPECTED"
  exit 2
end
