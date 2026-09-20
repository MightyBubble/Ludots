# gr-op-09 · 节点：聚合与迭代

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-09-aggregate.md)；编辑器需求见 [UXD](../uxd/gr-op-09-aggregate.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-09-aggregate.md)；editor spec 见 [editor spec](../spec-editor/gr-op-09-aggregate.md)；现状见 [reference](../reference/gr-op-09-aggregate.md)。

## 1. 定位

把目标列表折叠成答案的三件套：数个数（AggCount）、挑最近（AggMinByDistance）、按下标取一个（TargetListGet）。空间查询与实体集查询共同的收口。

## 2. 产品承诺

- **计数**：AggCount 吃 TargetList 出 Int，空表得零。
- **挑最近**：AggMinByDistance 以击落点为距离基准挑列表最近实体；空表出无效实体。
- **按下标取**：TargetListGet 读下标出实体，同时产出"取到了没有"的有效位；越界不报错，出无效实体加零。
- **广可用**：三件覆盖线性四类图，Count 与 MinByDistance 另进 Query 与 Script 图。

## 3. 运行行为

三件都是单遍扫描列表写单值；TargetListGet 的下标是 Int 值线，有效位落在布尔暂存（flags），实体落目的寄存器。

## 4. 异常承诺

列表引脚类型不符——编译失败。下标越界、空表挑最近——不报错，按上述缺省语义产出；下游用有效位或判空接管。

**相关文档**：[配置说明](../config/gr-op-09-aggregate.md) · [gr-op-06](gr-op-06-spatial.md) · [gr-op-07](gr-op-07-entityset.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
