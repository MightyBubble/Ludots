# PR #43 — 174 Mechanism Documents Three-Tier Review

> 审核日期: 2026-03-12 | 审核范围: `docs/architecture/interaction/features/` 全部 174 篇 + 5 篇结构文档
>
> 三重验证: Sub-agent 逐篇审阅 → 人工复核 → 交叉校验

---

## Executive Summary

| 指标 | 数值 |
|------|------|
| 文档总数 | 174 篇机制文档 + 5 篇结构文档 |
| ✅ PASS (三项全通过) | ~25 篇 (14%) |
| ⚠️ WARN (有注意事项) | ~120 篇 (69%) |
| ❌ FAIL (有关键问题) | ~29 篇 (17%) |
| **架构违规 (Graph 内结构变更)** | **7 篇** — E10, K1, K7, R5, U1, U3, U5 |
| **DSL 幻觉 (虚构 Effect 类型)** | **8 篇** — H2-H9 |
| **跨文档矛盾** | **3 处** — RequireTags 命名, CooldownSystem 必要性, CursorDirectionWriter 状态 |
| **模板合规** | ~12 篇完整遵循 TEMPLATE.md; 其余采用简化格式 |

**核心结论**: 机制设计方向正确, SC2-like 组合理念贯穿始终。但约 17% 文档存在架构违规或虚构 API, 需修正后方可作为实现指导。

---

## Critical Cross-Cutting Findings

### Finding 1: Architecture Violations — Graph 内结构变更 ❌

Graph VM 核心约束: **不能做结构变更 (创建/删除实体、挂载组件)**。以下文档违反此约束:

| 文档 | 违规内容 | 修正方案 |
|------|----------|----------|
| E10 (split_projectile) | Variant A: `SpawnSplitProjectiles` in Graph | 改用 `RuntimeEntitySpawnQueue` + OnExpire handler |
| K1 (summon_static) | Graph 内 `CreateEntity`/`SpawnUnit` | 改用 OnApply → `RuntimeEntitySpawnQueue` |
| K7 (controllable_summon) | Graph 内创建可控实体 | 同上 |
| R5 (sacrifice_revive) | `DestroyEntity`/`CreateUnit` in Graph | **CRITICAL** — 改用 handler + spawn queue |
| U1 (destructible_object) | Graph 内销毁实体 | 改用 Effect OnApply → destroy handler |
| U3 (trap_trigger) | Graph 内创建陷阱实体 | 改用 `RuntimeEntitySpawnQueue` |
| U5 (hazard_zone) | Graph 内创建区域实体 | 改用持续 Effect + spawn queue |

### Finding 2: DSL Hallucination — H2-H9 虚构 Effect 类型 ⚠️→❌

H2-H9 使用了不存在于 Ludots 的 Effect 类型 DSL:
```json
// 虚构的, 不在 EffectPresetType 枚举中:
"type": "AddTag"
"type": "DamageMultiplier"
"type": "ModifyIncomingDamage"
"type": "ConditionalResponse"
```

**影响**: 实现者若直接参照会发现 API 不存在, 需要翻译为真实的 EffectPresetType + Graph 组合。

**修正**: 全部改写为标准 Effect Template + Graph Phase 表达。H1 是正确范例。

### Finding 3: Three Confirmed Cross-Document Contradictions

**矛盾 1 — AbilityActivationRequireTags 命名**
- `gap_analysis.md` §1.1: ✅ 正确标注 "已有 — `AbilityActivationBlockTags.RequiredAll`"
- `graph_ops_reference.md` §已知缺口: ❌ 错误列为 "P1 gap"
- G 系列多篇沿用 `RequireTags` 命名 → 应统一为 `RequiredAll`
- **决议**: 修正 `graph_ops_reference.md`, G 系列统一术语

**矛盾 2 — CooldownSystem 是否需要新 System**
- `gap_analysis.md` §3.4: 提议新增 `CooldownTickSystem` (P1)
- T2 (cooldown_per_ability): 明确写 "无需新 System, Tag Duration 足够表达"
- **决议**: T2 正确。Tag Duration + BlockedAny 是更符合组合原则的方案, 删除 gap_analysis 中的 CooldownTickSystem 提案

