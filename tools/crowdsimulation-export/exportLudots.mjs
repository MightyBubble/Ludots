// 导出为 Ludots Mod（LU-21 / AU-07 / AU-08）：把作者态测试场景生成结果写成 Ludots 资产文件。
// 产物：.height（CHTM v2，uint16 缩放）、.navsurface（v1）、Maps/<id>.json（队伍 / 关系 / 阻挡物 / 桥）、
//       CrowdSimulationConfig.json 覆盖片段、parity/s1-truth.json（FNV-1a 摘要，供 C# 端逐格对拍）。
// 正本在 Ludots 仓库（本文件即正本）；运行时位于 CrowdSimulation Web 沙盒的 scripts/ 下，
// 依赖沙盒内 src/engine/ 的生成器（相对导入按该布局解析）。
// 用法：node scripts/exportLudots.mjs --seed 1337 --map-id crowd_simulation_s1 --out exports/crowd_simulation_s1
import { register } from 'node:module';

register('data:text/javascript,' + encodeURIComponent(`
export async function resolve(spec, ctx, next) {
  try { return await next(spec, ctx); }
  catch (e) { if (/^\\.\\.?\\//.test(spec) && !/\\.m?js$/.test(spec)) return next(spec + '.js', ctx); throw e; }
}`));

import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i > 0 ? process.argv[i + 1] : d; };
const seed = +arg('seed', 1337);
const mapId = arg('map-id', `crowd_simulation_s${seed}`);
const outRoot = arg('out', join('exports', mapId));
for (const sub of ['terrain', 'Maps', 'parity']) mkdirSync(join(outRoot, sub), { recursive: true });

const { DEFAULT_SOURCES, validateConfig } = await import('../src/engine/config.js');
const { generateWorld } = await import('../src/engine/terrain.js');
const { generateStructures } = await import('../src/engine/structures/mapGen.js');
const { personalRadius } = await import('../src/engine/core/space.js');
const { entityList } = await import('../src/engine/structures/store.js');

const sources = structuredClone(DEFAULT_SOURCES);
sources.scenario.world.seed = seed;
const config = validateConfig(sources);
const world = generateWorld(config);
const reach = Math.max(...config.agents.radiusClasses.map((_, r) => personalRadius(config, r)));
const structures = generateStructures(world, config, reach);

// ───────────────────────────── 小端二进制写出器 ─────────────────────────────
class BW {
  constructor() { this.parts = []; }
  u8(v) { this.parts.push(Buffer.from([v & 0xff])); }
  u16(v) { const b = Buffer.alloc(2); b.writeUInt16LE(v); this.parts.push(b); }
  i32(v) { const b = Buffer.alloc(4); b.writeInt32LE(v); this.parts.push(b); }
  u32(v) { const b = Buffer.alloc(4); b.writeUInt32LE(v >>> 0); this.parts.push(b); }
  f32(v) { const b = Buffer.alloc(4); b.writeFloatLE(v); this.parts.push(b); }
  bytes(buf) { this.parts.push(Buffer.from(buf)); }
  // .NET BinaryWriter.Write(string)：7 位变长字节数 + UTF-8
  netString(s) {
    const b = Buffer.from(s, 'utf8');
    let n = b.length;
    while (n >= 0x80) { this.u8((n & 0x7f) | 0x80); n >>= 7; }
    this.u8(n & 0x7f);
    this.bytes(b);
  }
  build() { return Buffer.concat(this.parts); }
}

// ───────────────────────────── .navsurface v1 ─────────────────────────────
const N = world.N, cellSizeCm = Math.round(world.cellSize * 100);
const terrainIds = config.terrainTypes.map((t) => t.id);
{
  const w = new BW();
  w.u32(0x46534e4c); // "LNSF"
  w.u16(1); // version
  w.u16(32); // headerBytes
  w.i32(N); w.i32(N); w.i32(cellSizeCm);
  w.i32(world.jumps.count);
  w.u16(terrainIds.length); w.u16(0); w.i32(0);
  for (const id of terrainIds) { const b = Buffer.from(id, 'utf8'); w.u16(b.length); w.bytes(b); }
  w.bytes(Buffer.from(world.type.buffer, world.type.byteOffset, N * N));
  for (let k = 0; k < world.jumps.count; k++) {
    const a = world.jumps.a[k], b = world.jumps.b[k];
    w.u16(a % N); w.u16((a / N) | 0); w.u16(b % N); w.u16((b / N) | 0);
    w.i32(Math.round(world.jumps.drop[k] * 100));
    w.f32(world.jumps.len[k]);
  }
  writeFileSync(join(outRoot, 'terrain', `${mapId}.navsurface`), w.build());
}

