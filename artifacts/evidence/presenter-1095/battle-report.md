# Presenter duration → Timer/Rule/Destroy 链路验收

## Header
- scenario: lifecycle.durationSeconds 编译为 TimerSet 计划，唯一销毁链路 TimerSet → TimerExpired → Rule → DestroyPresenter
- build: local PresentationTests, 真实 JSON 配置管线（PresenterDefinitionConfigLoader）
- clock: headless fixture, 生产系统序 Timer → Rules → Runtime, dt=0.125s/拍
- execution timestamp: 2026-09-01T10:27:27.9350799+00:00

## Timeline
- [T+002] command TimerSet: timer='presenter.duration' duration=0.5s instance=1
- [T+006] event TimerExpired: timer='presenter.duration' instance=1
- [T+006] command DestroyPresenter: instance=1
- [T+006] event PresenterDestroyed: instance=1
- [T+008] command TimerSet: timer='presenter.duration' duration=5.0s instance=1
- [T+008] event PresenterDestroyed: instance=1
- [T+008] action TimerKill: cancel duration timer -> immediate destroy funnel
- [T+009] command DestroyPresenter: instance=1
- [T+009] assert chain: TimerSet → TimerExpired → Rule → DestroyPresenter 逐拍成立

## Outcome
- success/failure decision: success
- failed assertions: none
- reason codes: compiled_timerset, rule_destroy_only_entry, timerkill_immediate_destroy, repeat_destroy_idempotent

## Summary Stats
- TimerExpired events: 1 (unit only; victim cancelled)
- PresenterDestroyed events: 2
- final timer table: 0 (no leak)
