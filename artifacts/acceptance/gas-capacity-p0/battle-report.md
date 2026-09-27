== GAS 装载期定容 P0 验收（Epic #1196 / RFC-0067 §3.3 P0）==
[容量计划] GasLoadTimeCapacityPlan.Freeze：需求记录（48 属性/100 标签→槽 48/位空间 128 字对齐）；绝对天花板 1024/4096 fail-closed（内容膨胀口径）；P0 物理上限校验（65 属性→GAS.CAPACITY.ERR.AttributeSlotsExceeded 指明 P1 出口；257 标签→TagBitsExceeded 指明 P2）；负数参数 fail-closed
[冻结窗口] GameEngine.InitializeCoreSystems 尾部（AttributeRegistry.Freeze 同窗口）挂接 + CoreServiceKeys 暴露；真实引擎启动验收（CapabilityStandardPhysics2D 全链）通过——真实内容在 64/256 内静默冻结
[基准床] 七 MetricId 固定场景（ENTITY_COUNT=10000/ITERATIONS=100 禁漂移）：min-of-5 采样（耗时取最小、分配取最大），热路径零分配常驻断言（attr.setw.get.hot / tag.add.has.hot alloc=0）
[baseline 入库] docs/rfcs/gas-loadtime-capacity/benchmark-baseline.json：attr.footprint 1072 B/实体、tag.footprint 134.7 B/实体、setw.get.hot ~0.3µs/op 零分配、aggregate.tick 亚毫秒零分配、pipeline 100k 全管线
[对比门] LUDOTS_COMPARE_CAPACITY_BASELINE=1：耗时 >10%（pipeline 1.25 按指标覆盖、Epic 留痕）或热路径新增分配 → 失败关闭；同机连续两次自证通过
[顺手修复 main 既有损坏] GasBenchmark.Run 缺 aggregateDirty/缺 FinalizeAll（孤立种子代码漂移）；GraphBrainFrontlineEquivalenceTests/GraphBrainOrderOpsTests 因 #1591 移除 RelationshipReasonRegistry 编译损坏——机械适配
== 验收通过：P0 退出条件满足（RFC/Epic 已入 main、CapacityPlan 脚手架就位、baseline JSON 可复跑）==
