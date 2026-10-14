import { walkStrokeCells, type StrokeCell } from '../src/Core/Map/StrokeCells.ts';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

function walk(from: StrokeCell, to: StrokeCell): StrokeCell[] {
  const cells: StrokeCell[] = [];
  walkStrokeCells(from, to, (c, r) => cells.push({ c, r }));
  return cells;
}

function assertStroke(from: StrokeCell, to: StrokeCell): void {
  const label = `(${from.c},${from.r})->(${to.c},${to.r})`;
  const cells = walk(from, to);
  const expectedLength = Math.abs(to.c - from.c) + Math.abs(to.r - from.r);
  assert(cells.length === expectedLength, `${label}: expected ${expectedLength} cells, got ${cells.length}`);
  if (expectedLength === 0) return;
  const last = cells[cells.length - 1];
  assert(last.c === to.c && last.r === to.r, `${label}: must end on the target cell, ended on (${last.c},${last.r})`);
  let prev = from;
  for (const cell of cells) {
    const step = Math.abs(cell.c - prev.c) + Math.abs(cell.r - prev.r);
    assert(step === 1, `${label}: cell (${cell.c},${cell.r}) is not one row or column away from (${prev.c},${prev.r})`);
    prev = cell;
  }
  const dc = to.c - from.c;
  const dr = to.r - from.r;
  const length = Math.hypot(dc, dr);
  for (const cell of cells) {
    const distanceFromLine = Math.abs(dr * (cell.c - from.c) - dc * (cell.r - from.r)) / length;
    assert(distanceFromLine <= 1, `${label}: cell (${cell.c},${cell.r}) strays ${distanceFromLine.toFixed(2)} cells from the drag line`);
  }
}

assert(walk({ c: 4, r: 4 }, { c: 4, r: 4 }).length === 0, 'same cell must not repaint');

const horizontal = walk({ c: 0, r: 0 }, { c: 5, r: 0 });
assert(horizontal.map((cell) => cell.c).join(',') === '1,2,3,4,5', 'horizontal stroke must fill every column');
assert(horizontal.every((cell) => cell.r === 0), 'horizontal stroke must stay on its row');

const vertical = walk({ c: 3, r: 9 }, { c: 3, r: 6 });
assert(vertical.map((cell) => cell.r).join(',') === '8,7,6', 'upward stroke must fill every row');

const endpoints: StrokeCell[] = [
  { c: 0, r: 0 }, { c: 1, r: 0 }, { c: 0, r: 1 }, { c: 7, r: 3 }, { c: -7, r: 3 }, { c: 3, r: -7 },
  { c: 12, r: 12 }, { c: -12, r: -12 }, { c: 200, r: 1 }, { c: 1, r: 200 }, { c: -137, r: 59 },
];
for (const from of endpoints) {
  for (const to of endpoints) {
    assertStroke({ c: from.c + 1000, r: from.r + 1000 }, { c: to.c + 1000, r: to.r + 1000 });
  }
}

console.log('stroke cells assert passed');
