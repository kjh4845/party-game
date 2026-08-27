#!/usr/bin/env ruby

require "pathname"
require "time"
require "yaml"

ROOT = Pathname.new(__dir__).join("..").expand_path
ROOT_REAL = ROOT.realpath
PROFILE_PATH = ROOT.join("config/evidence/EvidenceProfile.yaml")

def fail_validation(path, message)
  warn "#{path}: #{message}"
  exit 1
end

strict = ARGV.delete("--strict")

if ARGV.empty?
  warn "usage: ruby tools/verify_evidence_manifest.rb [--strict] <manifest.yaml> [...]"
  exit 2
end

profile = YAML.load_file(PROFILE_PATH).fetch("EvidenceProfile")
validated = 0

ARGV.each do |argument|
  argument_path = Pathname.new(argument)
  fail_validation(argument, "manifest path must be repository-relative") if argument_path.absolute?
  fail_validation(argument, "manifest path must be clean") unless argument_path.cleanpath.to_s == argument
  path = ROOT.join(argument_path).cleanpath
  fail_validation(argument, "file does not exist") unless path.file?
  manifest_real = path.realpath
  manifest_contained = manifest_real.to_s.start_with?(ROOT_REAL.to_s + File::SEPARATOR)
  fail_validation(argument, "manifest path escapes repository") unless manifest_contained

  document = YAML.load_file(path)
  manifest = document.is_a?(Hash) ? document["EvidenceManifest"] : nil
  fail_validation(argument, "missing EvidenceManifest root") unless manifest.is_a?(Hash)

  profile.fetch("requiredLegacyFields").each do |field|
    fail_validation(argument, "missing #{field}") unless manifest.key?(field)
  end

  selector_fields = profile.fetch("exactlyOneTaskSelector")
  selectors = selector_fields.count { |field| manifest.key?(field) }
  fail_validation(argument, "expected exactly one task selector") unless selectors == 1

  if manifest.key?("taskId")
    fail_validation(argument, "taskId must be a non-empty string") unless
      manifest["taskId"].is_a?(String) && !manifest["taskId"].empty?
  else
    task_ids = manifest["taskIds"]
    fail_validation(argument, "taskIds must be a non-empty unique string array") unless
      task_ids.is_a?(Array) && !task_ids.empty? && task_ids.all? { |value| value.is_a?(String) && !value.empty? } &&
      task_ids.uniq.length == task_ids.length
  end

  fail_validation(argument, "evidenceId must match filename") unless
    manifest["evidenceId"] == path.basename(".yaml").to_s
  fail_validation(argument, "unsupported status") unless
    profile.fetch("allowedStatuses").include?(manifest["status"])

  %w[profileVersions commandsOrManualSteps rawEvidencePaths limitations].each do |field|
    value = manifest[field]
    fail_validation(argument, "#{field} must be a non-empty string array") unless
      value.is_a?(Array) && !value.empty? && value.all? { |entry| entry.is_a?(String) && !entry.empty? }
  end

  %w[observedMetrics expectedThresholds].each do |field|
    fail_validation(argument, "#{field} must be a non-empty map") unless
      manifest[field].is_a?(Hash) && !manifest[field].empty?
  end

  fail_validation(argument, "failures must be a string array") unless
    manifest["failures"].is_a?(Array) && manifest["failures"].all? { |entry| entry.is_a?(String) }
  if profile.fetch("passRequiresEmptyFailures") && manifest["status"] == "PASS" && !manifest["failures"].empty?
    fail_validation(argument, "PASS requires failures=[]")
  end

  if manifest.key?("schemaVersion")
    fail_validation(argument, "unsupported schemaVersion") unless manifest["schemaVersion"] == 1
  elsif strict
    fail_validation(argument, "strict mode requires schemaVersion: 1")
  end

  if manifest["schemaVersion"] == 1
    profile.fetch("requiredForSchemaVersion1").each do |field|
      fail_validation(argument, "schemaVersion1 missing #{field}") unless manifest.key?(field)
    end
    fail_validation(argument, "buildKind must be a non-empty string") unless
      manifest["buildKind"].is_a?(String) && !manifest["buildKind"].empty?
    %w[goalId sourceRevision environment].each do |field|
      fail_validation(argument, "#{field} must be a non-empty string") unless
        manifest[field].is_a?(String) && !manifest[field].empty?
    end
    requirement_ids = manifest["requirementIds"]
    fail_validation(argument, "requirementIds must be a non-empty string array") unless
      requirement_ids.is_a?(Array) && !requirement_ids.empty? &&
      requirement_ids.all? { |value| value.is_a?(String) && !value.empty? }
    fail_validation(argument, "wrong evidenceProfile") unless
      manifest["evidenceProfile"] == profile.fetch("profileId")
  end

  manifest.fetch("rawEvidencePaths").each do |raw_path|
    relative = Pathname.new(raw_path)
    fail_validation(argument, "rawEvidencePath must be repository-relative") if relative.absolute?
    fail_validation(argument, "rawEvidencePath must be clean") unless relative.cleanpath.to_s == raw_path
    resolved = ROOT.join(relative).cleanpath
    fail_validation(argument, "missing rawEvidencePath #{raw_path}") if
      profile.fetch("rawEvidencePathsMustExist") && !resolved.exist?
    next unless resolved.exist?

    real = resolved.realpath
    contained = real == ROOT_REAL || real.to_s.start_with?(ROOT_REAL.to_s + File::SEPARATOR)
    fail_validation(argument, "rawEvidencePath escapes repository") unless contained
  end

  recorded_at = manifest["recordedAtUtc"]
  timestamp_pattern = Regexp.new(profile.fetch("recordedAtUtcPattern"))
  fail_validation(argument, "recordedAtUtc must be UTC second precision") unless
    recorded_at.is_a?(String) && recorded_at.match?(timestamp_pattern)
  begin
    fail_validation(argument, "recordedAtUtc must be a real UTC instant") unless
      Time.iso8601(recorded_at).utc.iso8601 == recorded_at
  rescue ArgumentError
    fail_validation(argument, "recordedAtUtc must be a real UTC instant")
  end

  validated += 1
end

puts "EVIDENCE_MANIFEST_VALIDATION=PASS files=#{validated}"