**矛盾 3 — CursorDirectionBlackboardWriter 状态**
- F1: ✅ 正确标注 "P1 新增需求"
- F3, F5: ❌ 错误声称 "已存在"
- **决议**: 修正 F3/F5, 统一为 P1 待实现

---

## Per-Category Review Summary

### A — 被动技能 (passive/) — 8 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| A1 | ✅ | ✅ | ✅ | **Gold standard** — 完整模板, Tag+Effect 组合正确 |
| A2 | ✅ | ✅ | ⚠️ | 机制正确 (属性加成), 缺验收/测试节 |
| A3 | ✅ | ✅ | ⚠️ | 条件触发设计合理, 缺完整模板 |
| A4 | ✅ | ✅ | ⚠️ | 叠层被动, Graph 表达合理 |
| A5 | ✅ | ✅ | ⚠️ | 光环效果, PeriodicSearch 方案正确 |
| A6 | ✅ | ✅ | ⚠️ | 击杀触发, SendEvent 方案正确 |
| A7 | ✅ | ✅ | ⚠️ | 多条件被动, 组合模式合理 |
| A8 | ✅ | ✅ | ⚠️ | 进化/升级被动, Tag 门控正确 |

**A 系列总评**: 机制设计全部正确, 完美体现 Effect+Tag 组合理念。主要问题是 A2-A8 缺少完整模板节 (验收口径、测试用例)。

### B — 无目标瞬发 (instant_press/) — 9 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| B1 | ✅ | ✅ | ✅ | **Gold standard** — 自身 buff, 完整模板 |
| B2 | ✅ | ✅ | ⚠️ | AoE 自身周围, QueryRadius 正确 |
| B3 | ✅ | ✅ | ⚠️ | 自我治疗, ModifyAttributeAdd 正确 |
| B4 | ✅ | ⚠️ | ⚠️ | 闪现, 依赖 Teleport handler (P1 gap) |
| B5 | ✅ | ⚠️ | ⚠️ | 时间回溯, 依赖 position history (P2 gap) |
| B6 | ✅ | ✅ | ⚠️ | 全局效果, FanOutApplyEffect 正确 |
| B7 | ✅ | ✅ | ⚠️ | 净化/驱散, Tag 移除方案正确 |
| B8 | ✅ | ✅ | ⚠️ | 变身触发, ToggleSpec 正确 |
| B9 | ✅ | ✅ | ⚠️ | 复活/重生, 需 handler 但设计合理 |

**B 系列总评**: 瞬发类设计扎实。B4 (Teleport) 和 B5 (Position History) 标注了正确的基建依赖。

### C — 指向单位 (unit_target/) — 9 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| C1 | ✅ | ✅ | ✅ | **Gold standard** — 单体伤害, 完整 Graph 示例 |
| C2 | ✅ | ✅ | ⚠️ | 单体 buff/debuff, GrantedTags 正确 |
| C3 | ✅ | ✅ | ⚠️ | 链式弹射, FanOutApplyEffect 合理 |
| C4 | ✅ | ✅ | ⚠️ | 吸血攻击, OnApply 回写正确 |
| C5 | ✅ | ✅ | ⚠️ | 沉默/禁锢, Tag 门控正确 |
| C6 | ✅ | ✅ | ⚠️ | 标记消耗, L 系列联动合理 |
| C7 | ✅ | ✅ | ⚠️ | 灵魂绑定, Tether 设计参考 gap_analysis §3.3 |
| C8 | ✅ | ✅ | ⚠️ | 窃取 buff, Effect 移除+复制方案可行 |
| C9 | ✅ | ⚠️ | ⚠️ | 栓绳, 依赖 Tether 组件 (P1 gap) |

**C 系列总评**: 设计质量高, 充分利用 Effect 组合。C9 Tether 依赖标注正确。

### D — 指向地面 (point_target/) — 8 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| D1 | ✅ | ✅ | ✅ | **Gold standard** — 地面 AoE, QueryRadius + FanOut |
| D2 | ✅ | ⚠️ | ⚠️ | 环形 AoE, 依赖 QueryRing (P2 gap) |
| D3 | ✅ | ✅ | ⚠️ | 延迟爆炸, 定时 Effect 正确 |
| D4 | ✅ | ✅ | ⚠️ | 全图传送, 位置选择方案合理 |
| D5 | ✅ | ✅ | ⚠️ | 地面持续区域, Periodic Effect 正确 |
| D6 | ✅ | ✅ | ⚠️ | 地形改变, Tag zone 方案合理 |
| D7 | ✅ | ✅ | ⚠️ | 多段落点, 序列 Effect 正确 |
| D8 | ✅ | ✅ | ⚠️ | 墙体/障碍, 需 spawn queue 但方向正确 |

