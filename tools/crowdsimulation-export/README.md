# CrowdSimulation Web 导出工具

正本在 Ludots 仓库；运行时把本目录同步到 CrowdSimulation Web 沙盒的 `scripts/`（与 `src/engine/` 同级），依赖沙盒内的引擎源码。

## exportLudots.mjs

把 Web 作者态场景导出成 Ludots 地形演示图能装载的资产，并写出 S1 及之后的对拍夹具。运行时把本文件放到 Web 沙盒的 `scripts/`（与 `src/engine/` 同级）。

```bash
node scripts/exportLudots.mjs --seed 7 --map-id crowd_simulation_s7 --out exports/crowd_simulation_s7 --only map
```

`--only map` 只写这些文件，不跑 S2 及之后的导航对拍：

- `terrain/<mapId>.height`（CHTM v2）
- `Maps/<mapId>.json`
- `Navigation/navmesh.json`（`solver` 为 `crowd-simulation`：`logicTerrainAsset`、`navCellSizeCm`、`terrainTypes`、`linkCandidateBake`）
- `CrowdSimulationConfig.json`（`world.seed`、`deploy.initialUnits` 为 0、出生点、`s7barrier`）
- `parity/s1-truth.json`

地图里的建筑和桥按 Web `structures.footprint` 的插入序写。生成器先放完全部建筑，再放桥；C# 结构仓也是先给阻挡编号、再给条带续号，所以文件里建筑整段在前、桥整段在后，两段内部都不按坐标排序。桥的跨度是 `CrowdSimulationPathSpan`（`fromXCm`、`fromYCm`、`toXCm`、`toYCm`、`widthCm`）。

`.navsurface` 不由本脚本写。加载器要头长 30 字节的链接候选布局，用地编网格重烘：

```bash
dotnet run --project src/Tools/Ludots.Tool -c Release -- nav bake-crowd-navsurface --mapId crowd_simulation_s7 --modId CrowdSimulationS1Terrain7Mod --out mods/showcases/crowd_simulation/CrowdSimulationS1Terrain7Mod/assets/terrain/crowd_simulation_s7.navsurface --overwrite
```

地编网格 `terrain/<mapId>.map_data.bin` 也不在本脚本里生成。

对拍口径（2026-10-09）：行为对拍，数值以 Ludots Fix64 为准；结构性字段逐位，派生量按带宽。

## exportFuzzParity.mjs

在 Node 无头环境跑 Web `fuzzIter`（`src/engine/debug.js`）同算法的录制循环，写出 `fuzz-parity-seed<seed>.json` 夹具，C# 端由 `CrowdWebFuzzRunner` 重放。

```bash
CROWD_WEB_SRC=<Web 源码根目录> node tools/crowdsimulation-export/exportFuzzParity.mjs --seed 11111 --ops 40 \
  --out mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/fuzz-parity-seed11111.json
```

夹具字段（`paritySchema` = `crowd-fuzz-parity-v3`）：

- `seed`、`opCount`、`blast`
- `templateOrder`、`formationOrder`：Web 按数组下标抽结构模板和阵型。C# 运行时表按 id 排序，还可能多出地图专属模板，所以按这里的 id 序抽，再按 id 解析
- `initialStructures`：开局结构锚点 `[x, y]`（米；矩形取中心，路径取起点），按 Web 结构表迭代序。拆除时按这个序抽，C# 端按锚点对到结构仓实体
- `steps[]`：每步 `rngDraws`、`op`（`kind` + `params`）、`units`、`validateOk`、`validateErrors`、`unitChecksumHex`、`positionStatL1Cm`

重新生成后跑 C# 对拍：

```bash
dotnet test src/Tests/CrowdSimulationTests --filter "FullyQualifiedName~CrowdFuzzParityTests"
```

每个种子只比前若干步（见 `CrowdFuzzParityTests.Fixtures` 的 `ComparedSteps`），再往后两端在指令规划上分叉。

## benchmarkStress50k.mjs

与 `stress-50k.json` 同场景，在 Web 无头内核测每 tick 墙钟耗时（中位 / p95），输出 JSON 供文档对照 C# `CrowdKernel50KBenchmarkTests`。

```bash
node scripts/benchmarkStress50k.mjs --warmup 40 --measured 200
```
