# GAS Composition Gate — Field Hierarchy（分组名单 + 父子边 + 查询时链）

Issue: #1178（分支 codex/issue-1177-mapfield-regions，随区域线一并交付）

## 1. Core judgment

**PASS。** 本切片新增一条**装载期名单挂边**（`RegionHierarchyBuilder`）与**查询时链遍历**（`TryResolveChain`/`TryEnumerateGroupMembers`），不新增 effect preset、profile enum、BuiltinHandler 或触发器算子。分组实体是普通实体 + `RegionGroupCm`，随 `MapEntity` 清理；层级关系只存在于名单资产与 `ChildOf` 边（复用 `RelationOps`，环检测/唯一父语义由它保证）。粗层投影按「查询时求值」档位实现——不存父层格子面，因此不存在可写投影面，也不存在会失步的派生存储。

## 2. Layer assignment

| 能力 | Layer | 实现载体 |
|---|---|---|
| 名单装载 | Core 配置管线 | `FieldHierarchyConfigLoader`（ArrayById by parent，跨碎片后载覆写 children） |
| 分组实体物化 | Core 系统 | `RegionHierarchyBuilder`（无格子的 parent 物化为 `RegionGroupCm` 实体） |
| 父子边 | 既有关系基建 | `RelationOps.SetParent`（环检测 + 恰一父） |
| 点查链/成员枚举 | Core 查询 | `TryResolveChain` / `TryEnumerateGroupMembers`（读 `ChildOf`/`ChildrenBuffer`） |

## 3. Reuse list

- 配置管线：`ConfigPipeline.RequireEntry` + `MergeArrayByIdFromCatalog` + `StrictJsonOptions`（同 #1175 装载器母本）。
- 关系基建：`RelationOps`、`ChildOf`、`ChildrenBuffer`（不新建层级结构）。
- 区域物化：#1177 的 `RegionEntityIndex`（key → 实体解析）。

## 4. New Layer 0 ops

无。无新 graph 节点/effect 步骤；触发图消费过境事件走 #1177 的既有通道。

## 5. Transaction boundary

- 挂边在地图装载期一次完成（烘焙期档位）；名单非法（双父/缺 key/成环）即地图加载失败。
- 点查与成员枚举是查询时求值，无失效维护问题；改名单 = 重挂边（实体身份不变，链即时跟随）。

## 6. Config SSOT

- `Fields/hierarchies.json`（ArrayById，id 字段 = parent）：`[{"parent": key, "children": [key...]}]`。
- 引擎零业务词：分几级、每级叫什么全部是资产明文；测试用 group.alpha/zone.a1 占位。

## 7. Red flag scan

- [x] 未新增 profile enum / preset 开关 / BuiltinHandler
- [x] 未新建第二套层级结构（复用 RelationOps/ChildOf）
- [x] 未存任何父层格子面（无可写投影面）
- [x] 未引入隐式启发式（缺 key/双父/环一律 fail-closed）
- [x] 零硬编码 id / 零业务语义
