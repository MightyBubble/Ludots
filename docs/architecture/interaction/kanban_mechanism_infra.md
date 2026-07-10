# Mechanism Infrastructure Kanban

> 从 174 篇 Feature 机制文档（A–U 系列）中提取的全部基建开发需求。
> 按架构层 → 模块分组，去重合并后按优先级排列。
>
> **状态定义**: 🔴 TODO · 🟡 WIP · 🟢 DONE
>
> **优先级**: P0 = 多机制共用阻塞项 · P1 = 单机制核心阻塞 · P2 = 功能增强 · P3 = 锦上添花
>
> **来源文件**: 括号内为依赖该项的机制文档 ID

---

## Layer 0 — Graph VM (GraphOps 扩展)

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| G01 | ReadPosition / WritePosition | P1 | 🔴 | I3, I8 | 读取/写入 Entity 世界坐标；瞬移 + 位置交换依赖 |
| G02 | StopEffect | P2 | 🔴 | I11 | 运行时终止指定 Effect（冲刺碰撞停止） |
| G03 | Batch RemoveTagsByPrefix | P2 | 🔴 | B7 | 批量移除匹配前缀的 Tag/Effect（净化技能） |
| G04 | Dynamic AbilitySlot Write | P2 | 🔴 | C7, J8 | 运行时修改 AbilityStateBuffer 的 slot→ability 绑定 |

---

## Layer 1 — GAS Core (Effect / Ability / Attribute)

### 1A — Attribute 注册

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| A01 | GuardStamina (current/max) | P1 | 🔴 | H1, H9 | 格挡耐久值；需配置 Regen |
| A02 | Posture (current/max/decayPerTick) | P1 | 🔴 | H6, H8 | 架势值；自然衰减 + 攻击动画中暂停衰减 |
| A03 | Energy (current/max/decayPerTick/decayDelay) | P1 | 🔴 | H9 | 吸收转化能量池 |
| A04 | combo_meter | P1 | 🔴 | G8, T7 | 连击计数器；命中+1、受伤归零 |
| A05 | Charges (current/max) | P1 | 🔴 | T3 | 技能充能层数 |
| A06 | Rage (current/max) | P1 | 🔴 | T4 | 怒气资源 |
| A07 | Ammo (current/max) | P1 | 🔴 | T6 | 弹药计数 |
| A08 | UltimateMeter | P1 | 🔴 | Q6 | 大招能量条 |
| A09 | gravity_scale | P3 | 🔴 | I9, I10 | 临时重力缩放（攀爬/滑翔） |

### 1B — AbilitySpec 扩展

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| AB01 | Ability-level Attribute Precondition | **P0** | 🔴 | T1, T3, T4, T5, G8, Q1, Q6, Q7 | 激活检查阶段对 Attribute 做门控（Mana≥Cost / Charges>0 / Rage≥Cost / HP>Cost+1），失败返回 `ActivationFailed.InsufficientResource`。**8+ 机制共用，最高优先级** |
| AB02 | Attribute Clamp on Modify (min guard) | P1 | 🔴 | T5 | `ModifyAttributeAdd` 支持 min clamp（如 HP 不低于 1，防自杀） |
| AB03 | InputGate with Deadline | P1 | 🔴 | Q8 | 等待特定按键输入 + 超时；QTE 系统核心 |
| AB04 | Branch on InputGate Result | P1 | 🔴 | Q8 | 根据 QTE 成功/失败执行不同 Effect 链 |
| AB05 | Form-based Ability Mapping | P1 | 🔴 | J2, J3, J4 | AbilityStateBuffer 支持 slot + formTag → 实际 ability 路由 |

### 1C — Effect / ResponseChain 扩展

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| EC01 | EffectContext.Target Mutability (OnPropose) | P1 | 🔴 | O3 | Proposal 阶段支持 Listener 修改 Target（重定向） |
| EC02 | GateDeadline for ResponseChainListener | P1 | 🔴 | O8 | ResponseChain 窗口超时自动 Pass |
| EC03 | ResponseChain Depth Limit | P2 | 🔴 | O2 | 防止递归 Chain 无限循环（建议 max=10） |
| EC04 | ResponseChainListener.PauseSimulation | P2 | 🔴 | O4 | 响应链窗口期暂停模拟（暂停选目标） |
| EC05 | TargetCollectionGate in ResponseChain | P2 | 🔴 | O3, O4 | ResponseChain 内支持显式目标集合确认（当前仅 AbilityExec 支持） |
| EC06 | Wildcard Tag Precondition (`tag:*`) | P1 | 🔴 | H3, H6 | 匹配任意后缀的 Tag（`finisher_opportunity:*`） |

