# Presenter Subtype Retained/Static Lane Benchmark

- subtypes: `Decal`, `VFX`, `Spline`, `GroundOverlay`, `SurfaceSource`
- production path: `PresenterEntityRuntime` -> `PresenterEmitSystem` -> `StableDrawCache` / `PresentationRequestBuffer`
- steady-state requirement: retained subtypes emit no unchanged requests; static subtypes do not rewrite stable cache

| Total | Each subtype | Create | First Emit | First Requests | Stable Cache | Avg Tick | P95 Tick | Avg Emit | Max Steady Requests | Stable Cache Min/Max | Content Revision First/Max |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 10000 | 2000 | 191.1103 ms | 56.1255 ms | 6000 | 4000 | 0.2027 ms | 0.2260 ms | 0.2022 ms | 0 | 4000 / 4000 | 4000 / 4000 |
