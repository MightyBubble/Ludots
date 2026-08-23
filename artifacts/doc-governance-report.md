# Documentation Governance Report

Date: 2026-08-24
Scope: `gitbook/reference/navmesh-authoring-bake-toolchain.md`, `gitbook/reference/logic-terrain-and-topology.md`, `gitbook/reference/nav-domain-configuration-migration-guide.md`, `gitbook/reference/nav-domain-unification-epic-report.md`
Ruleset: Ludots Doc Governance checklist, link validation, SSOT and evidence rules

## Summary

- Total findings: 0
- P0: 0
- P1: 0
- P2: 0
- P3: 0

## Review Result

The four navigation documents now use one role-based bake contract: direct `IVisualHeightmap` geometry, board `LogicTerrainField` classification, and authored/runtime obstacle inputs flow through `NavBakeContext` / `NavBakeService`. References used for the claims resolve to repository paths and contract tests.

Evidence:

- `src/Core/Navigation/NavMesh/Bake/NavContinuousHeightTerrainField.cs`
- `src/Core/Navigation/NavMesh/Bake/NavBakeContext.cs`
- `src/Core/Navigation/NavMesh/Bake/NavBakeHeightmapLoader.cs`
- `src/Core/Engine/GameEngine.cs`
- `src/Tools/Ludots.NavBake.Recast/RecastNavTileBaker.cs`
- `src/Tests/ArchitectureTests/NavBakeServiceContractTests.cs`

## Fix Order

1. Keep the four documents aligned with `NavBakePolicy` and the shared bake route.
2. Add future runtime/Editor evidence links when the corresponding acceptance artifacts land.

## Residual Risks

- The generic `VisualHeightmapLogicTerrainProjection` helper remains for explicit non-Nav callers; it must not be reintroduced into the Nav bake route.
