#!/usr/bin/env ruby

require "json"
require "open3"
require "optparse"
require "pathname"
require "set"
require "yaml"

class AuditError < StandardError
end

Violation = Struct.new(:category, :rule_id, :path, :line, keyword_init: true)

class ForbiddenInfrastructureAudit
  def initialize(root, policy)
    @root = root
    @policy = policy
    @violations = []
    @violation_keys = Set.new
    @content_files_scanned = 0
    @package_manifests_scanned = 0
    @narrative_patterns = policy.fetch("contentScan").fetch("narrativePathPatterns").map do |pattern|
      Regexp.new(pattern, Regexp::IGNORECASE)
    end
  end

  def run
    inventory = git_inventory
    audit_inventory_entries(inventory)
    audit_artifact_paths(inventory)
    audit_content(inventory)
    audit_packages(inventory)
    print_report(inventory)
    @violations.empty? ? 0 : 1
  end

  private

  def git_inventory
    configured = @policy.fetch("inventory").fetch("command")
    expected = "git ls-files --cached --others --exclude-standard -z"
    raise AuditError, "UNSUPPORTED_INVENTORY_COMMAND" unless configured == expected

    paths = git_null_list("ls-files", "--cached", "--others", "--exclude-standard", "-z")
    deleted = git_null_list("ls-files", "--deleted", "-z").to_set
    deleted.sort.each do |path|
      add_violation("audit", "TRACKED_FILE_MISSING", path)
    end

    active_paths = paths.uniq.reject { |path| deleted.include?(path) }.sort
    active_paths.each do |path|
      relative = Pathname.new(path)
      if relative.absolute? || relative.cleanpath.to_s != path || path.include?("\0")
        add_violation("audit", "INVALID_GIT_PATH", safe_path(path))
      end
    end
    active_paths
  end

  def git_null_list(*arguments)
    stdout, _stderr, status = Open3.capture3("git", "-C", @root.to_s, *arguments)
    raise AuditError, "GIT_INVENTORY_FAILED" unless status.success?

    stdout.b.split("\0").reject(&:empty?).map do |path|
      path.force_encoding(Encoding::UTF_8)
      raise AuditError, "NON_UTF8_GIT_PATH" unless path.valid_encoding?

      path
    end
  end

  def audit_artifact_paths(inventory)
    rules = @policy.fetch("artifactRules")
    basename_groups = rules.fetch("forbiddenBasenameGroups")
    directory_groups = rules.fetch("forbiddenDirectorySegmentGroups")
    extension_groups = rules.fetch("forbiddenExtensionGroups")
    path_patterns = compile_rules(rules.fetch("forbiddenPathPatterns"))
    filename_patterns = compile_rules(rules.fetch("forbiddenFileNamePatterns"))

    inventory.each do |relative|
      normalized = relative.tr("\\", "/")
      basename = File.basename(normalized)
      basename_lower = basename.downcase
      directory_segments = normalized.split("/")[0...-1].map(&:downcase)
      extension = File.extname(basename).downcase
      narrative = narrative_path?(normalized)

      basename_groups.each do |group|
        values = group.fetch("values").map(&:downcase)
        if values.include?(basename_lower)
          add_violation(group.fetch("category"), group.fetch("id"), normalized)
        end
      end

      directory_groups.each do |group|
        next if narrative

        values = group.fetch("values").map(&:downcase)
        if (directory_segments & values).any?
          add_violation(group.fetch("category"), group.fetch("id"), normalized)
        end
      end

      extension_groups.each do |group|
        values = group.fetch("values").map(&:downcase)
        if values.include?(extension)
          add_violation(group.fetch("category"), group.fetch("id"), normalized)
        end
      end

      path_patterns.each do |rule, expression, _path_expression|
        next if narrative && rule["narrativeExempt"] == true

        if expression.match?(normalized)
          add_violation(rule.fetch("category"), rule.fetch("id"), normalized)
        end
      end

      filename_patterns.each do |rule, expression, _path_expression|
        next if narrative
        applicable_extensions = rule["applicableExtensions"]
        next if applicable_extensions && !applicable_extensions.map(&:downcase).include?(extension)

        if expression.match?(basename)
          add_violation(rule.fetch("category"), rule.fetch("id"), normalized)
        end
      end
    end
  end

  def audit_inventory_entries(inventory)
    inventory.each do |relative|
      normalized = relative.tr("\\", "/")
      begin
        stat = File.lstat(@root.join(normalized))
      rescue Errno::ENOENT
        add_violation("audit", "INVENTORY_ENTRY_MISSING", normalized)
        next
      end
      add_violation("audit", "UNSCANNABLE_SYMLINK", normalized) if stat.symlink?
    end
  end

  def audit_content(inventory)
    settings = @policy.fetch("contentScan")
    roots = settings.fetch("roots").map { |path| normalize_prefix(path) }
    exclusions = settings.fetch("excludedPathPrefixes").map { |path| normalize_prefix(path) }
    extensions = settings.fetch("extensions").map(&:downcase).to_set
    basenames = Array(settings["basenames"]).map(&:downcase).to_set
    max_bytes = Integer(settings.fetch("maxBytesPerFile"))
    rules = compile_rules(settings.fetch("rules"))

    inventory.each do |relative|
      normalized = relative.tr("\\", "/")
      next unless under_any_prefix?(normalized, roots)
      next if under_any_prefix?(normalized, exclusions)
      next if narrative_path?(normalized)

      basename = File.basename(normalized)
      extension = File.extname(basename).downcase
      absolute = @root.join(normalized)
      begin
        stat = File.lstat(absolute)
      rescue Errno::ENOENT
        add_violation("audit", "CONTENT_FILE_MISSING", normalized)
        next
      end
      if stat.symlink?
        add_violation("audit", "UNSCANNABLE_SYMLINK", normalized)
        next
      end
      next unless stat.file?
      executable = (stat.mode & 0o111).positive?
      next unless extensions.include?(extension) || basenames.include?(basename.downcase) || executable

      if stat.size > max_bytes
        add_violation("audit", "CONTENT_FILE_TOO_LARGE", normalized)
        next
      end

      bytes = File.binread(absolute)
      text = bytes.dup.force_encoding(Encoding::UTF_8)
      unless text.valid_encoding?
        add_violation("audit", "CONTENT_FILE_INVALID_UTF8", normalized)
        next
      end

      @content_files_scanned += 1
      rules.each do |rule, expression, path_expression|
        next if path_expression && !path_expression.match?(normalized)

        match = expression.match(text)
        next unless match

        line = text.byteslice(0, match.begin(0)).count("\n") + 1
        add_violation(rule.fetch("category"), rule.fetch("id"), normalized, line)
      end
    end
  end

  def audit_packages(inventory)
    settings = @policy.fetch("packageScan")
    inventory_set = inventory.to_set
    rules = compile_rules(settings.fetch("rules"))
    max_bytes = Integer(settings.fetch("maxBytesPerManifest"))

    settings.fetch("manifests").each do |relative|
      normalized = relative.tr("\\", "/")
      unless inventory_set.include?(normalized)
        add_violation("audit", "PACKAGE_MANIFEST_MISSING", normalized)
        next
      end

      absolute = @root.join(normalized)
      begin
        stat = File.lstat(absolute)
        if stat.symlink?
          add_violation("audit", "UNSCANNABLE_SYMLINK", normalized)
          next
        end
        unless stat.file?
          add_violation("audit", "INVALID_PACKAGE_MANIFEST", normalized)
          next
        end
        if stat.size > max_bytes
          add_violation("audit", "PACKAGE_MANIFEST_TOO_LARGE", normalized)
          next
        end

        bytes = File.binread(absolute)
        text = bytes.dup.force_encoding(Encoding::UTF_8)
        unless text.valid_encoding?
          add_violation("audit", "INVALID_PACKAGE_MANIFEST", normalized)
          next
        end
        document = JSON.parse(text)
      rescue JSON::ParserError, Errno::ENOENT, EncodingError
        add_violation("audit", "INVALID_PACKAGE_MANIFEST", normalized)
        next
      end

      dependencies = document.is_a?(Hash) ? document["dependencies"] : nil
      unless dependencies.is_a?(Hash)
        add_violation("audit", "INVALID_PACKAGE_MANIFEST", normalized)
        next
      end

      @package_manifests_scanned += 1
      dependencies.keys.sort.each do |package_id|
        dependency_value = dependencies[package_id]
        searchable_values = [package_id, dependency_value.to_s]
        rules.each do |rule, expression, _path_expression|
          if searchable_values.any? { |value| expression.match?(value) }
            add_violation(rule.fetch("category"), rule.fetch("id"), normalized)
          end
        end
      end
    end
  end

  def compile_rules(rules)
    rules.map do |rule|
      begin
        expression = Regexp.new(rule.fetch("pattern"), Regexp::IGNORECASE | Regexp::MULTILINE)
        path_expression = rule["pathPattern"] && Regexp.new(rule.fetch("pathPattern"), Regexp::IGNORECASE)
        [rule, expression, path_expression]
      rescue RegexpError, KeyError
        raise AuditError, "INVALID_POLICY_REGEX"
      end
    end
  end

  def normalize_prefix(path)
    path.tr("\\", "/").sub(%r{/+\z}, "")
  end

  def under_any_prefix?(path, prefixes)
    prefixes.any? do |prefix|
      prefix == "." || path == prefix || path.start_with?(prefix + "/")
    end
  end

  def narrative_path?(path)
    @narrative_patterns.any? { |pattern| pattern.match?(path) }
  end

  def add_violation(category, rule_id, path, line = nil)
    key = [category, rule_id, path, line]
    return unless @violation_keys.add?(key)

    @violations << Violation.new(
      category: category,
      rule_id: rule_id,
      path: safe_path(path),
      line: line
    )
  end

  def safe_path(path)
    path.encode(Encoding::UTF_8, invalid: :replace, undef: :replace, replace: "?")
      .gsub(/[[:cntrl:]]/, "?")
  end

  def print_report(inventory)
    reporting = @policy.fetch("reporting")
    categories = reporting.fetch("categories")
    puts "FORBIDDEN_INFRASTRUCTURE_AUDIT"
    puts "POLICY_ID=#{@policy.fetch("profileId")}"
    puts "INVENTORY_KIND=git_tracked_and_nonignored_untracked"
    puts "INVENTORY_FILES=#{inventory.length}"
    puts "CONTENT_FILES_SCANNED=#{@content_files_scanned}"
    puts "PACKAGE_MANIFESTS_SCANNED=#{@package_manifests_scanned}"
    categories.each do |category|
      count = @violations.count { |violation| violation.category == category }
      puts "#{category.upcase}_VIOLATIONS=#{count}"
    end
    puts "TOTAL_VIOLATIONS=#{@violations.length}"

    @violations.sort_by { |violation| [violation.category, violation.rule_id, violation.path, violation.line || 0] }
      .each do |violation|
        location = violation.line ? " line=#{violation.line}" : ""
        puts "VIOLATION category=#{violation.category} rule=#{violation.rule_id} path=#{violation.path}#{location}"
      end

    puts "FINAL_RESULT=#{@violations.empty? ? "PASS" : "FAIL"}"
  end
