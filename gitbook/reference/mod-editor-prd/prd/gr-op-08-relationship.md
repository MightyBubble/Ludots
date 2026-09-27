# gr-op-08 · 节点：关系系统

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-08-relationship.md)；编辑器需求见 [UXD](../uxd/gr-op-08-relationship.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-08-relationship.md)；editor spec 见 [editor spec](../spec-editor/gr-op-08-relationship.md)；现状见 [reference](../reference/gr-op-08-relationship.md)。

## 1. 定位

实体间有向关系的图面：写侧建/断链与改度量旗标，读侧问链路问度量，Query 管线按关系建集、过滤、排序、聚合。关系类型、度量、旗标在关系目录中声明。

## 2. 产品承诺

- **建边与断边**：EnsureLink/RemoveLink 可写在 Effect、Script、地图触发图。
- **改度量与改旗标**：SetMetric/AddMetric/SetFlag 写在 Effect 图。效果提交时落库，效果失败时撤回。
- **读侧三件**：GetMetric 出 Int、HasFlag/HasLink 出 Bool。线性图、Query 图、脚本图、地图触发图都能问。同一张效果图里，写完再问，问到的是这次改动。
- **Query 管线十三件**：出边/入边/互链/点对建集，按度量区间或旗标过滤，按度量排序，按度量聚合成数或成实体。
- **效果图真能写**：五件写节点被效果模板引用时，计划按可提交的副作用收进来。效果整段成功才写入关系库；中途失败，关系库回到动笔之前。

## 3. 运行行为

效果图里的建边、断边、改度量、改旗标先记在这场效果里。同一张图随后的询问能看到这次改动。效果整段成功才写入关系库；失败则关系库保持原样。脚本图和地图触发图不走这套提交，建边和断边当场落库。读侧与管线只读。关系类型与度量符号在编译期经关系目录解析；管线输出 TargetList 或标量。

## 4. 异常承诺

引用目录外的关系类型/度量/旗标——编译失败并指明节点与符号。非 Query 图用管线——kind 掩码拒绝。改度量、改旗标写进脚本图或地图触发图——kind 掩码拒绝。建边、断边写进打分、校验、派生或 Query 图——kind 掩码拒绝。派生图里写关系——执行拒绝，关系库不动。

**相关文档**：[配置说明](../config/gr-op-08-relationship.md) · [gr-op-07](gr-op-07-entityset.md) · [rel-01](rel-01-catalog.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
