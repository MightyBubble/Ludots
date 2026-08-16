# S14 分层墙第二波架构审计（契约程序集 / 登记表实例 / Mod 端口）

**当时对象：** [#964](https://github.com/MightyBubble/Ludots/pull/964) @ `29b951470`（相对 `origin/main` @ `46fcd9dcda`）  
**现在怎样：** [图能力唯一入口](../../gitbook/architecture/graph-capability-status.md)  
**需求：** 独立审这一波实现 tip，不是把 #942 十五张票重审一遍  
**设计正本：** [`s14_layering_physicalization_design.md`](s14_layering_physicalization_design.md) §3.1–§3.3、§3.6 Wave 2–4  
**前序：** [`s_plan_landed_architecture_audit.md`](s_plan_landed_architecture_audit.md)（#962）。已关的票本轮只一行复验  
**方法：** 对照源码与指定测试证伪；零生产代码  
**刻意不审：** Wave 5 整层剥 Spatial/Input/Presentation/GAS；Wave 6 删除 `GetEngine`；#942 已关票的产品争论

**事后更正：** 本页结论「可以当脚手架合，不能当墙已经砌完」仍然成立。#964 已合。Wave 5–6 仍未做。不要把本页读成「分层搞好了」。

---

## 1. 概述

### 1.1 Verdict

**FIX-FORWARD。** 可以当脚手架合，不能当墙已经砌完。S14 整票仍然**不关单**。这一波也**不能关**。

实现方把契约程序集、登记表实例、Mod 端口、分析器捆在一张 PR 里，标题写「一次落地」。对照设计，三件事都只走到前半截：

1. **契约程序集。** 拆出了 `Ludots.Contracts` 和 `Ludots.Graph.Abstractions`。设计要的另外两份（GAS 契约、Mod 契约）没有。Core 仍是一个大程序集。禁边测试对着几乎空的 csproj 扫引用，绿了也不证明墙在。
2. **登记表实例。** 引擎上有一份 `ModRegistrySet`，装图时空表检查会失败关闭。冻结在单张表上是单向的。但六个门面仍有 `Clear()`，做的是换一张新表——旧表洗白换成新表，不是「要空表就 `new ModRegistrySet()`」。进程里还有一份 `ModRegistryAmbient`，后绑的引擎会盖掉先绑的。隔离测试只造了两份 set，没有两台引擎。
3. **Mod 端口。** 上下文上多了「登记系统 / 登记表」两个口，宿主会绑上。展厅一行都没改，203 处仍拿整台引擎。拿引擎次数棘轮仍顶在 205。服务 / 配置 / 空间三个口没做。

gitbook 合同没有改成「已落地」。设计文把头改成「实现进行中、本实现补 Wave 2–4」——这是实现方扩大本票范围，不是把分层写成已经完工。

没有玩家门回开。没有新的「一行配置杀死进程」。

### 1.2 维护者一句话

打开工程：能看见两份薄契约程序集，Core 还是原来那一坨。写 Mod：手里多了两个口，但今天没人用；仍能拿到整台引擎。开两张地图：表可以做成两份，可是全进程只认最后绑上的那一份。冻结之后不能在同一张表上解冻，但调用门面 `Clear()` 等于换一张没冻过的新表。

### 1.3 关单表

| 项 | 实现方声称 | 本轮 |
|----|------------|------|
| 契约程序集 | 帧阶段、跨层身份、图指令拆到独立程序集 | **合入不关。** 2/4 份契约；Core 未拆；禁边测试空转 |
| 登记表实例 | 身份表跟引擎走；冻结后不能解冻；装图时表不空就失败关闭 | **合入不关。** 引擎持有 set、装图空表检查成立；`Clear()` 还在；环境表是进程全局 |
| Mod 端口 | 上下文补上登记系统和登记表端口 | **合入不关。** 口在、能绑、展厅零采用；`GetEngine` 次数未降 |
| 分析器守模拟侧标记 | 分析器守模拟侧拥有的标记不被表现层乱写 | **合入不关。** 分析器是真的，但没挂进 Core/Mod 编译 |
| S14 整票 | 设计写明 Wave 5–6 不在本实现 | **不关单**（与 #962 一致） |
| #962 已关票 | — | **一行复验：S1/S9 图门 9/9 仍绿。** 不重开 |

---

## 2. 结构

```text
1 概述：Verdict / 关单
2 结构（本页）
3 详情：契约程序集 / 登记表 / Mod 端口 / #962 一行复验
4 场景：维护者与作者会看见什么
5 边界
6 UAT
附录 A 测试证据
附录 B 给后续 Agent 的最短提示词
```

---

## 3. 详情

### 3.1 契约程序集

设计 §3.1.1 要四份只放接口/枚举/只读 DTO 的契约：`Ludots.Contracts`、`Ludots.Graph.Abstractions`、`Ludots.GAS.Abstractions`、`Ludots.Modding.Abstractions`。

| 程序集 | 有没有 | 实际装了什么 |
|--------|--------|--------------|
| `Ludots.Contracts` | 有 | `SystemGroup`、`SystemGroupOrder`、`[WriteOwner]`、`PresentationStableId`、`PresentationDestroyPending` |
| `Ludots.Graph.Abstractions` | 有 | `GraphInstruction`、`GraphKind`、`GraphNodeOp`（外加解析器）。没有 `IGraphIdRegistry` / `IGraphProgramRegistry` / 瘦身的 `IGraphRuntimeApi` |
| `Ludots.GAS.Abstractions` | **没有** | 属性/标签/效果接口仍在 Core |
| `Ludots.Modding.Abstractions` | **没有** | `IModContext` / 新端口仍在 Core |
| `Ludots.Core` | 仍是大程序集 | 约 1300 个实现文件；handler、编译器、效果管线没搬 |
| `Ludots.Analyzers.Layering` | 有（不是契约） | `WriteOwnerAnalyzer`；只被 ArchitectureTests 引用，不是 Analyzer 项 |

`CullState` / `VisualTransform` 没有漏进 Contracts。`GasGraphOpHandlerTable` 没有漏进 Graph.Abstractions。没有 `TypeForwardedTo` 门面，旧命名空间留在新程序集里，Mod 仍只引用 Core。

引用图测试：`typeof(SystemGroup).Assembly` 这类正向断言是真的。禁边检查只读一份几乎没有 `ProjectReference` 的 csproj，扫不到 `Ludots.Core` 就判过——这不是「L0 看不见结算、Input 看不见裁剪」。

设计原文 Wave 2 写「不拆程序集」。本 PR 提前拆了两份薄契约，没有假装拆完实现层。gitbook 未标落地。

### 3.2 登记表实例

引擎持有 `RegistrySet`，初始化时 `ModRegistryAmbient.Bind(RegistrySet)`。`IdentityTable` 没有 `Unfreeze` / `Clear`；冻结后再 `Register` 抛错。`GraphProgramConfigLoader` 不再开头清图号，表不空或已冻结就失败关闭。

设计 Wave 2 还要求：**生产 API 删除 `Clear()`**；要空表就换新 set；同进程两台引擎 id 不串。

对照：

| 设计 | @ `29b951470` |
|------|----------------|
| 引擎拥有一份 set | 成立 |
| 装图要求图号表空且未冻 | 成立 |
| `IdentityTable` 单向冻结 | 成立 |
| 生产删除 `Clear()` | **未做。** 六个门面仍 `public static void Clear()`，内部 `Replace*()` 换新表 |
| 两台引擎隔离 | **未测。** 测试造两份裸 set，证明表本身不共享；环境表是单槽，后 `Bind` 覆盖先 `Bind` |
| 测试改用新实例、不再调生产 `Clear()` | **未做。** 测试里门面 `Clear()` 仍大量存在；隔离测试自己还演示 `GraphIdRegistry.Clear()` 换表后新表未冻结 |
| `TeamManager` 实例化 | **未做。** 仍是进程静态 |
| 效果/技能装载器不再 Clear | **未做。** `EffectTemplateLoader` / `AbilityExecLoader` 仍清身份表 |

`FacadeClear_ReplacesTable_DoesNotUnfreezeTheSameInstance` 把「换表洗白」写成了合法行为。旧表确实还冻着，但调用方拿到的是一张没冻过的新表。这和「地图重载 = 新 set，不是把旧表洗白」不是同一件事。

`ModRegistryAmbient` 没有锁，也不是「加锁假装实例化」——它是更直白的进程全局转发。设计说这只是 Wave 2 前半的临时层，删转发才算收口。本 PR 停在前半。

### 3.3 Mod 端口

`IModContext` 增加 `Systems` / `Registries`。未绑定时走 `Unavailable*`，调用即抛，不是空操作。`ModLoader` / `GameEngine` 在 `OnLoad` 前绑上真实现。`RegistrySetView` 写的是绑定的 set，不是环境表——这条有测试。

| 端口 | 有 | 能用 | 展厅在用 |
|------|----|------|----------|
| `ISystemRegistrar` | 是 | 绑上之后是 | **0** |
| `IRegistrySetView` | 是 | 绑上之后是 | **0** |
| `IServiceView` | 否 | — | — |
| `IConfigView` | 否 | — | — |
| `ISpatialSession` | 否 | — | — |

`.GetEngine(` 仍是 205（mods 203 + src 2），棘轮上限仍是 205。`[Obsolete]` 还在，Mod 仍能编译通过。本 PR 对 `mods/` **零行改动**。100 处直接 `RegisterSystem` 仍无能力声明；所谓 `SystemCapability` 只活在棘轮正则里，全仓没有这个类型。

分析器 `LDTS014` 是真 Roslyn 分析器，单测用内存编译调用，不是扫文本。它没有挂进 Core 或任何 Mod 的编译。模拟相禁读裁剪也没做。这是 Wave 4 的形状，不是 Wave 4 收口。

### 3.4 #962 已关票：一行复验

本 PR 没有改查询口、退役门、血条驱动、选中、覆盖表、验收页。`mods/` 零行。不重开那些产品争论。

| 票 | 一行 |
|----|------|
| S1 / S9 | `GraphInvokeCycleTests` + `GraphFrameFrontDoorTests` **9/9 绿** |
| S2–S8 / S10–S13 / S15 | 玩家路径未动；本轮不重跑、不改关单结论 |
| S14 | 上一轮就不关；本轮仍不关 |

---

## 4. 场景

1. **维护者打开解决方案。** 能看见 `Ludots.Contracts` 和 `Ludots.Graph.Abstractions`。看不到 GAS 契约、Mod 契约。Core 仍同时装着虚拟机、编译器、技能结算、镜头裁剪。
2. **作者写 Mod，想只登记一个系统和一个标签。** 上下文上有口。今天的展厅仍 `GetEngine()` 再 `RegisterSystem`。不改展厅，这两个口等于没人走。
3. **同进程开两台引擎、各登一张同名图。** 两份 set 本身不串号。两台引擎共用一个环境槽，后启动的会盖掉先启动的。没有测试罩住这件事。
4. **有人冻结后再登记。** 同一张表会抛。有人调用门面 `Clear()`：换一张新表，可以继续登记。
5. **装图时图号表已经有名字。** 失败关闭，不再先清空再装。
6. **表现层去写模拟侧的稳定编号。** 分析器单测能抓住。正式编 Core/Mod 时分析器不跑，编译器看不见。

---

## 5. 边界

**做了：** 证伪 #964 对契约程序集、登记表实例、Mod 端口的声称；对照设计 §3.1–§3.3 / Wave 2–4；#962 已关票一行复验。

**没做：** 改生产代码；审 Wave 5 搬家；重审 #942 已关票；把合同改成「已落地」；重开 Duration/Yield / 选中读裁剪等已裁决争论。

**实现方改了设计文状态。** 头从「设计提案」改成「本实现补 Wave 2–4」。§3.6 分波表仍在：Wave 2 不拆程序集、Wave 3 迁 `GetEngine`、Wave 5 才剥层。本报告按分波表验收，不按标题「一次落地」放行。

---

## 6. UAT

```gherkin
Feature: 层与层之间的墙开始有程序集边界
  Scenario: 契约程序集只放合同
    状态: 部分过
    证据: Contracts / Graph.Abstractions 存在且无 handler；缺 GAS/Mod 契约；Core 仍是大程序集
  Scenario: 禁边在编译期存在
    状态: 未过
    证据: 禁边测试只扫薄 csproj 的 ProjectReference；Mod 仍只引用 Core

Feature: 身份表跟引擎走，不跟进程走
  Scenario: 两台引擎各登同名图，编号不串
    状态: 未过（只证明了两份裸 set）
    证据: S14RegistryIsolationTests.TwoRegistrySets_SameGraphName_DoNotShareIdentity
  Scenario: 冻结后再登记必须抛，且没有解冻
    状态: 过（同一张表）
    证据: Freeze_ThenRegister_Throws_AndHasNoUnfreezeApi
  Scenario: 生产不再用 Clear 洗表
    状态: 未过
    证据: GraphIdRegistry.Clear → ReplaceGraphIds；隔离测试把换表写成合法

Feature: 装图时空表，不空就失败
  Scenario: 图号表已有名字时装载失败
    状态: 过
    证据: LoadIdsAndCompile_FailsClosed_WhenGraphTableAlreadyHasIds

Feature: Mod 只看见上下文端口
  Scenario: 作者能用上下文登记系统和登记表，不必拿整台引擎
    状态: 口过、路未过
    证据: BindHostPorts 存在；mods/ 零采用；GetEngine 仍 205
  Scenario: 没绑端口时不能装成没这回事
    状态: 过
    证据: UnavailablePorts_Throw_InsteadOfNoOp
```

合同状态必须继续「修复中」。S14 不得关单。

---

## 附录 A — 测试证据

对象：`/tmp/s14w2/pr964` @ `29b951470`。

| 过滤器 | 结果 |
|--------|------|
| ArchitectureTests：`S14LayeringRatchetTests` + `S14RegistryIsolationTests` + `S14LayeringReferenceGraphTests` + `S14LayeringAnalyzerTests` + SystemGroup/PhaseOrder + `AttributeWriteAuthorityGuardTests` | 24 / 24 |
| GasTests：`GraphInvokeCycleTests` + `GraphFrameFrontDoorTests`（#962 一行） | 9 / 9 |

本票自带测试全绿，不能从绿推导出「墙砌完」或「两台引擎已隔离」。

---

## 附录 B — 给后续 Agent 的最短提示词

按条拆。不要合成一条巨提示词。不要改合同落地状态。不要重开 #942 已关票。

### B.1 删生产 `Clear()`，隔离改测两台引擎

```text
对照 docs/audits/s14_wave2_architecture_audit.md §3.2。
生产六个门面删除 Clear()；要空表就 new ModRegistrySet() 交给新引擎。
EffectTemplateLoader / AbilityExecLoader 不得再 Clear 身份表。
隔离测试必须构造两台 GameEngine（或两次完整初始化），同名图 id 不得经 ModRegistryAmbient 串台。
禁止：把 Replace* 改个名字留下洗表；为了绿而把 FacadeClear 测试当成合同。
该跑：S14RegistryIsolationTests；S14LayeringRatchetTests（Clear 棘轮只许下降）
```

### B.2 拆掉环境表转发

```text
对照设计 §3.2.3 第 4 步。
Loader / 静态门面改为显式注入 ModRegistrySet，删除 ModRegistryAmbient。
不得在静态类上加锁假装实例化。
该跑：S14RegistryIsolationTests；GraphProgramConfigLoader 相关测试
```

### B.3 迁第一批 GetEngine（只做 M1 或 M2）

```text
对照设计 §3.3.3。只迁一批：World/已有 CoreServiceKeys，或 RegisterSystem 走 context.Systems。
棘轮 MaxGetEngineCalls / MaxUndeclaredModRegisterSystemCalls 只许下降。
禁止：本票拆 csproj；本票删 GetEngine；发明 SystemCapability 类型却不接线。
该跑：S14LayeringRatchetTests；被迁的那一个 Mod 的验收测试
```

### B.4 引用图测试要能失败

```text
对照 S14LayeringReferenceGraphTests.AssertNoForbiddenProjectReferences。
禁边必须打在实现程序集上（Core / 未来的 Input / Presentation），或断言程序集引用图，不要只扫两份薄契约 csproj。
分析器要进 Core 或 ci-gate 的 Analyzer 引用，不要只给 ArchitectureTests 当普通 ProjectReference。
禁止：为了绿放宽禁边名单。
该跑：S14LayeringReferenceGraphTests；S14LayeringAnalyzerTests
```

### B.5 Wave 5 剥层（独立票，不要和上面捆）

```text
对照设计 §3.6 Wave 5。一次只剥一层。
本票才允许大搬家。禁止在登记表 Clear 还在、环境表还在的时候剥层。
gitbook 仍不标分层已落地。
```
