# 表现层编译式 DSL 架构

> 状态：迁移终态架构。当前可用 authoring/runtime contract 见 [表现 Authoring 与运行时契约](presentation-authoring-runtime-contract.md)；本文定义的是收束目标和验收口径，不表示当前工作树已经删除 transitional `PerformerInstanceBuffer` runtime。

本文定义 Ludots 表现层的目标正式口径：`performer` 只作为 authoring DSL 概念存在，终态运行时不再存在通用 `PerformerInstance` / `PerformerEntityRuntime` / `1 performer = 1 entity` 模型。所有表现 authoring 都必须在 compile 阶段 lower 为 owner-level backend runtime、typed recipe arrays 和 owner-local execution lanes。

## 1 核心结论

- `performer` 只存在于配置、authoring AST 和 compiler pass 中。
- runtime 的唯一主语是 `owner entity`，不是 `performer entity`。
- HUD bar、text、mesh、spline、ground overlay、sound、animator 都是编译产物里的 typed recipe，不是运行时 performer 实例。
- 每个 owner 最多只有一个 `OwnerPresentationRuntime` 句柄。
- 运行时不允许按 `PerformerDefinition`、`BehaviorKind`、`ScopeId` 做全局 query。
- dirty 路由必须是 `owner -> compiled route span`，不是 `dirty owner -> scan definitions -> find performers`。
- `scope` 是 owner-local 的编译布局与激活 mask，不是运行时“查一批 scopeTag 对象”。
- 200K 理论 performer 规模的承载单位是 `compiled recipes`，不是 `runtime entities`。

## 2 为什么旧路线不成立

旧 performer 文档把“树形 authoring”与“运行时承载”绑成了一件事，于是自然滑向以下问题：

- `children` 被 lower 成运行时树节点，导致 `1 performer = 1 runtime object`
- behavior 仍以 generic interpreter 形式存在，dirty 后需要回查 definition 和实例集合
- `scope` 同时承担 authoring 分组和运行时查找键，最后演化成 `ScopeId == X` 式 query
- HUD/text/world mesh 共用一套 performer 实例解释链，无法做到 typed backend 直达
- authoring 中的“继承、children、binding、behavior”在运行时仍被重新解释，导致稳态成本无法收敛到 owner-local 增量

正式口径必须从“Performer-as-runtime”改成“Performer-as-authoring DSL”。

## 3 术语

| 术语 | 正式含义 |
|------|----------|
| `performer` | authoring DSL 术语，用于表达表现语义、scope、anchor、规则和 recipe |
| `PresentationArchetypeArtifact` | 某个 authoring 定义编译后的静态产物，供 runtime 按 owner 复用 |
| `OwnerPresentationRuntime` | 单个 owner entity 的运行时表现状态句柄 |
| `recipe` | 一条 typed 表现输出声明，如 mesh、hud bar、text、sound、animator |
| `route` | 某类变化触发时应执行的 owner-local 指令段 |
| `scope` | owner-local 的编译布局切片和激活位，不是运行时实体集合 |
| `anchor graph` | 编译后的锚点图，用于表达 root/hud/bone/grounding 等空间关系 |

## 4 Authoring DSL 语义模型

authoring 层允许继续使用 `performers.json` 或后续更强 DSL，但正式语义必须收敛到下列 6 类 authoring 元素：

1. `params`
2. `scopes`
3. `anchors`
4. `recipes`
5. `rules`
6. `exports / reads`

### 4.1 Params

- param 是 authoring 语义层的 SSOT。
- 编译后 param 必须被 pack 为 dense param layout，不允许运行时 parent-chain resolve。
- param lane 只允许 `Float`、`Int`、`Vector`、`Token` 四类。
- `Token` 仅用于 text 和资源查表，不能退化为自由字符串解释器。

### 4.2 Scope

scope 的正式职责只有两项：

- 定义 owner-local 的激活切片
- 定义哪些 param/export 可以被其他 scope 读取

scope 不再承担以下职责：

- 运行时实体容器
- 全局查找键
- 跨 owner 销毁或回收索引

authoring 例子：

```text
scope structure exports durability_ratio;
scope overlay reads durability_ratio, title_token;
scope working reads working;
```

编译器必须拒绝：

- 未声明 `exports` 的跨 scope 读取
- 读取未声明 param
- 运行时隐式“向父 scope 找”

### 4.3 Anchor

anchor 是 authoring 空间图，不是运行时树节点。编译后必须 lower 为 `AnchorRecipe[]`：

- `Root`
- `OwnerTransform`
- `ParentAnchor`
- `BoneAttachment`
- `GroundedOffset`
- `HudOffset`

纯管理型中间节点如果没有任何 recipe 挂载，编译后必须消除。

### 4.4 Recipe

正式 recipe 类型：

