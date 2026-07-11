# Documentation Governance Report

Date: 2026-07-11
Scope: MassNavigation issue #642 formal documentation and acceptance command surfaces:
- `gitbook/reference/mass-navigation-user-book.md`
- `gitbook/reference/map-scale-authoring-guide.md`
- `gitbook/reference/map-scale-authoring-starter.html`
- `mods/capabilities/navigation/MassNavigationMod/README.md`
- `gitbook/reference/mass-navigation-formal-chain.md`
- `gitbook/reference/obstacle-authoring.md`
- `gitbook/architecture/capability-standard-showcases.md`
- `scripts/acceptance/run-mass-navigation-large-world-uat.ps1`
- repository scan of `docs/**/*.md` and `gitbook/**/*.{md,html}` for removed MassNavigation configuration fields

Ruleset: `ludots-doc-governance` checklist, repository-relative path integrity, ConfigPipeline SSOT, strict no-alias configuration policy, and launcher wrapper command verification.

## Summary

- Total findings: 5
- P0: 0
- P1: 4
- P2: 1
- P3: 0
- Fixed in this slice: 4

## Findings

### P1-01 User book assigned obstacle, camera, and view-residency fields to MassNavigationConfig

- Problem: the user book described `obstacles`, `cameraProfiles`, `viewResidency`, and `cameraProbes` as current `MassNavigationConfig.json` authoring fields.
- Impact: Mod authors could write keys rejected by strict config loading or recreate private camera/residency policy inside MassNavigation.
- Evidence:
  - `gitbook/reference/mass-navigation-user-book.md`
  - `gitbook/reference/obstacle-authoring.md`
  - `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/game.json`
- Recommendation: keep obstacle authoring on map/template ECS components, camera profiles in the Camera ConfigPipeline, presentation culling in `game.json`, and navigation streaming under `MassNavigationConfig.streaming`. Applied.

### P1-02 Map-scale guide and interactive starter emitted dead duplicate window/streaming fields

- Problem: the guide and generated snippet authored `world.solverWindowWidthCm`, `world.solverWindowHeightCm`, and `world.streamingRadiusCm` beside the active owners.
- Impact: copied snippets could not represent the current strict schema and taught duplicate ownership for the solver window.
- Evidence:
  - `gitbook/reference/map-scale-authoring-guide.md`
  - `gitbook/reference/map-scale-authoring-starter.html`
  - `src/Core/MassNavigation/Runtime/MassNavigationConfig.cs`
- Recommendation: make `solver.fieldWidthCm/fieldHeightCm` the only solver-window size owner and use `streaming.radiusCm`. Applied to prose and generated JSON.
- Additional alignment: generated examples now use the ConfigPipeline `ArrayById` profile envelope and map `Metadata.massNavigation.profileId` binding instead of a process-global config object.

### P1-03 Capability documentation lacked truthful 10K capacity and memory evidence

- Problem: the soak wrapper claimed memory stability while reading fields absent from `summary.json`; the 10K `game.json` files used blanket 128K/256K presentation capacities without relating them to scenario occupancy.
- Impact: reviewers could see `n/a` memory columns and a high-performance label while the headless process retained about 1.85GB managed heap.
- Evidence:
  - `scripts/acceptance/run-mass-navigation-large-world-uat.ps1`
  - `src/Tools/Ludots.Launcher.Evidence/LauncherEvidenceRecorder.cs`
  - `artifacts/acceptance/mass-navigation-issue-642/performance-comparison.md`
- Recommendation: emit process-wide GC/heap/working-set metrics plus solver-owned capacity-growth counters, run a timing-disabled sustained-order interval, and bound presentation capacities from measured occupancy. Applied and verified by the canonical 60-second run.

### P1-04 Long-lived Mod consumers lacked the map-bound runtime contract

- Problem: Formation, Road, and capability-standard 10K systems could remain registered across map push/pop or unload/reload while caching the first simulation or accepting any active MassNavigation runtime.
- Impact: suspended entities could keep receiving input or route updates, and a reloaded map could write through a destroyed simulation instance.
- Evidence:
  - `gitbook/reference/mass-navigation-formal-chain.md`
  - `gitbook/reference/mass-navigation-user-book.md`
  - `src/Core/MassNavigation/Runtime/MassNavigationRuntimeBinding.cs`
  - `src/Tests/GasTests/RoadNetworkShowcaseTests.cs`
  - `src/Tests/PresentationTests/FormationCapabilityShowcaseContractTests.cs`
- Recommendation: require long-lived consumers to resolve the focused simulation through `MassNavigationRuntimeBinding`, gate Mod sidecars to their owning map, and exclude `SuspendedTag` entities from their queries. Applied to Formation, Road, and the capability-standard 10K input path.

### P2-01 Foundation README conflated navigation streaming with view residency

- Problem: the asset table claimed `MassNavigationConfig.json` owned view residency.
- Impact: subsystem ownership was ambiguous even though performer/camera policy belongs to shared presentation infrastructure.
- Evidence:
  - `mods/capabilities/navigation/MassNavigationMod/README.md`
  - `src/Core/Presentation/PresentationRuntimeConfig.cs`
- Recommendation: describe MassNavigation streaming as navigation policy and keep performer visibility ownership in presentation. Applied.

## Fix Order

1. Remove invalid authoring instructions and generated fields from the beginner guide and interactive starter. Completed.
2. Align foundation README terminology with the runtime ownership boundary. Completed.
3. Enforce the map-bound runtime contract for every long-lived MassNavigation consumer. Completed with behavioral regression coverage.
4. Keep the canonical capability-standard 10K command synchronized with `launcher.config.json` / `launcher.presets.json`. Completed in the acceptance wrapper.
5. Keep recorder fields, wrapper reads, and 10K presentation capacity bounds locked by `MassNavigationEvidenceContractTests`. Completed.

## Residual Risks

- Board-owned loaded-chunk integration and map-bound profiles are now implemented; documentation must be rechecked if their public authoring surface changes later.
- Process-wide allocation and working-set evidence cannot attribute every byte to MassNavigation. The acceptance report labels this limitation and uses the solver-owned storage-allocation counter for the narrow capacity-growth claim.
- The isolated final 10K process still retains about 1.153GB managed heap with 30,009 performers; further reduction needs measured performer/ECS attribution, not another blanket buffer cut.
