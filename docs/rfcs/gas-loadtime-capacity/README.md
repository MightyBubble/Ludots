# GAS 装载期定容 — Benchmark 对比床

配套 RFC-0066。`benchmark-baseline.json` 是 **P0 迁移前**（实体内嵌 64 属性 / 256 标签）的固定场景基线。`benchmark-after-p1.json` 是 **P1 世界列存**（`AttributeBuffer` 行句柄 + `GasWorldColumnStore`）同参捕获。

## 复跑基线

```bash
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~Capture_LegacyEmbedded_BaselineReport"
```

输出在测试 `WorkDirectory/gas-capacity-benchmark/`。若场景参数未变且需刷新入库基线，将捕获 JSON 覆盖本目录 `benchmark-baseline.json`（须在 Epic 留痕）。

## 对比迁移后

1. 跑 `Capture_AfterP1_WorldStoreReport`（写入本目录 `benchmark-after-p1.json`）。
2. 用 `GasCapacityBenchmarkReport.Compare(baseline, after)`（默认 10% 阈值；热路径分配只许不增）。
3. 未解释回归不得合入；已解释项见 `benchmark-regression-notes.md`。

### Production freeze hook（P1 状态）

尚无「登记窗口关闭」的单一生产钩子：`AttributeRegistry.Register` 仍会在模板加载时发生。P1 暂用 `GasLoadTimeCapacitySession.EnsureLegacyPlanAndStore()`（GameEngine 启动与测试）绑定 **legacy 64/256** 计划与世界列存。完整 `FreezeFromRegistries` + `EnsureStore` 应挂在全部 Mod/配置登记完成之后、首个 gameplay 生成之前（候选：`SchemaUpdate` 首帧或 ConfigPipeline 完成后的显式引擎步骤）——P2 前补齐，禁止再发明平行管线。

MetricId 合同见 RFC §3.4。
