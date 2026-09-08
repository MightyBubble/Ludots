# 大批量 Presenter 历史与销毁性能审计

日期：2026-09-08。审计对象：Case E 万人场景框选抬起时的表现切换，以及既有 Presenter 优化是否丢失。

## 结论

优先修现有实例清理路径。已有批量创建、静态增量输出、保留型表现优化仍在；本轮没有发现这些能力被 Case E 分支删除的证据。全局集合规则引入的大量实例销毁，暴露了早已存在的按实例全表扫描。

新建平行列式实例运行时的理由不成立，应暂停该提案。全局规则的业务语义应保留；优化应先落在现有 Presenter 基建内。

本页保留修复前的审计记录与基线。后续实现、配置和新测结果见 [实现报告](implementation-report.md)。

## 范围与版本

- 已拉取远端，在独立工作区审计 `origin/main`：`6b177db111ae7f57fcefa02641bad7004a5131da`。
- 对照 Case E 工作分支提交 `2a383853a2`，并读取其工作区当前亮环配置。
- 两个提交的 `PresenterRuntimeSystem.cs`、`PresenterVisualStableIdTable.cs` 无差异；`PresenterEntityRuntime.cs` 的差异仅为新增交互上下文绑定标记的 7 行，不涉及批量创建实现。
- 历史检查覆盖实例存储迁移、批量根与子树创建、稳定视觉 ID、静态增量、保留型输出、类型化请求、Case E 全局集合规则，以及近期渲染回滚与恢复。
- 本轮未核对玩家正在运行的二进制与上述源代码是否一致，也未测量 Raylib 完整输入到出图链路。结论不等于仓库所有历史提交均无性能回退。

## 发现

### P1：每销毁一个实例，扫描一次整张视觉 ID 表

源代码：`src/Core/Presentation/Presenters/PresenterVisualStableIdTable.cs` 的 `ReleasePresenter`，以及 `src/Core/Presentation/Systems/PresenterRuntimeSystem.cs` 的 `RemoveStableVisualCacheIfPresent`。

销毁时，系统先按行为槽的完整键删除缓存身份，再调用 `ReleasePresenter` 扫描整个底层数组。单个静态亮环的键已删除，这次扫描仍然执行。K 个实例销毁的扫描量为 K × 表容量。

Core Mod 将 `visualSnapshotBufferCapacity` 配为 131072。`src/Core/Engine/GameEngine.cs` 用该配置构造 ID 表，经负载率与二次幂取整后实际有 262144 个槽。Case E 的游戏配置未覆盖该值；本轮按这个配置测量，但没有读取运行进程的最终合并配置。

`src/Core/Presentation/Systems/PresenterEmitSystem.cs` 的销毁回调也调用同一清理方法。因此只优化某一条命令路径不能覆盖全部销毁入口。

来源：`c9b8e009a1`（2026-06-15）引入稳定视觉 ID 时，旧名 `ReleasePerformer` 就已经使用相同全表扫描；后续改名与类型迁移保留了算法。它是旧瓶颈，在新的大量增删场景中被放大。

建议：在现有 ID 表内建立实例到所属视觉键的清理索引，使销毁只访问该实例的键，并维护删除、哈希搬移、容量耗尽与身份不复用的约束。实验里的“只按键删除”仅用于隔离成本，不能直接作为通用修复；动态输出、多槽表现的剩余身份仍必须正确回收。

### P1：全局规则切换了负载形态，既有批量创建未覆盖此入口

`096408f0d0`（2026-09-05）把 Case E 亮环从单位根上的行为槽启停，改为全局规则创建、销毁独立 Scoped 实例。提交只改配置与验收测试，没有改 Core。

这保留了跨模板的全局集合语义，但抬起时从“修改行为状态”变成“大量销毁预览实例并创建选中实例”。没有成员变化的预览帧能走已有静态保留路径，抬起则进入实例生命周期路径。

`src/Core/Presentation/Presenters/PresenterEntityRuntime.cs` 的 `CreateEntityAnchoredRootBatch` 已有批量分配、编译签名、批量填充与子树处理；生产调用在 `src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs` 和 `src/Core/Systems/MapLoader.cs`。

