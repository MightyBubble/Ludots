# Presenter Named Timer Benchmark

- scenario: 30k presenter instances x 1 / 3 active named timers, staggered expiry, expired timers re-chained every frame
- production path: `PresenterTimerSystem.Tick` -> `PresentationEventStream(TimerExpired)` -> rule consumption (stream cleared per frame)
- steady-state requirement: 0 alloc, no gen0 GC, constant timer population

| Scenario | Timers | Avg Tick | P95 Tick | Max Tick | Avg Expired/Frame | Alloc Bytes |
|---|---:|---:|---:|---:|---:|---:|
| 30k x 1 | 30000 | 0.3047 ms | 0.6785 ms | 1.6935 ms | 700.0 | 0 |
| 30k x 3 | 90000 | 0.9478 ms | 2.0121 ms | 2.4806 ms | 1260.0 | 0 |
