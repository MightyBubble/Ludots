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
  f64(v) { const b = Buffer.alloc(8); b.writeDoubleLE(v); this.parts.push(b); }
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
  const seen = new Set(), contexts = [];  for (let a = 0; a < config.agentTypes.length; a++) {
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

  // ───────────────────────────── S3:tile 烘焙真相(内容键序) ─────────────────────────────
  // tile 缓存里的每个条目按内容键排序后逐字段入流;uid(获取序)不参与,跨端按内容对齐。
  {
    const entries = [...tileCache.map.entries()].sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0));
    // 逐字段写出一个条目(全局流与逐条目哈希共用同一段代码)
    const writeEntry = (w, e) => {
      const wi32 = (arr) => { w.i32(arr.length); for (let i = 0; i < arr.length; i++) w.i32(arr[i]); };
      const wf32 = (arr) => { w.i32(arr.length); for (let i = 0; i < arr.length; i++) w.f32(arr[i]); };
      w.i32(e.count); w.i32(e.regionCount);
      wf32(e.vx); wf32(e.vy);
      wi32(e.polyStart); wi32(e.polyVerts);
      wi32(e.neiStart); wi32(e.nei); wi32(e.neiA); wi32(e.neiB);
      wi32(e.bminx); wi32(e.bminy); wi32(e.bmaxx); wi32(e.bmaxy);
      wi32(e.polyOf);
      wi32(e.border.poly);
      w.i32(e.border.side.length); for (let i = 0; i < e.border.side.length; i++) w.u8(e.border.side[i]);
      wi32(e.border.lo); wi32(e.border.hi);
      w.i32(e.border.rev.length); for (let i = 0; i < e.border.rev.length; i++) w.u8(e.border.rev[i]);
    };
    const w2 = new BW();
    w2.bytes(Buffer.from('LS3T', 'ascii'));
    w2.i32(world.N);
    w2.i32(entries.length);
    const perEntry = [];
    for (const [, e] of entries) {
      writeEntry(w2, e);
      const w3 = new BW();
      writeEntry(w3, e);
      perEntry.push(fnv1a(w3.build()));
    }
    w2.i32(tileCache.bakes); w2.i32(tileCache.hits); w2.i32(tileCache.misses);
    const bytes2 = w2.build();
    writeFileSync(join(outRoot, 'parity', 's3-tile-truth.json'), JSON.stringify({
      mapId, seed, tiles: entries.length, bakes: tileCache.bakes, hits: tileCache.hits, misses: tileCache.misses, fnv1a: fnv1a(bytes2),
      // 键指纹 → 条目哈希(键 = tile 精确内容,跨端按内容对齐,不靠位置序)
      entryHashes: Object.fromEntries(entries.map(([k, ], i) => [fnv1a(Buffer.from([...k].flatMap((c) => { const cc = c.charCodeAt(0); return [cc & 0xff, cc >> 8]; }))), perEntry[i]])),
    }, null, 2));
    console.log(`[export] ${mapId} s3 tiles=${entries.length} bakes=${tileCache.bakes} fnv=${fnv1a(bytes2)}`);

    // ───────────────────────────── S3-b:拼装网格 + HPA* 抽象图真相 ─────────────────────────────
    // 拓扑(节点格 / 邻接目标 / 计数 / 网格多边形结构)进 FNV;代价数组(浮点)只落数值,
    // C# 侧按带宽比较(两套数学体系的最后位差异不算缺陷,超过带宽才算)。
    {
      const { assembleNavmesh } = await import('../src/engine/navtile/assemble.js');
      const { flattenHpa } = await import('../src/engine/hpa.js');
      const wq = new BW(); // 拓扑流
      const wc = new BW(); // 代价流
      wq.bytes(Buffer.from('LS3B', 'ascii'));
      wq.i32(world.N); wq.i32(contexts.length);
      const perCtx = [];
      const writeI32s = (w, arr) => { w.i32(arr.length); for (let i = 0; i < arr.length; i++) w.i32(arr[i]); };
      const writeU8s = (w, arr) => { w.i32(arr.length); for (let i = 0; i < arr.length; i++) w.u8(arr[i]); };
      const writeCosts = (arr) => { wc.i32(arr.length); for (let i = 0; i < arr.length; i++) wc.f64(arr[i]); };
      for (const { id, nav } of contexts) {
        wq.i32(id);
        const mesh = assembleNavmesh(nav);
        wq.i32(mesh.count); wq.i32(mesh.regionCount);
        writeI32s(wq, mesh.polyStart); writeI32s(wq, mesh.polyVerts); writeI32s(wq, mesh.polyTile);
        writeI32s(wq, mesh.neiStart); writeI32s(wq, mesh.nei);
        writeI32s(wq, mesh.polyOf);
        // 网格顶点与邻接端点坐标是整数(f32 容器装 int),按 int 入流
        writeI32s(wq, Int32Array.from(mesh.vx, (v) => v)); writeI32s(wq, Int32Array.from(mesh.vy, (v) => v));
        writeI32s(wq, Int32Array.from(mesh.pax, (v) => v)); writeI32s(wq, Int32Array.from(mesh.pay, (v) => v));
        writeI32s(wq, Int32Array.from(mesh.pbx, (v) => v)); writeI32s(wq, Int32Array.from(mesh.pby, (v) => v));
        writeCosts(mesh.polyCost);
        const flat = flattenHpa(nav.hpa);
        wq.i32(flat.nodeCount); wq.i32(flat.edgeCount);
        writeI32s(wq, flat.nodeCell); writeU8s(wq, flat.nodeLayer); writeI32s(wq, flat.nodeCluster);
        writeI32s(wq, flat.adjStart); writeI32s(wq, flat.adjTo);
        writeCosts(flat.adjCost);
        perCtx.push({ navId: id, polys: mesh.count, hpaNodes: flat.nodeCount, hpaEdges: flat.edgeCount });
      }
      const truthBytes = wq.build();
      writeFileSync(join(outRoot, 'parity', 's3b-topology.bin'), truthBytes);
      writeFileSync(join(outRoot, 'parity', 's3b-graph-truth.json'), JSON.stringify({
        mapId, seed, contexts: perCtx, fnv1a: fnv1a(truthBytes),
      }, null, 2));
      writeFileSync(join(outRoot, 'parity', 's3b-costs.bin'), wc.build());
      console.log(`[export] ${mapId} s3b fnv=${fnv1a(truthBytes)}`);
    }

    // ───────────────────────────── S3-c:两点路径 + 流场真相 ─────────────────────────────
    // 64 组确定性起终点 × 每上下文。对拍契约(两套浮点体系下唯一诚实的口径):
    //   逐位(FNV):分支(tile A*+漏斗 / HPA* / 不可达)、trace 是否到达;
    //   带宽:折线总长度(点列由浮点派生,平局时点数与顶点都可不同,坐标入诊断流)、
    //   路径代价、起格积分 / 绷紧长度;
    //   结构容差(s3c-detail.bin):HPA 走廊 / poly 链 / 到达集合是浮点派生量——等代价
    //     十字路口的择路会被最后位差翻转(两解同最优),逐格 integ / len 在两侧到达集合的
    //     交集上按带宽比较,独占格计数限容差;
    //   路点(s3c-wp.bin):决定性全等,平局双解按规则校验。
    {
      const { findTilePath, markTileClusters } = await import('../src/engine/navtile/tileQuery.js');
      const { findPath } = await import('../src/engine/hpa.js');
      const { buildFlowField, padMask, tracePath } = await import('../src/engine/flowfield.js');
      const { clusterOf } = await import('../src/engine/grid.js');
      const { FlowPool } = await import('../src/engine/planning/flowPool.js');

      // 固定种子的 LCG 取点,过滤到全部上下文地面可走;对子随真相落盘,C# 端直接读取
      let rngState = 0x2f6e2b1;
      const rng = () => (rngState = (Math.imul(rngState, 1103515245) + 12345) & 0x7fffffff) / 0x80000000;
      const pairs = [];
      let sampleGuard = 0;
      while (pairs.length < 64 && sampleGuard++ < 200000) {
        const sx = (rng() * N) | 0, sy = (rng() * N) | 0, gx = (rng() * N) | 0, gy = (rng() * N) | 0;
        if (Math.abs(sx - gx) + Math.abs(sy - gy) < 8) continue;
        const sCell = sy * N + sx, gCell = gy * N + gx;
        if (contexts.every(({ nav }) => nav.passable[sCell] && nav.passable[gCell])) pairs.push([sCell, gCell]);
      }
      if (pairs.length < 64) throw new Error(`S3-c 起终点采样不足: ${pairs.length}/64`);

      const inset = config.navmesh.portalInsetCells, pad = config.flowfield.corridorPadding;
      const pool = new FlowPool(N, config.flowfield.poolCapacity);
      const wq = new BW(); // 逐位流(FNV)
      const wf = new BW(); // 浮点流(带宽)
      const wd = new BW(); // 结构容差流(链 / 到达集合)
      const ww = new BW(); // 路点与路点链(派生量诊断流)
      const wi32s = (w, arr) => { w.i32(arr.length); for (let i = 0; i < arr.length; i++) w.i32(arr[i]); };
      wq.bytes(Buffer.from('LS3C', 'ascii'));
      wq.i32(N); wq.i32(contexts.length); wq.i32(pairs.length);
      wd.bytes(Buffer.from('LS3D', 'ascii'));
      wd.i32(N); wd.i32(contexts.length); wd.i32(pairs.length);
      ww.bytes(Buffer.from('LS3W', 'ascii'));
      ww.i32(N); ww.i32(contexts.length); ww.i32(pairs.length);
      const perCtxQ = [];
      for (const { id, nav } of contexts) {
        wq.i32(id);
        wd.i32(id);
        ww.i32(id);
        const { S, C } = nav.hpa;
        let reachable = 0;
        for (const [start, goal] of pairs) {
          const mask = new Uint8Array(C * C);
          mask[clusterOf(goal, N, S, C)] = 1;
          let branch = 0, polyRefs = [], hpaClusters = [], hpaCells = [], points = null, cost = 0;
          const r = nav.links ? null : findTilePath(nav, start, goal, nav.minCost, inset);
          if (r) {
            branch = 1; polyRefs = r.polys; points = r.points; cost = r.cost;
            markTileClusters(nav, r.polys, mask);
          } else {
            const p = findPath(nav, start, goal);
            if (p) {
              branch = 2; hpaClusters = p.clusters; hpaCells = p.cells; cost = p.cost;
              for (const c of p.clusters) mask[c] = 1;
              points = [];
              for (const c of p.cells) points.push((c % N) + 0.5, ((c / N) | 0) + 0.5);
            }
          }
          const flow = buildFlowField(nav, goal, padMask(mask, C, pad), pool);
          const trace = tracePath(flow, start, N * N);
          const traceOk = trace[trace.length - 1] === goal ? 1 : 0;
          const cells = Array.from(pool.order.subarray(0, flow.reached)).sort((a, b) => a - b);
          wq.u8(branch); wq.u8(traceOk);
          // 折线:总长度带宽对齐(点列由浮点派生,平局时点数与顶点都可不同);坐标入诊断流
          let plen = 0;
          if (points) for (let i = 2; i < points.length; i += 2) plen += Math.hypot(points[i] - points[i - 2], points[i + 1] - points[i - 1]);
          wf.f64(plen);
          wf.f64(cost);
          wd.i32(points ? points.length / 2 : -1);
          if (points) for (const v of points) wd.f64(v);
          wi32s(wd, polyRefs); wi32s(wd, hpaClusters); wi32s(wd, hpaCells);
          wd.i32(flow.reached);
          wd.i32(cells.length);
          for (const cell of cells) { wd.i32(cell); wf.f64(flow.integ[cell]); wf.f64(flow.len[cell]); }
          wf.f64(flow.integ[start]); wf.f64(flow.len[start]);
          wi32s(ww, trace);
          ww.i32(cells.length);
          for (const cell of cells) { ww.i32(cell); ww.i32(flow.wp[cell]); }
          if (branch || traceOk) reachable++;
        }
        perCtxQ.push({ navId: id, reachable });
      }
      const queryBytes = wq.build();
      writeFileSync(join(outRoot, 'parity', 's3c-topology.bin'), queryBytes); // 分歧定位用;摘要是权威
      writeFileSync(join(outRoot, 'parity', 's3c-detail.bin'), wd.build());
      writeFileSync(join(outRoot, 'parity', 's3c-wp.bin'), ww.build());
      writeFileSync(join(outRoot, 'parity', 's3c-floats.bin'), wf.build());
      writeFileSync(join(outRoot, 'parity', 's3c-query-truth.json'), JSON.stringify({
        mapId, seed, pairs, contexts: perCtxQ, fnv1a: fnv1a(queryBytes),
      }, null, 2));
      console.log(`[export] ${mapId} s3c pairs=${pairs.length} fnv=${fnv1a(queryBytes)}`);
    }
  }
}
