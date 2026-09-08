# Presenter 集合标记显隐与批量清理报告

日期：2026-09-08。任务：[issue #1465](https://github.com/MightyBubble/Ludots/issues/1465)。基线提交：`6b177db111ae7f57fcefa02641bad7004a5131da`。设计范围见 [实施方案](implementation-plan.md)。

## 结果

现有全局规则通过配置将集合退出改为隐藏，再进入恢复原实例。Core 同时修正真正销毁时的全表扫描。沿用现有 Presenter 实例、参数、静态输出和适配器管线；没有新增运行时、schema、enum 或玩法标记组件。

历史核对见 [修复前审计](README.md)：全表扫描在 `c9b8e009a1` 引入，`096408f0d0` 将 Case E 改为全局实例增删后放大了该成本。既有批量根创建和静态保留优化仍在，未发现它们被该配置提交删除。

## 性能对比

Release、Windows x64、.NET 9、`DOTNET_TieredCompilation=0`。环境详情见 [environment.json](environment.json)。均为 CPU 子链测量，未隔离系统调度，不能换算为游戏 FPS。最终清理复测时 Raylib 场景同时运行，存在机器负载差异。

### 真正销毁

相同程序、10000 所有者、5000 静态环，配置容量 131072、实际哈希槽 262144。测量真实 Runtime 的 `DestroyScopedPresenter` 阶段，中位数：

| 项目 | 修复前 | 修复后 |
|---|---:|---:|
| 销毁 5000 个环 | 902.6910 ms | 8.4864 ms |
| 当前线程分配 | 515992 B | 195928 B |

后测三次为 8.4864、8.8579、7.5557 ms，约降低 99.1%。分配仍非零：缓存委托消除了每次销毁的委托分配，其他生命周期容器仍有分配。原始数据：[前测](runtime-samples.csv)、[后测](final/runtime-samples.csv)。不要与下面的显隐整段计时直接相减。

表级隔离实验也保留了原来的存活身份完整性检查；10000 次按键删除再 Release 的后测中位数为 0.5598 ms，前测为 2555.8929 ms。索引维护使单独 Remove 增加了常数工作。原始数据：[前测](samples.csv)、[后测](final/samples.csv)。

### 配置显隐

真实配置加载器、全局规则、Runtime、Emit、RequestFlush 和最终快照。世界有 10000 个所有者；事件入队、断言和报告写入不计时。提交阶段包含预览退出和选中进入。此测量不含集合查询、输入、适配器同步及 GPU。

| 成员数 | 首次提交 | 重复提交范围，4 次 | 重复提交分配 | 保留总实例数 |
|---|---:|---:|---:|---:|
| 5000 | 25.4123 ms / 9952048 B | 6.9001–7.4573 ms | 0 B | 20000 |
| 10000 | 47.2527 ms / 15404984 B | 15.1135–17.1517 ms | 0 B | 30000 |

实例数包含 10000 个单位根、曾出现的预览环和选中环。清空选择后只输出 10000 个单位根，隐藏实例保留。四轮重复中，实例数、视觉身份数和 Runtime 结构版本保持不变；最后一轮的零分配有测试断言。

首次预览分别为 26.0800 / 32.3326 ms，仍需创建实例和扩容。最后一轮验收时 Raylib 同时运行；此前各轮万人重复提交测得约 11–15 ms。这些差异说明冷启动和系统负载不可忽略。本次没有预创建，也没有证明首次或重复抬起满足 16.7 ms 整帧预算。

数据：[5000 成员](../../acceptance/presenter-collection-visibility/scale-5000.csv)、[10000 成员](../../acceptance/presenter-collection-visibility/scale-10000.csv)。

## 配置与清理合同

配置正本放在现有 [Presenter 快速上手](../../../gitbook/architecture/presenter-quickstart.md)。进入集合使用 Scoped `CreatePresenter` 携带 Int 可见性 1；退出使用同定义、同作用域的 `SetParam` 写 0；`AssetBinding.visibilityParamKey` 显式声明读取该参数。

真正销毁保留原义。生命周期规则仍负责所有者死亡和作用域退出；隐藏不会将实例转交其他目标，也不会停止动画、声音、计时器和绑定。复杂 HUD 或行为需要各自验证。

视觉身份表新增按 Presenter 稳定 ID 索引的槽链。删除和哈希碰撞搬移同步维护链，清理只查所属键。两个 int 槽数组在当前容量下增加约 2 MiB，另有预分配 Dictionary 空间。这是以常驻内存换取清理成本；固定容量耗尽仍显式抛错。

## 验证

- 69 个聚焦回归通过，覆盖显隐、同单位不同作用域、重复复用、所有者死亡、碰撞搬移、随机增删、容量耗尽、身份不复用、资产类型和 duration 链。日志：[verification.log](verification.log)。
- 另补所有者销毁回归，经过明确死亡规则清理隐藏与可见实例；日志：[owner-cleanup-verification.log](owner-cleanup-verification.log)。
- Case E 正式配置的 7 个验收测试通过，包括全局装饰、框选和死亡；在依赖查询分支的 showcase 工作区执行。Core PR 不携带其前置查询实现。
- 可读验收与路径：[5000 成员](../../acceptance/presenter-collection-visibility/battle-report-5000.md)、[10000 成员](../../acceptance/presenter-collection-visibility/battle-report-10000.md)、[path.mmd](../../acceptance/presenter-collection-visibility/path.mmd)。

运行时验收记录随 Case E 配置提交提供。现有构建警告未修改，未运行全仓测试。

## 复现

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release --project artifacts/benchmarks/presenter-release-audit/ReleaseAudit.csproj -- artifacts/benchmarks/presenter-release-audit/final
dotnet run -c Release --project artifacts/benchmarks/presenter-release-audit/RuntimeAudit.csproj -- artifacts/benchmarks/presenter-release-audit/final
dotnet test src/Tests/PresentationTests/PresentationTests.csproj -c Release --filter 'FullyQualifiedName~PresenterCollectionVisibilityTests|FullyQualifiedName~PresenterVisualStableIdReleaseTests|FullyQualifiedName~PresenterAssetKindTests|FullyQualifiedName~PresenterDurationChainTests'
```

两个审计项目的中间文件按项目名隔离，避免共享 obj 导致运行错误 apphost。重跑写入 final 目录，保留根目录中的修复前基线。
