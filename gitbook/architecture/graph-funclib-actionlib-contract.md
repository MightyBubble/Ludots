# 图复用库合同补丁：FuncLib / ActionLib 与 Kind 表达力

状态：已落地（`GAS/action_lib.json` + `GraphActionCatalog`；FuncLib `purity`/禁 Yield；Effect `BranchBool` + 线性 `InvokeScript`；L2 Showcase 走 ActionLib；next-chain `GraphCompiler` 已移除）。对照评审仍以本页 + [图分层](graph-layering-flow-and-behavior.md) 为准。  
关联：#861 作者 SSOT / FuncLib；L0/L1/L2 分层；效果生命周期（Duration/Period）。

---

## 1. 概述

当前仓库已有「一种作者边模型 + 一台 VM」，但复用面仍混用：

- `func_lib.json` + `InvokeScript` 只指向 `Script`，且嵌套调用禁止 Yield；
- Script 在行为树叶子里又可 Yield——名义仍叫 FuncLib，实际已是「可挂起动作」；
- Effect 阶段图禁止 Yield（正确），但作者侧几乎是直线流水线，缺分支与可调纯函数；
- 文档写明不做编译期 Macro，却没有正式的 ActionLib 产品边界。

本补丁把复用拆成两条合同，并对齐效果时间轴与各 Kind 表达力：

1. **FuncLib**：无 Yield、无（或受控极少）世界副作用的纯函数库——固定输入/输出，供各 L1 Kind 调用。  
2. **ActionLib**：可 Yield、可改世界的动作库——只挂在能承载跨拍/副作用的宿主（L2、Script 切片），不得嵌进 Effect 事务中途挂起。  
3. **Duration / Period** 继续由效果生命周期数据驱动，**不用** Effect 图内 Yield 冒充。  
4. **Effect 阶段内**至少获得：分支，或调用 FuncLib 纯函数（二选一最小集，推荐两者都有）。

---

## 2. 结构

```text
效果壳（模板）
  durationTicks / periodTicks / Clock
        │ 到点再触发阶段（跑完即停）
L1 阶段图 Effect / Query / Score / Validation / Derived
        │ 可调用 ▼
FuncLib（纯：Script|Score|Validation，无 Yield，明确 I/O）
        │
L2 BT / HFSM / Level ──叶子──► ActionLib（Script，可 Yield，可副作用）
        │                         ▲
        └──── 不得从 Effect 事务 Invoke 挂起 Action ──┘
```

| 库 | 作者资产 | 运行合同 | 谁可以调用 |
|----|----------|----------|------------|
| FuncLib | `GAS/func_lib.json`（可扩展 kind 列） | 无 Yield；纯计算/纯判断；失败关闭 | Effect、Query、Score、Validation、Derived、Script、ActionLib |
| ActionLib | `GAS/action_lib.json`（新建 SSOT） | 可 Yield；可改世界；须由切片宿主续跑 | 仅 L2 叶子、关卡 `RunScript`、显式 Script 切片宿主 |

Macro：仍不提供「编译期文本展开宏」；ActionLib 是**登记的可调用图**，不是粘贴展开。

---

## 3. 详情

### 3.1 Duration / Period（澄清，非变更）

| 问题 | 合同答案 |
|------|----------|
| Effect 禁止 Yield，还能自定义持续时间吗？ | **能。** 写在效果模板 `durationTicks` / `periodTicks`（及 clock），由 `EffectLifetimeSystem` 计时。 |
| 「每隔 N 拍跳一次」怎么自定义逻辑？ | 到点触发 **OnPeriod** 阶段图（或 preset builtin），每次 **RunToHalt**；不是同一图 Yield 醒来。 |
| 禁止在 Effect 图里用 Wait/Yield 模拟 DoT 时间轴 | **硬禁。** 时间轴在壳上，图只负责「这一拍结算什么」。 |

### 3.2 Kind 角色（纯函数 vs 阶段片段）

| Kind | 合同角色 | Yield | 作者控制流 |
|------|----------|-------|------------|
| Score | 纯函数：上下文 → `F[0]` 分数 | 禁 | 线性 + **可调 FuncLib**；可不扩分支糖 |
| Validation | 纯函数：默认拒绝，写出通过位 | 禁 | 同上 |
| Query | 准纯管道：查/滤/聚 → Summary/Collection | 禁 | 保留 `list` 值边；可不扩 While 糖 |
| Derived | 公式：读属性 → `WriteSelfAttribute` | 禁 | 线性 + FuncLib；禁副作用 API |
| Effect | **有副作用的阶段片段** | 禁 | 见 3.4 |
| Script（FuncLib 条目） | 纯函数体 | 禁（作为 FuncLib 被调时） | 可有分支，但入口须声明 `purity=pure` |
| Script（ActionLib 条目） | 可挂起动作 | 允许 | 完整糖；仅切片宿主调用 |

