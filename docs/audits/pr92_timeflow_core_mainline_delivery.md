# PR92 TimeFlow Core 主线落地计划

本文记录 PR92 TimeFlow 候选改动在主线中的可执行收敛方案。它是审计与实施证据，不是新的规范来源。

## 1 目标

把 PR92 中真正可复用、可进入 `src/Core` 的 TimeFlow infra 独立摘出，形成单独可验证、可合主线的最小交付；同时明确哪些上层内容必须继续留在后续阶段处理。

## 2 任务执行判断

依据 `docs/conventions/02_ai_assisted_development.md` 第 4 节，这次落地不是“把 PR92 原样合并”，而是“将跨层混合改动拆回 Core infra 单独提交”。

本次提交只处理以下内容：

* `src/Core/Engine/TimeFlow/`
* `src/Core/Scripting/CoreServiceKeys.cs`
* `src/Core/Engine/GameEngine.cs`
* `src/Core/Gameplay/GAS/GasClockStepPolicy.cs`
* `src/Core/Gameplay/GAS/Systems/GasClockSystem.cs`
* `src/Tests/TimeFlowCoreTests/`
* `docs/architecture/time_flow.md`
* `artifacts/acceptance/timeflow-core/`

本次明确排除以下内容：

* `mods/capabilities/time/`
* `mods/showcases/time/`
* `mods/showcases/timeflow/`
* 所有 mini entry Mod

## 3 复用清单

复用基础设施：

* Registry: `src/Core/Registry/StringIntRegistry.cs`
  用于 domain 名称到 id 的映射。
* Pipeline: `src/Core/Engine/GameEngine.cs`
  复用现有 `Tick()` 主循环，而不是新增并行 runtime。
* System: `src/Core/Gameplay/GAS/Systems/GasClockSystem.cs`
  复用现有 GAS 时钟系统，仅扩展步进语义。
* Policy: `src/Core/Engine/Physics2D/Physics2DTickPolicy.cs`
  复用既有 physics tick 频率控制。
* Policy: retired navigation tick policy removed by the navigation-domain unification.
  复用既有 navigation tick 频率控制。

新增内容：

* Core time domain/token 服务
* 独立轻量测试工程 `src/Tests/TimeFlowCoreTests/`
* Core infra acceptance artifact
* 对应 architecture SSOT

## 4 执行顺序

1. 在干净 worktree 上从 `origin/main` 单独起分支。
2. 只摘取 Core TimeFlow 代码与最小接线。
3. 新增独立测试工程，避免把验证绑定到当前已知阻塞的 `GasTests` 大工程。
4. 生成确定性 acceptance artifact。
5. 回写 architecture SSOT，并保留上层收敛的技术债封条。

## 5 验证计划

最低验证闭环如下：

* `dotnet build src/Core/Ludots.Core.csproj`
* `dotnet test src/Tests/TimeFlowCoreTests/TimeFlowCoreTests.csproj`
* 复查 `artifacts/acceptance/timeflow-core/`
* 复查文档链接与 scope 是否仍只声明 Core infra

已知不纳入本次阻塞判定的环境问题：

* `src/Tests/GasTests/GasTests.csproj` 在 clean `origin/main` 上也会因为 `src/Libraries/Svg.Skia/externals/SVG/Generators/Svg.Generators.csproj` 的 `.NET 9` 需求触发 `NETSDK1045`。本次通过独立测试工程隔离该非本次改动引入的阻塞。

## 6 后续阶段

后续仍需按阶段继续收敛：

* Phase B: `mods/capabilities/time/TimeFlowMod/`
  建立强类型 capability 合同，移除字符串加反射桥接。
* Phase C: `mods/showcases/time/TimeFlowShowcaseMod/`
  通过正式扩展点接线，只保留一套 showcase 真相。
* Phase D: mini entry Mods
  改为仓库标准工程接线，再补 launcher resolve 级别验收。

## 7 相关文档

* Core TimeFlow SSOT：见 [../architecture/time_flow.md](../architecture/time_flow.md)
* PR92 技术债封条：见 `artifacts/techdebt/2026-04-01-pr92-timeflow-mainline-convergence.md`
