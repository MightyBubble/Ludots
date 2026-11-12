export type TraversalCellValue = { cost: number; speedDivisor: number; depart: string[]; arrive: string[] };
export type TraversalCell = false | TraversalCellValue;
export type TraversalLinkValue = { upCm: number; downCm: number; rangeNavCells: number; costPerNavCell: number };
export type TraversalLink = false | TraversalLinkValue;
export type TraversalRow = {
  maxSlopeDeg: number | null;
  links: Record<string, TraversalLink>;
  cells: Record<string, TraversalCell>;
};
export type Traversal = {
  areas: Record<string, { ignoresSlope: boolean }>;
  terrainAreas: Record<string, string>;
  linkKinds: Record<string, { traversalSpeedRatio: number }>;
  agentTypes: Record<string, TraversalRow>;
};
export const LINK_FIELDS = ['upCm', 'downCm', 'rangeNavCells', 'costPerNavCell'] as const;
export type LinkField = (typeof LINK_FIELDS)[number];

const OPEN_CELL: TraversalCellValue = { cost: 1, speedDivisor: 1, depart: [], arrive: [] };
const OPEN_LINK: TraversalLinkValue = { upCm: 2500, downCm: 20000, rangeNavCells: 7, costPerNavCell: 2 };

export function cloneTraversal(traversal: Traversal | undefined): Traversal | null {
  return traversal ? (JSON.parse(JSON.stringify(traversal)) as Traversal) : null;
}

export function readNumber(text: string): number {
  return text.trim() === '' ? Number.NaN : Number(text);
}

export function setAreaIgnoresSlope(traversal: Traversal, areaId: string, ignoresSlope: boolean): Traversal {
  return { ...traversal, areas: { ...traversal.areas, [areaId]: { ...traversal.areas[areaId], ignoresSlope } } };
}

export function setLinkSpeedRatio(traversal: Traversal, kind: string, ratio: number): Traversal {
  return { ...traversal, linkKinds: { ...traversal.linkKinds, [kind]: { traversalSpeedRatio: ratio } } };
}

function withRow(traversal: Traversal, agentType: string, edit: (row: TraversalRow) => TraversalRow): Traversal {
  return { ...traversal, agentTypes: { ...traversal.agentTypes, [agentType]: edit(traversal.agentTypes[agentType]) } };
}

export function setMaxSlope(traversal: Traversal, agentType: string, maxSlopeDeg: number | null): Traversal {
  return withRow(traversal, agentType, (row) => ({ ...row, maxSlopeDeg }));
}

/** 能力从 false 打开时先用默认里这一格的值，默认也是 false 才用通用值。 */
export function toggleLink(traversal: Traversal, defaults: Traversal | null, agentType: string, kind: string, on: boolean): Traversal {
  const fallback = defaults?.agentTypes[agentType]?.links[kind];
  return withRow(traversal, agentType, (row) => {
    const cells = on ? row.cells : stripKind(row.cells, kind);
    return { ...row, cells, links: { ...row.links, [kind]: on ? { ...(fallback || OPEN_LINK) } : false } };
  });
}

function stripKind(cells: Record<string, TraversalCell>, kind: string): Record<string, TraversalCell> {
  const next: Record<string, TraversalCell> = {};
  for (const [areaId, cell] of Object.entries(cells)) {
    next[areaId] = cell === false
      ? false
      : { ...cell, depart: cell.depart.filter((k) => k !== kind), arrive: cell.arrive.filter((k) => k !== kind) };
  }
  return next;
}

export function setLinkField(traversal: Traversal, agentType: string, kind: string, field: LinkField, value: number): Traversal {
  return withRow(traversal, agentType, (row) => {
    const link = row.links[kind];
    if (link === false) return row;
    return { ...row, links: { ...row.links, [kind]: { ...link, [field]: value } } };
  });
}

export function togglePassable(traversal: Traversal, defaults: Traversal | null, agentType: string, areaId: string, on: boolean): Traversal {
  const fallback = defaults?.agentTypes[agentType]?.cells[areaId];
  return withRow(traversal, agentType, (row) => ({
    ...row,
    cells: { ...row.cells, [areaId]: on ? usableCell(fallback || OPEN_CELL, row.links) : false },
  }));
}

function usableCell(cell: TraversalCellValue, links: Record<string, TraversalLink>): TraversalCellValue {
  const usable = (kind: string) => links[kind] !== undefined && links[kind] !== false;
  return { ...cell, depart: cell.depart.filter(usable), arrive: cell.arrive.filter(usable) };
}

export function setCellNumber(traversal: Traversal, agentType: string, areaId: string, field: 'cost' | 'speedDivisor', value: number): Traversal {
  return withRow(traversal, agentType, (row) => {
    const cell = row.cells[areaId];
    if (cell === false) return row;
    return { ...row, cells: { ...row.cells, [areaId]: { ...cell, [field]: value } } };
  });
}

export function setEndpoint(
  traversal: Traversal,
  agentType: string,
  areaId: string,
  side: 'depart' | 'arrive',
  kind: string,
  on: boolean,
): Traversal {
  return withRow(traversal, agentType, (row) => {
    const cell = row.cells[areaId];
    if (cell === false) return row;
    const rest = cell[side].filter((k) => k !== kind);
    return { ...row, cells: { ...row.cells, [areaId]: { ...cell, [side]: on ? [...rest, kind] : rest } } };
  });
}