---

## Layer 2 — BuiltinHandler (新增)

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| BH01 | RedirectProjectile | P1 | 🔴 | H7 | 修改弹道 Owner + 反转速度方向；注册到 BuiltinHandlerRegistry |
| BH02 | PickupAndThrow | P1 | 🔴 | U1 | 拾取可投掷物 → AttachToActor → LaunchProjectile → RuntimeEntitySpawnQueue 销毁 |
| BH03 | SpawnSplitProjectiles | P2 | 🔴 | E10 | 命中后 RuntimeEntitySpawnQueue 生成多个扩散弹道 |
| BH04 | MergeUnits | P2 | 🔴 | R5 | 销毁多个源单位 + 创建合体单位 |
| BH05 | DetonateProjectile | P2 | 🔴 | K7 | 引爆可控投射物 + RuntimeEntitySpawnQueue 销毁 |
| BH06 | CancelAbility (on target) | P1 | 🔴 | H3 | 中断目标正在执行的技能动画 |
| BH07 | Teleport (instant position set) | P1 | 🔴 | B4, I3 | 非位移型瞬间坐标设置；需确认 ApplyForce 是否已支持 instant 模式 |
| BH08 | OnDeath Handler (loot + destroy) | P2 | 🔴 | U3 | 可破坏物死亡 → spawn loot + destroy entity |

---

## Layer 3 — System (新增 / 扩展)

### 3A — 战斗系统

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| SY01 | GuardBreakSystem | P1 | 🔴 | H1 | 订阅 GuardStamina≤0 → 强制 RemoveEffect(BlockingBuff) + 施加 Status.GuardBroken（60 tick） |
| SY02 | PostureDecaySystem | P1 | 🔴 | H6, H8 | Posture 自然衰减；攻击动画中暂停衰减 |
| SY03 | ResponseChainTimeoutSystem | P1 | 🔴 | O8 | ResponseChain 窗口超时检查 + 自动 Pass |
| SY04 | OnReceiveDamage → combo_meter Reset | P1 | 🔴 | T7 | 受伤时触发 combo_meter 归零 |
| SY05 | Attribute-to-Tag Bridge Watcher | P2 | 🔴 | G8, H6 | 通用 Periodic Effect 检查属性阈值 → 授予/移除 Tag（已有模式，需标准化） |

### 3B — Input / 交互系统

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| SY10 | CursorDirectionBlackboardWriter | P1 | 🔴 | F1, F3, F5, G11 | 每帧将鼠标/摇杆方向写入施法者 Blackboard（`CursorDirectionRad`） |
| SY11 | ContextGroup Scoring Mechanism | P1 | 🔴 | S3, G3 | ContextGroup 注册 → Scorer 调用 → 候选排序 → 最高分 Ability 路由 |
| SY12 | DoubleTap Detection (command-source input) | P2 | 🔴 | S2 | InputTriggerType.DoubleTap 已废弃；需在 command-source/input profile 层追踪按压间隔判断 |
| SY13 | ModifierBehavior.forceTargetSelf | P2 | 🔴 | S4 | Alt 键状态检测 → Order.Target 强制设为 caster |
| SY14 | Input Focus / Actor Routing | P2 | 🟡 | K7, R1, R2, R4, R6 | `actorOrderRouting` 已实现本地命令 actor 路由；Camera Focus / 非本地玩家路由仍待补 |
| SY15 | Input Profile Remapping | P2 | 🔴 | K7 | 不同实体不同控制映射（投射物操控时） |

