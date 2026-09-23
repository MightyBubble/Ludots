# HUD Frame Consistency

## Scenario

- Build: `cf5bf6778de096e7fe71ee2f8ab1162c18dfb0b9` plus the HUD working diff.
- Date: 2026-09-08.
- Fixture: deterministic grid, 1k / 5k / 10k units, one bar and one numeric label per unit, 60 Hz input frames.
- Runtime: Raylib Release, `mass_navigation`, 10k GpuSkinned, Agent Bridge on port 47960.
- Reuse: CameraManager, CoreScreenProjector, WorldHudToScreenSystem, ScreenHudBatchBuffer, PresentationOverlaySceneBuilder, default lane pacer and SkiaOverlayRenderer.

```gherkin
Feature: Read unit health while moving the camera
  Scenario: Rotate, pause, and rotate back
    Given units have health bars and numeric health labels
    When the player rotates the camera and then stops
    Then visible health labels and bars remain at their current unit positions
    And they do not jump back to earlier screen positions

  Scenario: Health changes while the camera moves
    Given units have visible health bars and numeric health labels
    When health changes and the player moves or stops the camera
    Then the next presented frame shows the current health and position
```

## Evidence

- Red: `../../benchmarks/gpuskinned-regression/hud-frame-red2.log` records 5 failing image comparisons before the fixes.
- Green: `../../benchmarks/gpuskinned-regression/hud-final-targeted-serial.log` records 34 passing tests, including current-value assertions, retained scene item equality and current-frame image equality.
- Build: `../../benchmarks/gpuskinned-regression/hud-current-build.log` records a successful final Release build, zero errors and two warnings.
- Camera sequence: 45, 45, 46, 47, 48, 49, 50, 55, 55, 65, 65, 45, 45 degrees. Each frame is checked, including frames with no camera change.
- Live sequence: 45, 50, 55, 60, 65, 70, 75, 75, 65, 55, 45, 45 degrees. Agent Bridge confirmed target (0, 0), pitch 42 and distance 14000 cm remained constant. Simulation was paused and advanced two steps per pose.
- Live recording: `../../agent-bridge/recordings/20260908-141806/manifest.json`, 37 PNG frames. Dense overlapping labels limit visual evaluation; this is not continuous-input or near-view acceptance.

## Confirmed Defects

1. Large text switched from current retained sprites to stale chunk pictures on unchanged frames. Only one 128-item chunk refreshed per frame. Removing that secondary cache keeps unchanged and changed frames on the same retained path.
2. Partial movement could be declared uniform translation of an entire lane. Translation now requires complete lane coverage, matching identity/order and no earlier content mutation.
3. Visibility compaction moved surviving screen items without including their new indices in position update ranges. Compaction now expands any active position range to include the moved destinations.

The stale text branch dates to `f4511b16313` (2026-04-26). Range/translation changes date to `52e5bd5e664` (2026-05-02). These dates precede June; they do not establish the June-to-July regression trigger.

## Outcome And Limits

The deterministic HUD tests pass at all three scales. Lane pacing configuration is unchanged: a deferred uniform translation still draws current positions; changing its budget was not supported by the pixel comparisons and was excluded.

Full PresentationTests did not pass and was interrupted after failures outside the focused test set, including crowd physics, text catalog contracts and HUD visibility counts. Their baseline status is unclassified. The full log is `../../benchmarks/gpuskinned-regression/hud-presentation-full.log`.

Continuous mouse-camera input, readable near-view runtime acceptance, production frame allocation and whole-machine 60 FPS remain unverified by this patch. The screenshot sequence and local rendering tests must not be reported as whole-machine FPS proof. No GAS transaction code is modified.

The final near-view verification launch and bridge command was rejected by automatic approval review with `blocked by policy`; no more specific reason was returned. That verification did not execute.
