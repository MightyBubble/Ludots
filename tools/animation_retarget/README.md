# Animation Retarget (headless, open source)

Blender 4.x background bake. No paid addons. Mapping is data-driven JSON.

## Requirements

- `blender` on PATH (GPL, open source)
- Mapping file under `mappings/` (`ludots.animation_retarget_mapping.v1`)

## Usage

```bash
./tools/animation_retarget/run_retarget.sh \
  --source /path/source_anim.glb \
  --target /path/target_character.glb \
  --mapping tools/animation_retarget/mappings/kaykit_name_identity.json \
  --out /path/out.glb \
  --action Walking_A
```

Unmapped target bones stay at bind pose (extra fingers / missing slots are ignored, not silently remapped to wrong joints). Missing mapping or empty bake exits non-zero.

## Fixtures

KayKit sample GLBs are **not** committed. Fetch locally when validating:

```bash
# example sources used in agent validation
# Mannequin_Large.glb + Rig_Large_MovementBasic.glb from KayKit Character Animations (CC0)
```
