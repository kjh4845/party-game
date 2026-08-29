#!/usr/bin/env python3

import os
import sys

import bpy


ASSET_ID = "CHR_MasterCharacter_C1B_Blockout"
VIEWS = ("Front", "Side", "Back", "ThreeQuarter")
STYLES = ("Neutral", "Silhouette")


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


def main():
    output_directory = parse_output_directory()
    os.makedirs(output_directory, exist_ok=True)
    scene = bpy.context.scene
    view_layer = scene.view_layers[0]
    ground = bpy.data.objects["QA_Ground"]
    silhouette_material = bpy.data.materials["MAT_C1B003_Silhouette"]
    outputs = []
    for style in STYLES:
        if style == "Neutral":
            view_layer.material_override = None
            ground.hide_render = False
            set_world(scene, (0.18, 0.18, 0.18), 1.05)
        else:
            view_layer.material_override = silhouette_material
            ground.hide_render = True
            set_world(scene, (0.75, 0.75, 0.75), 0.8)
        for view in VIEWS:
            scene.camera = bpy.data.objects[f"CAM_C1B003_{view}"]
            filename = f"{ASSET_ID}_r01_{style}_{view}.png"
            scene.render.filepath = os.path.join(output_directory, filename)
            bpy.ops.render.render(write_still=True)
            outputs.append(filename)
    print(f"C1B003_RERENDER_COUNT={len(outputs)}")
    print("C1B003_RERENDER_RESULT=PASS")


if __name__ == "__main__":
    main()
