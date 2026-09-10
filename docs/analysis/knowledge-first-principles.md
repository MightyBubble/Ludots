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

## 2 复查：现有形态是**有意设计**，我先前两条批评不成立

读完 #190 / #191 ADR 后必须修正（原稿把"SoA 自建表"当成架构缺陷，是误判）：

**#191 ADR 明文规定**了当前形态：
- `finite projection`：可见性"不是布尔，是对 aspect 的有限投影"，
  presence / position / attributes / relationships / tags / source·expiry·confidence·revision。
- 热路径要求 **registry-id + array/span 基座、禁 LINQ / 禁迭代器分配 / 禁堆建投影对象**。
- 边界纪律：Blackboard 只做本地执行内存；Relationship 只做语义边与授权；
  EntityCollection 只做实体列表；**Knowledge Projection 是唯一的有限信息读取路径**。

所以：
- **§2.1「该搬进 ECS 组件」——撤回。** ADR 明确选了实体中心的 SoA 存储
  （#193）与零分配 resolver（#195），而不是普通 ECS 组件：因为 aspect 是
  "稀疏 + 有限 + 带过期与授权来源"的投影，不是每实体必有的稠密事实。
- **§2.3「三个 mask 是纯负担」——部分撤回。** mask 是 #191 定义的**最小有限 aspect**
  之一（#192 已落地），不是臆造；我之所以能对 minimap 绕开它，是因为
  **minimap 这个消费方不需要 mask**，不是因为 mask 本身多余。

真正成立、且 #190 尚未覆盖的缺口只有一个：

### 2.1（修正后）缺失的是**稠密消费方的查询形态**，不是存储形态

- 存储是 #193 定的 **"稀疏对"**：`(viewer, target)` 一条记录 —— 符合 ADR。
- 消费是 **"稠密扫描"**：minimap / 剔除 / 世界 HUD 要对**屏幕上每个实体**问一次 —— 10K 次点查。
- #195 的 resolver API 是 `CanKnowEntity / CanReadPosition / CanReadAttribute / ...`
  全是**点级**谓词；`CopyByPrimary/CopyTargets` 这类**集合级**读取存在但**没被稠密消费方使用**。

而 minimap 真正要的是"**这个 viewer 能看见哪些**"—— 集合级问题。
`#190` 的验收清单里 #197（selection/targeting）与 #198（minimap）都还是**未勾选**，
但**没有任何 issue 提到"稠密消费方的集合级读取 / revision 增量"这个形态缺口**。
这就是我要开的那一个（挂 #190 之下，不另立 epic）。

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
