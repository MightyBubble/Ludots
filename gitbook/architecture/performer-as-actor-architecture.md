# Performer-as-Actor 架构设计

本文定义 Ludots 表现层的目标架构：**Performer 是唯一的表现编排者**，基于 SC2 Actor 模型，拥有树形层级、行为组合、命令驱动和参数黑板继承。

## 1 动机

当前表现层存在系统性问题（详见 Issue #136）：

- Performer 是扁平的，无法组合成树形结构
- `EntityVisualEmitSystem` 绕过 Performer 直接从 ECS 实体发射视觉，构成双重真相
- 没有行为系统，资产绑定是定义上的静态属性而非可激活的行为
- 参数黑板限制 8 个 override，无父→子继承
- `PerformerEmitSystem` 是 732 行的 God System，承担 10+ 个独立职责
- C#/JSON 定义双轨、幽灵字段、Graph VM 代码复制等多重真相

## 2 核心原则

- Entity 是纯逻辑数据，不知道 Performer 存在
- Performer 是唯一的表现编排者，所有可见物都由 Performer 驱动
- Performer 与 Entity 不存在一对一关系
- Behavior 是组合单元，Performer 通过 Behavior 组合视觉输出
- Command 驱动一切：创建/销毁 performer、激活/停用 behavior、设置参数
- 平台适配层决定 HOW（怎么渲染），Core 只声明 WHAT（渲染什么）
- Rule 是响应事件的唯一配置点，不存在隐式 entity-scoped 收集

## 3 UAT 验收场景：铁匠铺

一个铁匠铺 gameplay entity，由 Performer 树驱动全部表现：

```
blacksmith_root (hook entity "blacksmith", 无自身 asset, 只做 scope 管理)
├── blacksmith_workshop_1 (scope: structure)
│   [AssetBinding(Mesh)] [AttributeBinding(durability→阈值→mesh swap)] [Material(region→砖色)]
├── blacksmith_workshop_2 (scope: structure)
│   [AssetBinding(Mesh)] [AttributeBinding] [Material]
├── blacksmith_furnace (scope: structure)
│   [AssetBinding(Mesh)]
├── blacksmith_smoke (scope: working) ← "working" tag 控制此 scope 的创建/销毁
│   [AssetBinding(VFX, chimney_smoke)]
└── blacksmith_worker (scope: working)
    [AssetBinding(SkinnedMesh)] [Animator(spline patrol)] [Sound(anvil, loop)]
```

验收事件流：

1. Entity 创建 → root performer 创建 → `children` 中的 structure scope 自动展开（3 个建筑 mesh）
2. "working" tag ON → Rule → `CreatePerformer(smoke, parent=root)` + `CreatePerformer(worker, parent=root)` → 烟囱和工人动态创建
3. "working" tag OFF → Rule → `DestroyPerformerScope(working)` → smoke + worker 销毁
4. 日夜切换 → `GlobalDayNight` → Rule → `SetParam(lampKey)` → Material behavior 切灯光材质
5. 区域参数 → root param=0(北方) → 子 performer blackboard 继承 → Material behavior 查表 → 黑砖
6. 耐久度变化 → AttributeBinding behavior 读比例 → 阈值映射 → AssetBinding 切 mesh（完善/破损/废墟）
7. 工人 Animator behavior → 样条巡逻 → 到点播动画
8. 工人 Sound behavior → 锤击声循环播放（working scope 存在时）

## 4 核心类型

### 4.1 PerformerDefinition（重写）

```csharp
public sealed class PerformerDefinition
{
    public int Id;
    public string Key;                       // "blacksmith_root"
    public string Extends;                   // 可选：继承父定义（§9.6）
    public ChildPerformerRef[] Children;      // 语法糖，ConfigLoader 展开为 PerformerCreated Rule（§9.10）
    public BehaviorSlot[] Behaviors;          // 行为组合
    public PerformerRule[] Rules;             // 事件→命令（复用现有模式）
    public PerformerParamBinding[] Bindings;  // 每帧绑定（复用）
    public ParamDefault[] ParamDefaults;      // 黑板默认值
    public float DefaultLifetime;             // <=0 = 持久
    public ConditionRef VisibilityCondition;  // 复用
}

public struct ChildPerformerRef
{
    public int DefinitionId;
    public int ScopeTag;                     // 用于按组销毁
    public ParamDefault[] ParamOverrides;    // 父→子参数传递
}

public struct ParamDefault
{
    public int ParamKey;
    public ParamLane Lane;
    public float FloatValue;
    public int IntValue;
    public Vector4 VectorValue;
}
```