- `MeshRecipe`
- `SkinnedMeshRecipe`
- `DecalRecipe`
- `VfxRecipe`
- `SplineRecipe`
- `GroundOverlayRecipe`
- `HudBarRecipe`
- `TextRecipe`
- `SoundRecipe`
- `AnimatorRecipe`

authoring 可以写统一的 behavior 风格配置，但 compile 后不得保留 generic `BehaviorKind` 解释器。

### 4.5 Rule

rule 的职责是把 authoring 事件 lower 为 owner-local route：

- owner spawn
- owner destroy
- attribute dirty
- tag dirty
- explicit presentation event
- continuous tick

rule 不再直接产出“创建一个 performer 节点”这类运行时结构命令。

## 5 编译器分层

编译器必须至少包含下列 pass：

| Pass | 输入 | 输出 | 必须做的事 |
|------|------|------|------------|
| `Parse` | `performers.json` / DSL 文本 | authoring AST | 只做语法解析，不塞运行时语义 |
| `Normalize` | AST | normalized AST | 展开别名、继承、默认值、模板引用 |
| `Resolve` | normalized AST | resolved AST | 解析 attribute/tag/material/animator/text token 等 registry 引用 |
| `Validate` | resolved AST | validated AST | 校验 scope contract、anchor 环、recipe 合法性、无隐式查询 |
| `Lower` | validated AST | typed IR | 把 behavior/binding/rule lower 为 typed recipes 和 route ops |
| `Pack` | typed IR | `PresentationArchetypeArtifact` | 压缩 ranges、scope masks、route spans、stable id seeds |

编译器必须输出 hard error，而不是 fallback：

- 跨 scope 读取但没有 `exports`
- recipe 没有可 lower 的 backend 类型
- anchor 形成环
- 规则需要“遍历 owner 的全部 performer children”才能表达
- 需要运行时 `ScopeId == X` 查找才能成立的 authoring 语义

## 6 编译产物

正式编译产物如下：

```csharp
public sealed class PresentationArchetypeArtifact
{
    public int ArchetypeId;
    public DenseParamLayout ParamLayout;
    public ScopeLayout[] Scopes;
    public AnchorRecipe[] Anchors;

    public MeshRecipe[] Meshes;
    public HudBarRecipe[] HudBars;
    public TextRecipe[] Texts;
    public SoundRecipe[] Sounds;
    public AnimatorRecipe[] Animators;
    public SplineRecipe[] Splines;
    public GroundOverlayRecipe[] GroundOverlays;

    public OwnerSpawnProgram SpawnProgram;
    public OwnerDestroyProgram DestroyProgram;
    public AttrDirtyRouteTable AttrRoutes;
    public TagDirtyRouteTable TagRoutes;
    public EventRouteTable EventRoutes;
    public TickProgram[] TickPrograms;
    public ProjectionLayout Projection;
}
```

### 6.1 DenseParamLayout

`DenseParamLayout` 是 compile-time pack 结果：

- owner runtime 只持有 param page index
- param lookup 必须是 O(1) offset 读取
- 不允许 `ResolveFloat(handle, key)` 这种链式查询接口继续留在热路径

### 6.2 ScopeLayout

`ScopeLayout` 必须是 owner-local 静态布局：

```csharp
public readonly struct ScopeLayout
{
    public int ScopeId;
    public ulong ScopeBit;
    public IntRange AnchorRange;
    public IntRange MeshRange;
    public IntRange HudBarRange;
    public IntRange TextRange;
    public IntRange SoundRange;
    public ParamExportMask ExportMask;
}
```

`ScopeBit` 用于激活和停用。scope 的启停只能影响 owner 自身 artifact 的切片，不得触发全局 query。

### 6.3 Route Tables

route table 的索引方式必须是 compile-time 固化：

- `AttrRoutes[attributeId] -> RouteSpan`
- `TagRoutes[tagId] -> RouteSpan`
- `EventRoutes[eventId] -> RouteSpan`

route 指令必须是 owner-local opcode，不允许在 route 执行时再去查 definition 或 performer 集合。

## 7 Runtime Backend

runtime backend 的正式主语是 owner。建议结构如下：

```csharp
public struct OwnerPresentationRuntime
{
    public Entity Owner;
    public int ArchetypeId;
    public int ParamPage;
    public int AnchorPage;
    public ulong ActiveScopeMask;
    public ulong DirtyMask;
    public uint Version;
    public ushort VisibleRecipeCount;
    public ushort TickProgramCount;
}
```

运行时总存储建议拆为：

- `OwnerPresentationRuntimeStore`
- `PresentationParamPages`
- `PresentationAnchorPages`
- `ProjectionCacheStore`
- `VisibleOwnerSet`
- `TickOwnerSet`

正式约束：

- 一个 owner 最多一个 runtime handle
- runtime handle 只引用 compiled artifact，不复制 authoring config
- owner destroy 时释放 owner-local pages，不做 performer subtree 递归销毁

