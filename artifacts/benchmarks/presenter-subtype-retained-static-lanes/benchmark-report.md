# Presenter Subtype Retained/Static Lane Benchmark

- subtypes: `Decal`, `VFX`, `Spline`, `GroundOverlay`, `SurfaceSource`
- production path: `PresenterEntityRuntime` -> `PresenterEmitSystem` -> `StableDrawCache` / `PresentationRequestBuffer`
- steady-state requirement: retained subtypes emit no unchanged requests; static subtypes do not rewrite stable cache

| Total | Each subtype | Create | First Emit | First Requests | Stable Cache | Avg Tick | P95 Tick | Avg Emit | Max Steady Requests | Stable Cache Min/Max | Content Revision First/Max |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 10000 | 2000 | 16.4184 ms | 3.9942 ms | 6000 | 4000 | 0.0136 ms | 0.0148 ms | 0.0135 ms | 0 | 4000 / 4000 | 4000 / 4000 |
