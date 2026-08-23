# Scenario: presenter-timer-hit-flash

## Header
- scenario name: SC2-style hit-flash sequencing via presenter named timer primitives (TimerSet / TimerExpired / TimerKill)
- build/version: local PresentationTests, real JSON config pipeline (PresenterDefinitionConfigLoader)
- seed/map/clock: deterministic fixture / in-memory world / render dt 0.125s per tick
- execution timestamp: 2026-08-23T10:54:56.8915350+00:00

## Timeline
- [T+001] 单位 A 的受击闪黄 presenter 上线（stable id 1）
- [T+001] 单位 B 的受击闪黄 presenter 上线（stable id 2）
- [T+002] 单位 A 受击 → TimerSet accept.flash（0.6s）启动
- [T+002] 单位 A 闪黄参数 = 1（受击高亮）
- [T+002] 单位 B 受击 → TimerSet accept.flash（0.6s）启动
- [T+002] 单位 B 闪黄参数 = 1（受击高亮）
- [T+004] 单位 B 的 Suppressed tag 丢失 → TimerKill "*" 清掉实例全部 timer（打断，不会再有 TimerExpired）
- [T+004] 单位 B 闪黄参数 = 0（复原）
- [T+007] 单位 A 的 accept.flash 到时 → TimerExpired（正常复原窗口，当帧进规则）
- [T+007] 单位 A 闪黄参数 = 0（复原）

## Outcome
- success/failure decision: success
- failed assertions: none
- reason codes: happy_path_expiry, taglost_interrupt_no_expiry, per_instance_isolation

## Summary Stats
- TimerExpired events: 1 (unit A only, at T+007)
- TimerSet commands: 2 | TimerKill commands: 1 (unit B, wildcard, at T+004)
- SetParam flash.yellow: 4 (A: 1→0 via expiry; B: 1→0 via interrupt)
- timer table high-water: 2; final: 0
