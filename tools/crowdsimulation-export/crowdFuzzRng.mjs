// mulberry32，与 Ludots CrowdSimRng 同输出 u32/2^32；nextInt 与 CrowdFuzzRunner.NextInt 同语义。
export function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function nextInt(rng, count) {
  if (count <= 1) return 0;
  const u = rng();
  return Math.min(count - 1, Math.floor(u * count));
}
