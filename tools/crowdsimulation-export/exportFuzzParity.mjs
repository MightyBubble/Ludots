// 模糊测试双端对拍夹具：与 C# CrowdFuzzRunner 同种子、同 catalog.fuzz 参数、同逐步 op 抽样。
// 在 Web 沙盒根运行（与 exportLudots.mjs 相同布局）；写出 fuzz-parity-seed*.json。
// 用法：node scripts/exportFuzzParity.mjs --seed 11111 --ops 40 --out <path>
import { register } from 'node:module';
import { mkdirSync, writeFileSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';

register('data:text/javascript,' + encodeURIComponent(`
export async function resolve(spec, ctx, next) {
  try { return await next(spec, ctx); }
  catch (e) { if (/^\\.\\.?\\//.test(spec) && !/\\.m?js$/.test(spec)) return next(spec + '.js', ctx); throw e; }
}`));

const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i > 0 ? process.argv[i + 1] : d; };
const seed = +arg('seed', 11111);
const opCount = +arg('ops', 40);
const outPath = arg('out', join('fuzz-parity-seed' + seed + '.json'));

const { mulberry32, nextInt } = await import('./crowdFuzzRng.mjs');
const { DEFAULT_SOURCES, validateConfig } = await import('../src/engine/config.js');
const { createHeadless } = await import('../src/engine/bench/headless.js');
const { validateSim, validateStructures, validateNav } = await import('../src/engine/validate.js');
const { unitChecksum } = await import('../src/engine/core/checksum.js');

const catalogPath = arg('catalog', join('mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/catalog.json'));
const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));
const fuzz = catalog.fuzz;
const ops = fuzz.ops;
const plan = {
  stepsPerOp: fuzz.stepsPerOp,
  probeHandleCount: fuzz.probeHandles,
  spawnCount: fuzz.spawnCount,
  placeTemplate: fuzz.placeTemplate,
  placeSizeCm: fuzz.placeSizeCm,
  orderShape: fuzz.orderShape,
  orderMemberCap: fuzz.orderMemberCap,
  stallGuard: fuzz.stallGuard,
  boomRadiusCm: fuzz.boomRadiusCm,
  ringScale: fuzz.ringScale,
  impulse: fuzz.impulse,
  pathTemplate: fuzz.pathTemplate,
  pathWidthCm: fuzz.pathWidthCm,
  pathLengthCm: fuzz.pathLengthCm,
  ops,
};

const sources = structuredClone(DEFAULT_SOURCES);
sources.scenario.world.seed = 7;
const config = validateConfig(sources);
const { sim } = createHeadless(sources);
sim.truthNavFrozen = true;

const rng = mulberry32(seed >>> 0);
const drawable = ops;
const steps = [];
const sideCm = config.world.size; // Web 世界边长（厘米）

function execOp(opId) {
  switch (opId) {
    case 'spawn': {
      const player = config.deploy.bases[nextInt(rng, config.deploy.bases.length)].player;
      sim.submit({
        type: 'spawnAt',
        player,
        xCm: nextInt(rng, sideCm),
        yCm: nextInt(rng, sideCm),
        count: plan.spawnCount,
        unitType: nextInt(rng, config.agents.unitTypes.length),
        rIdx: nextInt(rng, config.agents.radiusClasses.length),
      });
      break;
    }
    case 'order': {
      const player = config.deploy.bases[nextInt(rng, config.deploy.bases.length)].player;
      sim.submit({
        type: 'order',
        player,
        xCm: nextInt(rng, sideCm),
        yCm: nextInt(rng, sideCm),
        shape: plan.orderShape,
        members: null,
        memberCap: plan.orderMemberCap,
      });
      break;
    }
    case 'place': {
      const margin = Math.floor(plan.placeSizeCm / 2);
      const span = Math.max(1, sideCm - margin * 2);
      const x = margin + nextInt(rng, span);
      const y = margin + nextInt(rng, span);
      sim.submit({
        type: 'place',
        template: plan.placeTemplate,
        xCm: x,
        yCm: y,
        sizeCm: plan.placeSizeCm,
        toXCm: x,
        toYCm: y,
      });
      break;
    }
    case 'remove':
      sim.submit({ type: 'remove', xCm: nextInt(rng, sideCm), yCm: nextInt(rng, sideCm) });
      break;
    case 'placePath': {
      const half = Math.floor(plan.pathWidthCm / 2);
      const lo = half;
      const hi = sideCm - half;
      const room = Math.max(1, hi - lo);
      const x0 = lo + nextInt(rng, room);
      const y = lo + nextInt(rng, room);
      const span = Math.min(plan.pathLengthCm, hi - x0);
      sim.submit({
        type: 'place',
        template: plan.pathTemplate,
        xCm: x0,
        yCm: y,
        sizeCm: plan.pathWidthCm,
        toXCm: x0 + span,
        toYCm: y,
      });
      break;
    }
    case 'removePath':
      // 与 C# 一致：随机选上层结构拆除；Web 用 remove 最近足迹近似由引擎处理
      sim.submit({ type: 'remove', xCm: nextInt(rng, sideCm), yCm: nextInt(rng, sideCm) });
      break;
    case 'kill': {
      const n = sim.units.count;
      let x;
      let y;
      if (n > 0) {
        const j = nextInt(rng, n);
        x = sim.units.x[j];
        y = sim.units.y[j];
      } else {
        x = nextInt(rng, sideCm);
        y = nextInt(rng, sideCm);
      }
      sim.submit({
        type: 'kill',
        xCm: x,
        yCm: y,
        radius: plan.boomRadiusCm,
        blast: { ringScale: plan.ringScale, impulse: plan.impulse },
      });
      break;
    }
    default:
      throw new Error(`未知 op ${opId}`);
  }
}

for (let k = 0; k < opCount; k++) {
  const opId = drawable[nextInt(rng, drawable.length)];
  execOp(opId);
  for (let s = 0; s < plan.stepsPerOp; s++) {
    sim.step(1 / 30);
  }
  const v = validateSim(sim);
  if (!v.ok) throw new Error(`第 ${k + 1} 步 validateSim: ${v.errors.join('; ')}`);
  if (opId === 'place' || opId === 'remove' || opId === 'placePath' || opId === 'removePath') {
    const sv = validateStructures(sim);
    const nv = validateNav(sim);
    if (!sv.ok || !nv.ok) throw new Error(`第 ${k + 1} 步结构/导航: ${[...sv.errors, ...nv.errors].join('; ')}`);
  }
  const digest = unitChecksum(sim);
  steps.push({
    opId,
    units: sim.units.count,
    stateDigest: `0x${(digest.state >>> 0).toString(16).padStart(16, '0')}`,
    positionDigest: `0x${(digest.position >>> 0).toString(16).padStart(16, '0')}`,
  });
}

const doc = {
  paritySchema: 'crowd-fuzz-parity-v1',
  seed,
  opCount,
  plan,
  steps,
};
mkdirSync(dirname(outPath), { recursive: true });
writeFileSync(outPath, JSON.stringify(doc, null, 2));
console.log(`Wrote ${outPath} (${steps.length} steps)`);
sim.dispose();
