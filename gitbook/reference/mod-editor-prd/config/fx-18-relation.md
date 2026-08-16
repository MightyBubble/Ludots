# fx-17 配置说明 · 关系操作

> 配置写法与行为。第一性需求见 [fx-17 PRD](../prd/fx-18-relation.md)；编辑器需求见 [UXD](../uxd/fx-18-relation.md)；现状见 [reference](../reference/fx-18-relation.md)。

## 1. 示例配置

真实条目（`mods/showcases/rts_demo/RtsDemoMod/assets/GAS/effects.json`，进驻建筑）：

```json
{
  "id": "Effect.Rts.Shared.EnterGarrison",
  "presetType": "Relation",
  "lifetime": "Instant",
  "relation": {
    "operation": "SetParent",
    "subject": "Source",
    "parent": "Target",
    "snapSubjectToParentPosition": true
  }
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `operation` | `SetParent` 挂父（事务内）；`RemoveParent` 摘父；`EnsureLink` 保链（需关系运行时） |
| `subject` | 操作对象槽：`Source` / `Target` / `TargetContext`；禁 `None` |
| `parent` | 父/对端槽：SetParent 与 EnsureLink 必填非 None |
| `snapSubjectToParentPosition` | 仅 SetParent：把 subject 吸附到父的位置 |
| `relationshipType` | 仅 EnsureLink：关系目录中的类型名，须已注册 |

块只允许挂在 `presetType: Relation` + Instant。**现状提示**：RemoveParent/EnsureLink 可通过 loader 校验，但启动计划编译会拒绝含它们的模板（`GAS.EFFECT_PLAN.ERR.UnsupportedOperation`）；可执行组合只有 SetParent（治理跟踪中，见 spec）。

## 3. 文件结构

`assets/GAS/effects.json` 效果条目的 `relation` 块；relationshipType 引用 `Relationships/catalog.json` 声明的类型（目录合同见 rel-01）。

## 4. 运行时加载效果

loader 校验操作与槽位合同、解析 relationshipType 为类型 id；运行期 SetParent 走事务内 StageSetParent（可选吸附），RemoveParent/EnsureLink 直改世界并依赖 RelationshipRuntime。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 非 Relation 带块 / Relation 缺块 / 非 Instant | 启动失败，指明效果 |
| 未知 operation、subject 为 None、parent 违例 | 启动失败，列合法值 |
| snap / relationshipType 越权使用 | 启动失败，指明"仅 operation=…" |
| relationshipType 未注册 | 启动失败，指明名字 |
| 模板含 RemoveParent/EnsureLink | 启动计划编译失败（现状） |
| 运行期 subject/parent 实体失效 | 抛错带实体 id |

## 6. 实例

- 进驻建筑：`mods/showcases/rts_demo/RtsDemoMod/assets/GAS/effects.json`（EnterGarrison）

**相关文档**：[fx-17 PRD](../prd/fx-18-relation.md) · 见 rel-01（关系目录，第二期）
