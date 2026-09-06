# Scenario: issue-1403 render asset residency

## Header

- scenario name: cold render assets rendezvous with map loading
- build/version: `codex/issue-1403` based on `e5ed15a976`
- seed/map/clock: deterministic scripted residency + `presenter_blacksmith_showcase` / normal host frames
- execution timestamp: 2026-08-30

## Scenario Card

- player goal: enter a map without the game thread waiting for cold model or animation loading
- action: start a map whose required presentation assets are not resident
- success: the map stays in loading state while the host keeps producing frames, then becomes playable only after every required asset is resident
- guard branches: missing assets fail with identity and reason; changing maps cancels the old session and releases its map leases

## UAT

Feature: 玩家进入需要现装模型与动画的地图

Scenario: 资产装载期间游戏保持响应，全部就绪后地图才可操作

Given 玩家首次进入一张模型与动画尚未驻留的地图
When 地图数据和实体已经创建，但必需的显示资产仍在装载
Then 玩家仍看到地图处于加载中，地图内实体不可操作
And 游戏主循环继续出帧，不等待模型或动画装载
And 所有必需资产驻留后，地图才进入可玩状态

Scenario: 必需资产损坏时明确终止加载

Given 玩家进入的地图依赖一个缺失动画或骨骼不匹配的蒙皮资产
When 后端确认该资产装载失败
Then 玩家不会进入一个残缺但看似可玩的地图
And 错误同时指出资产编号、来源 URI 与失败原因
And 游戏不会用静态模型或占位内容继续运行

Scenario: 等待资产时切换地图不会串入旧地图结果

Given 地图 A 正在等待必需资产驻留
When 玩家切换到地图 B
Then 地图 A 的等待会话被取消并释放自己的资产租约
And 只有地图 B 可以完成加载
And 地图 A 后续完成的后台工作不会把画面或操作权切回旧地图

## Timeline

- [T+000][manifest] Map entities, recursive template children, presenter create-plan children, and asset swap entries produced one sealed required-asset manifest. Non-render asset bindings were excluded.
- [T+001][poll] Scripted cold load reported `required=2 resident=1 inFlight=1 failed=0`; the map remained pending.
- [T+002][poll] The remaining asset became resident; the gate reported ready and retained one lease per map session.
- [T+003][failure] A required asset failed with its kind, id, render path, URI, and backend reason. Resident and in-flight map requests were released.
- [T+004][cancel] A second session replaced the first one; only the exact old session was canceled, and shared assets kept independent leases.
- [frame=1][raylib] `mapRequired=6 mapResident=0 mapInFlight=6 mapFailed=0`.
- [frame=2][raylib] `mapRequired=6 mapResident=5 mapInFlight=1 mapFailed=0`; Agent Bridge `pumpCount` advanced from `0` to `1`, so the host loop remained alive during cold loading.
- [frame=3..4][raylib] The last animation stayed in flight while more frames completed; no synchronous wait occurred on the render thread.

## Outcome

- success/failure decision: success
- failed assertions: none
- reason codes: `manifest_complete`, `pending_until_resident`, `failure_is_loud`, `session_cancel_isolated`, `skinned_cold_request_nonblocking`, `bone_query_nonblocking`, `residency_diagnostics_visible`

## Summary Stats

- Core map/gate/manifest tests: 13 passed
- Raylib asset-store consumer tests: 4 passed
- Raylib bone transform tests: 8 passed
- selected tests total: 25 passed
- build errors: 0
- observed cold-loading frames: 4
