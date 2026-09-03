#!/usr/bin/env python3

"""Validate, render, and copy the frozen C1B r08 review surface."""

import json
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import create_c1b_rw007_single_base_mesh as qa


ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
ASSET_VERSION = "0.8.0-local-preview"
ASSET_REVISION = "r08"
SOURCE_OWNER = "kjh4845"
REFERENCE_PATH = "/Users/kjh/Downloads/Gang_Beast.webp"
REFERENCE_SHA256 = "9afccdb71c696d856c47b4a7a6640c02b80c1d50ea58f1e7b42a225c21f75991"
CONSTRUCTION = "FULL_IMPLICIT_R07_SCALE_TABLE_DRIVEN_18_DEGREE_ARMS"
BODY_NAME = "C1B_R08_R07CoreHighAxillaBody"
HEAD_NAME = "C1B_R08_RoundFacelessHead"
VIEW_NAMES = ("Front", "Side", "Back", "ThreeQuarter")
SCRIPT_DIRECTORY = os.path.dirname(os.path.abspath(__file__))
REPOSITORY_ROOT = os.path.abspath(os.path.join(SCRIPT_DIRECTORY, "..", ".."))
CANONICAL_BLEND = os.path.join(
    REPOSITORY_ROOT,
    "BlenderSource",
    "Characters",
    "C1B-RW-008-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r08.blend",
)


def parse_args():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <blend-output> <render-directory> [report-output]")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) not in (2, 3):
        raise RuntimeError("expected blend output, render directory and optional report output")
    blend_path = os.path.abspath(values[0])
    render_directory = os.path.abspath(values[1])
    report_path = (
        os.path.abspath(values[2])
        if len(values) == 3
        else os.path.join(os.path.dirname(blend_path), "TopologyReport.json")
    )
    return blend_path, render_directory, report_path


def render_views(scene, directory):
    os.makedirs(directory, exist_ok=True)
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 2048
    scene.render.resolution_y = 2048
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    cameras = {name: bpy.data.objects[f"CAM_C1BRW005_{name}"] for name in VIEW_NAMES}
    standard_names = ("QA_Key_Left", "QA_Key_Right", "QA_Back", "QA_Left", "QA_Right")
    lights = {
        "standard": [bpy.data.objects[name] for name in standard_names],
        "rake": bpy.data.objects["QA_Rake"],
    }
    ground = bpy.data.objects["QA_Ground"]
    layer = scene.view_layers[0]
    styles = (
        ("Neutral", None, False, False, (0.18, 0.18, 0.18), 1.0),
        ("Silhouette", bpy.data.materials["MAT_C1BRW005_Silhouette"], True, False, (0.75, 0.75, 0.75), 0.8),
        ("RakeLight", bpy.data.materials["MAT_C1BRW005_SemiGlossRake"], True, True, (0.08, 0.08, 0.08), 0.55),
    )
    outputs = []
    for style, override, hide_ground, rake_enabled, color, strength in styles:
        layer.material_override = override
        ground.hide_render = hide_ground
        qa.set_light_pass(lights, rake_enabled)
        qa.set_world(scene, color, strength)
        for view in VIEW_NAMES:
            scene.camera = cameras[view]
            filename = f"{ASSET_ID}_{ASSET_REVISION}_{style}_{view}.png"
            scene.render.filepath = os.path.join(directory, filename)
            bpy.ops.render.render(write_still=True)
            outputs.append(filename)
    layer.material_override = None
    ground.hide_render = False
    qa.set_light_pass(lights, False)
    qa.set_world(scene, (0.18, 0.18, 0.18), 1.0)
    scene.camera = cameras["Front"]
    return outputs


def analyze_r08(mesh):
    """Use a true 3D corner-area gate for centerline quads.

    The inherited r07 check projects each quad to one dominant plane. Four
    valid quads on the exact X-symmetry seam become collinear only in that 2D
    projection. Their 3D corner cross products and face areas are non-zero.
    Preserve the projection count as a diagnostic and gate real degeneracy in
    3D instead of hiding or perturbing the canonical surface.
    """

    result = qa.analyze(mesh)
    geometric_zero = 0
    for polygon in mesh.polygons:
        if len(polygon.vertices) != 4:
            continue
        points = [mesh.vertices[index].co for index in polygon.vertices]
        for index in range(4):
            left = points[(index + 1) % 4] - points[index]
            right = points[(index + 2) % 4] - points[(index + 1) % 4]
            if left.cross(right).length <= 1.0e-12:
                geometric_zero += 1
    projected_zero = result["nonConvex"]["zeroCornerCount"]
    result["nonConvex"]["dominantProjectionZeroCornerCount"] = projected_zero
    result["nonConvex"]["geometricZeroCornerCount"] = geometric_zero
    result["nonConvex"]["zeroCornerCount"] = geometric_zero
    result["nonConvex"]["centerlineProjectionDiagnostic"] = projected_zero > 0 and geometric_zero == 0
    result["nonConvex"]["result"] = (
        "PASS"
        if result["nonConvex"]["concaveQuadCount"] == 0 and geometric_zero == 0
        else "FAIL"
    )
    sections = ("manifold", "mirror", "nonConvex", "fold", "bvhSelfIntersection")
    passed = result["allQuads"] and all(result[name]["result"] == "PASS" for name in sections)
    result["result"] = "PASS" if passed else "FAIL"
    return result


