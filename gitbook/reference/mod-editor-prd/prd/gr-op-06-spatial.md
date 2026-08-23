# gr-op-06 · 节点：空间查询

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/gr-op-06-spatial.md)；编辑器需求见 [UXD](../uxd/gr-op-06-spatial.md)；引擎实现见 [runtime spec](../spec-runtime/gr-op-06-spatial.md)；editor spec 见 [editor spec](../spec-editor/gr-op-06-spatial.md)；现状见 [reference](../reference/gr-op-06-spatial.md)。

## 1. 定位

按几何形状圈实体的查询族与随后的管线：圆/锥/矩形/线/六边三件七种形状查询，加排序、截断、剔除、层过滤、关系过滤五个流水线节点。产出 TargetList。

## 2. 产品承诺

- **七种形状**：圆（半径立即数）、锥/矩形/线（a b 两个形状参数值线）、六边范围/环/邻域（立即数），全部输出目标列表进管线。
- **容量策略二选一**：RequireComplete 要求装下全部命中否则报错；AllowTruncated 允许截断并给出丢弃计数。
- **管线五件**：稳定排序、数量截断、排除实体、层掩码过滤、关系过滤——一个接一个收窄列表。
- **中心语义说清**：锥/线/矩形以施法者侧为查询中心；其余形状先取目标点、无目标点回退施法者。

## 3. 运行行为

形状查询一次空间检索填 TargetList；流水线节点逐个原位收窄；排序稳定（相等保持原序）。Query 图与 Script 图里 QueryRadius/排序/截断可用，其余九件是线性四类图专属。

## 4. 异常承诺

RequireComplete 装不下命中集——执行失败并报容量；形状参数缺引脚、kind 越界——编译失败。AllowTruncated 的丢弃通过 dropped 计数暴露，不算错误。

**相关文档**：[配置说明](../config/gr-op-06-spatial.md) · [gr-op-09](gr-op-09-aggregate.md) · [fx-09](fx-09-target-query.md) · [节点画廊 wiki](../../graph-node-op-wiki/README.md)
