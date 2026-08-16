# 实时技能工作台架构契约

Parent: [Epic #615](https://github.com/MightyBubble/Ludots/issues/615)。本页是 Real-time Skill Workbench（LSW-1，[#616](https://github.com/MightyBubble/Ludots/issues/616)）的正式架构 SSOT。后续实现单 #617–#625 必须遵守本页；不得在 `docs/adr/` 另建平行 ADR。

玩家目标一句话：我改了技能或属性之后，必须立刻知道它是已经生效、下次释放才生效、需要重进地图，还是必须重启；失败时要看到原因，当前对局不能被错误配置污染。

## 1. 概述

当前引擎只有很窄的配置重载入口：`GameEngine.ReloadConfigs(...)` 会重读 catalog、导航 agent profiles，并对部分 AI / Narrative 做处理。玩家最想热调的 GAS ability、effect、Graph、Attr、Tag 等玩法数据，目前不会真正热应用。

本契约把目标从“文件变了就 reload”升级为 **实时技能工作台**：

- 热调技能数值，并明确生效时机。
- 调试选中角色属性。
- 观察一次技能释放的效果链。
- 让 AI 生成技能草稿，先试玩再保存成 Mod。

核心原则：

- 可安全热应用的内容，明确应用并可观察。
- 不能安全热应用的内容，明确提示需要重进地图或重启。
- 错误配置不能污染当前运行时。
- 禁止 silent fallback、静默失败、半提交 registry、热路径 ECS 结构变更、会话内 id remap。
- ECS 热路径遵守 SoA、0Alloc、Chunk 迭代、command buffer。

建议新增主线能力名：`LiveGasEditPipeline`。它不是 `ReloadConfigs` 的大 if 分支，而是一条正式的编辑 → 验证 → 分级 → 应用 → 观察 → 保存闭环。

## 2. 结构

```text
手动工作台编辑
文件变化
AI 生成草稿
        |
        v
  实时编辑会话 (session id / revision / source / rollback)
        |
        v
  调试补丁层 (Debug Patch) —— 表达“想改什么”，不直接写 live registry
        |
        v
  候选 GAS 编译验证 (stage，不 clear live registry)
        |
        v
  热应用分级报告
        |
        +-- ImmediateCommand --------> 运行时调试命令（属性 set/add）
        +-- NextCastLiveApply -------> 安全帧应用队列 -> 运行时定义仓库
        +-- MapReloadRequired -------> 明确提示重进地图
        +-- EngineRestartRequired ---> 明确提示重启游戏
        |
        v
  技能 / 效果 / Graph 执行
        |
        v
  效果链追踪器
        |
        v
  工作台面板 / Showcase
        |
        v
  （可选）接受草稿并保存为 Mod 配置
```

### 2.1 复用清单（禁止另造平行管线）

| 能力 | 必须复用 |
|------|----------|
| 配置来源、合并、冲突 | `ConfigPipeline`、`ConfigCatalog`、`ConfigConflictReport` |
| GAS 定义加载与编译 | 现有 Ability / Effect / Graph / Attr / Tag loader、registry、compiler |
| 运行时服务发布 | `CoreServiceKeys` typed service |
| 属性调试写入 | `AttributeMutationOps`、`AttributeBuffer` |
| 效果链观察入口 | `GasPresentationEventBuffer`、`ResponseChainTelemetryBuffer`、`GameplayEventBus`、`EffectRequestQueue` |
| 地图级重建 | `MapManager.LoadMap` / map lifecycle（`GameEngine.LoadMap`） |
| 现有窄重载 | `GameEngine.ReloadConfigs` 仅保留其既有职责，不扩展成 GAS 热应用入口 |

不得新建平行配置格式、平行 registry、平行属性写入路径，或绕过 ConfigPipeline 的私有热加载器。

### 2.2 与现有 GAS 分层的关系

工作台不改变 [GAS 分层架构](gas-layered-architecture.md) 的 phase 顺序。热应用只替换稳定 id 下的定义内容或提交运行时命令；执行仍走 `AbilityActivation → EffectProcessing → AttributeCalculation → DeferredTriggerCollection`。

## 3. 详情

### 3.1 编辑来源（Edit Sources）

所有改动先进入同一编辑会话，正式 Mod 文件在编辑阶段不被直接修改。

| 来源 | 含义 | 进入方式 |
|------|------|----------|
| ManualWorkbench | 工作台 UI / CLI 手动改值 | 直接写入调试补丁 |
| FileChange | 监视到的配置文件变化 | 解析为补丁，不直接 reload live GAS |
| AiGeneratedDraft | AI 生成的技能草稿 | 必须先变成结构化调试补丁，再走同一 stage / validate / classify |

会话必须记录：`sessionId`、`revision`、来源、改动摘要、验证结果、应用状态、回滚点。丢弃会话不得留下半提交状态。

### 3.2 调试补丁层（Debug Patch）

补丁表达“想改什么”，不是直接改 registry：

- 技能数值：伤害、蓝耗、冷却、范围、持续时间等。
- 效果数值：modifier、duration、period、configParams 等。
- 选中角色属性命令：Health / Mana / MoveSpeed 等运行时值的 set/add。
- 已有 Graph body：图名 / id 不变，只改内部逻辑或常量。
- 已有 Tag rule：tag id 不变，只改规则体。
- 已有 Attr constraints：属性身份不变，只改 min/max 等约束值。

补丁必须是结构化数据，禁止用自由文本冒充可应用改动。无效补丁 fail-fast，并给出可读错误。

### 3.3 候选编译与热应用分级（Apply Modes）

一次提交必须先 stage，再 classify，再 commit：

1. **Stage**
   - 合并相关配置或补丁。
   - 编译成候选 Ability / Effect / Graph / Tag / Attr 定义。
   - 校验引用、id 稳定性、Graph 输出结构、Attr/Tag 身份。
   - 生成用户可读报告。
   - 编译期禁止 `Clear + Register all` 清空 live registry。

2. **Classify**（四种应用模式）

| 模式 | 何时使用 | 用户看到的话 |
|------|----------|--------------|
| `ImmediateCommand` | 选中角色属性等运行时调试命令 | 立即生效（调试命令，默认不写 Mod） |
| `NextCastLiveApply` | 技能/效果数值、已有 Graph body、已有 Tag rule、已有 Attr constraints | 下一次释放生效 |
| `MapReloadRequired` | 地图实体、模板、地形、导航、现有实例结构需要重建 | 需要重进地图 |
| `EngineRestartRequired` | Mod DLL、`mod.json`、加载顺序、系统注册、新 C# handler、新 Graph op | 需要重启游戏 |

3. **Commit**
   - 仅在主线程安全点提交。
   - 只能替换 immutable snapshot，或稳定 id 下的定义内容。
   - 失败则旧版本继续工作；报告必须写明失败原因。
   - 禁止半提交：任一候选失败，整次 commit 回滚到旧 snapshot。

### 3.4 运行时定义仓库约束

当前不少 loader 是 `Clear + Register all`，不适合运行期热应用。正式热应用入口是 `LiveGasEditPipeline`（`CoreServiceKeys.LiveGasEditPipeline`），不是 `GameEngine.ReloadConfigs`。

已落地的稳定 id 替换（#622 首批全量）：

- `GraphProgramRegistry.ReplaceProgram`：同 id、同 kind 替换 Graph body（`NextCastLiveApply`，安全帧提交）。
- `EffectTemplateRegistry.TryReplaceHotNumericField`：`duration.durationTicks` / `duration.periodTicks`（`NextCastLiveApply`）。
- `TagOps.ReplaceTagRuleSet`：同 tagId 覆盖规则体；候选编译走 `TagRuleSetLoader.CompileRuleSetForHotApply`（**禁止** Register 新 tag 名）。
- `AttributeRegistry.ReplaceConstraints`：同 attributeId 替换已有 `constraints.min` / `constraints.max`（禁止热路径自动注册新属性）。
- 选中角色属性：`ImmediateCommand` → `ILiveAttributeCommandSink` → `AttributeMutationOps`。
- Graph/Tag/Attr 身份变更或新 id → `EngineRestartRequired`；未知 effect / constraint 字段 → `MapReloadRequired`。
- `#874` 的 `ReloadConfigs(GAS)` 演示捷径**不是**正式路径；正式入口仅 `LiveGasEditPipeline`。

硬约束：

- 编译候选时不得清空 live registry。
- id / name 映射在会话内必须稳定。
- rename / delete / remap 默认拒绝，或归类为 `MapReloadRequired` / `EngineRestartRequired`。
- steady-state 读取不得为热调试引入持续分配。

### 3.5 角色属性调试命令

改角色属性不走文件 reload：

1. 用户选择明确实体或实体集合。
2. 工作台提交属性 set/add 命令。
3. 命令走 `AttributeMutationOps`，保留 clamping、dirty flags、trigger、presentation event。
4. 默认不保存为 Mod；只有后续显式“转成 authored default / 保存草稿”才落盘。

### 3.6 效果链追踪器

每次技能释放生成 `traceId`，串起：

- cast started / committed / failed
- effect request / applied / activated
- Graph program id/name 与关键输出
- attribute delta
- tag grant/remove 或 effective changed
- response chain window / proposal / resolved
- presentation events

必须有容量上限和 dropped event 报告；禁止悄悄丢关键事件。观察入口复用现有 buffer / bus，不另造平行遥测总线。

### 3.7 AI 草稿生成

AI 不能直接改世界，也不能直接写正式 Mod。

AI 输出必须变成结构化调试补丁，走同一套 stage → validate → classify → apply。通过后可临时绑定到选中角色或 debug slot 试玩。满意后再走“保存为 Mod 配置”。

### 3.8 第一阶段支持 / 不支持

**支持（first-scope supported）：**

- 技能 / 效果数值热调（`NextCastLiveApply`）
- 选中角色属性运行时调试命令（`ImmediateCommand`）
- 已有 Graph body 编辑（身份不变）
- 已有 Tag rule body 编辑（身份不变）
- 已有 Attr constraints 值编辑（身份不变）
- AI 草稿生成、预检、临时试玩
- 接受草稿并保存为 Mod 配置（后续切片）
- 可玩 Showcase 与 Cucumber UAT（后续切片）

**不支持（first-scope unsupported）：**

- 删除 / 重命名 Ability、Effect、Graph、Attr、Tag id
- 改变 Attr / Tag / Graph 身份
- 改变 Graph 输出结构
- 热加载 C# handler 或 Graph op
- 热替换 Mod DLL、`mod.json`、加载顺序、launcher graph
- 会话内 id remap
- 热路径 ECS 结构变更
- 错误配置静默忽略或自动 fallback

### 3.9 依赖与后续切片映射

本页只定义契约；实现按下列切片推进，不得在本契约 PR 中实现代码：

| Issue | 切片 | 契约落点 |
|-------|------|----------|
| #617 LSW-2 | 调试补丁层与编辑会话 | §3.1、§3.2 |
| #618 LSW-3 | 候选 GAS 编译与热应用分级报告 | §3.3 |
| #619 LSW-4 | 安全帧应用与下一次释放生效 | §3.3 Commit、§3.4 |
| #620 LSW-5 | 选中角色属性调试命令 | §3.5 |
| #621 LSW-6 | 效果链追踪器与可读时间线 | §3.6 |
| #622 LSW-7 | Graph / Tag / Attr 安全热应用首批支持 | §3.8 支持项 |
| #623 LSW-8 | AI 技能草稿生成、预检与试玩绑定 | §3.7 |
| #624 LSW-9 | 接受草稿并保存为 Mod 配置 | §3.1 落盘边界、§3.7 |
| #625 LSW-10 | 可玩 Showcase 与 Cucumber UAT | §4、§6 |

## 4. 场景

### 场景 A：热调技能数值

策划把火球伤害从 50 改到 80，点击预检。工作台显示“下一次释放生效”。点击应用后，再放火球，敌人受到 80 伤害。已经在飞的旧投射物不被偷偷改写。

### 场景 B：改角色属性

玩家选中一个单位，把生命设成 100。单位生命立即变化，UI / trigger / presentation 都通过正式属性变更链路观察到变化。工作台说明这是运行时调试命令，不会写入 Mod。

### 场景 C：追技能 bug

玩家释放连锁技能后发现伤害不对，打开效果链时间线，看到 cast、effect、Graph、attribute delta、response chain 每一步。点击某一步能看到来源定义和关键输入输出。

### 场景 D：AI 搓技能

玩家输入“做一个小范围冰冻技能”。AI 生成草稿，系统编译并展示风险。通过后临时绑定到选中角色技能槽，玩家试玩。满意后保存到当前 Mod。

### 场景 E：不安全改动被拒绝

作者把 `State.Burning` 改名成 `State.Fire`。工作台拒绝立即应用，提示标签身份变化需要重进地图或重启。当前战斗继续使用旧稳定版本。

## 5. 边界

### 5.1 本契约负责

- 定义编辑来源、补丁层、候选编译、四种应用模式、观察与保存边界。
- 钉死复用清单与禁止事项。
- 作为 #617–#625 的实现判断口径。

### 5.2 本契约不负责

- 实现 `LiveGasEditPipeline` 代码（#617 起）。
- 扩展 `ReloadConfigs` 去热应用 GAS。
- 新建平行 ADR 或平行配置格式。
- 商业引擎 adapter 私有热更语义。
- 把 showcase 做成技术日志堆叠。

### 5.3 硬性红线

- 禁止 silent fallback / 静默失败。
- 禁止半提交 registry（失败必须整次回滚）。
- 禁止热路径 ECS 结构变更与内存飞线。
- 禁止会话内 id remap。
- 禁止 AI 或文件监视直接写 live registry / 正式 Mod。
- 候选验证失败时，运行中游戏必须继续使用旧版本，并显示原因。

## 6. UAT

```gherkin
Feature: 实时技能工作台架构口径
  Scenario: 策划看到一个改动是否能立即试玩
    Given 策划正在调整一个技能
    When 策划提交一次改动
    Then 工作台明确显示该改动是立即生效、下一次释放生效、需要重进地图、还是需要重启游戏
    And 如果改动不能生效，工作台显示原因
    And 当前运行中的游戏不会被错误配置污染
```

```gherkin
Feature: 实时调试技能数值
  Scenario: 策划修改火球伤害后立即试玩
    Given 玩家选中了一个会释放火球的角色
    And 火球当前命中敌人造成 50 点伤害
    When 策划在工作台把火球伤害改成 80 并点击应用
    And 玩家再次释放火球命中同一个敌人
    Then 敌人受到 80 点伤害
    And 工作台显示本次生效版本号
    And 已经在飞的旧火球不会被偷偷改成新伤害
```

```gherkin
Feature: 调试选中角色属性
  Scenario: 策划把选中单位生命调满
    Given 玩家选中了一个生命值为 25 的单位
    When 策划在工作台把生命值设为 100
    Then 该单位生命值变为 100
    And 工作台显示这是一次运行时调试命令
    And 正式 Mod 配置没有被修改
```

```gherkin
Feature: 查看技能效果链条
  Scenario: 玩家追踪一次异常技能释放
    Given 玩家开启了技能链路追踪
    When 玩家释放一次连锁闪电
    Then 工作台显示从释放、命中、属性变化到响应链触发的完整时间线
    And 玩家能看到每一步来自哪个技能、效果或 Graph
    And 如果追踪缓冲满了，工作台明确显示有事件被丢弃
```

```gherkin
Feature: AI 生成技能草稿
  Scenario: 玩家让 AI 生成一个冰冻新技能并试玩
    Given 玩家打开实时技能工作台
    When 玩家输入“做一个小范围冰冻技能”
    And AI 生成技能草稿
    Then 系统先显示编译结果和风险
    And 正式 Mod 文件尚未被修改
    When 玩家点击试玩
    Then 选中角色获得一个临时技能槽
    And 释放后目标被减速或冻结
```

```gherkin
Feature: 禁止不安全热应用
  Scenario: 玩家重命名运行中的标签
    Given 当前地图中已有单位带有 "State.Burning" 标签
    When 玩家把标签重命名为 "State.Fire"
    Then 工作台拒绝立即应用
    And 明确提示该改动需要切图或重启
    And 当前战斗不会静默使用错误标签
```

## 代码锚点（实现时核对，本契约不改代码）

- `src/Core/Config/ConfigPipeline.cs`
- `src/Core/Config/ConfigCatalog.cs`
- `src/Core/Config/ConfigConflictReport.cs`
- `src/Core/Scripting/CoreServiceKeys.cs`
- `src/Core/Gameplay/GAS/AttributeMutationOps.cs`
- `src/Core/Gameplay/GAS/Components/AttributeBuffer.cs`
- `src/Core/Gameplay/GAS/Presentation/GasPresentationEventBuffer.cs`
- `src/Core/Gameplay/GAS/ResponseChainTelemetryBuffer.cs`
- `src/Core/Gameplay/GAS/GameplayEventBus.cs`
- `src/Core/Engine/GameEngine.cs`（`ReloadConfigs` / `LoadMap`）
- `src/Core/Map/MapManager.cs`

## 相关文档

- [GAS 分层架构](gas-layered-architecture.md)
- [Mod 架构](mod-architecture.md)
- [运行时总览](runtime-overview.md)
- Epic #615、契约单 #616、实现切片 #617–#625
