# CrowdSimulation Web 导出工具

正本在 Ludots 仓库；运行时把本目录同步到 CrowdSimulation Web 沙盒的 `scripts/`（与 `src/engine/` 同级），依赖沙盒内的引擎源码。

## exportLudots.mjs

把作者态场景导出为 Ludots Mod 资产与 `parity/*` 真值夹具。见文件头用法。

对拍口径（2026-10-09）：**行为对拍**，数值以 Ludots Fix64 为准；结构性字段逐位，派生量按带宽。

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

读 `stress-50k.json`（厘米、玩家 1..4），换成 Web 的米制和 0 基玩家后，用无头内核跑文件里的预热段和测量段。不读 `BENCH_SCENARIOS`。输出 JSON 供文档对照 C# `CrowdKernel50KBenchmarkTests`。

```bash
node scripts/benchmarkStress50k.mjs --scenario mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/stress-50k.json
```

脚本放在 Web 沙盒的 `scripts/`（与 `src/engine/` 同级）时，`--scenario` 仍指向 Ludots 仓库里的那份文件。
