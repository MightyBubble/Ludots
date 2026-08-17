# fx-19 配置说明 · 视野揭示

> 配置写法与行为。第一性需求见 [fx-19 PRD](../prd/fx-19-vision.md)；编辑器需求见 [UXD](../uxd/fx-19-vision.md)；现状见 [reference](../reference/fx-19-vision.md)。

## 1. 示例配置

真实 JSON（测试内嵌 `src/Tests/GasTests/Integration/CoreHeroSkillInfraTests.cs`，hero_reveal：周期揭示 + 移除衰减；仓库 mod 暂无使用）：

```json
{
  "id": "hero_reveal",
  "presetType": "None",
  "lifetime": "After",
  "duration": { "durationTicks": 30, "periodTicks": 5, "clockId": "FixedFrame" },
  "phaseGraphs": {
    "OnApply":  { "main": "Graph.Vision.RevealArea" },
    "OnPeriod": { "main": "Graph.Vision.RevealArea" },
    "OnRemove": { "main": "Graph.Vision.DecayRevealArea" }
  },
  "revealArea": {
    "radius": 600,
    "scope": "team",
    "layers": ["ground", "detection"],
    "memoryTtlTicks": 90,
    "detectionStrength": 2
  }
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `radius` | 揭示圆半径 cm，必须 >0 |
| `scope` | 知识作用域名，须在 Progression/scopes 声明并注册 |
| `layers` | 战争迷雾层名列表：至少 1 层、至多上限（事实页/常量 4），逐层须已注册 |
| `memoryTtlTicks` | 揭示记忆保留 tick 数；0 不留记忆 |
| `detectionStrength` | 探测强度 0..255，影响可探测目标的揭示 |

块的挂载合同：任意 presetType 皆可；生命周期限 Instant / After，After 需 `duration.periodTicks > 0` 做周期刷新。**现状提示**：处理器未通过原子域认证，含本块的模板启动计划编译即拒（`GAS.EFFECT_PLAN.ERR.UnsupportedOperation`，治理跟踪中，见 spec）。

## 3. 文件结构

`assets/GAS/effects.json` 效果条目的 `revealArea` 块；scope 引用 `Progression/scopes.json`，layers 引用迷雾层注册表（配置目录见 cfg-04）。

## 4. 运行时加载效果

loader 校验范围/记忆合同并解析 scope 与层名为 id；运行期经相位图调用揭示与衰减运行时写入知识区域（现状调用链未认证，见 spec）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 生命周期非 Instant/After；After 无正周期 | 启动失败，指明效果 |
| radius <=0、层数 0 或超上限、强度越界 | 启动失败 |
| scope 或某层未注册 | 启动失败，指明名字 |
| 含 revealArea 的模板进入计划编译 | 启动失败（现状，Unsupported(Vision)） |
| 运行期揭示中心不可解析 | 跳过本次，不抛错 |

## 6. 实例

- 周期揭示 + 衰减：`src/Tests/GasTests/Integration/CoreHeroSkillInfraTests.cs`（hero_reveal，测试内嵌 JSON）

**相关文档**：[fx-19 PRD](../prd/fx-19-vision.md) · 见 infra-03（视野基建，第二期）
