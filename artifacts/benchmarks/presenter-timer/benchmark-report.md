# Presenter Named Timer Benchmark

- scenario: 30k presenter instances x 1 / 3 active named timers, staggered expiry, expired timers re-chained every frame
- production path: `PresenterTimerSystem.Tick` -> `PresentationEventStream(TimerExpired)` -> rule consumption (stream cleared per frame)
- steady-state requirement: 0 alloc, no gen0 GC, constant timer population

| Scenario | Timers | Avg Tick | P95 Tick | Max Tick | Avg Expired/Frame | Alloc Bytes |
|---|---:|---:|---:|---:|---:|---:|
| 30k x 1 | 30000 | 0.1879 ms | 0.3753 ms | 1.2658 ms | 700.0 | 0 |
| 30k x 3 | 90000 | 0.4952 ms | 0.9701 ms | 1.8270 ms | 1260.0 | 0 |
