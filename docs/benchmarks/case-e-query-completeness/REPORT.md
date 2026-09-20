# Case E 查询修复性能与验收

## 范围与方法

基线是 `16db792db2`。修复在单独工作区执行，Windows / .NET 9 / Release。同机结果受 JIT 与后台负载影响；耗时为样本，不是硬实时保证。原 Case E 基线只算了 77/104，不能与完整结果直接计算加速比。

可复查证据：`artifacts/acceptance/query-completeness/prepush-final.trx`、`artifacts/acceptance/query-completeness/baseline/collection-before.trx`。基准源码是 `src/Tests/GasTests/Association/CollectionWriteComparisonTests.cs`，两个工作区使用同一份文件。

## 相同负载对照

每组预热 3 次，随后执行 15 对“移出一半，再加回”操作。耗时为单对操作中位数；分配为 15 对操作累计。

| 成员数 | 基线中位数 ms | 修复中位数 ms | 基线分配 B | 修复分配 B |
|---:|---:|---:|---:|---:|
| 100 | 0.1169 | 0.0380 | 27,720 | 0 |
| 1,000 | 8.5737 | 0.4323 | 270,720 | 0 |
| 10,000 | 121.9773 | 1.5424 | 2,700,720 | 0 |

历史回查：`61543121a9` 将 EventKeyedCollectionWriter 改为 CollectionWrite 后引入 MergeScratch.ToArray；前身复用数组。线性去重问题此前已有。本次复用数组和集合判重分别解决分配与平方级合并。

## 正确性与增量成本

- 10k 世界：候选 104/104 和 1004/1004；整框命中数均与候选数相等，半框对照原屏幕投影判定。
- 1k / 10k 世界，单实体变更 10,000 次：各重评估 10,000 次，暖态均 0 B；样本分别 8.6491 / 7.0575 ms。
- 区域内 501 个候选，远处 100 / 10,000 个候选：各执行 100 次框选，精检次数均 50,100，暖态均 0 B；样本分别 25.2388 / 24.0625 ms。
- 全拖拽样本：104 候选为 56.979 ms、最大单拍 21.899 ms；1004 候选为 333.685 ms、最大单拍 178.415 ms。包含展示创建等成本，端到端峰值尚未消除。

## 验证状态

测试项目 Release 构建通过。提交前定向运行 47/47 通过，包括 Case E、派生索引、完整查询、空间查询、集合写入和节点 gallery 自动化测试。命令过滤器：DerivedEntityIndexTests、EntitySetQueryRuntimeTests、IndexedScreenQueryTests、CaseESelection、GraphOpsNodeGalleryAcceptanceTests、DescriptorProjections_TriggerGraphMirrorsScriptIncludingYield、CollectionWriteComparisonTests。

此前较宽回归为 1237 通过 / 29 失败；部分已修，但尚未重新完成整套回归。真实进程录制与两个新增节点 wiki 资产未完成。本 PR 为草稿，不能据此声明 10k 展示性能或零编码多玩家切换已验收。

## 尚待审查

普通查询仍把完整缓冲交给 VM 过滤；生成 chunk 委托和令牌分页未交付。绑定支持受限过滤链。直接 ref 写入必须通知；写入点审计尚需覆盖完整调用面。图替换重绑、最后一个持有者退出时的索引回收、同帧空间成员时序及执行结果借用寿命需要进一步验证。
