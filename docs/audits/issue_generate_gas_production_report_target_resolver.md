# Issue: GenerateGasProductionReport — MOBA TargetResolver 锥形查询返回 0 条 fan-out 命令

**类型**: Bug  
**优先级**: P0  
**测试**: `GasProductionFeatureReportTests.GenerateGasProductionReport`（当前已 [Ignore]）

## 现象

MOBA 场景中，`TargetResolverFanOutHelper.CollectFanOutTargets` 在 `ResolveTargets` 返回 ≥2 候选的情况下，`ValidateAndCollect` 产生 0 条 `FanOutCommand`。

- 失败步骤：`TargetResolver creates fan-out commands - Count=0 Dropped=0`
- 前置步骤 `TargetResolver ResolveTargets returns candidates` 通过，说明空间查询有结果
- 前置步骤 `SpatialQuery cone finds targets` 通过，说明 `engine.SpatialQueries.QueryCone` 正常

## 可能原因

1. `ValidateAndCollect` 中 RelationshipFilter / LayerMask / ExcludeSource 等过滤逻辑将候选全部过滤
2. 空间分区与实体同步时序（SpatialPartitionUpdateSystem）在测试场景下未正确填充
3. 无 Board 的 entry 地图使用引擎默认分区，与 HexGridBoard 分区行为不一致

## 复现

```bash
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~GenerateGasProductionReport"
```

移除 `[Ignore]` 后运行。

## 相关

- `src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs`
- `src/Mods/MobaDemoMod/assets/GAS/effects.json` — Effect.Moba.Damage.E (Cone)
- `docs/audits/final_merge_plan_20260304.md`
