---
文档类型: 测试用例
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: Claude
文档版本: v1.0
适用范围: GAS - TCG品类扩展验证 (TcgDemoMod 扩展)
状态: 已实现
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/04_Effect类型体系_架构设计.md
---

# TCG 品类扩展验证测试用例

## 1 测试总览

在 GPT 原有 Hook/Modify 两场景基础上新增 5 个场景，补充 Chain 响应、Effect Stack、GrantedTags、Manual 时钟。

## 2 场景列表

### T3: Chain 追加连锁

**测试目标**: Response Chain `Chain` 类型在 Spell 结算时追加 CounterBlast 效果

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 加载 tcg_chain 地图 | ChainListener 创建, 监听 Effect.Tcg.Spell, Chain 类型 |
| 2 | TcgHero 施放 Fireball (2101) | Fireball 进入 Response Chain 窗口 |
| 3 | ChainListener 响应 | 追加 CounterBlast (Effect.Tcg.CounterBlast, -15 HP) 到连锁 |
| 4 | 结算完成 | TcgEnemy HP = 100 - 30(Fireball) - 15(CounterBlast) = 55 |

### T4: 毒计数器堆叠 (Effect Stack - AddDuration)

**测试目标**: PoisonCounter DoT 效果叠加，StackPolicy=AddDuration, Limit=5

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 施放 PoisonCounter (2102) 第 1 次 | Stack=1, Duration=120 ticks |
| 2 | 施放第 2 次 | Stack=2, Duration += 120 (AddDuration) |
| 3 | 施放第 5 次 | Stack=5, limit 达到 |
| 4 | 施放第 6 次 | 被 RejectNew 拒绝, Stack 保持 5 |

### T5: 永续魔法 GrantedTags (Magic Barrier)

**测试目标**: Infinite 生命周期效果授予 Immune.Spell 标签

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | TcgHero 施放 MagicBarrier (2103) | Buff(Infinite) 创建, GrantedTags=[Immune.Spell] |
| 2 | 检查 TcgHero 标签 | Immune.Spell 标签存在 |
| 3 | 效果不会自然过期 | 因为 lifetime=Infinite |

### T6: 力量增幅 (Buff + Modifier + GrantedTags)

**测试目标**: PowerBoost 同时具有属性修改和标签授予

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 施放 PowerBoost (2104) | Attack +20, 授予 Status.Empowered |
| 2 | 验证 Attack 属性 | Attack = 0 + 20 = 20 |
| 3 | 验证标签 | Status.Empowered 存在 |

### T7: Manual 时钟模式 (回合制)

**测试目标**: GasClock Manual 模式不自动推进，需要 RequestStep

| 步骤 | 操作 | 预期结果 |
|------|------|----------|
| 1 | 加载 TCG mod (clock.json mode=Manual) | GasStepMode = Manual |
| 2 | 固定帧推进 10 帧 | Step 不推进 (StepNow=0) |
| 3 | RequestStep(1) | Step 推进 1 步 |
| 4 | Duration 效果在 step 内 tick | PoisonCounter 按 Manual step 节奏结算 |

## 3 GAS 能力覆盖矩阵 (扩展部分)

| GAS 能力 | T3 | T4 | T5 | T6 | T7 |
|----------|----|----|----|----|-----|
| Response Chain (Chain) | X | | | | |
| Effect Stack (AddDuration) | | X | | | |
| GrantedTags (Immune.Spell) | | | X | | |
| Buff + Modifier + GrantedTags | | | | X | |
| GasClock Manual | | | | | X |
