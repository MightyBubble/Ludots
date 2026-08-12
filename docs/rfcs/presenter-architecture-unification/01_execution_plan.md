# Presenter 统一编排落地方案

Status: Proposed
Last Updated: 2026-04-16

## 1. 目标

本计划用于将当前“双主线表现架构”逐步收束为单一 `Presenter` 编排架构，避免大爆炸式重构。

落地约束：

- 不做一次性大 rename
- 不在迁移初期触碰 adapter 层
- 不把 `PrefabPart` 运行时化
- 不让 `PresentationRequest` 承担 orchestration truth
- presenter 间的参数透传、联动和投影一律优先复用现有 `bindings + override + set-param` 参数黑板
- 只允许扩展现有 presenter 参数黑板，不允许再起第二套参数真相
- `PerformCommand` 是演出领域命令；`PresentationCommand` 只允许作为过渡 transport 壳存在
- 先收束边界，再迁移主模型与动画

## 2. 总体阶段

### Phase 0: RFC 与术语冻结

目标：

- 冻结统一术语与架构边界
- 明确哪些类型是保留、哪些类型是迁移目标、哪些路径属于 legacy lane

产出：

- `docs/rfcs/presenter-architecture-unification/00_overview.md`
- `docs/rfcs/presenter-architecture-unification/01_execution_plan.md`
- `docs/rfcs/presenter-architecture-unification/02_cross_review.md`
- `docs/rfcs/presenter-architecture-unification/03_phase_system_design.md`

完成标准：

- 团队接受 `Presenter = orchestration SSOT`
- 接受 `PerformRule / PerformCommand / PerformBehavior` 命名方向

### Phase 1: 引入命名安全的并行契约

目标：

- 在不打断现有运行时的前提下，引入新的 `Perform*` 契约

建议新增目录：

- `src/Core/Presentation/Perform/`

建议新增类型：

- `PerformRule`
- `PerformCommand`
- `PerformBehaviorDefinition`
- `PerformBehaviorInstance`
- `PresentAudienceContext`
- `PresentPhaseInput`
- `PresentPhaseResult`
- `PresentPhaseResolver`

策略：

- 先做 wrapper / adapter，不立即删除 `Presenter*`、`PresentationBehavior*`
- 允许新旧类型短期共存，但必须标明最终收敛目标
- 所有新增 behavior 输入先映射到现有 presenter 参数黑板；如需新参数，只扩展 param key / value source / graph 计算
- 如 `PresentationCommand` 继续保留，它只能承载 `PerformCommand` 语义，不得发展成第二套领域命令模型

完成标准：

- 新契约可编译、可被测试引用
- 不发生运行时行为变化

### Phase 2: 统一 orchestration entry

目标：

- 所有新的表现触发都必须先经过 perform orchestration

建议新增系统：

- `src/Core/Presentation/Systems/PerformOrchestrationSystem.cs`

职责：

- 消费 gameplay event、presentation event、entity-fed visibility input
- 输出 `PerformCommand`
- 激活或关闭 `PerformBehavior`

约束：

- 从这一阶段开始，不允许再往 `EntityVisualEmitSystem` 添加新的业务编排逻辑
- showcase / fixture / mod 中不再新增 direct visual orchestration 特判
- presenter 之间的联动、attachment 输入、fade 权重、attr 映射输入，都优先走现有参数黑板，不再引入额外 linkage 容器

完成标准：

- 新增表现需求只走 perform orchestration path
- direct visual 路径只承担 legacy 兼容责任
- `PerformCommand` 与 `PresentationCommand` 的职责边界文档化且代码实现不再混淆

### Phase 3: 拆分 behavior taxonomy

目标：

- 让输出种类 switch 迁移为行为体系

建议行为分类：

- `ModelPerformBehavior`
- `AnimatorPerformBehavior`
- `WorldHudPresentBehavior`
- `IndicatorPerformBehavior`
- `SoundPerformBehavior`
- `VfxPerformBehavior`
- `SplinePerformBehavior`

