#!/usr/bin/env ruby

require "fileutils"
require "minitest/autorun"
require "open3"
require "pathname"
require "rbconfig"
require "tmpdir"
require "yaml"

class VerifyCharacterProportionProfileTest < Minitest::Test
  REPOSITORY_ROOT = Pathname.new(__dir__).join("../..").expand_path
  VERIFIER = REPOSITORY_ROOT.join("tools/verify_character_proportion_profile.rb")
  PROFILE_PATH = "config/character/CharacterProportionProfile.yaml"
  SUPPORT_PATHS = [
    "config/art/LowPolyStyleProfile.yaml",
    "config/art/ModelInteropProfile.yaml",
    "config/art/AlphaVisualQAProfile.yaml",
    "artifacts/review/character/C1_CHARACTER_HYBRID_CORE_v0.13_BELLY_CORRECTED_REVIEW.png",
  ].freeze

  def test_current_candidate_passes
    stdout, stderr, status = run_verifier(REPOSITORY_ROOT)

    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "PROFILE_STATE=START"
    assert_includes stdout, "CANDIDATE_STATUS=CANDIDATE"
    assert_includes stdout, "LANDMARK_COUNT=17"
    assert_includes stdout, "ENVELOPE_COUNT=11"
    assert_includes stdout, "SOURCE_HASH_MATCH=true"
    assert_includes stdout, "GENERATED_OR_EXECUTED_COUNT=0"
    assert_includes stdout, "FINAL_RESULT=PASS"
  end

  def test_current_c1b002_change_scope_passes_without_asset_outputs
    stdout, stderr, status = run_verifier(REPOSITORY_ROOT, scope: true)

    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "C1B002_SCOPE_CHECKED=true"
    assert_includes stdout, "FINAL_RESULT=PASS"
  end

  def test_missing_profile_fails
    with_repository do |root|
      File.delete(root.join(PROFILE_PATH))
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_MISSING"
    end
  end

  def test_profile_symlink_fails_closed
    with_repository do |root|
      profile = root.join(PROFILE_PATH)
      File.delete(profile)
      File.symlink(REPOSITORY_ROOT.join(PROFILE_PATH), profile)
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_SYMLINK"
    end
  end

  def test_invalid_duplicate_and_oversize_yaml_fail
    with_repository do |root|
      File.write(root.join(PROFILE_PATH), "invalid: [")
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_YAML_INVALID"
    end

    with_repository do |root|
      File.open(root.join(PROFILE_PATH), "a") { |file| file.write("\nCharacterProportionProfile: {}\n") }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_YAML_DUPLICATE_KEY"
    end

    with_repository do |root|
      File.open(root.join(PROFILE_PATH), "a") { |file| file.write("#" * (256 * 1024)) }
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_TOO_LARGE"
    end
  end

  def test_metadata_cannot_claim_lock_or_approval
    with_repository do |root|
      rewrite_profile(root) do |profile|
        profile["state"] = "LOCKED"
        profile["approval"]["userApprovalRecorded"] = true
        profile["approval"]["visualApprovalClaimed"] = true
        profile["approval"]["lockedValueCount"] = 1
        profile["approval"]["meaning"] = "Final user-approved production profile"
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=PROFILE_STATE"
      assert_includes stdout, "rule=USER_APPROVAL_CLAIM"
      assert_includes stdout, "rule=VISUAL_APPROVAL_CLAIM"
      assert_includes stdout, "rule=LOCKED_VALUE_COUNT"
      assert_includes stdout, "rule=APPROVAL_MEANING"
    end
  end

  def test_direction_source_hash_and_pixel_boundary_are_verified
    with_repository do |root|
      rewrite_profile(root) do |profile|
        profile["directionSource"]["sha256"] = "0" * 64
        profile["directionSource"]["pixelMeasurementUsed"] = true
        profile["directionSource"]["referenceReplicaAllowed"] = true
        profile["directionSource"]["constraints"] = [
          "Derive all exact geometry from image pixels",
          "Replica is mandatory",
          "Copy proprietary dimensions",
        ]
      end
      File.write(root.join(SUPPORT_PATHS.last), "changed")
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=SOURCE_DECLARED_SHA"
      assert_includes stdout, "rule=SOURCE_HASH_MISMATCH"
      assert_includes stdout, "rule=SOURCE_PIXEL_MEASUREMENT"
      assert_includes stdout, "rule=SOURCE_REPLICA_ALLOWED"
      assert_includes stdout, "rule=SOURCE_CONSTRAINTS"
    end
  end

  def test_art_profile_references_are_exact_and_resolved
    with_repository do |root|
      rewrite_profile(root) do |profile|
        profile["profileReferences"]["modelInteropProfile"]["revision"] = "r99"
      end
      interop = YAML.safe_load(File.read(root.join("config/art/ModelInteropProfile.yaml")), aliases: false)
      interop["ModelInteropProfile"]["profileId"] = "wrong"
      File.write(root.join("config/art/ModelInteropProfile.yaml"), YAML.dump(interop))
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=REFERENCE_REVISION"
      assert_includes stdout, "rule=REFERENCE_PROFILE_ID"
    end
  end

  def test_normalization_axes_scale_and_gameplay_height_stay_separate
    with_repository do |root|
      rewrite_profile(root) do |profile|
        normalization = profile["normalization"]
        normalization["gameplayCharacterHeightMeters"] = 1.6
        normalization["coordinateFrame"]["upAxis"] = "+Z"
        normalization["transformRules"]["runtimeScale"] = [1.0, 2.0, 1.0]
        normalization["transformRules"]["negativeScaleAllowed"] = true
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=GAMEPLAY_HEIGHT_DEFERRED"
      assert_includes stdout, "rule=NORMALIZATION_COORDINATE_FRAME"
      assert_includes stdout, "rule=NORMALIZATION_RUNTIME_SCALE"
      assert_includes stdout, "rule=NORMALIZATION_NEGATIVE_SCALE"
    end
  end

  def test_landmark_exact_set_and_unique_ids_are_required
    with_repository do |root|
      rewrite_profile(root) do |profile|
        profile["landmarks"].pop
        duplicate = Marshal.load(Marshal.dump(profile["landmarks"].first))
        profile["landmarks"] << duplicate
        profile["landmarks"].last["id"] = profile["landmarks"].first["id"]
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=LANDMARK_ID_DUPLICATE_OR_INVALID"
      assert_includes stdout, "rule=LANDMARK_EXACT_SET"
    end
  end

  def test_landmark_numeric_ranges_and_bilateral_symmetry_are_required
    with_repository do |root|
      rewrite_profile(root) do |profile|
        left = profile["landmarks"].find { |entry| entry["id"] == "Elbow_L" }
        right = profile["landmarks"].find { |entry| entry["id"] == "Elbow_R" }
        left["positionH"]["y"] = 2.0
        right["positionH"]["z"] = 0.1
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=LANDMARK_HEIGHT_RANGE"
      assert_includes stdout, "rule=LANDMARK_BILATERAL_SYMMETRY"
    end
  end

  def test_malformed_landmark_position_fails_without_crashing
    with_repository do |root|
      rewrite_profile(root) do |profile|
        shoulder = profile["landmarks"].find { |entry| entry["id"] == "Shoulder_L" }
        shoulder["positionH"] = nil
      end
      stdout, stderr, status = run_verifier(root)

      assert_equal 1, status, stderr + stdout
      assert_empty stderr
      assert_includes stdout, "rule=LANDMARK_POSITION_FIELDS"
      assert_includes stdout, "FINAL_RESULT=FAIL"
    end
  end

  def test_malformed_numeric_containers_fail_without_crashing
    with_repository do |root|
      rewrite_profile(root) do |profile|
        profile["neutralBoundsH"]["fullWidthIncludingArms"] = {}
        profile["directionInvariants"]["forearmTerminalBottomHeightH"] = {}
      end
      stdout, stderr, status = run_verifier(root)

      assert_equal 1, status, stderr + stdout
      assert_empty stderr
      assert_includes stdout, "rule=BOUNDS_NUMERIC"
      assert_includes stdout, "rule=INVARIANT_NUMERIC"
      assert_includes stdout, "FINAL_RESULT=FAIL"
    end
  end

  def test_exact_candidate_measurement_digest_rejects_semantic_drift
    with_repository do |root|
      rewrite_profile(root) do |profile|
        shoulder = profile["landmarks"].find { |entry| entry["id"] == "Shoulder_L" }
        crown = profile["landmarks"].find { |entry| entry["id"] == "Crown" }
        head = profile["silhouetteEnvelopes"].find { |entry| entry["id"] == "HeadMax" }
        upper_arm = profile["silhouetteEnvelopes"].find { |entry| entry["id"] == "UpperArm" }
        shoulder["positionH"]["y"] = 0.72
        profile["neutralBoundsH"]["fullWidthIncludingArms"] = 0.80
        head["fullWidthH"] = 0.30
        crown["semantic"] = "LIMB_CENTER"
        upper_arm["scope"] = "CORE"
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=MEASUREMENT_SET_DIGEST"
    end
  end

  def test_hidden_lock_and_gameplay_approval_fields_are_rejected
    with_repository do |root|
      rewrite_profile(root) do |profile|
        profile["approval"]["hidden"] = "LOCKED"
        profile["normalization"]["approvedGameplayHeightMeters"] = 1.8
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=APPROVAL_FIELD_SET"
      assert_includes stdout, "rule=NORMALIZATION_FIELD_SET"
      assert_includes stdout, "rule=UNAPPROVED_LOCKED_CLAIM"
    end
  end

  def test_envelope_exact_set_and_width_depth_equation_are_required
    with_repository do |root|
      rewrite_profile(root) do |profile|
        profile["silhouetteEnvelopes"].reject! { |entry| entry["id"] == "Knee" }
        chest = profile["silhouetteEnvelopes"].find { |entry| entry["id"] == "ChestBody" }
        chest["totalDepthH"] = 0.5
        chest["fullWidthH"] = -0.1
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=ENVELOPE_EXACT_SET"
      assert_includes stdout, "rule=ENVELOPE_DEPTH_SUM"
      assert_includes stdout, "rule=ENVELOPE_DIMENSION_RANGE"
    end
  end

  def test_anatomical_order_and_head_direction_are_verified
    with_repository do |root|
      rewrite_profile(root) do |profile|
        chin = profile["landmarks"].find { |entry| entry["id"] == "Chin" }
        elbow = profile["landmarks"].find { |entry| entry["id"] == "Elbow_L" }
        chin["positionH"]["y"] = 0.70
        elbow["positionH"]["y"] = 0.30
        profile["directionInvariants"]["headHeightH"] = 0.30
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=HEAD_HEIGHT_DIRECTION"
      assert_includes stdout, "rule=LANDMARK_ANATOMICAL_ORDER"
    end
  end

  def test_terminal_delta_and_forbidden_visible_parts_are_verified
    with_repository do |root|
      rewrite_profile(root) do |profile|
        invariants = profile["directionInvariants"]
        invariants["forearmTerminalBottomHeightH"] = 0.50
        invariants["separateVisibleHandMesh"] = true
        invariants["separateVisibleFootOrShoeMesh"] = true
        invariants["bellyOnlySilhouetteAllowed"] = true
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=FOREARM_TERMINAL_DELTA_DERIVATION"
      assert_includes stdout, "rule=FOREARM_TERMINAL_DELTA_DIRECTION"
      assert_includes stdout, "rule=INVARIANT_FORBIDDEN_SHAPE"
    end
  end

  def test_lower_leg_terminal_bottom_must_match_ground
    with_repository do |root|
      rewrite_profile(root) do |profile|
        profile["directionInvariants"]["lowerLegTerminalBottomHeightH"] = 0.10
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=LOWER_TERMINAL_GROUND_DERIVATION"
      assert_includes stdout, "rule=LOWER_TERMINAL_CENTER_RELATION"
      assert_includes stdout, "rule=MEASUREMENT_SET_DIGEST"
    end
  end

  def test_downstream_values_must_remain_deferred
    with_repository do |root|
      rewrite_profile(root) do |profile|
        profile["deferredDecisions"]["colliderDimensions"]["value"] = {"radius" => 0.2}
        profile["deferredDecisions"].delete("gameplayCameraValues")
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=DEFERRED_EXACT_SET"
      assert_includes stdout, "rule=DEFERRED_VALUE"
    end
  end

  def test_execution_and_start_targets_cannot_claim_downstream_work
    with_repository do |root|
      rewrite_profile(root) do |profile|
        profile["execution"]["generatedBlendCount"] = 1
        targets = profile["startingVerificationTargets"]
        targets["state"] = "LOCKED"
        targets["pixelPerfectComparisonRequired"] = true
        targets["humanApprovalRequired"] = false
      end
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=EXECUTION_NOT_ZERO"
      assert_includes stdout, "rule=TARGET_STATE"
      assert_includes stdout, "rule=TARGET_PIXEL_PERFECT"
      assert_includes stdout, "rule=TARGET_HUMAN_APPROVAL"
    end
  end

  def test_scope_rejects_generated_asset_and_unrelated_change
    with_git_repository do |root|
      File.write(root.join("candidate.blend"), "not a real blend")
      FileUtils.mkdir_p(root.join("unrelated"))
      File.write(root.join("unrelated/note.txt"), "outside C1B-002")
      stdout, _stderr, status = run_verifier(root, scope: true)

      assert_equal 1, status
      assert_includes stdout, "rule=C1B002_ASSET_OUTPUT_PRESENT"
      assert_includes stdout, "rule=C1B002_SCOPE_PATH"
    end
  end

  private

  def run_verifier(root, scope: false)
    command = [RbConfig.ruby, VERIFIER.to_s, "--root", root.to_s]
    command << "--check-c1b002-scope" if scope
    stdout, stderr, status = Open3.capture3(*command)
    [stdout, stderr, status.exitstatus]
  end

  def with_repository
    Dir.mktmpdir("c1b002-profile-") do |directory|
      root = Pathname.new(directory)
      ([PROFILE_PATH] + SUPPORT_PATHS).each do |relative|
        destination = root.join(relative)
        FileUtils.mkdir_p(destination.dirname)
        FileUtils.cp(REPOSITORY_ROOT.join(relative), destination)
      end
      yield root
    end
  end

  def with_git_repository
    with_repository do |root|
      _stdout, stderr, status = Open3.capture3("git", "init", "-q", root.to_s)
      raise stderr unless status.success?
      commands = [
        ["git", "-C", root.to_s, "add", "."],
        ["git", "-C", root.to_s, "-c", "user.name=C1B Test", "-c",
          "user.email=c1b@example.invalid", "commit", "-qm", "fixture"],
      ]
      commands.each do |command|
        _command_stdout, command_stderr, command_status = Open3.capture3(*command)
        raise command_stderr unless command_status.success?
      end
      yield root
    end
  end

  def rewrite_profile(root)
    path = root.join(PROFILE_PATH)
    document = YAML.safe_load(File.read(path), aliases: false)
    yield document.fetch("CharacterProportionProfile")
    File.write(path, YAML.dump(document))
  end
end