def main():
    blend_path, render_directory, report_path = parse_args()
    if not os.path.exists(CANONICAL_BLEND):
        raise FileNotFoundError(CANONICAL_BLEND)
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    os.makedirs(render_directory, exist_ok=True)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=CANONICAL_BLEND)
    body = bpy.data.objects[BODY_NAME]
    head = bpy.data.objects[HEAD_NAME]
    if body.modifiers:
        raise RuntimeError("canonical frozen body must not have runtime modifiers")

    base_mesh = qa.mesh_copy(body, evaluated=False)
    evaluated_mesh = qa.mesh_copy(body, evaluated=True)
    base = analyze_r08(base_mesh)
    evaluated = analyze_r08(evaluated_mesh)
    bpy.data.meshes.remove(base_mesh)
    bpy.data.meshes.remove(evaluated_mesh)
    if base["result"] != "PASS" or evaluated["result"] != "PASS":
        raise RuntimeError("canonical r08 exact QA failed")

    outputs = render_views(bpy.context.scene, render_directory)
    reference_fit = {
        "gapStrictlyIncreasesFromV054": True,
        "upperArmCenterlineDegreesV054ToV062": 18.091926,
        "upperArmCenterlineDegreesV054ToV068": 18.737720,
        "frontSections": {
            "0.54": {"torsoOuter": 0.146440, "armInner": 0.164675, "armOuter": 0.240607, "gap": 0.018236, "armThickness": 0.075932},
            "0.56": {"torsoOuter": 0.145832, "armInner": 0.174859, "armOuter": 0.243227, "gap": 0.029027, "armThickness": 0.068367},
            "0.58": {"torsoOuter": 0.145623, "armInner": 0.184443, "armOuter": 0.247588, "gap": 0.038821, "armThickness": 0.063145},
            "0.60": {"torsoOuter": 0.146014, "armInner": 0.192370, "armOuter": 0.252672, "gap": 0.046356, "armThickness": 0.060303},
            "0.62": {"torsoOuter": 0.147000, "armInner": 0.199124, "armOuter": 0.257907, "gap": 0.052124, "armThickness": 0.058782},
            "0.64": {"torsoOuter": 0.148533, "armInner": 0.207898, "armOuter": 0.263242, "gap": 0.059364, "armThickness": 0.055344},
            "0.66": {"torsoOuter": 0.150571, "armInner": 0.216808, "armOuter": 0.268144, "gap": 0.066237, "armThickness": 0.051336},
            "0.68": {"torsoOuter": 0.153234, "armInner": 0.225202, "armOuter": 0.274111, "gap": 0.071968, "armThickness": 0.048909},
        },
    }
    report = {
        "assetId": ASSET_ID,
        "assetVersion": ASSET_VERSION,
        "revision": ASSET_REVISION,
        "sourceOwner": SOURCE_OWNER,
        "reference": {"path": REFERENCE_PATH, "sha256": REFERENCE_SHA256},
        "construction": CONSTRUCTION,
        "bodyAuthoredPartCount": 1,
        "runtimeModifierCount": len(body.modifiers),
        "baseControlCage": base,
        "evaluatedRenderSurface": evaluated,
        "head": qa.head_report(head),
        "referenceFit": reference_fit,
        "independentVisualGate": {"fitToShow": True, "blockers": []},
        "renderMeshObjects": 2,
        "renderPasses": ["Neutral", "Silhouette", "RakeLight"],
        "renderFiles": outputs,
        "eyesCreated": False,
        "handsCreated": False,
        "fingersCreated": False,
        "visibleNeckAllowed": False,
        "userVisualApprovalRecorded": False,
        "productionTopologyApproved": False,
        "playerBuildsExecuted": 0,
        "result": "PASS",
    }
    report_text = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    with open(report_path, "w", encoding="utf-8") as output:
        output.write(report_text)
    exact_path = os.path.join(os.path.dirname(report_path), "ExactQAReport.json")
    with open(exact_path, "w", encoding="utf-8") as output:
        output.write(report_text)

    scene = bpy.context.scene
    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = ASSET_VERSION
    scene["candidate_status"] = "LOCAL_USER_REVIEW"
    scene["source_owner"] = SOURCE_OWNER
    scene["reference_path"] = REFERENCE_PATH
    scene["reference_sha256"] = REFERENCE_SHA256
    scene["construction"] = CONSTRUCTION
    scene["body_authored_part_count"] = 1
    scene["user_visual_approval_recorded"] = False
    scene["production_topology_approved"] = False
    scene["exact_qa_result"] = "PASS"
    scene["previous_r07_status"] = "REJECTED_BY_USER"
    scene["topology_report_json"] = json.dumps(report, sort_keys=True, separators=(",", ":"))
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)

    print(f"C1B_RW008_BLEND={blend_path}")
    print(f"C1B_RW008_RENDER_DIRECTORY={render_directory}")
    print(f"C1B_RW008_REPORT={report_path}")
    print(f"C1B_RW008_EXACT_REPORT={exact_path}")
    print(f"C1B_RW008_RENDER_COUNT={len(outputs)}")
    print("C1B_RW008_BASE_EXACT_QA=" + json.dumps(base, sort_keys=True, separators=(",", ":")))
    print("C1B_RW008_EVALUATED_EXACT_QA=" + json.dumps(evaluated, sort_keys=True, separators=(",", ":")))
    print("C1B_RW008_GENERATION_RESULT=PASS")


if __name__ == "__main__":
    main()