不再有 `VisualKind`、`EntityScope`、`MeshOrShapeId` 等扁平字段。视觉输出完全由 Behavior 组合决定。

### 4.2 BehaviorSlot — 行为是组合单元

采用 flat union struct（ECS 数据导向风格，cache-friendly、零 GC）：

```csharp
public struct BehaviorSlot
{
    public int SlotIndex;                    // 行为在 Performer 内的唯一索引
    public BehaviorKind Kind;
    public bool ActiveByDefault;
    public ConditionRef ActivationCondition; // 可选：声明式激活条件

    // 按 Kind 解释的配置数据（union 风格）
    public AssetBindingConfig AssetBinding;
    public AttributeBindingConfig AttributeBinding;
    public TagBindingConfig TagBinding;
    public AnimatorConfig Animator;
    public AttachmentConfig Attachment;
    public SoundConfig Sound;
    public MaterialConfig Material;
    public SplineConfig Spline;
}

public enum BehaviorKind : byte
{
    AssetBinding = 1,      // 绑定资产（mesh/skinned/decal/vfx）
    AttributeBinding = 2,  // GAS 属性→param（含阈值映射）
    TagBinding = 3,        // GAS tag→param
    Animator = 4,          // 动画控制器（读写 param blackboard）
    Attachment = 5,        // 骨骼挂点
    Sound = 6,             // 声音
    Material = 7,          // 材质切换（param→材质表）
    Spline = 8,            // 样条路径（道路、巡逻路线）
}

public enum AssetKind : byte
{
    Mesh = 1, SkinnedMesh = 2, Decal = 3, VFX = 4, Sound = 5, Spline = 6,
    WorldHud = 7,    // 血条、名字板等世界空间 HUD
    WorldText = 8,   // 浮动战斗文字
    GroundOverlay = 9, // 地面指示器（Circle/Cone/Line/Ring）
}
```

各 Config 结构体：