### 3C — 移动 / 物理系统

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| SY20 | Movement Slow Tag Support | P1 | 🔴 | F6 | 移动系统检查 `slow_50`/`slow_30` Tag → 应用对应速度缩放 |
| SY21 | Movement Rooted Tag Support | P1 | 🔴 | F7 | 移动系统检查 `rooted` Tag → 完全阻止移动输入 |
| SY22 | Displacement Collision Stop | P2 | 🔴 | I11, U2 | 位移效果碰撞墙壁/目标时提前终止 + 触发回调 |
| SY23 | Airborne State System | P2 | 🔴 | I12, Q4 | 浮空 Tag + 空中物理约束 + 落地检测 |
| SY24 | Atomic Dual-Write Position Swap | P1 | 🔴 | I8 | 同帧完成两个实体的坐标互写 |
| SY25 | Wall Contact Detection | P3 | 🔴 | I9, U4 | 侧面碰撞检测（攀爬/墙面战斗） |
| SY26 | Swing Physics (tether constraint) | P3 | 🔴 | I5 | 钟摆物理约束 + 绳索模拟 |

### 3D — 召唤 / 放置系统

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| SY30 | Summon Ownership Link | P1 | 🔴 | K3, C6 | 召唤物与召唤者关联；Blackboard 记录 owner entity ref |
| SY31 | Max Summon/Turret Count Limit | P2 | 🔴 | K2, K3, K4 | 同时存在数量上限管理 |
| SY32 | Summon Death Callback | P2 | 🔴 | K3 | 召唤物死亡时触发冷却/回调 |
| SY33 | AI Graph Bind on Spawn | P1 | 🔴 | K4 | AI 召唤物生成时绑定行为图 |
| SY34 | AI Follow Owner Behavior | P2 | 🔴 | K4 | 无目标时跟随主人 |
| SY35 | Portal Link + Teleport Mechanism | P1 | 🔴 | K5 | 双向传送门关联 + 冷却防无限传送 |
| SY36 | Proximity Trigger (trap/ward) | P1 | 🔴 | K1, U6 | 敌方进入半径触发效果 |
| SY37 | Placement Validation | P2 | 🔴 | K2 | 放置位合法性检测（地形） |

### 3E — 视野 / 环境系统

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| SY40 | Vision Radius + Fog of War | P1 | 🔴 | K6 | 视野计算 + 战争迷雾 |
| SY41 | Environment Tag Scanning | P1 | 🔴 | U1, U6 | 扫描附近可交互环境实体（投掷物、陷阱、机关） |
| SY42 | Terrain Type Query | P2 | 🔴 | U5 | 查询当前位置地形类型（草丛/水面/岩石） |
| SY43 | Navigation Blocker for Dynamic Walls | P2 | 🔴 | U7 | 动态创建墙壁影响寻路 |
| SY44 | AI Taunt Response | P2 | 🔴 | B9 | AI 识别 `taunted` Tag → 强制攻击施法者 |

### 3F — 其他系统

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| SY50 | PositionHistory Ring Buffer | P2 | 🔴 | B5 | 记录最近 N 帧位置历史（时间回溯） |
| SY51 | Time Dilation / Global TimeScale | P2 | 🔴 | O4, H5 | 全局/局部时间缩放控制 |
| SY52 | Charge Effect OnExpire Auto-Fire | P1 | 🔴 | F8 | EffectClip OnExpire Phase 自动触发 "release" 事件 |
| SY53 | IncomingAttackWithinTicks Query | P1 | 🔴 | H5, H8 | 检测即将命中的攻击（完美闪避/方向反击判定） |
| SY54 | Dead Unit Retention | P1 | 🔴 | C4 | 死亡单位保留 Entity + "dead" Tag（不立即销毁，供复活） |
| SY55 | Tether Distance Monitor | P1 | 🔴 | C9 | 持续监测两实体距离；超限断裂 |
| SY56 | Blackboard Rally Point | P1 | 🟢 | R6 | Mod 配置 `persistentStoredTarget` + `BlackboardStoredTargetOps` + `SubmitOrderFromBlackboard`；见 `gitbook/architecture/input-order-and-spawn-target.md` |
| SY57 | TransportCapacity Component | P2 | 🔴 | R4 | 运输载具容量 + 货物列表管理 |

---

