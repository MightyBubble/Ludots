# PresenterRuntimeSystem 执行 SinkParamToAsset 命令闭环

## Scenario Card
- Player goal: 发出 SinkParamToAsset 命令后，目标 Presenter 指定 asset slot 在命令处理阶段同步得到 param lane 当前值；四类失败可观测且不阻断后续命令。
- Map: N/A（runtime 单测 + pipeline fixture；Showcase slice 按批次显式推迟）
- Branch: `codex/issue-1091-sink-param-to-asset`

## Contract
- `SinkParamToAsset` 是唯一 sink 入口：枚举/loader/route 原样（`PresenterCommandKind.SinkParamToAsset = 7`、route `SingleRuntime`），仅补 `PresenterRuntimeSystem` 的执行分支。
- 命令字段固化：目标 Presenter handle（`PresenterEntity`，复用 SetParam 的 scoped 实例回退解析）、param lane（`ParamLane`）、asset slot（`TargetBehaviorSlot`）。
- 命令处理阶段同步写入：分支内直接调用 `PresenterAssetEmitRuntime.Emit`（AssetBinding 底层发射接口）把当前 param 值解析成 PresentationRequest 写入 `PresentationRequestBuffer`，不延迟到 PresenterEmitSystem/帧尾。
- 结构化拒绝：`PresenterSinkDiagnostics`（环形记录 + `LogChannels.Presentation` 日志）逐条携带 commandId/target/definitionId/paramKey/lane/slot 与拒绝原因；异常不外抛、命令不静默丢弃，命令循环继续。

## Failure Classes (四类失败全部可观测)
| 类别 | PresenterSinkRejection | 判定 |
|---|---|---|
| presenter | `TargetPresenterMissing` / `TargetDefinitionMissing` / `TargetEmitComponentsMissing` | handle 未解析到存活 presenter、定义未注册、缺 emit 组件 |
| slot | `AssetSlotMissing` / `AssetSlotNotAssetBinding` / `AssetSlotInactive` | slot 越界/未声明/非资产槽/行为未激活 |
| lane | `LaneMissing` | paramKey 在任何 lane 都无当前值 |
| 类型 | `LaneTypeMismatch` | paramKey 存在但位于与命令不同的 lane |
| (写入侧) | `AssetWriteSuppressed` | 平台门（LOD/可见性/audience）抑制了同步写，显式记录不静默 |

## Timeline
- [T+000] `runtime_branch` -> `PresenterRuntimeSystem` dispatch 新增 `SinkParamToAsset` case；拒绝/接受双出口均写 `PresenterSinkDiagnostics` + `LogChannels.Presentation`。
- [T+001] `sync_write` -> sink 在命令处理阶段调用 `PresenterAssetEmitRuntime.Emit`，请求直接落入 `PresentationRequestBuffer`；同帧 `PresenterEmitSystem` 不会用旧值覆盖（版本一致性由 param 未变保证）。
- [T+002] `unit_runtime` -> 10 条 runtime 单测覆盖成功写入、同步性、四类拒绝、坏命令后续命令继续处理。
- [T+003] `pipeline_fixture` -> 同批 Create+SetParam+Sink 一次 Runtime update 闭环；Runtime+Emit 双系统夹具验证 sink 请求先于 emit 存在且值一致。
- [T+004] `regression` -> GasTests `FullyQualifiedName~Presenter` 104/104 通过；PresentationTests（排除 Benchmark/50k）775 通过、12 失败经基线复跑确认为本环境既有失败（与本次改动无关）。

## Outcome
- success: yes
- 成功用例断言指定 slot 的 VisualProxy 请求 Scale == lane 当前值（2.5 / 0.5 / 3.25 三组数值），日志与诊断含 target/lane/slot。
- presenter/lane/slot/类型四类拒绝均可观测（结构化记录 + Warn 日志），拒绝后后续 SetParam 命令照常执行。
- 不存在第二个 sink 入口：grep 全仓 `SinkParamToAsset` 消费面仅 `PresenterRuntimeSystem` 一个执行分支。
- 既有 SetParam/AssetBinding 回归通过（GasTests Presenter 全绿）。

## Summary Stats
- 新增 runtime 单测 + pipeline fixture: 10 passed, 0 failed (`PresenterSinkParamToAssetTests`)
- GasTests `--filter FullyQualifiedName~Presenter`: 104 passed, 0 failed
- PresentationTests（排除 Benchmark/50k 重负载夹具）: 775 passed / 12 failed —— 12 个失败（MapLoad 粒子装配、5 个 loader 合同、6 个物理/信息面板 showcase、1 GenreInfo 验收）在 stash 基线复跑中同样失败，属本 worktree 环境既有问题
- Core build: 0 errors

## Deferred
- Showcase slice（presenter_blacksmith A/B 旋钮与截图）按本批范围显式推迟，未修改 `mods/showcases/presenter_blacksmith/` 与 `showcase.registry.json`。