```csharp
// ── Asset Binding：绑定一个可渲染资产 ──
public struct AssetBindingConfig
{
    public AssetKind AssetKind;          // Mesh / SkinnedMesh / Decal / VFX / Sound / Spline / WorldHud / WorldText / GroundOverlay
    public int AssetId;                  // MeshAssetRegistry 中的 ID
    public int MaterialId;               // 默认材质
    public VisualRenderPath RenderPath;  // 渲染通道（从 VisualRuntimeState 迁入）
    public VisualMobility Mobility;      // 静态/可移动（从 VisualRuntimeState 迁入）
    public Vector3 LocalOffset;
    public Quaternion LocalRotation;
    public Vector3 LocalScale;
    public int ScaleParamKey;            // -1 = 不绑定
    public int ColorParamKey;            // -1 = 不绑定（读 Vector lane）
    public int MaterialParamKey;         // -1 = 不绑定（读 Int lane，运行时切材质）
    public int AssetSwapParamKey;        // -1 = 不绑定（读 Int lane，运行时切 mesh）
    public int VisibilityParamKey;       // -1 = 不绑定（读 Int lane，0=hidden/1=visible）
    public GroundingMode Grounding;      // 默认 None（详见 performer-transform-and-attachment.md §4）
    public float GroundingOffset;        // 贴合后的额外 Y 偏移
}

// ── Attribute Binding：GAS 属性→param + 阈值映射 ──
public struct AttributeBindingConfig
{
    public int AttributeId;
    public int TargetParamKey;
    public ValueSourceKind Mode;         // Attribute / AttributeRatio / AttributeBase
    public ThresholdMapping[] Thresholds;
}

public struct ThresholdMapping
{
    public float Threshold;              // 低于此值时触发
    public int OutputParamKey;
    public float OutputValue;
}

// ── Tag Binding：GAS tag→param ──
public struct TagBindingConfig
{
    public int TagId;
    public int TargetParamKey;
    public bool InvertLogic;             // true = tag 失效时写入 1.0
}

// ── Animator：动画控制器，读写 performer param blackboard ──
public struct AnimatorConfig
{
    public int AnimatorControllerId;
    public int AnimationProfileId;
    public int SpeedParamKey;            // blackboard 中的速度参数
    public int StateParamKey;            // blackboard 中的状态索引输出
    // Animator 的 SetFloat/SetInt/SetBool 直接映射到 blackboard 的对应 lane
    // 不再有独立的 AnimatorParameterBuffer，统一由 performer blackboard 驱动
}

// ── Attachment：骨骼挂点 ──
public struct AttachmentConfig
{
    public int BoneId;
    public Vector3 Offset;
}

// ── Sound：声音 ──
public struct SoundConfig
{
    public int SoundAssetId;
    public bool Loop;
    public float Volume;
    public int VolumeParamKey;           // -1 = 不绑定
}

// ── Material：材质切换 ──
public struct MaterialConfig
{
    public int BaseMaterialId;
    public int MaterialSwapParamKey;
    public MaterialSwapEntry[] SwapTable;
}

public struct MaterialSwapEntry
{
    public float ParamValue;
    public int MaterialId;
}

// ── Spline：样条路径（道路渲染、巡逻路线）──
public struct SplineConfig
{
    public int SplineAssetId;
    public SplineUsage Usage;            // Render / Patrol
    public int WidthParamKey;            // -1 = 不绑定
    public int ColorParamKey;            // -1 = 不绑定
    public int SpeedParamKey;            // Patrol 模式下的移动速度
    public int ProgressParamKey;         // 当前进度 0~1 输出
    public bool Loop;
    public bool PingPong;
    public int WaypointEventId;          // 到达 waypoint 时触发的事件 ID（0=不触发）
}

public enum SplineUsage : byte
{
    Render = 1,   // 渲染为道路/河流等可见样条
    Patrol = 2,   // 作为巡逻路线驱动 Performer 位置
}
```

### 4.3 PerformerInstance（扩展层级）

详见 [Transform、Grounding 与 Attachment](performer-transform-and-attachment.md)。

```csharp
public struct PerformerInstance
{
    public int DefId, ScopeId, StableId;
    public Entity Owner;
    public PresentationAnchorKind AnchorKind;
    public Vector3 WorldPosition;
    public Quaternion WorldRotation;
    public Vector3 WorldScale;
    public float Elapsed;
    public bool Active;
    public TransformSource TransformSource;
    // 树形结构
    public int ParentHandle;             // -1 = 根节点
    public int FirstChildHandle;         // -1 = 叶子
    public int NextSiblingHandle;        // -1 = 末尾
    // 行为激活位图
    public uint BehaviorActiveMask;      // bit=1 激活（最多 32 slot）
}
```

### 4.4 PerformerParamBlackboard — 多类型参数总线

详见 [参数黑板与 Animator 统一](performer-param-blackboard.md)。

分三条 lane（Float / Int / Vector），统一 Performer 参数和 Animator 参数，支持父→子继承链。

### 4.5 PerformerCommand（扩展）

```csharp
public enum PerformerCommandKind : byte
{
    None = 0,
    CreatePerformer = 1,       // parentHandle 决定层级（-1=root，>=0=子节点）
    DestroyPerformer = 2,      // 递归销毁子树
    DestroyPerformerScope = 3, // 按 scopeTag 销毁
    SetParam = 4,              // 写入 blackboard
    ActivateBehavior = 5,
    DeactivateBehavior = 6,
    SinkParamToAsset = 7,      // 强制刷新 param → asset 属性
}
```

### 4.6 PresentationEventKind（扩展）

