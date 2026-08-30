#!/usr/bin/env ruby

require "digest"
require "fileutils"
require "minitest/autorun"
require "open3"
require "pathname"
require "rbconfig"
require "tmpdir"
require "yaml"

class VerifyLfsRepositoryTest < Minitest::Test
  REPOSITORY_ROOT = Pathname.new(__dir__).join("../..").expand_path
  VERIFIER = REPOSITORY_ROOT.join("tools/verify_lfs_repository.rb")
  POLICY_PATH = "config/repository/BinaryAssetPolicy.md"
  INVENTORY_PATH = "config/repository/BinaryAssetInventory.yaml"
  SUPPORT_PATHS = [POLICY_PATH, INVENTORY_PATH, ".gitattributes", ".gitignore"].freeze
  INVENTORY_TEMPLATE = YAML.safe_load(
    File.read(REPOSITORY_ROOT.join(INVENTORY_PATH)), [], [], false
  ).freeze
  BINARY_PATHS = INVENTORY_TEMPLATE.fetch("BinaryAssetInventory").fetch("files")
    .map { |entry| entry.fetch("path") }.freeze
  LFS_BINARY_PATHS = BINARY_PATHS.select do |path|
    %w[.blend .fbx .glb .wav .flac .psd .exr .hdr .tif .tiff]
      .include?(File.extname(path).downcase)
  end.freeze

  def test_current_repository_reports_only_expected_pre_stage_gap_or_passes_after_pointer_stage
    stdout, stderr, status = run_verifier(REPOSITORY_ROOT)

    assert_includes stdout, "DEFAULT_LFS_PATTERN_COUNT=10"
    assert_includes stdout, "BINARY_INVENTORY_COUNT=66"
    assert_includes stdout, "LFS_REQUIRED_CANDIDATES=4"
    assert_includes stdout, "ORDINARY_PNG_FILES=62"
    assert_includes stdout, "HISTORY_REWRITE_CLAIMED=false"
    assert_includes stdout, "LOCAL_LFS_REQUESTED=false"
    assert_includes stdout, "REMOTE_REQUESTED=false"
    if status.zero?
      assert_empty stderr
      assert_includes stdout, "LFS_TRACKED_FILES=4"
      assert_includes stdout, "TOTAL_VIOLATIONS=0"
      assert_includes stdout, "FINAL_RESULT=PASS"
    else
      assert_equal 1, status, stderr + stdout
      assert_empty stderr
      assert_includes stdout, "LFS_TRACKED_FILES=3"
      rules = stdout.lines.map { |line| line[/VIOLATION rule=([^ ]+)/, 1] }.compact.sort
      assert_equal %w[
        INVENTORY_SUMMARY_CURRENTLFSTRACKEDFILES
        INVENTORY_SUMMARY_ORDINARYGITBINARYFILES
        LFS_REQUIRED_FILE_NOT_POINTER
      ].sort, rules
      assert_includes stdout, "TOTAL_VIOLATIONS=3"
      assert_includes stdout, "FINAL_RESULT=FAIL"
    end
  end

  def test_fresh_git_checkout_fixture_is_hermetic_by_default
    with_repository do |root|
      stdout, stderr, status = run_verifier(root)

      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "LOCAL_LFS_REQUESTED=false"
      assert_includes stdout, "REMOTE_REQUESTED=false"
      assert_includes stdout, "PACKAGE_IDS_HARDCODED=0"
      assert_includes stdout, "FINAL_RESULT=PASS"
    end
  end

  def test_duplicate_invalid_and_oversize_inventory_yaml_fail_closed
    with_repository do |root|
      path = root.join(INVENTORY_PATH)
      path.write(path.read.sub("  revision: \"r07\"\n", "  revision: \"r07\"\n  revision: \"r07\"\n"))
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_YAML_DUPLICATE_KEY"
    end

    with_repository do |root|
      root.join(INVENTORY_PATH).write("BinaryAssetInventory: [\n")
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_YAML_INVALID"
    end

    with_repository do |root|
      root.join(INVENTORY_PATH).write("x" * (2 * 1024 * 1024 + 1))
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_TOO_LARGE"
    end
  end

  def test_tracked_symlink_is_rejected_without_following_it
    with_repository do |root|
      policy = root.join(POLICY_PATH)
      target = root.join("outside-policy.md")
      FileUtils.mv(policy, target)
      File.symlink(target, policy)
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=GIT_ACTIVE_SYMLINK"
      assert_includes stdout, "rule=POLICY_SYMLINK"
    end
  end

  def test_inventory_storage_schema_remote_and_no_migration_contract_are_exact
    with_repository do |root|
      mutate_inventory(root) do |inventory|
        inventory["unexpected"] = true
        storage = inventory.fetch("storage")
        storage["remoteVisibility"] = "PUBLIC"
        storage["lfsTrackedPatternCount"] = 9
        storage["historyMigrationPerformed"] = true
        storage["existingReviewPngsMigrated"] = true
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_FIELD_SET"
      assert_includes stdout, "rule=INVENTORY_STORAGE_REMOTEVISIBILITY"
      assert_includes stdout, "rule=INVENTORY_STORAGE_LFSTRACKEDPATTERNCOUNT"
      assert_includes stdout, "rule=INVENTORY_STORAGE_HISTORYMIGRATIONPERFORMED"
      assert_includes stdout, "rule=INVENTORY_STORAGE_EXISTINGREVIEWPNGSMIGRATED"
      assert_includes stdout, "HISTORY_REWRITE_CLAIMED=true"
    end
  end

  def test_inventory_summary_count_and_byte_aggregates_are_recomputed
    with_repository do |root|
      mutate_inventory(root) do |inventory|
        summary = inventory.fetch("summary")
        summary["fileCount"] += 1
        summary["totalBytes"] += 1
        summary["uniqueContentHashes"] += 1
        summary["ordinaryGitBinaryFiles"] -= 1
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_SUMMARY_FILECOUNT"
      assert_includes stdout, "rule=INVENTORY_SUMMARY_TOTALBYTES"
      assert_includes stdout, "rule=INVENTORY_SUMMARY_UNIQUECONTENTHASHES"
      assert_includes stdout, "rule=INVENTORY_SUMMARY_ORDINARYGITBINARYFILES"
    end
  end

  def test_inventory_file_size_and_sha_drift_fail
    with_repository do |root|
      mutate_inventory(root) do |inventory|
        entry = inventory.fetch("files").first
        entry["bytes"] = 1
        entry["sha256"] = "0" * 64
        recompute_summary(inventory, required: 4, tracked: 4)
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_FILE_SIZE_DRIFT"
      assert_includes stdout, "rule=INVENTORY_FILE_SHA_DRIFT"
    end
  end

  def test_binary_inventory_requires_exact_repository_coverage
    with_repository do |root|
      write_file(root.join("incoming/unregistered.png"), "ordinary png fixture\n")
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_BINARY_EXACT_SET"
    end

    with_repository do |root|
      mutate_inventory(root) do |inventory|
        inventory.fetch("files").pop
        recompute_summary(inventory, required: 4, tracked: 4)
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_BINARY_EXACT_SET"
    end
  end

  def test_missing_duplicate_and_malformed_lfs_attributes_fail
    with_repository do |root|
      attributes = root.join(".gitattributes")
      attributes.write(attributes.read.lines.reject { |line| line.start_with?("*.blend ") }.join)
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=GITATTR_LFS_PATTERN_EXACT_SET"
      assert_includes stdout, "rule=GITATTR_EFFECTIVE_LFS_CONTRACT"
    end

    with_repository do |root|
      attributes = root.join(".gitattributes")
      attributes.open("a") { |file| file.write("*.blend filter=lfs diff=lfs merge=lfs -text\n") }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=GITATTR_LFS_PATTERN_EXACT_SET"
    end

    with_repository do |root|
      attributes = root.join(".gitattributes")
      attributes.write(attributes.read.sub("*.fbx filter=lfs diff=lfs", "*.fbx filter=lfs diff=wrong"))
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=GITATTR_LFS_ATTRIBUTE_CONTRACT"
      assert_includes stdout, "rule=GITATTR_EFFECTIVE_LFS_CONTRACT"
    end
  end

  def test_policy_markers_and_extension_set_are_enforced
    with_repository do |root|
      policy = root.join(POLICY_PATH)
      policy.write(policy.read
        .sub("`.blend`, `.fbx`, `.glb`", "`.blend`, `.glb`")
        .sub(
          "Core revision `d7877b3` uploaded the fourth production LFS object",
          "Fourth production object round-trip status omitted",
        )
        .sub("Do not run `git lfs migrate`", "Migration command omitted"))
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=POLICY_LFS_EXTENSION_EXACT_SET"
      assert_includes stdout, "rule=POLICY_REQUIRED_MARKER"
    end
  end

  def test_png_cannot_be_added_to_lfs_attributes
    with_repository do |root|
      root.join(".gitattributes").open("a") do |file|
        file.write("*.png filter=lfs diff=lfs merge=lfs -text\n")
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=GITATTR_LFS_PATTERN_EXACT_SET"
      assert_includes stdout, "rule=GITATTR_UNAPPROVED_LFS_PATTERN"
      assert_includes stdout, "rule=GITATTR_PNG_ORDINARY_CONTRACT"
      assert_includes stdout, "rule=GITATTR_EFFECTIVE_PNG_CONTRACT"
    end
  end

  def test_existing_png_cannot_be_staged_as_an_lfs_pointer
    with_repository do |root|
      path = BINARY_PATHS.first
      stage_pointer_for_working_file(root, path)
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=PNG_LFS_POINTER"
      assert_includes stdout, "rule=UNAPPROVED_LFS_POINTER"
      assert_includes stdout, "rule=INVENTORY_SUMMARY_CURRENTLFSTRACKEDFILES"
    end
  end

  def test_required_extension_file_must_be_a_staged_lfs_pointer
    with_repository do |root|
      write_file(root.join("Project hotfix/Assets/Characters/raw.blend"), "raw blend fixture\n")
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=BLEND_INSIDE_UNITY_ASSETS"
      assert_includes stdout, "rule=LFS_REQUIRED_FILE_NOT_POINTER"
      assert_includes stdout, "rule=INVENTORY_BINARY_EXACT_SET"
      assert_includes stdout, "rule=INVENTORY_SUMMARY_CURRENTLFSREQUIREDCANDIDATES"
    end
  end

  def test_any_file_over_ten_mib_requires_lfs_even_with_another_extension
    with_repository do |root|
      path = root.join("incoming/large-source.bin")
      FileUtils.mkdir_p(path.dirname)
      File.open(path, "wb") do |file|
        file.write("\0binary fixture\n")
        file.truncate(10 * 1024 * 1024 + 1)
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=LFS_REQUIRED_FILE_NOT_POINTER"
      assert_includes stdout, "rule=LFS_REQUIRED_FILE_FILTER"
      assert_includes stdout, "rule=INVENTORY_SUMMARY_CURRENTLFSREQUIREDCANDIDATES"
    end

    with_repository do |root|
      path = root.join("incoming/large-notes.md")
      FileUtils.mkdir_p(path.dirname)
      chunk = "plain UTF-8 text\n" * 4096
      File.open(path, "wb") do |file|
        file.write(chunk) while file.pos <= 10 * 1024 * 1024
      end
      stdout, stderr, status = run_verifier(root)

      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "LFS_REQUIRED_CANDIDATES=4"
      assert_includes stdout, "FINAL_RESULT=PASS"
    end
  end

  def test_materialized_required_asset_with_matching_index_pointer_passes
    with_repository do |root|
      path = "BlenderSource/Characters/approved.blend"
      payload = "\0BLENDER".b + ("\x01".b * (10 * 1024 * 1024))
      write_file(root.join(path), payload)
      mutate_inventory(root) do |inventory|
        inventory.fetch("files") << {
          "path" => path,
          "bytes" => payload.bytesize,
          "sha256" => Digest::SHA256.hexdigest(payload),
        }
        recompute_summary(inventory, required: 5, tracked: 5)
      end
      policy = root.join(POLICY_PATH)
      policy.write(policy.read.sub(
        "has `4` LFS-tracked files and `4` LFS-required candidates",
        "has `5` LFS-tracked files and `5` LFS-required candidates",
      ))
      pointer = stage_pointer_for_working_file(root, path)

      stdout, stderr, status = run_verifier(root)
      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "LFS_REQUIRED_CANDIDATES=5"
      assert_includes stdout, "LFS_TRACKED_FILES=5"
      assert_includes stdout, "FINAL_RESULT=PASS"

      write_file(root.join(path), pointer)
      stdout, stderr, status = run_verifier(root)
      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "LFS_TRACKED_FILES=5"
      assert_includes stdout, "FINAL_RESULT=PASS"
    end
  end

  def test_local_and_remote_checks_are_explicit_and_fail_closed
    with_repository do |root|
      stdout, _stderr, status = run_verifier(root, "--verify-local-lfs")
      assert_equal 1, status
      assert_includes stdout, "rule=LOCAL_LFS_CONFIG_"
      assert_includes stdout, "rule=LOCAL_LFS_PRE_PUSH_HOOK"
      assert_includes stdout, "LOCAL_LFS_VERIFIED=false"
    end

    with_repository do |root|
      configure_local_lfs_fixture(root)
      Dir.mktmpdir("lfs-command-fixture-") do |directory|
        fake_bin = Pathname.new(directory)
        executable = fake_bin.join("git-lfs")
        executable.write("#!/bin/sh\nprintf '%s\\n' 'git-lfs/3.8.0 (fixture)'\n")
        executable.chmod(0o755)
        stdout, stderr, status = run_verifier(
          root,
          "--verify-local-lfs",
          env: { "PATH" => "#{fake_bin}:#{ENV.fetch("PATH")}" },
        )
        assert_equal 0, status, stderr + stdout
        assert_includes stdout, "LOCAL_LFS_VERIFIED=true"
      end
    end

    with_repository do |root|
      stdout, _stderr, status = run_verifier(root, "--verify-remote")
      assert_equal 1, status
      assert_includes stdout, "rule=REMOTE_EXACT_SET"
      assert_includes stdout, "REMOTE_VERIFIED=false"
      refute_includes stdout, "rule=REMOTE_GITHUB_READ_FAILED"
    end
  end

  private

  def run_verifier(root, *arguments, env: {})
    command = [RbConfig.ruby, VERIFIER.to_s, "--root", root.to_s, *arguments]
    stdout, stderr, status = Open3.capture3(env, *command)
    [stdout, stderr, status.exitstatus]
  end

  def with_repository
    Dir.mktmpdir("lfs-foundation-fixture-") do |directory|
      root = Pathname.new(directory)
      _stdout, stderr, status = Open3.capture3("git", "-C", root.to_s, "init", "-q")
      raise stderr unless status.success?

      SUPPORT_PATHS.each { |relative| copy_file(REPOSITORY_ROOT.join(relative), root.join(relative)) }
      BINARY_PATHS.each { |relative| link_or_copy(REPOSITORY_ROOT.join(relative), root.join(relative)) }
      _stdout, stderr, status = Open3.capture3("git", "-C", root.to_s, "add", "-A", "--", ".")
      raise stderr unless status.success?
      LFS_BINARY_PATHS.each { |relative| stage_pointer_for_working_file(root, relative) }
      yield root
    end
  end

  def copy_file(source, destination)
    FileUtils.mkdir_p(destination.dirname)
    FileUtils.cp(source, destination)
  end

  def link_or_copy(source, destination)
    FileUtils.mkdir_p(destination.dirname)
    File.link(source, destination)
  rescue SystemCallError
    FileUtils.cp(source, destination)
  end

  def write_file(path, content)
    FileUtils.mkdir_p(path.dirname)
    File.binwrite(path, content)
  end

  def mutate_inventory(root)
    document = YAML.safe_load(root.join(INVENTORY_PATH).read, [], [], false)
    yield document.fetch("BinaryAssetInventory")
    root.join(INVENTORY_PATH).write(YAML.dump(document))
  end

  def recompute_summary(inventory, required:, tracked:)
    files = inventory.fetch("files")
    inventory["summary"] = {
      "fileCount" => files.length,
      "totalBytes" => files.sum { |entry| entry.fetch("bytes") },
      "uniqueContentHashes" => files.map { |entry| entry.fetch("sha256") }.uniq.length,
      "filesOver10MiB" => files.count { |entry| entry.fetch("bytes") > 10 * 1024 * 1024 },
      "currentLfsRequiredCandidates" => required,
      "currentLfsTrackedFiles" => tracked,
      "ordinaryGitBinaryFiles" => files.length - tracked,
    }
  end

  def stage_pointer_for_working_file(root, relative)
    bytes = root.join(relative).binread
    pointer = "version https://git-lfs.github.com/spec/v1\n" \
      "oid sha256:#{Digest::SHA256.hexdigest(bytes)}\n" \
      "size #{bytes.bytesize}\n"
    oid, stderr, status = Open3.capture3(
      "git", "-C", root.to_s, "hash-object", "-w", "--stdin", stdin_data: pointer
    )
    raise stderr unless status.success?
    _stdout, stderr, status = Open3.capture3(
      "git", "-C", root.to_s, "update-index", "--add", "--cacheinfo",
      "100644,#{oid.strip},#{relative}"
    )
    raise stderr unless status.success?
    pointer
  end

  def configure_local_lfs_fixture(root)
    values = {
      "lfs.repositoryformatversion" => "0",
      "filter.lfs.process" => "git-lfs filter-process",
      "filter.lfs.required" => "true",
      "filter.lfs.clean" => "git-lfs clean -- %f",
      "filter.lfs.smudge" => "git-lfs smudge -- %f",
    }
    values.each do |key, value|
      _stdout, stderr, status = Open3.capture3(
        "git", "-C", root.to_s, "config", "--local", key, value
      )
      raise stderr unless status.success?
    end
    hook = root.join(".git/hooks/pre-push")
    hook.write("#!/bin/sh\ngit lfs pre-push \"$@\"\n")
    hook.chmod(0o755)
  end
end