end

def validate_policy!(policy)
  raise AuditError, "INVALID_POLICY" unless policy["profileId"].is_a?(String) && !policy["profileId"].empty?
  raise AuditError, "INVALID_POLICY" unless policy["ownerTask"] == "FDN-009"

  inventory = policy["inventory"]
  raise AuditError, "INVALID_POLICY" unless inventory.is_a?(Hash)
  raise AuditError, "INVALID_POLICY" unless
    inventory["command"] == "git ls-files --cached --others --exclude-standard -z" &&
    inventory["includeTrackedAndNonIgnoredUntracked"] == true &&
    inventory["followSymlinks"] == false &&
    inventory["failWhenGitInventoryFails"] == true

  artifact = policy["artifactRules"]
  content = policy["contentScan"]
  packages = policy["packageScan"]
  reporting = policy["reporting"]
  raise AuditError, "INVALID_POLICY" unless [artifact, content, packages, reporting].all? { |value| value.is_a?(Hash) }

  artifact_group_keys = %w[
    forbiddenBasenameGroups
    forbiddenDirectorySegmentGroups
    forbiddenExtensionGroups
  ]
  artifact_pattern_keys = %w[forbiddenPathPatterns forbiddenFileNamePatterns]
  artifact_group_keys.each do |key|
    groups = artifact[key]
    raise AuditError, "INVALID_POLICY" unless groups.is_a?(Array) && !groups.empty?
    groups.each do |group|
      raise AuditError, "INVALID_POLICY" unless
        group.is_a?(Hash) && group["values"].is_a?(Array) && !group["values"].empty? &&
        group["values"].all? { |value| value.is_a?(String) && !value.empty? }
    end
  end
  artifact_pattern_keys.each do |key|
    rules = artifact[key]
    raise AuditError, "INVALID_POLICY" unless rules.is_a?(Array) && !rules.empty?
  end

  expected_exclusions = [
    "tools/verify_forbidden_infrastructure.rb",
    "tools/tests/verify_forbidden_infrastructure_test.rb",
    "config/infrastructure/ForbiddenInfrastructurePolicy.yaml",
  ]
  raise AuditError, "INVALID_POLICY" unless content["roots"] == ["."]
  raise AuditError, "INVALID_POLICY" unless
    content["excludedPathPrefixes"].is_a?(Array) &&
    content["excludedPathPrefixes"].sort == expected_exclusions.sort
  raise AuditError, "INVALID_POLICY" unless
    content["extensions"].is_a?(Array) && !content["extensions"].empty? &&
    content["basenames"].is_a?(Array) && !content["basenames"].empty? &&
    content["rules"].is_a?(Array) && !content["rules"].empty?
  max_content_bytes = Integer(content["maxBytesPerFile"], exception: false)
  raise AuditError, "INVALID_POLICY" unless max_content_bytes && max_content_bytes.positive? && max_content_bytes <= 4 * 1024 * 1024

  narrative_patterns = content["narrativePathPatterns"]
  raise AuditError, "INVALID_POLICY" unless narrative_patterns.is_a?(Array) && !narrative_patterns.empty?
  begin
    narrative_expressions = narrative_patterns.map { |pattern| Regexp.new(pattern, Regexp::IGNORECASE) }
  rescue RegexpError, TypeError
    raise AuditError, "INVALID_POLICY_REGEX"
  end
  narrative_match = lambda { |path| narrative_expressions.any? { |expression| expression.match?(path) } }
  raise AuditError, "INVALID_POLICY" unless narrative_match.call("docs/design.md")
  raise AuditError, "INVALID_POLICY" unless narrative_match.call("artifacts/evidence/G0/EV-SCOPE.yaml")
  %w[ops/release.sh tools/tests/integration.sh docs/Dockerfile].each do |path|
    raise AuditError, "INVALID_POLICY" if narrative_match.call(path)
  end

  expected_manifests = [
    "Project hotfix/Packages/manifest.json",
    "Project hotfix/Packages/packages-lock.json",
  ]
  raise AuditError, "INVALID_POLICY" unless packages["required"] == true
  raise AuditError, "INVALID_POLICY" unless packages["manifests"].is_a?(Array) && packages["manifests"].sort == expected_manifests.sort
  raise AuditError, "INVALID_POLICY" unless packages["rules"].is_a?(Array) && !packages["rules"].empty?
  max_manifest_bytes = Integer(packages["maxBytesPerManifest"], exception: false)
  raise AuditError, "INVALID_POLICY" unless max_manifest_bytes && max_manifest_bytes.positive? && max_manifest_bytes <= 4 * 1024 * 1024

  allowed_categories = %w[backend database dedicated container audit]
  raise AuditError, "INVALID_POLICY" unless reporting["categories"] == allowed_categories
  raise AuditError, "INVALID_POLICY" unless
    reporting["printRelativePathsOnly"] == true && reporting["printMatchedContent"] == false &&
    reporting["passExitCode"] == 0 && reporting["violationExitCode"] == 1 &&
    reporting["usageOrPolicyErrorExitCode"] == 2

  rule_groups = artifact_group_keys.flat_map { |key| artifact.fetch(key) }
  pattern_rules = artifact_pattern_keys.flat_map { |key| artifact.fetch(key) } +
    content.fetch("rules") + packages.fetch("rules")
  all_rules = rule_groups + pattern_rules
  ids = all_rules.map { |rule| rule.is_a?(Hash) && rule["id"] }
  raise AuditError, "INVALID_POLICY" unless
    ids.all? { |id| id.is_a?(String) && !id.empty? } && ids.uniq.length == ids.length
  raise AuditError, "INVALID_POLICY" unless all_rules.all? do |rule|
    allowed_categories.include?(rule["category"]) && rule["category"] != "audit"
  end
  pattern_rules.each do |rule|
    begin
      Regexp.new(rule.fetch("pattern"), Regexp::IGNORECASE | Regexp::MULTILINE)
      Regexp.new(rule.fetch("pathPattern"), Regexp::IGNORECASE) if rule["pathPattern"]
      if rule["applicableExtensions"]
        extensions = rule.fetch("applicableExtensions")
        raise TypeError unless extensions.is_a?(Array) && !extensions.empty? && extensions.all? do |extension|
          extension.is_a?(String) && extension.start_with?(".")
        end
      end
    rescue RegexpError, KeyError, TypeError
      raise AuditError, "INVALID_POLICY_REGEX"
    end
  end
