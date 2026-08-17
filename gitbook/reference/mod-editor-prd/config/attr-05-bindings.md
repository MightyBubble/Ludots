# attr-05 配置说明 · 属性绑定与 Sink

> 配置写法与行为。第一性需求见 [attr-05 PRD](../prd/attr-05-bindings.md)；编辑器需求见 [UXD](../uxd/attr-05-bindings.md)；现状见 [reference](../reference/attr-05-bindings.md)。

## 1. 示例配置

引擎默认绑定表（`assets/GAS/attribute_bindings.json`，ArrayById）现状全量两类共 16 条，物理力两条之一真实原文：

```json
{
  "id": "Bind.Physics.ForceInput2D.X",
  "attribute": "Physics.ForceRequestX",
  "sink": "Physics.ForceInput2D",
  "channel": 0,
  "mode": "Override",
  "scale": 1.0,
  "resetPolicy": "ResetToZeroPerLogicFrame"
}
```

相机行为通道同构（教学骨架——14 条之一，通道与属性名按需替换）：

```json
{ "id": "Bind.CameraBehavior.Zoom", "attribute": "Camera.Behavior.Zoom", "sink": "Camera.BehaviorInput", "channel": 8, "mode": "Override", "scale": 1.0, "resetPolicy": "ResetToZeroPerLogicFrame" }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` / `attribute` | 绑定身份与源属性名（首现注册，见 attr-01） |
| `sink` | 目标 sink 名，只能取注册表内已注册 sink |
| `channel` | sink 内通道号；写 0..255，实际合法域由 sink 复核 |
| `mode` / `scale` | `Override` 替换通道值，`Add` 累加（bool 通道即 OR）；写入前乘有限系数 |
| `resetPolicy` | `ResetToZeroPerLogicFrame`：每逻辑帧消费后源属性归零；`None`：保留 |

注意：全部七字段显式必填，无缺省值；反向（输入→属性）是另一套表 `Input/action_attribute_bindings.json`，与本文同名不同物（A11）。

## 3. 文件结构

`assets/GAS/attribute_bindings.json`（目录登记、ArrayById 合并）。内置 sink 三个：`Physics.ForceInput2D`（通道 0/1）、`Camera.BehaviorInput`（通道 0-14）、`Graph.EdgeCostOverlay`（现状零绑定，A9）。

## 4. 运行时加载效果

加载时逐条校验并注册属性、查 sink；合并后按 id 排序遍历，按 (sink, 声明序) 折叠成组。运行期绑定系统在聚合重算后逐组应用；脉冲策略帧末把源属性归零。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 未知 sink / id 不一致 / 漏字段 | 启动失败，指明条目 |
| channel 超 sink 合法域 | 启动失败（sink 复核） |
| mode / resetPolicy 非法、scale 非有限 | 启动失败 |
| 相机目标实体数量≠1 | 运行失败 |

## 6. 实例

- 全量 16 条与目录登记：`assets/GAS/attribute_bindings.json`；`assets/config_catalog.json`

**相关文档**：[attr-05 PRD](../prd/attr-05-bindings.md) · [attr-03 配置说明](attr-03-aggregation.md)
