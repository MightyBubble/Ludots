# Presenter 参数黑板与 Animator 统一

Presenter 参数黑板是 presenter 实体上的参数存取层：每个 presenter 实例持有六个 ECS 组件（三 lane override + 三 lane 定义默认值）， Animator 没有独立参数存储——速度、转移条件、状态反馈全部读写同一块黑板。本文回答"参数存在哪、怎么写进去、怎么读出来、怎么继承"；"参数变了画面怎么变"（sink 键、标脏、按 AssetKind 解析成资产属性）是另一条链路，SSOT 见 [参数 sink 机制](../reference/presenter-capability-catalog/param-sink.md)。

## 1 是什么：per-entity 三 lane 组件

每个 presenter 实体创建时固定挂六个组件（`src/Core/Presentation/Presenters/PresenterEntityRuntime.cs:312-329`）：

| 组件 | lane | 角色 | 容量 |
|---|---|---|---|
| `PresenterFloatParams` | Float | 运行时 override（指令/绑定写入） | 16（`src/Core/Presentation/Presenters/PresenterFloatParams.cs:12-17`） |
| `PresenterIntParams` | Int | 运行时 override（含 bool，用 0/1） | 16（`src/Core/Presentation/Presenters/PresenterIntParams.cs:5-10`） |
| `PresenterVectorParams` | Vector | 运行时 override（Vector4，平铺 XYZW 四个 fixed buffer） | 8（`src/Core/Presentation/Presenters/PresenterVectorParams.cs:6-15`） |
| `PresenterFloatDefaults` | Float | 定义默认值（paramDefaults 装载） | 16（`src/Core/Presentation/Presenters/PresenterFloatParams.cs:79-84`） |
| `PresenterIntDefaults` | Int | 定义默认值 | 16（`src/Core/Presentation/Presenters/PresenterIntParams.cs:72-77`） |
| `PresenterVectorDefaults` | Vector | 定义默认值 | 8（`src/Core/Presentation/Presenters/PresenterVectorParams.cs:83-91`） |

- lane 由 `ParamLane : byte` 枚举定义：`Float = 0, Int = 1, Vector = 2`（`src/Core/Presentation/Presenters/PresenterFloatParams.cs:5-10`）。bool 不单独开 lane，用 Int lane 的 0/1 表示。
- 组件是 `fixed` buffer 结构体，`Set` 在 key 已存在时覆写、容量满时丢弃新项（`src/Core/Presentation/Presenters/PresenterFloatParams.cs:37-53`）；`Clear` 用末位交换删除（:56-74）。
- param key 是全局整数 id：语义字符串经 `PresenterParamKeyRegistry.Register` 编译（`src/Core/Presentation/Presenters/PresenterParamKeyRegistry.cs:18-46`），未声明用 `UnsetParamKey = -1`（:14），自定义键从 200_000 起（:15）。well-known 保留键清单见 [参数 sink 机制](../reference/presenter-capability-catalog/param-sink.md)。
- override 与 defaults 分开存的意义：`ClearParam` 只清 override，清掉后回落到定义默认值而不是"无值"。

## 2 写入路径

三个写入入口最终都汇到 `PresenterEntityRuntime.SetParamInternal`：

```text
SetParam 指令（rule/command）      PresenterRuntimeSystem :129-154
bindings（每帧数据源绑定）          PresenterBehaviorSystem.ApplyBindings :1107-1138
paramDefaults（定义默认值，创建时） PresenterEntityRuntime.SetParamDefault :721-748
                    │
                    ▼
SetParamAndPropagateToAffectedChildren / SetParam（PresenterEntityRuntime :660-676）
                    │
                    ▼
SetParamInternal（:678-719）：写 lane 组件 → 值变更则 state.Version++
                    → MarkStaticDirtyIfVisualParamChanged（视觉参数才标脏）
                    → PropagateParamToAffectedChildren（受影响子实例复制）
```

### 2.1 SetParam 指令

JSON 规则里的 SetParam 命令（真实样例 `mods/fixtures/blacksmith/BlacksmithTestMod/assets/Presentation/presenters.json:92-97`）：

```jsonc
{ "kind": "SetParam", "paramKey": "blacksmith.fixture.dayNight",
  "paramLane": "Float", "valueSource": "Fixed", "paramValue": 1 }
```

命令 DTO 字段 `ParamKey / ParamLane / ParamValue / IntValue / VectorValue`（`src/Core/Presentation/Presenters/PresenterCommand.cs:34-38`）；装载解析在 `src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs:1337` 与 :1497-1516。运行期 `PresenterRuntimeSystem` 的 SetParam 分支先按 definitionId+scopeTag 定位 scoped 实例，再调 `SetParamAndPropagateToAffectedChildren`（`src/Core/Presentation/Systems/PresenterRuntimeSystem.cs:129-154`）。指令层完整语义见 [commands](../reference/presenter-capability-catalog/commands.md)。