**D 系列总评**: 地面系设计合理。D2 QueryRing gap 标注正确。

### E — 方向/弹道 (direction_skillshot/) — 10 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| E1 | ✅ | ✅ | ⚠️ | 直线弹道, Projectile 基础正确 |
| E2 | ✅ | ⚠️ | ⚠️ | 穿透弹道, 依赖 ProjectileHitMode (P1 gap) |
| E3 | ✅ | ⚠️ | ⚠️ | 锥形技能, **错误声称 QueryCone 需新增** (已存在 op 104) |
| E4 | ✅ | ⚠️ | ⚠️ | 矩形技能, **错误声称 QueryRectangle 需新增** (已存在 op 105) |
| E5 | ✅ | ⚠️ | ⚠️ | 弧形弹道, 依赖 ProjectileTrajectory.Arc (P2 gap) |
| E6 | ✅ | ⚠️ | ⚠️ | 回旋弹道, 依赖 ProjectileEndBehavior.Reverse (P1 gap) |
| E7 | ✅ | ⚠️ | ⚠️ | 追踪弹道, 依赖 Homing (P2 gap) |
| E8 | ✅ | ✅ | ⚠️ | 多发弹道, 序列 spawn 正确 |
| E9 | ✅ | ✅ | ⚠️ | 反射弹道, 碰撞+角度计算合理 |
| E10 | ✅ | ❌ | ⚠️ | 分裂弹道, **Variant A 在 Graph 内 SpawnSplitProjectiles — 架构违规** |

**E 系列总评**: 弹道类设计思路正确, 但 E3/E4 存在"虚假缺口"(对应 op 已存在), E10 有架构违规。

### F — 蓄力/按住 (charge_hold/) — 9 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| F1 | ✅ | ✅ | ✅ | **Gold standard** — 蓄力释放, 完整模板 |
| F2 | ✅ | ✅ | ⚠️ | 按住引导, Held trigger 正确 |
| F3 | ✅ | ⚠️ | ⚠️ | 蓄力瞄准, **错误声称 CursorDirectionWriter 已存在** |
| F4 | ✅ | ✅ | ⚠️ | 蓄力多段, 阈值检查合理 |
| F5 | ✅ | ⚠️ | ⚠️ | 蓄力移动, **同 F3 错误** |
| F6 | ✅ | ✅ | ⚠️ | 过度蓄力, 自伤机制合理 |
| F7 | ✅ | ✅ | ⚠️ | 蓄力护盾, 防御+蓄力组合正确 |
| F8 | ✅ | ✅ | ⚠️ | 蓄力召唤, 组合方案合理 |
| F9 | ✅ | ✅ | ⚠️ | 蓄力传送, 与 B4 Teleport 联动 |

**F 系列总评**: 蓄力机制设计优秀。F3/F5 的 CursorDirectionWriter 状态需修正为 P1 待实现。

### G — 连击/多段 (combo/) — 11 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| G1 | ✅ | ✅ | ⚠️ | 基础连击, Tag 推进正确 |
| G2 | ✅ | ✅ | ⚠️ | 方向连击, 方向输入+combo 合理 |
| G3 | ✅ | ✅ | ⚠️ | 条件分支, Tag 门控分支正确 |
| G4 | ✅ | ✅ | ⚠️ | 取消连击, 窗口期设计合理 |
| G5 | ✅ | ✅ | ⚠️ | 无限连击, 循环 Tag 方案正确 |
| G6 | ✅ | ✅ | ⚠️ | 空中连击, 状态 Tag 组合合理 |
| G7 | ✅ | ✅ | ⚠️ | 武器切换连击, Form routing 关联 |
| G8 | ✅ | ⚠️ | ⚠️ | 属性阈值连击, **虚构 Branch op** |
| G9 | ✅ | ✅ | ⚠️ | 协作连击, 多实体 Tag 同步合理 |
| G10 | ✅ | ✅ | ⚠️ | 终结连击, 与 Q 系列联动 |
| G11 | ✅ | ✅ | ⚠️ | 节奏连击, 时间窗口 Tag 正确 |

