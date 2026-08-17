# fx-22 配置说明 · 出生下单

> 配置写法与行为。第一性需求见 [fx-22 PRD](../prd/fx-22-submit-order.md)；编辑器需求见 [UXD](../uxd/fx-22-submit-order.md)；现状见 [reference](../reference/fx-22-submit-order.md)。

## 1. 示例配置

真实条目（`mods/showcases/rts_demo/RtsDemoMod/assets/GAS/effects.json`，出生单位走向集结目标）：

```json
{
  "id": "Effect.Rts.Shared.ApplySpawnTargetOrder",
  "presetType": "SubmitOrderFromBlackboard",
  "lifetime": "Instant",
  "submitOrderFromBlackboard": {
    "source": "Source", "target": "Target",
    "storedTarget": {
      "targetKindKey": "Rts.SpawnTarget.Kind", "targetPositionKey": "Rts.SpawnTarget.Position",
      "targetEntityKey": "Rts.SpawnTarget.Entity", "hexQKey": "Rts.SpawnTarget.HexQ",
      "hexRKey": "Rts.SpawnTarget.HexR"
    },
    "pointMoveOrderTypeKey": "moveTo",
    "entityOrderTypeKey": "castAbility",
    "entityOrderIntArg0": 1,
    "submitMode": "Immediate"
  }
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `source` / `target` | 槽位（Source/Target/TargetContext），缺省 Source/Target；source 持黑板，target 是下单执行者 |
| `storedTarget` 五键 | 读黑板存储目标的键名：种类、位置、实体、六角 Q/R——全必填，经黑板键注册表解析 |
| `pointMoveOrderTypeKey` | 点/六角目标时提交的订单类型 key，须已注册 |
| `entityOrderTypeKey` | 实体目标时提交的订单类型 key，须已注册 |
| `entityOrderIntArg0` | 实体订单的整型参数（如技能槽位号） |
| `submitMode` | `Immediate` 立即提交 / `Queued` 排入执行者队列 |

块只允许挂在 `presetType: SubmitOrderFromBlackboard` + Instant。黑板五键的写入方是订单输入协议（见 ord-04/ord-05），本篇只管读出与下单。

## 3. 文件结构

`assets/GAS/effects.json` 效果条目的 `submitOrderFromBlackboard` 块；订单类型 key 引用 `order_types.json`（见 ord-01），黑板键经黑板键注册表声明。

## 4. 运行时加载效果

loader 校验槽位与五键、把订单类型 key 解析为 id；运行期读黑板存储目标，按目标种类组装点移动单或实体单，经正式订单队列入口提交。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 非 SubmitOrderFromBlackboard 带块 / 缺块 / 非 Instant | 启动失败，指明效果 |
| 槽位为 None、五键缺失或未注册 | 启动失败，指明键名 |
| 订单类型 key 未注册 | 启动失败，指明 key |
| submitMode 未知 | 启动失败，列 Immediate/Queued |
| 运行期黑板无目标 | 静默跳过（不下单不抛错） |
| 提交被准入拒绝 | 抛错，带订单 id 与拒绝结果 |

## 6. 实例

- 出生集结：`mods/showcases/rts_demo/RtsDemoMod/assets/GAS/effects.json`（ApplySpawnTargetOrder，与造单位效果同链使用）

**相关文档**：[fx-22 PRD](../prd/fx-22-submit-order.md) · [fx-16 配置说明](fx-16-unit-creation.md)
