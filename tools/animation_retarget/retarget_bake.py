#!/usr/bin/env python3
"""Headless open-source animation retarget bake for Ludots (Blender bpy).

Usage:
  blender --background --python tools/animation_retarget/retarget_bake.py -- \\
    --source /path/anim.glb --target /path/character.glb \\
    --mapping tools/animation_retarget/mappings/kaykit_name_identity.json \\
    --out /path/out.glb
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def _parse_args(argv: list[str]) -> argparse.Namespace:
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    parser = argparse.ArgumentParser(description="Ludots headless GLB animation retarget bake")
    parser.add_argument("--source", required=True, help="Source GLB containing animations")
    parser.add_argument("--target", required=True, help="Target GLB containing skinned mesh")
    parser.add_argument("--mapping", required=True, help="JSON bone name mapping SSOT")
    parser.add_argument("--out", required=True, help="Output GLB path")
    parser.add_argument("--action", default="", help="Optional source action name filter (substring)")
    return parser.parse_args(argv)


def _die(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(2)


def _load_mapping(path: Path) -> dict[str, str]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schema") != "ludots.animation_retarget_mapping.v1":
        _die(f"unsupported mapping schema in {path}")
    bones = data.get("bones")
    if not isinstance(bones, dict) or not bones:
        _die(f"mapping bones missing in {path}")
    return {str(k): str(v) for k, v in bones.items()}


def _armatures():
    import bpy

    return [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]


def _clear_scene() -> None:
    import bpy

    bpy.ops.wm.read_factory_settings(use_empty=True)


def _import_glb(path: Path) -> None:
    import bpy

    if not path.is_file():
        _die(f"missing file: {path}")
    bpy.ops.import_scene.gltf(filepath=str(path))


def _bone_lookup(arm) -> dict[str, str]:
    return {b.name.lower(): b.name for b in arm.data.bones}


def _select_only(obj) -> None:
    import bpy

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def main() -> None:
    args = _parse_args(sys.argv)
    source_path = Path(args.source).resolve()
    target_path = Path(args.target).resolve()
    mapping_path = Path(args.mapping).resolve()
    out_path = Path(args.out).resolve()
    out_path.parent.mkdir(parents=True, exist_ok=True)
    mapping = _load_mapping(mapping_path)

    try:
        import bpy
    except ImportError as exc:
        _die(f"bpy unavailable; run under blender --background --python: {exc}")

    _clear_scene()
    _import_glb(target_path)
    target_arms = _armatures()
    if not target_arms:
        _die("target GLB has no armature")
    target_arm = target_arms[0]
    target_bone_names = _bone_lookup(target_arm)
    keep_object_ids = {id(obj) for obj in bpy.data.objects}

    arms_before = set(id(a) for a in _armatures())
    actions_before = set(a.name for a in bpy.data.actions)
    _import_glb(source_path)
    source_arms = [a for a in _armatures() if id(a) not in arms_before]
    if not source_arms:
        _die("source GLB has no additional armature")
    source_arm = source_arms[0]
    source_bone_names = _bone_lookup(source_arm)

    source_actions = [a for a in bpy.data.actions if a.name not in actions_before]
    if not source_actions:
        source_actions = list(bpy.data.actions)
    if args.action:
        source_actions = [a for a in source_actions if args.action.lower() in a.name.lower()]
    if not source_actions:
        _die("source GLB produced no actions to retarget")

    mapped = 0
    missing_source: list[str] = []
    missing_target: list[str] = []
    for source_key, target_key in mapping.items():
        src = source_bone_names.get(source_key.lower())
        dst = target_bone_names.get(target_key.lower())
        if src is None:
            missing_source.append(source_key)
            continue
        if dst is None:
            missing_target.append(target_key)
            continue
        pb = target_arm.pose.bones.get(dst)
        if pb is None:
            missing_target.append(target_key)
            continue
        constraint = pb.constraints.new("COPY_TRANSFORMS")
        constraint.name = f"retarget_{src}"
        constraint.target = source_arm
        constraint.subtarget = src
        constraint.mix_mode = "REPLACE"
        constraint.target_space = "WORLD"
        constraint.owner_space = "WORLD"
        mapped += 1

    if mapped == 0:
        _die(
            "no bones mapped; check mapping JSON against source/target joint names. "
            f"missingSource={missing_source[:8]} missingTarget={missing_target[:8]}"
        )

    print(
        f"INFO: mappedBones={mapped} missingSource={len(missing_source)} "
        f"missingTarget={len(missing_target)} sourceActions={len(source_actions)}"
    )

    _select_only(target_arm)
    if target_arm.animation_data is None:
        target_arm.animation_data_create()

    baked_names: list[str] = []
    for action in source_actions:
        if source_arm.animation_data is None:
            source_arm.animation_data_create()
        source_arm.animation_data.action = action

        start = int(action.frame_range[0])
        end = int(max(action.frame_range[1], action.frame_range[0] + 1))
        bpy.context.scene.frame_start = start
        bpy.context.scene.frame_end = end

        # Ensure bake creates a fresh action on the target.
        target_arm.animation_data.action = None
        bpy.ops.nla.bake(
            frame_start=start,
            frame_end=end,
            step=1,
            only_selected=False,
            visual_keying=True,
            clear_constraints=False,
            clear_parents=False,
            use_current_action=False,
            bake_types={"POSE"},
        )
        baked = target_arm.animation_data.action if target_arm.animation_data else None
        if baked is None:
            _die(f"bake failed for source action '{action.name}'")
        baked.name = f"retarget_{action.name}"
        baked_names.append(baked.name)

    if not baked_names:
        _die("bake produced zero actions")

    for pb in target_arm.pose.bones:
        for constraint in list(pb.constraints):
            if constraint.name.startswith("retarget_"):
                pb.constraints.remove(constraint)

    # Drop every object introduced by the source import (extra meshes/armatures).
    for obj in list(bpy.data.objects):
        if id(obj) not in keep_object_ids:
            bpy.data.objects.remove(obj, do_unlink=True)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(
        filepath=str(out_path),
        export_format="GLB",
        use_selection=False,
        export_animations=True,
        export_animation_mode="ACTIONS",
        export_skins=True,
        export_morph=False,
    )

    if not out_path.is_file() or out_path.stat().st_size <= 0:
        _die(f"export failed: {out_path}")

    print(f"OK: wrote {out_path} bakedActions={len(baked_names)} mappedBones={mapped}")
    for name in baked_names:
        print(f"ACTION: {name}")


if __name__ == "__main__":
    main()