优先顺序：

1. `WorldHudPresentBehavior`
2. `IndicatorPerformBehavior`
3. `SplinePerformBehavior`
4. `VfxPerformBehavior`
5. `ModelPerformBehavior`
6. `AnimatorPerformBehavior`

原因：

- HUD、marker、spline 当前已较多通过 presenter 输出，迁移风险最低
- model 与 animator 耦合最重，应留到后面

完成标准：

- `PresenterEmitSystem` 不再以扩大 `PresenterVisualKind` 为主要扩展方式
- 至少一批行为种类已从 switch 中拆出

### Phase 4: 收编主模型路径

目标：

- 把主模型从 entity visual 直通路径迁入 `ModelPerformBehavior`

涉及代码：

- `src/Core/Presentation/Systems/EntityVisualEmitSystem.cs`
- `src/Core/Presentation/Config/VisualTemplateConfigLoader.cs`
- `src/Core/Presentation/Config/PresentationAuthoringContext.cs`

建议中间态：

- authoring 仍可从现有 visual template 配置出发
- 但 authoring 输出逐步改为“生成 perform behavior authoring record”
- 必要时保留一小段 compatibility shim，将新定义翻译到旧组件

完成标准：

- 主模型行为能在 presenter 生命周期下工作
- `EntityVisualEmitSystem` 不再作为 architecture truth

### Phase 5: 收编动画路径

目标：

- 把 animator 从 entity-owned runtime 重构为 `AnimatorPerformBehavior`

涉及代码：

- `src/Core/Presentation/Systems/AnimatorRuntimeSystem.cs`
- `src/Core/Presentation/Components/AnimatorPackedState.cs`
- `src/Core/Presentation/Components/AnimatorRuntimeState.cs`
- `src/Core/Presentation/Components/VisualRuntimeState.cs`

策略：

- 复用现有 controller、packed state、state resolution 逻辑
- 改变 ownership 语义，而不是一开始就推翻 animator 内核
- 让 animator 作为 behavior runtime 附着于 presenter，而不是 entity visual

完成标准：

- 动画切换、默认 state、profile/controller 解析都由 behavior runtime 承接
- model behavior 能消费 animator behavior 的输出状态

### Phase 6: 接入 entity 发起的 visibility / phase input

目标：

- 将大战略、多人联机最关键的“谁看得到什么”提升为一等模型

建议契约：

- entity / phase 层 visibility snapshot
- `PresentAudienceContext`
- `PresentPhaseInput`
- `PresentPhaseResult`
- `PresentPhaseResolver`
- 现有 `CullState` / `LOD` 的标准化消费接口

第一批支持能力：

- local player / remote player
- owner cull visible
- LOD

第二批支持能力：

- ally / enemy / observer
- fog / vision
- replay / debug / spectator

完成标准：

- 同一语义事件可以为不同 viewer 产生不同演出结果
- cull/phase 变化不会破坏 behavior 语义状态

### Phase 7: 删除 legacy lane

目标：

- 彻底移除或硬性停用平行编排路径

优先删除对象：

- `EntityVisualEmitSystem` 作为主视觉编排入口的角色
- entity-owned animator orchestration 的主路径语义
- 新代码继续引入 `PresenterVisualKind` 扩展的模式

完成标准：

- presenter 成为唯一编排入口
- 新增代码被测试和架构规则约束，无法绕过 perform orchestration

## 3. 建议代码落点

建议新增结构如下：

```text
src/Core/Presentation/Perform/
src/Core/Presentation/Perform/Behaviors/
src/Core/Presentation/Perform/Policies/
src/Core/Presentation/Perform/Runtime/
```

建议第一批文件：