// ───────────────────────────── .height（CHTM v2） ─────────────────────────────
const B = world.B, HS = world.heightScale;
let heightRaw, heightOffsetCm, heightSpanCm;
{
  const count = B * B;
  let mn = Infinity, mx = -Infinity;
  for (let i = 0; i < count; i++) {
    const cm = world.heightBake[i] * HS * 100;
    if (cm < mn) mn = cm;
    if (cm > mx) mx = cm;
  }
  const offsetCm = Math.floor(mn), spanCm = Math.max(1, Math.ceil(mx) - offsetCm);
  const raw = Buffer.alloc(count * 2);
  for (let i = 0; i < count; i++) {
    const cm = world.heightBake[i] * HS * 100;
    raw.writeUInt16LE(Math.min(65535, Math.max(0, Math.round(((cm - offsetCm) * 65535) / spanCm))), i * 2);
  }
  heightRaw = raw; heightOffsetCm = offsetCm; heightSpanCm = spanCm;
  const w = new BW();
  w.bytes(Buffer.from('CHTM', 'ascii'));
  w.i32(2); // version
  const worldCm = world.size * 100;
  w.i32(0); w.i32(0); w.i32(worldCm); w.i32(worldCm); // bounds
  w.i32(B); w.i32(B); // sampleColumns / sampleRows
  w.i32(2); // RowMajorUInt16Scaled
  w.i32(0); // defaultLayerIndex
  w.i32(1); // TriangleHeightfield
  w.i32(offsetCm); w.i32(spanCm); w.i32(65535); // sampleScale: cm = offset + raw * span / 65535
  w.i32(1); // layerCount
  w.i32(0); w.netString('base'); w.i32(0); w.i32(count); // layer 0
  w.i32(count);
  w.bytes(raw);
  writeFileSync(join(outRoot, 'terrain', `${mapId}.height`), w.build());
}

// ───────────────────────────── 地图实体（阻挡物 / 桥） ─────────────────────────────
const cm = (m) => Math.round(m * 100);
const tplOf = (t) => config.structures.templates[t];
const blockers = [], bridges = [];
for (const e of entityList(structures)) {
  const tpl = tplOf(e.tpl);
  if (tpl.blocker) blockers.push({ xCm: cm(e.fp.x), yCm: cm(e.fp.y), sizeCm: cm(e.fp.hx * 2) });
  else if (tpl.layered) bridges.push({ x0Cm: cm(e.fp.x0), y0Cm: cm(e.fp.y0), x1Cm: cm(e.fp.x1), y1Cm: cm(e.fp.y1), widthCm: cm(e.fp.w) });
}
blockers.sort((a, b) => a.xCm - b.xCm || a.yCm - b.yCm || a.sizeCm - b.sizeCm);
bridges.sort((a, b) => a.x0Cm - b.x0Cm || a.y0Cm - b.y0Cm || a.x1Cm - b.x1Cm || a.y1Cm - b.y1Cm);

const entities = [
  { InstanceId: 'team_1', Template: 'crowd_simulation_team' },
  { InstanceId: 'team_2', Template: 'crowd_simulation_team' },
  ...config.players.map((p, i) => ({ InstanceId: `player_${i + 1}`, Template: 'crowd_simulation_player' })),
  ...blockers.map((b, i) => ({
    InstanceId: `building_${String(i + 1).padStart(4, '0')}`,
    Template: `crowd_simulation_blocker_${b.sizeCm / 100}m`,
    Overrides: { WorldPositionCm: { Value: { X: b.xCm, Y: b.yCm } } },
  })),
  ...bridges.map((b, i) => ({
    InstanceId: `bridge_${String(i + 1).padStart(4, '0')}`,
    Template: 'crowd_simulation_bridge',
    Overrides: {
      CrowdSimulationBridgeSpan: { x0Cm: b.x0Cm, y0Cm: b.y0Cm, x1Cm: b.x1Cm, y1Cm: b.y1Cm, widthCm: b.widthCm },
    },
  })),
];

