# 相位系统开发计划

Status: Proposed
Last Updated: 2026-04-16

## 1. 目标

本计划把当前 RFC 与相位系统设计稿拆成可执行的开发任务，供不同开发者并行接手。

目标不是一次性完成全部 performer 重构，而是分阶段交付：

- 先把 phase system 边界和契约立住
- 再让 performer 能消费 phase result
- 最后逐步迁移 model / animator / HUD / indicator 等行为

## 2. 总原则

所有开发任务必须遵守以下约束：

1. 不把 `selection` 混入 phase system
2. 不把 visibility truth 放进 `Performer`
3. 不把 `player` / `team` 硬编码成唯一观众模型
4. 不复制多个 performer 实例来表达多观众差异
5. 不把 `PresentationRequest` 变成 orchestration state 容器
6. performer 参数透传、联动、投影只允许扩展现有 `bindings + override + set-param` 机制
7. 不再新增第二套 performer 参数黑板、linkage context 或隐式继承容器
8. 不以“只有测试通过”作为完成标准，必须提供 UAT 与用户第一视角可见证据

## 3. 开发分期

### Phase A: 契约冻结与基建摸底

目标：

- 冻结 phase system 的 Core 边界
- 明确上游输入来自哪些现有基建
- 给后续开发者一个不会跑偏的接口面

任务包：

1. 定义 phase contract 草案
- 建议文件：
  - `src/Core/Presentation/Perform/PerformAudienceContext.cs`
  - `src/Core/Presentation/Perform/PerformPhaseInput.cs`
  - `src/Core/Presentation/Perform/PerformPhaseResult.cs`
- 要求：
  - 只表达 performer 需要消费的输入与结果
  - 不直接嵌入 selection 概念
  - 不直接绑定 adapter-facing 类型

2. 梳理 phase input 的上游来源
- 必须对齐当前可复用基建：
  - `src/Core/Gameplay/Components/IdentityComponents.cs`
  - `src/Core/Gameplay/Teams/TeamManager.cs`
  - `src/Core/Gameplay/Teams/TeamEntityLookup.cs`
  - `src/Core/Gameplay/Relationships/RelationshipRuntime.cs`
  - `src/Core/Presentation/Components/CullState.cs`
  - `src/Core/Presentation/Rendering/LODLevel.cs`

3. 补充架构测试护栏
- 建议文件：
  - `src/Tests/ArchitectureTests/PerformContractsAndLegacyLaneTests.cs`
  - 新增 `PhaseSystemArchitectureTests.cs`
- 要求：
  - phase contract 不依赖 selection runtime
  - phase contract 不依赖 request flush 层
  - performer 只消费 phase input，不拥有 visibility truth
  - performer 参数联动仍以现有参数黑板为真相

完成标准：

- phase contract 进入 Core
- 架构护栏测试通过
- 文档与契约命名一致

建议负责人：

- 1 名 Core 架构开发

## 4. Phase B: Phase Resolver 接入

目标：

- 建立从上游 identity / relation / visibility 输入到 phase result 的统一解析层

任务包：

1. 新建 phase resolver
- 建议文件：
  - `src/Core/Presentation/Perform/PerformPhaseResolver.cs`
- 职责：
  - 输入 owner entity
  - 输入 viewer / audience context
  - 读取 team / relationship / cull / vision / debug inputs
  - 输出 `PerformPhaseResult`

2. 定义 audience 输入入口
- 注意：
  - 不要直接拍死成 `PlayerId + TeamId`
  - 应允许至少从 entity 或上游 projection context 进入
- 可先留为中性上下文类型，例如：
  - `PerformAudienceContext`

3. 建立最小 phase matrix
- 第一批只覆盖：
  - full visible
  - reduced visible
  - hidden
  - observer/debug

完成标准：

- phase resolver 有最小可运行实现
- 可以稳定产出 phase result
- 不依赖 selection 相关基建

建议负责人：

- 1 名 Core runtime 开发

依赖：

- 必须在 Phase A 完成后开始

## 5. Phase C: Performer 消费相位结果

目标：

- performer 从“直接读零散 visibility 条件”改为“消费 phase result”

任务包：

1. 在 performer runtime 中接入 phase result
- 重点文件：
  - `src/Core/Presentation/Systems/PerformerRuntimeSystem.cs`
  - `src/Core/Presentation/Systems/PerformerEmitSystem.cs`

2. 把当前零散条件收束为 phase-driven projection
- 当前已有：
  - local player 相关条件
  - owner cull visible
  - LOD
