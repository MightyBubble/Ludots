# ord-01 配置说明 · 订单类型

> 配置写法与行为。第一性需求见 [ord-01 PRD](../prd/ord-01-types.md)；编辑器需求见 [UXD](../uxd/ord-01-types.md)；现状见 [reference](../reference/ord-01-types.md)。

## 1. 示例配置

核心 mod 真实表（`mods/LudotsCoreMod/assets/GAS/order_types.json`）节选：

```json
{
  "orderBlackboardKeys": { "Attack.MovePosition": true, "Attack.TargetEntity": true },
  "orderTypes": { "castAbility": {
    "orderTypeId": 100, "label": "Cast Ability", "priority": 100,
    "maxQueueSize": 3, "queuedModeMaxSize": 8, "sameTypePolicy": "Queue", "queueFullPolicy": "DropOldest",
    "bufferWindowMs": 500, "pendingBufferWindowMs": 400, "canInterruptSelf": false,
    "allowQueuedMode": true, "clearQueueOnActivate": true, "validationGraph": "none",
    "spatialBlackboardKey": "Cast.TargetPosition", "entityBlackboardKey": "Cast.TargetEntity",
    "intArg0BlackboardKey": "Cast.SlotIndex", "instantComplete": false } },
  "orderRules": { }
}
```

教学骨架（瞬时完成类型，五键组见 ord-04）：

```json
{ "orderBlackboardKeys": { "My.Point": true },
  "orderTypes": { "myPing": { "orderTypeId": "myPing", "label": "Ping", "instantComplete": true,
    "spatialBlackboardKey": "My.Point", "entityBlackboardKey": "none", "intArg0BlackboardKey": "none",
    "persistentStoredTarget": { "targetKindKey": "My.Kind", "targetPositionKey": "My.Point",
      "targetEntityKey": "My.Entity", "hexQKey": "My.Q", "hexRKey": "My.R" } } }, "orderRules": { } }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `orderTypeId` / `label` | 编号：整数（>0 且 <256，重复失败）或与条目 key 逐字相同的语义串（按字典序从 1 取最小空闲）；展示名必填禁空白 |
| `maxQueueSize` / `queuedModeMaxSize` / `priority` | 同型排队深度（均 0..8）与实体缓冲排序权重（见 ord-03） |
| `sameTypePolicy` / `queueFullPolicy` / `bufferWindowMs` / `pendingBufferWindowMs` | 同型裁决：排队/替换/忽略；队满：丢最老/拒新单；入队与待定缓冲窗（≤0 永不过期） |
| `canInterruptSelf` / `allowQueuedMode` / `clearQueueOnActivate` | 同型可否自打断；可否排队态；激活即清同型队列 |
| `spatialBlackboardKey` / `entityBlackboardKey` / `intArg0BlackboardKey` | 订单参数的黑板落点；语义串必填（`"none"` 表示无），须已注册 |
| `validationGraph` / `instantComplete` / `persistentStoredTarget` | 准入校验图（`"none"` 不校验）；瞬时完成开关；五键组当且仅当瞬时真值时提供（见 ord-04） |
| 键段 `orderBlackboardKeys` | 键值必须 `true`；内置键（见 ord-04）禁重声明；自定义键编号从 10000 起 |

## 3. 文件结构

`GAS/order_types.json`（目录声明的合并路径；根资产现状无此文件，数据由核心 mod 贡献。根对象三段：`orderBlackboardKeys` / `orderTypes` / `orderRules`，前两段可空但必须显式存在）。

## 4. 运行时加载效果

装配期加载器清空订单类型与规则两注册表后重建：注册黑板键 → 编号分配 → 类型 → 规则；随后引擎校验常量声明的必备类型已注册。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 三段任一缺席 / `orderTypes` 为空 / 必备类型未注册 | 启动失败，指明文件与段名/常量来源 |
| 编号重复、≤0、≥256、语义串与条目 key 不一致、五键组绑定非法 | 启动失败 |
| 三键组引用未注册键或写成数字、内置键重声明、键段值非 `true` | 启动失败 |

## 6. 实例

- 全量真实表：`mods/LudotsCoreMod/assets/GAS/order_types.json`（9 类型）；必备编号常量：`mods/LudotsCoreMod/assets/game.json` `constants.orderTypeIds`

**相关文档**：[ord-01 PRD](../prd/ord-01-types.md) · [ord-02 配置说明](ord-02-rules.md) · [ord-04 配置说明](ord-04-blackboard.md)
