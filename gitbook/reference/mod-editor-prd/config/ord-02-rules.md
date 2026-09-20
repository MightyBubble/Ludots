# ord-02 配置说明 · 订单规则与打断

> 配置写法与行为。第一性需求见 [ord-02 PRD](../prd/ord-02-rules.md)；编辑器需求见 [UXD](../uxd/ord-02-rules.md)；现状见 [reference](../reference/ord-02-rules.md)。

## 1. 示例配置

核心 mod 真实规则（`mods/LudotsCoreMod/assets/GAS/order_types.json` 的 `orderRules` 段）节选：

```json
{
  "stop": {
    "orderTypeKey": "stop",
    "blockedActiveOrderTypeKeys": [],
    "interruptsActiveOrderTypeKeys": [ "castAbility", "moveTo", "attackTarget" ]
  },
  "castAbility.End": {
    "orderTypeKey": "castAbility.End",
    "blockedActiveOrderTypeKeys": [],
    "interruptsActiveOrderTypeKeys": [ "castAbility", "castAbility.Start" ]
  }
}
```

教学骨架（阻止 + 打断齐用）：

```json
{ "channelRoot": {
    "orderTypeKey": "channelRoot",
    "blockedActiveOrderTypeKeys": [ "stop" ],
    "interruptsActiveOrderTypeKeys": [ "moveTo" ] } }
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `orderTypeKey` | 规则主体类型；必须已在 `orderTypes` 注册 |
| `blockedActiveOrderTypeKeys` | 实体当前**活动单**属于这些类型时，主体新单直接被规则拒收 |
| `interruptsActiveOrderTypeKeys` | 主体新单可以打断这些类型的**活动单**；查表无此边 = 不可打断 |

- 两数组必填、可为空；单数组上限 8 条；同条规则内禁止重复引用。
- 同型打断不走这张表，只看类型自身的 `canInterruptSelf`（见 ord-01）。
- 被打断的活动单以"取消·被打断"终态收场；打断激活时是否清空同型队列由 `clearQueueOnActivate` 决定。

## 3. 文件结构

规则段与类型段同居 `GAS/order_types.json` 的 `orderRules` 根字段（同文件同合并路径，见 ord-01）；一类型至多一条规则，键即 `orderTypeKey`。

## 4. 运行时加载效果

规则在类型全部注册后加载编译为快查表；提交路径按"阻止表 → 打断表"两步消费（裁决时序见 ord-03）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 引用未注册类型 | 启动失败，指明规则与类型名 |
| 同条规则重复引用同一类型 / 单数组超 8 条 | 启动失败 |
| 规则主体与键不一致或缺段 | 启动失败 |

## 6. 实例

- 全量真实规则：`mods/LudotsCoreMod/assets/GAS/order_types.json` `orderRules` 段（6 条）

**相关文档**：[ord-02 PRD](../prd/ord-02-rules.md) · [ord-01 配置说明](ord-01-types.md) · [ord-03 配置说明](ord-03-pipeline.md)
