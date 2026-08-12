# GAS 装载期定容 — Benchmark 对比床

配套 RFC-0066。`benchmark-baseline.json` 是 **P0 迁移前**（当前实体内嵌 64 属性 / 256 标签）的固定场景基线。

## 复跑基线

```bash
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~Capture_LegacyEmbedded_BaselineReport"
```

输出在测试 `WorkDirectory/gas-capacity-benchmark/`。若场景参数未变且需刷新入库基线，将捕获 JSON 覆盖本目录 `benchmark-baseline.json`（须在 Epic 留痕）。

## 对比迁移后

1. 跑同一套 `GasCapacityBenchmarkCompareTests` 捕获 `phase=after-pN` 报告。
2. 用 `GasCapacityBenchmarkReport.Compare(baseline, after)`（默认 10% 阈值；热路径分配只许不增）。
3. 未解释回归不得合入。

MetricId 合同见 RFC §3.4。
