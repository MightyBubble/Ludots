---
文档类型: 架构设计
创建日期: 2026-02-08
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 游戏逻辑 - 能力系统(GAS) - Effect 动态上下文传递与参数通道
状态: 审阅中
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/05_Effect参数架构_架构设计.md
---

# Effect 动态上下文 架构设计

# 1 EffectContext 三元组

每个 effect entity 携带一个 `EffectContext`：

```csharp
public struct EffectContext
{
    public int RootId;          // 同一连锁中所有 effect 共享的根 ID（预算追踪）
    public Entity Source;       // 施法者 / 来源
    public Entity Target;       // 目标
    public Entity TargetContext; // 辅助上下文（AOE 中心、重定向中间目标等）
}
```

Graph 程序中的固定寄存器映射：`E[0] = Source (Caster)`, `E[1] = Target`, `E[2] = TargetContext`。

# 2 TargetResolver 上下文映射

当 effect 具有 TargetResolver 时，扇出的每个新 EffectRequest 需要决定 Source/Target/TargetContext 的来源。通过 `TargetResolverContextMapping` 配置：

```csharp
public enum ContextSlot : byte
{
    OriginalSource = 0,        // 原始施法者
    OriginalTarget = 1,        // 原始目标
    OriginalTargetContext = 2,  // 原始辅助上下文
    ResolvedEntity = 3,        // 解析出的新实体
}
```

**预设模式**：

| 模式 | PayloadSource | PayloadTarget | PayloadTargetContext | 场景 |
|---|---|---|---|---|
| AOE（默认） | OriginalSource | ResolvedEntity | OriginalTarget | AOE 伤害，每个目标独立结算 |
| Reflect | OriginalTarget | OriginalSource | OriginalTarget | 伤害反弹，Source/Target 互换 |
| Redirect | OriginalSource | OriginalTargetContext | OriginalTarget | 重定向到辅助上下文指定的目标 |

# 3 连锁效果的上下文规则

**核心规则：Source/Target 不自动链式传递。**

上一个 effect 的 Target 不会自动成为下一个 effect 的 Source。上下文传递必须**显式配置**：

1. **TargetResolver 扇出**：通过 `ContextMapping` 显式映射（上文第 2 节）。
2. **Graph 中触发子效果**：通过 `ApplyEffectTemplate(caster, target, templateId, args)` 显式传递 entity 引用。Graph 程序可以自由选择将哪个寄存器中的 entity 作为新效果的 caster/target。
3. **RootId 共享**：同一连锁中的所有 effect 共享 RootId，用于预算追踪（`MAX_CREATES_PER_ROOT`）。

# 4 EffectRequest 的参数通道

CallerParams 是 `EffectRequest` 的**唯一命名参数通道**。所有需要从调用方传递到 effect 的参数，统一通过 CallerParams key-value 传递。

**废除 F0-F3/I0-I1**：原 `EffectRequest` 中的 6 个无名无类型载荷槽位 (`F0-F3`, `I0-I1`) 将被移除。它们的语义由 PresetType 隐式定义，无类型安全，无法被 CallerParams merge 机制覆盖。

**迁移**：
- `ApplyForce2D` 的 `F0=ForceX, F1=ForceY` → 改为由调用方填 `CallerParams` 的 `_ep.forceXAttribute` / `_ep.forceYAttribute`，C# 代码从 merged ConfigParams 读取。
- 其他使用 F0-F3 的自定义逻辑 → 改为使用 CallerParams 命名键。

# 5 应用场景用例

## 5.1 AOE 伤害

```text
玩家释放暴风雪 → AbilityTimeline 的 EffectClip 触发 →
  EffectRequest { templateId=FrostDamage, CallerParams: { _ep.queryRadius: 800, tickDamage: 15 } } →
    EffectApplicationSystem 创建 effect entity →
      OnResolve: TargetQuery (Circle, radius=800 from merged ConfigParams) 找到 5 个目标 →
      OnHit: 逐目标验证 (免疫检测) →
      每个目标: 扇出 EffectRequest → payloadEffect 独立结算
```

## 5.2 护盾吸收

```text
A 给 B 施加护盾 buff (Infinite effect) →
  effect entity 上注册 PhaseListener:
    listenPhase=OnApply, scope=Target, action=ExecuteGraph(Graph.Shield.Absorb) →
  当 C 攻击 B (Damage effect OnApply) →
    Listener 被触发 → Graph.Shield.Absorb 读 BB 的 shieldRemaining，
    修改 incoming damage → 写回 BB shieldRemaining
```

## 5.3 DOT 堆叠

```text
技能 A 施加 Burn DOT → CallerParams { tickDamage: 10, _ep.durationTicks: 150 }
技能 B 也使用同一个 Burn 模板 → CallerParams { tickDamage: 20, _ep.durationTicks: 300 }

两个 effect 实例使用同一 template，但 CallerParams 不同：
  实例 1: OnPeriod Graph 读 LoadConfigFloat("tickDamage") → 10（来自 CallerParams merge）
  实例 2: OnPeriod Graph 读 LoadConfigFloat("tickDamage") → 20（来自 CallerParams merge）
```

## 5.4 反弹

```text
A 攻击 B → B 身上有反弹 buff (Listener) →
  Listener 触发 Graph.Reflect → Graph 中:
    caster = E[1] (原始 target = B)
    target = E[0] (原始 source = A)
    ApplyEffectTemplate(B, A, ReflectDamageTemplate)
  → B 变成新效果的 source，A 变成新效果的 target
```
