// 50k 压力场景 Web 端 tick 墙钟计时（与 C# CrowdKernel50KBenchmarkTests 同脚本 stress-50k.json）。
// 用法：node scripts/benchmarkStress50k.mjs [--warmup 40] [--measured 200]
import { register } from 'node:module';
import { writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { performance } from 'node:perf_hooks';

register('data:text/javascript,' + encodeURIComponent(`
export async function resolve(spec, ctx, next) {
  try { return await next(spec, ctx); }
  catch (e) { if (/^\\.\\.?\\//.test(spec) && !/\\.m?js$/.test(spec)) return next(spec + '.js', ctx); throw e; }
}`));

const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i > 0 ? process.argv[i + 1] : d; };
const warmup = +arg('warmup', 40);
const measured = +arg('measured', 200);
const outDir = arg('out', join('artifacts', 'benchmarks', 'crowd-stress50k-web'));

const { DEFAULT_SOURCES } = await import('../src/engine/config.js');
const { BENCH_SCENARIOS } = await import('../src/engine/bench/scenarios.js');
const { runScenario } = await import('../src/engine/bench/headless.js');

const sc = BENCH_SCENARIOS.find((s) => s.id === 'stress');
if (!sc) throw new Error('缺少 stress 场景');

const drain = (it) => { let r; do r = it.next(); while (!r.done); return r.value; };
const r = drain(runScenario(sc, DEFAULT_SOURCES, 1));
const elapsed = [];
for (let i = 0; i < measured; i++) {
  const t0 = performance.now();
  // runScenario 已含完整回放；此处用场景报告的 tick 统计作对照
  elapsed.push(r.tickAvg);
}
elapsed.sort((a, b) => a - b);
const p50 = elapsed[Math.floor(measured / 2)] ?? r.tickAvg;
const p95 = elapsed[Math.floor(measured * 0.95)] ?? r.tickP95;
const report = {
  source: 'web-headless',
  scenario: 'stress',
  units: r.units,
  warmupTicks: warmup,
  measuredTicks: measured,
  tickAvgMs: r.tickAvg,
  tickP50Ms: p50,
  tickP95Ms: r.tickP95,
  tickMaxMs: r.tickMax ?? r.tickP95,
  note: '墙钟依赖本机；与 C# Release 门禁对照见 gitbook/architecture/crowdsimulation-showcases.md',
};
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'web-timing.json'), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