```csharp
public enum PresentationEventKind : byte
{
    // 现有
    GameplayEvent = 1,
    TagEffectiveChanged = 2,
    PerformerCreated = 10,
    PerformerDestroyed = 11,
    EffectApplied = 20,
    CastCommitted = 21,
    CastFailed = 22,
    // 新增
    GlobalDayNight = 30,
    GlobalRegionChanged = 31,
    GlobalWeather = 32,
    AttributeValueChanged = 40,
}
```

## 5 系统职责与数据流

```
GAS Domain              Global Domain
 TagChanged              DayNight / Region / Weather
 EffectApplied
 AttributeChanged
      │                       │
      ▼                       ▼
   PresentationEventStream
      │
      ▼
  PerformerRuleSystem [复用]       Event × Rule → Command（倒排索引）
      │
      ▼
  PerformerCommandBuffer
      │
      ▼
  PerformerRuntimeSystem [重写]    实例创建/销毁/树管理/黑板写入
      │
      ▼
  PerformerBehaviorSystem [新]     行为驱动：
      │                           - AttributeBinding: 属性→param + 阈值映射
      │                           - TagBinding: tag→param
      │                           - Material: param→materialId 查表
      │                           - Animator: 动画状态推进
      │                           - Sound: 声音请求
      │
      ▼
  PerformerEmitSystem [重写]       AssetBinding → PresentationVisualProxy
      │                           (~200行，只做 asset emit)
        ▼
    PrimitiveDrawBuffer / SkinnedVisualBatchBuffer / SoundRequestBuffer[新]
        │
        ▼
    Platform Adapter（不变）

说明：迁移阶段允许保留一个 `LegacyPerformerEmitSystem` 承接旧 `VisualKind` / entity-scoped model 路径，但它不再承担 Wave 4 的 AssetBinding emit 职责；`PerformerEmitSystem` 的单一职责仍然是“只处理 AssetBinding emit”。
```

### 5.1 各系统职责

| 系统 | 状态 | 职责 |
|------|------|------|
| `PresentationBridgeSystem` | 保留 | GAS 事件 → PresentationEvent |
| `GlobalEventBridgeSystem` | 新增 | 全局事件（日夜/区域/天气）→ PresentationEvent |
| `PerformerRuleSystem` | 保留+小改 | Event × Rule → Command（倒排索引，扩展新 CommandKind） |
| `PerformerRuntimeSystem` | 重写 | 消费 Command，管理实例树（递归创建/销毁子树、scope 销毁、behavior mask 翻转、黑板写入） |
| `PerformerBehaviorSystem` | 新增 | 遍历激活 Behavior，驱动 AttributeBinding/TagBinding/Material/Animator/Sound |
| `PerformerEmitSystem` | 重写 | 遍历有 AssetBinding 的 Performer，解析 param，发射 PresentationVisualProxy |

### 5.2 删除的系统

| 系统 | 原因 |
|------|------|
| `EntityVisualEmitSystem` | 绕过 Performer 的双真值源，所有 entity 视觉改走 Performer |
| `PresentationStartupPerformerSystem` | 启动 performer 改为 PerformerRuntimeSystem 的子树自动展开 |

## 6 JSON Schema