const worldCm = world.size * 100;
// 绝对高程着色的海平面与峰跨度用真实数据算（默认 36 m 跨度会把 1400 m 地形整个饱和成单色）
const seaLevelCm = Math.round(world.thresholds.seaH * HS * 100);
const peakSpanCm = Math.max(100, Math.round((1 - world.thresholds.seaH) * HS * 100));
const mapJson = {
  Id: mapId,
  Tags: ['crowd_simulation'],
  Tuning: { LoadedChunkCapacity: 256 },
  Boards: [{
    Name: 'default', SpatialType: 'Grid', WidthCm: worldCm, HeightCm: worldCm,
    Anchor: { LocalXCm: 0, LocalYCm: 0, WorldXCm: 0, WorldYCm: 0 }, Grid: { CellSizeCm: 100 },
  }],
  ContinuousHeightmap: {
    Asset: `assets/terrain/${mapId}.height`,
    RenderProfile: {
      OverviewSwitchChunkSpans: 2.5,
      OverviewVertexLimit: 65536,
      ChunkLodErrorPx: 240,
      DisableDistanceFog: true,
      DisplayHeightScale: 1.0,
      SeaLevelCm: seaLevelCm,
      AbsoluteColorPeakSpanCm: peakSpanCm,
    },
  },
  DefaultCamera: { VirtualCameraId: 'CrowdSimulation.Camera.Terrain', TargetXCm: worldCm / 2, TargetYCm: worldCm / 2, Yaw: 45, Pitch: 55, DistanceCm: Math.round(worldCm * 1.2), FovYDeg: 50 },
  Teams: [{ TeamId: 1, RepresentativeInstanceId: 'team_1' }, { TeamId: 2, RepresentativeInstanceId: 'team_2' }],
  Players: config.players.map((p, i) => ({ PlayerId: i + 1, TeamId: p.team === 'A' ? 1 : 2, RepresentativeInstanceId: `player_${i + 1}` })),
  ParticipantRelationships: { Teams: [{ TeamA: 1, TeamB: 2, TypeId: 'LudotsCore.Participant', Attitude: 'Hostile', Symmetric: true }] },
  Entities: entities,
};
writeFileSync(join(outRoot, 'Maps', `${mapId}.json`), JSON.stringify(mapJson, null, 2));

// ───────────────────────────── CrowdSimulationConfig 覆盖片段 ─────────────────────────────
writeFileSync(join(outRoot, 'CrowdSimulationConfig.json'), JSON.stringify({
  version: 7,
  mapId,
  world: { surfaceAsset: `terrain/${mapId}.navsurface` },
}, null, 2));

