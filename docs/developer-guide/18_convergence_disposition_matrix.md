# 版本收束处置矩阵（分支 / PR / Issue）

本文给出本轮收束后的处置建议清单，用于主干合并与远端清理。核心原则：不整包引入脏分支，不保留与主线冲突的双真相路径，不通过忽略测试掩盖问题。

## 1 分支与 PR 处置建议

## 1.1 当前 open PR

| PR | 标题 | 建议动作 | 理由 |
|---|---|---|---|
| #13 | 生产最优方案 | **按提交择优吸收后关闭 PR** | 主方向正确（去 fallback/去旧链路），但不建议整包，避免引入非必要噪声与历史冲突。 |
| #11 | 实体外观显示系统 | **关闭 PR，保留可复用片段到独立任务** | Draft 且侵入较大，已与当前主线展示方案（FeatureHub/DiagnosticsOverlay）重叠。 |
| #10 | Performer skill demo mod | **按文件粒度继续摘取后关闭 PR** | 含高价值展示改进，但需避免与现有接口/类型演进冲突。 |
| #9 | 效果预设验收 | **关闭 PR（核心价值已吸收）** | `SpawnedUnitRuntimeSystem` 命名增强等关键点已并入当前收束分支。 |
| #7 | Gas input order 效果预设 | **关闭 PR** | 分叉深、冲突面大，已被当前链路重构覆盖。 |

## 1.2 远端分支清理建议

- 建议保留：
  - `origin/main`
  - 本轮收束分支（已包含可合并结果）：`origin/cursor/-bc-3e3acf5d-5054-4215-b36a-200bd7a75d87-c6a8`

- 建议清理（在主干合并后）：
  - `origin/cursor/-bc-75495c7f-bea9-4e55-9957-c268ef633bcc-11ce`
  - `origin/cursor/-bc-7642a536-0c85-4cec-b5b9-c3e03631616a-752d`
  - `origin/cursor/-bc-9f291f8d-ff2a-4c38-a9c5-343552ffb4f8-cab8`
  - `origin/cursor/-bc-af870afa-661c-47db-a68e-03ad8cce55d7-aa26`
  - `origin/cursor/development-environment-setup-2eea`
  - `origin/cursor/gas-input-order-933a`
  - `origin/cursor/gas-mod-dcf5`
  - `origin/cursor/performer-skill-demo-mod-5a21`
  - `origin/cursor/prs-dbf7`
  - `origin/feat/config-id-audit`
  - `origin/feat/feature-showcase-demos`
  - `origin/feat/unified-camera-system`
  - `origin/fix/camera-wasd-grid-and-overlay`
  - `origin/fix/moba-test-coreinputmod-dependency`

## 2 Issue 状态更新建议

## 2.1 可直接关闭

1. **#14 GasTests: 25 个既有失败需排查**  
   建议：**关闭**（问题描述已过时）  
   证据：当前全量结果为 `GasTests 639/639`，不再存在该 issue 描述的失败规模。

2. **#2 GAS 容量边界静默失败需显式化处理**  
   建议：**关闭**  
   证据：已补齐显式计数链路并增加测试覆盖：
   - `ActiveEffectContainer.Add()` 失败计数
   - Listener 收集/注册截断计数
   - `GameplayEventBus.DroppedEventsLastUpdate` 纳入预算
   - 见 `RootBudgetTests` 相关新增断言

## 2.2 建议“补证据后关闭”

1. **#3 Editor ↔ Engine 实体位置语义断裂**  
   建议：补一次端到端证据后关闭。  
   当前依据：Bridge/Runtime 侧配置与位置链路已统一到当前收束语义。

2. **#6 统一 Editor ↔ Raylib 相机初始状态**  
   建议：补一次端到端证据后关闭。  
   当前依据：主线已使用 `MapConfig.DefaultCamera` 作为显式入口，并完成 camera 规范文档化。

## 2.3 建议保留并拆分

1. **#1 配置治理：统一继承与合并语义**  
   建议：保留，拆成可执行子项（catalog 规则、loader 规则、Bridge 规则、回归测试）。

2. **#4 Audit: Phase 1 + Phase 2a 审计**  
   建议：保留为历史审计跟踪项，附上本轮收束提交作为结果映射，再视团队流程决定是否关闭。

## 3 本轮收束关键结果（用于 PR/Issue 关联）

- 工具链统一：`48c32aa`
- 配置 ID 收束：`577a03c`
- 可玩性与展示入口吸收：`3f3bb24`
- P0 报告与容量边界修复：`f607dca`
- 空间查询防御性修复（全量回归中发现）：`cfd142c`
- 唯一真相规则文档：`ef291d9`

## 4 执行顺序建议

1. 先将收束分支合入主干。
2. 按本矩阵更新 open PR 状态（close 或转任务）。
3. 按 issue 建议批量更新状态（close / keep with sub-tasks）。
4. 最后清理远端冗余分支，保持主干可维护性。

## 5 相关文档

- Mod 运行时唯一真相与收束准则：见 [17_mod_runtime_single_source_of_truth.md](17_mod_runtime_single_source_of_truth.md)
- ConfigPipeline 合并管线：见 [07_config_pipeline.md](07_config_pipeline.md)