```jsonc
// mods/SomeMod/assets/Presentation/performers.json
[
  {
    "id": "blacksmith_root",
    "bindings": [
      { "paramKey": 100, "source": "attributeRatio", "attributeName": "durability" }
    ],
    "children": [
      { "definitionId": "blacksmith_workshop_1", "scopeTag": "structure" },
      { "definitionId": "blacksmith_workshop_2", "scopeTag": "structure" },
      { "definitionId": "blacksmith_furnace", "scopeTag": "structure" }
    ],
    "rules": [
      {
        "event": { "kind": "TagEffectiveChanged", "keyId": "working" },
        "condition": { "inline": "TagGained" },
        "command": { "kind": "CreatePerformer", "definitionId": "blacksmith_smoke", "scopeTag": "working" }
      },
      {
        "event": { "kind": "TagEffectiveChanged", "keyId": "working" },
        "condition": { "inline": "TagGained" },
        "command": { "kind": "CreatePerformer", "definitionId": "blacksmith_worker", "scopeTag": "working" }
      },
      {
        "event": { "kind": "TagEffectiveChanged", "keyId": "working" },
        "condition": { "inline": "TagLost" },
        "command": { "kind": "DestroyPerformerScope", "scopeTag": "working" }
      },
      {
        "event": { "kind": "GlobalDayNight" },
        "command": { "kind": "SetParam", "paramKey": 200, "paramValue": 1.0 }
      }
    ],
    "paramDefaults": [
      { "paramKey": 300, "lane": "Float", "floatValue": 0 }
    ]
  },
  {
    "id": "blacksmith_workshop_1",
    "behaviors": [
      {
        "slot": 0, "kind": "AssetBinding", "activeByDefault": true,
        "assetBinding": {
          "assetKind": "Mesh", "assetId": "workshop_mesh_a",
          "localOffset": [2.0, 0, 0],
          "assetSwapParamKey": 101
        }
      },
      {
        "slot": 1, "kind": "AttributeBinding", "activeByDefault": true,
        "attributeBinding": {
          "attributeName": "durability", "targetParamKey": 100,
          "mode": "attributeRatio",
          "thresholds": [
            { "threshold": 0.66, "outputParamKey": 101, "outputValue": 0 },
            { "threshold": 0.33, "outputParamKey": 101, "outputValue": 1 },
            { "threshold": 0.0,  "outputParamKey": 101, "outputValue": 2 }
          ]
        }
      },
      {
        "slot": 2, "kind": "Material", "activeByDefault": true,
        "material": {
          "baseMaterialId": "brick_north",
          "materialSwapParamKey": 300,
          "swapTable": [
            { "paramValue": 0, "materialId": "brick_black" },
            { "paramValue": 1, "materialId": "brick_red" }
          ]
        }
      }
    ]
  },
  {
    "id": "blacksmith_worker",
    "behaviors": [
      {
        "slot": 0, "kind": "AssetBinding", "activeByDefault": true,
        "assetBinding": { "assetKind": "SkinnedMesh", "assetId": "worker_model" }
      },
      {
        "slot": 1, "kind": "Animator", "activeByDefault": true,
        "animator": {
          "animatorControllerId": "worker_anim",
          "speedParamKey": 10
        }
      },
      {
        "slot": 2, "kind": "Sound", "activeByDefault": true,
        "sound": { "soundAssetId": "anvil_hammering", "loop": true }
      },
      {
        "slot": 3, "kind": "Spline", "activeByDefault": true,
        "spline": {
          "splineAssetId": "blacksmith_patrol",
          "usage": "Patrol",
          "speedParamKey": 10,
          "loop": true
        }
      }
    ]
  },
  {
    "id": "blacksmith_smoke",
    "behaviors": [
      {
        "slot": 0, "kind": "AssetBinding", "activeByDefault": true,
        "assetBinding": {
          "assetKind": "VFX", "assetId": "chimney_smoke",
          "localOffset": [0, 5.0, 0]
        }
      }
    ]
  }
]
```

### 6.1 JSON Schema 规范字段名

每个 JSON 字段只有一个规范名，ConfigLoader 不得接受别名。以下为易混淆字段的唯一规范名：

| 上下文 | 规范名 | 禁止别名 |
|--------|--------|---------|
| PerformerCommand | `kind` | ~~commandKind~~ |
| PerformerCommand | `definitionId` | ~~performerDefinitionId~~ |
| PerformerCommand | `scopeTag` | ~~scopeId~~ |
| PerformerCommand | `paramLane` | ~~lane~~（command 上下文） |
| PerformerCommand | `targetBehaviorSlot` | ~~behaviorSlot~~ |
| ParamDefault | `floatValue` / `intValue` / `vectorValue` | ~~value~~（必须显式指定类型） |
| ParamDefault | `lane`（显式） | 禁止隐式推断 |
| BehaviorSlot | `slot` | ~~slotIndex~~ |
| TagBindingConfig | `tagId` | ~~tag~~ |
| SplineConfig | `splineAssetId` | ~~splinePathId~~ |
| AttributeBindingConfig | `attributeId` | ~~sourceId~~ |
| WorldText binding | `textToken` | ~~sourceKey~~ |