// ───────────────────────────── S1 对拍摘要（FNV-1a 32） ─────────────────────────────
// 字节流契约（C# 端按同一顺序重建）：
//   "LS1T" · i32 N · i32 cellSizeCm · u16 类型数 · 类型 id 表（u16 长 + utf8） · N² 类型栅格
//   · N² 区域栅格（地形映射后） · N² 阻挡栅格 · i32 阻挡数 · 逐阻挡 i32 xCm/yCm/sizeCm（已排序）
//   · i32 桥数 · 逐桥 i32 x0/y0/x1/y1/widthCm（已排序）
//   · i32 跳跃候选数 · 逐候选 u16 fromX/fromY/toX/toY · i32 dropCm · f32 lenCells
function fnv1a(buf) {
  let h = 2166136261;
  for (const b of buf) { h ^= b; h = Math.imul(h, 16777619); }
  return (h >>> 0).toString(16).padStart(8, '0');
}
{
  const w = new BW();
  w.bytes(Buffer.from('LS1T', 'ascii'));
  w.i32(N); w.i32(cellSizeCm);
  w.u16(terrainIds.length);
  for (const id of terrainIds) { const b = Buffer.from(id, 'utf8'); w.u16(b.length); w.bytes(b); }
  w.bytes(Buffer.from(world.type.buffer, world.type.byteOffset, N * N));
  w.bytes(Buffer.from(structures.area.buffer, structures.area.byteOffset, N * N));
  w.bytes(Buffer.from(structures.blocked.buffer, structures.blocked.byteOffset, N * N));
  w.i32(blockers.length);
  for (const b of blockers) { w.i32(b.xCm); w.i32(b.yCm); w.i32(b.sizeCm); }
  w.i32(bridges.length);
  for (const b of bridges) { w.i32(b.x0Cm); w.i32(b.y0Cm); w.i32(b.x1Cm); w.i32(b.y1Cm); w.i32(b.widthCm); }
  w.i32(world.jumps.count);
  for (let k = 0; k < world.jumps.count; k++) {
    const a = world.jumps.a[k], b = world.jumps.b[k];
    w.u16(a % N); w.u16((a / N) | 0); w.u16(b % N); w.u16((b / N) | 0);
    w.i32(Math.round(world.jumps.drop[k] * 100));
    w.f32(world.jumps.len[k]);
  }
  const bytes = w.build();
  writeFileSync(join(outRoot, 'parity', 's1-truth.json'), JSON.stringify({
    mapId, seed, navCells: N, cellSizeCm,
    blockers: blockers.length, bridges: bridges.length, jumpCandidates: world.jumps.count,
    fnv1a: fnv1a(bytes),
  }, null, 2));
}

console.log(`[export] ${mapId} seed=${seed} cells=${N}x${N} blockers=${blockers.length} bridges=${bridges.length} jumps=${world.jumps.count} → ${outRoot}`);

// ───────────────────────────── S2:逐导航上下文真相（量化高度输入） ─────────────────────────────
// 两端读同一份量化高度（14.1）：navHeight / slope 从写出的 uint16 样本重建，再跑真实烘焙。
const { buildNavContext, navIdOf } = await import('../src/engine/nav.js');
const { TileCache } = await import('../src/engine/navtile/tileCache.js');
const { clearanceOf } = await import('../src/engine/core/space.js');

const worldQ = Object.assign({}, world);
// 跳跃候选同样换成量化后的 .navsurface 落盘值（drop 四舍五入到厘米;len 已是 f32）
{
  const n = world.jumps.count;
  const q = { count: n, a: new Int32Array(n), b: new Int32Array(n), len: new Float32Array(n), drop: new Float32Array(n) };
  for (let k = 0; k < n; k++) {
    q.a[k] = world.jumps.a[k]; q.b[k] = world.jumps.b[k];
    q.len[k] = Math.fround(world.jumps.len[k]);
    q.drop[k] = Math.round(world.jumps.drop[k] * 100) / 100;
  }
  worldQ.jumps = q;
}
{
  const Bq = world.B, Nq = world.N, cellSizeM = world.cellSize;
  const cmQ = new Float64Array(Bq * Bq);
  for (let i = 0; i < Bq * Bq; i++) cmQ[i] = (heightOffsetCm + (heightRaw.readUInt16LE(i * 2) * heightSpanCm) / 65535) / 100;
  const navHeight = new Float64Array(Nq * Nq);
  for (let y = 0; y < Nq; y++) {
    const by0 = Math.floor((y * Bq) / Nq), by1 = Math.max(by0 + 1, Math.floor(((y + 1) * Bq) / Nq));
    for (let x = 0; x < Nq; x++) {
      const bx0 = Math.floor((x * Bq) / Nq), bx1 = Math.max(bx0 + 1, Math.floor(((x + 1) * Bq) / Nq));
      let sum = 0, cnt = 0;
      for (let by = by0; by < by1; by++) for (let bx = bx0; bx < bx1; bx++) { sum += cmQ[by * Bq + bx]; cnt++; }
      navHeight[y * Nq + x] = sum / cnt;
    }
  }
  const slope = new Float64Array(Nq * Nq), k2 = 1 / (2 * cellSizeM);
  for (let y = 0; y < Nq; y++) for (let x = 0; x < Nq; x++) {
    const xl = Math.max(0, x - 1), xr = Math.min(Nq - 1, x + 1), yu = Math.max(0, y - 1), yd = Math.min(Nq - 1, y + 1);
    const gx = (navHeight[y * Nq + xr] - navHeight[y * Nq + xl]) * k2;
    const gy = (navHeight[yd * Nq + x] - navHeight[yu * Nq + x]) * k2;
    slope[y * Nq + x] = Math.sqrt(gx * gx + gy * gy);
  }
  worldQ.navHeight = navHeight; worldQ.slope = slope;
}

