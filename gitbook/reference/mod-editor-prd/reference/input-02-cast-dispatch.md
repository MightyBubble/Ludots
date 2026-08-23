# input-02 reference · 施法派发档案

> 现状参考。第一性需求见 [input-02 PRD](../prd/input-02-cast-dispatch.md)；配置说明见 [input-02 配置说明](../config/input-02-cast-dispatch.md)。

## 1. 现状快照

- 档案形状：`profiles[].id` / `selector{kind all|topN|cycle, n, advanceOn}` / `scorer{kind:"utility", considerations:["distanceToTarget:invert"]}` / `router{kind parallel|sequential, sharedOrderId}`。
- 安装：引擎装配期注册（GameEngine）。
- 消费：意图路由出组后 `SelectDispatchTargets` 选演员并决定共享单号/顺序；`dispatchProfileId` 取自 `ControlSchemeRuntime.ActiveDefault`。
- 根资产三档案：`dispatch.all_together`（all+parallel 共享）/ `dispatch.one_by_one`（cycle，advanceOn `orderAccepted`，sequential）/ `dispatch.nearest_top_n`（topN n=3 + 距离反转评分 + parallel 共享）。
- 缺陷：cycle 的推进入口 `NotifyAdvance` 生产零调用（仅测试），`one_by_one` 退化为永远第一位演员。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 档案字段形状 | src/Core/Input/Interaction/CastDispatchProfile.cs:145-186 |
| 安装点 | src/Core/Engine/GameEngine.cs:1398 |
| 选人消费 | src/Core/Input/Orders/InputOrderMappingSystem.cs:1736-1748 |
| 轮转推进入口（无生产调用） | src/Core/Input/Interaction/CastDispatchProfileRegistry.cs:163-176 |
| 根资产 | assets/Input/cast_dispatch_profiles.json |

**相关文档**：[input-02 PRD](../prd/input-02-cast-dispatch.md) · [input-01 reference](input-01-command-intent.md)
