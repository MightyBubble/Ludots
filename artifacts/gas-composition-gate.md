# GAS Composition Gate — Self Review

- **Task / Issue**: Epic #1196 / RFC-0067 **收口**：P2 标签世界位列（256→4096）+ P3 跨域守卫与 presenter 高槽读 + P4-lite 拆 T16 + P5 全量对照
- **Date**: 2026-09-20
- **Agent / Author**: ZCode（分支 feat/gas-tag-capacity-closeout）

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: 均不是——标签位列进既有 `WorldAttributeStore`（行共享、容量计划唯一真相）；TagOps 高车道（位 [256,Plan) 列存唯一真相）；presenter/exchange/query 读路由；`ExtensionAttributeRegistry` 删除（T16 唯一出口：死代码拆除）。无新 enum/preset/管线。

结论: PASS

一句话理由: 标签写入口径唯一（TagOps 世界级入口）；高 id 规则在注册期即被既有 256 守卫拒绝（失败关闭不降级）；跨域未对齐面全部显式失败关闭并指明 P3 边界；活差分门两轮全过零新增分配。

## 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| 标签位列（镜像 + 高位真相） | 0 | `WorldAttributeStore`（tagBits/tagLastSnapshot/tagDirtyRows 列） |
| TagOps 高车道（含稀疏计数） | 0 | `TagOps.AddTagHigh/RemoveTagHigh/HasTagRouted/MirrorTagLow` |
| 高标签延迟触发 | 2 | `AttributeHighLane.CollectHighTagChanges` |
| 标签 authoring 种子 | 3 | ComponentRegistry.SetGameplayTagContainer 高 id 建行 |
| presenter/exchange/query 高槽读 | 2 | PresenterBehaviorSystem 路由；内联初始/定义条件高 id 显式失败关闭 |
| T16 拆除 | — | ExtensionAttributeRegistry/AttributeSchemaUpdateSystem/接线/测试 删除 |
| 活差分对比门 | 测试设施 | LUDOTS_COMPARE_LIVE=1（同进程背靠背双测，抗外部负载） |

## 3. Reuse list

P1 全套基建（store/ambient/reads/highlane）；TagCountContainer 稀疏表（任意 id 天然支持）；TagRuleRegistry 既有 256 注册守卫（高 id 规则失败关闭由此免费获得）；DirtyEntityQueue 既有脏通道。

## 4. New Layer 0 ops

N/A

## 5. Transaction boundary

标签事务沿用 TagRuleTransaction 既有合同；高 id 规则不存在故事务不触高车道。

## 6. Config SSOT

容量真相唯一：`GasLoadTimeCapacityPlan`（GameEngine 传参升级为绝对天花板 4096）。基准入库 `benchmark-baseline.json` + `benchmark-final.json`。

是否新增 JSON schema: NO。

## 7. Red flag scan

- [x] 未新增 profile enum/开关
- [x] 未新建平行管线（标签写仍 TagOps 单口）
- [x] 高 id 规则/未对齐跨域全部显式失败关闭（TagRuleNotAligned / RequiredAttributeHighSlot / InlineInitialHighAttribute / HighLaneUnavailable）
- [x] 计数叠层语义与内嵌对齐（重复 Add 叠层不丢）

## 8. Next variant test

下一个变体（第 300 个标签名/第 200 个属性名）零代码改动：注册窗口直接登记，UAT `GasTagCapacityTests`/`GasWorldAttributeStoreTests` 钉死全链。