### 2.2 bindings（每帧数据源）

定义的 `bindings` 声明 paramKey → `ValueRef` 数据源（`src/Core/Presentation/Presenters/PresenterParamBinding.cs:11-22`），`PresenterBehaviorSystem.ApplyBindings` 逐帧解析（owner 颜色、朝向、常量等）后经系统级 `SetParam` 写入（`src/Core/Presentation/Systems/PresenterBehaviorSystem.cs:1107-1138`，包装在 :2740-2742，同样走传播入口）；graph 程序绑定点在 `ApplyGraphParamBindings`（:1144 起）。compiled lane 对此的执行优化不影响黑板语义（`gitbook/architecture/presenter-compiled-lanes.md`）。

### 2.3 paramDefaults（定义默认值）

`paramDefaults` 数组装载为 `PresenterDefinition.ParamDefaults`（`src/Core/Presentation/Presenters/PresenterDefinition.cs:116`，元素结构 `ParamDefault` :32-39）。真实样例（`mods/fixtures/presenter_schema_reference/PresenterSchemaReferenceMod/assets/Presentation/presenters.json:169-186`）：

```jsonc
{ "paramKey": "presenter_schema_reference.asset.visibility", "lane": "Int", "intValue": 1 },
{ "paramKey": "presenter_schema_reference.asset.color", "lane": "Vector", "vectorValue": [1, 1, 1, 1] }
```

解析强校验：lane 必填、值必须用对应 lane 字段（`floatValue`/`intValue`/`vectorValue`），已删除的通用 `value` 字段直接报错（`src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs:2869-2933`，拒绝点 :2886-2890）。定义继承时父子 paramDefaults 按 `(paramKey, lane)` 合并、子覆盖父（:230-232，合并实现 :464-477）。实例创建时 `SetParamDefault` 把数组写进 Defaults 组件——root 单建、批量建、plan 子节点、批量子节点四条创建路径都覆盖（`src/Core/Presentation/Presenters/PresenterEntityRuntime.cs:531`、:469-473、:2314、:2520-2524）。

### 2.4 SetParamInternal：变更检测与标脏

`SetParamInternal`（`src/Core/Presentation/Presenters/PresenterEntityRuntime.cs:678-719`）按 lane 写组件，值未变直接返回（写入是廉价的）；值变了：`state.Version++`（:710-711）→ `MarkStaticDirtyIfVisualParamChanged`（:712，实现在 :4388-4430，只有 `AffectsStaticVisualParam` / `AffectsMaterialSourceParam` 命中的 key 才给 `PresenterEmitCache` 标脏）→ 可选传播（:713-716）。`ClearParam` / `ClearParamAndPropagateToAffectedChildren` 是对称的删除路径（:780-788）。

### 2.5 其他写入方

持有 chunk span 的系统可以直写组件：如 `MassNavigationLocomotionAnimatorParamSystem` 把寻路速度写进 `PresenterFloatParams` 并手推 `Version++`（`src/Core/MassNavigation/Systems/MassNavigationLocomotionAnimatorParamSystem.cs:45-52`）。直写绕过标脏与传播，只适合不影响静态视觉的参数。

## 3 父→子继承链

黑板提供两种互补的父子语义，**读时回落**与**写时传播**：

### 3.1 读时回落（resolver 沿 parent 链上溯）

`PresenterParamResolver.ResolveFloat/Int/Vector`（`src/Core/Presentation/Presenters/PresenterParamResolver.cs:8-33`、:35-60、:62-87）从当前实体出发，逐级：本实体 override → 本实体 defaults → 沿 `PresenterParent.Parent`（`src/Core/Presentation/Presenters/PresenterParent.cs:5-9`）到父实体重复，直到命中或链尽返回调用方默认值。优先级全序：

1. 自己的 override
2. 自己的定义默认值
3. 父的 override
4. 父的定义默认值
5. ……逐级向上，链尽 → 调用方传入的 defaultValue

链深不限、三 lane 独立。`PresenterEntityRuntime` 上的 `ResolveFloat/TryResolveFloat` 等包装转发到同一实现（`src/Core/Presentation/Presenters/PresenterEntityRuntime.cs:750-778`）。测试锚点：三 lane 存取、parent 链上溯取最近 override、override 先于 default 先于 parent、父 override 压过子 default（`src/Tests/PresentationTests/Presenter/PresenterParamBlackboardTests.cs:20`、:46、:75、:99）。

### 3.2 写时传播（受影响子实例的值复制）