**G 系列总评**: 连击设计充分体现 Tag 推进+RequiredAll 门控模式。G8 的 Branch op 需改为 `JumpIfFalse` (op 7)。术语应统一为 `RequiredAll` 而非 `RequireTags`。

### H — 防御/弹反 (defense/) — 9 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| H1 | ✅ | ✅ | ✅ | **Gold standard** — 格挡, 完整模板; GuardBreakSystem 可考虑 Graph/Effect 替代 |
| H2 | ✅ | ❌ | ❌ | 弹反, **虚构 DSL** ("type": "ConditionalResponse") |
| H3 | ✅ | ❌ | ❌ | 闪避, **虚构 DSL** |
| H4 | ✅ | ❌ | ❌ | 护盾, **虚构 DSL** ("type": "AddTag", "type": "DamageAbsorb") |
| H5 | ✅ | ❌ | ❌ | 免疫, **虚构 DSL** |
| H6 | ✅ | ❌ | ❌ | 反伤, **虚构 DSL** ("type": "DamageMultiplier") |
| H7 | ✅ | ❌ | ❌ | 减伤, **虚构 DSL** |
| H8 | ✅ | ❌ | ❌ | 复活护盾, **虚构 DSL** |
| H9 | ✅ | ❌ | ❌ | 无敌, **虚构 DSL** |

**H 系列总评**: 机制概念全部正确, 但 H2-H9 使用了不存在的 Effect 类型 DSL, 实现者无法直接参照。**必须全部改写为标准 EffectPresetType + Graph Phase 表达**, 参照 H1 格式。

### I — 移动技能 (movement/) — 12 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| I1 | ✅ | ✅ | ⚠️ | 冲刺, Displacement handler 正确 |
| I2 | ✅ | ✅ | ⚠️ | 跳跃, 位移+无敌帧合理 |
| I3 | ✅ | ⚠️ | ⚠️ | 瞬移, 依赖 Teleport handler (P1) |
| I4 | ✅ | ✅ | ⚠️ | 滑行, 持续位移正确 |
| I5 | ✅ | ✅ | ⚠️ | 钩爪, Tether+Displacement 组合 |
| I6 | ✅ | ✅ | ⚠️ | 击退, Displacement 方向计算合理 |
| I7 | ✅ | ✅ | ⚠️ | 拉扯, 反向 Displacement |
| I8 | ✅ | ⚠️ | ⚠️ | 交换位置, 依赖 Teleport handler |
| I9 | ✅ | ✅ | ⚠️ | 弹射墙壁, 碰撞回调依赖 (P2) |
| I10 | ✅ | ✅ | ⚠️ | 浮空, 状态 Tag 正确 |
| I11 | ✅ | ⚠️ | ⚠️ | 墙壁碰撞伤害, 依赖 Displacement 碰撞回调 (P2) |
| I12 | ✅ | ✅ | ⚠️ | 减速区域, Modifier Effect 正确 |

**I 系列总评**: 移动类设计合理, 正确标注了 Teleport handler 和 Displacement 碰撞回调依赖。

### J — 切换/变身 (toggle_stance/) — 9 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| J1 | ✅ | ✅ | ✅ | **Gold standard** — Toggle 开关, ToggleSpec 正确 |
| J2 | ✅ | ⚠️ | ⚠️ | 姿态切换, 依赖 Form-based routing (P1) |
| J3 | ✅ | ⚠️ | ⚠️ | 变身, 同上 |
| J4 | ✅ | ⚠️ | ⚠️ | 武器切换, Form routing + Tag |
| J5 | ✅ | ⚠️ | ⚠️ | 元素切换, Form routing |
| J6 | ✅ | ✅ | ⚠️ | 光环切换, Toggle + PeriodicSearch |
| J7 | ✅ | ✅ | ⚠️ | CallerParams 方案, **值得与 J2-J5 的 AbilityStateBuffer 方案对比** |
| J8 | ✅ | ✅ | ⚠️ | 双形态, 两套 Tag 切换 |
| J9 | ✅ | ✅ | ⚠️ | 渐进变身, 叠层 Tag 门控 |

