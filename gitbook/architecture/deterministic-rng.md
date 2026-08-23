# 确定性随机与加权分布（Deterministic RNG）

> Epic #1039 能力 SSOT。机制归 Core（种子流 + 分布原语 + `WeightedPick` 图 op），玩法语法糖（loot/卡组）建其上。
> Composition gate 自审：`artifacts/gas-composition-gate-rng-capability.md`。

## 一、能力形态

| 层 | 位置 | 职责 |
|---|---|---|
| 种子流 | `src/Core/Engine/Randomization/` | 命名确定性流（xorshift32 显式推进、快照/恢复、FNV 种子混合）；fail-closed 声明/查询 |
| 分布原语 | `src/Core/Gameplay/Rng/` | `DistributionTable`（权重归一化、锁定份额构造期锚定、调制 permille clamp）；`RngPickService`（分布 registry + key id intern） |
| 图 op | `GraphNodeOp.WeightedPick = 443` | `Imm`=分布 key id、`I[A]`=调制 permille、`I[Dst]`=条目索引；经 `IGraphRuntimeApi.WeightedPick` 执行。作者 JSON 以分布名符号引用，编译期 `RequireSymbol(node.Distribution)` 收进符号表，加载期 `GraphProgramSymbolPatcher` 经 `ResolveRngDistribution` 绑定 key id，未知分布名失败关闭 |
| 存档 | `src/Core/Persistence/CoreSaveParticipants.cs` 的 `RngSaveParticipant`（domain `rng`） | 捕获全部已声明流的 (streamId, seed, state, position)；读档声明集/种子不一致即失败关闭，恢复后流位置与序列延续 |
| 作者数据 | mod `assets/Rng/distributions.json` | Core catalog 声明 `Rng/distributions.json`（ArrayById+AllowEmpty），各 mod 分片供数据 |
| Showcase | `mods/showcases/rng/RngShowcaseMod` | 自动抽取主循环 + 旋钮 + 重放证明 + AgentBridge 工具 |

## 二、行为合同

- **确定性**：同种子 + 同调用序列 ⇒ 同结果。种子只经显式 `Next*/Advance` 推进（禁止 getter 副作用）；`RngStreamSnapshot` 支撑重放与存档。
- **失败关闭**：未声明流、未知分布/key、负权重、全禁用分布、清零最后一个未锁定正权重——一律抛可读错误，无隐式回退。
- **锁定锚定**：锁定条目的份额在构造时锚定；运行时改未锁定权重只在未锁定预算内重归一化，锚不动。
- **调制**：`minPermille/maxPermille`（相对基准份额），`invert` 反向；NaN/∞ 熔断为中性；结果 clamp 在区间内。
- **分布数据 schema**：`{ id, stream, streamSeed?, entries: [{ id, weight, enabled, locked, modulation? }] }`；流种子缺省按流名 FNV 派生。

## 三、Showcase（按 showcase-design 八步）

1. **形态**：数据/规则。动态轴 = 世界事件（tick 连抽）⇒ 抽取结果与期望偏差实时变化。
2. **一句话**：「拨一个属性，掉落分布当场偏移；按一次重放，整段掉落逐个重现。」目标用户：做掉落表/抽卡/随机事件的玩法作者。
3. **主循环**：自动抽取环（默认每 30 tick 一轮 ×10 连抽）持续累计 actual vs expected；惊喜时刻 = 调制拨满 + 重放对齐。
4. **解释层**：`ludots.rng.state` 输出每条目 actual/actualPct/expectedPct/effectiveShare + 流位置（视觉直方图为后续面板切片）。
5. **旋钮**（`ludots.rng.knob`）：调制 permille / burstSize / intervalTicks / autoRun / distribution 切换。
6. **结构**：主演示 = hunt.loot 自动环；hunt.critical 为第二分布（含禁用条目与 invert 调制示例）。
7. **门户资产**：本页 + `showcase.registry.json`（id `rng_distribution_showcase`）；截图经 AgentBridge 采集。
8. **反向 API 审计已兑现项**：流快照/恢复、分布期望只读、运行时旋钮、抽取审计计数（state 工具）、分布名作者图符号绑定（编译期 `RequireSymbol` + 加载期 `ResolveRngDistribution`）、存档 participant（domain `rng`，声明集/种子失败关闭）；待补：卡组/可耗尽状态组件、mod op 前门泛化。

## 四、验收

- 单测：`src/Tests/RngCoreTests/`（39 项固定种子断言：同种子同序列、快照重放、锁定守恒、调制方向/熔断、失败关闭、key 互通、存档→读档→续抽序列相等、篡改快照拒绝）。
- 桥验收：`ludots.rng.replay` 必须 `matched:true`；`ludots.rng.draw` 两次同快照序列相等；调制拨动后 expectedPct 偏移。
- 启动：`run-mod-launcher.cmd cli launch --selector rng_showcase --mod AgentBridgeMod`。

## 五、边界与后续

- Effect 方言图禁 Yield——跨拍抽取流走 Script/TriggerGraph 或系统推进。
- 后续切片：`DepletableStateCm`/`DeckStateCm`（有状态抽取 + 可审计洗牌计划）、`LootCapabilityMod`（嵌套掉落/稀有度/保底语法糖）、WebUI 分布调试面板（直方图 + TEST 连抽）、mod op 的 `ModId.OpName` 作者图前门。
