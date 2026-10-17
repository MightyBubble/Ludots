// 从 Web 源码跑 fuzzIter 同序录制，导出 crowd-fuzz-trace-v1 逐 tick 逐单位 trace
// （单位：句柄/位置/状态机/组/玩家/层/指令/mode；导航版本；领队路径）。只读仿真状态，不改仿真语义。
// 正本在本仓库，运行于 Web 沙盒 scripts/ 下；对本地 Web 检出用 --web-src 指向其根目录。
// 用法：
//   node scripts/exportFuzzTrace.mjs --web-src . --seed 11111 --ops 40 --out parity/fuzz-trace-seed11111.json
import { register } from 'node:module';
import { createHash } from 'node:crypto';
import { mkdirSync, writeFileSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { pathToFileURL } from 'node:url';

register('data:text/javascript,' + encodeURIComponent(`
export async function resolve(spec, ctx, next) {
  try { return await next(spec, ctx); }
  catch (e) { if (/^\\.\\.?\\//.test(spec) && !/\\.m?js$/.test(spec)) return next(spec + '.js', ctx); throw e; }
}`));

const arg = (k) => { const i = process.argv.indexOf('--' + k); return i > 0 ? process.argv[i + 1] : undefined; };
const webSrc = process.env.CROWD_WEB_SRC || arg('web-src');
if (!webSrc) {
  console.error('缺少 Web 源码路径：设置环境变量 CROWD_WEB_SRC 或传入 --web-src');
  process.exit(1);
}

const seed = +(arg('seed') ?? 11111);
const opCount = +(arg('ops') ?? 40);
const outPath = arg('out');
if (!outPath) {
  console.error('缺少 --out 输出路径');
  process.exit(1);
}

// 爆炸参数来自 Web 沙盒调试工具表（ringScale/impulse）。仓库内运行可省略 --ring-scale/--impulse，
// 此时从 CrowdSimulationUiMod 的 CrowdSimulationDebug.json 读取；沙盒里没有 mods/ 树，必须显式传。
const ringScale = arg('ring-scale');
const impulse = arg('impulse');
let blast;
if (ringScale !== undefined && impulse !== undefined) {
  blast = { ringScale: +ringScale, impulse: +impulse };
} else {
  const toolsPath = arg('tools') ?? join('mods/capabilities/navigation/CrowdSimulationUiMod/assets/CrowdSimulationDebug.json');
  const tools = JSON.parse(readFileSync(toolsPath, 'utf8')).tools;
  blast = { ringScale: tools.ringScale, impulse: tools.impulse };
}

const imp = async (rel) => import(pathToFileURL(join(webSrc, rel)).href);
const { mulberry32 } = await imp('src/engine/noise.js');
const { detCos, detSin } = await imp('src/engine/core/detMath.js');
const { validateSim, validateStructures, validateNav, fuzzIter } = await imp('src/engine/debug.js');
const { unitChecksum } = await imp('src/engine/core/checksum.js');
const { DEFAULT_SOURCES, validateConfig } = await imp('src/engine/config.js');
const { createHeadless } = await imp('src/engine/bench/headless.js');

globalThis.__S4_FIX64_SPAWN__ = true;
const sources = structuredClone(DEFAULT_SOURCES);
sources.scenario.world.seed = 7;
const config = validateConfig(sources);
const { sim } = createHeadless(sources);

function positionStatL1Cm(sim) {
  const u = sim.units, n = u.count;
  let sum = 0;
  for (let i = 0; i < n; i++) {
    sum += Math.round(u.x[i] * 100) + Math.round(u.y[i] * 100);
  }
  return sum;
}

// 与 debug.js fuzzIter 同步的录制循环（结构须与 fuzzIter 一致），每个 sim.step 后抓一份只读快照。
function recordFuzz(sim, ops, blast, seed) {
  const rng = mulberry32(seed >>> 0);
  const log = [], ticks = [];
  const u = sim.units, size = sim.world.size;
  const tpls = sim.config.structures.templates;
  const pushDraw = (arr, v) => { arr.push((v * 4294967296) >>> 0); };
  const draw = (arr) => { const v = rng(); pushDraw(arr, v); return v; };
  const pickPoint = (arr) => {
    if (u.count && draw(arr) < 0.7) {
      const i = (draw(arr) * u.count) | 0;
      return [u.x[i], u.y[i]];
    }
    return [draw(arr) * size, draw(arr) * size];
  };
  const clampW = (v) => Math.min(sim.world.size - 1, Math.max(0, v));
  const captureTick = (opIndex) => {
    const n = u.count, units = new Array(n);
    for (let i = 0; i < n; i++) {
      units[i] = [u.id[i], u.x[i], u.y[i], u.state[i], u.group[i], u.player[i], u.level[i], u.order[i], u.mode[i]];
    }
    const navs = [...sim.planner.navs.values()].sort((a, b) => a.id - b.id).map((nav) => [nav.id, nav.version]);
    const leaders = [];
    for (const o of sim.orders.list) {
      for (let k = 0; k < o.leaders.length; k++) {
        const L = o.leaders[k], pts = new Array(L.path.length >> 1);
        for (let p = 0; p < L.path.length; p += 2) pts[p >> 1] = [L.path[p], L.path[p + 1]];
        leaders.push([o.id, k, L.x, L.y, L.seg, !!L.done, pts]);
      }
    }
    return { tick: sim.tickCount, op: opIndex, units, navs, leaders };
  };

  for (let k = 0; k < ops; k++) {
    const rngDraws = [];
    const op = draw(rngDraws);
    let name, params;
    if (op >= 0.88) {
      name = 'structure';
      const ids = [...sim.structures.footprint.keys()];
      if (ids.length && draw(rngDraws) < 0.4) {
        const fp = sim.structures.footprint.get(ids[(draw(rngDraws) * ids.length) | 0]);
        const x = fp.shape === 'path' ? fp.x0 : fp.x;
        const y = fp.shape === 'path' ? fp.y0 : fp.y;
        sim.submit({ type: 'remove', x, y });
        params = { action: 'remove', x, y };
        const sv = validateStructures(sim), nv = validateNav(sim);
        if (!sv.ok || !nv.ok) {
          log.push({ rngDraws, op: { kind: name, params }, units: u.count, validateOk: false, validateErrors: [...sv.errors, ...nv.errors], unitChecksumHex: unitChecksum(sim), positionStatL1Cm: positionStatL1Cm(sim) });
          return { ok: false, failedAt: k, steps: log, ticks };
        }
      } else {
        const tpl = tpls[(draw(rngDraws) * tpls.length) | 0];
        const [x, y] = pickPoint(rngDraws);
        const ang = draw(rngDraws) * Math.PI * 2;
        const len = 200 + draw(rngDraws) * 1500;
        const sz = tpl.footprint === 'path' ? 40 + draw(rngDraws) * 200 : tpl.footprint === 'disc' ? 80 + draw(rngDraws) * 500 : 60 + draw(rngDraws) * 800;
        const to = [clampW(x + detCos(ang) * len), clampW(y + detSin(ang) * len)];
        sim.submit({ type: 'place', template: tpl.id, x, y, size: sz, to });
        params = { action: 'place', template: tpl.id, x, y, size: sz, to };
        const sv = validateStructures(sim), nv = validateNav(sim);
        if (!sv.ok || !nv.ok) {
          log.push({ rngDraws, op: { kind: name, params }, units: u.count, validateOk: false, validateErrors: [...sv.errors, ...nv.errors], unitChecksumHex: unitChecksum(sim), positionStatL1Cm: positionStatL1Cm(sim) });
          return { ok: false, failedAt: k, steps: log, ticks };
        }
      }
    } else if (op < 0.3 && u.count) {
      name = 'explode';
      const [x, y] = pickPoint(rngDraws);
      const r = 40 + draw(rngDraws) * 400;
      const r2 = r * r;
      const probe = [];
      for (let t = 0; t < 32; t++) {
        const j = (draw(rngDraws) * u.count) | 0;
        const dx = u.x[j] - x, dy = u.y[j] - y;
        probe.push([u.id[j], dx * dx + dy * dy <= r2]);
      }
      sim.submit({ type: 'kill', x, y, radius: r, blast });
      for (const [h, dead] of probe) {
        const d = u.denseOf(h);
        if (dead ? d !== -1 : d < 0 || u.id[d] !== h) {
          log.push({ rngDraws, op: { kind: name, params: { x, y, radius: r, probe } }, units: u.count, validateOk: false, validateErrors: [`句柄 ${h} 解析错误`], unitChecksumHex: unitChecksum(sim), positionStatL1Cm: positionStatL1Cm(sim) });
          return { ok: false, failedAt: k, steps: log, ticks };
        }
      }
      params = { x, y, radius: r, probe };
    } else if (op < 0.55) {
      name = 'spawn';
      const [x, y] = pickPoint(rngDraws);
      const count = 1 + ((draw(rngDraws) * 300) | 0);
      const player = (draw(rngDraws) * sim.P) | 0;
      const unitType = (draw(rngDraws) * sim.U) | 0;
      const rIdx = (draw(rngDraws) * sim.R) | 0;
      sim.submit({ type: 'spawnAt', x, y, count, player, unitType, rIdx });
      params = { x, y, count, player, unitType, rIdx };
    } else {
      name = 'order';
      const p = (draw(rngDraws) * sim.P) | 0;
      const [x, y] = pickPoint(rngDraws);
      const [ax, ay] = pickPoint(rngDraws);
      sim.submit({ type: 'clearSelection' });
      const didSelect = draw(rngDraws) < 0.6;
      if (didSelect) sim.submit({ type: 'select', player: p, x0: ax - 600, y0: ay - 600, x1: ax + 600, y1: ay + 600, visible: null, additive: false });
      const ang = draw(rngDraws) * Math.PI * 2;
      const drag = draw(rngDraws) < 0.3;
      sim.submit({
        type: 'order', x, y, player: p, visible: null,
        opts: {
          shape: sim.config.formations[(draw(rngDraws) * sim.config.formations.length) | 0].id,
          auto: draw(rngDraws) < 0.8,
          face: drag ? [detCos(ang), detSin(ang)] : null,
          width: drag ? 100 + draw(rngDraws) * 800 : 0,
          fogTerrain: draw(rngDraws) < 0.5,
        },
      });
      params = { x, y, player: p, select: didSelect, ax, ay, drag };
    }
    sim.step(1 / 30);
    ticks.push(captureTick(k + 1));
    sim.step(1 / 30);
    ticks.push(captureTick(k + 1));
    const v = validateSim(sim);
    const row = {
      rngDraws,
      op: { kind: name, params },
      units: u.count,
      validateOk: v.ok,
      validateErrors: v.ok ? undefined : v.errors,
      unitChecksumHex: unitChecksum(sim),
      positionStatL1Cm: positionStatL1Cm(sim),
    };
    log.push(row);
    if (!v.ok) return { ok: false, failedAt: k, steps: log, ticks };
  }
  sim.submit({ type: 'clearSelection' });
  return { ok: true, steps: log, ticks };
}

const initialStructures = [...sim.structures.footprint.values()].map((fp) => (fp.shape === 'path' ? [fp.x0, fp.y0] : [fp.x, fp.y]));
const recorded = recordFuzz(sim, opCount, blast, seed);
if (!recorded.ok) {
  console.error(`录制 fuzz 在第 ${recorded.failedAt} 步失败`);
  process.exit(2);
}

// 交叉验证：独立 sim 跑官方 fuzzIter 应同样成功
globalThis.__S4_FIX64_SPAWN__ = true;
const { sim: sim2 } = createHeadless(sources);
const it = fuzzIter(sim2, opCount, blast, seed);
let last;
for (;;) {
  const n = it.next();
  if (n.done) { last = n.value; break; }
}
if (!last?.ok) {
  console.error('fuzzIter 验证失败', last);
  process.exit(3);
}

const contentSha256 = createHash('sha256')
  .update(JSON.stringify(recorded.steps))
  .update('\n')
  .update(JSON.stringify(recorded.ticks))
  .digest('hex');

const doc = {
  paritySchema: 'crowd-fuzz-trace-v1',
  seed,
  opCount,
  worldSeed: 7,
  tickHz: 30,
  blast,
  templateOrder: sim.config.structures.templates.map((t) => t.id),
  formationOrder: sim.config.formations.map((f) => f.id),
  initialStructures,
  generatedBy: 'scripts/exportFuzzTrace.mjs',
  generationCommand: `node scripts/exportFuzzTrace.mjs --web-src ${webSrc} --seed ${seed} --ops ${opCount} --out ${outPath}`,
  contentSha256,
  steps: recorded.steps,
  ticks: recorded.ticks,
};
mkdirSync(dirname(outPath), { recursive: true });
writeFileSync(outPath, JSON.stringify(doc));
console.log(`Wrote ${outPath} (${recorded.steps.length} steps, ${recorded.ticks.length} ticks, sha256 ${contentSha256.slice(0, 12)}…, fuzzIter ok, units=${last.units})`);
sim.dispose();
sim2.dispose();
