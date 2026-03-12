# Interaction Showcase Stage Handoff

- date: `2026-03-12`
- branch: `feat/mod-interaction-showcase`
- stage commit: `94f5277`

## Scope closed in this stage

- Added core `moveTo` runtime execution path for world-centimeter movement orders and wired multi-select queue/stop flows into the production order pipeline.
- Fixed GAS caller-parameter preservation for line/vector/displacement paths so blink-dash-rune and displacement presets resolve from preserved origin/target params even when exec state is missing.
- Moved live input sampling ahead of simulation in `GameEngine.Tick()` so authoritative input snapshots are current-frame instead of one-frame late.
- Added `AbilityAimOverlayPresentationSystem` in `CoreInputMod` so aim/indicator previews are emitted during presentation instead of being lost from simulation-time buffer writes.
- Hardened production acceptance for interaction showcase, including indicator-mode baseline accounting against the persistent selection ring overlay.
- Closed the planned GAS form-routing gap with `AbilityFormSetRegistry` + `AbilityFormSetConfigLoader` + `AbilityFormRoutingSystem`, keeping routing tag-driven instead of introducing a parallel condition runtime.
- Split effective slot resolution into layered `Granted > Form > Base`, so form routing stays isolated from future transient grants while `AbilitySystem`, `AbilityExecSystem`, `ContextScoredOrderResolver`, and indicator bridging all consume the same slot boundary.

## Green commands

```powershell
dotnet test src\Tests\GasTests\GasTests.csproj -c Release /m:1 --filter "FullyQualifiedName~InteractionShowcasePlayableAcceptanceTests.InteractionShowcase_PlayableFlow_WritesAcceptanceArtifacts"
```

```powershell
dotnet test src\Tests\GasTests\GasTests.csproj -c Release /m:1 --filter "FullyQualifiedName~InputOrderAbilityAuditTests|FullyQualifiedName~ContextScoredResolverTests|FullyQualifiedName~AbilityFormRoutingSystemTests|FullyQualifiedName~InputOrderConvergenceValidationTests|FullyQualifiedName~InteractionShowcasePlayableAcceptanceTests.InteractionShowcase_PlayableFlow_WritesAcceptanceArtifacts"
```

```powershell
dotnet test src\Tests\GasTests\GasTests.csproj -c Release /m:1 --filter "FullyQualifiedName~AuthoritativeInputConvergenceTests|FullyQualifiedName~InputOrderConvergenceValidationTests|FullyQualifiedName~InteractionShowcasePlayableAcceptanceTests|FullyQualifiedName~ProductionAllModsValidationTests|FullyQualifiedName~TargetResolver_LineSearch_UsesPreservedCallerParams_WhenExecIsMissing|FullyQualifiedName~BuiltinHandler_ApplyDisplacement_UsesPreservedCallerTargetPoint_WhenExecIsMissing|FullyQualifiedName~InteractionShowcase_ArcweaverRuneBurst_AfterBlinkDash_DirectVectorCast_DamagesTarget|FullyQualifiedName~InteractionShowcase_ArcweaverRuneBurst_AfterBlinkDash_ChordInput_DamagesTarget|FullyQualifiedName~InteractionShowcase_ArcweaverRuneBurst_AfterQBlinkGuardDash_ChordInput_DamagesTarget"
```

## Key files for follow-up agents

- `src/Tests/GasTests/Production/InteractionShowcasePlayableAcceptanceTests.cs`
- `src/Tests/GasTests/AbilityFormRoutingSystemTests.cs`
- `mods/CoreInputMod/Systems/AbilityAimOverlayPresentationSystem.cs`
- `src/Core/Engine/GameEngine.cs`
- `src/Core/Gameplay/GAS/AbilityFormSetRegistry.cs`
- `src/Core/Gameplay/GAS/Systems/AbilityFormRoutingSystem.cs`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`
- `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md`
- `artifacts/acceptance/interaction-showcase/battle-report.md`
- `artifacts/acceptance/interaction-showcase/trace.jsonl`
- `artifacts/acceptance/interaction-showcase/path.mmd`

## Next acceptance frontier

Use `artifacts/acceptance/interaction-showcase/feature_coverage_matrix.md` as the stage boundary. It distinguishes branch-proven slices from design-only interaction documents.

Recommended execution order for the next stage:

1. Reuse the shared selection + `moveTo` + `stop` baseline to close more unit-selection variants on the hub map.
2. Reuse the presentation-time aim overlay path for indicator / aim-cast / hold-release / channel flows instead of adding a second preview stack.
3. Extend vector and multi-stage abilities on top of the preserved caller-param path and current `ContextGroup` runtime.
4. Only after each new slice has deterministic hub-map coverage, expand the stress-map scenarios.