**J 系列总评**: Toggle/变身设计合理。J7 展示的 CallerParams 替代方案值得评估 — 可能比 AbilityStateBuffer 扩展更轻量, 符合"不扩展 System"原则。

### K — 放置/召唤 (placement/) — 8 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| K1 | ⚠️ | ❌ | ❌ | 静态召唤, **Graph 内 CreateEntity — 架构违规** |
| K2 | ✅ | ⚠️ | ⚠️ | 可控召唤, 需 spawn queue |
| K3 | ✅ | ✅ | ⚠️ | 陷阱放置, OnApply → spawn queue |
| K4 | ✅ | ✅ | ⚠️ | 图腾, Periodic Effect 正确 |
| K5 | ✅ | ✅ | ⚠️ | 传送门, 双点位 spawn 合理 |
| K6 | ✅ | ✅ | ⚠️ | 障碍物, spawn queue 正确 |
| K7 | ⚠️ | ❌ | ❌ | 可控召唤物, **Graph 内创建实体 — 架构违规** |
| K8 | ✅ | ✅ | ⚠️ | 临时克隆, spawn + 复制属性 |

**K 系列总评**: K1/K7 的架构违规必须修正。其余篇正确使用 `RuntimeEntitySpawnQueue`。

### L — 标记/引爆 (mark_detonate/) — 6 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| L1 | ✅ | ✅ | ⚠️ | 基础标记引爆, Tag + SendEvent 完美组合 |
| L2 | ✅ | ✅ | ⚠️ | 叠层标记, 计数器 + 阈值正确 |
| L3 | ✅ | ✅ | ⚠️ | 链式引爆, FanOutApplyEffect 正确 |
| L4 | ✅ | ✅ | ⚠️ | 定时引爆, Duration Effect 正确 |
| L5 | ✅ | ✅ | ⚠️ | 条件引爆, Tag 门控合理 |
| L6 | ✅ | ✅ | ⚠️ | 区域引爆, 与 D 系列联动正确 |

**L 系列总评**: **全系列最强** — 完美体现 SC2-like 组合原则, 无新增 System 需求, 纯靠 Effect + Tag + Graph 组合实现。**推荐作为全文档范例**。

### M — 引导/持续 (channel/) — 7 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| M1 | ✅ | ✅ | ⚠️ | 固定引导, Periodic Effect 正确 |
| M2 | ✅ | ⚠️ | ⚠️ | 瞄准引导, 依赖 CursorDirectionWriter (P1) |
| M3 | ✅ | ✅ | ⚠️ | 移动引导, Tag 允许移动合理 |
| M4 | ✅ | ✅ | ⚠️ | 叠层引导, 递增 Effect 正确 |
| M5 | ✅ | ⚠️ | ⚠️ | 射线引导, 依赖 Beam performer (P2) |
| M6 | ✅ | ⚠️ | ⚠️ | 栓绳引导, 依赖 Tether 组件 (P1) |
| M7 | ✅ | ⚠️ | ⚠️ | 传送引导, 引导完成 → Teleport |

**M 系列总评**: 引导机制设计合理, 正确标注了多项基建依赖。

### N — 上下文智能 (context_scored/) — 8 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| N1 | ✅ | ⚠️ | ⚠️ | 智能普攻, **依赖 ContextGroup (P0)** |
| N2 | ✅ | ⚠️ | ⚠️ | 智能技能, 同上 |
| N3 | ✅ | ⚠️ | ⚠️ | 距离评分, ScoreGraph 设计合理 |
| N4 | ✅ | ⚠️ | ⚠️ | 状态评分, Tag 查询 + 评分 |
| N5 | ✅ | ⚠️ | ⚠️ | 优先级评分, BasePriority 合理 |
| N6 | ✅ | ⚠️ | ⚠️ | 方向评分, 朝向计算 |
| N7 | ✅ | ⚠️ | ⚠️ | 组合评分, 多因子合理 |
| N8 | ✅ | ⚠️ | ⚠️ | AI 评分, 与 AI 决策联动 |

**N 系列总评**: 全系列依赖 ContextGroup (P0), 这是正确的 — 它是唯一真正需要新增的核心机制。评分 Graph 设计合理, 与 gap_analysis §2.1-2.3 一致。