集合规则生成的命令在 `PresenterRuntimeSystem.Update` 中逐条调用 `CreateHierarchy`。这是覆盖范围缺口，未发现已有集合批量入口被近期提交删除的证据。

不能直接把根批量 API 套到亮环上：全局集合规则会关联单位根作为父实例；现有批量根路径还明确拒绝带 duration 的定义，因为它不发布对应的创建事件。复用时必须保留父子关系、作用域、事件顺序、参数、计时器与生命周期合同。

建议：先去除已实测的清理扫描，再测量剩余创建成本，决定如何扩展现有批量能力。若需要配置新的执行方式，应显式声明并在编译时验证，不能根据数量或定义形状暗中切换。

### P2：原有性能验收主要覆盖稳定帧，未覆盖大批量退出

历史报告确实证明做过大量优化：

- `artifacts/benchmarks/presenter-mesh-ism-production-path/benchmark-report.md`：1 万根实例批量创建约 15.5183 ms，稳定 tick 平均约 1.7387 ms；不含 GPU 绘制。
- `artifacts/benchmarks/presenter-subtype-retained-static-lanes/benchmark-report.md`：1 万个多类型表现稳定 tick 平均约 0.0631 ms，未变化请求为零。
- `artifacts/benchmarks/presenter-creation-control-variables/benchmark-report.md`：另测逐条命令创建，说明两类入口此前就分开存在。

这些是仓库历史数据，未作为本机本轮的新测结果。它们没有证明一次销毁数千实例也满足帧预算。Case E 全局规则改动的测试验证实例随集合生灭，未设置这个规模的性能约束。

本轮实际命令路径也发生分配：5000 个预览实例创建约 7.93 MB，销毁 515992 字节，选中实例创建 840216 字节；测量段的静态发射为零分配。分配来源未逐项归因，不能称整条生命周期路径已经 0Alloc。

## 本轮测量

运行环境见 [environment.json](environment.json)：Release、.NET 9.0.14、Windows x64、关闭分层编译。每个组合预热后采样 3 次；单线程墙钟计时和当前线程分配计数，不隔离操作系统调度。微秒以下数据只说明路径很轻，不用于推算游戏帧率。

### 原表源码隔离实验

[ReleaseAudit.csproj](ReleaseAudit.csproj) 直接链接生产表、键与分配器源码，不复制算法。固定保留 10000 个视觉身份，删除指定数量的其他身份。每轮校验全部保留身份未变、删除身份不可查询、最终数量正确。

中位数，单位 ms；两种模式的测量段均为零分配。

| 配置容量 / 实际槽数 | 删除数 | 按键删除控制组 | 按键删除后 ReleasePresenter |
|---|---:|---:|---:|
| 16384 / 32768 | 5000 | 0.1499 | 179.7741 |
| 131072 / 262144 | 100 | 0.0007 | 15.9953 |
| 131072 / 262144 | 1000 | 0.0065 | 166.5919 |
| 131072 / 262144 | 5000 | 0.0818 | 1967.0423 |
| 131072 / 262144 | 10000 | 0.1659 | 2555.8929 |

原始 48 个测量点：[samples.csv](samples.csv)。键分布、占用率、分支预测和系统调度会影响常数；不要将这组时间与下一组时间相减来估算某个方法的占比。缩小容量仅为控制变量，不是生产修法。

### 真实 Presenter 子链

[RuntimeAudit.csproj](RuntimeAudit.csproj) 引用完整 Core 项目。建立 10000 个所有者与静态根表现，向 5000 个根挂预览实例，再通过真实 `DestroyScopedPresenter` 命令销毁，随后创建选中实例。发射使用真实 `PresenterEmitSystem` 与 `StableDrawCache`。每阶段检查实例数、视觉身份数、缓存数，并检查所有者和根仍存活。

中位数，单位 ms：

| 阶段 | 实际槽数 32768 | 实际槽数 262144 |
|---|---:|---:|
| 预览实例创建 | 7.5958 | 8.5581 |
| 预览首次发射 | 1.2416 | 1.6275 |
| 预览实例销毁 | 208.9928 | 902.6910 |
| 选中实例创建 | 8.1131 | 8.7055 |
| 选中首次发射 | 1.1549 | 1.5109 |

