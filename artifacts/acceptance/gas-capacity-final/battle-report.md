== GAS 装载期定容收口验收（Epic #1196 / RFC-0067 P2+P3+P4-lite+P5）==
[P2 上限解除] 标签可登记 256 → 4096（TagRegistry.MaxTagIds 与内嵌位图宽度解耦；位 [256,Plan) 世界列存唯一真相）
[P2 全链] GasTagCapacityTests 7/7：高标签 add/remove/has 幂等往返 → 稀疏计数叠层 → 延迟触发 TagChanged（absent↔present）→ 低位镜像一致 → 高 id 规则注册期失败关闭 → 无列存失败关闭
[P3 跨域] presenter 运行期属性绑定高槽读路由（BehaviorSystem）；内联初始/定义条件高 id 显式失败关闭并指明替代通道；exchange/query/production 读路由
[P4-lite] ExtensionAttributeRegistry/AttributeSchemaUpdateSystem 全链删除（T16 唯一出口：普查证实零消费的死代码）
[P5 全量对照] 活差分门（同进程背靠背双测，抗 100% 外部负载）两轮全过：
  attr.setw.get.hot +1.0%/+0.6% 零分配 | attr.aggregate +2.7%/-1.1% 零分配 | attr/tag footprint 持平或更快（字节同） | tag.add.has +2.0%/-4.8% 零分配
  入库 benchmark-baseline.json（无列存侧）+ benchmark-final.json（列存侧）
[方法学留痕] 顺序偏差教训：活差分两侧必须在门内同热身新测（先跑侧吃冷 JIT）；负载 100% 机器上 JSON 跨时刻对比不可用，活差分为诚实口径
== 验收通过：Epic #1196 收口条件满足；剩余债务单列跟进票（内嵌镜像拆除 + 规则/导航/知识域高 id 对齐）==
