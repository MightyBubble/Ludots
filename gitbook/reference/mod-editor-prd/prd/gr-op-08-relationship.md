# gr-op-08 · 节点：关系系统

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-08-relationship.md)；编辑器需求见 [UXD](../uxd/gr-op-08-relationship.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-08-relationship.md)；editor spec 见 [editor spec](../spec-editor/gr-op-08-relationship.md)；现状见 [reference](../reference/gr-op-08-relationship.md)。

## 1. 定位

实体间有向关系的图面：写侧建/断链与改度量旗标，读侧问链路问度量，Query 管线按关系建集、过滤、排序、聚合。关系类型、度量、旗标在关系目录中声明。

## 2. 产品承诺

- **写侧五件**：EnsureLink/RemoveLink 建断链，SetMetric/AddMetric 写加度量，SetFlag 置旗标——都是 Effect 图里的动作。
- **读侧三件**：GetMetric 出 Int、HasFlag/HasLink 出 Bool，线性图与 Query 图可用。
- **Query 管线十三件**：出边/入边/互链/点对建集，按度量区间或旗标过滤，按度量排序，按度量聚合成数或成实体。
- **效果组合的门**：写侧五件在效果组合编译（把图折叠进效果执行计划）时按关系域 fail-closed 拒绝——关系写入只属于显式图创作。

## 3. 运行行为

写侧在 Effect 事务内落关系存储；读侧与管线只读。关系类型与度量符号在编译期经关系目录解析；管线输出 TargetList 或标量。

## 4. 异常承诺

引用目录外的关系类型/度量/旗标——编译失败并指明节点与符号。效果组合折叠遇到写侧节点——编译拒绝（关系域 fail-closed）。非 Query 图用管线、非 Effect 图用写侧——kind 掩码拒绝。

**相关文档**：[配置说明](../config/gr-op-08-relationship.md) · [gr-op-07](gr-op-07-entityset.md) · [rel-01](rel-01-catalog.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