`SetParamAndPropagateToAffectedChildren`（`src/Core/Presentation/Presenters/PresenterEntityRuntime.cs:666-676`）先写自己，再递归遍历 `PresenterChildren` 子树：只有定义声明该 key 影响静态视觉或材质（`AffectsStaticVisualParam` / `AffectsMaterialSourceParam`，`src/Core/Presentation/Presenters/PresenterDefinition.cs:932-953`）的子实例才把值复制进自己的 override 组件，但**无论是否复制都继续向更深处递归**（`src/Core/Presentation/Presenters/PresenterEntityRuntime.cs:4432-4447`、:4449-4468）。

两种机制分工：回落解决"子 presenter 读到区域参数"，传播解决"不声明 sink 的子行为（如 Animator 条件）在 root 写参数后立刻看到新值"——Animator 读参数走 `TryResolveFloatFast`（`src/Core/Presentation/Systems/AnimatorRuntimeSystem.cs:677-722`），它先查 chunk 本地 span，再上溯 parent 链，传播保证本地 span 命中、避免跨实体查找。

## 4 Animator 统一

Animator 没有独立参数存储：状态机的全部输入（播放速度、转移条件）从黑板读，全部输出（当前状态、转移反馈）写回黑板；`AnimatorPackedState` 只是送往 adapter 的 128 位渲染载荷，不是参数容器。

### 4.1 输入：speedParamKey 与转移条件

- **speedParamKey（Float）**：`AnimatorConfig` 的 `SpeedParamKey`（`src/Core/Presentation/Presenters/BehaviorSlot.cs:319-333`，JSON 解析 `src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs:3113-3114`）。`ResolvePlaybackSpeed` 未声明（<0）时用状态自身 `PlaybackSpeed`；声明了则从黑板解析乘子，负值直接抛错——无 fallback（`src/Core/Presentation/Systems/AnimatorRuntimeSystem.cs:604-631`）。解析缺失同样 fail-loud（:633-659）。
- **转移条件（Int/Float）**：Trigger / BoolTrue / BoolFalse 条件读 Int 参数，FloatGreaterOrEqual / FloatLessOrEqual 读 Float 参数，全部经黑板 parent 链解析（`src/Core/Presentation/Systems/AnimatorRuntimeSystem.cs:526-538`）；Trigger 条件带 `ConsumeTrigger` 时消费后写回 0（:541-542）。

### 4.2 输出：stateParamKey 键族

`StateParamKey` 声明后，Animator 每帧把当前状态索引写回黑板 Int lane（`src/Core/Presentation/Systems/AnimatorRuntimeSystem.cs:317-318`）；六类反馈事件（Initialized / TransitionStarted / TransitionCompleted / StateCompleted / ControllerMissing 等）派生出 `StateParamKey+1..+5` 五个键：kind/from/to（Int）、normalizedTime/value0（Float），偏移常量在 :16-20，写入在 `WriteFeedbackToBlackboard`（:724-732）。规则的 `event` 过滤器或后续行为可以直接以这些黑板键为条件，不需要另建事件桥。

Animator 写黑板用的是不传播的 `SetParam`（区别于指令路径的传播入口），因为状态与反馈是每个 animator 实例自己的输出。

### 4.3 与 AnimatorPackedState 的关系

`AnimatorRuntimeSystem` 查询 `WithAll<PresenterState, PerfHasAnimator, PresenterFloatParams, PresenterFloatDefaults, PresenterParent>`（`src/Core/Presentation/Systems/AnimatorRuntimeSystem.cs:27-28`；`PerfHasAnimator` 标记由 `PresenterEntityRuntime` 同步，`src/Core/Presentation/Presenters/PresenterEntityRuntime.cs:1520`），用 chunk 本地 span 零拷贝读写黑板。状态机推演结果压缩进 `AnimatorPackedState`：Word0 存 controller/主次状态/归一化时间/过渡进度/flags，Word1 保留 64 个 bool/trigger 位（`src/Platform/Ludots.Platform.Abstractions/AnimatorPackedState.cs:6-39`），经 `PresenterAnimatorStateBuffer` 槽位送往 skinned adapter（`src/Core/Presentation/Systems/AnimatorRuntimeSystem.cs:168-170`）。黑板是语义参数层，packed state 是 adapter 契约层，两者由 AnimatorRuntimeSystem 单向衔接，互不反向。

## 5 与 param-sink 的分工

- 本文：参数的**存取与继承**——组件布局、写入入口、resolver 回落、写时传播、Animator 读写统一。
- [参数 sink 机制](../reference/presenter-capability-catalog/param-sink.md)：参数的**消费**——sink 键声明、编译期收集（`CollectStaticVisualParams`，`src/Core/Presentation/Presenters/PresenterDefinition.cs:955-968`）、标脏与重发、按 AssetKind 解析成资产属性。

相关：整体架构见 [Presenter-as-Actor 架构设计](presenter-as-actor-architecture.md) §4.4；compiled lane 对 bindings 的执行优化见 [presenter-compiled-lanes.md](presenter-compiled-lanes.md)。
