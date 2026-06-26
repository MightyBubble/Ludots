# Time Flow

TimeFlow provides domain-level time scaling without introducing a second simulation scheduler. It lives under `src/Core/Engine/TimeFlow/` and is applied by `GameEngine` through the existing main loop and explicit domain policies.

## Current Domains

| Domain | Owner | Notes |
|---|---|---|
| `simulation` | Core main loop | Root simulation cadence |
| `gas` | GAS clock policy | Ability/order/effect cadence |
| `physics2d` | Physics clock policy | Physics integration cadence |

The retired navigation execution domain has been removed. Navigation-domain movement now runs through MassFlow execution and the normal physics/GAS cadence as wired by the active runtime.

## Rules

- Domain ids are strict strings registered in `TimeFlowDomainIds`.
- Missing or wrong-case domains fail through normal config validation.
- No removed domain should be reintroduced for compatibility.

## Verification

Use `src/Tests/TimeFlowCoreTests/` for domain registration, scale changes, and config validation.
