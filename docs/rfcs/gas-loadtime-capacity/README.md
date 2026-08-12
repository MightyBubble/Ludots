# GAS 装载期定容 — Benchmark 对比床

配套 RFC-0066。`benchmark-baseline.json` 是 **P0 迁移前**（实体内嵌 64 属性 / 256 标签）的固定场景基线。`benchmark-after-p1.json` 是属性世界列存捕获；`benchmark-after-p2.json` 是标签世界位列 + 共享行句柄捕获。

## 复跑基线

```bash
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~Capture_LegacyEmbedded_BaselineReport"
```

输出在测试 `WorkDirectory/gas-capacity-benchmark/`。若场景参数未变且需刷新入库基线，将捕获 JSON 覆盖本目录 `benchmark-baseline.json`（须在 Epic 留痕）。

## 对比迁移后

1. 跑 `Capture_AfterP2_WorldStoreReport`（写入本目录 `benchmark-after-p2.json`）。
2. 用 `GasCapacityBenchmarkReport.Compare(baseline, after)`（默认 10% 阈值；热路径分配只许不增）。
3. 未解释回归不得合入；已解释项见 `benchmark-regression-notes.md`。

### Production freeze hook

登记窗口关闭点：`GameEngine.InitializeWithConfigPipelineInternal` 在全部 ConfigPipeline 装载器完成之后、首个 gameplay 实体（相机 `AttributeBuffer` 目标）创建之前，调用 `FreezeEnsureStoreAndSealFromRegistries()`（`FreezeFromRegistries` + `EnsureStore` + `SealGameplay`）。测试预绑定用 `EnsureLegacyPlanAndStoreForTests`。

MetricId 合同见 RFC §3.4。