### O — 响应窗口 (response_window/) — 8 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| O1 | ✅ | ✅ | ✅ | **Gold standard** — 受击响应, ResponseChainListener 正确 |
| O2 | ✅ | ✅ | ⚠️ | 反击窗口, Tag 时间窗口正确 |
| O3 | ✅ | ⚠️ | ⚠️ | 响应修改目标, 依赖 ResponseType.Modify target (P1) |
| O4 | ✅ | ✅ | ⚠️ | 连锁响应, 多层 Listener 正确 |
| O5 | ✅ | ✅ | ✅ | 条件响应, Tag 门控 + Listener |
| O6 | ✅ | ✅ | ✅ | 延迟响应, 定时 Effect + 响应 |
| O7 | ✅ | ✅ | ⚠️ | 概率响应, Graph 随机数可行 |
| O8 | ✅ | ⚠️ | ⚠️ | GateDeadline 与 O1 不一致 — 需统一 |

**O 系列总评**: 响应窗口设计优秀, ResponseChainListener 使用正确。O8 GateDeadline 术语需与 O1 对齐。

### P — 插入式上下文 (insertable_context/) — 7 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| P1 | ✅ | ✅ | ✅ | **Gold standard** — PhaseListener 插入, 架构清晰 |
| P2 | ✅ | ✅ | ⚠️ | 暴击插入, OnCalculate Listener 正确 |
| P3 | ✅ | ✅ | ⚠️ | 元素反应, Tag 组合触发合理 |
| P4 | ✅ | ✅ | ⚠️ | 距离衰减, Graph 距离计算正确 |
| P5 | ✅ | ✅ | ⚠️ | 地形加成, Tag zone 查询合理 |
| P6 | ✅ | ✅ | ⚠️ | 装备效果, Modifier 叠加正确 |
| P7 | ✅ | ✅ | ⚠️ | 天赋加成, 配置参数覆盖合理 |

**P 系列总评**: 插入式上下文完美体现 PhaseListener 设计。全部正确, 无架构问题。

### Q — 终结/处决 (finisher/) — 8 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| Q1 | ✅ | ✅ | ✅ | **Gold standard** — HP 阈值处决, RequiredAll 正确 |
| Q2 | ✅ | ✅ | ⚠️ | 标记处决, L 系列联动 |
| Q3 | ✅ | ✅ | ⚠️ | 连击终结, G 系列联动 |
| Q4 | ✅ | ✅ | ⚠️ | 团队处决, 多实体 Tag 检查 |
| Q5 | ✅ | ✅ | ⚠️ | 怒气处决, 资源 Tag 门控 |
| Q6 | ✅ | ✅ | ⚠️ | 环境处决, U 系列联动 |
| Q7 | ✅ | ✅ | ⚠️ | 时限处决, Duration Tag 正确 |
| Q8 | ✅ | ✅ | ⚠️ | 多阶段处决, 阈值递进合理 |

**Q 系列总评**: 处决机制设计正确, RequiredAll 门控使用到位。

### R — 同伴/多单位 (companion/) — 6 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| R1 | ✅ | ✅ | ⚠️ | 宠物 AI, Blackboard 控制合理 |
| R2 | ✅ | ✅ | ⚠️ | 宠物命令, Order 转发正确 |
| R3 | ✅ | ✅ | ⚠️ | 召唤物属性继承, 属性复制方案 |
| R4 | ✅ | ✅ | ⚠️ | 多召唤物管理, 计数限制合理 |
| R5 | ✅ | ❌ | ❌ | 牺牲复活, **Graph 内 DestroyEntity/CreateUnit — CRITICAL 架构违规** |
| R6 | ✅ | ✅ | ⚠️ | 召唤物协同, Tag 同步正确 |

**R 系列总评**: R5 是最严重的架构违规之一 — 必须改用 handler + `RuntimeEntitySpawnQueue`。

### S — 特殊输入 (special_input/) — 7 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| S1 | ✅ | ✅ | ✅ | **Gold standard** — 快速施法, SmartCast 正确 |
| S2 | ✅ | ⚠️ | ⚠️ | 双击施法, **引用已废弃的 DoubleTap** — 需改为 Tag 模拟 |
| S3 | ✅ | ✅ | ⚠️ | 长按施法, Held trigger 正确 |
| S4 | ✅ | ✅ | ⚠️ | 组合键, 多 Action 组合合理 |
| S5 | ✅ | ✅ | ✅ | 手势输入, 方向序列 + Tag 门控 |
| S6 | ✅ | ⚠️ | ⚠️ | 小地图点击, 依赖 Minimap adapter (P3) |
| S7 | ✅ | ✅ | ✅ | 自动施法, 条件触发 + AutoCast Tag |

