#!/usr/bin/env ruby

require "fileutils"
require "digest"
require "json"
require "minitest/autorun"
require "open3"
require "pathname"
require "rbconfig"
require "tmpdir"
require "yaml"

class VerifyLicenseInventoryTest < Minitest::Test
  REPOSITORY_ROOT = Pathname.new(__dir__).join("../..").expand_path
  VERIFIER = REPOSITORY_ROOT.join("tools/verify_license_inventory.rb")
  SUPPORT_PATHS = [
    "config/licenses/LicensePolicy.yaml",
    "config/licenses/ThirdPartyInventory.yaml",
    "config/repository/BinaryAssetInventory.yaml",
    "THIRD_PARTY_NOTICES.md",
    "Project hotfix/Packages/manifest.json",
    "Project hotfix/Packages/packages-lock.json",
  ].freeze
  INVENTORY_TEMPLATE = YAML.safe_load(
    File.read(REPOSITORY_ROOT.join("config/licenses/ThirdPartyInventory.yaml")), [], [], false
  ).freeze
  REVIEW_PATHS = INVENTORY_TEMPLATE.fetch("ThirdPartyInventory")
    .fetch("reviewOnlyAssets").fetch("items").map { |item| item.fetch("path") }.freeze
  FIRST_PARTY_PATHS = INVENTORY_TEMPLATE.fetch("ThirdPartyInventory")
    .fetch("firstPartyProductionAssets").fetch("items").map { |item| item.fetch("path") }.freeze
  REGISTERED_MEDIA_PATHS = (REVIEW_PATHS + FIRST_PARTY_PATHS).freeze

  def test_current_repository_passes_with_and_without_package_cache_verification
    stdout, stderr, status = run_verifier(REPOSITORY_ROOT)

    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "PACKAGE_INVENTORY_COUNT=58"
    assert_includes stdout, "REVIEW_ASSET_COUNT=18"
    assert_includes stdout, "FIRST_PARTY_ASSET_COUNT=39"
    assert_includes stdout, "PACKAGE_CACHE_VERIFIED=false"
    assert_includes stdout, "TOTAL_VIOLATIONS=0"
    assert_includes stdout, "FINAL_RESULT=PASS"

    stdout, stderr, status = run_verifier(REPOSITORY_ROOT, verify_cache: true)

    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "PACKAGE_CACHE_MATCHES=58"
    assert_includes stdout, "PACKAGE_CACHE_EVIDENCE_LOCATORS=40"
    assert_includes stdout, "FINAL_RESULT=PASS"
  end

  def test_package_version_source_relationship_and_required_evidence_drift_fail
    with_repository do |root|
      mutate_inventory(root) do |inventory|
        package = inventory.fetch("packages").first
        package["resolvedVersion"] = "999.0.0"
        package["source"] = "builtin"
        package["relationship"] = "TRANSITIVE"
        package["noticeDisposition"] = ""
        package["sourceEvidence"] = []
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_PACKAGE_VERSION_DRIFT"
      assert_includes stdout, "rule=INVENTORY_PACKAGE_SOURCE_DRIFT"
      assert_includes stdout, "rule=INVENTORY_PACKAGE_RELATIONSHIP_DRIFT"
      assert_includes stdout, "rule=INVENTORY_PACKAGE_REQUIRED_FIELD"
    end
  end

  def test_package_inventory_missing_extra_and_duplicate_ids_fail
    with_repository do |root|
      mutate_inventory(root) do |inventory|
        packages = inventory.fetch("packages")
        packages.pop
        packages << Marshal.load(Marshal.dump(packages.first))
        extra = Marshal.load(Marshal.dump(packages.first))
        extra["packageId"] = "com.example.unapproved"
        packages << extra
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_PACKAGE_COUNT"
      assert_includes stdout, "rule=INVENTORY_PACKAGE_ID_UNIQUE"
      assert_includes stdout, "rule=INVENTORY_PACKAGE_SET"
    end
  end

  def test_manifest_direct_set_and_hash_drift_fail
    with_repository do |root|
      mutate_json(root, "Project hotfix/Packages/manifest.json") do |document|
        document.fetch("dependencies").delete("com.unity.ai.navigation")
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=MANIFEST_HASH_DRIFT"
      assert_includes stdout, "rule=INVENTORY_PACKAGE_BASELINE_COUNT"
      assert_includes stdout, "rule=POLICY_MANIFEST_COUNT_DRIFT"
      assert_includes stdout, "rule=INVENTORY_PACKAGE_RELATIONSHIP_DRIFT"
    end
  end

  def test_lock_version_source_depth_and_dependency_relationship_drift_fail
    with_repository do |root|
      mutate_json(root, "Project hotfix/Packages/packages-lock.json") do |document|
        entry = document.fetch("dependencies").fetch("com.unity.ai.navigation")
        entry["version"] = "999.0.0"
        entry["source"] = "git"
        entry["depth"] = 1
        entry["dependencies"]["com.example.missing"] = "1.0.0"
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=LOCK_HASH_DRIFT"
      assert_includes stdout, "rule=LOCK_SOURCE_INVALID"
      assert_includes stdout, "rule=LOCK_DEPTH_RELATIONSHIP"
      assert_includes stdout, "rule=LOCK_RELATIONSHIP_TARGET_MISSING"
      assert_includes stdout, "rule=INVENTORY_PACKAGE_VERSION_DRIFT"
      assert_includes stdout, "rule=INVENTORY_PACKAGE_SOURCE_DRIFT"
    end
  end

  def test_review_asset_inventory_missing_and_extra_paths_fail_exact_set
    with_repository do |root|
      mutate_inventory(root) do |inventory|
        item = inventory.fetch("reviewOnlyAssets").fetch("items").first
        old_path = item.fetch("path")
        item["path"] = "docs/assets/ui/not-present.png"
        item["sourceEvidence"] = [
          "config/repository/BinaryAssetInventory.yaml#files[path=docs/assets/ui/not-present.png]",
          "embedded C2PA claim detected in docs/assets/ui/not-present.png; provenance is not a license grant",
        ]
        raise "fixture did not change" if old_path == item["path"]
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=REVIEW_MEDIA_EXACT_SET"
      assert_includes stdout, "rule=UNREGISTERED_MEDIA_ASSET"
      assert_includes stdout, "rule=INVENTORIED_MEDIA_MISSING"
      assert_includes stdout, "rule=BINARY_REVIEW_PATH_SET"
    end
  end

  def test_review_asset_and_binary_sha_or_size_drift_fail
    with_repository do |root|
      mutate_inventory(root) do |inventory|
        inventory.fetch("reviewOnlyAssets").fetch("items").first["sha256"] = "0" * 64
      end
      mutate_binary_inventory(root) do |binary|
        binary.fetch("files").first["sha256"] = "1" * 64
        binary.fetch("files").first["bytes"] = 1
        recompute_binary_summary(binary)
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=REVIEW_ASSET_SHA_DRIFT"
      assert_includes stdout, "rule=REVIEW_BINARY_SHA_DRIFT"
      assert_includes stdout, "rule=REVIEW_BINARY_SIZE_DRIFT"
    end
  end

  def test_unknown_or_blocked_package_and_review_shipping_true_fail
    with_repository do |root|
      mutate_inventory(root) do |inventory|
        inventory.fetch("packages").first["licenseFamily"] = "UNKNOWN LICENSE"
        inventory.fetch("reviewOnlyAssets").fetch("items").first["shippingAllowed"] = true
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_BLOCKED_PACKAGE_LICENSE"
      assert_includes stdout, "rule=INVENTORY_REVIEW_SHIPPING_FLAG"
    end
  end

  def test_review_asset_moved_into_unity_assets_fails_even_if_records_match
    with_repository do |root|
      inventory = load_yaml(root, "config/licenses/ThirdPartyInventory.yaml")
      item = inventory.fetch("ThirdPartyInventory").fetch("reviewOnlyAssets").fetch("items").first
      old_path = item.fetch("path")
      new_path = "Project hotfix/Assets/review-only.png"
      FileUtils.mkdir_p(root.join(new_path).dirname)
      FileUtils.mv(root.join(old_path), root.join(new_path))
      item["path"] = new_path
      item["sourceEvidence"] = [
        "config/repository/BinaryAssetInventory.yaml#files[path=#{new_path}]",
        "embedded C2PA claim detected in #{new_path}; provenance is not a license grant",
      ]
      write_yaml(root, "config/licenses/ThirdPartyInventory.yaml", inventory)
      mutate_binary_inventory(root) do |binary|
        binary.fetch("files").find { |entry| entry["path"] == old_path }["path"] = new_path
      end

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_REVIEW_INSIDE_UNITY_ASSETS"
    end
  end

  def test_registered_project_authored_blend_and_fbx_pass
    with_repository do |root|
      register_first_party_asset(root, "source/character/hero.blend", "fixture blend\n", "BLENDER_SOURCE")
      register_first_party_asset(root, "Project hotfix/Assets/Characters/hero.fbx", "fixture fbx\n", "MODEL")
      mutate_binary_inventory(root) do |binary|
        recompute_binary_summary(binary, lfs_required: 2, lfs_tracked: 2)
      end

      stdout, stderr, status = run_verifier(root)

      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "FIRST_PARTY_ASSET_COUNT=41"
      assert_includes stdout, "TOTAL_VIOLATIONS=0"
      assert_includes stdout, "FINAL_RESULT=PASS"
    end
  end

  def test_unregistered_first_party_candidates_fail
    extras = {
      "source/character/unregistered.blend" => "unregistered blend\n",
      "Project hotfix/Assets/Characters/unregistered.fbx" => "unregistered fbx\n",
    }
    with_repository(extra_files: extras) do |root|
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      extras.each_key do |path|
        assert_includes stdout, "rule=UNREGISTERED_MEDIA_ASSET path=#{path}"
      end
    end
  end

  def test_first_party_unknown_source_and_review_flags_fail
    with_repository do |root|
      path = "source/character/unknown.blend"
      register_first_party_asset(root, path, "unknown source\n", "BLENDER_SOURCE")
      mutate_inventory(root) do |inventory|
        item = inventory.fetch("firstPartyProductionAssets").fetch("items")
          .find { |entry| entry["path"] == path }
        item["sourceOwner"] = "UNKNOWN"
        item["sourceStatus"] = "UNKNOWN"
        item["reviewOnly"] = true
        item["shippingAllowed"] = true
      end

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_FIRST_PARTY_SOURCE_OWNER path=#{path}"
      assert_includes stdout, "rule=INVENTORY_FIRST_PARTY_SOURCE_STATUS path=#{path}"
      assert_includes stdout, "rule=INVENTORY_FIRST_PARTY_REVIEW_FLAG path=#{path}"
      assert_includes stdout, "rule=INVENTORY_FIRST_PARTY_SHIPPING_FLAG path=#{path}"
    end
  end

  def test_first_party_and_review_path_overlap_fails
    with_repository do |root|
      review = INVENTORY_TEMPLATE.fetch("ThirdPartyInventory")
        .fetch("reviewOnlyAssets").fetch("items").first
      add_first_party_inventory_record(root, review.fetch("path"), review.fetch("sha256"), "REFERENCE_IMAGE")

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_MEDIA_SCOPE_OVERLAP"
      assert_includes stdout, "rule=INVENTORY_MEDIA_SCOPE_OVERLAP path=#{review.fetch("path")}"
    end
  end

  def test_first_party_asset_sha_drift_fails
    with_repository do |root|
      path = "Project hotfix/Assets/Characters/drift.fbx"
      register_first_party_asset(root, path, "fixture fbx\n", "MODEL")
      mutate_inventory(root) do |inventory|
        item = inventory.fetch("firstPartyProductionAssets").fetch("items")
          .find { |entry| entry["path"] == path }
        item["sha256"] = "0" * 64
      end

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=FIRST_PARTY_ASSET_SHA_DRIFT path=#{path}"
    end
  end

  def test_production_source_inside_unity_assets_fails
    with_repository do |root|
      path = "Project hotfix/Assets/Characters/canonical.blend"
      register_first_party_asset(root, path, "canonical blend\n", "BLENDER_SOURCE")

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_FIRST_PARTY_SOURCE_INSIDE_UNITY_ASSETS path=#{path}"
    end
  end

  def test_project_authored_reference_render_evidence_passes
    with_repository do |root|
      path = "source/character/Renders/evidence.png"
      register_first_party_asset(root, path, "project-authored render\n",
        "CHARACTER_REFERENCE_RENDER", intended_use: "PRODUCTION_EVIDENCE")

      stdout, stderr, status = run_verifier(root)

      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "FIRST_PARTY_ASSET_COUNT=40"
      assert_includes stdout, "TOTAL_VIOLATIONS=0"
      assert_includes stdout, "FINAL_RESULT=PASS"
    end
  end

  def test_reference_render_cannot_be_source_or_player_content
    %w[PRODUCTION_SOURCE PLAYER_CONTENT].each do |invalid_use|
      with_repository do |root|
        path = nil
        mutate_inventory(root) do |inventory|
          item = inventory.fetch("firstPartyProductionAssets").fetch("items")
            .find { |entry| entry["assetType"] == "CHARACTER_REFERENCE_RENDER" }
          path = item.fetch("path")
          item["intendedUse"] = invalid_use
          item["shippingAllowed"] = invalid_use == "PLAYER_CONTENT"
        end

        stdout, _stderr, status = run_verifier(root)

        assert_equal 1, status
        assert_includes stdout,
          "rule=INVENTORY_FIRST_PARTY_EVIDENCE_INTENDED_USE path=#{path}"
      end
    end
  end

  def test_production_evidence_shipping_and_manifest_drift_fail
    with_repository do |root|
      path = nil
      mutate_inventory(root) do |inventory|
        item = inventory.fetch("firstPartyProductionAssets").fetch("items")
          .find { |entry| entry["assetType"] == "CHARACTER_REFERENCE_RENDER" }
        path = item.fetch("path")
        item["shippingAllowed"] = true
        item["sourceEvidence"].reject! do |locator|
          locator.include?("GenerationManifest.yaml#stages.reference-render")
        end
      end

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_FIRST_PARTY_SHIPPING_FLAG path=#{path}"
      assert_includes stdout,
        "rule=INVENTORY_FIRST_PARTY_RENDER_MANIFEST_EVIDENCE path=#{path}"
      assert_includes stdout, "rule=INVENTORY_C1B003_MANIFEST_EVIDENCE path=#{path}"
    end
  end

  def test_blend_cannot_be_production_evidence
    with_repository do |root|
      mutate_inventory(root) do |inventory|
        item = inventory.fetch("firstPartyProductionAssets").fetch("items")
          .find { |entry| entry["assetType"] == "BLENDER_SOURCE" }
        item["intendedUse"] = "PRODUCTION_EVIDENCE"
      end

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_FIRST_PARTY_BLEND_INTENDED_USE"
    end
  end

  def test_c1b004_owner_render_class_and_file_anchor_are_exact
    with_repository do |root|
      path = nil
      mutate_inventory(root) do |inventory|
        item = inventory.fetch("firstPartyProductionAssets").fetch("items")
          .find { |entry| entry["path"].to_s.include?("C1B-004/Renders/") }
        path = item.fetch("path")
        item["sourceOwner"] = "Different Owner"
        item["sourceEvidence"] = item.fetch("sourceEvidence").map do |locator|
          locator == "project-author:kjh4845" ? "project-author:Different Owner" : locator
        end
        item["assetType"] = "CHARACTER_REFERENCE_RENDER"
        item["sourceEvidence"] = item.fetch("sourceEvidence").map do |locator|
          next locator unless locator.include?("GenerationManifest.yaml#stages.reference-render")

          "BlenderSource/Characters/C1B-004/GenerationManifest.yaml" \
            "#stages.reference-render.outputs[path=#{path}]"
        end
      end

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_C1B004_ASSET_TYPE_COUNT"
      assert_includes stdout, "rule=INVENTORY_C1B004_SOURCE_OWNER path=#{path}"
      assert_includes stdout, "rule=INVENTORY_C1B004_MANIFEST_EVIDENCE path=#{path}"
      assert_includes stdout, "rule=INVENTORY_C1B004_RENDER_CLASS path=#{path}"
    end
  end

  def test_c1b005_fbx_player_content_and_manifest_evidence_are_exact
    with_repository do |root|
      path = nil
      mutate_inventory(root) do |inventory|
        item = inventory.fetch("firstPartyProductionAssets").fetch("items")
          .find { |entry| entry["path"].to_s.end_with?("C1B-005/CHR_MasterCharacter_C1B_Neutral_r02.fbx") }
        path = item.fetch("path")
        item["intendedUse"] = "PRODUCTION_SOURCE"
        item["shippingAllowed"] = false
        item["sourceEvidence"].delete(
          "BlenderSource/Characters/C1B-005/GenerationManifest.yaml#identity.fbxSha256"
        )
      end

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_FIRST_PARTY_SOURCE_INSIDE_UNITY_ASSETS path=#{path}"
      assert_includes stdout, "rule=INVENTORY_FIRST_PARTY_NONSHIPPING_INSIDE_UNITY_ASSETS path=#{path}"
      assert_includes stdout, "rule=INVENTORY_C1B005_FBX_CLASS path=#{path}"
      assert_includes stdout, "rule=INVENTORY_C1B005_FBX_MANIFEST_EVIDENCE path=#{path}"
    end
  end

  def test_c1b005_capture_owner_class_and_manifest_anchor_are_exact
    with_repository do |root|
      path = nil
      mutate_inventory(root) do |inventory|
        item = inventory.fetch("firstPartyProductionAssets").fetch("items")
          .find { |entry| entry["path"].to_s.include?("C1B-005/Captures/Unity/") }
        path = item.fetch("path")
        item["sourceOwner"] = "Different Owner"
        item["sourceEvidence"] = item.fetch("sourceEvidence").map do |locator|
          locator == "project-author:kjh4845" ? "project-author:Different Owner" : locator
        end
        item["assetType"] = "CHARACTER_REFERENCE_RENDER"
        item["sourceEvidence"].reject! do |locator|
          locator.include?("C1B-005/GenerationManifest.yaml#stages.reference-render")
        end
      end

      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_C1B005_ASSET_TYPE_COUNT"
      assert_includes stdout, "rule=INVENTORY_C1B005_SOURCE_OWNER path=#{path}"
      assert_includes stdout, "rule=INVENTORY_C1B005_CAPTURE_CLASS path=#{path}"
      assert_includes stdout, "rule=INVENTORY_C1B005_CAPTURE_MANIFEST_EVIDENCE path=#{path}"
    end
  end

  def test_duplicate_invalid_and_oversize_yaml_fail_closed
    with_repository do |root|
      path = root.join("config/licenses/LicensePolicy.yaml")
      File.open(path, "a") { |file| file.write("\nLicensePolicy:\n  schemaVersion: 1\n") }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=POLICY_YAML_DUPLICATE_KEY"
    end

    with_repository do |root|
      File.write(root.join("config/licenses/ThirdPartyInventory.yaml"), "ThirdPartyInventory: [")
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=INVENTORY_YAML_INVALID"
    end

    with_repository do |root|
      File.write(root.join("config/repository/BinaryAssetInventory.yaml"), "x" * (2 * 1024 * 1024 + 1))
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=BINARY_INVENTORY_TOO_LARGE"
    end
  end

  def test_policy_symlink_is_rejected_without_following_target
    with_repository do |root|
      policy = root.join("config/licenses/LicensePolicy.yaml")
      target = root.join("outside-policy.yaml")
      FileUtils.mv(policy, target)
      File.symlink(target, policy)
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=GIT_ACTIVE_SYMLINK"
      assert_includes stdout, "rule=POLICY_SYMLINK"
    end
  end

  def test_missing_policy_or_notice_and_missing_notice_marker_fail
    with_repository do |root|
      File.delete(root.join("config/licenses/LicensePolicy.yaml"))
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=POLICY_MISSING"
    end

    with_repository do |root|
      File.delete(root.join("THIRD_PARTY_NOTICES.md"))
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=NOTICE_MISSING"
    end

    with_repository do |root|
      path = root.join("THIRD_PARTY_NOTICES.md")
      File.write(path, File.read(path).sub("Automatic Build remains prohibited", "build state omitted"))
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=NOTICE_REQUIRED_MARKER"
    end
  end

  def test_removed_media_reintroduction_fails_but_tracked_deletion_is_inactive
    deleted_path = "Project hotfix/Assets/TutorialInfo/Icons/URP.png"
    with_repository(extra_files: { deleted_path => "removed tutorial media\n" }) do |root|
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=UNREGISTERED_MEDIA_ASSET"

      File.delete(root.join(deleted_path))
      stdout, stderr, status = run_verifier(root)
      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "FINAL_RESULT=PASS"
    end
  end

  def test_new_font_audio_model_shader_and_native_dll_require_registration
    extras = {
      "incoming/font.ttf" => "font",
      "incoming/sound.ogg" => "audio",
      "incoming/model.fbx" => "model",
      "Project hotfix/Assets/Shaders/new.shader" => "shader",
      "Project hotfix/Assets/Plugins/new.dll" => "native",
    }
    with_repository(extra_files: extras) do |root|
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      extras.keys.each do |path|
        assert_includes stdout, "rule=UNREGISTERED_MEDIA_ASSET path=#{path}"
      end
    end
  end

  def test_package_cache_missing_and_partial_fail_only_when_option_is_enabled
    with_repository do |root|
      stdout, _stderr, status = run_verifier(root)
      assert_equal 0, status

      stdout, _stderr, status = run_verifier(root, verify_cache: true)
      assert_equal 1, status
      assert_includes stdout, "rule=PACKAGE_CACHE_MISSING"
    end

    with_repository(include_cache: true) do |root|
      package_root = cache_root_for(root, "com.unity.ai.navigation")
      FileUtils.rm_rf(package_root)
      stdout, _stderr, status = run_verifier(root, verify_cache: true)

      assert_equal 1, status
      assert_includes stdout, "rule=PACKAGE_CACHE_PACKAGE_SET"
      assert_includes stdout, "rule=PACKAGE_CACHE_PACKAGE_MISSING"
      assert_includes stdout, "rule=PACKAGE_CACHE_DIRECTORY_COUNT"
    end
  end

  def test_package_cache_root_license_notice_and_nested_inline_evidence_are_verified
    with_repository(include_cache: true) do |root|
      ai = cache_root_for(root, "com.unity.ai.navigation")
      burst = cache_root_for(root, "com.unity.burst")
      mathematics = cache_root_for(root, "com.unity.mathematics")
      File.delete(ai.join("LICENSE.md"))
      File.delete(burst.join("Third Party Notices.md"))
      File.delete(mathematics.join("Unity.Mathematics/Noise/LICENSE"))

      stdout, _stderr, status = run_verifier(root, verify_cache: true)

      assert_equal 1, status
      assert_includes stdout, "rule=PACKAGE_CACHE_ROOT_LICENSE"
      assert_includes stdout, "rule=PACKAGE_CACHE_ROOT_NOTICE"
      assert_includes stdout, "rule=PACKAGE_CACHE_EVIDENCE_MISSING"
      assert_includes stdout, "rule=PACKAGE_CACHE_LICENSE_COUNT"
      assert_includes stdout, "rule=PACKAGE_CACHE_NOTICE_COUNT"
    end
  end

  private

  def run_verifier(root, verify_cache: false)
    command = [RbConfig.ruby, VERIFIER.to_s, "--root", root.to_s]
    command << "--verify-package-cache" if verify_cache
    stdout, stderr, status = Open3.capture3(*command)
    [stdout, stderr, status.exitstatus]
  end

  def with_repository(include_cache: false, extra_files: {})
    Dir.mktmpdir("lic001-fixture-") do |directory|
      root = Pathname.new(directory)
      _stdout, stderr, status = Open3.capture3("git", "-C", root.to_s, "init", "-q")
      raise stderr unless status.success?

      SUPPORT_PATHS.each { |relative| copy_file(REPOSITORY_ROOT.join(relative), root.join(relative)) }
      REGISTERED_MEDIA_PATHS.each { |relative| link_or_copy(REPOSITORY_ROOT.join(relative), root.join(relative)) }
      File.write(root.join(".gitignore"), "Project hotfix/Library/\n")
      extra_files.each do |relative, content|
        path = root.join(relative)
        FileUtils.mkdir_p(path.dirname)
        File.binwrite(path, content)
      end
      copy_package_cache(root) if include_cache

      _stdout, stderr, status = Open3.capture3("git", "-C", root.to_s, "add", "-A", "--", ".")
      raise stderr unless status.success?
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

  def copy_package_cache(root)
    source_roots = {}
    Dir.children(REPOSITORY_ROOT.join("Project hotfix/Library/PackageCache")).each do |name|
      directory = REPOSITORY_ROOT.join("Project hotfix/Library/PackageCache", name)
      next unless directory.directory? && directory.join("package.json").file?

      metadata = JSON.parse(File.read(directory.join("package.json")))
      source_roots[metadata.fetch("name")] = directory
    end
    inventory = INVENTORY_TEMPLATE.fetch("ThirdPartyInventory")
    inventory.fetch("packages").each do |package|
      package_id = package.fetch("packageId")
      source = source_roots.fetch(package_id)
      destination = root.join("Project hotfix/Library/PackageCache", source.basename)
      copy_file(source.join("package.json"), destination.join("package.json"))
      package.fetch("sourceEvidence").grep(/^resolved-package:/).each do |locator|
        match = locator.match(/\Aresolved-package:[^@\/]+@[^\/]+\/(.+)\z/)
        raise "bad fixture locator: #{locator}" unless match

        relative = match[1]
        copy_file(source.join(relative), destination.join(relative))
      end
    end
  end

  def cache_root_for(root, package_id)
    cache = root.join("Project hotfix/Library/PackageCache")
    Dir.children(cache).each do |name|
      directory = cache.join(name)
      next unless directory.join("package.json").file?
      return directory if JSON.parse(File.read(directory.join("package.json")))["name"] == package_id
    end
    raise "cache package missing: #{package_id}"
  end

  def load_yaml(root, relative)
    YAML.safe_load(File.read(root.join(relative)), [], [], false)
  end

  def write_yaml(root, relative, document)
    File.write(root.join(relative), YAML.dump(document))
  end

  def mutate_inventory(root)
    document = load_yaml(root, "config/licenses/ThirdPartyInventory.yaml")
    yield document.fetch("ThirdPartyInventory")
    write_yaml(root, "config/licenses/ThirdPartyInventory.yaml", document)
  end

  def register_first_party_asset(root, relative, content, asset_type, intended_use: nil)
    path = root.join(relative)
    FileUtils.mkdir_p(path.dirname)
    File.binwrite(path, content)
    sha256 = Digest::SHA256.file(path).hexdigest
    add_first_party_inventory_record(root, relative, sha256, asset_type, intended_use: intended_use)
    mutate_binary_inventory(root) do |binary|
      binary.fetch("files") << {
        "path" => relative,
        "bytes" => File.size(path),
        "sha256" => sha256,
      }
      recompute_binary_summary(binary)
    end
  end

  def add_first_party_inventory_record(root, relative, sha256, asset_type, intended_use: nil)
    intended_use ||= if File.extname(relative).downcase == ".blend"
      "PRODUCTION_SOURCE"
    elsif asset_type.end_with?("_REFERENCE_RENDER")
      "PRODUCTION_EVIDENCE"
    else
      "PLAYER_CONTENT"
    end
    shipping_allowed = intended_use == "PLAYER_CONTENT"
    evidence = [
      "config/repository/BinaryAssetInventory.yaml#files[path=#{relative}]",
      "project-author:Fixture Owner",
    ]
    if asset_type == "BLENDER_SOURCE"
      evidence << "#{File.dirname(relative)}/GenerationManifest.yaml#stages.blend-source"
    elsif asset_type.end_with?("_REFERENCE_RENDER")
      manifest_root = relative.include?("/Renders/") ? relative.split("/Renders/", 2).first : File.dirname(relative)
      evidence << "#{manifest_root}/GenerationManifest.yaml#stages.reference-render.outputs[path=#{relative}]"
    end
    mutate_inventory(root) do |inventory|
      first_party = inventory.fetch("firstPartyProductionAssets")
      first_party.fetch("items") << {
        "path" => relative,
        "sha256" => sha256,
        "assetType" => asset_type,
        "sourceOwner" => "Fixture Owner",
        "sourceStatus" => "PROJECT_AUTHORED",
        "intendedUse" => intended_use,
        "licenseFamily" => "PROJECT_AUTHORED",
        "rightsStatus" => "FIRST_PARTY",
        "noticeDisposition" => "NO_THIRD_PARTY_NOTICE",
        "reviewOnly" => false,
        "shippingAllowed" => shipping_allowed,
        "sourceEvidence" => evidence,
      }
      first_party["itemCount"] = first_party.fetch("items").length
    end
  end

  def mutate_binary_inventory(root)
    document = load_yaml(root, "config/repository/BinaryAssetInventory.yaml")
    binary = document.fetch("BinaryAssetInventory")
    yield binary
    write_yaml(root, "config/repository/BinaryAssetInventory.yaml", document)
  end

  def mutate_json(root, relative)
    path = root.join(relative)
    document = JSON.parse(File.read(path))
    yield document
    File.write(path, JSON.pretty_generate(document) + "\n")
  end

  def recompute_binary_summary(binary, lfs_required: 0, lfs_tracked: 0)
    files = binary.fetch("files")
    binary["summary"] = {
      "fileCount" => files.length,
      "totalBytes" => files.sum { |entry| entry.fetch("bytes") },
      "uniqueContentHashes" => files.map { |entry| entry.fetch("sha256") }.uniq.length,
      "filesOver10MiB" => files.count { |entry| entry.fetch("bytes") > 10 * 1024 * 1024 },
      "currentLfsRequiredCandidates" => lfs_required,
      "currentLfsTrackedFiles" => lfs_tracked,
      "ordinaryGitBinaryFiles" => files.length - lfs_tracked,
    }
  end
end
