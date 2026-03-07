# Ability Interaction Model — Architecture Overview

> **SSOT**: 施法交互模型的唯一权威文档。
> 定义交互层的最小原语集、正交配置、以及 Tag/Effect/Attribute 职责边界。

---

## 1. 核心原则

**交互层的唯一职责**: 将玩家的物理输入事件转换为 GAS 能理解的 `(能力, 目标)` 对。
交互层不关心效果如何执行、伤害如何计算、状态如何流转——那些全部是 Effect + Tag + Attribute 的事。

---

## 2. 三轴模型

### Axis 1: InputConfig (输入事件配置)

不再是枚举, 而是**三个槽位的组合配置**:

```
InputConfig:
  ReactsTo: Down | Up | DownAndUp
```

- **Down only** = 瞬发型 (按下即触发)
- **DownAndUp** = 蓄力型 (按下开始, 松开结束)
- **Up only** = 极少, 特殊释放触发

> **关键洞察**: `HoldRelease` 不是独立原语。按住期间的蓄力积累是 Effect tick 写 Attribute, 不在交互层。

所有原来看似不同的"激活方式", 分解如下:

| 原概念 | InputConfig | 实际行为 |
|--------|------------|---------|
| Press (瞬发) | ReactsTo: Down | Down → Execute |
| HoldRelease (蓄力) | ReactsTo: DownAndUp | Down → Start Effect(charge), Up → Execute(read charge) |
| Toggle (切换) | ReactsTo: Down | Down → Execute(if HasTag: remove, else: add) |
| Channel (引导) | ReactsTo: Down | Down → Execute(add Channeling tag, start tick effect) |
| Recast (多段) | ReactsTo: Down | Down → Execute(check stage tag precondition) |
| AutoCast | ReactsTo: (system) | 系统自动触发, 无玩家输入 |
| Hold-to-Sustain | ReactsTo: DownAndUp | Down → Start Effect, Up → Stop Effect |

### Axis 2: TargetMode (目标选取模式)

```csharp
enum TargetMode
{
    None,       // 无目标 (自身/全局/周围)
    Unit,       // 选取一个单位
    Point,      // 选取一个地面点
    Direction,  // 选取一个方向 (施法者→光标)
    Vector      // 选取两个点 (起点+终点/方向)
}
```

### Axis 3: Acquisition (目标获取方式)

```csharp
enum Acquisition
{
    Explicit,       // 玩家手动选取 (LoL/Dota/SC2/OW/DS)
    ContextScored   // 系统评分选取 (Arkham/Spider-Man/GoW)
}
```

---

## 3. 有效组合矩阵

```
              None   Unit   Point   Direction   Vector
Down+Explicit   ✓      ✓      ✓        ✓          ✓
Down+Context    ✓      ✓      ✓        ✓          -
DnUp+Explicit   ✓      -      -        ✓          -
DnUp+Context    ✓      -      -        ✓          -
```

**约 13 个有效组合**, 覆盖全部游戏:

| 组合 | 含义 | 典型 |
|------|------|------|
| Down + None + Explicit | 按键自我/全局 | Dota BKB, LoL Flash |
| Down + Unit + Explicit | 按键→选单位 | Dota Hex, LoL Annie Q |
| Down + Point + Explicit | 按键→选地面 | Dota Chrono, SC2 Storm |
| Down + Dir + Explicit | 按键→方向 | Dota Hook, LoL Ezreal Q |
| Down + Vector + Explicit | 按键→两点 | LoL Viktor E |
| Down + None + Context | 按键, 系统选 | Arkham反击, DS弹反 |
| Down + Unit + Context | 按键, 系统选目标+技能 | Arkham攻击, Spider-Man攻击 |
| Down + Point + Context | 按键, 系统选锚点 | Spider-Man蛛丝摆荡 |
| Down + Dir + Context | 按键, 系统辅助瞄准 | GoW斧投掷(轻度锁定) |
| DnUp + None + Explicit | 蓄力自身 | DS R2蓄力 |
| DnUp + Dir + Explicit | 蓄力+方向 | LoL Varus Q, OW Widow |
| DnUp + None + Context | 蓄力, 系统选 | DS蓄力+lock-on |
| DnUp + Dir + Context | 蓄力+辅助瞄准 | Spider-Man蓄力蛛丝 |

---

## 4. 正交配置 (不构成独立模式)

| 配置 | 可选值 | 说明 |
|------|--------|------|
| **TargetFilter** | Self, Ally, Enemy, Any, Terrain, Corpse, Structure | 对 Unit/Point 的合法目标过滤 |
| **AreaShape** | Circle, Cone, Line, Rectangle, Ring, Wall, Custom | Point/Direction 的区域形状指示器 |
| **Range** | value (cm) 或 Global | 射程约束 |
| **CastBehavior** | Instant, Windup(duration) | 按下到生效的前摇时间 |

---

## 5. Ludots 现有映射

### InteractionModeType → 本模型的 Acquisition

| Ludots InteractionModeType | Acquisition | 说明 |
|---------------------------|-------------|------|
| TargetFirst (WoW) | Explicit | 先选单位, 再按键 |
| SmartCast (LoL) | Explicit | 按键时自动取光标下目标 |
| AimCast (DotA/SC2) | Explicit | 按键→进入瞄准→确认 |
| SmartCastWithIndicator | Explicit | 按住→显示指示器→松开 |
| *(新增需要)* | **ContextScored** | 系统评分选目标+技能 |