- 改造方向：
  - performer 不再自己决定 visibility truth
  - performer 只根据 phase result 决定 active / suspended / emitted variant
  - emitted variant 的具体参数继续通过现有 param binding / override 流计算

3. 保持 legacy lane 不扩张
- 不向 `EntityVisualEmitSystem` 添加新相位逻辑

完成标准：

- performer 可以读取统一 phase result
- 不再新增散落的 visibility if/switch

建议负责人：

- 1 名 Presentation runtime 开发

依赖：

- 依赖 Phase B

## 6. Phase D: 行为分层迁移

目标：

- 让不同行为种类开始共享 phase projection 机制

推荐顺序：

1. `WorldHudPerformBehavior`
2. `IndicatorPerformBehavior`
3. `SplinePerformBehavior`
4. `VfxPerformBehavior`
5. `ModelPerformBehavior`
6. `AnimatorPerformBehavior`

任务包：

1. 先迁 HUD / indicator / spline
- 原因：
  - 风险最低
  - 用户可见差异最大
  - 最容易验证 phase result 是否合理

2. 再迁主模型
- 把主模型显示差异收编进 `ModelPerformBehavior`

3. 最后迁动画
- animator ownership 最复杂，必须延后

额外约束：

- 行为之间的参数联动必须优先通过现有 performer 参数黑板表达
- 如果现有 param key 不够，只新增 key、value source 或 graph 绑定，不新增第二套联动状态容器

完成标准：

- 至少 2 类行为能消费统一 phase result
- 不需要为不同 viewer 复制 performer

建议负责人：

- HUD / indicator 1 人
- model / animator 1 人

依赖：

- 依赖 Phase C

## 7. Phase E: 测试与验收

目标：

- 把 phase system 从“文档正确”变成“测试可保真且用户可见可验收”

任务包：

1. 架构测试
- 建议新增：
  - phase contract 不依赖 selection
  - `PresentationRequest` 不持有 phase/orchestration state
  - `PrefabPart` 不持有 phase/runtime state
  - performer 参数联动不依赖第二套黑板或 linkage context

2. PresentationTests
- 建议新增：
  - 同一 performer 对不同 audience 输出不同 projection
  - same semantic behavior under different phase result
  - hidden / ghosted / debug 分支正确
  - attachment / grounding / fade / attr mapping 所需数值均可由现有 param binding / override 正确驱动

3. 最小 showcase 验收
- 建议挑一个小场景：
  - 同一单位在两种 audience 下看到不同 HUD / indicator / model projection

4. 用户第一视角证据
- 必须输出至少一种用户第一视角证据：
  - 第一视角截图
  - 录像
  - launcher evidence
  - adapter-visible output 记录
- 必须能够直观看出不同 audience 下的 phase difference

完成标准：

- 至少一组 automated test 覆盖 phase projection
- 至少一个 showcase 验收覆盖多 audience 差异
- 至少一组用户第一视角可见证据覆盖多 audience 差异

建议负责人：

- 1 名测试 / 验收开发

依赖：

- 至少依赖 Phase C

## 8. 任务拆分建议

为了方便分派，建议拆成以下工单：

### Ticket 1: Phase Contract

产出：

- `PerformPhaseInput`
- `PerformPhaseResult`
- 文档注释
- 架构测试

完成定义：

- 可编译
- 无 selection 依赖
- 无 request 依赖

### Ticket 2: Phase Resolver

产出：

- `PerformPhaseResolver`
- 最小 audience context
- 最小 phase matrix

完成定义：

- 能从上游 identity / relation / visibility 输入产出稳定 phase result

### Ticket 3: Performer Phase Consumption

产出：

- `PerformerEmitSystem` 接入 phase result
- 停止新增零散 visibility 分支

完成定义：

- performer 根据 phase result 投影输出

### Ticket 4: HUD / Indicator Projection

产出：

- HUD 和 indicator 行为消费 phase result

完成定义：

- 同一 performer 在不同 audience 下显示不同 HUD / indicator

### Ticket 5: Model Projection

产出：

- `ModelPerformBehavior` phase-driven projection

完成定义：

- 模型显示不再依赖复制 performer 实例

### Ticket 6: Animator Projection

产出：

- `AnimatorPerformBehavior` phase-aware runtime

完成定义：

- animator 跟随 performer projection，而不是 entity visual 旁路

### Ticket 7: UAT And Player-Visible Evidence

产出：

- headless acceptance artifacts
- user-first visible evidence
- 简明 UAT 判定说明

