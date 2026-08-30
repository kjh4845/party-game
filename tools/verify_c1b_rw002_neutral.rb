#!/usr/bin/env ruby

require "digest"
require "json"
require "open3"
require "optparse"
require "pathname"
require "set"
require "yaml"

class C1BRW002NeutralVerifier
  PROFILE = "config/character/CharacterProportionProfile-C1B-RW-001-r01.yaml"
  MANIFEST = "BlenderSource/Characters/C1B-RW-002/GenerationManifest.yaml"
  REPORT = "BlenderSource/Characters/C1B-RW-002/MeasurementReport.yaml"
  SOURCE = "BlenderSource/Characters/C1B-RW-002/CHR_MasterCharacter_C1B_NeutralRework_r01.blend"
  RENDER_ROOT = "BlenderSource/Characters/C1B-RW-002/Renders"
  GENERATOR = "tools/blender/create_c1b_rw002_neutral.py"
  INSPECTOR = "tools/blender/inspect_c1b_rw002_neutral.py"
  DIRECTION_SOURCE = "artifacts/review/character/C1_CHARACTER_HYBRID_CORE_v0.13_BELLY_CORRECTED_REVIEW.png"
  DIRECTION_SHA = "c1def169cefd59f19339a5b5edbac2dfd0c8fe9a05eba9ee0afb1ae598bab616"
  DEFAULT_BLENDER = "/Applications/Blender.app/Contents/MacOS/Blender"
  PROFILE_SHA = "6c55a9d522096256fd15652f97c0a7a772285f427c53edb87f0c4d90d024b883"
  SOURCE_SHA = "35f21abe5b6bcd35dc2b066aa3bd29cea5fbf8f9e8bd600b50ffa3f5daedb938"
  REPORT_SHA = "9f67ae7c6d3345e0f8f7a315dbb1302933f41cff690895b32b3345e2e1d0806b"
  GENERATOR_SHA = "0901d27d4a5183ca43d61f8285f97f6f171e7ac6770f6b24aa519c40689ff6db"
  INSPECTOR_SHA = "86faae7cadfe57e4e79acc9dcd3a1664a784c460036d0ab108a908670ba4cc6b"
  RENDER_BUNDLE_SHA = "622b68e9672956e000ccbccddf8c6c069e1a4f122f6c4659a2f94cc499ca60e7"
  POSITION_SHA = "15381bb936b84d07d2572da677c412bed104a1df71e6ced8cf7ff9dbf670e592"
  TOPOLOGY_SHA = "c29dfe56bbb5eb5d8ed338a542b69880bc120c34c7df5a079aaf0dc27ebf2fd5"
  GRAPH_SHA = "cf79a988cdc4341da7c9298dd8fa9e08e6fe787e5f9d01f7bf0dc2c05065d3c9"
  SECTION_DIGESTS = {
    "scene" => "6b56ff363f175c9cb1c976703b3e9e55b85685f0f092bf3514b43465175bb4ba",
    "counts" => "34c480febd6b9946aa2979581af6a75b4a3105bdd664ee5d8249d8f9071bf5fd",
    "mesh" => "9cee160b20cc96fa4aab7c3c41b506ec8ccd28862ba4cef48b90be4c3758dd3c",
    "bounds" => "b2ea4cd42807c7e040a5bf2f1023309b0e113b8a5595b5949e92274e94ba7399",
    "symmetry" => "d8d653b517b64dc0183aec45f7d16c8730eb92f652fafe667ad88710716a3224",
    "cameras" => "cd0890a3bfee49f9543fc42012755f102f0265bd766c719587bbdd081c5f3d23",
    "renderSettings" => "99ac69570350b617f1ec49abbfa5b511340f03cf0a764d52396602295a797c44",
  }.freeze
  VIEWS = %w[Back Front Side ThreeQuarter].freeze
  STYLES = %w[Neutral Silhouette].freeze
  RENDER_PATHS = STYLES.product(VIEWS).map do |style, view|
    "#{RENDER_ROOT}/CHR_MasterCharacter_C1B_NeutralRework_r01_#{style}_#{view}.png"
  end.sort.freeze
  MAX_YAML = 512 * 1024
  MAX_BINARY = 64 * 1024 * 1024

  def initialize(root, verify_blender: false, blender: nil)
    @root = Pathname.new(root).expand_path
    @verify_blender = verify_blender
    @blender = blender || DEFAULT_BLENDER
    @violations = []
    @seen = Set.new
    @profile = {}
    @manifest = {}
    @report = {}
    @render_hashes = 0
    @render_dimensions = 0
    @blender_verified = false
  end

  def run
    profile_doc = load_yaml(PROFILE, "PROFILE")
    manifest_doc = load_yaml(MANIFEST, "MANIFEST")
    report_doc = load_yaml(REPORT, "REPORT")
    @profile = h(profile_doc && profile_doc["CharacterProportionProfile"])
    @manifest = h(manifest_doc && manifest_doc["GenerationManifest"])
    @report = h(report_doc && report_doc["C1BRW002MeasurementReport"])
    expect(profile_doc.is_a?(Hash) && profile_doc.keys == ["CharacterProportionProfile"], "PROFILE_ROOT", PROFILE)
    expect(manifest_doc.is_a?(Hash) && manifest_doc.keys == ["GenerationManifest"], "MANIFEST_ROOT", MANIFEST)
    expect(report_doc.is_a?(Hash) && report_doc.keys == ["C1BRW002MeasurementReport"], "REPORT_ROOT", REPORT)
    validate_profile
    validate_manifest
    validate_report
    validate_files
    validate_blender if @verify_blender
    print_report
    @violations.empty? ? 0 : 1
  rescue StandardError => error
    add("VERIFIER_INTERNAL_ERROR", error.class.name)
    print_report
    1
  end

  private

  def validate_profile
    expected_top = %w[schemaVersion profileId version revision state candidateStatus ownerTask sourceOwner directionSource supersessionBoundary normalization visualIntent neutralMeshContract reviewConstructionBoundary deferredProductionDecisions neutralVisualGate execution]
    expect(@profile.keys.sort == expected_top.sort, "PROFILE_FIELD_SET", PROFILE)
    expect(@profile["schemaVersion"] == 1 && @profile["profileId"] == "CharacterProportionProfile-C1BRW-001-r01" && @profile["version"] == "0.1.0-start" && @profile["revision"] == "r01" && @profile["state"] == "START" && @profile["candidateStatus"] == "USER_REVIEW" && @profile["ownerTask"] == "C1BRW-001" && @profile["sourceOwner"] == "kjh4845", "PROFILE_METADATA", PROFILE)
    direction = h(@profile["directionSource"])
    expect(direction["sha256"] == "c1def169cefd59f19339a5b5edbac2dfd0c8fe9a05eba9ee0afb1ae598bab616" && direction["role"] == "QUALITATIVE_DIRECTION_ONLY" && direction["pixelMeasurementUsed"] == false && direction["referenceReplicaAllowed"] == false, "PROFILE_DIRECTION", PROFILE)
    supersession = h(@profile["supersessionBoundary"])
    expect(supersession["priorTasks"] == %w[C1B-002 C1B-003 C1B-004 C1B-005] && supersession["priorArtifactsStatus"] == "SUPERSEDED_EXCLUDED_FROM_REWORK_INPUT" && supersession["rewritePriorArtifactsAllowed"] == false && supersession["inheritOldSixPartGeometryAllowed"] == false && supersession["inheritOldPoseCapGeometryAllowed"] == false, "PROFILE_SUPERSESSION", PROFILE)
    bounds = h(h(@profile["normalization"])["boundsH"])
    expect(bounds == {"height"=>1.0,"fullWidth"=>0.464322566986084,"totalDepth"=>0.2069854587316513,"groundMinimum"=>0.0,"crownMaximum"=>1.0}, "PROFILE_BOUNDS", PROFILE)
    mesh = h(@profile["neutralMeshContract"])
    expect(mesh["renderMeshObjects"] == 1 && mesh["renderMeshDatablocks"] == 1 && mesh["connectedComponents"] == 1 && mesh["vertices"] == 1882 && mesh["edges"] == 3760 && mesh["polygons"] == 1880 && %w[boundaryEdges nonManifoldEdges looseEdges degenerateFaces nonFiniteVertices].all? { |key| mesh[key] == 0 } && mesh["xSymmetryRequired"] == true && mesh["oldSixPartOrCapMeshesAllowed"] == false && mesh["separateVisibleHandFingerFistMeshesAllowed"] == false && mesh["separateVisibleFootShoeToeMeshesAllowed"] == false, "PROFILE_MESH_CONTRACT", PROFILE)
    construction = h(@profile["reviewConstructionBoundary"])
    expect(construction["graphContractSha256"] == GRAPH_SHA && construction["graphNodes"] == 31 && construction["graphEdges"] == 30 && construction["activeConstructionModifiersAllowed"] == false && construction["productionTopologyApproved"] == false && construction["skinnedCharacterApproved"] == false, "PROFILE_CONSTRUCTION", PROFILE)
    gate = h(@profile["neutralVisualGate"])
    expect(gate == {"gateId"=>"UG-C1B-NEUTRAL","state"=>"PENDING_USER_REVIEW","userVisualApprovalRecorded"=>false,"requiredBeforePoseWork"=>true,"requiredBeforeFbxExport"=>true,"poseGenerationAllowed"=>false,"fbxExportAllowed"=>false,"unityImportAllowed"=>false}, "PROFILE_VISUAL_GATE", PROFILE)
    execution = h(@profile["execution"])
    expect(execution.values.all? { |value| value == 0 }, "PROFILE_EXECUTION", PROFILE)
    walk(@profile) { |value| add("PROFILE_ILLEGAL_APPROVAL", PROFILE) if value == "LOCKED" || value == "APPROVED" }
  end

  def validate_manifest
    expect(@manifest["state"] == "START" && @manifest["candidateStatus"] == "USER_REVIEW" && @manifest["ownerTask"] == "C1BRW-002" && @manifest["sourceOwner"] == "kjh4845", "MANIFEST_METADATA", MANIFEST)
    identity = h(@manifest["identity"])
    expect(identity["profileSha256"] == PROFILE_SHA && identity["sourceSha256"] == SOURCE_SHA && identity["fbxSha256"].nil? && identity["unityPrefabRevision"].nil?, "MANIFEST_IDENTITY", MANIFEST)
    supersession = h(@manifest["supersessionBoundary"])
    expect(supersession == {"priorTasks"=>%w[C1B-002 C1B-003 C1B-004 C1B-005],"priorArtifactsStatus"=>"SUPERSEDED_EXCLUDED_FROM_REWORK_INPUT","priorArtifactsRewritten"=>false,"inheritedOldGeometryCount"=>0}, "MANIFEST_SUPERSESSION", MANIFEST)
    stages = h(@manifest["stages"])
    expect(stages.keys.sort == %w[profile blend-source reference-render pose-generation fbx-export unity-import].sort, "MANIFEST_STAGE_SET", MANIFEST)
    blend = h(stages["blend-source"])
    expect(blend["status"] == "COMPLETE_LOCAL" && blend["path"] == SOURCE && blend["bytes"] == 157613 && blend["sha256"] == SOURCE_SHA && blend["lfsState"] == "PENDING_CORE_PUSH" && blend["indexPointerVerified"] == true && blend["remoteObjectRoundTripVerified"] == false, "MANIFEST_BLEND_STAGE", MANIFEST)
    render = h(stages["reference-render"])
    expect(render["outputCount"] == 8 && render["orderedBundleSha256"] == RENDER_BUNDLE_SHA && render["dimensions"] == [2048,2048] && Array(render["outputs"]).map { |entry| entry["path"] }.sort == RENDER_PATHS, "MANIFEST_RENDER_STAGE", MANIFEST)
    expect(h(stages["pose-generation"]) == {"status"=>"BLOCKED_PENDING_NEUTRAL_VISUAL_GATE","outputs"=>0}, "MANIFEST_POSE_BLOCK", MANIFEST)
    expect(h(stages["fbx-export"]) == {"status"=>"BLOCKED_PENDING_NEUTRAL_VISUAL_GATE","path"=>nil,"bytes"=>nil,"sha256"=>nil,"executed"=>false}, "MANIFEST_FBX_BLOCK", MANIFEST)
    expect(h(stages["unity-import"]) == {"status"=>"BLOCKED_PENDING_NEUTRAL_VISUAL_GATE","path"=>nil,"revision"=>nil,"executed"=>false}, "MANIFEST_UNITY_BLOCK", MANIFEST)
    evidence = h(@manifest["measurementEvidence"])
    expect(evidence == {"reportPath"=>REPORT,"reportSha256"=>REPORT_SHA,"result"=>"PASS_INTERNAL_STRUCTURE"}, "MANIFEST_REPORT_REFERENCE", MANIFEST)
    tools = h(@manifest["generationTools"])
    expect(tools == {"generator"=>{"path"=>GENERATOR,"sha256"=>GENERATOR_SHA},"inspector"=>{"path"=>INSPECTOR,"sha256"=>INSPECTOR_SHA}}, "MANIFEST_TOOLS", MANIFEST)
    boundary = h(@manifest["sourceBoundary"])
    expect(boundary.values_at("oldSixPartOrPoseCapMeshes","separateVisibleHandFingerFistMeshes","separateVisibleFootShoeToeMeshes","armatures","actions","colliders","uvLayers","weightedVertexAssignments","lodObjects","externalOrPackedInputs") == [0]*10 && boundary["productionTopologyApproved"] == false && boundary["userVisualApprovalRecorded"] == false, "MANIFEST_BOUNDARY", MANIFEST)
    gate = h(@manifest["neutralVisualGate"])
    expect(gate == {"gateId"=>"UG-C1B-NEUTRAL","state"=>"PENDING_USER_REVIEW","poseGenerationAllowed"=>false,"fbxExportAllowed"=>false,"unityImportAllowed"=>false}, "MANIFEST_GATE", MANIFEST)
    execution = h(@manifest["execution"])
    expect(execution == {"blendSources"=>1,"referenceRenders"=>8,"poseOutputs"=>0,"fbxExports"=>0,"unityImports"=>0,"armatures"=>0,"actions"=>0,"colliders"=>0,"playerBuilds"=>0,"dockerExecutions"=>0,"deployExecutions"=>0}, "MANIFEST_EXECUTION", MANIFEST)
  end

  def validate_report
    expect(file_sha(REPORT) == REPORT_SHA, "REPORT_CANONICAL_SHA", REPORT)
    expect(@report["state"] == "START" && @report["candidateStatus"] == "USER_REVIEW" && @report["ownerTask"] == "C1BRW-002" && @report["sourceOwner"] == "kjh4845" && @report["result"] == "PASS_INTERNAL_STRUCTURE" && @report["sourceSha256"] == SOURCE_SHA && @report["profileId"] == "CharacterProportionProfile-C1BRW-001-r01" && @report["profileSha256"] == PROFILE_SHA, "REPORT_METADATA", REPORT)
    geometry = h(@report["geometry"])
    expect(geometry["renderMeshObjects"] == 1 && geometry["renderMeshDatablocks"] == 1 && geometry["connectedComponents"] == 1 && geometry["vertices"] == 1882 && geometry["edges"] == 3760 && geometry["polygons"] == 1880 && geometry["positionSha256"] == POSITION_SHA && geometry["orientedTopologySha256"] == TOPOLOGY_SHA && geometry["result"] == "PASS", "REPORT_GEOMETRY", REPORT)
    expect(h(@report["boundsH"])["size"] == [0.464322566986084,0.2069854587316513,1.0], "REPORT_BOUNDS", REPORT)
    symmetry = h(@report["symmetry"])
    expect(symmetry["maximumPositionDeviationH"].to_f <= 0.000001 && symmetry["missingMirroredVertices"] == 0 && symmetry["missingMirroredEdges"] == 0 && symmetry["missingMirroredPolygons"] == 0 && symmetry["result"] == "PASS", "REPORT_SYMMETRY", REPORT)
    prohibited = h(@report["prohibitedScope"])
    expect(prohibited.values_at("armatures","actions","colliders","uvLayers","vertexGroups","weightedVertexAssignments","lodObjects","externalImages","packedImages","externalLibraries","embeddedTextBlocks") == [0]*11 && prohibited["directionReferenceEmbeddedOrPacked"] == false, "REPORT_PROHIBITED_SCOPE", REPORT)
    expect(h(@report["renders"])["expected"] == 8 && h(@report["renders"])["orderedBundleSha256"] == RENDER_BUNDLE_SHA && h(@report["renders"])["result"] == "PASS", "REPORT_RENDERS", REPORT)
    gate = h(@report["neutralVisualGate"])
    expect(gate["state"] == "PENDING_USER_REVIEW" && gate["userVisualApprovalRecorded"] == false && gate["poseGenerationAllowed"] == false && gate["fbxExportAllowed"] == false && gate["unityImportAllowed"] == false && gate["result"] == "BLOCKS_DOWNSTREAM_BY_DESIGN", "REPORT_GATE", REPORT)
    execution = h(@report["execution"])
    expect(execution["poseOutputsCreated"] == 0 && execution["fbxExportsCreated"] == 0 && execution["unityAssetsCreated"] == 0 && execution["armaturesCreated"] == 0 && execution["actionsCreated"] == 0 && execution["collidersCreated"] == 0 && execution["playerBuildsExecuted"] == 0, "REPORT_EXECUTION", REPORT)
  end

  def validate_files
    verify_binary(SOURCE, 157613, SOURCE_SHA, "SOURCE")
    verify_digest(PROFILE, PROFILE_SHA, "PROFILE")
    verify_digest(REPORT, REPORT_SHA, "REPORT")
    verify_digest(GENERATOR, GENERATOR_SHA, "GENERATOR")
    verify_digest(INSPECTOR, INSPECTOR_SHA, "INSPECTOR")
    verify_digest(DIRECTION_SOURCE, DIRECTION_SHA, "DIRECTION_SOURCE")
    outputs = Array(h(h(@manifest["stages"])["reference-render"])["outputs"])
    lines = []
    outputs.sort_by { |entry| entry["path"].to_s }.each do |entry|
      before = @violations.length
      verify_binary(entry["path"], entry["bytes"], entry["sha256"], "RENDER")
      @render_hashes += 1 if before == @violations.length
      if png_dimensions(entry["path"]) == [2048,2048]
        @render_dimensions += 1
      else
        add("RENDER_DIMENSIONS", entry["path"])
      end
      lines << "#{entry["path"]}=#{entry["sha256"]}\n"
    end
    expect(Digest::SHA256.hexdigest(lines.join) == RENDER_BUNDLE_SHA, "RENDER_BUNDLE_SHA", MANIFEST)
    actual = Dir[@root.join("#{RENDER_ROOT}/*.png")].map { |path| Pathname.new(path).relative_path_from(@root).to_s }.sort
    expect(actual == RENDER_PATHS, "RENDER_FILE_SET", RENDER_ROOT)
    allowed = ([SOURCE,MANIFEST,REPORT] + RENDER_PATHS).sort
    actual_rw = Dir[@root.join("BlenderSource/Characters/C1B-RW-002/**/*")].select { |path| File.file?(path) || File.symlink?(path) }.map { |path| Pathname.new(path).relative_path_from(@root).to_s }.sort
    expect(actual_rw == allowed, "RW002_FILE_SET", "BlenderSource/Characters/C1B-RW-002")
  end

  def validate_blender
    unless File.executable?(@blender); add("BLENDER_UNAVAILABLE", @blender); return; end
    out, err, status = Open3.capture3(@blender,"--background",@root.join(SOURCE).to_s,"--python",@root.join(INSPECTOR).to_s)
    line = out.lines.find { |value| value.start_with?("C1BRW002_INSPECTION_JSON=") }
    payload = line && JSON.parse(line.split("=",2)[1])
    unless status.success? && payload.is_a?(Hash)
      add("BLENDER_INSPECTION_RUN", err.lines.last.to_s); return
    end
    SECTION_DIGESTS.each { |key,sha| expect(canonical_sha(payload[key]) == sha, "BLENDER_#{key.upcase}_DIGEST", SOURCE) }
    expect(payload["graphContractSha256"] == GRAPH_SHA && payload["graphNodeCount"] == 31 && payload["graphEdgeCount"] == 30, "BLENDER_GRAPH", SOURCE)
    expect(payload["errors"] == [] && payload["externalImages"] == [] && payload["packedImages"] == [] && payload["forbiddenModelNames"] == [] && payload["lodObjects"] == [], "BLENDER_FORBIDDEN_SCOPE", SOURCE)
    mesh = h(payload["mesh"])
    expect(mesh["positionSha256"] == POSITION_SHA && mesh["orientedTopologySha256"] == TOPOLOGY_SHA && mesh["connectedComponents"] == 1 && mesh["boundaryEdges"] == 0 && mesh["nonManifoldEdges"] == 0 && mesh["looseEdges"] == 0 && mesh["degenerateFaces"] == 0 && mesh["uvLayers"] == 0 && mesh["weightedVertexAssignments"] == 0, "BLENDER_MESH", SOURCE)
    symmetry = h(payload["symmetry"])
    expect(symmetry["maximumPositionDeviationH"].to_f <= 0.000001 && symmetry["missingMirroredVertices"] == 0 && symmetry["missingMirroredEdges"] == 0 && symmetry["missingMirroredPolygons"] == 0, "BLENDER_SYMMETRY", SOURCE)
    @blender_verified = @violations.none? { |rule,_| rule.start_with?("BLENDER_") }
  rescue JSON::ParserError
    add("BLENDER_INSPECTION_JSON", SOURCE)
  end

  def canonical_sha(value)
    Digest::SHA256.hexdigest(JSON.generate(deep_sort(value)))
  end

  def deep_sort(value)
    case value
    when Hash then value.keys.sort.to_h { |key| [key, deep_sort(value[key])] }
    when Array then value.map { |entry| deep_sort(entry) }
    else value
    end
  end

  def load_yaml(relative,label)
    path=@root.join(relative); st=path.lstat rescue nil
    unless st; add("#{label}_MISSING",relative); return nil; end
    if st.symlink?; add("#{label}_SYMLINK",relative); return nil; end
    if st.size>MAX_YAML; add("#{label}_TOO_LARGE",relative); return nil; end
    text=path.binread; detect_duplicates(text,label,relative)
    YAML.safe_load(text,permitted_classes:[],permitted_symbols:[],aliases:false)
  rescue Psych::Exception=>error
    add("#{label}_YAML_INVALID","#{relative}:#{error.class}"); nil
  end

  def detect_duplicates(text,label,relative)
    visit=lambda do |node|
      if node.is_a?(Psych::Nodes::Mapping)
        keys=Set.new
        node.children.each_slice(2){|key,value| scalar=key.respond_to?(:value) ? key.value : nil; add("#{label}_YAML_DUPLICATE_KEY","#{relative}:#{scalar}") if scalar && !keys.add?(scalar); visit.call(value)}
      elsif node.respond_to?(:children) && node.children
        node.children.each{|child|visit.call(child)}
      end
    end
    visit.call(Psych.parse_stream(text))
  rescue Psych::Exception
    nil
  end

  def verify_binary(relative,bytes,sha,label)
    path=@root.join(relative); st=path.lstat rescue nil
    unless st; add("#{label}_MISSING",relative); return; end
    if st.symlink?; add("#{label}_SYMLINK",relative); return; end
    expect(st.file? && st.size<=MAX_BINARY,"#{label}_TYPE",relative)
    expect(st.size==bytes,"#{label}_SIZE",relative)
    expect(Digest::SHA256.file(path).hexdigest==sha,"#{label}_SHA",relative)
  end

  def verify_digest(relative,sha,label)
    path=@root.join(relative); st=path.lstat rescue nil
    unless st; add("#{label}_MISSING",relative); return; end
    if st.symlink?; add("#{label}_SYMLINK",relative); return; end
    expect(Digest::SHA256.file(path).hexdigest==sha,"#{label}_SHA",relative)
  end

  def png_dimensions(relative)
    data=@root.join(relative).binread(24)
    return nil unless data.byteslice(0,8)=="\x89PNG\r\n\x1a\n".b && data.byteslice(12,4)=="IHDR"
    data.byteslice(16,8).unpack("NN")
  rescue StandardError
    nil
  end

  def file_sha(relative); path=@root.join(relative); path.file? ? Digest::SHA256.file(path).hexdigest : nil; end
  def h(value); value.is_a?(Hash) ? value : {}; end
  def walk(value,&block); yield value; value.each_value{|entry|walk(entry,&block)} if value.is_a?(Hash); value.each{|entry|walk(entry,&block)} if value.is_a?(Array); end
  def expect(condition,rule,path); add(rule,path) unless condition; end
  def add(rule,path); key=[rule,path.to_s]; return unless @seen.add?(key); @violations<<key; end

  def print_report
    puts "C1B_RW002_NEUTRAL_AUDIT"
    puts "PROFILE_STATE=#{@profile["state"]}"
    puts "CANDIDATE_STATUS=#{@profile["candidateStatus"]}"
    puts "SOURCE_HASH_MATCH=#{file_sha(SOURCE)==SOURCE_SHA}"
    puts "RENDER_COUNT=#{RENDER_PATHS.length}"
    puts "RENDER_HASH_MATCHES=#{@render_hashes}"
    puts "RENDER_PNG_2048_MATCHES=#{@render_dimensions}"
    puts "BLENDER_VERIFIED=#{@blender_verified}" if @verify_blender
    puts "TOTAL_VIOLATIONS=#{@violations.length}"
    @violations.sort.each{|rule,path|puts "VIOLATION rule=#{rule} path=#{path}"}
    puts "FINAL_RESULT=#{@violations.empty? ? "PASS" : "FAIL"}"
  end
end

options={verify_blender:false}
OptionParser.new do |parser|
  parser.on("--root PATH"){|value|options[:root]=value}
  parser.on("--verify-blender"){options[:verify_blender]=true}
  parser.on("--blender PATH"){|value|options[:blender]=value}
end.parse!
root=Pathname.new(options.fetch(:root,Pathname.new(__dir__).join("..").expand_path.to_s)).expand_path
exit C1BRW002NeutralVerifier.new(root,verify_blender:options[:verify_blender],blender:options[:blender]).run
