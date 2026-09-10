# "周期 HealthDrift 每帧 ~150KB" 复核结论：**不是误判，数字略被低估**

被复核的声明（来自 `C:\001_AI\LudotsFixHud` 的 `FIX-NOTES-1-2.md` 问题 3）：

> 周期 HealthDrift 效果 ~150KB/帧（Simulation 侧）。本地 A/B：effect on 164KB / off 13KB。

## 1 结论

**场景成立，量级成立，甚至更大：实测 178KB/帧 → 5.9KB/帧（差 ~172KB）。**

但那个 **"164KB / 13KB" 这对数字本身是用被污染协议测的**，不可直接引用；
它恰好方向正确是巧合。可引用的数字见下。

## 2 我用的协议（该仓库自己的裁定）

`LudotsFixHud/RETRO-measurement-misjudgments.md` 已经裁定过可靠协议：

| 方式 | 判定 |
|---|---|
| `GC.GetAllocatedBytesForCurrentThread()` 在系统窗口内 | ❌ 漏 worker 分配（低估） |
| `GC.GetTotalAllocatedBytes()` 在**单系统 Update 窗口内**做增量 | ❌ 会纳入并行 worker 的其他工作（高估） |
| **整帧级 `GetTotalAllocatedBytes()` 增量 + `GC.CollectionCount`** | ✅ 可靠 |

**他们那条 164KB 的读数用的是第三种里最糟的组合** —— `Probe_EffectAllocProfile`：

```csharp
long before = GC.GetTotalAllocatedBytes();                 // 进程级总量
... engine.Tick(...) ...
alloc[i] = GC.GetAllocatedBytesForCurrentThread() - before; // 线程级计数 - 进程级基线
```

**拿线程级计数减进程级基线，两个不同的量相减**，结果无意义
（同时踩了"漏 worker"和"基线不匹配"两个坑）。

## 3 用裁定协议实测（本分支 `perf/massnav-10k-80fps`）

探针：沿用他们 `Probe_GcSanity`（整帧 `GetTotalAllocatedBytes` + Gen0/Gen1），
10K massnav，240 帧，headless Release。

| 配置 | 整帧均值 | max | Gen0/240帧 |
|---|---|---|---|
| 默认（HealthDrift 生效） | **178,313 B/f** | 607,304 | 3 |
| `periodTicks` 改到永不触发 | **5,876 B/f** | 32,800 | 0 |
| **只移除 `OnPeriod` graph**（保留 `OnApply`） | **5,876 B/f** | 24,600 | 0 |

两条独立性验证：
- 关周期 与 关整个 effect **得到同一个数**（5,876）⇒ 成本 100% 在**周期路径**，不在 effect 建立。
- 只摘 `OnPeriod`（保留 `OnApply`）也得到同一个数 ⇒ **不是 OnApply、不是效果本体**，
  就是**每 60 tick 触发一次的那种周期执行**。

## 4 已排除的位置（逐条取证，不是推断）

- **phase graph 执行本身不分配**：在 `EffectLifetimeSystem.ExecutePhaseGraphEntry` 外围加
  `GetAllocatedBytesForCurrentThread` 增量探针，10K 场景约 **3,000 次/60 帧**（≈50 次/帧）执行量下
  `phaseAlloc=0`。所以不是图 VM、不是 `ConfigParamsMerger.BuildMergedConfig`（那是纯 struct 拷贝）。
- **容器都是预分配的**：`_periodPhaseGraphs` 是带初容量的复用 `List`；
  `AddFixed` 是带 fail-closed 上界的 `List.Add`；`DeferredTriggerQueue` 是预分配数组；`PresentationRequestBuffer` fail-closed 不扩容。
- **结构体不分配**：`GameplayEffect` / `EffectContext` / `EffectConfigParams`（`fixed` 内联数组）都是值类型。

## 5 尚未定位到的那一段（诚实交代）

已确定在"周期路径"，但还没钉到具体分配语句。剩余嫌疑按可能性排序：

1. 周期触发经由 **deferred trigger / 地图事件桥** 的每笔处理（`AttributeChanged` 逐条），
   尽管 main 的 `1699990795` 已经把"无消费者"分支变便宜，HealthDrift 这条链路可能仍有消费者。
2. 周期事件进入 **presentation event stream**（血条更新）时的每笔记录。
3. `EffectLifetimeSystem` 扫描阶段每帧对 `GameplayEffect`/`EffectContext` 的**整块 struct 拷贝**
   （`World.Get` 两次）在 10K 规模下的累积 —— 这是拷贝不是分配，但会与上述混淆计时。

要钉死需要 dotnet-trace 的分配采样（本会话的探针协议只能到"整帧/单系统"粒度）。
建议作为独立 issue，不要在本轮猜。

## 6 对"能不能修"的判断

- **可修且值得修**：172KB/帧 ≈ 10MB/秒 的托管流量，虽然 Gen0 只有 3/240 帧
  （未构成 GC 停顿主因），但它是**同一 tick 上 10K 个效果同时到期**的同步尖峰，
  与卡顿的相关性比"平均分配"更值得查。
- **正确修法方向**（符合仓库的"批量/复用 op-state"取向）：
  让同 tick 到期的一批效果走**一次**批量处理，而不是逐笔建立/分发；
  即他们笔记里写的 "建议下一轮批量/复用 op-state"。
- **不要**用"降 period 频率"或"砍效果"来修 —— 那是改玩法语义，不是优化。
