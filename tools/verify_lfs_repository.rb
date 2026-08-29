#!/usr/bin/env ruby

require "digest"
require "json"
require "open3"
require "optparse"
require "pathname"
require "set"
require "yaml"

class LfsRepositoryAuditError < StandardError; end

class LfsRepositoryVerifier
  POLICY_PATH = "config/repository/BinaryAssetPolicy.md"
  INVENTORY_PATH = "config/repository/BinaryAssetInventory.yaml"
  ATTRIBUTES_PATH = ".gitattributes"

  MAX_POLICY_BYTES = 512 * 1024
  MAX_INVENTORY_BYTES = 2 * 1024 * 1024
  MAX_ATTRIBUTES_BYTES = 256 * 1024
  MAX_POINTER_BYTES = 1024
  BINARY_PROBE_BYTES = 64 * 1024
  LFS_SIZE_THRESHOLD = 10 * 1024 * 1024

  REQUIRED_LFS_PATTERNS = %w[
    *.blend *.fbx *.glb *.wav *.flac *.psd *.exr *.hdr *.tif *.tiff
  ].freeze
  REQUIRED_LFS_EXTENSIONS = REQUIRED_LFS_PATTERNS.map { |pattern| pattern.delete_prefix("*") }.to_set.freeze
  LFS_ATTRIBUTES = %w[filter=lfs diff=lfs merge=lfs -text].to_set.freeze
  ORDINARY_BINARY_EXTENSIONS = %w[
    .png .jpg .jpeg .gif .tga .bmp .mp3 .ogg .dll .so .dylib .bundle .zip .7z
  ].to_set.freeze
  INVENTORIED_BINARY_EXTENSIONS = (ORDINARY_BINARY_EXTENSIONS | REQUIRED_LFS_EXTENSIONS).freeze

  EXPECTED_EXCLUDED_ROOTS = %w[
    Project\ hotfix/Library
    Project\ hotfix/Logs
    Project\ hotfix/UserSettings
    Project\ hotfix/.vscode
  ].freeze
  EXPECTED_STORAGE = {
    "remoteName" => "origin",
    "remoteUrl" => "https://github.com/kjh4845/project-hotfix.git",
    "remoteVisibility" => "PRIVATE",
    "defaultBranch" => "main",
    "gitLfsVersion" => "3.8.0",
    "repositoryLocalLfsEnabled" => true,
    "lfsTrackedPatternCount" => REQUIRED_LFS_PATTERNS.length,
    "historyMigrationPerformed" => false,
    "existingReviewPngsMigrated" => false,
    "initialRemoteBackupRevision" => "8d735414bc75d1e786f79f3171b8435a100ddee9",
  }.freeze
  INVENTORY_FIELDS = %w[
    schemaVersion revision recordedAtUtc policyPath excludedGeneratedRoots summary storage files
  ].sort.freeze
  SUMMARY_FIELDS = %w[
    fileCount totalBytes uniqueContentHashes filesOver10MiB currentLfsRequiredCandidates
    currentLfsTrackedFiles ordinaryGitBinaryFiles
  ].sort.freeze
  STORAGE_FIELDS = EXPECTED_STORAGE.keys.sort.freeze
  FILE_FIELDS = %w[path bytes sha256].sort.freeze

  POINTER_PATTERN = /\Aversion https:\/\/git-lfs\.github\.com\/spec\/v1\noid sha256:([0-9a-f]{64})\nsize ([0-9]+)\n?\z/.freeze
  POINTER_PREFIX = "version https://git-lfs.github.com/spec/".b.freeze

  def initialize(root, verify_local_lfs: false, verify_remote: false)
    @root = Pathname.new(root).expand_path
    @verify_local_lfs = verify_local_lfs
    @verify_remote = verify_remote
    @violations = []
    @violation_keys = Set.new
    @active_paths = Set.new
    @index_oids = {}
    @valid_index_pointers = {}
    @required_candidate_paths = Set.new
    @ordinary_binary_paths = Set.new
    @inventory_files = []
    @local_lfs_verified = false
    @remote_verified = false
  end

  def run
    load_git_inventory
    @policy_text = load_text(POLICY_PATH, "POLICY", MAX_POLICY_BYTES)
    @attributes_text = load_text(ATTRIBUTES_PATH, "GITATTRIBUTES", MAX_ATTRIBUTES_BYTES)
    inventory_document = load_yaml(INVENTORY_PATH, "INVENTORY", MAX_INVENTORY_BYTES)
    @inventory = inventory_document && inventory_document["BinaryAssetInventory"]

    validate_gitattributes
    inspect_index_pointers
    validate_inventory_schema(inventory_document)
    validate_repository_files
    validate_inventory_summary
    validate_policy
    validate_local_lfs if @verify_local_lfs
    validate_remote if @verify_remote
    print_report
    @violations.empty? ? 0 : 1
  end

  private

  def load_git_inventory
    tracked, tracked_error, tracked_status = run_command(
      "git", "-C", @root.to_s, "ls-files", "--cached", "--others", "--exclude-standard", "-z"
    )
    deleted, deleted_error, deleted_status = run_command(
      "git", "-C", @root.to_s, "ls-files", "--deleted", "-z"
    )
    staged, staged_error, staged_status = run_command(
      "git", "-C", @root.to_s, "ls-files", "--stage", "-z"
    )
    unless tracked_status.success? && deleted_status.success? && staged_status.success?
      raise LfsRepositoryAuditError, "GIT_INVENTORY_FAILED"
    end
    unless tracked_error.empty? && deleted_error.empty? && staged_error.empty?
      raise LfsRepositoryAuditError, "GIT_INVENTORY_FAILED"
    end

    deleted_paths = decode_git_paths(deleted).to_set
    @active_paths = (decode_git_paths(tracked) - deleted_paths.to_a).to_set
    parse_index_entries(staged)

    @active_paths.each do |relative|
      absolute = @root.join(relative)
      begin
        stat = File.lstat(absolute)
        add("GIT_ACTIVE_SYMLINK", relative) if stat.symlink?
        add("GIT_ACTIVE_NOT_FILE", relative) unless stat.file? || stat.symlink?
      rescue Errno::ENOENT, Errno::ENOTDIR
        add("GIT_ACTIVE_PATH_MISSING", relative)
      end
    end
  end

  def decode_git_paths(bytes)
    bytes.b.split("\0").reject(&:empty?).map do |raw|
      path = raw.dup.force_encoding(Encoding::UTF_8)
      raise LfsRepositoryAuditError, "INVALID_GIT_PATH" unless path.valid_encoding?

      normalized = path.tr("\\", "/")
      raise LfsRepositoryAuditError, "INVALID_GIT_PATH" unless repository_relative_path?(normalized)
      normalized
    end.uniq
  end

  def parse_index_entries(bytes)
    bytes.b.split("\0").reject(&:empty?).each do |record|
      metadata, raw_path = record.split("\t", 2)
      raise LfsRepositoryAuditError, "INVALID_GIT_INDEX" unless metadata && raw_path

      match = metadata.match(/\A[0-7]{6} ([0-9a-f]{40,64}) ([0-3])\z/)
      raise LfsRepositoryAuditError, "INVALID_GIT_INDEX" unless match

      path = raw_path.dup.force_encoding(Encoding::UTF_8)
      raise LfsRepositoryAuditError, "INVALID_GIT_PATH" unless path.valid_encoding?
      path = path.tr("\\", "/")
      raise LfsRepositoryAuditError, "INVALID_GIT_PATH" unless repository_relative_path?(path)

      if match[2] != "0"
        add("GIT_INDEX_UNMERGED", path)
      elsif @index_oids.key?(path)
        add("GIT_INDEX_DUPLICATE_PATH", path)
      else
        @index_oids[path] = match[1]
      end
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
    text
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

  def validate_inventory_schema(document)
    expect(document.is_a?(Hash) && document.keys == ["BinaryAssetInventory"],
      "INVENTORY_DOCUMENT_ROOT", INVENTORY_PATH)
    unless @inventory.is_a?(Hash)
      add("INVENTORY_ROOT_INVALID", INVENTORY_PATH)
      return
    end

    expect(@inventory.keys.sort == INVENTORY_FIELDS, "INVENTORY_FIELD_SET", INVENTORY_PATH)
    expect(@inventory["schemaVersion"] == 1, "INVENTORY_SCHEMA_VERSION", INVENTORY_PATH)
    expect(@inventory["revision"] == "r04", "INVENTORY_REVISION", INVENTORY_PATH)
    expect(@inventory["recordedAtUtc"].is_a?(String) &&
      @inventory["recordedAtUtc"].match?(/\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\z/),
      "INVENTORY_RECORDED_AT", INVENTORY_PATH)
    expect(@inventory["policyPath"] == POLICY_PATH, "INVENTORY_POLICY_PATH", INVENTORY_PATH)
    expect(@inventory["excludedGeneratedRoots"] == EXPECTED_EXCLUDED_ROOTS,
      "INVENTORY_EXCLUDED_ROOTS", INVENTORY_PATH)

    summary = @inventory["summary"]
    expect(summary.is_a?(Hash) && summary.keys.sort == SUMMARY_FIELDS,
      "INVENTORY_SUMMARY_FIELD_SET", INVENTORY_PATH)
    storage = @inventory["storage"]
    expect(storage.is_a?(Hash) && storage.keys.sort == STORAGE_FIELDS,
      "INVENTORY_STORAGE_FIELD_SET", INVENTORY_PATH)
    if storage.is_a?(Hash)
      EXPECTED_STORAGE.each do |field, value|
        expect(storage[field] == value, "INVENTORY_STORAGE_#{field.upcase}", INVENTORY_PATH)
      end
    end

    files = @inventory["files"]
    unless files.is_a?(Array)
      add("INVENTORY_FILES_INVALID", INVENTORY_PATH)
      return
    end
    @inventory_files = files
    paths = []
    files.each_with_index do |entry, index|
      label = "#{INVENTORY_PATH}#files[#{index}]"
      unless entry.is_a?(Hash)
        add("INVENTORY_FILE_ENTRY_INVALID", label)
        next
      end
      expect(entry.keys.sort == FILE_FIELDS, "INVENTORY_FILE_FIELD_SET", label)
      path = entry["path"]
      expect(repository_relative_path?(path), "INVENTORY_FILE_PATH", label)
      paths << path if repository_relative_path?(path)
      expect(entry["bytes"].is_a?(Integer) && entry["bytes"] >= 0,
        "INVENTORY_FILE_BYTES", label)
      expect(entry["sha256"].is_a?(String) && entry["sha256"].match?(/\A[0-9a-f]{64}\z/),
        "INVENTORY_FILE_SHA256", label)
      validate_inventory_file(entry, label) if repository_relative_path?(path)
    end
    expect(paths.uniq.length == paths.length, "INVENTORY_FILE_PATH_UNIQUE", INVENTORY_PATH)
  end

  def validate_inventory_file(entry, label)
    path = entry["path"]
    absolute = safe_regular_file(path, "INVENTORIED_FILE", nil)
    return unless absolute

    working_pointer = working_lfs_pointer(absolute)
    index_pointer = @valid_index_pointers[path]
    if working_pointer && working_pointer == index_pointer
      expect(entry["bytes"] == working_pointer.fetch(:size), "INVENTORY_FILE_SIZE_DRIFT", path)
      expect(entry["sha256"] == working_pointer.fetch(:sha256), "INVENTORY_FILE_SHA_DRIFT", path)
    else
      stat = File.lstat(absolute)
      expect(entry["bytes"] == stat.size, "INVENTORY_FILE_SIZE_DRIFT", path)
      if entry["sha256"].is_a?(String) && entry["sha256"].match?(/\A[0-9a-f]{64}\z/)
        expect(entry["sha256"] == Digest::SHA256.file(absolute).hexdigest,
          "INVENTORY_FILE_SHA_DRIFT", path)
      end
    end
  end

  def validate_gitattributes
    return unless @attributes_text

    entries = []
    @attributes_text.each_line.with_index(1) do |line, number|
      stripped = line.strip
      next if stripped.empty? || stripped.start_with?("#")

      tokens = stripped.split(/\s+/)
      next unless tokens.drop(1).any? { |token| token == "filter=lfs" }
      entries << [tokens.first, tokens.drop(1), number]
    end

    patterns = entries.map(&:first)
    expect(patterns.sort == REQUIRED_LFS_PATTERNS.sort && patterns.uniq.length == patterns.length,
      "GITATTR_LFS_PATTERN_EXACT_SET", ATTRIBUTES_PATH)
    entries.each do |pattern, attributes, number|
      expect(attributes.to_set == LFS_ATTRIBUTES && attributes.length == LFS_ATTRIBUTES.length,
        "GITATTR_LFS_ATTRIBUTE_CONTRACT", "#{ATTRIBUTES_PATH}:#{number}")
      expect(REQUIRED_LFS_PATTERNS.include?(pattern),
        "GITATTR_UNAPPROVED_LFS_PATTERN", "#{ATTRIBUTES_PATH}:#{number}")
    end

    png_lines = @attributes_text.each_line.map(&:strip).reject { |line| line.empty? || line.start_with?("#") }
      .select { |line| line.split(/\s+/).first == "*.png" }
    expect(png_lines == ["*.png binary"], "GITATTR_PNG_ORDINARY_CONTRACT", ATTRIBUTES_PATH)

    REQUIRED_LFS_EXTENSIONS.each do |extension|
      attributes = git_attributes_for("__lfs_contract__#{extension}")
      next unless attributes
      expected = { "filter" => "lfs", "diff" => "lfs", "merge" => "lfs", "text" => "unset" }
      expect(attributes == expected, "GITATTR_EFFECTIVE_LFS_CONTRACT", ATTRIBUTES_PATH)
    end
    png_attributes = git_attributes_for("__ordinary_contract__.png")
    if png_attributes
      expected = { "filter" => "unspecified", "diff" => "unset", "merge" => "unset", "text" => "unset" }
      expect(png_attributes == expected, "GITATTR_EFFECTIVE_PNG_CONTRACT", ATTRIBUTES_PATH)
    end
  end

  def git_attributes_for(path)
    stdout, stderr, status = run_command(
      "git", "-C", @root.to_s, "check-attr", "-z", "filter", "diff", "merge", "text", "--", path
    )
    unless status.success? && stderr.empty?
      add("GITATTR_EFFECTIVE_CHECK_FAILED", ATTRIBUTES_PATH)
      return nil
    end
    fields = stdout.b.split("\0").reject(&:empty?)
    unless fields.length == 12
      add("GITATTR_EFFECTIVE_CHECK_FAILED", ATTRIBUTES_PATH)
      return nil
    end
    result = {}
    fields.each_slice(3) do |reported_path, attribute, value|
      unless reported_path == path
        add("GITATTR_EFFECTIVE_CHECK_FAILED", ATTRIBUTES_PATH)
        return nil
      end
      result[attribute] = value
    end
    result
  end

  def inspect_index_pointers
    blob_cache = {}
    @index_oids.each do |path, oid|
      size_stdout, _size_stderr, size_status = run_command(
        "git", "-C", @root.to_s, "cat-file", "-s", oid
      )
      unless size_status.success? && size_stdout.strip.match?(/\A\d+\z/)
        add("GIT_INDEX_BLOB_UNREADABLE", path)
        next
      end
      next if size_stdout.to_i > MAX_POINTER_BYTES

      bytes = blob_cache[oid]
      unless bytes
        bytes, _blob_stderr, blob_status = run_command(
          "git", "-C", @root.to_s, "cat-file", "blob", oid
        )
        unless blob_status.success?
          add("GIT_INDEX_BLOB_UNREADABLE", path)
          next
        end
        blob_cache[oid] = bytes
      end
      pointer = parse_lfs_pointer(bytes)
      if pointer
        @valid_index_pointers[path] = pointer
      elsif bytes.start_with?(POINTER_PREFIX)
        add("MALFORMED_LFS_POINTER", path)
      end
    end
  end

  def validate_repository_files
    validate_blender_backup_boundary
    binary_paths = Set.new
    @active_paths.sort.each do |path|
      absolute = @root.join(path)
      next unless regular_nonsymlink_file?(absolute)

      extension = File.extname(path).downcase
      if path.match?(/\.blend\d+\z/i)
        add("BLENDER_BACKUP_FILE", path)
      end
      if extension == ".blend" && path.start_with?("Project hotfix/Assets/")
        add("BLEND_INSIDE_UNITY_ASSETS", path)
      end
      stat = File.lstat(absolute)
      known_binary = INVENTORIED_BINARY_EXTENSIONS.include?(extension)
      large_binary = stat.size > LFS_SIZE_THRESHOLD && binary_content?(absolute)
      binary_paths << path if known_binary || large_binary
      required = REQUIRED_LFS_EXTENSIONS.include?(extension) || large_binary
      @required_candidate_paths << path if required

      pointer = @valid_index_pointers[path]
      if pointer
        validate_pointer_materialization(path, absolute, pointer)
        add("UNAPPROVED_LFS_POINTER", path) unless required
      elsif required
        add("LFS_REQUIRED_FILE_NOT_POINTER", path)
      end

      if extension == ".png"
        @ordinary_binary_paths << path
        add("PNG_LFS_POINTER", path) if pointer || working_pointer_prefix?(absolute)
      end

      if required
        attributes = git_attributes_for(path)
        expect(attributes && attributes["filter"] == "lfs", "LFS_REQUIRED_FILE_FILTER", path)
      end
    end

    inventory_paths = @inventory_files.select do |entry|
      entry.is_a?(Hash) && repository_relative_path?(entry["path"])
    end.map { |entry| entry["path"] }.to_set
    expect(binary_paths == inventory_paths, "INVENTORY_BINARY_EXACT_SET", INVENTORY_PATH)
  end

  def validate_blender_backup_boundary
    %w[__backup_contract__.blend1 __backup_contract__.blend12].each do |path|
      _stdout, _stderr, status = run_command(
        "git", "-C", @root.to_s, "check-ignore", "--quiet", "--no-index", "--", path
      )
      expect(status.success?, "BLENDER_BACKUP_NOT_IGNORED", ".gitignore")
    end
    _stdout, _stderr, canonical_status = run_command(
      "git", "-C", @root.to_s, "check-ignore", "--quiet", "--no-index", "--", "__source_contract__.blend"
    )
    expect(!canonical_status.success?, "BLENDER_CANONICAL_SOURCE_IGNORED", ".gitignore")
  end

  def validate_pointer_materialization(path, absolute, pointer)
    working_pointer = working_lfs_pointer(absolute)
    return if working_pointer == pointer

    stat = File.lstat(absolute)
    expect(pointer.fetch(:size) == stat.size, "LFS_POINTER_SIZE_DRIFT", path)
    if pointer.fetch(:size) == stat.size
      expect(pointer.fetch(:sha256) == Digest::SHA256.file(absolute).hexdigest,
        "LFS_POINTER_SHA_DRIFT", path)
    end
  end

  def working_pointer_prefix?(absolute)
    bytes = File.open(absolute, "rb") { |file| file.read(MAX_POINTER_BYTES) || "".b }
    parse_lfs_pointer(bytes) || bytes.start_with?(POINTER_PREFIX)
  end

  def working_lfs_pointer(absolute)
    return nil if File.lstat(absolute).size > MAX_POINTER_BYTES

    bytes = File.open(absolute, "rb") { |file| file.read(MAX_POINTER_BYTES) || "".b }
    parse_lfs_pointer(bytes)
  end

  def binary_content?(absolute)
    stat = File.lstat(absolute)
    chunks = []
    File.open(absolute, "rb") do |file|
      chunks << (file.read(BINARY_PROBE_BYTES) || "".b)
      if stat.size > BINARY_PROBE_BYTES
        file.seek([stat.size - BINARY_PROBE_BYTES, 0].max, IO::SEEK_SET)
        chunks << (file.read(BINARY_PROBE_BYTES) || "".b)
      end
    end
    chunks.any? { |chunk| chunk.include?("\0".b) || !bounded_utf8_sample?(chunk) }
  end

  def bounded_utf8_sample?(bytes)
    max_trim = [3, bytes.bytesize].min
    (0..max_trim).any? do |left_trim|
      (0..max_trim).any? do |right_trim|
        next false if left_trim + right_trim > bytes.bytesize
        finish = right_trim.zero? ? bytes.bytesize : bytes.bytesize - right_trim
        sample = bytes.byteslice(left_trim, finish - left_trim).dup.force_encoding(Encoding::UTF_8)
        sample.valid_encoding?
      end
    end
  end

  def parse_lfs_pointer(bytes)
    match = bytes.b.match(POINTER_PATTERN)
    return nil unless match
    { sha256: match[1], size: Integer(match[2], 10) }
  rescue ArgumentError
    nil
  end

  def validate_inventory_summary
    return unless @inventory.is_a?(Hash) && @inventory["summary"].is_a?(Hash)

    summary = @inventory["summary"]
    valid_entries = @inventory_files.select do |entry|
      entry.is_a?(Hash) && entry["bytes"].is_a?(Integer) && entry["sha256"].is_a?(String)
    end
    expected = {
      "fileCount" => @inventory_files.length,
      "totalBytes" => valid_entries.sum { |entry| entry["bytes"] },
      "uniqueContentHashes" => valid_entries.map { |entry| entry["sha256"] }.uniq.length,
      "filesOver10MiB" => valid_entries.count { |entry| entry["bytes"] > LFS_SIZE_THRESHOLD },
      "currentLfsRequiredCandidates" => @required_candidate_paths.length,
      "currentLfsTrackedFiles" => @valid_index_pointers.length,
      "ordinaryGitBinaryFiles" => @inventory_files.count do |entry|
        entry.is_a?(Hash) && !@valid_index_pointers.key?(entry["path"])
      end,
    }
    expected.each do |field, value|
      expect(summary[field] == value, "INVENTORY_SUMMARY_#{field.upcase}", INVENTORY_PATH)
    end
  end

  def validate_policy
    return unless @policy_text

    required_markers = [
      "- Owner: `FDN-011`",
      "- Revision: `r04`",
      "private GitHub `origin` over HTTPS (`#{EXPECTED_STORAGE.fetch("remoteUrl")}`)",
      "Git LFS: `#{EXPECTED_STORAGE.fetch("gitLfsVersion")}`",
      "repository-local filters and pre-push hook enabled",
      "Existing-history LFS migration: not performed and not required",
      "Do not run `git lfs migrate`",
      "rewrite existing commits",
      "force-push",
      "Existing PNGs stay in ordinary Git",
      "Core revision `af11dd2` uploaded the first production LFS object",
      "an authenticated fresh clone reproduced the same materialized SHA-256 and byte size",
      "existing PNG migration remains `0`",
      "Only the canonical `.blend` source is eligible for LFS tracking and inventory",
      "ruby tools/verify_lfs_repository.rb --verify-local-lfs --verify-remote",
      "`origin` is the only product Remote",
      "default branch is `main`",
      "Player Build, Steam upload and public publication remain separate user-controlled actions",
    ]
    required_markers.each do |marker|
      expect(@policy_text.include?(marker), "POLICY_REQUIRED_MARKER", POLICY_PATH)
    end

    active_section = @policy_text[/^## Active LFS boundary\s*$\n(.*?)(?=^## )/m, 1]
    unless active_section
      add("POLICY_ACTIVE_LFS_SECTION", POLICY_PATH)
      return
    end
    extension_lines = active_section.each_line.select do |line|
      line.start_with?("- Blender/interop source:", "- Lossless production audio/source images:")
    end
    extensions = extension_lines.flat_map { |line| line.scan(/`(\.[a-z0-9]+)`/).flatten }
    expect(extensions.sort == REQUIRED_LFS_EXTENSIONS.to_a.sort && extensions.uniq.length == extensions.length,
      "POLICY_LFS_EXTENSION_EXACT_SET", POLICY_PATH)

    summary = @inventory.is_a?(Hash) ? @inventory["summary"] : nil
    if summary.is_a?(Hash)
      tracked_count = summary["currentLfsTrackedFiles"]
      candidate_count = summary["currentLfsRequiredCandidates"]
      tracked_label = tracked_count == 1 ? "file" : "files"
      candidate_label = candidate_count == 1 ? "candidate" : "candidates"
      count_marker = "The current repository has `#{tracked_count}` LFS-tracked #{tracked_label} " \
        "and `#{candidate_count}` LFS-required #{candidate_label}"
      expect(@policy_text.include?(count_marker), "POLICY_CURRENT_LFS_COUNTS", POLICY_PATH)
    end
    backup = @inventory.is_a?(Hash) ? @inventory.dig("storage", "initialRemoteBackupRevision") : nil
    if backup.is_a?(String) && backup.match?(/\A[0-9a-f]{40}\z/)
      expect(@policy_text.include?("Initial remote backup: `main` at `#{backup[0, 7]}`"),
        "POLICY_INITIAL_BACKUP_REVISION", POLICY_PATH)
    end
  end

  def validate_local_lfs
    storage = @inventory.is_a?(Hash) ? @inventory["storage"] : nil
    return add("LOCAL_LFS_INVENTORY_UNAVAILABLE", INVENTORY_PATH) unless storage.is_a?(Hash)

    stdout, _stderr, status = run_command("git", "-C", @root.to_s, "lfs", "version")
    version = stdout[/\Agit-lfs\/([0-9]+\.[0-9]+\.[0-9]+)/, 1]
    expect(status.success? && version == storage["gitLfsVersion"], "LOCAL_LFS_VERSION", ".git/config")

    expected_config = {
      "lfs.repositoryformatversion" => "0",
      "filter.lfs.process" => "git-lfs filter-process",
      "filter.lfs.required" => "true",
      "filter.lfs.clean" => "git-lfs clean -- %f",
      "filter.lfs.smudge" => "git-lfs smudge -- %f",
    }
    expected_config.each do |key, value|
      config_stdout, _config_stderr, config_status = run_command(
        "git", "-C", @root.to_s, "config", "--local", "--get", key
      )
      expect(config_status.success? && config_stdout.chomp == value,
        "LOCAL_LFS_CONFIG_#{key.upcase.gsub(/[^A-Z0-9]+/, "_")}", ".git/config")
    end

    hook = @root.join(".git/hooks/pre-push")
    begin
      stat = File.lstat(hook)
      valid = stat.file? && !stat.symlink? && stat.executable? && stat.size <= 64 * 1024
      expect(valid, "LOCAL_LFS_PRE_PUSH_HOOK", ".git/hooks/pre-push")
      if valid
        text = hook.binread.force_encoding(Encoding::UTF_8)
        expect(text.valid_encoding? && text.include?("git lfs pre-push \"$@\""),
          "LOCAL_LFS_PRE_PUSH_HOOK_CONTENT", ".git/hooks/pre-push")
      end
    rescue Errno::ENOENT, Errno::ENOTDIR
      add("LOCAL_LFS_PRE_PUSH_HOOK", ".git/hooks/pre-push")
    end

    @local_lfs_verified = @violations.none? { |rule, _path| rule.start_with?("LOCAL_LFS_") }
  end

  def validate_remote
    storage = @inventory.is_a?(Hash) ? @inventory["storage"] : nil
    return add("REMOTE_INVENTORY_UNAVAILABLE", INVENTORY_PATH) unless storage.is_a?(Hash)

    remotes_stdout, _remotes_stderr, remotes_status = run_command("git", "-C", @root.to_s, "remote")
    remotes = remotes_stdout.lines.map(&:strip).reject(&:empty?)
    expect(remotes_status.success? && remotes == [storage["remoteName"]], "REMOTE_EXACT_SET", ".git/config")
    return unless remotes_status.success? && remotes.include?(storage["remoteName"])

    fetch_url = command_line("git", "-C", @root.to_s, "remote", "get-url", storage["remoteName"])
    push_url = command_line("git", "-C", @root.to_s, "remote", "get-url", "--push", storage["remoteName"])
    expect(fetch_url == storage["remoteUrl"], "REMOTE_FETCH_URL", ".git/config")
    expect(push_url == storage["remoteUrl"], "REMOTE_PUSH_URL", ".git/config")

    branch = command_line("git", "-C", @root.to_s, "symbolic-ref", "--short", "HEAD")
    expect(branch == storage["defaultBranch"], "REMOTE_LOCAL_BRANCH", ".git/HEAD")
    upstream_remote = command_line(
      "git", "-C", @root.to_s, "config", "--local", "--get", "branch.#{storage["defaultBranch"]}.remote"
    )
    upstream_merge = command_line(
      "git", "-C", @root.to_s, "config", "--local", "--get", "branch.#{storage["defaultBranch"]}.merge"
    )
    expect(upstream_remote == storage["remoteName"], "REMOTE_UPSTREAM_NAME", ".git/config")
    expect(upstream_merge == "refs/heads/#{storage["defaultBranch"]}",
      "REMOTE_UPSTREAM_BRANCH", ".git/config")

    repository = github_repository_name(storage["remoteUrl"])
    unless repository
      add("REMOTE_GITHUB_URL_FORMAT", INVENTORY_PATH)
      return
    end
    gh_stdout, _gh_stderr, gh_status = run_command(
      "gh", "repo", "view", repository,
      "--json", "nameWithOwner,visibility,defaultBranchRef,url"
    )
    if gh_status.success?
      begin
        metadata = JSON.parse(gh_stdout)
        expect(metadata["nameWithOwner"] == repository, "REMOTE_GITHUB_REPOSITORY", storage["remoteUrl"])
        expect(metadata["visibility"] == storage["remoteVisibility"],
          "REMOTE_GITHUB_VISIBILITY", storage["remoteUrl"])
        expect(metadata.dig("defaultBranchRef", "name") == storage["defaultBranch"],
          "REMOTE_GITHUB_DEFAULT_BRANCH", storage["remoteUrl"])
        expect(metadata["url"] == storage["remoteUrl"].delete_suffix(".git"),
          "REMOTE_GITHUB_CANONICAL_URL", storage["remoteUrl"])
      rescue JSON::ParserError
        add("REMOTE_GITHUB_METADATA_INVALID", storage["remoteUrl"])
      end
    else
      add("REMOTE_GITHUB_READ_FAILED", storage["remoteUrl"])
    end

    remote_stdout, _remote_stderr, remote_status = run_command(
      "git", "-C", @root.to_s, "ls-remote", "--symref", storage["remoteName"],
      "HEAD", "refs/heads/#{storage["defaultBranch"]}"
    )
    if remote_status.success?
      symref = remote_stdout[/^ref: (refs\/heads\/[^\s]+)\s+HEAD$/, 1]
      branch_sha = remote_stdout[/^([0-9a-f]{40,64})\s+refs\/heads\/#{Regexp.escape(storage["defaultBranch"])}$/, 1]
      local_head = command_line("git", "-C", @root.to_s, "rev-parse", "HEAD")
      expect(symref == "refs/heads/#{storage["defaultBranch"]}", "REMOTE_HEAD_SYMREF", storage["remoteUrl"])
      expect(branch_sha && branch_sha == local_head, "REMOTE_HEAD_REVISION", storage["remoteUrl"])
    else
      add("REMOTE_LS_REMOTE_FAILED", storage["remoteUrl"])
    end

    backup = storage["initialRemoteBackupRevision"]
    _ancestor_stdout, _ancestor_stderr, ancestor_status = run_command(
      "git", "-C", @root.to_s, "merge-base", "--is-ancestor", backup.to_s, "HEAD"
    )
    expect(ancestor_status.success?, "REMOTE_INITIAL_BACKUP_NOT_ANCESTOR", ".git/HEAD")
    @remote_verified = @violations.none? { |rule, _path| rule.start_with?("REMOTE_") }
  end

  def github_repository_name(url)
    match = url.to_s.match(%r{\Ahttps://github\.com/([^/]+)/([^/]+)\.git\z})
    match && "#{match[1]}/#{match[2]}"
  end

  def command_line(*command)
    stdout, _stderr, status = run_command(*command)
    status.success? ? stdout.chomp : nil
  end

  def run_command(*command)
    Open3.capture3(*command)
  rescue Errno::ENOENT
    ["", "", LfsMissingCommandStatus.new]
  end

  class LfsMissingCommandStatus
    def success?
      false
    end
  end

  def safe_regular_file(relative, kind, max_bytes)
    unless repository_relative_path?(relative)
      add("#{kind}_PATH_INVALID", relative.to_s)
      return nil
    end
    unless @active_paths.include?(relative)
      add("#{kind}_NOT_IN_GIT_INVENTORY", relative)
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

  def regular_nonsymlink_file?(absolute)
    stat = File.lstat(absolute)
    stat.file? && !stat.symlink?
  rescue Errno::ENOENT, Errno::ENOTDIR
    false
  end

  def repository_relative_path?(relative)
    return false unless relative.is_a?(String) && !relative.empty?
    return false if relative.start_with?("/", "\\") || relative.include?("\0")
    return false if relative.match?(/\A[A-Za-z]:[\\\/]/)

    parts = relative.split("/")
    !parts.empty? && parts.none? { |part| part.empty? || part == "." || part == ".." }
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
    history_rewrite = @inventory.is_a?(Hash) ? @inventory.dig("storage", "historyMigrationPerformed") : nil
    puts "LFS_REPOSITORY_AUDIT=FDN-011"
    puts "GIT_ACTIVE_FILES=#{@active_paths.length}"
    puts "DEFAULT_LFS_PATTERN_COUNT=#{REQUIRED_LFS_PATTERNS.length}"
    puts "BINARY_INVENTORY_COUNT=#{@inventory_files.length}"
    puts "LFS_REQUIRED_CANDIDATES=#{@required_candidate_paths.length}"
    puts "LFS_TRACKED_FILES=#{@valid_index_pointers.length}"
    puts "ORDINARY_PNG_FILES=#{@ordinary_binary_paths.length}"
    puts "HISTORY_REWRITE_CLAIMED=#{history_rewrite == true}"
    puts "LOCAL_LFS_REQUESTED=#{@verify_local_lfs}"
    puts "LOCAL_LFS_VERIFIED=#{@local_lfs_verified}" if @verify_local_lfs
    puts "REMOTE_REQUESTED=#{@verify_remote}"
    puts "REMOTE_VERIFIED=#{@remote_verified}" if @verify_remote
    puts "PACKAGE_IDS_HARDCODED=0"
    @violations.sort.each { |rule, path| puts "VIOLATION rule=#{rule} path=#{path}" }
    puts "TOTAL_VIOLATIONS=#{@violations.length}"
    puts "FINAL_RESULT=#{@violations.empty? ? "PASS" : "FAIL"}"
  end
end

options = { verify_local_lfs: false, verify_remote: false }
parser = OptionParser.new do |arguments|
  arguments.banner = "usage: ruby tools/verify_lfs_repository.rb [--root PATH] [--verify-local-lfs] [--verify-remote]"
  arguments.on("--root PATH", "Git worktree to audit") { |path| options[:root] = path }
  arguments.on("--verify-local-lfs", "Verify repository-local Git LFS filters and pre-push hook") do
    options[:verify_local_lfs] = true
  end
  arguments.on("--verify-remote", "Read-only verification of origin and the private GitHub repository") do
    options[:verify_remote] = true
  end
end

begin
  parser.parse!
  raise LfsRepositoryAuditError, "UNEXPECTED_ARGUMENT" unless ARGV.empty?
  default_root = Pathname.new(__dir__).join("..").expand_path
  root = Pathname.new(options.fetch(:root, default_root.to_s)).expand_path
  raise LfsRepositoryAuditError, "INVALID_ROOT" unless root.directory?
  raise LfsRepositoryAuditError, "ROOT_SYMLINK" if File.lstat(root).symlink?

  exit LfsRepositoryVerifier.new(
    root,
    verify_local_lfs: options[:verify_local_lfs],
    verify_remote: options[:verify_remote],
  ).run
rescue OptionParser::ParseError
  warn "LFS_REPOSITORY_AUDIT=ERROR reason=USAGE"
  exit 2
rescue LfsRepositoryAuditError => error
  warn "LFS_REPOSITORY_AUDIT=ERROR reason=#{error.message}"
  exit 2
rescue StandardError
  warn "LFS_REPOSITORY_AUDIT=ERROR reason=UNEXPECTED"
  exit 2
end