当前配置容量下销毁的 3 次样本为 882.8742、928.3159、902.6910 ms。成员不变时，测量的 runtime 与 emit 两个空闲阶段各自低于 0.01 ms；这不代表整个预览帧的耗时。

原始 42 个阶段测量点：[runtime-samples.csv](runtime-samples.csv)。程序在预览帧边界清空静态增量日志，模拟正式 flush 后的状态，避免把未消费日志的累计扩容误算成正常成本。

此测试隔离生命周期与发射子链：不含集合 diff、规则匹配、游戏图、真实关系上下文、屏幕投影、请求 flush、适配器同步、GPU 和输入调度。创建与销毁分段调用便于计时，不等同于游戏一帧的完整调度。尚未对玩家实际抬起帧做采样剖析，不能宣称已经证明唯一根因或修复游戏卡顿。

## 历史核对

| 提交 | 改动 | 当前核对结果 |
|---|---|---|
| `942d077cd0`、`ebce8b7244`，04-20 | 迁移到 Arch 实体实例，删除手写实例缓冲与参数黑板 | 平行实例存储曾明确退役；重新建一套需要充分理由 |
| `6f9faf6dfc`，04-22 | Blacksmith 性能管线与批量根创建 | 批量 API 与生产调用保留 |
| `f4511b1631`、`915c9b6616`，04-26 / 04-29 | 稳定输出、运行时与裁剪优化 | 当前仍有静态脏集合、批量处理与缓存跳过 |
| `c9b8e009a1`，06-15 | 稳定视觉身份表 | 本轮瓶颈的全表清理从这里已存在 |
| `ed0506ad77`，06-17 | 地图批量参数与静态增量 | `StableDrawCache` 增量和 Raylib 增量同步仍在 |
| `42f471acb7`、`4525591679`，08-26 | 类型化请求、比较去装箱 | 当前保留类型化请求路径与相关等值实现 |
| `e39c111e36` → `216357e17b`，08-30 | 大范围回滚后，在干净 main 重放渲染改动 | 恢复提交是审计 main 的祖先；抽查资产存储、原生资源、输入路由与回滚前一致，不能只凭 revert 判断丢失 |
| `096408f0d0`，09-05 | Case E 全局集合装饰采用实例创建/销毁 | 业务语义有效，但激活了缺少大批量退出覆盖的生命周期路径 |

## 修复顺序与验证边界

1. 在现有视觉身份表修正按实例清理，覆盖所有销毁入口，并验证多槽、动态输出、碰撞搬移、实例 ID 不复用、重复退出与容量耗尽。
2. 重跑本报告的容量与删除规模矩阵，确认成本不再随全表容量倍增；再测完整释放帧，定位剩余分配与创建成本。
3. 如动态全局装饰仍超预算，沿现有编译创建计划与 Arch 批量能力扩展，不复制一套实例存储，不将全局规则搬回每个模板。
4. 为集合进入、退出、玩家切换、重复框选增加规模验证。不同表现类型的发射成本仍需分别测量；亮环结果不能直接保证 HUD、文字或动画也同样快。

补充风险：`PresenterTimerTable.KillAll` 扫全部活跃计时器，`PresenterEntityRuntime.ReleaseDeadOwners` 在有命令时扫全部所有者；前者在当前无计时器环场景不是主要负担，后者是每次 Update 的线性工作。带规则定义的 `_byDefinition` 列表移除也值得专项测量，当前无规则环定义不进入该索引。以上未作为本次抬起瓶颈的实测结论。

此前“把环改成根行为启停就不会产生结构变化”的说法也不准确：`SetBehaviorActive` 仍会同步工作标记，可能加摘组件。不能据此保证无热路径结构变更。

## 复现

在仓库根目录执行，结果写到本报告目录；两个项目顺序运行，各自正常 restore。

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --configuration Release --project artifacts/benchmarks/presenter-release-audit/ReleaseAudit.csproj -- artifacts/benchmarks/presenter-release-audit
dotnet run --configuration Release --project artifacts/benchmarks/presenter-release-audit/RuntimeAudit.csproj -- artifacts/benchmarks/presenter-release-audit
```

两个程序均退出码 0，内置完整性检查通过。Core 构建有既存警告，本轮未修复这些警告；未运行全部仓库测试。
