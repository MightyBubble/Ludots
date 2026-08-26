# 验收与学习路线

按这条线走，可以从"看见一条能力"学到"证明一条能力"。总目录见 [README.md](README.md)。

## 路线〇：单能力入口（先逐条）

翻新合同：[Presenter 能力演示集体翻新](../../../docs/architecture/presenter-capability-showcase-refresh.md)。

每条 BehaviorKind / PresenterCommandKind 从本目录跳到 **L1 单能力演示**（一能力一入口）。铁匠铺与大世界压测是 L3 故事/压测层；逐条勾选认 L1。

本轮缺口补齐入口：

| 能力 | L1 preset | 操作 |
|---|---|---|
| TrailMesh | `capability_standard_presenter_trailmesh_showcase_raylib` | `T` 开合拖尾 |
| Material BehaviorKind | `capability_standard_presenter_material_behavior_showcase_raylib` | `C`/`W` 冷暖材质，`Space` 切换 |
| activationCondition | `capability_standard_presenter_activation_condition_showcase_raylib` | 左亮右灭对照 |
| Sound | `capability_standard_sound_showcase_raylib` | `1`/`2`/`3` |
| 指令全息（多站） | `capability_standard_presenter_command_showcase_raylib` | 站内按钮 |

## 路线一：引擎画廊 20 场景（先看见渲染）

每场景一键跑、带验收截图 + 120 帧统计 + 页内录像；逐场讲解（这场演的是什么 / 作者怎么写 / 怎么跑 / 边界）见 [引擎画廊 Wiki](../engine-gallery-wiki/README.md)。

```powershell
.\scripts\run-mod-launcher.cmd cli launch preset:engine_raylib_lighting --adapter raylib
```

## 路线二：逐能力 UAT（再对照）

[Presenter Raylib UAT 测试计划](../../architecture/presenter-raylib-uat.md)按模块给"玩家体验 UAT + Mod 作者配置 UAT"双视角表：每种 AssetKind、每种 BehaviorKind、树生命周期、Timer、黑板、grounding、铁匠铺端到端（§13）都有可执行验收项——学一条能力，对着一张表验一条。

## 路线三：铁匠铺集成巡演（串故事，不勾选逐条）

preset `presenter_blacksmith_showcase_raylib` 把「事件 → 规则 → presenter 树 → 行为 → draw buffer → raylib 出画」串成可玩故事。用它感受全链路。逐条能力是否演示完备，以路线〇 / 本目录各条目的 L1 preset 为准。

## 路线四：指令层（命令怎么驱动 presenter）

指令逐条目录见 [commands.md](commands.md)。三步走：先跑 preset `presenter_blacksmith_showcase_raylib` 看指令全链（出生建树、tag 切行为、日夜/区域 SetParam）；再读验收 `artifacts/acceptance/presenter-timer/battle-report.md`（TimerSet/TimerExpired/TimerKill 的受击闪黄时序与打断语义）；最后跑 Extension 黄金模板 preset `capability_standard_presenter_command_extension_showcase_raylib`。参数怎么从黑板流到资产属性见 [param-sink.md](param-sink.md)；Timer 与指令层的可玩 showcase = preset `capability_standard_presenter_command_showcase_raylib`（四站点覆盖 11 种内建指令）。

## 路线五：性能基线（信得过）

| 基线 | 规模 | 证据 |
|---|---|---|
| 静态合批 | 3k→300k 实例（slider） | `artifacts/acceptance/engine_raylib_instancing/stats.json` |
| 静态 presenter 生产路径 | 30k 实例 | preset `capability_standard_static_presenter_30k_raylib` |
| 命名 Timer | 90k tick | `artifacts/acceptance/presenter-timer/battle-report.md` |
| HUD hotpath | 5 万级 HUD 投影 | `artifacts/acceptance/presentation-hotpath-harness/battle-report.md` |
| 蒙皮运行时合同 | 桶化蒙皮一致性 | `artifacts/acceptance/presentation-skinned-runtime-contract/battle-report.md` |
| Animator MVP | 状态机→黑板→反馈 | `artifacts/acceptance/animator-runtime-mvp/battle-report.md` |

## 配套合同文档

- [Presenter-as-Actor 架构设计](../../architecture/presenter-as-actor-architecture.md)——分层/命令/黑板/树/裁剪总纲
- [Raylib 渲染配置结构](../raylib-render-config-structure.md)——五类配置文件字段表 + fail-loud 边界
- [Quarks 粒子 Schema](../../architecture/quarks-particle-schema.md)——粒子作者面全字段
- [Instanced Batch 外部 Source Contract](../../architecture/instanced-batch-source-contract.md)——外部实例源合同
- [Retained Static Incremental Projection](../../architecture/retained-static-incremental-projection.md)——增量投影合同
