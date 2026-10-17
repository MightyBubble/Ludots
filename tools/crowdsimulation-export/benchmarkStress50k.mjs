// 50k 压力场景 Web 端 tick 墙钟计时。
// 读 Ludots 的 stress-50k.json（厘米、玩家 1..4），换算成 Web 指令后用无头内核跑。
// 不使用 src/engine/bench/scenarios.js 的 BENCH_SCENARIOS。
// 用法：node scripts/benchmarkStress50k.mjs --scenario <stress-50k.json> [--warmup 40] [--measured 200]
import { register } from 'node:module';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';

register('data:text/javascript,' + encodeURIComponent(`
export async function resolve(spec, ctx, next) {
  try { return await next(spec, ctx); }
  catch (e) { if (/^\\.\\.?\\//.test(spec) && !/\\.m?js$/.test(spec)) return next(spec + '.js', ctx); throw e; }
}`));

const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i > 0 ? process.argv[i + 1] : d; };
const scenarioPath = arg('scenario', '');
if (!scenarioPath) throw new Error('必须 --scenario 指向 stress-50k.json');
if (!scenarioPath.endsWith('stress-50k.json')) throw new Error(`场景文件名不是 stress-50k.json：${scenarioPath}`);

const scenarioText = readFileSync(scenarioPath, 'utf8');
const doc = JSON.parse(scenarioText);
const sha256 = createHash('sha256').update(scenarioText).digest('hex');
const warmup = doc.warmupTicks;
const measured = doc.measuredTicks;
const argWarmup = arg('warmup', String(warmup));
const argMeasured = arg('measured', String(measured));
if (Number(argWarmup) !== warmup || Number(argMeasured) !== measured) {
  throw new Error(`命令行 warmup/measured（${argWarmup}/${argMeasured}）与文件（${warmup}/${measured}）不一致`);
}
if (doc.ticks !== warmup + measured) throw new Error('ticks 必须等于预热加测量');

const webScript = doc.script.map((entry) => {
  const c = entry.cmd;
  if (c.type === 'spawn') {
    return { tick: entry.tick, cmd: { type: 'spawn', count: c.count } };
  }
  if (c.type === 'order') {
    return {
      tick: entry.tick,
      cmd: {
        type: 'order',
        player: c.player - 1,
        x: c.xCm / 100,
        y: c.yCm / 100,
        opts: { shape: c.shape ?? 'box' },
      },
    };
  }
  throw new Error(`stress-50k.json 含未换算的指令 ${c.type}`);
});

const outDir = arg('out', join('artifacts', 'benchmarks', 'crowd-stress50k-web'));
const readText = (file) => readFileSync(file, 'utf8').trim();
const loadavg = () => readText('/proc/loadavg');
const memAvailableKb = () => {
  const line = readText('/proc/meminfo').split('\n').find((row) => row.startsWith('MemAvailable:'));
  return Number(line.split(/\s+/)[1]);
};
const uname = execFileSync('uname', ['-srm'], { encoding: 'utf8' }).trim();
const nproc = Number(execFileSync('nproc', { encoding: 'utf8' }).trim());

const { DEFAULT_SOURCES } = await import('../src/engine/config.js');
const { createHeadless } = await import('../src/engine/bench/headless.js');
const { createProfile, profileTick } = await import('../src/engine/bench/benchProfile.js');

const loadBefore = loadavg();
const memBeforeKb = memAvailableKb();
const { sim, bakeMs } = createHeadless(DEFAULT_SOURCES);
sim.commands.schedule(webScript);
const prof = createProfile(sim, doc.ticks);
let loadAtWindow = loadBefore;
for (let t = 0; t < doc.ticks; t++) {
  if (t === warmup) loadAtWindow = loadavg();
  profileTick(sim, prof, t);
}
const loadAfter = loadavg();
const units = sim.units.count;
sim.dispose();
if (units !== doc.units) throw new Error(`单位数 ${units}，文件要求 ${doc.units}`);

const samples = Array.from(prof.total).slice(warmup, warmup + measured);
const sorted = samples.slice().sort((a, b) => a - b);
const avg = samples.reduce((a, b) => a + b, 0) / samples.length;
const p50 = sorted[Math.floor(samples.length / 2)];
const p95 = sorted[Math.floor(samples.length * 0.95)];
const max = sorted[sorted.length - 1];
const report = {
  source: 'web-headless',
  scenarioFile: scenarioPath,
  scenarioSha256: sha256,
  scenarioId: 'stress-50k.json',
  benchScenarios: false,
  units,
  ticks: doc.ticks,
  warmupTicks: warmup,
  measuredTicks: measured,
  tickAvgMs: avg,
  tickP50Ms: p50,
  tickP95Ms: p95,
  tickMaxMs: max,
  bakeMs,
  translatedScript: webScript,
  uname,
  nproc,
  loadBefore,
  loadAtWindow,
  loadAfter,
  memAvailableKbBefore: memBeforeKb,
  at: new Date().toISOString(),
};
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'web-timing.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