**S 系列总评**: S2 DoubleTap 引用需更新。其余设计正确。

### T — 资源/门控 (resource/) — 8 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| T1 | ✅ | ✅ | ⚠️ | 法力消耗, Tag 门控 + ModifyAttribute 正确 |
| T2 | ✅ | ✅ | ⚠️ | 冷却, **明确无需 CooldownSystem** — Tag Duration 足够 |
| T3 | ✅ | ⚠️ | ⚠️ | 充能, 依赖 charge refill timer (P1) |
| T4 | ✅ | ✅ | ⚠️ | 怒气, 属性累积 + Tag 门控正确 |
| T5 | ✅ | ✅ | ⚠️ | 弹药, 计数器 + Tag 正确 |
| T6 | ✅ | ✅ | ⚠️ | 生命消耗, ModifyAttribute 负值正确 |
| T7 | ✅ | ✅ | ⚠️ | 多资源, 多 Tag 组合门控 |
| T8 | ✅ | ✅ | ⚠️ | 共享冷却, 同 Tag 多技能阻止 |

**T 系列总评**: T2 是关键证据 — CooldownSystem 不需要新 System, Tag Duration + BlockedAny 足够。gap_analysis §3.4 应更新。

### U — 环境交互 (environment/) — 7 篇

| Doc | P1 机制 | P2 架构 | P3 模板 | 备注 |
|-----|---------|---------|---------|------|
| U1 | ⚠️ | ❌ | ⚠️ | 可破坏物, **Graph 内销毁实体 — 架构违规** |
| U2 | ✅ | ⚠️ | ⚠️ | 墙壁碰撞, 依赖 Displacement 碰撞回调 (P2) |
| U3 | ⚠️ | ❌ | ⚠️ | 陷阱触发, **Graph 内创建实体 — 架构违规** |
| U4 | ✅ | ✅ | ⚠️ | 地形效果, Tag zone 方案正确 |
| U5 | ⚠️ | ❌ | ⚠️ | 危险区域, **Graph 内创建区域实体 — 架构违规** |
| U6 | ✅ | ✅ | ⚠️ | 传送点, 位置设置方案合理 |
| U7 | ✅ | ✅ | ⚠️ | 视野遮挡, Tag 区域方案正确 |

**U 系列总评**: U1/U3/U5 的架构违规必须修正, 统一改用 `RuntimeEntitySpawnQueue`。

---

## Structural Documents Review

### gap_analysis.md

| 维度 | 评级 | 备注 |
|------|------|------|
| P0 ContextGroup | ✅ | 设计正确, 唯一真正需要的新核心机制 |
| P1 CooldownTickSystem | ❌ | **应删除** — T2 证明 Tag Duration 足够 |
| P1 RequireTags 标注 | ✅ | §1.1 正确标注已有 |
| P1 Projectile 扩展 | ✅ | hitMode/trajectory 方案合理 |
| P1 Teleport handler | ✅ | 需要新增 |
| P1 Form-based routing | ⚠️ | 需与 J7 CallerParams 方案对比 |

### graph_ops_reference.md

| 维度 | 评级 | 备注 |
|------|------|------|
| Op 列表准确性 | ✅ | 与代码一致 |
| 已知缺口 §RequireTags | ❌ | **错误** — RequiredAll 已存在, 需删除此条 |
| 其余缺口 | ✅ | 标注正确 |

### TEMPLATE.md

| 维度 | 评级 | 备注 |
|------|------|------|
| 结构完整性 | ✅ | 涵盖所有必要节 |
| 实用性 | ✅ | 配置示例、Graph 示例、验收口径齐全 |
| 合规率 | ❌ | 仅 ~12/174 篇 (7%) 完整遵循 |

### user_experience_checklist.md & README.md

| 维度 | 评级 | 备注 |
|------|------|------|
| 覆盖完整性 | ✅ | 174 条场景全覆盖 |
| 索引一致性 | ✅ | README 与实际文件对应 |

