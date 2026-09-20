# GAS Composition Gate — MapTriggerGraph 收口批自审（事件词典 / 区域 / 地图变量 / 时间线 / 旗舰 showcase / LevelDirector 退役）

## GAS Composition Gate — Self Review

- **Task / Issue**: Epic #1030 收口批（本文件不覆盖 `gas-composition-gate-map-trigger-graph-mvp.md`，那是首批方言/挂载切片的正本）
- **Date**: 2026-08-20
- **Agent / Author**: ZCode session（orchestrator + 5 implementation subagents）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: B——新事件源与地图作用域状态的数据作者面（不是 preset 开关、不是 profile enum）

结论: PASS

一句话理由: 关卡反应的全部新表达力落在数据层（事件词典/区域声明/Variables 声明/entry filters 与 refire）+ 4 个地图变量 op；事件全部经 TriggerManager 唯一总线，区域/时钟系统只是新的事件供给者，未出现第二分发器或第二执行器。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| ThinkWave 时钟 + 实体生死/存活计数事件供给 | 引擎系统层 | ThinkWaveClockSystem（DeferredTriggerCollection 组，地图节拍、suspend 感知） |
| 区域进出事件供给 | 引擎系统层 | RegionTriggerSystem（订阅 ThinkWaveElapsed，单节拍源） |
| 地图作用域变量 | L0 状态存储 | MapVariableStore（MapSession 持有，随装卸生灭，修订号单调） |
| MapVar 读写 op（443-446） | L0 | GasGraphOpHandlerTable + GasGraphRuntimeApi 端口（实体→地图解析） |
| PhaseChanged 事件 | 唯一总线 | Store 写路径 → TriggerManager.FireMapEventAsync |
| entry filters / refire | L1 作者面 | MapTriggerGraphEntry 扩展 + 前门严格解析 + 挂载触发器 CheckConditions |
| 时间线（Yield/Wait 续跑） | L0 既有 + 宿主 | 复跑触发器挂 ThinkWaveElapsed，游标/寄存器由触发器实例持有 |
| 旗舰 showcase | 数据 | map_trigger_night_raid：地图 Variables/Regions/挂载 + 一张 MapTriggerGraph，Runtime 零关卡逻辑 |

### 3. Reuse list

- Handlers: ShowPanel/CreatePanel 等既有 op handler 原样复用；MaterializeTemplate 未启用（Effect 语境专属，MapTrigger 前门按策略拒绝——诚实降级为预置波次）
- Queues / Systems: TriggerManager 事件路由、RegisterMapTriggers 生命周期、既有 think-wave 概念升格为引擎时钟
- Resolvers / Registries: GraphProgramRegistry（入口表扩展校验）、ConfigKeyRegistry（var 名符号）、MapSession.EntityIndex、World.SubscribeEntityDestroyed（死亡事件）
- Existing presets / graphs: 火球 showcase 面板模板/挂载模式原样沿用为旗舰的胜利面板

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| ReadMapVarInt/Float 443/444 | 读地图作用域变量 | 既有 ReadBlackboard* 是实体作用域；地图存储无既有读嘴 |
| WriteMapVarInt/Float 445/446 | 写地图作用域变量（phase 变量写即触发 PhaseChanged） | 同上；Incr 不设 op——用 Read+Add+Write 组合（组合优先） |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无新增事务面——Store 生命周期挂地图装卸；事件队列有界（1024）且丢弃计数器必须是活的（已测）。

### 6. Config SSOT

行为配置落在: map（Variables/Regions/ThinkWaveIntervalTicks/MapTriggerGraphs）+ graph（entries 的 filters/refire/once）

是否新增 JSON schema: YES——三处地图字段与 entry 扩展字段，均为严格加载（未知字段拒绝、方向/策略拼写校验、阈值+方向成对要求），理由同 MVP 批：这些是本批交付的本体概念。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线（旗舰用预置实体，未造图侧 spawn 旁路）
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（tag 过滤无载荷即不匹配、refire 默认 ignore 均为成文语义；EntitySpawned 为波界成员差分——语义成文并有测试钉死）
- [x] 删除了从未触发的 GameEvents.Tick；「丢弃计数永远为零」挂账清偿（ThinkWaveClockSystem.TotalDroppedLifecycleEvents 有测试断言递增）

### 8. Next variant test

「下一个 Mod 变体」将修改: 地图 JSON（新区域/新变量/新波次）或图 entries（新事件入口/新 filters/新 refire）——只动数据。boss 若要改用 tag 判定，需要给死亡事件补 tag 载荷（事件供给扩展，非作者面变更）。

### 附：本批成文的语义决策

- EntitySpawned = 波界净成员差分（spawn+死同波只发死）；EntityDied 逐实体发、队伍在销毁时捕获
- EntityAliveCountChanged 仅在计数变化沿触发（阈值/方向由 entry filters 表达）
- 区域边界算在内；死亡实体静默退出集合不发 Exited；资格丢失（去 tag/去位置）发 Exited
- 图侧 spawn/destroy 动词仍未开放（MaterializeTemplate 为 Effect 语境专属）——旗舰以预置波次诚实降级；开放图侧 spawn 属 #1030 后续动词切片
- 退役：LevelDirector/LevelBlueprintFactory/LevelTriggerOps/LevelScriptPrograms/GraphActionHost.Level/level.phaseAdvance 桩/关卡蓝图试炼 mod 全删，零兼容层