完成定义：

- 有 battle report / trace / path artifact
- 有用户第一视角可见证据
- 能说明“哪个 viewer 看到了什么差异”

## 9. 推荐排期

如果按保守推进，建议排期如下：

1. 第 1 周
- Ticket 1
- Ticket 2

2. 第 2 周
- Ticket 3
- Ticket 4

3. 第 3 周
- Ticket 5

4. 第 4 周
- Ticket 6
- Ticket 7 UAT 与用户可见证据收口

## 10. 风险提醒

### 风险 1

开发者误把 selection 重新引入 phase system。

规避：

- phase contract 测试直接禁止 selection 依赖

### 风险 2

开发者把 `LocalPlayerEntity` 当成完整 audience 模型。

规避：

- 文档和接口都要求中性 audience context

### 风险 3

开发者用“不同玩家一个 performer”快速实现。

规避：

- 测试明确校验“一个语义对象，一个 performer，多份 projection”

### 风险 4

动画迁移过早，导致大回归。

规避：

- animator 永远留在最后一阶段

### 风险 5

团队把 headless 结果误当成最终验收。

规避：

- 文档、工单、完成定义全部要求用户第一视角可见证据
- 没有 player-visible evidence 不能宣称完成

## 11. 开发交接建议

交给后续开发者时，建议附上以下阅读顺序：

1. `docs/rfcs/perform-architecture-unification/00_overview.md`
2. `docs/rfcs/perform-architecture-unification/03_phase_system_design.md`
3. `docs/rfcs/perform-architecture-unification/04_development_plan.md`
4. `docs/rfcs/perform-architecture-unification/03_phase_system_design.md` 中的 UAT 验收标准
5. `src/Core/Gameplay/Components/IdentityComponents.cs`
6. `src/Core/Gameplay/Teams/TeamManager.cs`
7. `src/Core/Gameplay/Relationships/RelationshipRuntime.cs`
8. `src/Core/Presentation/Systems/PerformerEmitSystem.cs`

## 12. 相关文档

- `docs/rfcs/perform-architecture-unification/00_overview.md`
- `docs/rfcs/perform-architecture-unification/01_execution_plan.md`
- `docs/rfcs/perform-architecture-unification/03_phase_system_design.md`
# 第一性优先级附录

## 1. 主问题

当前最高优先级问题不是某个单独名词或子系统，而是：

- 演出语义真相仍然分裂在 `entity visual / animator` 与 `performer` 两条主线上

只要这个主问题不解决：

- 相位系统再漂亮也只是挂在旁路上
- 可见性输入再完整也无法形成统一演出投影
- UAT 也只能验证零碎子能力，而不是统一用户体验

## 2. 次问题

以下问题很重要，但不应被误当成主问题：

- visibility 输入结构长什么样
- audience context 用哪些字段表达
- `player` / `team` / `relationship` 如何命名
- behavior taxonomy 具体拆几类

这些都必须服务于主问题，而不是反过来抢占主设计重心。

## 3. 必须避免的错误优先级

1. 围绕某个被提到的名词过度设计
- 例如把 `selection`、`visibility`、`observer` 单独抬成架构中心

2. 过早优化输入字段形状
- 在没有统一 orchestration truth 之前，细化 `AudienceContext` 字段收益很低

3. 先做底层漂亮抽象，后补用户可见结果
- 这会让工程看起来很完整，但 UAT 没法说服人

## 4. 正确的排序

从第一性出发，优先级应当始终是：

1. 统一演出编排真相
2. 让相位结果真正驱动用户可见差异
3. 再细化输入契约与抽象命名

## 5. Ticket 启动前检查

每个 Ticket 开始前先回答两个问题：

1. 这是否直接减少“双主线演出真相分裂”？
2. 这是否直接提升用户第一视角可见差异？

如果两者都不是，这个任务不应优先。

## 6. 参数黑板附录

后续开发时，performer 参数真相统一为现有机制：

- `PerformerDefinition.Bindings`
- `BindingIndex`
- `PerformerInstanceBuffer.SetParamOverride`
- `PerformerInstanceBuffer.TryGetParamOverride`
- `PresentationCommandKind.SetPerformerParam`

因此任何新需求都应优先落到以下扩展点：

1. 新 `paramKey`
2. 新 `ValueSourceKind`
3. 新 graph 计算
4. 新 rule 发出的 `set-param`

不应新增：

- 第二套 performer 参数黑板
- phase-specific linkage bag
- child performer implicit inheritance state