### OrderSelectionType → 本模型的 TargetMode

| Ludots OrderSelectionType | TargetMode |
|--------------------------|-----------|
| None | None |
| Entity | Unit |
| Position | Point |
| Direction | Direction |
| Vector | Vector |
| Entities | Unit (multi, via SelectionGate) |

### AbilityExecSpec Gates → Response Window / Insertable Context

| Gate | 用途 |
|------|------|
| InputGate | P5 确认窗口, P2 效果变体选择 |
| SelectionGate | P1 选额外目标, P6 多目标选取 |
| EventGate | O1-O8 响应窗口(等待事件) |

### Response Chain → 响应窗口

| ResponseType | 清单映射 |
|-------------|---------|
| Hook | O5 取消/无效化 |
| Modify | O6 修改数值 |
| Chain | O7 追加效果, O2 连锁 |
| PromptInput | O1/O3/O4 交互式响应 |

---

## 6. Tag/Effect/Attribute 职责分界

**铁律: 以下概念不允许进入交互层枚举:**

| 概念 | 归属层 | 实现方式 |
|------|--------|---------|
| 蓄力积累 | Effect | BeginCharge Effect tick → charge_amount Attribute |
| 连击段数 | Tag | combo_stage Tag 递增 |
| 连击计时 | Attribute | last_hit_time Attribute, 下段读 delta |
| 命中确认 | Tag | OnHit Effect → hit_confirmed Tag |
| Toggle | Tag | HasTag(active) ? Remove : Add |
| Channel中断 | Tag | channeling Tag + CC Effect 移除 |
| Recast门控 | Tag | stage_complete Tag 做 precondition |
| 变身/形态 | Tag | form Tag 切换 ability set |
| 标记引爆 | Tag | marked Tag + 引爆技能 precondition |
| Posture/架势 | Attribute | posture Attribute 累积, 满值加 Tag |
| 连击仪表 | Attribute | combo_meter Attribute, 阈值加 Tag |
| 资源消耗 | Attribute | Precondition 检查 mana/energy Attribute |
| 冷却 | Attribute | cooldown Attribute + 系统 tick 递减 |
| 充能 | Attribute | charge_count Attribute + 恢复 timer |
| 上下文选取 | ContextGroup | ScorePipeline (读 Tag + Attribute + 距离 + 角度) |
| 响应窗口 | ResponseChain | WindowPhase + PromptInput + OrderRequest |
| 插入上下文 | Gate | InputGate / SelectionGate / EventGate |

---

## 7. ContextScored 扩展: ContextGroup 机制

ContextScored 需要一个额外的 **ContextGroup + ScorePipeline** 机制:

```
InputBinding "Attack"
  → ContextGroup: [飞扑攻击, 普通拳击, 地面终结, 撞墙击, 空中连击, ...]
    → ScorePipeline 对每个候选评分
      → 选出最高分的候选
        → 该候选自带 TargetMode + 效果定义
          → 系统自动获取目标
            → 执行
```

### 评分因子 (全部可用 Tag + Attribute + Spatial 查询):

| Factor | Data Source |
|--------|------------|
| Distance | SpatialQueryService: 施法者与候选目标距离 |
| Angle | 施法者朝向与目标方向夹角 |
| InputDirection | 摇杆/移动输入方向 (soft bias) |
| TargetTags | 目标 Tag: Knockdown, Stunned, Attacking, Armed |
| SelfTags | 自身 Tag: Airborne, Grounded, NearWall, ComboStage |
| EnvironmentTags | 环境实体 Tag: Throwable, Interactable, Ledge |
| Priority | 设计师配置权重 |

### 候选定义:

```json
{
  "id": "wall_slam",
  "preconditions": [
    { "kind": "SelfTag", "tag": "NearWall" },
    { "kind": "Distance", "max": 200 }
  ],
  "score_weights": {
    "distance": 0.3,
    "angle": 0.3,
    "env_bonus_NearWall": 50
  },
  "target_mode": "Unit",
  "ability_id": "wall_slam_ability"
}
```

---

## 8. 163 条清单的完全覆盖论证

全部 163 条用户体验场景 (见 `01_user_experience_checklist.md`) 可被以下结构表达:

```
InteractionConfig : ReactsTo (Down | Up | DownAndUp)
TargetMode        : None | Unit | Point | Direction | Vector
Acquisition       : Explicit | ContextScored
+ 正交配置         : TargetFilter, AreaShape, Range, CastBehavior
+ Tag/Effect/Attr  : 见第 6 节映射表
+ ResponseChain    : Hook/Modify/Chain/PromptInput
+ Gates            : InputGate/SelectionGate/EventGate
```

**没有一条场景逃出此模型。**

---

## 9. 文件清单

| 文件 | 内容 |
|------|------|
| `00_interaction_model_overview.md` | 本文档 (架构总览) |
| `01_user_experience_checklist.md` | 163 条用户体验清单 |
| `features/` | 各 feature 独立实现方案 |
| `99_gap_analysis.md` | 缺口分析与修改建议 |