Score / Validation / Query / Derived **接受为纯或准纯**——本补丁确认，不是疏忽。

### 3.3 FuncLib 合同

**登记**

- 资产：`GAS/func_lib.json`：`name` / `graph` / `kind` / （新增）`purity`（默认 `pure`，非 pure 拒绝进 FuncLib）。  
- 允许 kind：`Script`（pure）、`Score`、`Validation`。（Derived 若暴露为库函数须另开评审；默认不进 FuncLib。）  
- 加载后校验：目标图无 `Yield`/`Wait`；`GraphKindOperationPolicy` 与 purity 一致；未登记名失败关闭。

**调用**

- 作者节点：`InvokeFunc`（或保留 `InvokeScript.functionName` 但语义改为「只调 FuncLib」——实现时二选一，禁止两套名字并存）。  
- **所有 L1 Kind 前门白名单必须包含该调用节点**（含 Effect 线性方言）。  
- 被调图：一次 RunToHalt；禁止嵌套 Yield；CallStack 由调用方提供。  
- I/O：以值边/寄存器约定为 SSOT（至少文档化：整型/浮点/布尔/实体入参槽与返回槽）；禁止靠「读全局黑板」冒充纯函数输入（黑板只读若保留须在条目上显式声明 `readsBlackboard`，默认 false）。

### 3.4 ActionLib 合同

**登记**

- 新资产：`GAS/action_lib.json`：`name` / `graph` / `kind=Script`。  
- 目标图允许 Yield；允许副作用（在 Script/宿主策略内）。  
- 不得与 FuncLib 同名。

**调用**

- 作者节点：`InvokeAction`（仅 Script 切片宿主、L2 绑定配置可解析名字→GraphId）。  
- **Effect / Score / Validation / Query / Derived 前门不得出现 InvokeAction。**  
- L2：BT 叶子 / HFSM OnEnter·OnTick·OnExit / Level `RunScript` 优先写 Action 名或 GraphId，解析只走 ActionLib 或 Registry，禁止私藏程序宇宙。  
- 续跑：宿主持有 `GraphExecutionCursor` + 寄存器；跨拍由宿主调度，不是 Effect 事务。

### 3.5 Effect 阶段表达力（补缺口）

最小必须落地其一，推荐两项都做：

| 项 | 合同 |
|----|------|
| A. 调 FuncLib | Effect 线性白名单包含 FuncLib 调用；阶段内可复用纯计算/纯判断 |
| B. 阶段内分支 | Effect 允许 `BranchBool`（或等价 JumpIfFalse 作者糖），**仍禁止** `Wait`/`While`/`Yield` |

禁止用「Effect 内 While+Wait」模拟 Period。  
复杂跨拍行为：拆成多个效果阶段、或 L2 Action，不要塞进单次 Effect 事务图。

### 3.6 与现状差异（实现债清单）

| # | 现状 | 补丁后 |
|---|------|--------|
| 1 | 只有 FuncLib，且 callee 必须 Script | FuncLib 纯函数；ActionLib 可挂起 Script |
| 2 | InvokeScript 嵌套禁 Yield，但 Script 又可当叶子 Yield | 按库拆开：同名不得跨库 |
| 3 | Effect 线性白名单无 InvokeScript、无分支糖 | Effect 可调 FuncLib；可选 BranchBool |
| 4 | 文档「无 Macro」 | 保持无文本宏；ActionLib ≠ Macro 展开 |
| 5 | Duration/Period 已在生命周期 | **不变**；写入作者手册避免再误用 Yield |

### 3.7 实现切片建议（非本文件编码范围）

1. 合同合入文档门户 + 守卫测试名额。  
2. `action_lib.json` + catalog + 加载顺序（graphs → func_lib → action_lib → patch）。  
3. 拆调用 opcode/作者节点：`InvokeFunc` / `InvokeAction`（或等价，禁止模糊重载）。  
4. Effect 前门白名单补 FuncLib + BranchBool。  
5. L2/Showcase 零旁路改绑 ActionLib。  
6. 迁移：现有 `func_lib` 中含 Yield 的条目移入 ActionLib（失败关闭，禁止静默）。

