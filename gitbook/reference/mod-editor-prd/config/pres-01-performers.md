# pres-01 配置说明 · 表现器档案

> 配置写法与行为。第一性需求见 [pres-01 PRD](../prd/pres-01-performers.md)；编辑器需求见 [UXD](../uxd/pres-01-performers.md)；现状见 [reference](../reference/pres-01-performers.md)。

## 1. 示例配置

核心 mod 真实档案（`mods/LudotsCoreMod/assets/Presentation/presenters.json`，节选）：

```json
{
  "id": "entity_health_bar",
  "bindings": [
    { "paramKey": "worldBar.width", "source": "constant", "constantValue": 50 },
    { "paramKey": "worldBar.height", "source": "constant", "constantValue": 8 }
  ],
  "behaviors": [
    {
      "slot": "body",
      "kind": "AssetBinding",
      "activeByDefault": true,
      "assetBinding": {
        "assetKind": "WorldHud",
        "renderPath": "None",
        "mobility": "Movable",
        "localScale": [50, 8, 1],
        "materialParamKey": "worldBar.fillRatio"
      }
    }
  ]
}
```

教学骨架（带网格行为的最小档案，合成）：

```json
{
  "id": "demo.unit.body",
  "behaviors": [
    { "slot": "body", "kind": "AssetBinding", "assetBinding": { "assetKind": "Mesh", "assetId": "cube" } }
  ]
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 全局命名；实体模板与其他表按此引用 |
| `bindings[].paramKey` | 表现参数键（材质通道、HUD 尺寸等） |
| `bindings[].source` | 取值来源；`constant` 配 `constantValue` 直供常量 |
| `behaviors[].slot` | 行为挂载槽位；同槽互斥 |
| `behaviors[].kind` | 行为种类，须在引擎白名单内 |
| `behaviors[].assetBinding` | 资产绑定：assetKind + assetId/materialParamKey 等；instanced 批次在该块引 `batchAssetId` |
| `behaviors[].activeByDefault` | 生成时是否默认激活 |

行为内联在 `behaviors` 数组；instanced_batches 条目的 `behaviors` 字段是同类结构的另一落点（见 pres-02）。

## 3. 文件结构

目录条目 `Presentation/presenters.json`（根数据为空，由 mod 贡献）（ArrayById、整表可空、支持分片目录，分片表清单见 [事实页](../facts.md)）。分片放 目录条目 `Presentation/presenters/`（分片目录，根数据为空） 下按文件追加。注意：**不存在** `prefabs` 与 `presentation_behaviors` 两张表——组合体用本表 behaviors 表达。

## 4. 运行时加载效果

ArrayById 深合并（后加载 mod 只赢写到的字段，`__delete:true` 可删条目）；加载期把 bindings/behaviors 里的资产、文本 token、模板、效果 id 全部解析为数字 id 存注册表。**生效级别：重启。**

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 条目缺 `id` 或 id 重复合并冲突 | 启动失败，冲突入报告 |
| 行为 `kind` 不在白名单 | 启动失败，指明条目 |
| assetBinding 引用未注册资产/文本 | 启动失败，指明条目与资产名 |

## 6. 实例

- `mods/LudotsCoreMod/assets/Presentation/presenters.json`（血条 WorldHud 档案）
- `mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json`（分片用法）

**相关文档**：[pres-01 PRD](../prd/pres-01-performers.md) · [pres-02 配置说明](pres-02-asset-registry.md)