- `src/Core/Presentation/Perform/PerformRule.cs`
- `src/Core/Presentation/Perform/PerformCommand.cs`
- `src/Core/Presentation/Perform/PerformBehaviorDefinition.cs`
- `src/Core/Presentation/Perform/PerformBehaviorInstance.cs`
- `src/Core/Presentation/Perform/PresentAudienceContext.cs`
- `src/Core/Presentation/Perform/PresentPhaseInput.cs`
- `src/Core/Presentation/Perform/PresentPhaseResult.cs`
- `src/Core/Presentation/Perform/PresentPhaseResolver.cs`

## 4. 测试策略

### 4.1 架构测试

建议放入：

- `src/Tests/ArchitectureTests`

建议新增规则：

- 新的 presentation system 不得绕过 perform orchestration 直接发 request
- `PrefabPart` 不得持有运行时行为状态或 adapter-specific 字段
- behavior definition 不得直接引用 adapter-facing buffer
- presenter 只能消费上游 visibility input，不能拥有 visibility truth
- presenter 参数联动只能扩展现有参数黑板，不能新增第二套 presenter 参数容器

### 4.2 表现层测试

建议放入：

- `src/Tests/PresentationTests`

建议新增测试：

- `PerformRule` 正确发出 `PerformCommand`
- `PerformBehavior` 正确经历 activate / deactivate / pulse 生命周期
- animator behavior 正确解析 controller/profile/default state
- world HUD、indicator、sound 与 model 行为共享一致生命周期语义
- `PrefabFinalizationPipeline` 在新架构下仍保持 asset-only
- attachment / grounding / fade / attr mapping 所需输入可以通过现有 param binding / override 正确传递

### 4.3 多人相位测试

建议新增 focused suite 或继续放在 `src/Tests/PresentationTests`

关键用例：

- 同一事件对不同 viewer 产生不同输出
- cull 导致 suspend，而不是破坏语义状态
- phase 切换后行为恢复结果可预测且确定

### 4.4 Showcase 验收

建议优先覆盖：

- `mods/showcases/info_panels/GenreInfoShowcaseMod`
- `mods/fixtures/animation/AnimationAcceptanceMod`
- `mods/PerformanceVisualizationMod`

目标：

- 至少有一条完整 acceptance 场景证明“一个单位的主模型、动画、HUD、指示器、文本、特效都归 presenter 编排”

## 5. 风险与缓解

### 风险 1: 大规模 rename 先于结构收敛

影响：

- 代码 churn 高，但没有带来真实架构收益

缓解：

- 先引入并行契约，再做收束 rename

### 风险 2: animator 过早迁移

影响：

- 现有 visual runtime 与动画合同易出现大面积回归

缓解：

- 把 animator 放到后半阶段，并优先复用内部 controller runtime

### 风险 3: presenter 变成巨型 switch 中心

影响：

- 从双轨混乱变成单点泥球

缓解：

- 强制行为分类和 runtime ownership 分层

### 风险 4: showcase/fixture 再次内容漂移

影响：

- acceptance 继续频繁被无关资源重命名打断

缓解：

- 为展示和验收场景补充 fixture contract，限制 raw gameplay id 直接耦合

## 6. 执行顺序建议

建议严格按以下顺序推进：

1. 接受 RFC 与术语
2. 建立 `Perform*` 并行契约
3. 中心化 orchestration entry
4. 拆出 HUD / indicator / spline / VFX 行为
5. 收编主模型行为
6. 收编动画行为
7. 接入 entity-fed visibility / phase input
8. 删除 legacy lane

## 7. 第一批可执行任务

如果下一步直接开始落代码，建议第一批任务只做以下四件事：

1. 新建 `src/Core/Presentation/Perform/` 基础契约与空实现
2. 把当前 presenter 路径中 HUD / marker / spline 输出拆出独立 behavior runtime
3. 增加架构测试，阻止新逻辑继续进入 `EntityVisualEmitSystem`
4. 起一组 entity-fed visibility contract 测试，先覆盖 local player / cull / LOD

这样可以先把架构护栏立起来，再逐步吃掉最硬的 model/animator 迁移。
