#!/usr/bin/env python3

import json
import os
import sys

import bpy


def parse_output_directory():
    arguments = sys.argv
    if "--" not in arguments:
        raise RuntimeError("expected -- <output-directory>")
    custom = arguments[arguments.index("--") + 1 :]
    if len(custom) != 1:
        raise RuntimeError("expected one output directory")
    return os.path.abspath(custom[0])


def set_world(scene, color, strength):
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (*color, 1.0)
    background.inputs["Strength"].default_value = strength


def scenario_collections():
    result = {}
    for collection in bpy.data.collections:
        scenario_id = collection.get("c1b004_scenario_id")
        if scenario_id:
            result[str(scenario_id)] = collection
    return result


def set_scenario_visibility(scenarios, active_id):
    for scenario_id, collection in scenarios.items():
        collection.hide_render = scenario_id != active_id


def main():
    output_directory = parse_output_directory()
    os.makedirs(output_directory, exist_ok=True)
    scene = bpy.context.scene
    jobs = json.loads(scene["render_jobs_json"])
    scenarios = scenario_collections()
    view_layer = scene.view_layers[0]
    ground = bpy.data.objects["QA_Ground"]
    silhouette_material = bpy.data.materials["MAT_C1B003_Silhouette"]
    outputs = []
    for job in jobs:
        set_scenario_visibility(scenarios, job["scenarioId"])
        scene.camera = bpy.data.objects[job["camera"]]
        if job["style"] == "Neutral":
            view_layer.material_override = None
            ground.hide_render = False
            set_world(scene, (0.18, 0.18, 0.18), 1.05)
        else:
            view_layer.material_override = silhouette_material
            ground.hide_render = True
            set_world(scene, (0.75, 0.75, 0.75), 0.8)
        scene.render.filepath = os.path.join(output_directory, job["filename"])
        bpy.ops.render.render(write_still=True)
        outputs.append(job["filename"])
    view_layer.material_override = None
    ground.hide_render = False
    set_world(scene, (0.18, 0.18, 0.18), 1.05)
    set_scenario_visibility(scenarios, "Neutral")
    print(f"C1B004_RERENDER_COUNT={len(outputs)}")
    print("C1B004_RERENDER_RESULT=PASS")


if __name__ == "__main__":
    main()
