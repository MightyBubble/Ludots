# Dynamic NavMesh Open World 64x64

Playable open-world battlefield (`nav_bake_open_world_64x64`): 4096 coarse graph nodes, fixed 8x8 resident NavMesh window, hotspot jumps.

## Launch

```text
.\scripts\run-mod-launcher.cmd cli launch preset:nav_bake_showcase_raylib
```

Choose Open World in the shared overlay panel. Local wall rebuilds only visit the resident window; long moves use the coarse graph corridor first.