## 7 相关文档

- [参数黑板与 Animator 统一](performer-param-blackboard.md) — 多类型 param blackboard 设计
- [Transform、Grounding 与 Attachment](performer-transform-and-attachment.md) — 变换传播、地面贴合、骨骼挂载
- [Raylib UAT 测试计划](performer-raylib-uat.md) — 逐 AssetKind、逐 BehaviorKind 的验收测试
- [现有基建收尾整合](performer-legacy-consolidation.md) — Animator/VisualTemplate/Prefab/渲染类型的迁移方案

## 8 实施阶段

| Phase | 内容 |
|-------|------|
| 1 | 基础类型：BehaviorSlot, AssetKind, SplineConfig, PerformerParamBlackboard（多类型）, 扩展 PerformerCommand/PresentationEventKind |
| 2 | 树形实例：重写 PerformerInstance + PerformerInstanceBuffer（parent/child 指针、递归销毁、TransformSource） |
| 3 | 定义重写：重写 PerformerDefinition + ConfigLoader（新 JSON schema） |
| 4 | Runtime 重写：重写 PerformerRuntimeSystem（子树创建、scope 销毁、behavior mask） |
| 5 | Behavior 系统：新增 PerformerBehaviorSystem + Animator 参数统一 + Transform 计算 + Grounding |
| 6 | Emit 重写：重写 PerformerEmitSystem（只处理 AssetBinding emit）+ VisualRenderPayload 提取 |
| 7 | 全局事件 + 清理：GlobalEventBridgeSystem + 删除遗留代码 |
| 8 | Raylib UAT：Layer 1→2→3 逐层验证，铁匠铺端到端 |
| 9 | UE5 适配：UE5 adapter 实现 AssetKind 映射，跑同一套 UAT JSON |

## 9 补充设计：第一性原理覆盖

### 9.1 Performer 与 Entity 的关系

- 一个 entity 可以有多个 root performer（如一个英雄同时有模型 performer 和头顶 UI performer）
- 一个 performer 只能有一个 owner entity（或无 owner，如纯装饰 performer）
- owner entity 销毁时，其所有 root performer 自动递归销毁（由 `PerformerRuntimeSystem` 在 entity destroy 事件时执行）
- performer 不能比 owner entity 活得更久，除非 owner 为 null（世界固定 performer）

### 9.2 销毁序列与死亡动画

`DestroyPerformer` 命令支持两种模式：

```csharp
public enum DestroyMode : byte
{
    Immediate = 0,    // 立即销毁，回收 StableId
    Deferred = 1,     // 标记 PendingDestroy，等待销毁动画完成后回收
}
```

Deferred 模式下：
1. `PerformerRuntimeSystem` 标记实例为 `PendingDestroy`
2. `PerformerBehaviorSystem` 检测到 PendingDestroy → 触发 `PerformerDestroyed` 事件
3. Rule 可以响应此事件播放死亡动画（如 `ActivateBehavior(deathAnim)`）
4. 动画完成后，Animator feedback 写回 blackboard → Rule 触发 `DestroyPerformer(Immediate)`
5. 或者超过 `DefaultLifetime` 后强制回收

### 9.3 可见性、雾战与选择高亮

Performer 的可见性由三层控制：

1. **Entity CullState 继承** — owner entity 被摄像机裁剪时，其 performer 树跳过 emit（不发射 proxy）。子 performer 继承父的 cull 状态。
2. **VisibilityCondition** — 声明式条件（如 `OwnerCullVisible`、`SourceIsLocalPlayer`），每帧评估。
3. **VisibilityParamKey** — 命令式控制（如雾战系统写入 0=hidden/1=visible）。

选择高亮和队伍着色通过 Material behavior 实现：
- 选择系统写入 `selectionParamKey=1` → Material behavior 切换到高亮材质
- 队伍系统写入 `teamColorParamKey=vec4(r,g,b,a)` → AssetBinding 的 `ColorParamKey` 读取

### 9.4 LOD 与大规模场景

Performer 的 LOD 与 entity 仿真 LOD（`entity-simulation-layering.md`）协调：

