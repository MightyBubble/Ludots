---
文档类型: 测试用例
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: Claude
文档版本: v1.0
适用范围: GAS - RTS品类场景验证 (RtsDemoMod)
状态: 已实现
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/04_Effect类型体系_架构设计.md
---

# RTS 品类场景验证测试用例

## 1 测试总览

| 维度 | 数量 |
|------|------|
| 场景数 | 4 |
| 测试文件 | `src/Mods/RtsDemoMod/` (7 abilities, 11 effects) |
| 覆盖能力 | Ability Cost, PeriodicSearch, Effect Stack, Search AOE, CreateUnit, 3-faction Teams, GrantedTags |

## 2 场景列表

### R1: 单位生产与资源消耗

**测试目标**: 验证 Ability Cost (多 EffectSignal 实现) + CreateUnit

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | SCV 激活 BuildBarracks (5001) | 触发两个 EffectSignal: BuildCost.Barracks(-150 Minerals) + BuildBarracks(CreateUnit) |
| 2 | 检查 SCV 的 Minerals 属性 | Minerals = 500 - 150 = 350 |
| 3 | 验证 Barracks 实体生成 | 新 Unit.Barracks 实体存在于 World |
| 4 | Barracks 激活 TrainMarine (5002) | 触发 TrainCost.Marine(-50 Minerals) + TrainMarine(CreateUnit) |

**前置条件**: SCV 实体, Minerals=500, AbilityStateBuffer=[5001]

### R2: 科学球护盾光环 (PeriodicSearch + Stack)

**测试目标**: 验证 PeriodicSearch 持续搜索友方 + Buff 堆叠

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | Science Vessel 激活 ShieldAura (5006) | 创建 PeriodicSearch 效果实体, lifetime=After, period=30 ticks |
| 2 | 等待 30 ticks (1 个周期) | 对范围内友方 Marine 施加 ShieldBuff (+10 Shield) |
| 3 | 等待第 2 个周期 | ShieldBuff Stack=2, duration 刷新 (RefreshDuration), Shield=20 |
| 4 | 等待第 3 个周期 | ShieldBuff Stack=3 (达到 limit), Shield=30 |
| 5 | 等待第 4 个周期 | Stack=3 不变 (RejectNew overflow), Shield=30 |

**前置条件**: Science Vessel(Team 1) + Marine(Team 1) 在 800cm 范围内

### R3: 攻城坦克 AOE 搜索 (Search + 3 阵营)

**测试目标**: 验证 Search 预设 + 敌友过滤 + 多阵营关系

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 设置 3 阵营: Terran(1)↔Zerg(2) Hostile, Terran↔Protoss(3) Hostile, Zerg↔Protoss Hostile | TeamManager 正确设置所有关系 |
| 2 | Siege Tank(Team 1) 激活 SiegeAoe (5005) | Search(Circle, r=600, Hostile) 搜索范围内敌方 |
| 3 | 验证搜索结果 | 只命中 Zerg 和 Protoss 单位, 不命中 Terran 友方 |
| 4 | 检查命中单位 Health | 被 AoeDamage(-20 Health) 命中 |

**前置条件**: Terran/Zerg/Protoss 各有单位在 600cm AOE 范围内

### R4: 兴奋剂 (RequiredAll 激活条件 + GrantedTags)

**测试目标**: 验证 Ability 激活前置标签条件 + Effect GrantedTags

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | Marine 尝试激活 StimPack (5004) | 失败: 缺少 RequiredAll 标签 Tech.Stim |
| 2 | Barracks 完成 ResearchStim (5003) 120 ticks | ResearchComplete 效果 GrantedTags 授予 Tech.Stim 标签 |
| 3 | Marine 再次激活 StimPack | 成功: Tech.Stim 满足 RequiredAll |
| 4 | 检查 StimBuff 效果 | AttackSpeed+50, Health-10, Duration=180 ticks |

**前置条件**: Barracks + Marine 实体, Barracks 有 ability 5003

## 3 GAS 能力覆盖矩阵

| GAS 能力 | R1 | R2 | R3 | R4 |
|----------|----|----|----|----|
| Ability Cost (multi-signal) | X | | | |
| CreateUnit | X | | | |
| PeriodicSearch | | X | | |
| Effect Stack (RefreshDuration) | | X | | |
| Search (Circle) | | | X | |
| TeamManager (3 faction) | | | X | |
| RelationFilter (Hostile) | | | X | |
| RequiredAll 激活条件 | | | | X |
| GrantedTags | | | | X |
| TagClip | | | | X |
| DoT (Irradiate) | | | X | |
