# 验收与学习路线

按这条线走，可以从"看见一条能力"学到"证明一条能力"。总目录见 [README.md](README.md)。

## 路线一：引擎画廊 20 场景（先看见）

每场景一键跑、带验收截图 + 120 帧统计 + 页内录像；逐场讲解（这场演的是什么 / 作者怎么写 / 怎么跑 / 边界）见 [引擎画廊 Wiki](../engine-gallery-wiki/README.md)。

```powershell
.\scripts\run-mod-launcher.cmd cli launch preset:engine_raylib_lighting --adapter raylib
```

## 路线二：逐能力 UAT（再对照）

[Presenter Raylib UAT 测试计划](../../architecture/presenter-raylib-uat.md)按模块给"玩家体验 UAT + Mod 作者配置 UAT"双视角表：每种 AssetKind、每种 BehaviorKind、树生命周期、Timer、黑板、grounding、铁匠铺端到端（§13）都有可执行验收项——学一条能力，对着一张表验一条。

## 路线三：铁匠铺全链路（串起来）

preset `presenter_blacksmith_showcase_raylib` 把"事件 → 规则 → presenter 树 → 行为 → draw buffer → raylib 出画"整条链跑给你看：建筑出生自动展开子树、开工 tag 点烟、日夜切换灯光材质、区域参数换砖、耐久度阈值换 mesh、工人样条巡逻、浮动文字与 HUD。改它 mod 内 presenters.json 的任意一条规则再跑，是最快的学习回路。

## 路线四：指令层（命令怎么驱动 presenter）

指令逐条目录见 [commands.md](commands.md)。三步走：先跑 preset `presenter_blacksmith_showcase_raylib` 看指令全链（出生建树、tag 切行为、日夜/区域 SetParam）；再读验收 `artifacts/acceptance/presenter-timer/battle-report.md`（TimerSet/TimerExpired/TimerKill 的受击闪黄时序与打断语义）；最后跑 Extension 黄金模板 preset `capability_standard_performer_command_extension_showcase_raylib`。参数怎么从黑板流到资产属性见 [param-sink.md](param-sink.md)；Timer 可玩 showcase 随配套 PR 提供。

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