// 分区摘要用规范化标签（按连通域最小格号排序编号），跨语言只比较划分不比较标签编号
function canonComp(comp, n2) {
  const minCell = new Map();
  for (let i = 0; i < n2; i++) {
    const c = comp[i];
    if (c < 0) continue;
    const m = minCell.get(c);
    if (m === undefined || i < m) minCell.set(c, i);
  }
  const comps = [...minCell.entries()].sort((a, b) => a[1] - b[1]);
  const rank = new Map(comps.map((e, i) => [e[0], i]));
  const out = new Int32Array(n2).fill(-1);
  for (let i = 0; i < n2; i++) if (comp[i] >= 0) out[i] = rank.get(comp[i]);
  return out;
}

{
  const n2 = world.N * world.N;
  const tileCache = new TileCache(config.navtile.cacheCapacity, config.hpa.clusterSize, config.navmesh);
  const seen = new Set(), contexts = [];
  for (let a = 0; a < config.agentTypes.length; a++) {
    for (let r = 0; r < config.agents.radiusClasses.length; r++) {
      const c = clearanceOf(config, r), id = navIdOf(a, c);
      if (seen.has(id)) continue;
      seen.add(id);
      contexts.push({ a, clearance: c, id, nav: buildNavContext(worldQ, config, a, c, structures, tileCache) });
    }
  }

  const w = new BW();
  w.bytes(Buffer.from('LS2T', 'ascii'));
  w.i32(world.N); w.i32(contexts.length);
  const summary = [];
  for (const { a, clearance, id, nav } of contexts) {
    w.i32(id);
    w.bytes(Buffer.from(nav.passable.buffer, nav.passable.byteOffset, n2));
    const cc = canonComp(nav.comp, n2);
    w.bytes(Buffer.from(cc.buffer, cc.byteOffset, n2 * 4));
    const links = nav.links;
    const linkCount = links ? links.count : 0;
    w.i32(linkCount);
    for (let e = 0; e < linkCount; e++) { w.u32(links.from[e]); w.u32(links.to[e]); w.u8(links.two[e]); w.f32(links.len[e]); }
    let passableCount = 0;
    for (let i = 0; i < n2; i++) passableCount += nav.passable[i];
    summary.push({
      navId: id, agentType: config.agentTypes[a].id, clearance,
      passableCount, compCount: nav.compCount, linkCount,
    });
  }
  const bytes = w.build();
  writeFileSync(join(outRoot, 'parity', 's2-nav-truth.json'), JSON.stringify({
    mapId, seed, contexts: summary, fnv1a: fnv1a(bytes),
  }, null, 2));
  // 逐上下文可走栅格转储(分歧定位用;真相摘要是权威,本文件只做调试)
  const dbgDir = join(outRoot, 'parity', 's2');
  mkdirSync(dbgDir, { recursive: true });
  for (const { a, clearance, nav } of contexts) {
    writeFileSync(join(dbgDir, `${config.agentTypes[a].id}_c${clearance}.pass`), Buffer.from(nav.passable.buffer, nav.passable.byteOffset, n2));
  }
  // 量化输入推导的坡度栅格(f32)——坡度分歧测量用
  {
    const sf = new Float32Array(worldQ.slope.length);
    for (let i = 0; i < sf.length; i++) sf[i] = worldQ.slope[i];
    writeFileSync(join(dbgDir, 'slope.f32'), Buffer.from(sf.buffer));
  }
  console.log(`[export] ${mapId} s2 contexts=${contexts.length} fnv=${fnv1a(bytes)}`);
}
