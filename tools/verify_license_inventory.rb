#!/usr/bin/env ruby

require "digest"
require "json"
require "open3"
require "optparse"
require "pathname"
require "set"
require "yaml"

class LicenseInventoryAuditError < StandardError; end
class DuplicateJsonKeyError < StandardError; end

class UniqueJsonHash < Hash
  def []=(key, value)
    raise DuplicateJsonKeyError, key if key?(key)

    super
  end
end

class LicenseInventoryVerifier
  POLICY_PATH = "config/licenses/LicensePolicy.yaml"
  INVENTORY_PATH = "config/licenses/ThirdPartyInventory.yaml"
  NOTICE_PATH = "THIRD_PARTY_NOTICES.md"
  BINARY_INVENTORY_PATH = "config/repository/BinaryAssetInventory.yaml"
  MANIFEST_PATH = "Project hotfix/Packages/manifest.json"
  LOCK_PATH = "Project hotfix/Packages/packages-lock.json"
  PACKAGE_CACHE_PATH = "Project hotfix/Library/PackageCache"
  C1B003_ROOT = "BlenderSource/Characters/C1B-003/"
  C1B003_MANIFEST_PATH = "#{C1B003_ROOT}GenerationManifest.yaml"
  C1B004_ROOT = "BlenderSource/Characters/C1B-004/"
  C1B004_MANIFEST_PATH = "#{C1B004_ROOT}GenerationManifest.yaml"
  C1B005_MANIFEST_PATH = "BlenderSource/Characters/C1B-005/GenerationManifest.yaml"
  C1B005_FBX_PATH = "Project hotfix/Assets/ProjectHotfix/Art/Characters/C1B-005/" \
    "CHR_MasterCharacter_C1B_Neutral_r02.fbx"
  C1B005_CAPTURE_ROOT = "artifacts/evidence/G0/C1B-005/Captures/Unity/"
  CHARACTER_PROFILE_ANCHOR = "config/character/CharacterProportionProfile.yaml#CharacterProportionProfile"
  C1BRW002_ROOT = "BlenderSource/Characters/C1B-RW-002/"
  C1BRW002_MANIFEST_PATH = "#{C1BRW002_ROOT}GenerationManifest.yaml"
  C1BRW_PROFILE_ANCHOR =
    "config/character/CharacterProportionProfile-C1B-RW-001-r01.yaml#CharacterProportionProfile"

  MAX_YAML_BYTES = 2 * 1024 * 1024
  MAX_JSON_BYTES = 4 * 1024 * 1024
  MAX_NOTICE_BYTES = 2 * 1024 * 1024
  MAX_EVIDENCE_BYTES = 64 * 1024 * 1024
  REQUIRED_PACKAGE_FIELDS = %w[
    packageId resolvedVersion source relationship usageClass licenseFamily
    noticeDisposition sourceEvidence
  ].freeze
  PACKAGE_ENTRY_FIELDS = (REQUIRED_PACKAGE_FIELDS + %w[
    shippingNoticeCandidate perPackageNoticePresent
  ]).sort.freeze
  REVIEW_ENTRY_FIELDS = %w[
    path sha256 contentCredentialStatus intendedUse licenseFamily rightsStatus
    noticeDisposition reviewOnly shippingAllowed sourceEvidence
  ].sort.freeze
  FIRST_PARTY_ENTRY_FIELDS = %w[
    path sha256 assetType sourceOwner sourceStatus intendedUse licenseFamily
    rightsStatus noticeDisposition reviewOnly shippingAllowed sourceEvidence
  ].freeze

  MEDIA_EXTENSIONS = %w[
    .png .jpg .jpeg .gif .bmp .tga .tif .tiff .psd .exr .hdr .svg .webp .ico
    .ttf .otf .woff .woff2
    .wav .mp3 .ogg .flac .aac .m4a .aiff .mp4 .mov .avi .webm
    .blend .fbx .glb .gltf .obj .dae .3ds
    .shader .shadergraph .shadersubgraph .compute .hlsl .cginc .glsl
    .dll .so .dylib .bundle .aar .jar
    .wlt .unitypackage .zip .7z .rar
  ].to_set.freeze

  POLICY_TOP_LEVEL_FIELDS = %w[
    schemaVersion profileId status approvedAtUtc approvalBasis scope sourceOfTruth
    evaluationOrder officialUnityPackageRule generallyAllowedWithObligations
    blockedFamilies newExternalAssetRule firstPartyProductionAssetRule
    reviewOnlyContentRule evidenceRules releaseAudit limitations
  ].sort.freeze
  INVENTORY_TOP_LEVEL_FIELDS = %w[
    schemaVersion inventoryId status recordedAtUtc policyPath distributedNoticeIndex
    packageBaseline packages firstPartyProductionAssets reviewOnlyAssets auditNotes
  ].sort.freeze
  NOTICE_MARKERS = [
    "# Third-Party Source Inventory and Notice Index",
    "`LIC-001`",
    "## Runtime and build candidates",
    "### Unity built-in engine modules",
    "## Editor and test inventory",
    "## Review-only C2PA images",
    "`shippingAllowed: false`",
    "## Final Windows Player audit",
    "`BLD-001`",
    "`ALP-001`",
    "Automatic Build remains prohibited",
  ].freeze

  def initialize(root, verify_package_cache: false)
    @root = Pathname.new(root).expand_path
    @verify_package_cache = verify_package_cache
    @violations = []
    @violation_keys = Set.new
    @active_paths = Set.new
    @deleted_paths = Set.new
    @loaded_bytes = {}
    @package_cache_matches = 0
    @package_cache_locators = 0
  end

  def run
    load_git_inventory
    @policy_document = load_yaml(POLICY_PATH, "POLICY")
    @inventory_document = load_yaml(INVENTORY_PATH, "INVENTORY")
    @binary_document = load_yaml(BINARY_INVENTORY_PATH, "BINARY_INVENTORY")
    @manifest_document = load_json(MANIFEST_PATH, "MANIFEST")
    @lock_document = load_json(LOCK_PATH, "LOCK")
    @notice_text = load_text(NOTICE_PATH, "NOTICE", MAX_NOTICE_BYTES)

    validate_policy
    validate_package_documents
    validate_inventory
    validate_binary_inventory
    validate_repository_assets
    validate_notice
    validate_package_cache if @verify_package_cache
    print_report
    @violations.empty? ? 0 : 1
  end

  private

  def add(rule, path)
    normalized = path.to_s.tr("\\", "/")
    key = [rule, normalized]
    return if @violation_keys.include?(key)

    @violation_keys << key
    @violations << key
  end

  def expect(condition, rule, path)
    add(rule, path) unless condition
  end

  def load_git_inventory
    tracked, tracked_error, tracked_status = Open3.capture3(
      "git", "-C", @root.to_s, "ls-files", "--cached", "--others", "--exclude-standard", "-z"
    )
    deleted, deleted_error, deleted_status = Open3.capture3(
      "git", "-C", @root.to_s, "ls-files", "--deleted", "-z"
    )
    unless tracked_status.success? && deleted_status.success?
      raise LicenseInventoryAuditError, "GIT_INVENTORY_FAILED"
    end
    raise LicenseInventoryAuditError, "GIT_INVENTORY_FAILED" unless tracked_error.empty? && deleted_error.empty?

    tracked_paths = decode_git_paths(tracked)
    @deleted_paths = decode_git_paths(deleted).to_set
    @active_paths = (tracked_paths - @deleted_paths.to_a).to_set
    @active_paths.each do |relative|
      absolute = @root.join(relative)
      begin
        stat = File.lstat(absolute)
        add("GIT_ACTIVE_SYMLINK", relative) if stat.symlink?
      rescue Errno::ENOENT, Errno::ENOTDIR
        add("GIT_ACTIVE_PATH_MISSING", relative)
      end
    end
  end

  def decode_git_paths(bytes)
    bytes.b.split("\0").reject(&:empty?).map do |raw|
      path = raw.dup.force_encoding(Encoding::UTF_8)
      raise LicenseInventoryAuditError, "INVALID_GIT_PATH" unless path.valid_encoding?

      normalized = path.tr("\\", "/")
      raise LicenseInventoryAuditError, "INVALID_GIT_PATH" unless repository_relative_path?(normalized)

      normalized
    end.uniq
  end

  def repository_relative_path?(relative)
    return false unless relative.is_a?(String) && !relative.empty?
    return false if relative.start_with?("/", "\\") || relative.include?("\0")
    return false if relative.match?(/\A[A-Za-z]:[\\\/]/)

    parts = relative.split("/")
    !parts.empty? && parts.none? { |part| part.empty? || part == "." || part == ".." }
  end

  def safe_regular_file(relative, kind, max_bytes)
    unless repository_relative_path?(relative)
      add("#{kind}_PATH_INVALID", relative)
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

  def load_text(relative, kind, max_bytes)
    absolute = safe_regular_file(relative, kind, max_bytes)
    return nil unless absolute

    bytes = absolute.binread
    @loaded_bytes[relative] = bytes
    text = bytes.dup.force_encoding(Encoding::UTF_8)
    unless text.valid_encoding?
      add("#{kind}_INVALID_UTF8", relative)
      return nil
    end
    text
  end

  def load_yaml(relative, kind)
    text = load_text(relative, kind, MAX_YAML_BYTES)
    return nil unless text

    stream = Psych.parse_stream(text)
    detect_duplicate_yaml_keys(stream, kind, relative)
    document = YAML.safe_load(
      text,
      permitted_classes: [],
      permitted_symbols: [],
      aliases: false,
    )
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
        key = key_node.value
        add("#{kind}_YAML_DUPLICATE_KEY", relative) if seen.include?(key)
        seen << key
        detect_duplicate_yaml_keys(value_node, kind, relative)
      end
    elsif node.respond_to?(:children) && node.children.is_a?(Array)
      node.children.each { |child| detect_duplicate_yaml_keys(child, kind, relative) }
    end
  end

  def load_json(relative, kind)
    text = load_text(relative, kind, MAX_JSON_BYTES)
    return nil unless text

    document = JSON.parse(text, object_class: UniqueJsonHash)
    unless document.is_a?(Hash)
      add("#{kind}_JSON_INVALID", relative)
      return nil
    end
    document
  rescue JSON::ParserError, DuplicateJsonKeyError
    add("#{kind}_JSON_INVALID", relative)
    nil
  end

  def nonempty?(value)
    case value
    when String then !value.strip.empty?
    when Array then !value.empty? && value.all? { |item| nonempty?(item) }
    when Hash then !value.empty? && value.keys.all? { |key| nonempty?(key) } && value.values.all? { |item| !item.nil? }
    else !value.nil?
    end
  end

  def validate_policy
    return unless @policy_document

    expect(@policy_document.keys == ["LicensePolicy"], "POLICY_DOCUMENT_ROOT", POLICY_PATH)
    @policy = @policy_document["LicensePolicy"]
    unless @policy.is_a?(Hash)
      add("POLICY_ROOT_INVALID", POLICY_PATH)
      return
    end

    expect(@policy.keys.sort == POLICY_TOP_LEVEL_FIELDS, "POLICY_FIELD_SET", POLICY_PATH)
    expect(@policy["schemaVersion"] == 1, "POLICY_SCHEMA_VERSION", POLICY_PATH)
    expect(@policy["profileId"] == "project-hotfix-license-policy-r04", "POLICY_PROFILE_ID", POLICY_PATH)
    expect(@policy["status"] == "APPROVED", "POLICY_STATUS", POLICY_PATH)
    expect(nonempty?(@policy["approvedAtUtc"]), "POLICY_APPROVAL_TIME", POLICY_PATH)
    approval = @policy["approvalBasis"].to_s
    expect(approval.include?("User approved"), "POLICY_APPROVAL_BASIS", POLICY_PATH)
    expect(approval.include?("three source-unproven review files"), "POLICY_APPROVED_DELETION_BASIS", POLICY_PATH)
    expect(approval.include?("Unity tutorial/readme files"), "POLICY_APPROVED_DELETION_BASIS", POLICY_PATH)
    expect(approval.include?("no-automatic-build"), "POLICY_NO_BUILD_BASIS", POLICY_PATH)
    expect(approval.include?("kjh4845") && approval.include?("sourceOwner"),
      "POLICY_FIRST_PARTY_OWNER_BASIS", POLICY_PATH)
    expect(approval.include?("PRODUCTION_EVIDENCE") && approval.include?("non-shipping"),
      "POLICY_PRODUCTION_EVIDENCE_BASIS", POLICY_PATH)
    expect(approval.include?("SUPERSEDED_CONTENT") && approval.include?("rejected"),
      "POLICY_SUPERSEDED_CONTENT_BASIS", POLICY_PATH)

    scope = @policy["scope"]
    unless scope.is_a?(Hash)
      add("POLICY_SCOPE_INVALID", POLICY_PATH)
      return
    end
    expected_scope_paths = {
      "packageManifest" => MANIFEST_PATH,
      "packageLock" => LOCK_PATH,
      "packageInventory" => INVENTORY_PATH,
      "distributedNoticeIndex" => NOTICE_PATH,
    }
    expect(scope.keys.sort == (expected_scope_paths.keys + %w[
      directPackageCount lockedPackageCount packageChangesPerformedByLic001
      reviewOnlyC2paImageCount
    ]).sort, "POLICY_SCOPE_CONTRACT", POLICY_PATH)
    expected_scope_paths.each do |key, value|
      expect(scope[key] == value, "POLICY_SCOPE_CONTRACT", POLICY_PATH)
    end
    %w[directPackageCount lockedPackageCount reviewOnlyC2paImageCount].each do |key|
      expect(scope[key].is_a?(Integer) && scope[key].positive?, "POLICY_SCOPE_COUNT", "#{POLICY_PATH}:#{key}")
    end
    expect(scope["packageChangesPerformedByLic001"] == 0,
      "POLICY_PACKAGE_CHANGES", POLICY_PATH)
    expect(nonempty?(@policy["sourceOfTruth"]), "POLICY_SOURCE_OF_TRUTH", POLICY_PATH)
    expect(nonempty?(@policy["evaluationOrder"]), "POLICY_EVALUATION_ORDER", POLICY_PATH)

    unity_rule = @policy["officialUnityPackageRule"]
    expect(unity_rule.is_a?(Hash) && unity_rule["decision"] == "ALLOW_WITH_CONDITIONS" &&
      nonempty?(unity_rule["conditions"]), "POLICY_UNITY_RULE", POLICY_PATH)
    allowed = @policy["generallyAllowedWithObligations"]
    expect(allowed.is_a?(Array) && !allowed.empty? && allowed.all? do |entry|
      entry.is_a?(Hash) && entry.keys.sort == %w[family obligations] && nonempty?(entry.values)
    end, "POLICY_ALLOWED_LICENSES", POLICY_PATH)
    blocked = @policy["blockedFamilies"]
    expect(blocked.is_a?(Array) && blocked.length >= 8 && blocked.all? { |item| nonempty?(item) },
      "POLICY_BLOCKED_LICENSES", POLICY_PATH)
    %w[GPL AGPL SSPL Non-commercial No-derivatives Unknown].each do |marker|
      expect(blocked.is_a?(Array) && blocked.any? { |item| item.include?(marker) },
        "POLICY_BLOCKED_LICENSE_MARKER", POLICY_PATH)
    end

    external = @policy["newExternalAssetRule"]
    expect(external.is_a?(Hash) && external["decision"] == "FAIL_CLOSED" &&
      external["requiredEvidence"].is_a?(Array) && external["requiredEvidence"].length == 5 &&
      external["failureDisposition"].to_s.include?("shippingAllowed must remain false"),
      "POLICY_EXTERNAL_ASSET_FAIL_CLOSED", POLICY_PATH)
    first_party = @policy["firstPartyProductionAssetRule"]
    expected_first_party_values = {
      "sourceStatus" => "PROJECT_AUTHORED",
      "licenseFamily" => "PROJECT_AUTHORED",
      "rightsStatus" => "FIRST_PARTY",
      "noticeDisposition" => "NO_THIRD_PARTY_NOTICE",
      "reviewOnly" => false,
    }
    expect(first_party.is_a?(Hash) &&
      first_party.keys.sort == %w[
        decision requiredFields requiredValues intendedUseProfiles
        shippingAllowedSemantics requiredEvidence failureDisposition
      ].sort &&
      first_party["decision"] == "ALLOW_WITH_EXPLICIT_FIRST_PARTY_EVIDENCE" &&
      first_party["requiredFields"] == FIRST_PARTY_ENTRY_FIELDS &&
      first_party["requiredValues"] == expected_first_party_values &&
      first_party["intendedUseProfiles"].is_a?(Hash) &&
      first_party["intendedUseProfiles"].keys.sort == %w[
        PLAYER_CONTENT PRODUCTION_EVIDENCE PRODUCTION_SOURCE SUPERSEDED_CONTENT
      ] &&
      first_party["intendedUseProfiles"].all? do |_name, profile|
        profile.is_a?(Hash) && profile.keys.sort == %w[meaning shippingAllowed] &&
          [true, false].include?(profile["shippingAllowed"]) && nonempty?(profile["meaning"])
      end &&
      first_party["intendedUseProfiles"]["PRODUCTION_SOURCE"]["shippingAllowed"] == false &&
      first_party["intendedUseProfiles"]["PRODUCTION_EVIDENCE"]["shippingAllowed"] == false &&
      first_party["intendedUseProfiles"]["SUPERSEDED_CONTENT"]["shippingAllowed"] == false &&
      first_party["intendedUseProfiles"]["PLAYER_CONTENT"]["shippingAllowed"] == true &&
      nonempty?(first_party["shippingAllowedSemantics"]) &&
      first_party["requiredEvidence"].is_a?(Array) && first_party["requiredEvidence"].length >= 4 &&
      nonempty?(first_party["failureDisposition"]),
      "POLICY_FIRST_PARTY_ASSET_RULE", POLICY_PATH)
    review = @policy["reviewOnlyContentRule"]
    expect(review.is_a?(Hash) && review["shippingAllowed"] == false &&
      nonempty?(review["allowedRepositoryPurpose"]) && nonempty?(review["allowedHumanReferenceUse"]) &&
      review["prohibitedUses"].is_a?(Array) && review["prohibitedUses"].length >= 4 &&
      nonempty?(review["promotionRequirement"]), "POLICY_REVIEW_ONLY_RULE", POLICY_PATH)

    evidence = @policy["evidenceRules"]
    expected_evidence_flags = {
      "normativePathsMustBeRepositoryRelative" => true,
      "machineSpecificAbsolutePathsAllowedInNormativeFields" => false,
      "packageCacheHashesAllowedInNormativeFields" => false,
      "packageCacheObservationsAllowedOnlyUnderAuditNotes" => true,
      "emptyRequiredFieldsAllowed" => false,
    }
    expect(evidence.is_a?(Hash), "POLICY_EVIDENCE_RULES", POLICY_PATH)
    if evidence.is_a?(Hash)
      expected_evidence_flags.each do |key, value|
        expect(evidence[key] == value, "POLICY_EVIDENCE_RULES", POLICY_PATH)
      end
      expect(evidence["requiredPackageFields"] == REQUIRED_PACKAGE_FIELDS,
        "POLICY_REQUIRED_PACKAGE_FIELDS", POLICY_PATH)
      expect(evidence["requiredFirstPartyAssetFields"] == FIRST_PARTY_ENTRY_FIELDS,
        "POLICY_REQUIRED_FIRST_PARTY_FIELDS", POLICY_PATH)
    end

    release = @policy["releaseAudit"]
    expect(release.is_a?(Hash) && release["automaticBuildAllowed"] == false &&
      release["currentScope"] == "Source package, review-only reference, first-party production source/evidence, and Player-content asset inventory" &&
      release["finalTarget"] == "Windows x64 Player" &&
      release["finalAuditOwners"] == %w[BLD-001 ALP-001] &&
      nonempty?(release["requiredChecks"]), "POLICY_RELEASE_AUDIT", POLICY_PATH)
    expect(nonempty?(@policy["limitations"]), "POLICY_LIMITATIONS", POLICY_PATH)
  end

  def validate_package_documents
    @manifest_dependencies = @manifest_document && @manifest_document["dependencies"]
    @lock_dependencies = @lock_document && @lock_document["dependencies"]

    if @manifest_document
      expect(@manifest_document.keys == ["dependencies"] && @manifest_dependencies.is_a?(Hash),
        "MANIFEST_SCHEMA", MANIFEST_PATH)
    end
    if @lock_document
      expect(@lock_document.keys == ["dependencies"] && @lock_dependencies.is_a?(Hash),
        "LOCK_SCHEMA", LOCK_PATH)
    end
    return unless @manifest_dependencies.is_a?(Hash) && @lock_dependencies.is_a?(Hash)

    expect((@manifest_dependencies.keys - @lock_dependencies.keys).empty?, "MANIFEST_PACKAGE_SET", MANIFEST_PATH)

    @manifest_dependencies.each do |package_id, version|
      expect(nonempty?(package_id) && nonempty?(version), "MANIFEST_DEPENDENCY_ENTRY", MANIFEST_PATH)
      locked = @lock_dependencies[package_id]
      expect(locked.is_a?(Hash) && locked["version"] == version, "MANIFEST_LOCK_VERSION", package_id)
    end

    source_counts = Hash.new(0)
    @lock_dependencies.each do |package_id, entry|
      unless entry.is_a?(Hash)
        add("LOCK_DEPENDENCY_ENTRY", package_id)
        next
      end
      required = %w[version depth source dependencies]
      allowed = required + ["url"]
      expect((required - entry.keys).empty? && (entry.keys - allowed).empty?, "LOCK_DEPENDENCY_SCHEMA", package_id)
      expect(nonempty?(entry["version"]), "LOCK_VERSION_EMPTY", package_id)
      expect(entry["depth"].is_a?(Integer) && entry["depth"] >= 0, "LOCK_DEPTH_INVALID", package_id)
      expect(%w[registry builtin].include?(entry["source"]), "LOCK_SOURCE_INVALID", package_id)
      expect(entry["dependencies"].is_a?(Hash), "LOCK_RELATIONSHIPS_INVALID", package_id)
      source_counts[entry["source"]] += 1
      direct = @manifest_dependencies.key?(package_id)
      expect(direct ? entry["depth"] == 0 : entry["depth"].is_a?(Integer) && entry["depth"] > 0,
        "LOCK_DEPTH_RELATIONSHIP", package_id)
      if entry["source"] == "registry"
        expect(entry["url"] == "https://packages.unity.com", "LOCK_REGISTRY_URL", package_id)
      else
        expect(!entry.key?("url"), "LOCK_BUILTIN_URL", package_id)
      end
      next unless entry["dependencies"].is_a?(Hash)

      entry["dependencies"].each do |dependency_id, dependency_version|
        target = @lock_dependencies[dependency_id]
        expect(target.is_a?(Hash), "LOCK_RELATIONSHIP_TARGET_MISSING", package_id)
        # Unity lock edges record the declaring package's compatible/minimum
        # version, which may be lower than the version selected at the root.
        expect(nonempty?(dependency_id) && nonempty?(dependency_version),
          "LOCK_RELATIONSHIP_VERSION", package_id)
      end
    end
    @lock_source_counts = source_counts
  end

  def validate_inventory
    return unless @inventory_document

    expect(@inventory_document.keys == ["ThirdPartyInventory"], "INVENTORY_DOCUMENT_ROOT", INVENTORY_PATH)
    @inventory = @inventory_document["ThirdPartyInventory"]
    unless @inventory.is_a?(Hash)
      add("INVENTORY_ROOT_INVALID", INVENTORY_PATH)
      return
    end
    expect(@inventory.keys.sort == INVENTORY_TOP_LEVEL_FIELDS, "INVENTORY_FIELD_SET", INVENTORY_PATH)
    expect(@inventory["schemaVersion"] == 1, "INVENTORY_SCHEMA_VERSION", INVENTORY_PATH)
    expect(@inventory["inventoryId"] == "project-hotfix-third-party-inventory-r06",
      "INVENTORY_ID", INVENTORY_PATH)
    expect(@inventory["status"] == "APPROVED_SOURCE_INVENTORY_WINDOWS_FINAL_AUDIT_PENDING",
      "INVENTORY_STATUS", INVENTORY_PATH)
    expect(nonempty?(@inventory["recordedAtUtc"]), "INVENTORY_RECORDED_AT", INVENTORY_PATH)
    expect(@inventory["policyPath"] == POLICY_PATH, "INVENTORY_POLICY_PATH", INVENTORY_PATH)
    expect(@inventory["distributedNoticeIndex"] == NOTICE_PATH, "INVENTORY_NOTICE_PATH", INVENTORY_PATH)

    validate_inventory_baseline(@inventory["packageBaseline"])
    validate_package_inventory(@inventory["packages"])
    validate_first_party_inventory(@inventory["firstPartyProductionAssets"])
    validate_review_inventory(@inventory["reviewOnlyAssets"])
    validate_audit_notes(@inventory["auditNotes"])
  end

  def validate_inventory_baseline(baseline)
    unless baseline.is_a?(Hash)
      add("INVENTORY_PACKAGE_BASELINE", INVENTORY_PATH)
      return
    end
    expect(baseline.keys.sort == %w[
      manifestPath manifestSha256 lockPath lockSha256 directCount transitiveCount
      lockedCount registryCount builtinCount builtinModuleCount
    ].sort, "INVENTORY_PACKAGE_BASELINE", INVENTORY_PATH)
    expect(baseline["manifestPath"] == MANIFEST_PATH && baseline["lockPath"] == LOCK_PATH,
      "INVENTORY_PACKAGE_BASELINE_PATH", INVENTORY_PATH)
    expect(baseline["manifestSha256"].to_s.match?(/\A[0-9a-f]{64}\z/) &&
      baseline["lockSha256"].to_s.match?(/\A[0-9a-f]{64}\z/),
      "INVENTORY_PACKAGE_BASELINE_SHA", INVENTORY_PATH)
    actual_manifest_sha = Digest::SHA256.hexdigest(@loaded_bytes.fetch(MANIFEST_PATH, ""))
    actual_lock_sha = Digest::SHA256.hexdigest(@loaded_bytes.fetch(LOCK_PATH, ""))
    expect(baseline["manifestSha256"] == actual_manifest_sha, "MANIFEST_HASH_DRIFT", MANIFEST_PATH)
    expect(baseline["lockSha256"] == actual_lock_sha, "LOCK_HASH_DRIFT", LOCK_PATH)
    return unless @manifest_dependencies.is_a?(Hash) && @lock_dependencies.is_a?(Hash)

    direct_count = @manifest_dependencies.length
    locked_count = @lock_dependencies.length
    derived = {
      "directCount" => direct_count,
      "transitiveCount" => locked_count - direct_count,
      "lockedCount" => locked_count,
      "registryCount" => @lock_source_counts.fetch("registry", 0),
      "builtinCount" => @lock_source_counts.fetch("builtin", 0),
      "builtinModuleCount" => @lock_dependencies.keys.count { |id| id.start_with?("com.unity.modules.") },
    }
    derived.each do |key, value|
      expect(baseline[key] == value, "INVENTORY_PACKAGE_BASELINE_COUNT", "#{INVENTORY_PATH}:#{key}")
    end
    scope = @policy.is_a?(Hash) ? @policy["scope"] : nil
    if scope.is_a?(Hash)
      expect(scope["directPackageCount"] == direct_count, "POLICY_MANIFEST_COUNT_DRIFT", POLICY_PATH)
      expect(scope["lockedPackageCount"] == locked_count, "POLICY_LOCK_COUNT_DRIFT", POLICY_PATH)
    end
  end

  def validate_package_inventory(packages)
    unless packages.is_a?(Array)
      add("INVENTORY_PACKAGES_INVALID", INVENTORY_PATH)
      @packages = []
      return
    end
    @packages = packages
    expected_ids = @lock_dependencies.is_a?(Hash) ? @lock_dependencies.keys : []
    expect(packages.length == expected_ids.length, "INVENTORY_PACKAGE_COUNT", INVENTORY_PATH)
    ids = packages.map { |entry| entry.is_a?(Hash) ? entry["packageId"] : nil }.compact
    expect(ids.length == ids.uniq.length, "INVENTORY_PACKAGE_ID_UNIQUE", INVENTORY_PATH)
    expect(ids.sort == expected_ids.sort, "INVENTORY_PACKAGE_SET", INVENTORY_PATH)

    resolved_locator_count = 0
    packages.each_with_index do |entry, index|
      path = entry.is_a?(Hash) && nonempty?(entry["packageId"]) ? entry["packageId"] : "package[#{index}]"
      unless entry.is_a?(Hash)
        add("INVENTORY_PACKAGE_ENTRY", path)
        next
      end
      expect(entry.keys.sort == PACKAGE_ENTRY_FIELDS, "INVENTORY_PACKAGE_FIELD_SET", path)
      REQUIRED_PACKAGE_FIELDS.each do |field|
        expect(nonempty?(entry[field]), "INVENTORY_PACKAGE_REQUIRED_FIELD", "#{path}:#{field}")
      end
      expect(%w[YES NO PENDING].include?(entry["shippingNoticeCandidate"]),
        "INVENTORY_PACKAGE_SHIPPING_ENUM", path)
      expect([true, false].include?(entry["perPackageNoticePresent"]),
        "INVENTORY_PACKAGE_NOTICE_BOOLEAN", path)
      expect(!entry["licenseFamily"].to_s.match?(/\b(?:GPL|AGPL|SSPL)\b|UNKNOWN|NO LICENSE/i),
        "INVENTORY_BLOCKED_PACKAGE_LICENSE", path)

      locked = @lock_dependencies.is_a?(Hash) ? @lock_dependencies[path] : nil
      next unless locked.is_a?(Hash)

      direct = @manifest_dependencies.is_a?(Hash) && @manifest_dependencies.key?(path)
      expect(entry["resolvedVersion"] == locked["version"], "INVENTORY_PACKAGE_VERSION_DRIFT", path)
      expect(entry["source"] == locked["source"], "INVENTORY_PACKAGE_SOURCE_DRIFT", path)
      expect(entry["relationship"] == (direct ? "DIRECT" : "TRANSITIVE"),
        "INVENTORY_PACKAGE_RELATIONSHIP_DRIFT", path)
      evidence = entry["sourceEvidence"]
      next unless evidence.is_a?(Array)

      lock_anchor = "#{LOCK_PATH}#dependencies.#{path}"
      manifest_anchor = "#{MANIFEST_PATH}#dependencies.#{path}"
      expect(evidence.include?(lock_anchor), "INVENTORY_LOCK_EVIDENCE", path)
      expect(evidence.include?(manifest_anchor) == direct, "INVENTORY_MANIFEST_EVIDENCE", path)
      evidence.each do |locator|
        expect(locator.is_a?(String) && !locator.strip.empty?, "INVENTORY_SOURCE_EVIDENCE_EMPTY", path)
        next unless locator.is_a?(String)

        expect(!locator.match?(/\A(?:\/|[A-Za-z]:[\\\/])/), "INVENTORY_ABSOLUTE_EVIDENCE", path)
        expect(!locator.include?("Library/PackageCache"), "INVENTORY_MACHINE_CACHE_EVIDENCE", path)
        resolved_locator_count += 1 if locator.start_with?("resolved-package:")
      end

      if path.start_with?("com.unity.modules.")
        expect(entry["usageClass"] == "BUILTIN_ENGINE_MODULE", "INVENTORY_MODULE_USAGE", path)
        expect(evidence.any? { |item| item.include?("BuiltInPackages/#{path}/package.json") },
          "INVENTORY_MODULE_METADATA_EVIDENCE", path)
        expect(evidence.any? { |item| item.include?("Contents/Resources/legal.txt") },
          "INVENTORY_MODULE_LEGAL_EVIDENCE", path)
      else
        license_locator = "resolved-package:#{path}@#{locked["version"]}/LICENSE.md"
        expect(evidence.include?(license_locator), "INVENTORY_PACKAGE_LICENSE_EVIDENCE", path)
      end
      notice_locator_present = evidence.any? do |item|
        item.start_with?("resolved-package:") && File.basename(item).match?(/notice/i)
      end
      expect(notice_locator_present == entry["perPackageNoticePresent"],
        "INVENTORY_PACKAGE_NOTICE_EVIDENCE", path)
    end
    expected_minimum_locators = packages.count do |entry|
      entry.is_a?(Hash) && !entry["packageId"].to_s.start_with?("com.unity.modules.")
    end
    expect(resolved_locator_count >= expected_minimum_locators,
      "INVENTORY_RESOLVED_EVIDENCE_COUNT", INVENTORY_PATH)
  end

  def validate_first_party_inventory(first_party)
    unless first_party.is_a?(Hash)
      add("INVENTORY_FIRST_PARTY_INVALID", INVENTORY_PATH)
      @first_party_items = []
      return
    end
    expect(first_party.keys.sort == %w[itemCount items purpose].sort,
      "INVENTORY_FIRST_PARTY_FIELD_SET", INVENTORY_PATH)
    expect(first_party["purpose"] ==
      "Project-authored production source, non-shipping evidence, and Player-content assets with explicit rights evidence",
      "INVENTORY_FIRST_PARTY_PURPOSE", INVENTORY_PATH)
    expect(first_party["itemCount"].is_a?(Integer) && first_party["itemCount"] >= 0,
      "INVENTORY_FIRST_PARTY_COUNT", INVENTORY_PATH)
    items = first_party["items"]
    unless items.is_a?(Array)
      add("INVENTORY_FIRST_PARTY_ITEMS_INVALID", INVENTORY_PATH)
      @first_party_items = []
      return
    end
    @first_party_items = items
    expect(items.length == first_party["itemCount"],
      "INVENTORY_FIRST_PARTY_ITEM_COUNT", INVENTORY_PATH)
    paths = items.map { |item| item.is_a?(Hash) ? item["path"] : nil }.compact
    expect(paths.length == paths.uniq.length,
      "INVENTORY_FIRST_PARTY_PATH_UNIQUE", INVENTORY_PATH)
    c1b004_items = items.select do |item|
      item.is_a?(Hash) && item["path"].to_s.start_with?(C1B004_ROOT)
    end
    expect(c1b004_items.length == 21, "INVENTORY_C1B004_ITEM_COUNT", INVENTORY_PATH)
    c1b004_source_count = c1b004_items.count { |item| item["assetType"] == "BLENDER_SOURCE" }
    c1b004_render_count = c1b004_items.count do |item|
      item["assetType"] == "CHARACTER_POSE_LINEUP_REFERENCE_RENDER"
    end
    expect(c1b004_source_count == 1 && c1b004_render_count == 20,
      "INVENTORY_C1B004_ASSET_TYPE_COUNT", INVENTORY_PATH)
    c1b005_items = items.select do |item|
      next false unless item.is_a?(Hash)

      item["path"] == C1B005_FBX_PATH || item["path"].to_s.start_with?(C1B005_CAPTURE_ROOT)
    end
    expect(c1b005_items.length == 9, "INVENTORY_C1B005_ITEM_COUNT", INVENTORY_PATH)
    c1b005_fbx_count = c1b005_items.count { |item| item["path"] == C1B005_FBX_PATH }
    c1b005_capture_count = c1b005_items.count do |item|
      item["assetType"] == "UNITY_INTEROP_REFERENCE_RENDER"
    end
    expect(c1b005_fbx_count == 1 && c1b005_capture_count == 8,
      "INVENTORY_C1B005_ASSET_TYPE_COUNT", INVENTORY_PATH)
    c1brw002_items = items.select do |item|
      item.is_a?(Hash) && item["path"].to_s.start_with?(C1BRW002_ROOT)
    end
    expect(c1brw002_items.length == 9, "INVENTORY_C1BRW002_ITEM_COUNT", INVENTORY_PATH)
    c1brw002_source_count = c1brw002_items.count { |item| item["assetType"] == "BLENDER_SOURCE" }
    c1brw002_render_count = c1brw002_items.count do |item|
      item["assetType"] == "CHARACTER_REWORK_REFERENCE_RENDER"
    end
    expect(c1brw002_source_count == 1 && c1brw002_render_count == 8,
      "INVENTORY_C1BRW002_ASSET_TYPE_COUNT", INVENTORY_PATH)

    items.each_with_index do |item, index|
      path = item.is_a?(Hash) && nonempty?(item["path"]) ? item["path"] : "first-party[#{index}]"
      unless item.is_a?(Hash)
        add("INVENTORY_FIRST_PARTY_ENTRY", path)
        next
      end
      expect(item.keys.sort == FIRST_PARTY_ENTRY_FIELDS.sort,
        "INVENTORY_FIRST_PARTY_ENTRY_FIELDS", path)
      FIRST_PARTY_ENTRY_FIELDS.each do |field|
        next if %w[reviewOnly shippingAllowed].include?(field)
        expect(nonempty?(item[field]), "INVENTORY_FIRST_PARTY_REQUIRED_FIELD", "#{path}:#{field}")
      end
      expect(repository_relative_path?(path), "INVENTORY_FIRST_PARTY_PATH_INVALID", path)
      expect(MEDIA_EXTENSIONS.include?(File.extname(path).downcase),
        "INVENTORY_FIRST_PARTY_ASSET_TYPE_PATH", path)
      expect(item["assetType"].to_s.match?(/\A[A-Z][A-Z0-9_]*\z/),
        "INVENTORY_FIRST_PARTY_ASSET_TYPE", path)
      expect(item["sourceOwner"].is_a?(String) && !item["sourceOwner"].strip.empty? &&
        !item["sourceOwner"].match?(/\A(?:UNKNOWN|UNPROVEN|UNVERIFIED)\z/i),
        "INVENTORY_FIRST_PARTY_SOURCE_OWNER", path)
      expect(item["sourceStatus"] == "PROJECT_AUTHORED",
        "INVENTORY_FIRST_PARTY_SOURCE_STATUS", path)
      profiles = if @policy.is_a?(Hash) && @policy["firstPartyProductionAssetRule"].is_a?(Hash)
        @policy["firstPartyProductionAssetRule"]["intendedUseProfiles"]
      end
      profile = profiles.is_a?(Hash) ? profiles[item["intendedUse"]] : nil
      expect(profile.is_a?(Hash), "INVENTORY_FIRST_PARTY_INTENDED_USE", path)
      extension = File.extname(path).downcase
      if item["intendedUse"] == "PRODUCTION_SOURCE"
        expect(!path.start_with?("Project hotfix/Assets/"),
          "INVENTORY_FIRST_PARTY_SOURCE_INSIDE_UNITY_ASSETS", path)
      end
      if item["shippingAllowed"] == false && item["intendedUse"] != "SUPERSEDED_CONTENT"
        expect(!path.start_with?("Project hotfix/Assets/"),
          "INVENTORY_FIRST_PARTY_NONSHIPPING_INSIDE_UNITY_ASSETS", path)
      end
      if item["intendedUse"] == "SUPERSEDED_CONTENT"
        expect(extension == ".fbx" && item["assetType"] == "MODEL" &&
          item["shippingAllowed"] == false && item["reviewOnly"] == false,
          "INVENTORY_FIRST_PARTY_SUPERSEDED_CLASS", path)
      end
      if extension == ".blend" || item["assetType"] == "BLENDER_SOURCE"
        expect(extension == ".blend" && item["assetType"] == "BLENDER_SOURCE" &&
          item["intendedUse"] == "PRODUCTION_SOURCE",
          "INVENTORY_FIRST_PARTY_BLEND_INTENDED_USE", path)
      end
      reference_render = item["assetType"].to_s.end_with?("_REFERENCE_RENDER")
      if item["intendedUse"] == "PRODUCTION_EVIDENCE" || reference_render
        expect(extension == ".png" && reference_render &&
          item["intendedUse"] == "PRODUCTION_EVIDENCE",
          "INVENTORY_FIRST_PARTY_EVIDENCE_INTENDED_USE", path)
      end
      expect(item["licenseFamily"] == "PROJECT_AUTHORED",
        "INVENTORY_FIRST_PARTY_LICENSE_STATUS", path)
      expect(item["rightsStatus"] == "FIRST_PARTY",
        "INVENTORY_FIRST_PARTY_RIGHTS_STATUS", path)
      expect(item["noticeDisposition"] == "NO_THIRD_PARTY_NOTICE",
        "INVENTORY_FIRST_PARTY_NOTICE_STATUS", path)
      expect(item["reviewOnly"] == false, "INVENTORY_FIRST_PARTY_REVIEW_FLAG", path)
      expected_shipping = profile.is_a?(Hash) ? profile["shippingAllowed"] : nil
      expect([true, false].include?(item["shippingAllowed"]) &&
        item["shippingAllowed"] == expected_shipping,
        "INVENTORY_FIRST_PARTY_SHIPPING_FLAG", path)
      expect(item["sha256"].to_s.match?(/\A[0-9a-f]{64}\z/),
        "INVENTORY_FIRST_PARTY_SHA_FORMAT", path)
      evidence = item["sourceEvidence"]
      expect(evidence.is_a?(Array) && !evidence.empty? &&
        evidence.all? { |value| value.is_a?(String) && !value.strip.empty? },
        "INVENTORY_FIRST_PARTY_SOURCE_EVIDENCE", path)
      next unless evidence.is_a?(Array)

      binary_anchor = "#{BINARY_INVENTORY_PATH}#files[path=#{path}]"
      expect(evidence.include?(binary_anchor), "INVENTORY_FIRST_PARTY_BINARY_EVIDENCE", path)
      expect(evidence.include?("project-author:#{item["sourceOwner"]}"),
        "INVENTORY_FIRST_PARTY_AUTHOR_EVIDENCE", path)
      if extension == ".blend" || item["assetType"] == "BLENDER_SOURCE"
        expect(evidence.any? do |locator|
          locator.is_a?(String) && locator.end_with?("GenerationManifest.yaml#stages.blend-source")
        end,
          "INVENTORY_FIRST_PARTY_BLEND_MANIFEST_EVIDENCE", path)
      end
      if reference_render
        render_anchor_suffixes = [
          "GenerationManifest.yaml#stages.reference-render.outputs[path=#{path}]",
          "GenerationManifest.yaml#stages.reference-render.outputs[file=#{File.basename(path)}]",
        ]
        expect(evidence.any? do |locator|
          locator.is_a?(String) && render_anchor_suffixes.any? { |suffix| locator.end_with?(suffix) }
        end,
          "INVENTORY_FIRST_PARTY_RENDER_MANIFEST_EVIDENCE", path)
      end
      if path.start_with?(C1B003_ROOT)
        expect(item["sourceOwner"] == "kjh4845", "INVENTORY_C1B003_SOURCE_OWNER", path)
        expect(evidence.include?(CHARACTER_PROFILE_ANCHOR),
          "INVENTORY_C1B003_PROFILE_EVIDENCE", path)
        manifest_anchor = if extension == ".blend"
          "#{C1B003_MANIFEST_PATH}#stages.blend-source"
        else
          "#{C1B003_MANIFEST_PATH}#stages.reference-render.outputs[path=#{path}]"
        end
        expect(evidence.include?(manifest_anchor), "INVENTORY_C1B003_MANIFEST_EVIDENCE", path)
      end
      if path.start_with?(C1B004_ROOT)
        expect(item["sourceOwner"] == "kjh4845", "INVENTORY_C1B004_SOURCE_OWNER", path)
        expect(evidence.include?(CHARACTER_PROFILE_ANCHOR),
          "INVENTORY_C1B004_PROFILE_EVIDENCE", path)
        manifest_anchor = if extension == ".blend"
          "#{C1B004_MANIFEST_PATH}#stages.blend-source"
        else
          "#{C1B004_MANIFEST_PATH}#stages.reference-render.outputs[file=#{File.basename(path)}]"
        end
        expect(evidence.include?(manifest_anchor), "INVENTORY_C1B004_MANIFEST_EVIDENCE", path)
        if extension == ".png"
          expect(item["assetType"] == "CHARACTER_POSE_LINEUP_REFERENCE_RENDER" &&
            item["intendedUse"] == "PRODUCTION_EVIDENCE" && item["shippingAllowed"] == false,
            "INVENTORY_C1B004_RENDER_CLASS", path)
        end
      end
      if path == C1B005_FBX_PATH || path.start_with?(C1B005_CAPTURE_ROOT)
        expect(item["sourceOwner"] == "kjh4845", "INVENTORY_C1B005_SOURCE_OWNER", path)
        expect(evidence.include?(CHARACTER_PROFILE_ANCHOR),
          "INVENTORY_C1B005_PROFILE_EVIDENCE", path)
        if path == C1B005_FBX_PATH
          expect(extension == ".fbx" && item["assetType"] == "MODEL" &&
            item["intendedUse"] == "SUPERSEDED_CONTENT" && item["shippingAllowed"] == false,
            "INVENTORY_C1B005_FBX_CLASS", path)
          expect(evidence.include?("#{C1B005_MANIFEST_PATH}#stages.fbx-export") &&
            evidence.include?("#{C1B005_MANIFEST_PATH}#identity.fbxSha256") &&
            evidence.include?("superseded-by:#{C1BRW002_ROOT}CHR_MasterCharacter_C1B_NeutralRework_r01.blend") &&
            evidence.any? { |locator| locator.include?("geometry rejected") },
            "INVENTORY_C1B005_FBX_MANIFEST_EVIDENCE", path)
        else
          expect(extension == ".png" && item["assetType"] == "UNITY_INTEROP_REFERENCE_RENDER" &&
            item["intendedUse"] == "PRODUCTION_EVIDENCE" && item["shippingAllowed"] == false,
            "INVENTORY_C1B005_CAPTURE_CLASS", path)
          manifest_anchor =
            "#{C1B005_MANIFEST_PATH}#stages.reference-render.outputs[path=#{path}]"
          expect(evidence.include?(manifest_anchor), "INVENTORY_C1B005_CAPTURE_MANIFEST_EVIDENCE", path)
        end
      end
      if path.start_with?(C1BRW002_ROOT)
        expect(item["sourceOwner"] == "kjh4845", "INVENTORY_C1BRW002_SOURCE_OWNER", path)
        expect(evidence.include?(C1BRW_PROFILE_ANCHOR),
          "INVENTORY_C1BRW002_PROFILE_EVIDENCE", path)
        manifest_anchor = if extension == ".blend"
          "#{C1BRW002_MANIFEST_PATH}#stages.blend-source"
        else
          "#{C1BRW002_MANIFEST_PATH}#stages.reference-render.outputs[path=#{path}]"
        end
        expect(evidence.include?(manifest_anchor), "INVENTORY_C1BRW002_MANIFEST_EVIDENCE", path)
        if extension == ".png"
          expect(item["assetType"] == "CHARACTER_REWORK_REFERENCE_RENDER" &&
            item["intendedUse"] == "PRODUCTION_EVIDENCE" && item["shippingAllowed"] == false,
            "INVENTORY_C1BRW002_RENDER_CLASS", path)
        end
      end
      evidence.each do |locator|
        next unless locator.is_a?(String)

        expect(!locator.match?(/\A(?:\/|[A-Za-z]:[\\\/])/),
          "INVENTORY_FIRST_PARTY_ABSOLUTE_EVIDENCE", path)
        expect(!locator.include?("Library/PackageCache"),
          "INVENTORY_FIRST_PARTY_MACHINE_CACHE_EVIDENCE", path)
      end
    end
  end

  def validate_review_inventory(review)
    unless review.is_a?(Hash)
      add("INVENTORY_REVIEW_INVALID", INVENTORY_PATH)
      @review_items = []
      return
    end
    expect(review.keys.sort == %w[defaultShippingAllowed itemCount items purpose].sort,
      "INVENTORY_REVIEW_FIELD_SET", INVENTORY_PATH)
    expect(review["purpose"] == "Design reference and decision history only",
      "INVENTORY_REVIEW_PURPOSE", INVENTORY_PATH)
    expected_review_count = if @policy.is_a?(Hash) && @policy["scope"].is_a?(Hash)
      @policy["scope"]["reviewOnlyC2paImageCount"]
    end
    expect(review["itemCount"] == expected_review_count, "INVENTORY_REVIEW_COUNT", INVENTORY_PATH)
    expect(review["defaultShippingAllowed"] == false, "INVENTORY_REVIEW_DEFAULT_SHIPPING", INVENTORY_PATH)
    items = review["items"]
    unless items.is_a?(Array)
      add("INVENTORY_REVIEW_ITEMS_INVALID", INVENTORY_PATH)
      @review_items = []
      return
    end
    @review_items = items
    expect(items.length == review["itemCount"], "INVENTORY_REVIEW_ITEM_COUNT", INVENTORY_PATH)
    paths = items.map { |item| item.is_a?(Hash) ? item["path"] : nil }.compact
    expect(paths.length == paths.uniq.length, "INVENTORY_REVIEW_PATH_UNIQUE", INVENTORY_PATH)

    items.each_with_index do |item, index|
      path = item.is_a?(Hash) && nonempty?(item["path"]) ? item["path"] : "review[#{index}]"
      unless item.is_a?(Hash)
        add("INVENTORY_REVIEW_ENTRY", path)
        next
      end
      expect(item.keys.sort == REVIEW_ENTRY_FIELDS, "INVENTORY_REVIEW_ENTRY_FIELDS", path)
      REVIEW_ENTRY_FIELDS.each do |field|
        next if %w[reviewOnly shippingAllowed].include?(field)
        expect(nonempty?(item[field]), "INVENTORY_REVIEW_REQUIRED_FIELD", "#{path}:#{field}")
      end
      expect(repository_relative_path?(path), "INVENTORY_REVIEW_PATH_INVALID", path)
      expect(!path.start_with?("Project hotfix/Assets/"), "INVENTORY_REVIEW_INSIDE_UNITY_ASSETS", path)
      expect(item["reviewOnly"] == true, "INVENTORY_REVIEW_ONLY_FLAG", path)
      expect(item["shippingAllowed"] == false, "INVENTORY_REVIEW_SHIPPING_FLAG", path)
      expect(item["contentCredentialStatus"] == "C2PA_CLAIM_DETECTED_NOT_A_LICENSE_GRANT",
        "INVENTORY_REVIEW_CREDENTIAL_STATUS", path)
      expect(item["intendedUse"] == "DESIGN_REFERENCE_ONLY", "INVENTORY_REVIEW_INTENDED_USE", path)
      expect(item["licenseFamily"] == "UNKNOWN_EXTERNAL_ASSET_RIGHTS",
        "INVENTORY_REVIEW_LICENSE_STATUS", path)
      expect(item["rightsStatus"] == "NOT_PROVEN_FOR_SHIPPING", "INVENTORY_REVIEW_RIGHTS_STATUS", path)
      expect(item["noticeDisposition"] == "REVIEW_ONLY_BLOCKED_FROM_SHIPPING",
        "INVENTORY_REVIEW_NOTICE_STATUS", path)
      expect(item["sha256"].to_s.match?(/\A[0-9a-f]{64}\z/), "INVENTORY_REVIEW_SHA_FORMAT", path)
      evidence = item["sourceEvidence"]
      if evidence.is_a?(Array)
        binary_anchor = "#{BINARY_INVENTORY_PATH}#files[path=#{path}]"
        expect(evidence.include?(binary_anchor), "INVENTORY_REVIEW_BINARY_EVIDENCE", path)
        expect(evidence.any? { |value| value.include?("embedded C2PA claim detected in #{path}") },
          "INVENTORY_REVIEW_C2PA_EVIDENCE", path)
      end
    end
  end

  def validate_audit_notes(notes)
    unless notes.is_a?(Hash)
      add("INVENTORY_AUDIT_NOTES", INVENTORY_PATH)
      return
    end
    package_count = @lock_dependencies.is_a?(Hash) ? @lock_dependencies.length : 0
    package_matches = (@packages || []).count do |package|
      next false unless package.is_a?(Hash) && @lock_dependencies.is_a?(Hash)
      locked = @lock_dependencies[package["packageId"]]
      locked.is_a?(Hash) && locked["version"] == package["resolvedVersion"]
    end
    non_module_count = (@packages || []).count do |package|
      package.is_a?(Hash) && !package["packageId"].to_s.start_with?("com.unity.modules.")
    end
    notice_count = (@packages || []).count do |package|
      package.is_a?(Hash) && package["perPackageNoticePresent"] == true
    end
    expected = {
      "normative" => false,
      "packageCacheRoot" => "Project hotfix/Library/PackageCache (Unity-generated and Git-ignored)",
      "observedResolvedPackageDirectories" => package_count,
      "lockToPackageNameVersionMatches" => package_matches,
      "observedRootLicenseFilesForNonModulePackages" => non_module_count,
      "observedRootThirdPartyNoticeFiles" => notice_count,
      "packageJsonLicenseMetadataFieldsPresent" => 0,
      "packageNoticesDeclaringGPLorAGPLorSSPLAsAComponentLicense" => 0,
      "unityEditorVersion" => "6000.3.9f1 (7a9955a4f2fa)",
      "windowsStandaloneSupportInstalled" => false,
      "playerBuildInspected" => false,
    }
    expected.each do |key, value|
      expect(notes[key] == value, "INVENTORY_AUDIT_NOTE", "#{INVENTORY_PATH}:#{key}")
    end
    expect(notes.keys.sort == (expected.keys + ["limitation"]).sort,
      "INVENTORY_AUDIT_NOTE_FIELDS", INVENTORY_PATH)
    expect(notes["limitation"].to_s.include?("no automatic build is authorized"),
      "INVENTORY_AUDIT_LIMITATION", INVENTORY_PATH)
  end

  def validate_binary_inventory
    return unless @binary_document

    expect(@binary_document.keys == ["BinaryAssetInventory"],
      "BINARY_INVENTORY_DOCUMENT_ROOT", BINARY_INVENTORY_PATH)
    binary = @binary_document["BinaryAssetInventory"]
    unless binary.is_a?(Hash)
      add("BINARY_INVENTORY_ROOT", BINARY_INVENTORY_PATH)
      return
    end
    expect(binary.keys.sort == %w[
      schemaVersion revision recordedAtUtc policyPath excludedGeneratedRoots summary storage files
    ].sort, "BINARY_INVENTORY_FIELDS", BINARY_INVENTORY_PATH)
    expect(binary["schemaVersion"] == 1 && binary["revision"] == "r07",
      "BINARY_INVENTORY_VERSION", BINARY_INVENTORY_PATH)
    expect(binary["policyPath"] == "config/repository/BinaryAssetPolicy.md",
      "BINARY_INVENTORY_POLICY_PATH", BINARY_INVENTORY_PATH)
    files = binary["files"]
    unless files.is_a?(Array)
      add("BINARY_INVENTORY_FILES", BINARY_INVENTORY_PATH)
      return
    end
    @binary_items = files
    expected_media_count = (@review_items || []).length + (@first_party_items || []).length
    expect(files.length == expected_media_count,
      "BINARY_INVENTORY_FILE_COUNT", BINARY_INVENTORY_PATH)
    paths = files.map { |entry| entry.is_a?(Hash) ? entry["path"] : nil }.compact
    expect(paths.length == paths.uniq.length, "BINARY_INVENTORY_PATH_UNIQUE", BINARY_INVENTORY_PATH)

    total_bytes = 0
    hashes = []
    files.each_with_index do |entry, index|
      path = entry.is_a?(Hash) && nonempty?(entry["path"]) ? entry["path"] : "binary[#{index}]"
      unless entry.is_a?(Hash)
        add("BINARY_INVENTORY_ENTRY", path)
        next
      end
      expect(entry.keys.sort == %w[bytes path sha256], "BINARY_INVENTORY_ENTRY_FIELDS", path)
      expect(entry["bytes"].is_a?(Integer) && entry["bytes"] >= 0, "BINARY_INVENTORY_BYTES", path)
      expect(entry["sha256"].to_s.match?(/\A[0-9a-f]{64}\z/), "BINARY_INVENTORY_SHA", path)
      total_bytes += entry["bytes"] if entry["bytes"].is_a?(Integer)
      hashes << entry["sha256"] if entry["sha256"].is_a?(String)
    end
    summary = binary["summary"]
    expected_summary_fields = %w[
      fileCount totalBytes uniqueContentHashes filesOver10MiB
      currentLfsRequiredCandidates currentLfsTrackedFiles ordinaryGitBinaryFiles
    ]
    expect(summary.is_a?(Hash) && summary.keys.sort == expected_summary_fields.sort,
      "BINARY_INVENTORY_SUMMARY_FIELDS", BINARY_INVENTORY_PATH)
    expected_content_summary = {
      "fileCount" => files.length,
      "totalBytes" => total_bytes,
      "uniqueContentHashes" => hashes.uniq.length,
      "filesOver10MiB" => files.count { |item| item.is_a?(Hash) && item["bytes"].is_a?(Integer) && item["bytes"] > 10 * 1024 * 1024 },
    }
    expected_content_summary.each do |key, value|
      expect(summary.is_a?(Hash) && summary[key] == value,
        "BINARY_INVENTORY_SUMMARY", "#{BINARY_INVENTORY_PATH}:#{key}")
    end
    if summary.is_a?(Hash)
      %w[currentLfsRequiredCandidates currentLfsTrackedFiles ordinaryGitBinaryFiles].each do |key|
        expect(summary[key].is_a?(Integer) && summary[key] >= 0,
          "BINARY_INVENTORY_STORAGE_COUNT", "#{BINARY_INVENTORY_PATH}:#{key}")
      end
      tracked_count = summary["currentLfsTrackedFiles"]
      ordinary_count = summary["ordinaryGitBinaryFiles"]
      expect(tracked_count.is_a?(Integer) && ordinary_count.is_a?(Integer) &&
        tracked_count + ordinary_count == files.length,
        "BINARY_INVENTORY_STORAGE_PARTITION", BINARY_INVENTORY_PATH)
    end
    storage = binary["storage"]
    expect(storage.is_a?(Hash) &&
      storage["remoteName"] == "origin" &&
      storage["remoteVisibility"] == "PRIVATE" &&
      storage["defaultBranch"] == "main" &&
      storage["repositoryLocalLfsEnabled"] == true &&
      storage["historyMigrationPerformed"] == false &&
      storage["existingReviewPngsMigrated"] == false,
      "BINARY_INVENTORY_STORAGE", BINARY_INVENTORY_PATH)
  end

  def validate_repository_assets
    review_items = @review_items || []
    first_party_items = @first_party_items || []
    binary_items = @binary_items || []
    review_by_path = review_items.select { |item| item.is_a?(Hash) }.to_h { |item| [item["path"], item] }
    first_party_by_path = first_party_items.select { |item| item.is_a?(Hash) }.to_h do |item|
      [item["path"], item]
    end
    binary_by_path = binary_items.select { |item| item.is_a?(Hash) }.to_h { |item| [item["path"], item] }
    active_media = @active_paths.select { |path| MEDIA_EXTENSIONS.include?(File.extname(path).downcase) }.to_set
    review_paths = review_by_path.keys.compact.to_set
    first_party_paths = first_party_by_path.keys.compact.to_set
    overlap = review_paths & first_party_paths
    expect(overlap.empty?, "INVENTORY_MEDIA_SCOPE_OVERLAP", INVENTORY_PATH)
    overlap.sort.each { |path| add("INVENTORY_MEDIA_SCOPE_OVERLAP", path) }
    registered_paths = review_paths | first_party_paths
    expect(active_media == registered_paths, "REVIEW_MEDIA_EXACT_SET", INVENTORY_PATH)
    (active_media - registered_paths).sort.each { |path| add("UNREGISTERED_MEDIA_ASSET", path) }
    (registered_paths - active_media).sort.each { |path| add("INVENTORIED_MEDIA_MISSING", path) }
    expect(binary_by_path.keys.compact.to_set == registered_paths,
      "BINARY_REVIEW_PATH_SET", BINARY_INVENTORY_PATH)

    review_by_path.each do |path, review|
      next unless repository_relative_path?(path)
      absolute = safe_regular_file(path, "REVIEW_ASSET", MAX_EVIDENCE_BYTES)
      next unless absolute

      size = File.size(absolute)
      digest = Digest::SHA256.file(absolute).hexdigest
      expect(digest == review["sha256"], "REVIEW_ASSET_SHA_DRIFT", path)
      binary = binary_by_path[path]
      unless binary.is_a?(Hash)
        add("REVIEW_ASSET_BINARY_RECORD_MISSING", path)
        next
      end
      expect(binary["sha256"] == digest, "REVIEW_BINARY_SHA_DRIFT", path)
      expect(binary["bytes"] == size, "REVIEW_BINARY_SIZE_DRIFT", path)
      bytes = File.binread(absolute)
      expect(bytes.include?("OpenAI Media Service") && bytes.include?("trainedAlgorithmicMedia"),
        "REVIEW_ASSET_C2PA_MARKERS", path)
    end

    first_party_by_path.each do |path, first_party|
      next unless repository_relative_path?(path)
      absolute = safe_regular_file(path, "FIRST_PARTY_ASSET", MAX_EVIDENCE_BYTES)
      next unless absolute

      size = File.size(absolute)
      digest = Digest::SHA256.file(absolute).hexdigest
      expect(digest == first_party["sha256"], "FIRST_PARTY_ASSET_SHA_DRIFT", path)
      binary = binary_by_path[path]
      unless binary.is_a?(Hash)
        add("FIRST_PARTY_ASSET_BINARY_RECORD_MISSING", path)
        next
      end
      expect(binary["sha256"] == digest, "FIRST_PARTY_BINARY_SHA_DRIFT", path)
      expect(binary["bytes"] == size, "FIRST_PARTY_BINARY_SIZE_DRIFT", path)
    end
  end

  def validate_notice
    return unless @notice_text

    NOTICE_MARKERS.each do |marker|
      expect(@notice_text.include?(marker), "NOTICE_REQUIRED_MARKER", NOTICE_PATH)
    end
    return unless @packages.is_a?(Array)

    direct_count = @manifest_dependencies.is_a?(Hash) ? @manifest_dependencies.length : 0
    locked_count = @lock_dependencies.is_a?(Hash) ? @lock_dependencies.length : 0
    transitive_count = locked_count - direct_count
    module_count = @packages.count do |package|
      package.is_a?(Hash) && package["packageId"].to_s.start_with?("com.unity.modules.")
    end
    review_count = (@review_items || []).length
    [
      "direct packages `#{direct_count}`",
      "transitive packages `#{transitive_count}`",
      "locked entries `#{locked_count}`",
      "#{module_count} `com.unity.modules.*@1.0.0` entries",
      "#{review_count} repository images",
    ].each do |marker|
      expect(@notice_text.include?(marker), "NOTICE_DYNAMIC_COUNT", NOTICE_PATH)
    end

    @packages.each do |package|
      next unless package.is_a?(Hash)
      package_id = package["packageId"].to_s
      next if package_id.start_with?("com.unity.modules.")

      marker = "#{package_id}@#{package["resolvedVersion"]}"
      expect(@notice_text.include?(marker), "NOTICE_PACKAGE_COVERAGE", package_id)
    end
  end

  def validate_package_cache
    cache = safe_directory(PACKAGE_CACHE_PATH, "PACKAGE_CACHE")
    return unless cache
    expected_ids = @lock_dependencies.is_a?(Hash) ? @lock_dependencies.keys : []
    roots = {}
    observed_package_roots = 0
    Dir.children(cache).sort.each do |name|
      child = cache.join(name)
      begin
        stat = File.lstat(child)
      rescue Errno::ENOENT
        add("PACKAGE_CACHE_ENTRY_MISSING", PACKAGE_CACHE_PATH)
        next
      end
      if stat.symlink?
        add("PACKAGE_CACHE_ENTRY_SYMLINK", "#{PACKAGE_CACHE_PATH}/#{name}")
        next
      end
      next unless stat.directory?

      package_json = safe_regular_absolute(child.join("package.json"), child, "PACKAGE_CACHE_JSON", MAX_JSON_BYTES)
      next unless package_json
      begin
        text = package_json.binread.force_encoding(Encoding::UTF_8)
        raise JSON::ParserError unless text.valid_encoding?
        metadata = JSON.parse(text, object_class: UniqueJsonHash)
      rescue JSON::ParserError, DuplicateJsonKeyError
        add("PACKAGE_CACHE_JSON_INVALID", relative_to_root(package_json))
        next
      end
      observed_package_roots += 1
      package_id = metadata["name"]
      version = metadata["version"]
      unless expected_ids.include?(package_id)
        add("PACKAGE_CACHE_PACKAGE_EXTRA", relative_to_root(child))
        next
      end

      if roots.key?(package_id)
        add("PACKAGE_CACHE_PACKAGE_DUPLICATE", package_id)
      else
        roots[package_id] = { path: child, version: version, metadata: metadata }
      end
    end

    @package_cache_matches = roots.length
    expect(observed_package_roots == expected_ids.length,
      "PACKAGE_CACHE_DIRECTORY_COUNT", PACKAGE_CACHE_PATH)
    expect(roots.keys.sort == expected_ids.sort, "PACKAGE_CACHE_PACKAGE_SET", PACKAGE_CACHE_PATH)
    root_license_packages = 0
    root_notice_packages = 0
    license_metadata_count = 0
    (@packages || []).each do |package|
      next unless package.is_a?(Hash)
      package_id = package["packageId"]
      root = roots[package_id]
      unless root
        add("PACKAGE_CACHE_PACKAGE_MISSING", package_id)
        next
      end
      expect(root[:version] == package["resolvedVersion"], "PACKAGE_CACHE_VERSION_DRIFT", package_id)
      metadata = root[:metadata]
      license_metadata_count += 1 if metadata.key?("license") || metadata.key?("licenses")

      children = Dir.children(root[:path])
      license_names = children.select do |name|
        !name.downcase.end_with?(".meta") && name.match?(/\ALICENSE(?:\..*)?\z/i)
      end
      notice_names = children.select do |name|
        !name.downcase.end_with?(".meta") && name.match?(/notice/i)
      end
      has_license = license_names.any? do |name|
        safe_regular_absolute(root[:path].join(name), root[:path], "PACKAGE_CACHE_LICENSE", MAX_EVIDENCE_BYTES)
      end
      has_notice = notice_names.any? do |name|
        safe_regular_absolute(root[:path].join(name), root[:path], "PACKAGE_CACHE_NOTICE", MAX_EVIDENCE_BYTES)
      end
      root_license_packages += 1 if has_license
      root_notice_packages += 1 if has_notice
      module_package = package_id.start_with?("com.unity.modules.")
      expect(has_license == !module_package, "PACKAGE_CACHE_ROOT_LICENSE", package_id)
      expect(has_notice == package["perPackageNoticePresent"], "PACKAGE_CACHE_ROOT_NOTICE", package_id)

      Array(package["sourceEvidence"]).grep(/^resolved-package:/).each do |locator|
        @package_cache_locators += 1
        match = locator.match(/\Aresolved-package:([^@\/]+)@([^\/]+)\/(.+)\z/)
        unless match
          add("PACKAGE_CACHE_LOCATOR_INVALID", package_id)
          next
        end
        locator_id, locator_version, relative = match.captures
        expect(locator_id == package_id && locator_version == package["resolvedVersion"],
          "PACKAGE_CACHE_LOCATOR_ID_VERSION", package_id)
        unless repository_relative_path?(relative)
          add("PACKAGE_CACHE_LOCATOR_PATH", package_id)
          next
        end
        evidence = safe_regular_absolute(root[:path].join(relative), root[:path],
          "PACKAGE_CACHE_EVIDENCE", MAX_EVIDENCE_BYTES)
        next unless evidence

        expect(File.size(evidence).positive?, "PACKAGE_CACHE_EVIDENCE_EMPTY", locator)
      end
    end
    expected_license_count = (@packages || []).count do |package|
      package.is_a?(Hash) && !package["packageId"].to_s.start_with?("com.unity.modules.")
    end
    expected_notice_count = (@packages || []).count do |package|
      package.is_a?(Hash) && package["perPackageNoticePresent"] == true
    end
    expect(root_license_packages == expected_license_count,
      "PACKAGE_CACHE_LICENSE_COUNT", PACKAGE_CACHE_PATH)
    expect(root_notice_packages == expected_notice_count,
      "PACKAGE_CACHE_NOTICE_COUNT", PACKAGE_CACHE_PATH)
    expect(license_metadata_count == 0, "PACKAGE_CACHE_LICENSE_METADATA_COUNT", PACKAGE_CACHE_PATH)
  end

  def safe_directory(relative, kind)
    unless repository_relative_path?(relative)
      add("#{kind}_PATH_INVALID", relative)
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
    unless File.lstat(cursor).directory?
      add("#{kind}_NOT_DIRECTORY", relative)
      return nil
    end
    cursor
  end

  def safe_regular_absolute(absolute, boundary, kind, max_bytes)
    absolute = Pathname.new(absolute).expand_path
    boundary = Pathname.new(boundary).expand_path
    relative = absolute.relative_path_from(boundary).to_s.tr("\\", "/")
    unless repository_relative_path?(relative)
      add("#{kind}_PATH_INVALID", relative_to_root(absolute))
      return nil
    end
    cursor = boundary
    relative.split("/").each do |component|
      begin
        children = Dir.children(cursor)
        unless children.include?(component)
          rule = children.any? { |name| name.casecmp(component).zero? } ?
            "#{kind}_CASE_MISMATCH" : "#{kind}_MISSING"
          add(rule, relative_to_root(absolute))
          return nil
        end
      rescue Errno::ENOENT, Errno::ENOTDIR
        add("#{kind}_MISSING", relative_to_root(absolute))
        return nil
      end
      cursor = cursor.join(component)
      begin
        stat = File.lstat(cursor)
      rescue Errno::ENOENT, Errno::ENOTDIR
        add("#{kind}_MISSING", relative_to_root(absolute))
        return nil
      end
      if stat.symlink?
        add("#{kind}_SYMLINK", relative_to_root(absolute))
        return nil
      end
    end
    stat = File.lstat(cursor)
    unless stat.file?
      add("#{kind}_NOT_FILE", relative_to_root(absolute))
      return nil
    end
    if stat.size > max_bytes
      add("#{kind}_TOO_LARGE", relative_to_root(absolute))
      return nil
    end
    cursor
  rescue ArgumentError
    add("#{kind}_PATH_INVALID", relative_to_root(absolute))
    nil
  end

  def relative_to_root(path)
    Pathname.new(path).expand_path.relative_path_from(@root).to_s.tr("\\", "/")
  rescue ArgumentError
    "<outside-root>"
  end

  def print_report
    puts "LICENSE_INVENTORY_AUDIT=LIC-001"
    puts "GIT_ACTIVE_FILES=#{@active_paths.length}"
    puts "PACKAGE_INVENTORY_COUNT=#{(@packages || []).length}"
    puts "REVIEW_ASSET_COUNT=#{(@review_items || []).length}"
    puts "FIRST_PARTY_ASSET_COUNT=#{(@first_party_items || []).length}"
    puts "PACKAGE_CACHE_VERIFIED=#{@verify_package_cache}"
    if @verify_package_cache
      puts "PACKAGE_CACHE_MATCHES=#{@package_cache_matches}"
      puts "PACKAGE_CACHE_EVIDENCE_LOCATORS=#{@package_cache_locators}"
    end
    @violations.sort.each do |rule, path|
      puts "VIOLATION rule=#{rule} path=#{path}"
    end
    puts "TOTAL_VIOLATIONS=#{@violations.length}"
    puts "FINAL_RESULT=#{@violations.empty? ? "PASS" : "FAIL"}"
  end
