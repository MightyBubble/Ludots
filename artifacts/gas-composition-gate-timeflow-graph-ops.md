## GAS Composition Gate — Self Review

- **Task / Issue**: 时间流图节点。整局和玩法步进用暂停令牌、变速令牌；一个人的快慢继续写 time.scale_permille，经已有 AttributeSink 落到本地时钟。
- **Date**: 2026-09-23
- **Agent / Author**: Cursor cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 新能力是五个图节点，加上一条已有 AttributeSink 管线里的绑定。复用 TimeFlowService、AttributeMutationOps、ModifyAttributeSet、LoadAttribute 和 AttributeBindingSystem。没有新的时间服务，没有新的配置枚举。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 读某个时间域停没停、读有效倍率 | 0 | GraphNodeOp 520–521，处理器转到 TimeFlowService |
| 拿暂停令牌、拿变速令牌、放回令牌 | 0 | GraphNodeOp 522–524，写路径与 TimeFlowService 同一条 |
| 一个人的快慢 | 0 | 已有 ModifyAttributeSet / LoadAttribute，属性 time.scale_permille |
| 属性落到本地时钟 | 1 | 绑定 Bind.Time.EntityScalePermille，sink Time.EntityScalePermille |
| 时间域有哪些 | 3 | TimeFlowService 构造时登记 simulation、simulation.gas。图不能新建域 |

### 3. Reuse list

- Handlers: GasGraphOpHandlerTable 转发到 IGraphRuntimeApi，再进 TimeFlowService
- Queues / Systems: AttributeBindingSystem 在 AttributeCalculation 落地；EntityLocalClockSystem 排在它后面，只读落地后的整数
- Resolvers / Registries: 域名留在符号表，运行时按 TimeFlowService 已登记的名字解析；令牌编号用已有黑板整数或地图变量
- Existing presets / graphs: 画廊沿用 script 驱动、FrontDoor 图分片和覆盖注册表

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| ReadTimeFlowPaused | 读这个时间域现在停没停 | 暂停不是实体属性，LoadAttribute 读不到 |
| ReadTimeFlowScalePermille | 读这个时间域的有效倍率，含父域 | 有效倍率在 TimeFlowService，不在 Clock.Speed，也不是属性 |
| AcquireTimeFlowPause | 拿一张暂停令牌并交出编号 | 没有现成节点能往时间域上加暂停 |
| AcquireTimeFlowScale | 拿一张变速令牌并交出编号 | 变速令牌相乘规则在 TimeFlowService，不能拆成改一个属性 |
| ReleaseTimeFlowToken | 放回一张令牌 | 放回必须点名编号，不能靠再写一次属性抵消 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。同一张脚本里先拿后放，放回发生在这张脚本结束之前。中途失败不回滚已经拿上的令牌；读档会重拿新编号，旧编号再放回失败。

### 6. Config SSOT

行为配置落在: graph / catalog（路径）: 域名写在图节点上；一个人的倍率写在属性 time.scale_permille；落地绑定在 `assets/GAS/attribute_bindings.json` 的 `Bind.Time.EntityScalePermille`。

是否新增 JSON schema: NO — 节点字段 domain 走现有图文档，绑定走现有 attribute_bindings。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线

若选了 Core enum → FAIL
