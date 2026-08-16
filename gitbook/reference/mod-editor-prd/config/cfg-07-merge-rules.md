# cfg-07 配置说明 · 合并规则案例集

> 配置写法与行为。第一性需求见 [cfg-07 PRD](../prd/cfg-07-merge-rules.md)；编辑器需求见 [UXD](../uxd/cfg-07-merge-rules.md)；现状见 [reference](../reference/cfg-07-merge-rules.md)。

## 1. 案例与行为

| # | 意图 | 写法 | 结果与坑 |
|---|---|---|---|
| 1 | 新增内容 | 新 id 条目 | 整条加入；结果顺序 = id 首次出现序 |
| 2 | 改对方一个标量 | 同 id + 只写该字段 | 赢该字段，其余保留 |
| 3 | 扩展对方的对象 | 同 id + 只写对象子路径 | 递归合并，可只写一片叶子 |
| 4 | 改对方数组里的一个元素 | **做不到** | 数组整组替换：必须整组重写（见危险清单） |
| 5 | 追加数组元素 | 仅当字段登记为可追加 | 当前没有任何字段如此登记，对作者即"不存在追加" |
| 6 | 屏蔽对方条目 | 同 id + 删除标记 | 时序删除：只删此前出现的该 id；之后加载的 mod 写同名会复活 |
| 7 | 删除自己拥有的行 | 物理删除本 mod 文件里的行 | 与删除标记是两种语义，不要混用 |
| 8 | 整文件级配置 | 时钟、订单类型、属性约束、游戏配置 | 对象深合并：对象递归，标量与数组后到者覆盖 |
| 9 | 主文件与分片并存 | 同根的 `abilities.json` 与 `abilities/` 目录 | 主文件先收、分片后收（稳定顺序）；两者汇入同一合并 |
| 10 | 大小写 | 合并与清单侧敏感 | 路径、策略名、去重字段名、id 比较为 Ordinal；启动器侧（绑定/预设/依赖闭包）忽略大小写 |

危险数组清单（覆盖即整组替换，改一项须重写整组）：技能的执行时间轴 `items`、`blockTags` 两组、`interruptAny`、`callerParams`、`activeEffects`；效果的 `modifiers`、`grantedTags`、`phaseListeners`、`tags`；Tag 规则六组；订单的阻止/打断两组；形态 `routes`、上下文 `candidates`；AI 各表的考虑项/任务/决策/决策者/过滤/标签。

## 2. 示例

改对方一个标量（案例 2，教学骨架）：

```json
[ { "id": "Effect.Example.Income", "modifiers": [ { "attribute": "Gold", "op": "Add", "value": 8 } ] } ]
```

屏蔽（案例 6）：`[ { "id": "Effect.SomeMod.Something", "__delete": true } ]`

## 3. 文件结构与异常

写法落在 cfg-05 描述的两个合法位置；分片形态同理（cfg-04）。删除标记想屏蔽"之后"加载的条目不生效——需由更晚的 mod 再写一次；数组字段只写部分元素即整组替换，对方其余元素消失。

**相关文档**：[cfg-07 PRD](../prd/cfg-07-merge-rules.md) · [cfg-05 配置说明](cfg-05-config-pipeline.md) · [cfg-03 配置说明](cfg-03-launch-graph.md)
