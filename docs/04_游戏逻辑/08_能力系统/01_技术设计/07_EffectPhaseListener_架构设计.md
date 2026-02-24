---
文档类型: 架构设计
创建日期: 2026-02-08
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 游戏逻辑 - 能力系统(GAS) - EffectPhaseListener 反应式监听
状态: 审阅中
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/06_Effect生命周期Phase_架构设计.md
---

# EffectPhaseListener 架构设计

本文描述"别的 effect 到达某 phase 时，我做什么"——即反应式监听机制。

effect 自身的生命周期管线（"我到了什么阶段做什么"）见 `06_Effect生命周期Phase_架构设计.md`。

# 1 概念定义

| | Phase Graph（自身行为） | Phase Listener（反应式监听） |
|---|---|---|
| 回答的问题 | "这个 effect **自己**在某 phase 做什么" | "**别的** effect 到达某 phase 时，**我**做什么" |
| 存储 | `EffectPhaseGraphBindings`（template 上） | `EffectPhaseListenerBuffer`（entity 上） |
| 触发 | effect 自身的生命周期 | 其他 effect 的 phase 事件 |
| 执行时机 | Pre → Main → Post 管线内部 | Pre/Main/Post 之后，Step 4 dispatch |
| 典型用例 | 伤害公式计算、DOT tick 逻辑 | 护盾吸收、伤害反射、连锁反应 |

# 2 执行流程

```text
 ─── 某个 effect X 到达 Phase (如 OnApply) ───

  Step 1-3: X 的 Pre → Main → Post 正常执行
                    │
                    ▼
  Step 4: EffectPhaseExecutor 查找所有匹配的 Listener
                    │
                    ├─── match by Tag  (listenTag == X.Tag)
                    ├─── match by Id   (listenEffectId == X.TemplateId)
                    └─── match by scope (target / source)
                    │
                    ▼
  Step 5: 按 priority 排序，逐个执行 Listener 的 action
                    │
                    ├─── action=graph  → 执行 Graph 程序
                    ├─── action=event  → 发射 GameplayEvent
                    └─── action=both   → 先 graph 后 event
```

# 3 注册与注销

| 时机 | 行为 |
|---|---|
| effect entity 创建（OnApply 后） | 若 template 有 `phaseListeners` 配置，注册到 `EffectPhaseListenerBuffer` |
| effect entity 过期（OnExpire） | 注销所有已注册的 Listener |
| effect entity 移除（OnRemove） | 注销所有已注册的 Listener |

**scope 决定注册位置**：

| scope | 注册到 | 含义 |
|---|---|---|
| `target` | effect 的 Target entity | "当我的目标被别的 effect 命中时" |
| `source` | effect 的 Source entity | "当我的施法者被别的 effect 命中时" |

# 4 匹配规则

Listener 匹配发生在 Step 4，通过以下条件：

| 条件 | 说明 |
|---|---|
| `listenTag` | 触发 effect 的 Tag 必须匹配。空字符串 = 通配（匹配所有 effect）。 |
| `listenEffectId` | 触发 effect 的 template ID 必须匹配。空字符串 = 通配。 |
| `phase` | 触发 effect 到达的 Phase 必须匹配。 |
| `scope` | 在正确的 entity 上查找 Listener（target 或 source）。 |

`listenTag` 和 `listenEffectId` 同时指定时为 AND 关系。

# 5 Action 类型

| action | 说明 |
|---|---|
| `graph` | 执行指定的 Graph 程序。Graph 的 ConfigContext 为 **Listener 所属 effect** 的 merged ConfigParams（不是触发 effect 的）。寄存器 `E[0]`/`E[1]`/`E[2]` 映射为 Listener 所属 effect 的 Source/Target/TargetContext。 |
| `event` | 发射一个 GameplayEvent（`eventTag`），可被 Performer 表现层消费。 |
| `both` | 先执行 graph，再发射 event。 |

**Graph 上下文重要说明**：Listener Graph 运行时，Blackboard 是 **Listener 所属 effect entity** 的 Blackboard。如果需要读取**触发 effect** 的信息，通过 `TriggerContext` 寄存器传入（设计待定）。

# 6 JSON 配置

```json
"phaseListeners": [
  {
    "listenTag": "Effect.Damage",
    "listenEffectId": "",
    "phase": "onApply",
    "scope": "target",
    "action": "graph",
    "graphProgram": "Graph.Shield.Absorb",
    "eventTag": "",
    "priority": 0
  }
]
```

字段表详见 `docs/04_游戏逻辑/08_能力系统/03_配置结构/02_EffectTemplate配置_配置结构.md` 第 6 节。

# 7 典型用例

## 7.1 护盾吸收

```text
A 给 B 施加护盾 buff (Infinite effect)
  → effect entity 上注册 Listener:
      listenTag="Effect.Damage", phase=onApply, scope=target

C 攻击 B → Damage effect 到达 OnApply
  → Step 1-3: Damage 的 Pre/Main/Post 正常执行
  → Step 4: 查找 B 身上的 Listener
  → 命中护盾的 Listener (tag 匹配 "Effect.Damage")
  → 执行 Graph.Shield.Absorb:
      读 Blackboard "shieldRemaining"
      读 incoming damage
      计算吸收量
      写回 Blackboard "shieldRemaining"
      若护盾耗尽 → 触发移除自身
```

## 7.2 伤害反射

```text
B 身上有反射 buff (After effect)
  → Listener: listenTag="Effect.Damage", phase=onApply, scope=target, action=graph

C 攻击 B → Damage OnApply
  → Listener 触发 Graph.Reflect:
      caster = E[0] (B, 反射 buff 的 Source)
      target = E[1] (B, 反射 buff 的 Target)
      通过 ApplyEffectTemplate(B, C, ReflectDamageTemplate) 发起反弹
      → B 成为新 effect 的 source, C 成为新 effect 的 target
```

## 7.3 连锁闪电

```text
连锁 buff 在施法者身上
  → Listener: listenTag="Effect.Damage", phase=onApply, scope=source, action=graph

A 对 B 施放伤害 → Damage OnApply
  → A 身上的 Listener 触发 Graph.Chain.Lightning:
      读 Blackboard "chainsRemaining"
      若 > 0: 查找 B 附近最近的新目标 C
      ApplyEffectTemplate(A, C, ChainLightningDamage)
      chainsRemaining -= 1
      写回 Blackboard
```

# 8 与 Phase Graph 的协作

Phase Graph（自身行为）和 Phase Listener（反应式监听）可以同时存在于一个 effect template 上。它们互不干扰：

```text
Effect.Shield.Basic:
  ├── phaseGraphs:
  │     onApply.post = "Graph.Shield.Init"        ← 自身 OnApply 时初始化护盾值
  │     onExpire.post = "Graph.Shield.Cleanup"     ← 自身过期时清理
  │
  └── phaseListeners:
        listenTag="Effect.Damage", phase=onApply   ← 监听别人的 Damage OnApply
        action=graph, graphProgram="Graph.Shield.Absorb"
```

**执行顺序保证**：触发 effect 的 Pre/Main/Post 全部执行完毕后，才分发给 Listener。Listener 永远不会打断触发 effect 的管线。
