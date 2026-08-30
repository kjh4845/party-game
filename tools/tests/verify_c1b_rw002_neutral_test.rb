#!/usr/bin/env ruby
require "fileutils";require "minitest/autorun";require "open3";require "pathname";require "rbconfig";require "tmpdir";require "yaml"
class VerifyC1BRW002NeutralTest < Minitest::Test
 ROOT=Pathname(__dir__).join("../..").expand_path; V=ROOT.join("tools/verify_c1b_rw002_neutral.rb"); P="config/character/CharacterProportionProfile-C1B-RW-001-r02.yaml";M="BlenderSource/Characters/C1B-RW-002-r02/GenerationManifest.yaml";R="BlenderSource/Characters/C1B-RW-002-r02/MeasurementReport.yaml";S="BlenderSource/Characters/C1B-RW-002-r02/CHR_MasterCharacter_C1B_NeutralRework_r02.blend"
 SUPPORT=[P,M,R,S,*Dir[ROOT.join("BlenderSource/Characters/C1B-RW-002-r02/Renders/*.png")].map{|x|Pathname(x).relative_path_from(ROOT).to_s},"tools/blender/create_c1b_rw002_neutral.py","tools/blender/inspect_c1b_rw002_neutral.py"].freeze
 def test_exact;out,_,s=execute(ROOT);assert_equal 0,s,out;assert_includes out,"FINAL_RESULT=PASS"end
 def test_blender;skip unless File.executable?("/Applications/Blender.app/Contents/MacOS/Blender");out,_,s=execute(ROOT,true);assert_equal 0,s,out;assert_includes out,"BLENDER_VERIFIED=true"end
 def test_malformed_duplicate_and_source_drift
  repo{|r|File.write(r.join(P),"bad: [");rule(r,"PROFILE_YAML_INVALID")};repo{|r|File.open(r.join(M),"a"){|f|f.write("\nGenerationManifest: {}\n")};rule(r,"MANIFEST_YAML_DUPLICATE_KEY")};repo{|r|File.open(r.join(S),"ab"){|f|f.write("x")};rule(r,"SOURCE_SIZE")}
 end
 def test_crease_mutation;repo{|r|mut(r,R,"C1BRW002MeasurementReport"){|x|x["geometryGates"]["shoulderContinuity"]["observedP95Degrees"]=80};rule(r,"REPORT_SHOULDER")}end
 def test_square_head_mutation;repo{|r|mut(r,R,"C1BRW002MeasurementReport"){|x|x["geometryGates"]["headRoundness"]["observedCoefficient"]=0.5};rule(r,"REPORT_HEAD")}end
 def test_neck_and_gap_mutation;repo{|r|mut(r,R,"C1BRW002MeasurementReport"){|x|x["construction"]["neckSemanticElementCount"]=1;x["geometryGates"]["directAttachment"]["observedOverlapH"]=[0,0,0]};o,_,s=execute(r);assert_equal 1,s;assert_includes o,"REPORT_CONSTRUCTION";assert_includes o,"REPORT_ATTACHMENT"}end
 def test_aabb_overlap_without_surface_intersection_fails;repo{|r|mut(r,R,"C1BRW002MeasurementReport"){|x|x["geometryGates"]["directAttachment"]["observedTriangleIntersectionPairs"]=0};rule(r,"REPORT_ATTACHMENT")}end
 def test_topology_mapping_substitution_rejected;repo{|r|mut(r,P,"CharacterProportionProfile"){|x|x["geometryValidation"]["symmetry"]["method"]="EXACT_VERTEX_MAPPING"};rule(r,"PROFILE_SYMMETRY")}end
 def test_fake_approval_and_downstream_execution;repo{|r|mut(r,P,"CharacterProportionProfile"){|x|x["state"]="LOCKED";x["neutralVisualGate"]["poseGenerationAllowed"]=true};o,_,s=execute(r);assert_equal 1,s;assert_includes o,"PROFILE_METADATA";assert_includes o,"PROFILE_GATE";assert_includes o,"ILLEGAL_APPROVAL"}end
 def test_uv_rig_old_geometry_and_render_drift;repo{|r|mut(r,M,"GenerationManifest"){|x|x["sourceBoundary"]["uvLayers"]=1;x["sourceBoundary"]["armatures"]=1;x["sourceBoundary"]["oldGraphCapPegOrPartGeometry"]=1};rule(r,"MANIFEST_BOUNDARY")};repo{|r|p=Dir[r.join("BlenderSource/Characters/C1B-RW-002-r02/Renders/*.png")].first;File.open(p,"ab"){|f|f.write("x")};rule(r,"RENDER_SIZE")}end
 def test_bundle_digest_and_blocked_stages_are_exact
  repo{|r|mut(r,M,"GenerationManifest"){|x|x["stages"]["reference-render"]["orderedBundleSha256"]="0"*64};rule(r,"RENDER_BUNDLE_SHA")}
  repo{|r|mut(r,M,"GenerationManifest"){|x|x["stages"]["pose-generation"]["status"]="COMPLETE";x["stages"]["fbx-export"]["executed"]=true;x["stages"]["unity-import"]["executed"]=true;x["neutralVisualGate"]["poseGenerationAllowed"]=true};o,_,s=execute(r);assert_equal 1,s;assert_includes o,"MANIFEST_POSE_BLOCK";assert_includes o,"MANIFEST_FBX_BLOCK";assert_includes o,"MANIFEST_UNITY_BLOCK";assert_includes o,"MANIFEST_GATE"}
 end
 def test_verified_lfs_round_trip_is_exact
  repo{|r|mut(r,M,"GenerationManifest"){|x|b=x["stages"]["blend-source"];b["lfsState"]="PENDING_CORE_PUSH";b["coreCommit"]="0"*40;b["indexPointerBytes"]=130;b["indexPointerVerified"]=false;b["remoteObjectRoundTripVerified"]=false};rule(r,"MANIFEST_BLEND")}
 end
 def test_profile_required_before_flags_are_exact;repo{|r|mut(r,P,"CharacterProportionProfile"){|x|x["neutralVisualGate"]["requiredBeforePoseWork"]=false;x["neutralVisualGate"]["requiredBeforeFbxExport"]=false};rule(r,"PROFILE_GATE")}end
 private
 def execute(r,b=false);c=[RbConfig.ruby,V.to_s,"--root",r.to_s];c<<"--verify-blender"if b;o,e,s=Open3.capture3(*c);[o,e,s.exitstatus]end
 def rule(r,x);o,_,s=execute(r);assert_equal 1,s,o;assert_includes o,"rule=#{x}"end
 def repo;Dir.mktmpdir{|d|r=Pathname(d);SUPPORT.each{|p|dst=r.join(p);FileUtils.mkdir_p(dst.dirname);FileUtils.cp(ROOT.join(p),dst)};yield r}end
 def mut(r,p,key);d=YAML.safe_load(r.join(p).read,aliases:false);yield d[key];r.join(p).write(YAML.dump(d))end
end
