# fx-15 配置说明 · 目标派发

> 配置写法与行为。第一性需求见 [fx-14 PRD](../prd/fx-11-target-dispatch.md)；编辑器需求见 [UXD](../uxd/fx-11-target-dispatch.md)；现状见 [reference](../reference/fx-11-target-dispatch.md)。

## 1. 示例配置

预设引用（champion，真实）与显式映射（moba，真实）：

```json
[
  { "id": "Effect.Champion.Garen.Judgment", "presetType": "Search",
    "lifetime": "Instant", "participatesInResponse": true,
    "targetDispatch": { "payloadEffect": "Effect.Champion.Garen.JudgmentHit" } },
  { "id": "Effect.Moba.Damage.E", "presetType": "Search",
    "lifetime": "Instant", "participatesInResponse": true,
    "targetDispatch": {
      "payloadEffect": "Effect.Moba.Cone.E.Hit",
      "contextMapping": { "payloadSource": "OriginalSource", "payloadTarget": "ResolvedEntity",
                          "payloadTargetContext": "OriginalTarget" } } }
]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `payloadEffect` | 每个通过过滤的候选发射一次该效果；须已注册 |
| `preset` | 引用派发预设表；与 contextMapping 互斥 |
| `contextMapping` | 显式写三槽（payloadSource/payloadTarget/payloadTargetContext） |
| 缺省 | 默认映射：Source=原施法者、Target=解析实体、TargetContext=原目标 |

槽值域四值：OriginalSource / OriginalTarget / ResolvedEntity / OriginalTargetContext。

内建预设表（`assets/GAS/target_dispatch_presets.json`，真实 4 条）：

| 预设 id | Source | Target | TargetContext |
|---|---|---|---|
| SourceToResolved | 原施法者 | 解析实体 | 原目标 |
| TargetToResolved | 原目标 | 解析实体 | 原施法者 |
| ResolvedToSource | 解析实体 | 原施法者 | 原目标 |
| SourceToOriginalTargetContext | 原施法者 | 原目标上下文 | 解析实体 |

## 3. 文件结构

`targetDispatch` 是效果模板顶层组件块（fx-04）；预设表 `GAS/target_dispatch_presets.json`（加载序在效果表之前）。

## 4. 运行时加载效果

loader 校验互斥、载荷注册与槽值域；运行期内建链为：纯查询处理器写候选数 → 派发处理器过滤加根预算入命令缓冲 → 二合一处理器合并两步；图路径等价走运行时 API（事务内随提交发布）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| preset 与 contextMapping 同写 | 启动失败 |
| payloadEffect 未注册 | 启动失败 |
| 槽值域外 / 预设字段缺失 | 启动失败 |

## 6. 实例

- `assets/GAS/target_dispatch_presets.json`（4 条内建预设）
- `mods/showcases/moba_demo/MobaDemoMod/assets/GAS/effects.json`（显式映射用例）

**相关文档**：[fx-14 PRD](../prd/fx-11-target-dispatch.md) · [fx-12 配置说明](fx-10-target-filter.md)