end

options = { verify_package_cache: false }
parser = OptionParser.new do |arguments|
  arguments.banner = "usage: ruby tools/verify_license_inventory.rb [--root PATH] [--verify-package-cache]"
  arguments.on("--root PATH", "Git worktree to audit") { |path| options[:root] = path }
  arguments.on("--verify-package-cache", "Also verify the current Unity PackageCache") do
    options[:verify_package_cache] = true
  end
end

begin
  parser.parse!
  raise LicenseInventoryAuditError, "UNEXPECTED_ARGUMENT" unless ARGV.empty?

  default_root = Pathname.new(__dir__).join("..").expand_path
  root = Pathname.new(options.fetch(:root, default_root.to_s)).expand_path
  raise LicenseInventoryAuditError, "INVALID_ROOT" unless root.directory?
  raise LicenseInventoryAuditError, "ROOT_SYMLINK" if File.lstat(root).symlink?

  exit LicenseInventoryVerifier.new(root, verify_package_cache: options[:verify_package_cache]).run
rescue OptionParser::ParseError
  warn "LICENSE_INVENTORY_AUDIT=ERROR reason=USAGE"
  exit 2
rescue LicenseInventoryAuditError => error
  warn "LICENSE_INVENTORY_AUDIT=ERROR reason=#{error.message}"
  exit 2
rescue StandardError
  warn "LICENSE_INVENTORY_AUDIT=ERROR reason=UNEXPECTED"
  exit 2
end