## 8 执行车道

正式执行车道固定为 6 条：

| Lane | 触发条件 | 输入 | 输出 | 复杂度目标 |
|------|---------|------|------|------------|
| `Materialize` | owner spawn / owner archetype attach | owner + artifact | owner runtime page、spawn ops | O(spawn owners) |
| `AttrDirty` | attribute changed | owner + attr route span | param writes、projection dirty bits | O(dirty owners × affected ops) |
| `TagDirty` | tag changed | owner + tag route span | scope mask / param writes / dirty bits | O(dirty owners × affected ops) |
| `Event` | explicit event | owner + event route span | param writes / sound / state flips | O(event owners × affected ops) |
| `Tick` | 每帧，仅 active tick owners | owner + tick programs | animator/spline/continuous state | O(active tick owners × tick ops) |
| `Projection` | 每帧，仅 visible owners | owner + active recipe slices | typed backend batches | O(visible owners × active recipes) |

正式禁止：

- `PerformerBehaviorSystem` 式 generic behavior loop
- `PerformerEmitSystem` 式 definition-driven emit interpreter
- `PerformerRuntimeSystem` 式命令回放后再去同步整张 performer 表

## 9 HUD / Text / Mesh 的 backend 组织

### 9.1 Mesh / SkinnedMesh / VFX

- compile 后进入 typed recipe arrays
- `ProjectionLane` 直接把 recipe lower 成 mesh/skin/vfx batch item
- 稳态下只允许位置、旋转、颜色等 dirty 字段更新

### 9.2 HUD Bar

HUD bar 必须 lower 为 `HudBarRecipe`：

- world space anchor 来源于 owner anchor page
- value/color/size 来源于 dense param page
- world hud item 由 projection lane 直接输出
- `WorldHudToScreenSystem` 保留为屏幕投影阶段，不再承担 performer 解释职责

### 9.3 Text

text 必须 lower 为 `TextRecipe`：

- 内容来自 token / localized text id / numeric formatter
- text recipe 只持有编译后的 token source 和格式化模式
- runtime 不允许把 text 当成自由脚本行为解释

## 10 规模与复杂度目标

目标规模不是“200K runtime performer entities”，而是：

- 30K owners
- 200K compiled recipes
- 每 owner 1 runtime handle
- 每 archetype 共享静态 artifact

复杂度目标：

- steady-state static owner：`AttrDirty = 0`、`TagDirty = 0`、`Tick = 0`
- 500 dirty owners：只跑 500 个 owner 的 route spans
- visible projection：只遍历 visible owners 的 active recipe slices
- scope toggle：只翻 owner-local `ActiveScopeMask` 并标记对应 slice dirty

正式诊断项：

- `RuntimePerformerEntityCount == 0`
- `DefinitionScanCount == 0`
- `GlobalScopeQueryCount == 0`
- `OwnerRouteFallbackCount == 0`
- `SteadyStateAllocBytesPerFrame == 0`

## 11 测试与验收合同

必须新增并长期保留以下守卫：

- `PresentationDslCompiler_RootWithHudAndText_LowersToTypedRecipes`
- `PresentationDslCompiler_ChildrenCompileToAnchorGraph_NotRuntimeNodes`
- `PresentationDslCompiler_ScopeReadWithoutExport_Fails`
- `OwnerPresentationRuntime_SpawnOwner_AllocatesSingleHandle`
- `OwnerPresentationRuntime_DurabilityDirty_OnlyTouchesDependentRecipes`
- `OwnerPresentationRuntime_DestroyScope_DoesNotQueryGlobalScopeTable`
- `PresentationScale_ThirtyThousandOwners_TwoHundredThousandRecipes_NoPerformerEntities`
- `PresentationScale_SteadyStateStatic_NoBehaviorEvalNoAlloc`

完整任务拆解见 [表现层编译式 DSL 开发计划](presentation-compiled-dsl-development-plan.md)。现有码收束与删改路径见 [表现层编译式 DSL 迁移计划](presentation-compiled-dsl-migration-plan.md)。

## 12 与旧 performer 页面关系

下列页面保留为历史材料，但不再定义正式 runtime 口径：

- `performer-as-actor-architecture.md`
- `performer-compiled-lanes.md`
- `performer-param-blackboard.md`
- `performer-development-kanban.md`
- `performer-legacy-consolidation.md`
- `performer-transform-and-attachment.md`
- `performer-raylib-uat.md`

这些页面中的以下表述都已失效：

- `PerformerInstance`
- `PerformerEntityRuntime`
- `PerformerRuntimeSystem`
- `PerformerBehaviorSystem`
- `PerformerEmitSystem`
- `CreatePerformer(parentHandle)`
- `DestroyPerformerScope` 的全局扫描语义
- `1 performer = 1 runtime entity`
