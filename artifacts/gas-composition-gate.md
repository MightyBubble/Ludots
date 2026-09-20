# GAS Composition Gate — Self Review

- **Task / Issue**: Epic #1196 / RFC-0067 P0——GasLoadTimeCapacityPlan 脚手架 + 七指标基准对比床 + baseline 入库
- **Date**: 2026-09-20
- **Agent / Author**: ZCode（分支 feat/gas-loadtime-capacity-p0）

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: 均不是——装载期容量计划（纯数据记录 + fail-fast 校验）与基准对比床（测试设施）。不新增运行时行为变体、enum、preset 开关或平行管线；存储形态零变化（P0 明确不动 AttributeBuffer/GameplayTagContainer）。

结论: PASS

一句话理由: P0 是容量真相的"记账与门禁"，属性/标签读写路径一行未动；写入口径仍由既有 AttributeMutationOps + 治理白名单锁死。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| GasLoadTimeCapacityPlan | 3（装载期容量合同，纯数据） | `src/Core/Gameplay/GAS/GasLoadTimeCapacityPlan.cs` |
| 冻结窗口挂接 | 3 | GameEngine.InitializeCoreSystems 尾部（AttributeRegistry.Freeze 同窗口，RFC 点名位置） |
| 基准对比床 | 测试设施 | `GasLoadTimeCapacityBenchmarkTests`（七 MetricId，min-of-5，emit/compare 双模式） |

## 3. Reuse list

- Registries: `AttributeRegistry`/`TagRegistry`（Count 透传新增，委托 IdentityTable 既有 Count）；`CoreServiceKeys`/`SetService` 世界级暴露先例
- 冻结窗口: `GameEngine.InitializeCoreSystems` 既有 AttributeRegistry.Freeze 位置（:2183）
- 基准: NUnit 内嵌 alloc+耗时范式（FogBenchmark/GasBenchmarkTests 先例）、`GasBenchmark.Run`（RFC 点名扩展点，本次顺手修复其在 main 上的两处既有损坏）
- 不新建 runner 脚本：对比器内嵌为测试（RFC §3.4 规则 3 的失败关闭即断言失败）

## 4. New Layer 0 ops (if any)

N/A

## 5. Transaction boundary

N/A——装载期一次性 Freeze，失败即启动失败。

## 6. Config SSOT

行为配置落在: 无新表；baseline 数据 `docs/rfcs/gas-loadtime-capacity/benchmark-baseline.json`（RFC §3.4 规则 1 点名路径）。

是否新增 JSON schema: NO（baseline 是证据工件非配置）。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线（存储零改动）
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（超天花板/超物理上限/负数全部 fail-closed，错误指明 P1/P2 出口）

## 8. Next variant test

下一个变体（P1 属性世界列存）将修改: 新世界列存类型 + AttributeMutationOps 路由开关（RFC 允许的迁移期开关，P4 拆）——不改 Core enum、不加 preset。
