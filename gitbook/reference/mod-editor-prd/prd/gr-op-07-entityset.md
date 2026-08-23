# gr-op-07 · 节点：实体集查询

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-07-entityset.md)；编辑器需求见 [UXD](../uxd/gr-op-07-entityset.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-07-entityset.md)；editor spec 见 [editor spec](../spec-editor/gr-op-07-entityset.md)；现状见 [reference](../reference/gr-op-07-entityset.md)。

## 1. 定位

Query 图专属的实体集操作：全图取集、从集合键取集、五种过滤（队伍/模板/属性区间/tag 任意/tag 皆无）、按属性排序与聚合。不碰几何，只按身份与数值筛。

## 2. 产品承诺

- **两种建集方式**：全图实体一网打尽，或按集合键取一张已登记的集合。
- **过滤五件**：队伍、实体模板、属性数值区间、命中任一 tag、不命中任何 tag。
- **排序带方向**：按属性排序可升降序（旗标）。
- **聚合出数或出实体**：按属性求和/均值/最大/最小出 Float；最大/最小对应的实体出 Entity。
- **Query 图专属**：整族十四件只进 Query 图；线性图与 Script 图的目标选择走 gr-op-06。

## 3. 运行行为

建集节点产出 TargetList；过滤与排序原位收窄/重排；聚合把列表折叠成单值写目的寄存器。属性与 tag 符号在编译期解析。

## 4. 异常承诺

引用未注册的属性/tag/集合键——编译失败并指明节点与符号。非 Query 图使用本族——编译拒绝（kind 掩码外）。空列表聚合按各聚合的空集语义产出，不报错。

**相关文档**：[配置说明](../config/gr-op-07-entityset.md) · [gr-op-06](gr-op-06-spatial.md) · [gr-op-08](gr-op-08-relationship.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
