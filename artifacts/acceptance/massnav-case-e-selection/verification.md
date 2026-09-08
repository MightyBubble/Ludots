# Verification

## Passing checks

- `GasTests`: 33 passed, covering Case E selection, scale pressure, and move-plan lifecycle.
- `PresentationTests`: 24 passed, covering the 10k MassNavigation production path, presenter contracts, and Formation lifecycle.
- `ArchitectureTests`: 25 passed for presentation and prefab configuration contracts.
- Raylib Release build: 0 warnings, 0 errors.
- Launcher resolve: root selector `capability_standard_mass_navigation_large_world_10k` resolves `SelectionInteractionMod` before `MassNavigationMod`; the legacy acquisition flag is false.

## Runtime boundary

A separate Raylib launch with AgentBridge on port 47922 loaded the 10k `mass_navigation` map and all seven planned mods. The process then stopped before the first usable interaction frame because the existing renderer rejected the soldier asset's 615 bone slots against its hard 512-slot limit:

`RaylibPoseTexturePalette requested bone slot capacity 615 exceeds hard cap 512`

This is outside the selection migration and prevents claiming a whole-machine interactive FPS or mouse acceptance from this run. The existing process on port 47921 was not touched.