end

def load_policy(path)
  document = YAML.safe_load(File.read(path), permitted_classes: [], permitted_symbols: [], aliases: false)
  policy = document.is_a?(Hash) ? document["ForbiddenInfrastructurePolicy"] : nil
  raise AuditError, "INVALID_POLICY" unless policy.is_a?(Hash)
  raise AuditError, "UNSUPPORTED_POLICY_SCHEMA" unless policy["schemaVersion"] == 1
  validate_policy!(policy)

  policy
rescue Psych::Exception, Errno::ENOENT
  raise AuditError, "INVALID_POLICY"
end

options = {}
parser = OptionParser.new do |arguments|
  arguments.banner = "usage: ruby tools/verify_forbidden_infrastructure.rb [--root PATH] [--policy PATH]"
  arguments.on("--root PATH", "Git worktree to audit") { |path| options[:root] = path }
  arguments.on("--policy PATH", "Policy YAML to use") { |path| options[:policy] = path }
end

begin
  parser.parse!
  raise AuditError, "UNEXPECTED_ARGUMENT" unless ARGV.empty?

  default_root = Pathname.new(__dir__).join("..").expand_path
  root = Pathname.new(options.fetch(:root, default_root.to_s)).expand_path
  raise AuditError, "INVALID_ROOT" unless root.directory?

  policy_path = Pathname.new(
    options.fetch(:policy, root.join("config/infrastructure/ForbiddenInfrastructurePolicy.yaml").to_s)
  ).expand_path
  policy = load_policy(policy_path)
  exit ForbiddenInfrastructureAudit.new(root.realpath, policy).run
rescue OptionParser::ParseError
  warn "FORBIDDEN_INFRASTRUCTURE_AUDIT=ERROR reason=USAGE"
  exit 2
rescue AuditError => error
  warn "FORBIDDEN_INFRASTRUCTURE_AUDIT=ERROR reason=#{error.message}"
  exit 2
rescue StandardError
  warn "FORBIDDEN_INFRASTRUCTURE_AUDIT=ERROR reason=UNEXPECTED"
  exit 2
end
