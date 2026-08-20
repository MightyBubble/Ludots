# GAS Composition Gate — RNG 能力（Epic #1039 Phase 2）

## GAS Composition Gate — Self Review

- **Task / Issue**: 确定性随机与加权分布能力 Mod（RngCapabilityMod 垂直切片）/ #1039
- **Date**: 2026-08-20
- **Agent / Author**: Codex (ZCode)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A** —— 新增 graph 节点（`RngCapabilityMod.WeightedPick`，后续 `DrawFromDeck`）及其连线/参数；配套的 `distributions.json` 是**数据 catalog**（条目/权重/限额），不是行为开关 schema。

结论: **PASS**

一句话理由: 抽取机制是单一参数化 op（分布 id + 流名 + pick 数 + 调制输入），一切行为变体（pick 数、防重复、属性调制、换分布）都通过图连线与节点参数表达，不改任何 Core enum。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 命名确定性种子流 | 基础服务（Phase 1 已交付） | `Ludots.Core.Engine.Randomization.IRngStreamService` |
| 加权抽取 op | Layer 2（Mod extension op，opcode ≥1024） | `RngCapabilityMod.WeightedPick` handler |
| 分布作者数据 | 数据资产（非行为层） | mod `assets/RNG/distributions.json` + config_catalog `ArrayById` |
| 归一化/锁定不变式 | Mod 运行时语义 | 加载期校验 + `DistributionTable` 运行时对象 |
| 有状态抽取（可耗尽/卡组） | 后续切片（本 gate 涵盖其形态约束：状态进 ECS 组件 + 系统推进） | `DepletableStateCm` / `DeckStateCm`（后续） |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable` 扩展机制（mod op 经 `ModExtensionHub.Gas` 注册进同一张表）
- Queues / Systems: `SystemFactoryRegistry`（有状态抽取的系统推进，后续切片）
- Resolvers / Registries: `GraphLookupTableRegistry`（对照边界，见 §4 论证）、`CoreServiceKeys`/`GameEngine.SetService`（种子流上册，Phase 1 已接）
- Existing presets / graphs: `RandomFloat01` op + `GraphExecutionState.RandomSeed`（单点均匀随机先例；本能力不改动它）
- 配置管线: `ConfigPipeline` + `config_catalog.json`（`ArrayById` 合并 + `ShardDirectories`），不建私有加载器

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| （Core 侧）N/A | — | — |
| `RngCapabilityMod.WeightedPick`（Mod extension op，Layer 2 面） | 从指定分布按权重抽 N 个条目索引 | 理论上可用 `RandomFloat01` + 比较链组合，但那会把**分布数据编进图拓扑**（条目数变化 → 重编图），违反数据/行为分离；正确形态是单一抽取算子 + 分布 id 参数 |

**与查表 SSOT 的边界论证**：`graph-table-lookup.md` 禁止「表内聚合新 opcode」。`WeightedPick` 不是表内聚合——它是**新抽取算子**（读分布表并返回被抽中的条目），与 `ResolveTableRow`（确定性读行）正交：前者消耗随机流，后者不消耗。二者可组合（先抽行、再读列）。

### 5. Transaction boundary

本切片的抽取 op **无副作用**（读流、读表、写图输出；流推进是确定性的，重放即回滚）。后续有状态抽取的 units/卡组消耗必须在单一 system update 内 all-or-nothing 提交（Layer 1 壳），禁止半完成状态。

### 6. Config SSOT

行为配置落在: graph 节点参数与连线（pick 数、流名、调制输入、防重复模式）；数据落在 mod catalog（`assets/RNG/distributions.json`）。

是否新增 JSON schema: **YES** —— `distributions.json` 为数据 catalog（id/weight/enabled/locked/limit 数值字段），不含任何行为开关字段（无 mode/inherit/placement 类声明）；行为变异全部走图组合，故通过。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback——未声明流名/未知分布 id 一律失败关闭（编译期或加载期可读错误）

### 8. Next variant test

「下一个 Mod 变体」（例如：防重复抽取、拟物洗牌、保底计数）将修改: **graph 连线 / op 参数**（新增 op 输入或后续 op），不触碰 Core enum。

若选了 Core enum → FAIL（未选中）

---

## 复用 / 新增清单（§4.2 合并输出）

| 类型 | 项 |
|------|-----|
| 复用 | `IRngStreamService`（Phase 1）、`ModExtensionHub.Gas` 图 op 注册、`GasGraphOpHandlerTable`、`ConfigPipeline` + config_catalog、`GraphProgramAuthoringFrontDoor`、`RngSeed` FNV 混合 |
| 新增 Layer 0 op | 无（Core 不加 op；`RandomFloatRange` 可由 `RandomFloat01` 线性组合，不需要） |
| 新增 Layer 1 | 无（本切片无事务壳） |
| 新增 Layer 2 | `RngCapabilityMod.WeightedPick` extension op + 分布 catalog schema + 运行时 `DistributionTable` |
| 禁止 | 无新 profile DSL、无平行加载器、无隐式全局流 fallback |

---

## 附录：分层修正（2026-08-20，随评审意见落地）

原方案把分布机制放在 `RngCapabilityMod`（mod 侧）。评审指出并经仓库证据确认（Core 已收
`ItemDefinitionRegistry`/`InventoryRuntimeService` 等领域通用机制，§4.4 两个以上 Mod 复用应提取 Core）：
加权分布是与物品/背包同级的**引擎原语**，capability mod 应是利用该基建的 loot 语法糖。已重分层：

- 机制下沉 Core：`src/Core/Gameplay/Rng/`（DistributionTable/Config/Loader/RngPickService），
  `CoreServiceKeys.RngStreamService/RngPickService` 上册，GameEngine 初始化装配；
  Core `assets/config_catalog.json` 声明 `Rng/distributions.json`（ArrayById + AllowEmpty），
  各 mod 以 `assets/Rng/distributions.json` 分片贡献作者数据（与 Items 同模式）。
- `RngCapabilityMod` 撤销；`RngCapabilityMod.WeightedPick` mod op 与 interning 上下文删除
  （作者图尚不能引用 mod op，待前门缺口修复后直接做 Core op）。loot 语法糖（嵌套掉落/稀有度/保底/可耗尽）
  归属后续 LootCapabilityMod / showcase 切片。

判定不变：A（op 组合，无 enum/preset 开关）。
