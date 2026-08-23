# Nav Bake Policy Contract

Scenario: a Grid board declares continuous height, board logic classification, authored static obstacles, and runtime obstacles as separate roles.

Build: `codex/nav-bake-policy` at latest `origin/main` plus the policy slice

## Timeline

- Board policy parsed from map JSON.
- Policy validator accepted the compatible continuous-height + board-logic combination.
- Missing selected height input was rejected before a bake started.
- NodeGraph policy with NavMesh roles was rejected.
- The same CDT bake context sampled the declared `.vhtm` directly; NavTile vertex Y changed while the logic terrain stayed the classification source.
- Recast, CDT, Editor Bridge, CLI, and Runtime now bind the same continuous-height input and obstacle set contract.
- Physics2D authoring supplies Polygon, Box, Circle, and Compound static obstacles to runtime incremental bake when the board selects map entities.

## Outcome

PASS. No projection, silent source substitution, or implicit NodeGraph NavMesh path was introduced.

## Summary

- Policy and NavBake service contract tests: 29 passed.
- Core/ArchitectureTests build: passed with 0 errors (the repository retains its existing warning baseline).