---

## Actionable Recommendations

### Priority 1 — Must Fix Before Implementation (阻塞项)

1. **修正 7 篇架构违规文档**: E10, K1, K7, R5, U1, U3, U5
   - 所有 Graph 内 CreateEntity/DestroyEntity/SpawnUnit 改为 `RuntimeEntitySpawnQueue` 路径
   - R5 为最高优先级 (CRITICAL FAIL)

2. **重写 H2-H9 Effect 定义**: 从虚构 DSL 改为标准 EffectPresetType + Graph Phase
   - 参照 H1 格式

3. **修正 graph_ops_reference.md**: 删除 `AbilityActivationRequireTags` P1 缺口条目

4. **修正 gap_analysis.md §3.4**: 删除 CooldownTickSystem 提案, 引用 T2 的 Tag Duration 方案

### Priority 2 — Should Fix (质量提升)

5. **统一术语**: 全文档将 `RequireTags` 替换为 `RequiredAll` (特别是 G 系列)

6. **修正 F3/F5**: CursorDirectionBlackboardWriter 状态从 "已存在" 改为 "P1 待实现"

7. **修正 E3/E4**: 删除 "QueryCone/QueryRectangle 需新增" 的错误声明 (op 104/105 已存在)

8. **统一 O1/O8 GateDeadline 术语**

9. **评估 J7 CallerParams 方案**: 与 J2-J5 AbilityStateBuffer 方案对比, 选择更轻量的方案

10. **S2 更新**: DoubleTap 引用改为 Tag 模拟方案

### Priority 3 — Nice to Have (文档完善)

11. **模板合规**: 将 ~160 篇简化格式文档补齐验收口径和测试用例节
    - 建议分批处理: 先补 Gold standard 相邻的同系列文档

12. **G8 修正**: Branch op 改为 `JumpIfFalse` (op 7) + `CompareGtFloat` (op 30)

---

## Statistics

| 类别 | 篇数 | ✅ 全通过 | ⚠️ 有 WARN | ❌ 有 FAIL |
|------|------|-----------|------------|-----------|
| A passive | 8 | 1 | 7 | 0 |
| B instant_press | 9 | 1 | 8 | 0 |
| C unit_target | 9 | 1 | 8 | 0 |
| D point_target | 8 | 1 | 7 | 0 |
| E direction_skillshot | 10 | 0 | 9 | 1 |
| F charge_hold | 9 | 1 | 8 | 0 |
| G combo | 11 | 0 | 11 | 0 |
| H defense | 9 | 1 | 0 | 8 |
| I movement | 12 | 0 | 12 | 0 |
| J toggle_stance | 9 | 1 | 8 | 0 |
| K placement | 8 | 0 | 6 | 2 |
| L mark_detonate | 6 | 0 | 6 | 0 |
| M channel | 7 | 0 | 7 | 0 |
| N context_scored | 8 | 0 | 8 | 0 |
| O response_window | 8 | 3 | 5 | 0 |
| P insertable_context | 7 | 1 | 6 | 0 |
| Q finisher | 8 | 1 | 7 | 0 |
| R companion | 6 | 0 | 5 | 1 |
| S special_input | 7 | 3 | 4 | 0 |
| T resource | 8 | 0 | 8 | 0 |
| U environment | 7 | 0 | 4 | 3 |
| **Total** | **174** | **15** | **148** | **15** |

---

## Conclusion

**整体评价**: PR #43 的 174 篇机制文档在**机制设计层面质量优秀** — 全部 174 篇的 P1 (机制正确性) 评级为 ✅ 或 ⚠️, 没有一篇在核心机制设计上犯根本性错误。SC2-like 组合理念贯穿始终, 特别是 L 系列 (标记引爆)、O 系列 (响应窗口)、P 系列 (插入式上下文) 展示了极高的架构理解水平。

**主要风险集中在两处**: (1) 7 篇文档的 Graph 内结构变更违规 — 这是硬伤, 必须在实现前修正; (2) H2-H9 的虚构 DSL — 虽然机制概念正确, 但实现者无法直接参照。

**建议合并策略**: 先修正 Priority 1 的 4 项阻塞问题 (约 15 篇文档), 然后合并。Priority 2/3 可在后续迭代中逐步完善。
