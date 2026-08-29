#!/usr/bin/env ruby

require "digest"
require "json"
require "open3"
require "optparse"
require "pathname"
require "set"
require "yaml"

class CharacterProportionProfileVerifier
  PROFILE_PATH = "config/character/CharacterProportionProfile.yaml"
  MAX_PROFILE_BYTES = 256 * 1024
  MAX_REFERENCE_BYTES = 2 * 1024 * 1024
  EPSILON = 0.000001

  REQUIRED_TOP_LEVEL_FIELDS = %w[
    schemaVersion profileId version revision state candidateStatus ownerTask approval
    measurementSetSha256
    directionSource profileReferences normalization measurementSemantics neutralBoundsH
    landmarks silhouetteEnvelopes directionInvariants startingVerificationTargets
    deferredDecisions execution
  ].sort.freeze

  REQUIRED_LANDMARK_IDS = %w[
    Crown Chin Shoulder_L Shoulder_R Elbow_L Elbow_R ForearmTerminal_L
    ForearmTerminal_R Chest Pelvis Crotch Hip_L Hip_R Knee_L Knee_R
    LowerLegTerminal_L LowerLegTerminal_R
  ].freeze

  CENTER_LANDMARK_IDS = %w[Crown Chin Chest Pelvis Crotch].freeze
  BILATERAL_LANDMARK_BASES = %w[
    Shoulder Elbow ForearmTerminal Hip Knee LowerLegTerminal
  ].freeze

  REQUIRED_ENVELOPE_IDS = %w[
    HeadMax Chin ShoulderBody ChestBody PelvisBody CrotchBridge UpperArm
    ForearmTerminal UpperThigh Knee LowerLegTerminal
  ].freeze

  REQUIRED_DEFERRED_FIELDS = %w[
    gameplayCharacterHeightMeters colliderDimensions gameplayReachAndAnchors
    massJointAndRagdollTuning prototypeRigBonePlacement productionHelperBoneExceptions
    topologyUvWeightsAndLods
    bevelNormalsMaterialsPaletteShaderLighting gameplayCameraValues actionAnimationPolish
  ].freeze

  EXPECTED_REFERENCES = {
    "lowPolyStyleProfile" => {
      "profileId" => "LowPolyStyleProfile-ART-001-r01",
      "revision" => "r01",
      "path" => "config/art/LowPolyStyleProfile.yaml",
      "root" => "LowPolyStyleProfile",
    },
    "modelInteropProfile" => {
      "profileId" => "ModelInteropProfile-ART-001-r01",
      "revision" => "r01",
      "path" => "config/art/ModelInteropProfile.yaml",
      "root" => "ModelInteropProfile",
    },
    "alphaVisualQaProfile" => {
      "profileId" => "AlphaVisualQAProfile-ART-001-r01",
      "revision" => "r01",
      "path" => "config/art/AlphaVisualQAProfile.yaml",
      "root" => "AlphaVisualQAProfile",
    },
  }.freeze

  EXPECTED_SOURCE_PATH =
    "artifacts/review/character/C1_CHARACTER_HYBRID_CORE_v0.13_BELLY_CORRECTED_REVIEW.png"
  EXPECTED_SOURCE_SHA256 =
    "c1def169cefd59f19339a5b5edbac2dfd0c8fe9a05eba9ee0afb1ae598bab616"
  EXPECTED_APPROVAL_MEANING =
    "A reversible blockout candidate; not an approved C1b proportion, gameplay scale, collider or production mesh"
  EXPECTED_SOURCE_CONSTRAINTS = [
    "Use the approved rounded head, short wide torso, low center and short thick continuous limbs as qualitative direction only.",
    "Do not reverse-engineer pixel distances into geometry, bones, colliders, anchors, reach or gameplay scale.",
    "Do not reproduce a reference work's proprietary mesh, silhouette, material, animation or dimensions.",
  ].freeze
  EXPECTED_MEASUREMENT_SET_SHA256 =
    "76c98acfe8cfbf01b51936b29c2f6ba2e78c26222dfd53c033fe84233e562722"
  MEASUREMENT_DIGEST_FIELDS = %w[
    normalization measurementSemantics neutralBoundsH landmarks silhouetteEnvelopes
    directionInvariants startingVerificationTargets deferredDecisions
  ].freeze

  C1B002_ALLOWED_CHANGE_PATHS = [
    PROFILE_PATH,
    "tools/verify_character_proportion_profile.rb",
    "tools/tests/verify_character_proportion_profile_test.rb",
    "docs/03_IMPLEMENTATION_PLAN.md",
    "artifacts/reports/FOUNDATION_DECISION_RATIONALE.md",
  ].freeze
  C1B002_ALLOWED_CHANGE_PREFIXES = ["artifacts/evidence/G0/C1B-002/"].freeze
  FORBIDDEN_ASSET_EXTENSIONS = %w[
    .blend .fbx .glb .prefab .unity .asset .mat .png .jpg .jpeg .exr .hdr .psd .tif .tiff
  ].freeze

  def initialize(root, check_scope: false)
    @root = Pathname.new(root).expand_path
    @check_scope = check_scope
    @violations = []
    @violation_keys = Set.new
    @profile = nil
    @landmarks = {}
    @envelopes = {}
    @source_hash_match = false
    @scope_paths_checked = 0
  end

  def run
    document = load_yaml(PROFILE_PATH, "PROFILE", MAX_PROFILE_BYTES)
    validate_document(document)
    validate_metadata
    validate_source
    validate_references
    validate_normalization
    validate_landmarks
    validate_envelopes
    validate_invariants
    validate_deferred_and_execution
    validate_measurement_digest
    validate_no_locked_claims
    validate_scope if @check_scope
    print_report
    @violations.empty? ? 0 : 1
  rescue StandardError => error
    add("VERIFIER_INTERNAL_ERROR", error.class.name)
    print_report
    1
  end

  private

  def validate_document(document)
    expect(document.is_a?(Hash) && document.keys == ["CharacterProportionProfile"],
      "PROFILE_DOCUMENT_ROOT", PROFILE_PATH)
    @profile = document && document["CharacterProportionProfile"]
    unless @profile.is_a?(Hash)
      add("PROFILE_ROOT_INVALID", PROFILE_PATH)
      @profile = {}
      return
    end
    expect(@profile.keys.sort == REQUIRED_TOP_LEVEL_FIELDS, "PROFILE_FIELD_SET", PROFILE_PATH)
  end

  def validate_metadata
    expect(@profile["schemaVersion"] == 1, "PROFILE_SCHEMA_VERSION", PROFILE_PATH)
    expect(@profile["profileId"] == "CharacterProportionProfile-C1B-002-r01",
      "PROFILE_ID", PROFILE_PATH)
    expect(@profile["version"] == "0.1.0-start", "PROFILE_VERSION", PROFILE_PATH)
    expect(@profile["revision"] == "r01", "PROFILE_REVISION", PROFILE_PATH)
    expect(@profile["state"] == "START", "PROFILE_STATE", PROFILE_PATH)
    expect(@profile["candidateStatus"] == "CANDIDATE", "PROFILE_CANDIDATE_STATUS", PROFILE_PATH)
    expect(@profile["ownerTask"] == "C1B-002", "PROFILE_OWNER", PROFILE_PATH)
    expect(@profile["measurementSetSha256"] == EXPECTED_MEASUREMENT_SET_SHA256,
      "MEASUREMENT_DECLARED_DIGEST", PROFILE_PATH)

    approval = hash(@profile["approval"])
    expected_approval_fields = %w[
      approvalTask approvalGate userApprovalRecorded visualApprovalClaimed lockedValueCount meaning
    ]
    expect(approval.keys.sort == expected_approval_fields.sort, "APPROVAL_FIELD_SET", PROFILE_PATH)
    expect(approval["approvalTask"] == "C1B-006", "APPROVAL_TASK", PROFILE_PATH)
    expect(approval["approvalGate"] == "UG-C1B", "APPROVAL_GATE", PROFILE_PATH)
    expect(approval["userApprovalRecorded"] == false, "USER_APPROVAL_CLAIM", PROFILE_PATH)
    expect(approval["visualApprovalClaimed"] == false, "VISUAL_APPROVAL_CLAIM", PROFILE_PATH)
    expect(approval["lockedValueCount"] == 0, "LOCKED_VALUE_COUNT", PROFILE_PATH)
    expect(approval["meaning"] == EXPECTED_APPROVAL_MEANING, "APPROVAL_MEANING", PROFILE_PATH)
  end

  def validate_source
    source = hash(@profile["directionSource"])
    expected_source_fields = %w[
      profileId path sha256 role pixelMeasurementUsed referenceReplicaAllowed constraints
    ]
    expect(source.keys.sort == expected_source_fields.sort, "SOURCE_FIELD_SET", PROFILE_PATH)
    expect(source["profileId"] == "C1a-Hybrid-Core-v0.13", "SOURCE_PROFILE_ID", PROFILE_PATH)
    expect(source["path"] == EXPECTED_SOURCE_PATH, "SOURCE_PATH", PROFILE_PATH)
    expect(source["sha256"] == EXPECTED_SOURCE_SHA256, "SOURCE_DECLARED_SHA", PROFILE_PATH)
    expect(source["role"] == "DIRECTION_ONLY", "SOURCE_ROLE", PROFILE_PATH)
    expect(source["pixelMeasurementUsed"] == false, "SOURCE_PIXEL_MEASUREMENT", PROFILE_PATH)
    expect(source["referenceReplicaAllowed"] == false, "SOURCE_REPLICA_ALLOWED", PROFILE_PATH)
    expect(source["constraints"] == EXPECTED_SOURCE_CONSTRAINTS, "SOURCE_CONSTRAINTS", PROFILE_PATH)

    absolute = safe_regular_file(EXPECTED_SOURCE_PATH, "SOURCE", MAX_REFERENCE_BYTES)
    return unless absolute

    actual = Digest::SHA256.file(absolute).hexdigest
    @source_hash_match = actual == EXPECTED_SOURCE_SHA256
    expect(@source_hash_match, "SOURCE_HASH_MISMATCH", EXPECTED_SOURCE_PATH)
  end

  def validate_references
    references = hash(@profile["profileReferences"])
    expect(references.keys.sort == EXPECTED_REFERENCES.keys.sort,
      "REFERENCE_FIELD_SET", PROFILE_PATH)

    EXPECTED_REFERENCES.each do |key, expected|
      reference = hash(references[key])
      expect(reference.keys.sort == %w[path profileId revision], "REFERENCE_ENTRY_FIELD_SET", expected["path"])
      %w[profileId revision path].each do |field|
        expect(reference[field] == expected[field], "REFERENCE_#{field.upcase}", expected["path"])
      end
      document = load_yaml(expected["path"], "REFERENCE", MAX_PROFILE_BYTES)
      referenced_profile = document && document[expected["root"]]
      expect(referenced_profile.is_a?(Hash), "REFERENCE_ROOT", expected["path"])
      next unless referenced_profile.is_a?(Hash)

      expect(referenced_profile["profileId"] == expected["profileId"],
        "REFERENCE_PROFILE_ID", expected["path"])
      expect(referenced_profile["revision"] == expected["revision"],
        "REFERENCE_REVISION", expected["path"])
    end
  end

  def validate_normalization
    normalization = hash(@profile["normalization"])
    expected_normalization_fields = %w[
      unit totalHeightH dimensionless normalizedSourceUnitsPerH gameplayCharacterHeightMeters
      gameplayHeightOwnerTask origin coordinateFrame sourceAuthoringAxes targetAxes transformRules
    ]
    expect(normalization.keys.sort == expected_normalization_fields.sort,
      "NORMALIZATION_FIELD_SET", PROFILE_PATH)
    expect(normalization["unit"] == "H", "NORMALIZATION_UNIT", PROFILE_PATH)
    expect(number_equal?(normalization["totalHeightH"], 1.0), "NORMALIZATION_HEIGHT", PROFILE_PATH)
    expect(normalization["dimensionless"] == true, "NORMALIZATION_DIMENSIONLESS", PROFILE_PATH)
    expect(number_equal?(normalization["normalizedSourceUnitsPerH"], 1.0),
      "NORMALIZATION_SOURCE_UNITS", PROFILE_PATH)
    expect(normalization.key?("gameplayCharacterHeightMeters") &&
      normalization["gameplayCharacterHeightMeters"].nil?, "GAMEPLAY_HEIGHT_DEFERRED", PROFILE_PATH)
    expect(normalization["gameplayHeightOwnerTask"] == "C1B-006",
      "GAMEPLAY_HEIGHT_OWNER", PROFILE_PATH)
    expect(nonempty_string?(normalization["origin"]), "NORMALIZATION_ORIGIN", PROFILE_PATH)

    frame = hash(normalization["coordinateFrame"])
    expect(frame == {
      "rightAxis" => "+X", "upAxis" => "+Y", "forwardAxis" => "+Z",
      "anatomicalLeftXSign" => "negative",
    }, "NORMALIZATION_COORDINATE_FRAME", PROFILE_PATH)
    source_axes = hash(normalization["sourceAuthoringAxes"])
    expect(source_axes == {"blenderUpAxis" => "+Z", "blenderCharacterForwardAxis" => "-Y"},
      "NORMALIZATION_BLENDER_AXES", PROFILE_PATH)
    target_axes = hash(normalization["targetAxes"])
    expect(target_axes == {"unityUpAxis" => "+Y", "unityCharacterForwardAxis" => "+Z"},
      "NORMALIZATION_UNITY_AXES", PROFILE_PATH)
    transforms = hash(normalization["transformRules"])
    expect(transforms["runtimeScale"] == [1.0, 1.0, 1.0], "NORMALIZATION_RUNTIME_SCALE", PROFILE_PATH)
    expect(transforms["negativeScaleAllowed"] == false, "NORMALIZATION_NEGATIVE_SCALE", PROFILE_PATH)
    expect(transforms["perFileScaleCorrectionAllowed"] == false,
      "NORMALIZATION_SCALE_CORRECTION", PROFILE_PATH)

    semantics = hash(@profile["measurementSemantics"])
    expected_semantics = %w[
      positionH centerlineHeightPlane limbCenter frontViewFullWidthH sideViewTotalDepthH
      crossSectionScope silhouetteEnvelope surfaceTangent approvedPixelSource
    ]
    expect(semantics.keys.sort == expected_semantics.sort, "MEASUREMENT_SEMANTIC_FIELDS", PROFILE_PATH)
    expected_semantics.reject { |key| key == "approvedPixelSource" }.each do |key|
      expect(nonempty_string?(semantics[key]), "MEASUREMENT_SEMANTIC_VALUE", key)
    end
    expect(semantics["approvedPixelSource"] == false, "MEASUREMENT_PIXEL_SOURCE", PROFILE_PATH)

    bounds = hash(@profile["neutralBoundsH"])
    expected_bounds_fields = %w[height fullWidthIncludingArms totalDepth groundMinimumY crownMaximumY]
    expect(bounds.keys.sort == expected_bounds_fields.sort, "BOUNDS_FIELD_SET", PROFILE_PATH)
    %w[height fullWidthIncludingArms totalDepth groundMinimumY crownMaximumY].each do |field|
      expect(finite_number?(bounds[field]), "BOUNDS_NUMERIC", field)
    end
    expect(number_equal?(bounds["height"], 1.0), "BOUNDS_HEIGHT", PROFILE_PATH)
    expect(number_equal?(bounds["groundMinimumY"], 0.0), "BOUNDS_GROUND", PROFILE_PATH)
    expect(number_equal?(bounds["crownMaximumY"], 1.0), "BOUNDS_CROWN", PROFILE_PATH)
    if finite_number?(bounds["fullWidthIncludingArms"])
      expect(bounds["fullWidthIncludingArms"] > 0.0 && bounds["fullWidthIncludingArms"] <= 1.0,
        "BOUNDS_WIDTH_RANGE", PROFILE_PATH)
    end
    if finite_number?(bounds["totalDepth"])
      expect(bounds["totalDepth"] > 0.0 && bounds["totalDepth"] <= 1.0,
        "BOUNDS_DEPTH_RANGE", PROFILE_PATH)
    end
  end

  def validate_landmarks
    entries = @profile["landmarks"]
    unless entries.is_a?(Array)
      add("LANDMARKS_INVALID", PROFILE_PATH)
      return
    end

    entries.each do |entry|
      expected_fields = %w[
        id semantic crossSectionScope positionH frontViewFullWidthH sideViewTotalDepthH
      ]
      unless entry.is_a?(Hash) && entry.keys.sort == expected_fields.sort
        add("LANDMARK_FIELD_SET", PROFILE_PATH)
        next
      end
      id = entry["id"]
      if !nonempty_string?(id) || @landmarks.key?(id)
        add("LANDMARK_ID_DUPLICATE_OR_INVALID", id.to_s)
        next
      end
      @landmarks[id] = entry
      expect(%w[SURFACE_TANGENT CENTERLINE_HEIGHT_PLANE LIMB_CENTER].include?(entry["semantic"]),
        "LANDMARK_SEMANTIC", id)
      expect(%w[CORE EACH_LIMB POINT_TANGENT].include?(entry["crossSectionScope"]),
        "LANDMARK_CROSS_SECTION_SCOPE", id)
      cross_fields = %w[frontViewFullWidthH sideViewTotalDepthH]
      cross_fields.each do |field|
        expect(finite_number?(entry[field]), "LANDMARK_CROSS_SECTION_NUMERIC", id)
      end
      if cross_fields.all? { |field| finite_number?(entry[field]) }
        if id == "Crown"
          expect(entry["crossSectionScope"] == "POINT_TANGENT" &&
            cross_fields.all? { |field| number_equal?(entry[field], 0.0) },
            "LANDMARK_CROWN_TANGENT_SECTION", id)
        else
          expect(entry["crossSectionScope"] != "POINT_TANGENT" &&
            cross_fields.all? { |field| entry[field] > 0.0 && entry[field] <= 1.0 },
            "LANDMARK_CROSS_SECTION_RANGE", id)
        end
      end
      position = hash(entry["positionH"])
      expect(position.keys.sort == %w[x y z], "LANDMARK_POSITION_FIELDS", id)
      %w[x y z].each { |axis| expect(finite_number?(position[axis]), "LANDMARK_POSITION_NUMERIC", id) }
      next unless %w[x y z].all? { |axis| finite_number?(position[axis]) }

      expect(position["y"].between?(0.0, 1.0), "LANDMARK_HEIGHT_RANGE", id)
      expect(position["x"].abs <= 0.5 && position["z"].abs <= 0.5,
        "LANDMARK_HORIZONTAL_RANGE", id)
    end

    expect(@landmarks.keys.sort == REQUIRED_LANDMARK_IDS.sort, "LANDMARK_EXACT_SET", PROFILE_PATH)
    CENTER_LANDMARK_IDS.each do |id|
      next unless @landmarks[id]
      position = hash(@landmarks[id]["positionH"])
      expect(number_equal?(position["x"], 0.0) && number_equal?(position["z"], 0.0),
        "LANDMARK_CENTERLINE", id)
    end
    BILATERAL_LANDMARK_BASES.each do |base|
      left = @landmarks["#{base}_L"]
      right = @landmarks["#{base}_R"]
      next unless left && right
      left_position = hash(left["positionH"])
      right_position = hash(right["positionH"])
      right_x = right_position["x"]
      mirrored = finite_number?(right_x) && number_equal?(left_position["x"], -right_x) &&
        number_equal?(left_position["y"], right_position["y"]) &&
        number_equal?(left_position["z"], right_position["z"]) &&
        number_equal?(left["frontViewFullWidthH"], right["frontViewFullWidthH"]) &&
        number_equal?(left["sideViewTotalDepthH"], right["sideViewTotalDepthH"])
      signs_valid = finite_number?(left_position["x"]) && finite_number?(right_x) &&
        left_position["x"] < 0.0 && right_x > 0.0
      expect(mirrored && signs_valid,
        "LANDMARK_BILATERAL_SYMMETRY", base)
    end
  end

  def validate_envelopes
    entries = @profile["silhouetteEnvelopes"]
    unless entries.is_a?(Array)
      add("ENVELOPES_INVALID", PROFILE_PATH)
      return
    end

    entries.each do |entry|
      required = %w[id scope heightH fullWidthH frontExtentH rearExtentH totalDepthH]
      unless entry.is_a?(Hash) && entry.keys.sort == required.sort
        add("ENVELOPE_FIELD_SET", PROFILE_PATH)
        next
      end
      id = entry["id"]
      if !nonempty_string?(id) || @envelopes.key?(id)
        add("ENVELOPE_ID_DUPLICATE_OR_INVALID", id.to_s)
        next
      end
      @envelopes[id] = entry
      expect(%w[CORE EACH_LIMB].include?(entry["scope"]), "ENVELOPE_SCOPE", id)
      numeric_fields = %w[heightH fullWidthH frontExtentH rearExtentH totalDepthH]
      numeric_fields.each { |field| expect(finite_number?(entry[field]), "ENVELOPE_NUMERIC", id) }
      next unless numeric_fields.all? { |field| finite_number?(entry[field]) }

      expect(entry["heightH"].between?(0.0, 1.0), "ENVELOPE_HEIGHT_RANGE", id)
      %w[fullWidthH frontExtentH rearExtentH totalDepthH].each do |field|
        expect(entry[field] > 0.0 && entry[field] <= 1.0, "ENVELOPE_DIMENSION_RANGE", id)
      end
      expect(number_equal?(entry["frontExtentH"] + entry["rearExtentH"], entry["totalDepthH"]),
        "ENVELOPE_DEPTH_SUM", id)
    end

    expect(@envelopes.keys.sort == REQUIRED_ENVELOPE_IDS.sort, "ENVELOPE_EXACT_SET", PROFILE_PATH)
    bounds = hash(@profile["neutralBoundsH"])
    unless @envelopes.empty?
      depths = @envelopes.values.map { |entry| entry["totalDepthH"] }.select { |value| finite_number?(value) }
      if depths.length == @envelopes.length && finite_number?(bounds["totalDepth"])
        expect(number_equal?(depths.max, bounds["totalDepth"]), "BOUNDS_ENVELOPE_DEPTH", PROFILE_PATH)
      end
    end
    torso_ids = %w[ShoulderBody ChestBody PelvisBody]
    if torso_ids.all? { |id| @envelopes[id] }
      depths = torso_ids.map { |id| @envelopes[id]["totalDepthH"] }
      maximum = hash(@profile["directionInvariants"])["torsoDepthVariationMaximumH"]
      if depths.all? { |value| finite_number?(value) } && finite_number?(maximum)
        expect(depths.max - depths.min <= maximum + EPSILON, "TORSO_DEPTH_VARIATION", PROFILE_PATH)
      end
    end
    if @envelopes["HeadMax"] && @envelopes["ShoulderBody"]
      head_width = @envelopes["HeadMax"]["fullWidthH"]
      torso_width = @envelopes["ShoulderBody"]["fullWidthH"]
      if finite_number?(head_width) && finite_number?(torso_width)
        expect(head_width < torso_width, "HEAD_TORSO_WIDTH_RELATION", PROFILE_PATH)
      end
    end
  end

  def validate_invariants
    return if @landmarks.empty?
    invariants = hash(@profile["directionInvariants"])
    expected_fields = %w[
      headHeightH forearmTerminalBottomHeightH crotchHeightH
      forearmTerminalBottomAboveCrotchH forearmTerminalBottomAboveCrotchAllowedRangeH
      lowerLegTerminalBottomHeightH torsoDepthVariationMaximumH
      bilateralNeutralSymmetryRequired separateVisibleHandMesh
      visibleFingerOrFistShape separateVisibleFootOrShoeMesh visibleToeShape
      roundedForearmTerminalRequired roundedLowerLegTerminalRequired bellyOnlySilhouetteAllowed
    ]
    expect(invariants.keys.sort == expected_fields.sort, "INVARIANT_FIELD_SET", PROFILE_PATH)
    numeric_invariants = %w[
      headHeightH forearmTerminalBottomHeightH crotchHeightH
      forearmTerminalBottomAboveCrotchH lowerLegTerminalBottomHeightH torsoDepthVariationMaximumH
    ]
    numeric_invariants.each do |field|
      expect(finite_number?(invariants[field]) && invariants[field] >= 0.0,
        "INVARIANT_NUMERIC", field)
    end

    crown_y = landmark_y("Crown")
    chin_y = landmark_y("Chin")
    crotch_y = landmark_y("Crotch")
    if crown_y && chin_y
      expect(number_equal?(crown_y - chin_y, invariants["headHeightH"]), "HEAD_HEIGHT_DERIVATION", PROFILE_PATH)
      expect((invariants["headHeightH"].to_f - 0.20).abs <= 0.01, "HEAD_HEIGHT_DIRECTION", PROFILE_PATH)
    end
    if crotch_y
      expect(number_equal?(crotch_y, invariants["crotchHeightH"]), "CROTCH_HEIGHT_DERIVATION", PROFILE_PATH)
      bottom = invariants["forearmTerminalBottomHeightH"]
      delta = finite_number?(bottom) ? bottom - crotch_y : nil
      expect(delta && number_equal?(delta, invariants["forearmTerminalBottomAboveCrotchH"]),
        "FOREARM_TERMINAL_DELTA_DERIVATION", PROFILE_PATH)
      allowed = invariants["forearmTerminalBottomAboveCrotchAllowedRangeH"]
      valid_range = allowed.is_a?(Array) && allowed.length == 2 && allowed.all? { |value| finite_number?(value) }
      expect(valid_range, "FOREARM_TERMINAL_DELTA_RANGE", PROFILE_PATH)
      if valid_range && delta
        expect(delta >= allowed[0] - EPSILON && delta <= allowed[1] + EPSILON,
          "FOREARM_TERMINAL_DELTA_DIRECTION", PROFILE_PATH)
      end
    end
    lower_bottom = invariants["lowerLegTerminalBottomHeightH"]
    ground = hash(@profile["neutralBoundsH"])["groundMinimumY"]
    lower_center = landmark_y("LowerLegTerminal_L")
    expect(finite_number?(lower_bottom) && finite_number?(ground) &&
      number_equal?(lower_bottom, ground), "LOWER_TERMINAL_GROUND_DERIVATION", PROFILE_PATH)
    expect(finite_number?(lower_bottom) && lower_center && lower_bottom < lower_center,
      "LOWER_TERMINAL_CENTER_RELATION", PROFILE_PATH)

    heights = {
      "Crown" => landmark_y("Crown"), "Chin" => landmark_y("Chin"),
      "Shoulder" => landmark_y("Shoulder_L"), "Chest" => landmark_y("Chest"),
      "Elbow" => landmark_y("Elbow_L"), "ForearmTerminal" => landmark_y("ForearmTerminal_L"),
      "Pelvis" => landmark_y("Pelvis"), "Hip" => landmark_y("Hip_L"),
      "Crotch" => landmark_y("Crotch"), "Knee" => landmark_y("Knee_L"),
      "LowerLegTerminal" => landmark_y("LowerLegTerminal_L"),
    }
    if heights.values.all?
      ordered = heights["Crown"] > heights["Chin"] &&
        heights["Chin"] > heights["Shoulder"] &&
        heights["Shoulder"] > heights["Chest"] &&
        heights["Chest"] > heights["Elbow"] &&
        heights["Elbow"] > heights["ForearmTerminal"] &&
        heights["ForearmTerminal"] > heights["Pelvis"] &&
        heights["Pelvis"] > heights["Hip"] &&
        heights["Hip"] > heights["Crotch"] &&
        heights["Crotch"] > heights["Knee"] &&
        heights["Knee"] > heights["LowerLegTerminal"]
      expect(ordered, "LANDMARK_ANATOMICAL_ORDER", PROFILE_PATH)
    end

    false_fields = %w[
      separateVisibleHandMesh visibleFingerOrFistShape separateVisibleFootOrShoeMesh
      visibleToeShape bellyOnlySilhouetteAllowed
    ]
    true_fields = %w[
      bilateralNeutralSymmetryRequired roundedForearmTerminalRequired roundedLowerLegTerminalRequired
    ]
    false_fields.each { |field| expect(invariants[field] == false, "INVARIANT_FORBIDDEN_SHAPE", field) }
    true_fields.each { |field| expect(invariants[field] == true, "INVARIANT_REQUIRED_SHAPE", field) }

    targets = hash(@profile["startingVerificationTargets"])
    expect(targets["state"] == "START", "TARGET_STATE", PROFILE_PATH)
    expect(number_equal?(targets["profileToBlockoutSilhouetteLandmarkBoundsMaximumH"], 0.005),
      "TARGET_PROFILE_TOLERANCE", PROFILE_PATH)
    expect(number_equal?(targets["groundPivotMaximumH"], 0.005), "TARGET_PIVOT_TOLERANCE", PROFILE_PATH)
    expect(targets["orthographicViews"] == %w[Front Side Back ThreeQuarter], "TARGET_VIEWS", PROFILE_PATH)
    expect(targets["pixelPerfectComparisonRequired"] == false, "TARGET_PIXEL_PERFECT", PROFILE_PATH)
    expect(targets["humanApprovalRequired"] == true, "TARGET_HUMAN_APPROVAL", PROFILE_PATH)
  end

  def validate_deferred_and_execution
    deferred = hash(@profile["deferredDecisions"])
    expect(deferred.keys.sort == REQUIRED_DEFERRED_FIELDS.sort, "DEFERRED_EXACT_SET", PROFILE_PATH)
    REQUIRED_DEFERRED_FIELDS.each do |field|
      entry = hash(deferred[field])
      expect(entry.keys.sort == %w[approvalGate ownerTasks value], "DEFERRED_FIELD_SET", field)
      expect(entry.key?("value") && entry["value"].nil?, "DEFERRED_VALUE", field)
      owners = entry["ownerTasks"]
      expect(nonempty_string_array?(owners) && owners.uniq.length == owners.length,
        "DEFERRED_OWNER", field)
      expect(nonempty_string?(entry["approvalGate"]), "DEFERRED_GATE", field)
    end

    execution = hash(@profile["execution"])
    expected_execution = %w[
      generatedBlendCount generatedFbxCount generatedUnityAssetCount generatedCaptureCount
      colliderProfilesCreated gameplayReachProfilesCreated playerBuildsExecuted
    ]
    expect(execution.keys.sort == expected_execution.sort, "EXECUTION_FIELD_SET", PROFILE_PATH)
    expected_execution.each { |field| expect(execution[field] == 0, "EXECUTION_NOT_ZERO", field) }
  end

  def validate_measurement_digest
    bundle = MEASUREMENT_DIGEST_FIELDS.each_with_object({}) do |field, result|
      result[field] = @profile[field]
    end
    actual = Digest::SHA256.hexdigest(JSON.generate(canonicalize(bundle)))
    expect(actual == EXPECTED_MEASUREMENT_SET_SHA256, "MEASUREMENT_SET_DIGEST", PROFILE_PATH)
  end

  def validate_no_locked_claims
    walk(@profile) do |value|
      add("UNAPPROVED_LOCKED_CLAIM", PROFILE_PATH) if value == "LOCKED"
    end
  end

  def validate_scope
    changed, changed_error, changed_status = Open3.capture3(
      "git", "-C", @root.to_s, "diff", "--name-only", "-z", "HEAD", "--"
    )
    untracked, untracked_error, untracked_status = Open3.capture3(
      "git", "-C", @root.to_s, "ls-files", "--others", "--exclude-standard", "-z"
    )
    unless changed_status.success? && untracked_status.success? && changed_error.empty? && untracked_error.empty?
      add("C1B002_GIT_SCOPE_UNAVAILABLE", ".")
      return
    end
    paths = (decode_git_paths(changed) + decode_git_paths(untracked)).uniq.sort
    @scope_paths_checked = paths.length
    paths.each do |path|
      add("C1B002_ASSET_OUTPUT_PRESENT", path) if FORBIDDEN_ASSET_EXTENSIONS.include?(File.extname(path).downcase)
      allowed = C1B002_ALLOWED_CHANGE_PATHS.include?(path) ||
        C1B002_ALLOWED_CHANGE_PREFIXES.any? { |prefix| path.start_with?(prefix) }
      add("C1B002_SCOPE_PATH", path) unless allowed
    end
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

  def safe_regular_file(relative, kind, max_bytes)
    unless repository_relative_path?(relative)
      add("#{kind}_PATH_INVALID", relative.to_s)
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

  def repository_relative_path?(relative)
    return false unless relative.is_a?(String) && !relative.empty?
    return false if relative.start_with?("/", "\\") || relative.include?("\0")
    return false if relative.match?(/\A[A-Za-z]:[\\\/]/)
    path = Pathname.new(relative)
    path.cleanpath.to_s == relative && !path.each_filename.include?("..")
  end

  def decode_git_paths(bytes)
    bytes.b.split("\0").reject(&:empty?).map do |raw|
      path = raw.dup.force_encoding(Encoding::UTF_8)
      unless path.valid_encoding? && repository_relative_path?(path.tr("\\", "/"))
        add("C1B002_GIT_PATH_INVALID", "<invalid>")
        next
      end
      path.tr("\\", "/")
    end.compact
  end

  def landmark_y(id)
    entry = @landmarks[id]
    value = entry && entry["positionH"] && entry["positionH"]["y"]
    finite_number?(value) ? value.to_f : nil
  end

  def hash(value)
    value.is_a?(Hash) ? value : {}
  end

  def finite_number?(value)
    value.is_a?(Numeric) && value.finite?
  end

  def canonicalize(value)
    case value
    when Hash
      value.keys.sort.each_with_object({}) { |key, result| result[key] = canonicalize(value[key]) }
    when Array
      value.map { |entry| canonicalize(entry) }
    else
      value
    end
  end

  def walk(value, &block)
    yield value
    case value
    when Hash
      value.each_value { |entry| walk(entry, &block) }
    when Array
      value.each { |entry| walk(entry, &block) }
    end
  end

  def number_equal?(actual, expected)
    finite_number?(actual) && (actual.to_f - expected.to_f).abs <= EPSILON
  end

  def nonempty_string?(value)
    value.is_a?(String) && !value.strip.empty?
  end

  def nonempty_string_array?(value)
    value.is_a?(Array) && !value.empty? && value.all? { |entry| nonempty_string?(entry) }
  end

  def expect(condition, rule, path)
    add(rule, path) unless condition
  end

  def add(rule, path)
    key = [rule, path]
    return unless @violation_keys.add?(key)
    @violations << key
  end

  def print_report
    approval = hash(@profile && @profile["approval"])
    source = hash(@profile && @profile["directionSource"])
    execution = hash(@profile && @profile["execution"])
    puts "CHARACTER_PROPORTION_PROFILE_AUDIT=C1B-002"
    puts "PROFILE_LOADED=#{@profile.is_a?(Hash)}"
    puts "PROFILE_STATE=#{@profile && @profile["state"]}"
    puts "CANDIDATE_STATUS=#{@profile && @profile["candidateStatus"]}"
    puts "LANDMARK_COUNT=#{@landmarks.length}"
    puts "ENVELOPE_COUNT=#{@envelopes.length}"
    puts "SOURCE_HASH_MATCH=#{@source_hash_match}"
    puts "PIXEL_MEASUREMENT_USED=#{source["pixelMeasurementUsed"] == true}"
    puts "USER_APPROVAL_RECORDED=#{approval["userApprovalRecorded"] == true}"
    puts "LOCKED_VALUE_COUNT=#{approval["lockedValueCount"] || 0}"
    generated = execution.values.select { |value| value.is_a?(Numeric) }.sum
    puts "GENERATED_OR_EXECUTED_COUNT=#{generated}"
    puts "C1B002_SCOPE_CHECKED=#{@check_scope}"
    puts "C1B002_SCOPE_PATHS=#{@scope_paths_checked}"
    puts "TOTAL_VIOLATIONS=#{@violations.length}"
    @violations.sort.each { |rule, path| puts "VIOLATION rule=#{rule} path=#{path}" }
    puts "FINAL_RESULT=#{@violations.empty? ? "PASS" : "FAIL"}"
  end
end

options = {check_scope: false}
parser = OptionParser.new do |arguments|
  arguments.banner = "usage: ruby tools/verify_character_proportion_profile.rb [--root PATH] [--check-c1b002-scope]"
  arguments.on("--root PATH", "Repository root") { |path| options[:root] = path }
  arguments.on("--check-c1b002-scope", "Reject non-C1B-002 changes and generated assets") do
    options[:check_scope] = true
  end
end

begin
  parser.parse!
  unless ARGV.empty?
    warn "CHARACTER_PROPORTION_PROFILE_AUDIT=ERROR reason=USAGE"
    exit 2
  end
  root = Pathname.new(options.fetch(:root, Pathname.new(__dir__).join("..").expand_path.to_s)).expand_path
  unless root.directory?
    warn "CHARACTER_PROPORTION_PROFILE_AUDIT=ERROR reason=INVALID_ROOT"
    exit 2
  end
  exit CharacterProportionProfileVerifier.new(root, check_scope: options[:check_scope]).run
rescue Errno::ENOENT, Errno::EACCES => error
  warn "CHARACTER_PROPORTION_PROFILE_AUDIT=ERROR reason=#{error.class}"
  exit 2
end
