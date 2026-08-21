# GAS Composition Gate — TriggerGraph 更名 + 挂载域模型 + 实体域（Epic #1031 S1）自审

## GAS Composition Gate — Self Review

- **Task / Issue**: Epic #1031 切片 S1（D1 方言更名 / D2 挂载域模型 / D3 实体域挂载 / 载荷寄存器种子扩展）
- **Date**: 2026-08-20
- **Agent / Author**: ZCode session（implementation subagent，S1）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: B——挂载域模型（map|entity）与实体模板 TriggerGraphs 的数据作者面（不是 preset 开关、不是 profile enum）

结论: PASS

一句话理由: 实体域的全部新表达力落在数据层（模板 TriggerGraphs、挂载 domain 字段）；更名是零兼容层的合同收紧；实体域挂载复用同一 TriggerManager 地图注册管线、同一切片执行器、同一 think-wave 节拍，未出现第二事件总线、第二执行器或平行物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| GraphKind.MapTrigger → TriggerGraph 更名（作者串同步，旧串失败关闭） | L1 合同 | GraphKind + GraphKindParser + 前门/编译器/登记表消息 |
| 挂载域 domain: map\|entity（ability 现拒，S3 落地） | L1 作者面 | TriggerGraphMount 严格解析 + TriggerGraphMounting 域路由 |
| 实体模板 TriggerGraphs 声明 | 数据 | EntityTemplate 字段 + MapLoader.LoadTemplates 严格校验 |
| 实体域挂载创建（map-load 批量 / 运行时 spawn / MaterializeTemplate） | 引擎接线 | EntityTriggerGraphMounts（MapLoader 缓冲 + GameEngine flush；RuntimeEntitySpawnSystem / EntityLifecycleAtomicOps 即挂即注册） |
| EntitySpawned 当拍立即分发（mount-local，不发总线） | 宿主语义 | EntityTriggerGraphMounts.DispatchLifecycle（挂载触发器 ExecuteLifecycleDispatch） |
| EntityDied 销毁当拍分发（单一全局 World.SubscribeEntityDestroyed） | 引擎系统层 | EntityTriggerGraphMounts.OnEntityDestroyed（先于组件剥离读取地图归属/队伍） |
| 死挂载惰性清扫（有界，think wave 期） | 引擎系统层 | ThinkWaveElapsed 事件处理器 → SweepDeadMounts（每波 ≤64）+ TriggerManager.RemoveMapTriggers |
| 载荷种子扩展 E[1]/I[2]/F[1]（存在才种） | L0 | TriggerGraphMountTrigger.SeedEntryRegisters（Contains 判在场） |

### 3. Reuse list

- Handlers/Executor: GraphExecutor.ExecuteScriptSlice 单一切片执行器原样复用；零新 opcode
- Queues / Systems: TriggerManager 唯一总线（新增 AddMapTriggers/RemoveMapTriggers 为同一登记表的操作面，非第二总线）；RegisterMapTriggers 卸载回收原样覆盖实体域挂载
- Resolvers / Registries: GraphProgramRegistry（TriggerGraphEntries 沿用）、MapSession.EntityIndex、MapVariableStore（实体域 scope=self 经 MapEntity 解析地图，op 端口未动）、TriggerDecoratorRegistry（实体域挂载在创建点装饰，引擎后置装饰跳过实体域防双装饰）
- Existing spawn lanes: MapLoader 批量/单实体两路、RuntimeEntitySpawnSystem 单体/批量两路、EntityLifecycleAtomicOps.MaterializeTemplate 三处均为增量接线，未改物化合同

### 4. New Layer 0 ops (if any)

无。实体域图的全部算子走既有 op 表；本切片零新 opcode、零新事件键。

### 5. Transaction boundary

必须原子 rollback 的步骤: 挂载创建失败（图未注册/类型不符/入口越界）在 map load 或 spawn 点抛错失败关闭（命名模板+图）；运行时 spawn 即挂即注册，失败沿 spawn 管线抛出。无新增需回滚的多步事务。

### 6. Config SSOT

行为配置落在: map（TriggerGraphs 挂载数组，domain 缺省 map；旧字段名 MapTriggerGraphs 失败关闭指名更名）+ entity template（TriggerGraphs: string[]，严格非空修剪串，未知图名在挂载点失败关闭）+ graph（kind "TriggerGraph"，旧 kind 串失败关闭指名更名）

是否新增 JSON schema: YES——三处字段更名/新增均严格加载（未知字段拒绝、ability 域现拒、entity 域必须 scopeInstanceId、模板串严格修剪），理由：挂载域与模板挂载是本切片交付的本体概念。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线（三处既有 spawn 路径增量接线）
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（domain 缺省 map、实体域生命周期事件仅管线分发的语义均成文于类注释并有测试钉死；死挂载 CheckConditions false 成文）
- [x] 单一销毁订阅（World.SubscribeEntityDestroyed 全局一处，先例 ThinkWaveClockSystem）
- [x] 旧名零兼容层：kind 串/挂载字段/类型名/宿主枚举一次迁清，断言测试守卫残留
