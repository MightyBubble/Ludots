# CrowdSimulation Web 导出工具

正本在 Ludots 仓库；运行时把本目录同步到 CrowdSimulation Web 沙盒的 `scripts/`（与 `src/engine/` 同级），依赖沙盒内的引擎源码。

## exportLudots.mjs

把作者态场景导出为 Ludots Mod 资产与 `parity/*` 真值夹具。见文件头用法。

对拍口径（2026-10-09）：**行为对拍**，数值以 Ludots Fix64 为准；结构性字段逐位，派生量按带宽。

## exportFuzzParity.mjs

在 Node 无头环境跑与 C# `CrowdFuzzRunner` 同算法的模糊序列（种子与 `regress/catalog.json` 的 `fuzz` 段一致），写出 `fuzz-parity-seed<seed>.json` 夹具。

```bash
# 在 Web 沙盒根目录（含 src/engine）：
node scripts/exportFuzzParity.mjs --seed 11111 --ops 40 \
  --out mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/fuzz-parity-seed11111.json
```

夹具字段：`paritySchema`、`seed`、`opCount`、`plan`、`steps[]`（每步 `opId`、`units`、`stateDigest`、`positionDigest`）。

重新生成后跑 C# 对拍：

```bash
dotnet build mods/LudotsCoreMod -c Release
dotnet test src/Tests/CrowdSimulationTests -c Release \
  --filter "FullyQualifiedName~CrowdFuzzParityTests.Seed11111_RecordedOps_MatchWebFixture"
```

## benchmarkStress50k.mjs

与 `stress-50k.json` 同场景，在 Web 无头内核测每 tick 墙钟耗时（中位 / p95），输出 JSON 供文档对照 C# `CrowdKernel50KBenchmarkTests`。

```bash
node scripts/benchmarkStress50k.mjs --warmup 40 --measured 200
```
