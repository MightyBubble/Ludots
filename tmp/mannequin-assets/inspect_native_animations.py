import ctypes as c
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[1]

class Vec3(c.Structure):
    _fields_ = [(axis, c.c_float) for axis in ("x", "y", "z")]

class Vec4(c.Structure):
    _fields_ = [(axis, c.c_float) for axis in ("x", "y", "z", "w")]

class Transform(c.Structure):
    _fields_ = [("translation", Vec3), ("rotation", Vec4), ("scale", Vec3)]

class BoneInfo(c.Structure):
    _fields_ = [("name", c.c_char * 32), ("parent", c.c_int)]

class Animation(c.Structure):
    _fields_ = [
        ("boneCount", c.c_int), ("frameCount", c.c_int),
        ("bones", c.POINTER(BoneInfo)),
        ("framePoses", c.POINTER(c.POINTER(Transform))),
        ("name", c.c_char * 32),
    ]

dll_path = repo / "src/Platforms/Desktop/raylib.dll"
raylib = c.CDLL(str(dll_path))
raylib.LoadModelAnimations.argtypes = [c.c_char_p, c.POINTER(c.c_int)]
raylib.LoadModelAnimations.restype = c.POINTER(Animation)
raylib.UnloadModelAnimations.argtypes = [c.POINTER(Animation), c.c_int]
model_path = root / "mannequin_large_idle_walk.glb"
count = c.c_int()
animations = raylib.LoadModelAnimations(str(model_path).encode(), c.byref(count))
assert animations and count.value == 11, count.value
results = []
try:
    for index in range(count.value):
        animation = animations[index]
        assert animation.boneCount == 23 and animation.frameCount > 0
        name = animation.name.decode()
        changed_bones = []
        for bone in range(animation.boneCount):
            base = animation.framePoses[0][bone]
            maximum = 0.0
            for frame in range(1, animation.frameCount):
                pose = animation.framePoses[frame][bone]
                values = (
                    abs(getattr(getattr(pose, part), axis) - getattr(getattr(base, part), axis))
                    for part, axes in (("translation", "xyz"), ("rotation", "xyzw"), ("scale", "xyz"))
                    for axis in axes
                )
                maximum = max(maximum, *values)
            if maximum > 0.0001:
                changed_bones.append({"bone": animation.bones[bone].name.decode(), "max_component_delta": maximum})
        results.append({"index": index, "name": name, "frames": animation.frameCount, "bones": animation.boneCount, "moving_bones": changed_bones})
    for expected_index, expected_name in ((5, "retarget_Idle_A"), (6, "retarget_Walking_A")):
        selected = results[expected_index]
        assert selected["name"] == expected_name and selected["frames"] > 2 and len(selected["moving_bones"]) > 5
finally:
    raylib.UnloadModelAnimations(animations, count)
result = {"dll": str(dll_path), "dll_sha256": hashlib.sha256(dll_path.read_bytes()).hexdigest(), "animation_count": count.value, "animations": results}
(root / "native-animation-validation.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps([{k: value for k, value in item.items() if k != "moving_bones"} | {"moving_bone_count": len(item["moving_bones"])} for item in results], indent=2))
