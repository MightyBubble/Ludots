# misc-02 配置说明 · 物品与兑换

> 配置写法与行为。第一性需求见 [misc-02 PRD](../prd/misc-02-items-exchange.md)；编辑器需求见 [UXD](../uxd/misc-02-items-exchange.md)；现状见 [reference](../reference/misc-02-items-exchange.md)。

## 1. 示例配置

FourX 关联 showcase 真实三件 + 兑换（`mods/showcases/fourx_association/FourXAssociationShowcaseMod`，节选）：

```json
[ { "id": "fourx_association_shape_1x1", "rows": ["X"], "rotatable": false } ]
```

```json
[ { "id": "fourx_association_city_stash", "purpose": "Stash", "width": 2, "height": 1 } ]
```

```json
[ { "id": "fourx_association_supply", "displayName": "Signal Supply", "shape": "fourx_association_shape_1x1",
    "maxStack": 999, "tags": ["FourXAssociation.Supply"] } ]
```

```json
[
  {
    "id": "fourx_association.trade_supply",
    "relationshipRequirements": [
      { "source": "Source", "target": "Target", "type": "FourXAssociation.Diplomacy",
        "flag": "FourXAssociation.TradePact", "flagValue": true }
    ],
    "inputs": [ { "kind": "AttributeCost", "actor": "Source", "attribute": "Gold", "quantity": 5 } ],
    "outputs": [ { "kind": "CreateItem", "actor": "Source", "purpose": "Stash", "item": "fourx_association_supply", "quantity": 1 } ]
  }
]
```

外交贸易 showcase 的简单物品（DiplomacyTradeGateShowcaseMod 的 Items 定义（教学骨架引用））：

```json
[ { "id": "trade_gate_credit", "displayName": "Trade Credits", "shape": "trade_gate_shape_1x1", "maxStack": 999, "tags": ["Currency.TradeGate"] } ]
```

## 2. 字段与行为

| 表 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| shapes | `rows` | 网格掩码（X=占格）；多行多列即异形 |
| shapes | `rotatable` | true 时自动生成 4 个旋转 |
| layouts | `purpose` | 容器用途（Stash/装备等），兑换产出按 purpose 定容器 |
| layouts | `width`/`height`/`blockedRows` | 容器格数与封禁行 |
| layouts | `namedSlots`/`grantsEquipmentBonuses` | 专属槽位与装备加成开关 |
| definitions | `shape` | 必填形状引用 |
| definitions | `maxStack` | 堆叠上限；≤0 归 1 |
| definitions | `tags`/`allowedNamedSlots` | 分类与可放槽位 |
| definitions | `equipEffects`/`abilityGrants` | 装备效果与技能授予 |
| definitions | `mountedContainers` | 随身容器 |
| operations | `relationshipRequirements` | 关系旗标门槛（不满足即拒） |
| operations | `inputs`/`outputs` | 投入（AttributeCost 等）与产出（CreateItem 等） |

## 3. 文件结构

目录条目 `Items/*`（根数据为空） 三件（shapes/layouts/definitions，ArrayById）+ 目录条目 Exchange/operations.json（根数据为空）。**四张根表全空占位（D3）**，内容全部下沉 mod。

## 4. 运行时加载效果

Items 三表依序加载并互查引用；Exchange 两段式（LoadIds 先收集再解析）依赖 Item 与 Relationship 注册表。**生效级别：重启**。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 定义缺 shape 或引用未注册形状 | 启动失败 |
| 布局/容器引用非法 | 启动失败 |
| 兑换引用未注册物品/关系类型/旗标 | 启动失败，指明操作 |
| 操作缺 inputs/outputs 结构 | 启动失败 |

## 6. 实例

- `mods/showcases/fourx_association/FourXAssociationShowcaseMod`、Exchange/operations.json
- `mods/showcases/diplomacy_trade_gate/DiplomacyTradeGateShowcaseMod`
- `mods/showcases/gold_market/GoldMarketShowcaseMod/assets/Exchange/operations.json`

**相关文档**：[misc-02 PRD](../prd/misc-02-items-exchange.md) · [misc-01 配置说明](misc-01-progression.md)
