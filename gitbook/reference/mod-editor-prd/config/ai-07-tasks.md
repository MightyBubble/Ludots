# ai-07 配置说明 · 任务

> 配置写法与行为。第一性需求见 [ai-07 PRD](../prd/ai-07-tasks.md)；编辑器需求见 [UXD](../uxd/ai-07-tasks.md)；现状见 [reference](../reference/ai-07-tasks.md)。

## 1. 示例配置

真实例（utility_autocast 目录条目 `AI/tasks.json`（根数据为空，由 mod 贡献） 三条之一）：

```json
[
  {
    "id": "Task.UtilityAutocast.Attack",
    "Kind": "SubmitOrder",
    "OrderTypeKey": "castAbility",
    "AbilityKey": "Ability.UtilityAutocast.Attack",
    "AbilitySlotIndex": 0,
    "SubmitMode": 0,
    "PlayerId": 0
  }
]
```

教学骨架（组合 Kind 写法——现状行为见 I5）：

```json
[ { "id": "Task.Example.Combo", "Kind": "Sequence" },
  { "id": "Task.Example.MoveArgs", "Kind": "SubmitOrder",
    "OrderTypeKey": "moveTo", "IntArg1": 3 } ]
```

## 2. 字段与行为

| 字段 | 默认 | 这样配会产生什么效果 |
|---|---|---|
| Kind | 必填 | SubmitOrder/Sequence/Parallel/ParallelComplete |
| OrderTypeKey 或 OrderTypeId | 仅 SubmitOrder 必填 | 双写互验：解析不一致报错 |
| SubmitMode | 0（Immediate） | 订单提交模式，枚举外值报错 |
| PlayerId | 0 | 订单归属玩家 |
| AbilityKey 或 AbilityId | 可选 | 任务级技能绑定（回退链一环） |
| AbilitySlotIndex | -1 | 任务级槽位（回退链首环） |
| IntArg0 | -1 | 无槽位时的 I0 负值（≥0 才写进 Order） |
| IntArg1 | 0 | 写进 Order 的 I1 |

组合 Kind 现状（问题 I5）：Sequence=no-op、Parallel/ParallelComplete 仅置 requiredAny 标记不做事——三种行为近乎等价，命名误导；真正提交只靠 SubmitOrder。

## 3. 文件结构

目录条目 `AI/tasks.json`（根数据为空，由 mod 贡献）（ArrayById）。任务条目被 decisions 的 Tasks 数组按 id 引用且须连续区间（ai-04/I3）。无 schema（I10）。

## 4. 运行时加载效果

编译期解析 Kind 与订单/技能双引用；SubmitOrder 强制 OrderType 在场。运行提交链：槽位回退 task→decision→TryFindAbilitySlot；Order.Args.I0=槽位（或 IntArg0≥0 时）、I1=IntArg1、Spatial=目标位置；TryEnqueue 失败=Blocked。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 未知 Kind | 启动失败：Unsupported task kind |
| SubmitOrder 缺 OrderType | 启动失败：must declare OrderTypeKey or OrderTypeId |
| Key/Id 双写不一致 | 启动失败：resolved to X but Y |
| SubmitMode 越界 | 启动失败：Unsupported submit mode |
| TryEnqueue 失败 | 运行期：任务 Blocked，本轮不提交 |

## 6. 实例

- `mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/tasks.json`（真实，3 条 SubmitOrder：Attack/HealBurst/Curse，槽位 0/1/2）

**相关文档**：[ai-07 PRD](../prd/ai-07-tasks.md) · [ai-04 配置说明](ai-04-decisions.md) · [ai-05 配置说明](ai-05-dm-profiles.md)
