# cfg-07 reference · 合并规则案例集

> 现状参考。产品承诺见 [cfg-07 prd](../prd/cfg-07-merge-rules.md)；目标实现见 [cfg-07 spec](../spec-runtime/cfg-07-merge-rules.md)。

## 1. 现状快照

- 同 id 条目内字段级合并：对象递归合并，标量后写覆盖，数组默认整体替换；仅目录条目 `ArrayAppendFields` 列名的数组字段追加，当前全部目录条目零使用。
- 删除标记 `__delete` / `Disabled` 在遍历到该片段时即时生效：从已合并集合移除该 id 并撤出顺序位；后续片段再写同名 id 按"首次出现"重新加入。也接受可解析字符串布尔。
- 整文件级深合并用于时钟、订单类型、属性约束、游戏配置等文件；目录 71 条实际只使用同 id 深合并与深合并两种策略，Replace / ArrayReplace / ArrayAppend 三种策略无任何条目使用。
- 两互相没有依赖关系的 mod 的覆盖胜负由启动计划顺序决定（见 cfg-03 reference）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 同 id 字段级合并（对象递归、标量覆盖） | src/Core/Config/ConfigMerger.cs:200-236 |
| 数组整体替换与 ArrayAppendFields 例外 | src/Core/Config/ConfigMerger.cs:221-231、238-245 |
| 删除标记即时生效（遍历中移除并撤出顺序位） | src/Core/Config/ConfigMerger.cs:144-157 |
| 删除标记的布尔解析（含字符串布尔宽容） | src/Core/Config/ConfigMerger.cs:181-198 |
| 目录条目 ArrayAppendFields 语义 | src/Core/Config/ConfigCatalogLoader.cs:40-68 |

**相关文档**：[cfg-07 prd](../prd/cfg-07-merge-rules.md) · [cfg-07 spec](../spec-runtime/cfg-07-merge-rules.md) · [cfg-05 reference](cfg-05-config-pipeline.md) · [cfg-03 reference](cfg-03-launch-graph.md)
