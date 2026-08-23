# Nav Bake Policy Contract

Scenario: a Grid board declares continuous height, board logic classification, authored static obstacles, and runtime obstacles as separate roles.

Build: `codex/nav-bake-policy` at latest `origin/main` plus the policy slice

## Timeline

- Board policy parsed from map JSON.
- Policy validator accepted the compatible continuous-height + board-logic combination.
- Missing selected height input was rejected before a bake started.
- NodeGraph policy with NavMesh roles was rejected.

## Outcome

PASS. No projection, silent source substitution, or implicit NodeGraph NavMesh path was introduced.

## Summary

- Contract tests: 3 passed.
- Existing NavBake service contract tests: 25 passed.
- Core/ArchitectureTests build: passed with 0 warnings and 0 errors.