| Entity 车道 | Performer 行为 |
|------------|---------------|
| Authority（不可裁剪） | 全部 behavior 激活 |
| Budgeted（可降频） | 降频评估 behavior（如 Animator 降到 10fps） |
| MassCrowd | 只保留 AssetBinding，禁用 Animator/Sound/Material |
| Culled | 跳过整棵 performer 树 |

`PerformerBehaviorSystem` 读取 entity 的 `SimulationLodState`，按档位跳过非必要 behavior。

### 9.5 Performer 池化

`PerformerInstanceBuffer` 已经是固定容量槽位 + 自由列表的设计，天然支持池化。StableId 在 `Release()` 时回收到自由列表，下次 `Allocate()` 复用。Blackboard 的 per-handle 段在 `ClearAll()` 时归零但不释放内存。

### 9.6 定义继承

支持 JSON 级别的定义继承：

```jsonc
{
  "id": "knight",
  "extends": "base_unit",
  "behaviors": [
    { "slot": 2, "kind": "Material", "material": { "baseMaterialId": "knight_armor" } }
  ]
}
```

`extends` 表示继承父定义的所有 children/behaviors/rules/bindings/paramDefaults，子定义按 `slot` 索引覆盖或追加。ConfigLoader 在加载时展开继承链。

### 9.7 HUD 元素、投射物与摄像机特效

| 类别 | 是否走 Performer | 说明 |
|------|-----------------|------|
| 血条/名字板 | 是 | AssetKind 扩展 `WorldHud`，PerformerEmitSystem 发射到 HudBuffer |
| 浮动战斗文字 | 是 | 一次性 performer（DefaultLifetime > 0），AssetKind = WorldText |
| 投射物视觉 | 是 | 投射物 entity 的 performer，AssetBinding(Mesh/VFX) + Spline(Patrol) |
| 地面指示器 | 是 | AssetKind = GroundOverlay，GroundingMode = AlignToSurface |
| 摄像机抖动/色调 | 否 | 走独立的 CameraEffectSystem，不属于 performer 职责 |

### 9.8 插值

Performer 不做独立插值。`TransformSource.EntityTransform` 模式下读取的 `VisualTransform` 已经是插值后的结果（由 `WorldToVisualSyncSystem` 完成）。`InheritParent` 模式下，父的 WorldPosition 已经是插值后的值，子自然继承。

### 9.9 热重载

运行时修改 performers.json 后：
- 已有 performer 实例不自动更新（定义是创建时快照）
- 新创建的 performer 使用新定义
- 如需强制刷新，销毁并重建 performer 树

### 9.10 层级创建模型

Performer 树的层级关系完全由 `CreatePerformer` 命令决定，命令中指定 `parentHandle`。不存在独立的"children 声明"机制。

```csharp
public struct PerformerCommand
{
    public PerformerCommandKind CommandKind;
    public int PerformerDefinitionId;
    public int ParentHandle;             // -1 = 创建为 root，>=0 = 创建为指定 performer 的子节点
    public int ScopeTag;
    // ...
}
```

层级可以无限嵌套：A 创建 B（parent=A），B 的 Rule 再创建 C（parent=B），形成 A→B→C 树。

JSON 中的 `children` 字段是语法糖——ConfigLoader 将其展开为 `PerformerCreated` 事件的自动 Rule：

```jsonc
// 这两种写法等价：

// 写法 1：children 语法糖
{ "id": "root", "children": [{ "definitionId": "child_a", "scopeTag": "s" }] }

// 写法 2：显式 Rule（ConfigLoader 展开后的实际形式）
{ "id": "root", "rules": [
    { "event": { "kind": "PerformerCreated" },
      "command": { "kind": "CreatePerformer", "definitionId": "child_a", "scopeTag": "s" } }
]}
```

这意味着：
- 静态子树用 `children` 简写
- 动态子树用 Rule + `CreatePerformer`（如 working tag 触发）
- 深层嵌套：子 performer 的定义中也可以有 `children` 或 Rule，递归展开
- 所有层级关系最终都归结为 `CreatePerformer(parentHandle=X)`
