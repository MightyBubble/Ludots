# Knowledge 投影的第一性原理审查：它解决什么用户场景，以及当前形态是否成立

写作动机：minimap 每帧对 10,009 个 marker 各做一次 knowledge 解析（1.4–3.0ms，
占 minimap 成本 ~75%）。我把热路径改快了一档（`7fe343843b`），但该问的是
**这条链路本身是否符合 Ludots 的架构原则，以及它到底在服务哪个用户场景**。

## 1 它服务什么用户场景（有据可查）

来源是 **RFC-0065**（`docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md`，
落地为 issue #581 系列）。它解决的问题是真实且有分量的：

> **同一个 pointer intent（"右键"只是绑定数据），按 actor 能力 × target 事实 动态路由**
> —— 点敌方单位 = 普攻；有驻扎能力的单位点可驻扎建筑 = 进驻；点可破坏道具 = 攻击；
> 同一个目标既能驻扎又能破坏时必须有**明确配置的唯一胜出者**。

其中 knowledge 的职责是**战争迷雾的正确性合同**（RFC DEC-14）：

> target 事实必须经 viewer knowledge 门控，**不是 sim 真值**——fog 下不可见单位
> 不可被路由；伪装单位按**被投影的 tag/stance** 路由。
> `| L8 target 事实求值零 sim 真值直读（必经 viewer KnowledgeProjection）|`

配套场景：per-viewer 可见性（本地玩家 / 队友 / **裁判**）。裁判是"恰好持有
knowledge grant、不持 controls 边的 viewer anchor"——这是它存在的核心理由：
**同一个世界，不同 viewer 看到不同事实**，且这个差异必须可被玩法读取，而不只是画面。

所以：**场景成立，不是幻想出来的基建。** 如果删掉它，fog 下的点选/瞄准会退化成
读 sim 真值，玩家能隔着迷雾点到看不见的单位——这是实打实的玩法缺陷。

## 2 架构上真正的问题（不是性能，是分层和形态）

### 2.1 它是一张**与 ECS 平行的关系表**，而不是 ECS 数据

`KnowledgeProjectionStore` 内部是 `EntityKeyedSoaTable<KnowledgeProjectionPayload>`：
自己维护 `_primaryIds/_primaryVersions/_secondaryIds/_bucketHeads/_entryNext`……
即**自建哈希表 + 自建世代号 + 自建过期**。它挂在一个 Service 上
（`CoreServiceKeys.KnowledgeProjectionStore`），**没有任何 ECS 组件承载**。

与项目自己的原则对照：

- `gitbook/architecture/entity-simulation-layering.md` 与本仓库一贯的
  "一切皆 Mod / 数据先声明 / 复用 Registry 和 ECS" 取向，要求实体事实尽量落在
  **组件 + System** 上，让查询、结构变更、序列化、调试观测都走同一条链路。
- 现状是：一类实体事实（谁看得见谁）落在**自建索引**里，于是 ECS 那套
  （chunk 迭代、SoA、revision、存档、agent-bridge 观测）全部用不上，
  必须为它单独写一遍 scope 解析 / accumulator 合并 / 投影构造。

**代价已经在测量里显形**：minimap 每 marker 一次解析 = 每帧 10K 次自建表查找 +
两个大结构体（各带 3×256-bit mask）的构造。热路径优化只能削掉一部分，因为
根因是"ECS 之外还有一张表"。

### 2.2 查询形态与消费形态不匹配（这是 10K 成本的直接来源）

- **存储是"稀疏对"**：(viewer, target) 一条记录。合理。
- **消费是"稠密扫描"**：minimap 要对**屏幕上每个 marker**问一次。10K 个 agent
  = 10K 次单点查找。
- 但 minimap 真正需要的只是"**这张地图上，我这个 viewer 能看见哪些**"——
  一个**集合级**问题，被实现成了 10K 次**点级**查询。

正确形态应该是：knowledge 变化时产出一个**按 viewer 的可见集合/掩码**
（或一个 revision），minimap 直接过滤，而不是每帧重问 10K 次。
RFC 其实已经指向这个方向：INT-4 提到"spatial 候选查询必须补 knowledge 过滤"，
说明 knowledge 应该能作为**查询谓词**参与，而不是逐点后置判断。

### 2.3 三个 256-bit mask 在"点级查询"上是纯负担

`KnowledgeDisclosureRecord` / `KnowledgeProjection` 各带
attribute/relationship/tag 三个 256-bit mask。它们是 RFC 里 **INT-8**（per-viewer
tag/stance 事实投影，伪装/假情报的前提）的预留，**该单尚未落地**。

于是当前每一帧的 10K 次查询，都在为**一个还没实现的特性**搬运/合并 96 字节的掩码。
我刚提交的 `TryResolveDisclosure` 正是绕开它们——但这是打补丁；
如果 INT-8 落地后再长回来，又会回到原地。**该按消费方需要分区**：
只要 presence/position 的消费方（minimap、可见性门控）不该付 mask 的钱。

## 3 结论与建议

**保留场景，重构形态。** 具体三条，按价值排序：

1. **把"per-viewer 可见性"做成集合级产物**（最高价值）。
   knowledge 变化时（它已经有 `Revision`）产出一个按 viewer 的可见集或 dense mask，
   minimap/剔除/HUD 直接消费。这一步把 O(标记数) 次单点查询变成 O(1) 次集合读取，
   是唯一能把 minimap 那 3ms 真正拿掉的办法。
   *这也顺带解掉 RFC 自己的 INT-4（spatial 候选查询的 knowledge 过滤）。*

2. **把热事实搬进 ECS 组件 + System**，让自建表退化成"稀疏/远期记录"的存储。
   近期、逐帧要读的（presence/position）值得是组件；远期、稀疏的（relation grant、
   INT-8 的伪装事实）留在表里合理。**判定标准**：如果每帧每个实体都要读，它就该是组件。

3. **按消费方拆分投影的宽度**。presence/position 是一个便宜视图；
   三个 mask 是另一个只在 INT-8/伪装场景才需要的视图。不要用一个 struct 同时服务两者。

### 与本轮工作的关系

我已经做的两件事与上述一致、但只是止血：

- `7fe343843b`：给点级消费方开窄查询，绕开 mask 与 accumulator —— 削掉 ~30%。
- 天花板实验证明：完全跳过 knowledge，minimap 从 5.5–6.0ms 掉到 0.8ms。
  **剩下的 1.6–2.2ms 是"每 marker 一次自建表查找"的结构性成本，热路径优化吃不掉。**

我**没有**动第 1/2 条：它们要改 Core 接口与数据所有权（属于规范 §4.1 的"基建任务，
应先说明方案再继续"），而且 RFC-0065 有明确的未完成里程碑，需要作者/负责人拍板，
不能由性能会话单方面重写。
