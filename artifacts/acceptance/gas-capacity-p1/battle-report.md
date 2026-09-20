== GAS 装载期定容 P1 验收（Epic #1196 / RFC-0067 §3.3 P1：属性世界列存切流）==
[上限解除] 属性可登记上限 64 → 1024（AttributeRegistry.MaxAttributeIds 与内嵌镜像宽度 64 解耦；约束数组同步 1024）
[列存] WorldAttributeStore：Plan 冻结后一次性分配（槽×行），行分配只写索引——对局零扩容零分配；行容量/槽位越界全部失败关闭
[双车道] 写权威路由：槽 [0,64) 内嵌镜像（种子建行后同步镜像）+ 槽 [64,Plan) 列存唯一真相；无列存引用高槽 → HighLaneUnavailable 失败关闭
[全链 UAT] GasWorldAttributeStoreTests 6/6：注册 100 属性 → 第 71+ 名落高槽 → 变异/读回/Base=Current 合同 → 低槽镜像一致 → 高槽聚合（cap=base+修饰=110，持久 current 不动）→ 高槽延迟触发（携带 id 与新旧值）→ 行容量/无列存失败关闭
[切流面] 聚合器 ProcessHighSlots、延迟触发 AttributeHighLane、authoring 种子（ComponentRegistry/批量）、事务懒分配高行、存档高行随档与切片恢复、效果提案/BuiltinHandlers/图运行时读路由
[迁移合同] 旧合同钉子迁移：id∈[64,1024) 从 ArgumentOutOfRangeException 升级为合法高槽；<0 或 ≥1024 仍 ArgumentOutOfRange（AttributeAggregatorTests 区间断言原语义保留）
[bench 对比门] 连续三轮通过（10% 阈值、pipeline 1.25、热路径零新增分配）；属性域大回归 175/179（4 失败经同提交 main 基线对照为存量红，与本切无关）
== 验收通过：P1 退出条件满足（属性 UAT 不回归 + 属性对比床不回归），数据见 battle-report 同目录 benchmark-p1.json ==
