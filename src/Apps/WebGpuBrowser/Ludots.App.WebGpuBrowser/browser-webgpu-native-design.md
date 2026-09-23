# Browser-Resident C# WebGPU Mass Navigation Showcase

## 1. Overview

This application runs the production `capability_standard_mass_navigation_large_world_10k` showcase inside the browser. It is not a visual imitation or a reduced browser-specific scenario: the same C# Mod chain owns startup, world state, simulation, presentation, and player commands.

The browser supplies WebAssembly, worker, canvas, and WebGPU hosting. After startup, C# reads the production presentation buffers and submits WebGPU work directly through the Emscripten C ABI. No game state, frame packet, input packet, or rendering command crosses a hand-written JavaScript protocol.

## 2. Structure

```text
Ludots.App.WebGpuBrowser
  -> Ludots.Adapter.WebGpu
     -> GameEngine
        -> LudotsCoreMod
        -> CoreInputMod
        -> MassNavigationMod
        -> CapabilityStandardMassNavigationLargeWorld10kMod
     -> production presentation buffers
        -> terrain / mesh batches / HUD / minimap / overlays
  -> Ludots.Client.WebGpu.Runtime
     -> Ludots.Client.WebGpu.Native
        -> Emscripten WebGPU and HTML5 C ABIs
           -> browser WebGPU
```

- `Ludots.App.WebGpuBrowser` is the browser WASM composition root and declares the exact production Mod load plan.
- `Ludots.Adapter.WebGpu` translates existing Core presentation ports into allocation-stable WebGPU frame data. It does not create a second game state.
- `Ludots.Client.WebGpu.Runtime` owns WebGPU device, surface, pipelines, GPU resources, frame scheduling, and failure handling.
- `Ludots.Client.WebGpu.Native` owns the Emscripten ABI declarations used by C#.

## 3. Details

- Target: `net10.0-browser`, `browser-wasm`, Release AOT, WASM threads, SIMD, and native linking.
- Formal launch: `LudotsCoreMod -> CoreInputMod -> MassNavigationMod -> CapabilityStandardMassNavigationLargeWorld10kMod`.
- Formal world: the configured `mass_navigation` map and its `MassNavigationSimulationRuntime` must report exactly 10,000 navigation agents before the showcase is considered ready.
- Terrain: read from `IVisualHeightmapRenderSource`; a missing or unreadable chunk terminates the frame instead of drawing a substitute.
- Units: read from `SkinnedVisualBatchBuffer` as contiguous instances and rendered with the registered WebGPU host mesh asset.
- Static presentation: read from the production primitive snapshot and grouped by registered mesh asset without per-frame collection growth.
- HUD and minimap: read from `ScreenHudBatchBuffer`, `ScreenOverlayBuffer`, and `MinimapScreenMarkerBuffer`, including text, bars, clipping, orientation, and controls.
- Input: C# registers document, canvas, and window callbacks through the Emscripten HTML5 C ABI. Keyboard and mouse state enter the existing `PlayerInputHandler`; JavaScript does not translate actions or commands.
- Canvas ownership: the loader transfers one `OffscreenCanvas` to the .NET worker before managed startup. This one-time bootstrap is not an application protocol.
- Failure policy: missing WebGPU capabilities, assets, presentation sources, buffer capacity, or unsupported presentation data cause explicit terminal errors. There is no WebGL, streamed-frame, reduced-agent, or placeholder fallback.
- Performance: ECS and presentation consumption use spans and retained arrays. GPU instance buffers grow geometrically outside steady state and are reused. The acceptance target is stable foreground frame pacing without presentation-buffer drops or frame-wide blanking.

Reference repositories are research inputs only and are not product dependencies:

- Evergine WebGPU.NET at `03660d7669fb96086c776ea38f0a3ccf21526679`
- Pollus at `64e87fb1a8dffca82322f0ec28460bb435a1dc19`
- Istok.WebGPU at `33037aa2adf388222229c07e4e87f7b33e93e460`
- hjoykim/THREE at `a77bcae5542b3a33048fd2af6230c32b4a9fc6f2`

## 4. Scenarios

- A new player opens the page and immediately sees the production large-world battlefield, four armies, health presentation, and the strategic minimap.
- The player moves the camera with WASD while the minimap viewport follows the same camera.
- The player drags over visible friendly units and sees both the marquee and the selected-unit command markers.
- The player right-clicks valid terrain and the selected units receive the production Mass Navigation order and begin moving.
- The player toggles the minimap and continues playing without pausing or recreating the simulation.
- The player resizes the browser and the world, HUD, pointer coordinates, and minimap continue to agree.

## 5. Boundaries

- The application consumes the existing Mass Navigation Mod and capability-standard showcase. Browser-only gameplay rules, entity copies, scripted formations, and reduced agent counts are prohibited.
- The handwritten JavaScript file may validate browser prerequisites, transfer the canvas once, and start the generated .NET loader. It may not own input mapping, simulation, frame scheduling, presentation data, or WebGPU commands.
- Generated .NET and Emscripten loader code is runtime infrastructure, not an application-level cross-language protocol.
- Raylib remains the reference adapter, but browser validation compares player-visible behavior and measured foreground timing. Claims of equal performance require measurements from both adapters under equivalent visibility and release settings.
- Unsupported production presentation data must either be implemented by the WebGPU adapter or fail explicitly. Silent omission is not acceptable.

## 6. UAT

```gherkin
Feature: Play the production 10K Mass Navigation showcase in the browser

  Scenario: A player enters the battlefield
    Given the browser supports WebGPU, worker isolation, and canvas transfer
    When the player opens the WebGPU showcase
    Then the production Mass Navigation map appears
    And four armies containing 10,000 navigation units are running
    And the terrain, unit models, health presentation, and minimap are visible
    And no substitute scene or reduced unit count is used

  Scenario: A player surveys the battlefield
    Given the battlefield is running
    When the player holds W, A, S, or D
    Then the world camera moves continuously in that direction
    And the minimap viewport follows the same camera
    And the HUD remains anchored to the corresponding units

  Scenario: A player selects an army group
    Given friendly units are visible outside the minimap
    When the player drags a selection box across those units
    Then a selection marquee follows the pointer while dragging
    And at least one friendly unit becomes selected after release
    And every selected unit receives its production command marker

  Scenario: A player orders selected units to move
    Given at least one friendly unit is selected
    When the player right-clicks valid terrain away from the group
    Then the browser context menu does not appear
    And the selected units receive one production Mass Navigation move order
    And at least one selected unit changes world position toward the destination

  Scenario: A player toggles the strategic minimap
    Given the minimap is visible
    When the player presses M
    Then the minimap disappears while the battlefield keeps running
    When the player presses M again
    Then the same minimap returns with current unit and camera positions

  Scenario: The battle remains visually stable
    Given the production battle is in steady state
    When the player watches consecutive frames
    Then terrain, units, health presentation, and minimap do not flash blank
    And no production presentation buffer reports dropped items
    And no WebGPU validation error is reported

  Scenario: A required browser or production capability is unavailable
    Given a required WebGPU, worker, asset, Mod, or presentation capability is missing
    When the player opens or continues the showcase
    Then the application enters an explicit failed state naming the missing capability
    And no fallback renderer, streamed frame, placeholder asset, or smaller simulation starts
```
