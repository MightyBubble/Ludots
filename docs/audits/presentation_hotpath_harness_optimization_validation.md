# Presentation Hotpath Harness 优化验证

本文记录 `#51` 当前 presentation hotpath harness 的优化验证结果，目标是确认新增 crowd gating、lane toggle 和 terrain-backed 场景后，shared acceptance harness 仍然稳定、可重复，并且能观察到玩家可感知的 lane 变化。

## 1 范围

本次验证只覆盖已经落地的实现：

* `mods/fixtures/camera/CameraAcceptanceMod/CameraAcceptanceModEntry.cs`
* `mods/fixtures/camera/CameraAcceptanceMod/Systems/CameraAcceptanceHotpathLaneSystem.cs`
* `mods/fixtures/camera/CameraAcceptanceMod/Systems/CameraAcceptanceDiagnosticsToggleSystem.cs`
* `mods/fixtures/camera/CameraAcceptanceMod/Systems/CameraAcceptanceSelectionOverlaySystem.cs`
* `src/Tests/ThreeCTests/CameraAcceptanceModTests.cs`

本次验证不把 `RFC-0002` 中的三个 playable mod 设计当作已实现功能。

## 2 验证目标

1. 确认 hotpath harness 在真实 runtime 场景中稳定生成 `256` 个 crowd，而不会出现重复 enqueue 漂移。
2. 确认 panel、diagnostics HUD、selection labels、HUD bars、HUD text、terrain、primitives、crowd/culling pressure 可以独立切换。
3. 确认切 lane 后，battle-report 中能观察到用户可见变化与内部 timing 变化同时出现。
4. 确认 `CameraAcceptanceMod` 其他 acceptance 场景未被 `#51` 回归破坏。

## 3 执行命令

本次执行的验证命令：

```powershell
dotnet test src/Tests/ThreeCTests/ThreeCTests.csproj -c Debug --filter CameraAcceptanceMod_HotpathHarness_TogglesPresentationLanes_AndWritesAcceptanceArtifacts --no-restore
dotnet test src/Tests/ThreeCTests/ThreeCTests.csproj -c Debug --filter FullyQualifiedName~CameraAcceptanceModTests --no-restore
```

## 4 结果

### 4.1 Hotpath harness 定向验证

`CameraAcceptanceMod_HotpathHarness_TogglesPresentationLanes_AndWritesAcceptanceArtifacts` 通过。

关键观察：

* baseline crowd = `256`
* restored crowd = `256`
* baseline visible crowd = `256`
* baseline world bars / text = `256 / 256`
* bars lane 关闭后，world bars 降为 `0`
* HUD text lane 关闭后，world text 与 screen text 同时降为 `0`
* crowd lane 关闭后，crowd / visible crowd / selection labels 同时降为 `0`

这说明 crowd gating 已经把之前的重复 enqueue 漂移收敛到稳定目标数，lane toggle 也没有串扰到不相关的 presentation buffer。

### 4.2 Battle-report 采样

本次执行生成的本地 artifact 位于 `artifacts/acceptance/presentation-hotpath-harness/`。battle-report 中可观察到以下样本：

* `baseline_all_on`: `Crowd=256/256 | Bars=256->256 | Text=256->256 | Labels=16 | Panel=ON | HUD=ON | Terrain=ON | Prims=ON | Cull=0.17ms | HudProj=0.18ms`
* `cull_crowd_off`: `Crowd=0/0 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=ON | HUD=ON | Terrain=OFF | Prims=OFF | Cull=0.09ms | HudProj=0.01ms`
* `restored_all_on`: `Crowd=256/256 | Bars=256->256 | Text=256->256 | Labels=16 | Panel=ON | HUD=ON | Terrain=ON | Prims=ON | Cull=0.23ms | HudProj=0.15ms`

结论：

* 关闭 lane 后，玩家可见内容与内部 sample 会一起下降。
* 恢复 lane 后，场景回到 baseline 形状，而不是残留中间状态。
* timing sample 会随 crowd / HUD 投影负载变化而变化，但这些数值属于观测证据，不应被写成硬性性能阈值。

### 4.3 CameraAcceptance 回归验证

`FullyQualifiedName~CameraAcceptanceModTests` 全部通过，本次执行结果为 `22/22`。

为避免 `CameraAcceptanceModTests` 在默认 test worker 下发生 fixture 级别的并行污染，本次同时把该 fixture 标记为 `NonParallelizable`，与其实际共享运行时初始化约束保持一致。

这说明 `#51` 新增的 hotpath harness 没有破坏以下既有 acceptance 面：

* projection / raycast / random scatter
* selection buffer 与 selection label
* panel reactive scene 稳定性
* RTS / TPS / Follow / Blend / Stack camera behavior

## 5 审计结论

本轮优化验证通过。

当前 `#51` 已经具备以下收敛结论：

1. shared hotpath harness 已从“只讨论”推进到“可重复执行的 runtime 验收”。
2. deterministic crowd gating 已消除 crowd overshoot 漂移。
3. live lane toggle 能同时服务手动验证与 headless acceptance。
4. 下一步缺口不再是技术 harness 本身，而是把同样的 lane 能力映射到玩家可感知的 playable mod 场景；该部分由 `docs/rfcs/RFC-0002-presentation-hotpath-playable-mods.md` 定义。

## 6 相关路径

* technical harness：`mods/fixtures/camera/CameraAcceptanceMod/`
* hotpath map：`mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_hotpath.json`
* crowd gating：`mods/fixtures/camera/CameraAcceptanceMod/Systems/CameraAcceptanceHotpathLaneSystem.cs`
* acceptance tests：`src/Tests/ThreeCTests/CameraAcceptanceModTests.cs`
* follow-up playable design RFC：`docs/rfcs/RFC-0002-presentation-hotpath-playable-mods.md`
