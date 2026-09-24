# Performer-as-Actor 架构

本文是 Ludots Performer 领域的规范性架构正本。PRD、Showcase、配置生成器和运行时代码必须服从本文；它们可以补充场景和交付细节，但不得重新定义 Rule、Command、Behavior、Tween、Scope 或 AssetKind 的层级关系。

## 1. 概述

Performer 是表现编排的身份和生命周期单位。它通过 child performer 组合复杂画面，通过参数总线传递普通表现数据，通过 Rule 响应事件并产生离散 Command，通过激活的 Behavior 持续计算表现状态，最终把具体资产请求交给平台适配器。

核心关系只有一套：

```text
PerformerDefinition
|- Children[]
|- ParamDefaults[] / Bindings[]
|- Rules[]
|  `- EventFilter + Condition -> PerformerCommand template
`- Behaviors[]
   |- AssetBinding
   |- AttributeBinding / TagBinding
   |- Animator / Attachment / Sound / Material / Spline / Grounding
   |- MinimapMarker
   `- ParamTween
```

`ParamTween` 是 `BehaviorKind` 的一种，不是与 Behavior 平级的运行时。`PerformerCommand` 是瞬时状态变更协议，不是 Behavior，也不是 PerformerDefinition 中与 Rule 平行的第三套配置集合。

## 2. 结构

### 2.1 定义层

`PerformerDefinition` 的一等组成是：

| 组成 | 职责 | 是否拥有时间推进 |
|---|---|---|
| `Children` | 声明稳定的父子组合和 scope 参数覆盖 | 否 |
| `ParamDefaults` | 声明实例参数初值 | 否 |
| `Bindings` | 把正式来源映射到参数总线 | 由绑定所属系统更新 |
| `Rules` | 声明事件和条件匹配后要发出的 Command 模板 | 否 |
| `Behaviors` | 声明激活期间持续生效的能力槽 | 按具体 BehaviorKind 决定 |

Rule 才回答“何时响应、满足什么条件”。Behavior 回答“激活期间持续做什么”。两者通过 Command 对实例状态的离散修改衔接。

### 2.2 Command 通道

`PerformerCommand` 是 Core 提供的离散写入端口，允许以下生产者投递：

- `PerformerRuleSystem`：把匹配的 Event + Rule 编译为运行时 Command。
- 受控生命周期系统：在不虚构 PresentationEvent 的情况下请求创建或销毁 Performer。
- 测试和工具：通过同一正式端口驱动可验证状态变化。

Command 只允许表达一次性状态变化：创建、销毁、按 scope 销毁、设置参数、激活或停用 Behavior、初始化变换。Command 不保存时间进度，不直接绘制，也不绕过 PerformerRuntime 修改适配器状态。

CommandBuffer 是当帧传输结构。容量不足必须明确失败；未知或未实现的 CommandKind 必须明确失败，不得静默跳过。

### 2.3 Behavior 通道

Behavior 是 Performer 实例上的持续能力槽。`ActiveByDefault` 决定实例创建时的初始位，后续只有 `ActivateBehavior` / `DeactivateBehavior` Command 修改 `BehaviorActiveMask`。

Behavior 不再拥有第二套 `ActivationCondition`。需要条件激活时，作者必须声明 Rule：

```text
Event + Condition -> ActivateBehavior / DeactivateBehavior
```

这样条件判断只有 Rule 一个事实源，不存在“条件为真但 Command 已停用”之类的双重优先级。

### 2.4 ParamTween

`ParamTween` 是连续参数变化的 BehaviorKind。它读取自己的 Behavior 配置和 ECS 实例状态，按帧写入 Performer 参数总线：

```text
motion class (authoring sugar)
-> offline compile
-> BehaviorSlot { Kind = ParamTween }
-> PerformerParamTweenState
-> PerformerBehaviorSystem
-> Float / Vector parameter
```

Tween 不创建或销毁 Performer，不直接操作资产，不拥有 scope。离散 Int 切换使用 `SetParam` Command；连续 Float / Vector 变化使用 ParamTween Behavior。

### 2.5 生命周期与组合

- Performer 拥有实例身份、父子关系、scope、参数和 BehaviorActiveMask。
- `DefaultLifetime` 是 Performer 生命周期策略，可以保留在定义顶层；它不是动效曲线。
- child performer 是复杂表现的组合单位。激光、闪电、爆炸等语义由多个具体 child 组合，不新增语义型 AssetKind。
- scope 负责身份、父子参数继承和成组销毁，不负责描述画法。

## 3. 详情

### 3.1 运行时顺序

```text
PresentationEventStream
-> PerformerRuleSystem
-> PerformerCommandBuffer
-> PerformerRuntimeSystem
-> Performer instance state / params / BehaviorActiveMask
-> PerformerBehaviorSystem
-> PerformerEmitSystem / dedicated request systems
-> adapter-neutral request
-> Raylib / Effekseer / other adapter
```

执行顺序表达因果关系，不表示 Command 与 Behavior 属于同一类配置。Command 修改状态，Behavior 消费状态。

### 3.2 参数总线

参数分为 Float、Int、Vector lane。作者配置写语义名，加载期通过正式 Registry 编译为编号；裸编号不得进入作者合同。

- Rule / Command 可以离散写参数。
- Binding 可以把 gameplay 或关系事实投影为参数。
- ParamTween 可以连续写 Float / Vector 参数。
- AssetBinding 和其他 Behavior 消费参数，生成具体请求。
- 父参数通过明确的 child override 和继承规则传播，不借用材质自定义数据传递业务语义。

同一时刻两个激活的 ParamTween 不得写同一 lane 的同一参数。离线和运行时都必须拒绝该冲突。

### 3.3 具体资产

Core 只声明具体表现图元或资产类型。`AssetKind.VFX` 已退役，编号永久保留为空洞，不得复用。

- 原生图元：`Ring`、`Line`。
- Effekseer emitter：`SpriteEmitter`、`RibbonEmitter`、`TrackEmitter`、`RingEmitter`、`ModelEmitter`。
- 其他既有具体类型：`Mesh`、`SkinnedMesh`、`Decal`、`GroundOverlay`、`Spline`、`WorldHud`、`WorldText` 等。

`Laser`、`Explosion`、`Lightning`、`Shield` 是 authoring 语义，不是 AssetKind。Theme 是离线参数包，不是 Behavior，也不在运行时选择 AssetKind。

### 3.4 六边形边界

| 层 | 拥有 | 禁止 |
|---|---|---|
| Authoring Domain | semantic、theme、motion class、展开规则、中间表示 | 依赖 Raylib 窗口、Effekseer 进程或 HTML 报告 |
| Offline Compiler | 纯验证、展开、生成普通 Performer 定义和具体资产描述 | 修改 launcher、录屏或绘制 |
| Core Runtime | Performer、Rule、Command、Behavior、参数、生命周期、adapter-neutral request | 引用 Effekseer 或 Raylib API |
| Adapter | 加载并绘制具体 AssetKind | 解释 gameplay 语义、Theme 或 Rule |
| Delivery Tooling | 项目生成、launcher 注册、录屏、报告 HTML | 成为 authoring 语义或运行时合同的事实源 |

## 4. 场景

### 4.1 事件触发的持续激光

1. Gameplay 投递开始引导事件。
2. Rule 匹配事件和条件，产生 CreatePerformer Command。
3. Runtime 创建激光 root 和具体 emitter children。
4. children 的 AssetBinding 和 ParamTween Behavior 持续消费 source、target、width、alpha 参数。
5. Gameplay 投递结束事件。
6. Rule 产生 DestroyScopedPerformer 或 DestroyPerformerScope Command。
7. Runtime 清理整场表演，Adapter 只执行已生成请求的停止和资源释放。

### 4.2 条件开关 Behavior

需要“工作状态开启时播放声音”时，Rule 监听 tag 变化并在条件满足时发出 ActivateBehavior；条件失效时发出 DeactivateBehavior。Sound Behavior 本身不重复保存 tag 条件。

### 4.3 CSS 类动效

作者引用 `breathe` motion class。离线编译器将它展开为一个 ParamTween Behavior。运行时只看到普通 BehaviorSlot 和参数，不知道 CSS 类名，也不存在单独 Tween 领域。

## 5. 边界

### 5.1 必须成立

- Rule 是声明式事件条件的唯一事实源。
- Command 是离散状态变更端口。
- Behavior 是激活期间的持续能力。
- ParamTween 是 BehaviorKind，不与 Behavior 平级。
- Adapter 只消费具体请求，不解释 semantic、theme、Rule 或 Command。
- 未知字段、未知枚举、未实现 Command、容量溢出和参数冲突全部 fail-closed。

### 5.2 禁止

- 禁止用 `Behavior、Command、Tween` 表示三个平级概念。
- 禁止恢复 `ActivationCondition`，形成第二套条件激活逻辑。
- 禁止保留没有消费者的 CommandKind。
- 禁止新增笼统 `VFX`、`Effect` 或语义型 AssetKind。
- 禁止让 Theme 成为 Behavior 或运行时后端选择器。
- 禁止 Adapter 创建 Performer、解释 scope 或读取 gameplay 状态。

### 5.3 当前实现差距

下表描述当前仓库实然状态。只有“已对齐”项目可以作为生产能力对外承诺。

| 编号 | 实然 | 应然 | 状态与退出条件 |
|---|---|---|---|
| PG-001 | 文档曾把 Behavior、Command、Tween 并列 | 使用 Rule -> Command -> State -> Behavior；ParamTween 属于 Behavior | 本次治理已对齐；架构测试禁止旧表述回归 |
| PG-002 | `ActivationCondition` 可配置但运行时不消费 | 条件只属于 Rule | 本次治理删除字段、加载和 fixture；配置出现该字段必须失败 |
| PG-003 | `SinkParamToAsset` 可声明但没有执行分支 | 只保留有消费者的 CommandKind | 本次治理删除枚举并让 RuntimeSystem 对未知值抛错 |
| PG-004 | `positionYDriftPerSecond`、`alphaFadeOverLifetime` 是定义顶层的旧连续动画旁路 | 连续变化统一进入明确的 transform Behavior 或 ParamTween | 未对齐；完成配置迁移、性能测试并删除字段后才能关闭 |
| PG-005 | Raylib micro authoring 的验证、展开、入口生成、launcher 修改和报告生成集中在 showcase 专用脚本 | 纯 Authoring Domain/Compiler 与交付适配器分离 | 未对齐；提取可被两个以上 Mod 使用的纯编译库后才能称为 Core authoring 基建 |
| PG-006 | Core 曾保留无生产消费者的 `PresentationBehaviorRegistry`、Resolver 与独立配置入口 | 只有 Performer `BehaviorSlot` 表达持续表现能力 | 本次治理已对齐；删除死管线、配置目录项和服务键，架构测试禁止回流 |

PG-004、PG-005 未关闭前，准确表述是“Performer Runtime 主链已落地，Raylib micro authoring 是完整垂直样板”，不得表述为“整套生产级 Authoring 基建已经完成”。

## 6. UAT

```gherkin
功能: 作者能够分辨 Rule、Command、Behavior 与 ParamTween 的职责

  场景: motion class 被编译为 Behavior
    假如作者为一个 child performer 选择 breathe 动效类
    当离线编译完成
    那么生成结果包含 Kind 为 ParamTween 的 BehaviorSlot
    而且不存在独立 Tween 列表或 Tween 运行时

  场景: 条件通过 Rule 开关持续行为
    假如一个声音只应在角色工作时播放
    当工作状态事件满足 Rule 条件
    那么 Rule 产生 ActivateBehavior Command
    而且 Sound Behavior 不携带第二份 activationCondition

  场景: 未实现命令不能被静默忽略
    假如运行时收到未定义的 PerformerCommandKind
    当 PerformerRuntimeSystem 消费该命令
    那么本帧明确失败并指出未知命令

  场景: 当前未完成能力不被包装成已交付
    假如读者查看 Performer 架构正本或 Authoring PRD
    当他查看实然状态
    那么能够看到旧动画旁路和 showcase 专用编译器仍未完成治理
```

## 7. 相关文档

- [Performer 参数黑板与 Animator 统一](performer-param-blackboard.md)
- [Performer Transform、Grounding 与 Attachment](performer-transform-and-attachment.md)
- [Performer 编译式执行分层](performer-compiled-lanes.md)
- [Performer 现有基建收尾整合](performer-legacy-consolidation.md)
- [Performer 开发看板](performer-development-kanban.md)