---

## 4. 场景

1. **灼烧 DoT**  
   模板 `durationTicks=300`、`periodTicks=30`；OnPeriod 图调 FuncLib「读抗性算伤害」再 Apply——作者不写 Wait。

2. **技能命中分支**  
   OnApply Effect 图：`BranchBool`（有盾/无盾）或先 `InvokeFunc` 算「是否破韧」再直线结算。

3. **自动施法选招**  
   Score 图纯打分，可 `InvokeFunc` 共用「距离衰减」函数；不进 ActionLib。

4. **哨兵警戒**  
   HFSM OnTick → ActionLib「警戒一步」（内含 Yield）；不得从某次 Effect OnApply 里 InvokeAction。

5. **喝水直到满（Showcase）**  
   原子 Script 沙盒走 ActionLib 或 Script 切片宿主；不登记进 FuncLib。

---

## 5. 边界

- 禁止平行第二套 VM / 第二套作者边模型。  
- 禁止 Effect 事务中途 Yield / InvokeAction。  
- 禁止 FuncLib 条目含 Yield，或未声明即产生 ApplyEffect/关系/订单等副作用。  
- 禁止 ActionLib 与 FuncLib 同名；禁止缺文件/缺条目静默空表。  
- 禁止用 ActionLib 替代效果 Duration/Period 时间轴。  
- 禁止编译期文本 Macro 展开冒充库。  
- L2 不得私藏 Dictionary 程序宇宙（沿用 #861 零旁路）。  
- 热路径 0-alloc；CallStack 调用方自备。

---

## 6. UAT

```gherkin
Feature: 效果时间轴与阶段图分工
  作为技能作者
  我希望用持续时间和跳动间隔配置灼烧
  以便每一跳结算逻辑可定制，而不用在图里自己「睡 N 拍」

  Scenario: 用模板字段而不是 Yield 定义 DoT
    Given 我创建一条灼烧效果并设置持续 10 秒、每 1 秒跳一次
    And OnPeriod 阶段绑定一张结算图
    When 战斗进行超过 1 秒
    Then 系统应再次执行 OnPeriod 结算图
    And 该结算图不得包含 Wait 或 Yield 节点
    And 靶子应按跳动次数受到对应结算

Feature: 纯函数库可被技能阶段复用
  作为技能作者
  我希望把「按距离衰减伤害」做成可复用纯函数
  以便多个技能阶段图共同调用且不会挂起技能事务

  Scenario: Effect 阶段调用 FuncLib
    Given FuncLib 中登记了名为 damage.falloff 的纯函数且不含 Yield
    And 某技能的 OnApply 图调用该函数
    When 技能命中并进入 OnApply
    Then 衰减计算应生效并完成当次阶段
    And 技能效果事务不得因该调用而跨拍挂起

  Scenario: 含 Yield 的图不能进 FuncLib
    Given 作者试图把含 Wait 的图登记进 FuncLib
    When 配置加载或校验运行
    Then 加载必须失败并指出该条目违反纯函数合同

Feature: 可挂起动作只给行为调度用
  作为关卡/AI 作者
  我希望巡逻「走一步再想」可以跨拍
  同时保证技能结算不会调用这种动作

  Scenario: 行为树叶子调用 ActionLib
    Given ActionLib 中登记了 bt.patrolStep 且允许 Yield
    And 行为树叶子绑定该动作
    When 代理执行该叶子且当拍未完成
    Then 叶子应在下一拍从断点继续
    And 玩家应看到代理继续完成巡逻一步

  Scenario: 技能阶段不能调用 ActionLib
    Given 作者在 Effect 图中写入 InvokeAction
    When 图通过作者前门编译
    Then 编译必须失败
    And 失败原因应说明 Action 不得进入效果事务

Feature: 纯 Kind 保持纯函数语义
  作为自动施法作者
  我希望打分图只产出分数
  以便选招稳定可测

  Scenario: Score 图产出分数且可调 FuncLib
    Given Score 图调用 FuncLib 中的距离衰减函数
    When 系统对两个候选执行打分
    Then 每个候选应得到一个分数
    And 打分过程不得 Yield
    And 不得对世界施加技能效果类副作用
```

---

## 修订记录

| 日期 | 说明 |
|------|------|
| 2026-08-12 | 初稿：由 PR#895 图基建讨论收敛（Duration/Period 澄清、FuncLib/ActionLib 拆分、Effect 表达力最小补丁） |