## Layer 4 — Input / Target Filter 配置

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| IN01 | TargetFilter: DeadFriendly | P1 | 🔴 | C4 | 选取死亡友方单位（复活技能） |
| IN02 | TargetFilter: OwnedSummon | P2 | 🔴 | C6 | 筛选 `summoned` Tag + owner 匹配的单位 |
| IN03 | Direction TargetType | P2 | 🔴 | S3, E8 | OrderTargetType.Direction 支持方向输入 |
| IN04 | Vector Input Mode (two-point drag) | P2 | 🔴 | E8 | 起点+拖拽终点的双点输入 |
| IN05 | Minimap Click → World Position | P3 | 🔴 | D4, S6 | 小地图点击转世界坐标（Adapter 层） |

---

## Layer 5 — Projectile Config 扩展

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| PJ01 | BlockedBy Terrain Support | P1 | 🔴 | E1 | 弹道碰撞支持地形阻挡 |
| PJ02 | DestroyOnHit Flag | P1 | 🔴 | E1 | 命中后销毁弹道（默认行为配置化） |
| PJ03 | Penetrate Mode + MaxPierceCount | P1 | 🔴 | E2 | DestroyOnHit=false + 已命中实体去重 + 穿透次数限制 |
| PJ04 | Arc Trajectory (parabolic) | P2 | 🔴 | E5 | 抛物线弹道 + 重力参数 + OnGround 触发 |
| PJ05 | Boomerang Trajectory | P2 | 🔴 | E6 | 双阶段（去/回）状态机 + ReturnToCaster 自动追踪 |
| PJ06 | Bounce/Chain | P2 | 🔴 | E7 | 命中→搜索下一目标→重定向；MaxBounces + BounceRange |
| PJ07 | Wall Reflect | P3 | 🔴 | E9 | 地形法线获取 + 反射计算 + MaxReflections |
| PJ08 | Hit Entity Tracking (dedup) | P1 | 🔴 | E2 | 穿透/回旋弹道避免重复命中同一实体 |
| PJ09 | SpreadAngle Config | P2 | 🔴 | E10 | 扩散弹道角度配置 |

---

## Layer 6 — VFX / Performer / UI

| # | 需求 | 优先级 | 状态 | 来源 | 说明 |
|---|------|--------|------|------|------|
| VU01 | Attached VFX Lifetime Binding | P1 | 🔴 | H3, H6 | VFX 随 Tag 过期自动销毁（处决提示/反击提示） |
| VU02 | Vector Input Drag UI | P2 | 🔴 | E8 | 双点拖拽交互 UI |
| VU03 | Ward Detection UI | P2 | 🔴 | K6 | 检测并高亮敌方守卫 |
| VU04 | Posture Bar UI | P2 | 🔴 | H6 | 敌方头顶架势条 |

---

## 统计摘要

| 层 | P0 | P1 | P2 | P3 | 合计 |
|----|----|----|----|----|------|
| Graph VM | — | 1 | 3 | — | **4** |
| GAS Core (Attribute) | — | 8 | — | 1 | **9** |
| GAS Core (AbilitySpec) | 1 | 4 | — | — | **5** |
| GAS Core (Effect/Chain) | — | 3 | 3 | — | **6** |
| BuiltinHandler | — | 4 | 4 | — | **8** |
| System — 战斗 | — | 3 | 2 | — | **5** |
| System — Input/交互 | — | 2 | 4 | — | **6** |
| System — 移动/物理 | — | 3 | 2 | 2 | **7** |
| System — 召唤/放置 | — | 4 | 4 | — | **8** |
| System — 视野/环境 | — | 2 | 3 | — | **5** |
| System — 其他 | — | 7 | 3 | — | **10** |
| Input/TargetFilter | — | 1 | 3 | 1 | **5** |
| Projectile Config | — | 4 | 4 | 1 | **9** |
| VFX/Performer/UI | — | 1 | 3 | — | **4** |
| **合计** | **1** | **47** | **38** | **5** | **91** |

---

## 关键阻塞链

```
AB01 (Attribute Precondition) ← T1, T3, T4, T5, G8, Q1, Q6, Q7
  → 8 个机制共用，是全局最高优先级单点

SY10 (CursorDirectionWriter) ← F1, F3, F5, G11
  → 所有蓄力方向射击 + 持续开火机制的前置

BH01 (RedirectProjectile) ← H7
  → 弹道反射唯一阻塞项

EC06 (Wildcard Tag) ← H3, H6
  → 反击提示 + 架势击破处决的前置

SY40 (Vision + Fog) ← K6
  → 守卫/视野系统完整功能的前置
```
