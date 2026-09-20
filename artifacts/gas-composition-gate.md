# GAS Composition Gate — Self Review

- **Task / Issue**: Epic #1196 / RFC-0067 **P1**——属性世界列存切流（上限 64 → 计划定容 ≤1024）
- **Date**: 2026-09-20
- **Agent / Author**: ZCode（分支 feat/gas-world-attribute-store）

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: 均不是——`WorldAttributeStore`（装载期定容 SoA 列存）+ 写权威双车道路由（<64 内嵌镜像 / ≥64 列存真相）+ 聚合/延迟触发/种子/事务/存档的高车道。无新 enum、无 preset 开关；存储形态是 RFC §3.3 明文列出的 P1 交付（"并行（或特性开关），读写与聚合切流"），P4 拆内嵌真相。

结论: PASS

一句话理由: 写入口径仍唯一（AttributeMutationOps + 既有治理白名单零改动）；高车道全部失败关闭（无列存/越界/超天花板）；迁移成本由已入库对比床逐指标验证 ≤10%、热路径零新增分配。

## 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| 世界属性列存（预定容、行稀疏、零对局扩容） | 0（纯数据结构） | `WorldAttributeStore` + `WorldAttributeStoreAmbient` |
| 读路由（<64 镜像 / ≥64 列存） | 0 | `AttributeReads` |
| 写权威双车道 + 区间守卫 | 0 | `AttributeMutationOps`（SetCurrent/SetBase/ApplyModifiers 路由 + MirrorToStore） |
| 聚合高槽位 pass（base→修饰→cap/current 恢复） | 2 | `AttributeAggregatorSystem.ProcessHighSlots` + `EffectModifierOps.ApplyAggregatedHigh` |
| 延迟触发高车道 | 2 | `AttributeHighLane.CollectAttributeChanges`（DeferredTriggerCollectionSystem 接入） |
| 种子建行（authoring/批量） | 3 | ComponentRegistry.SetAttributeBuffer / TemplateEntityBatchSpawner.SeedWorldStore |
| 事务高车道（懒分配原始/暂存行） | 1 | EffectPhaseSideEffectTransaction（Capture/Stage/Commit/Rollback） |
| 存档高行 | 3 | LifecycleSnapshot.HighAttributes + CopyAttributeSlice 路由 |
| 注册上限解耦 64→1024 | 3 | AttributeRegistry.MaxAttributeIds + ModRegistrySet（约束数组同步 1024） |

## 3. Reuse list

- `GasLoadTimeCapacityPlan`（P0 交付，物理上限参数升级为列存天花板）
- `ModRegistryAmbient` 形状 → `WorldAttributeStoreAmbient`
- `AttributeMutationOps` 写权威与治理白名单（零改动，白名单锁的正是双车道落点）
- `EffectModifierOps`（≥64 跳过 + ApplyAggregatedHigh 高车道）
- 既有 NUnit 基准床（P0）——P1 升级为可绑定/不绑定列存的双形态对比

## 4. New Layer 0 ops

N/A（无新 atomic op；本切为存储与数据面）

## 5. Transaction boundary

事务高车道沿用既有 EffectPhaseSideEffectTransaction 的 all-or-nothing：捕获原始行 → 暂存 → commit 经写权威回放 / rollback 整行恢复。

## 6. Config SSOT

容量真相唯一：`GasLoadTimeCapacityPlan`（GameEngine 冻结窗口构建 store 并绑定）。baseline/after 数据：`docs/rfcs/gas-loadtime-capacity/benchmark-{baseline,p1}.json`（§3.4 规则 1/2）。

是否新增 JSON schema: NO。

## 7. Red flag scan

- [x] 未新增 profile enum/开关（迁移期"双轨"是 RFC §3.3 P1 明文形态，P4 拆除已排程）
- [x] 未新建平行物化管线（读面 27 处既有 `Get<AttributeBuffer>` 全部零改动即正确——<64 镜像合同）
- [x] 高车道失败模式全部 fail-closed（HighLaneUnavailable / AttributeSlotOutOfRange / AttributeRowCapacityExceeded / 区间 ArgumentOutOfRange）
- [x] 镜像只做有源镜像（种子建行后写路径同步；无行不镜像——不制造半真相）

## 8. Next variant test

下一个变体（内容作者再加第 200 个属性名）将修改：**零代码改动**——注册表在装载窗口直接登记至 1024，模板/地图照常 authoring，UAT `GasWorldAttributeStoreTests.HighSlots_RegisterWriteRead_RoundTripsThroughStore` 钉死全链。
