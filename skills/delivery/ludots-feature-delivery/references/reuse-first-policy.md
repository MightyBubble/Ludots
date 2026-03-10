# Reuse-First Policy

## Goal

Prevent per-mod or per-feature reinvention of engine/runtime foundations.

## Rules

1. Reuse existing core abstractions before introducing new stacks.
2. Add extension points in Core when needed; do not duplicate full pipelines in Mods.
3. Reuse existing config catalogs/loaders unless contract mismatch is proven.
4. Reuse existing query/budget/telemetry paths for new gameplay logic.

## Authoritative Input / Camera / Order SSOT

1. Live `PlayerInputHandler` is a render-frame sampler only; it is not the fixed-step gameplay input interface.
2. Fixed-step systems in `SystemGroup.InputCollection` must consume `CoreServiceKeys.AuthoritativeInput`, produced by `InputRuntimeSystem` + `AuthoritativeInputSnapshotSystem`.
3. Input, order mapping, selection/response, and core logic camera must advance on the same fixed-step timeline when they participate in gameplay authority.
4. Camera transitions belong to `CameraManager` / `VirtualCameraBrain`; `CameraPresenter` only interpolates logic state to render state, and adapters only apply render state.
5. Authoritative input consumers belong under `src/Core/Input/...` or the owning core subsystem, not under `src/Core/Presentation/...`.

## Red Flags

- new custom event loop in mod layer
- duplicated config parsing pipeline
- duplicated input/order processing path
- duplicated simulation scheduler
- direct `PlayerInputHandler` reads inside fixed-step gameplay systems
- ad-hoc camera tween logic in presenter, adapter, or mod triggers
- presentation systems registered to handle authoritative selection / order / response input

## Decision Record

When adding new infrastructure, record:
- why existing infra is insufficient
- impacted layers
- migration and deprecation plan

