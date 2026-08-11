export type SurfaceKind = 'reactive' | 'compose' | 'markup' | 'webui';

export type ValueKind = 'Float' | 'Int' | 'Text' | 'Bool';

export type SourceKind =
  | 'singleAttribute'
  | 'derivedAttribute'
  | 'aggregateProjection'
  | 'graphOutput';

export type PanelVariable = {
  id: string;
  label: string;
  valueKind: ValueKind;
};

/** Authoring-canvas node (Shader-graph style). Panel sink is UI sugar → outputs/bindings. */
export type CanvasNode = {
  id: string;
  title: string;
  detail: string;
  kind: 'source' | 'op' | 'const' | 'panel';
  x: number;
  y: number;
  /** output port ids on this node (default: ["out"]) */
  outs?: string[];
  /** input port ids (panel sink lists variable pins) */
  ins?: string[];
};

export type CanvasEdge = {
  id: string;
  from: string;
  fromPort: string;
  to: string;
  toPort: string;
  valueKind?: ValueKind;
};

export type PanelTemplate = {
  id: string;
  name: string;
  blurb: string;
  surfaceKind: SurfaceKind;
  variables: PanelVariable[];
  nodes: CanvasNode[];
  edges: CanvasEdge[];
  bindings: Record<
    string,
    {
      sourceKind: SourceKind;
      attributeId?: string;
      graphOutputKey?: string;
      /** producer canvas node id */
      fromNodeId?: string;
    }
  >;
  copyTemplate: string;
};

export const SURFACE_META: Record<
  SurfaceKind,
  { label: string; native: string; note: string }
> = {
  reactive: {
    label: 'Reactive',
    native: 'ReactivePage<TState>',
    note: '变量 = TState 字段；投影写入后再画 Ui.*',
  },
  compose: {
    label: 'Compose',
    native: 'Controller fields + Ui.*',
    note: '变量 = 控制器字段；变更后整树重建',
  },
  markup: {
    label: 'Markup',
    native: 'HTML + code-behind',
    note: '引擎无 {{var}} 绑定；值由 code-behind 填入',
  },
  webui: {
    label: 'Web UI',
    native: 'WPK descriptor fields[]',
    note: 'sourceKind + attributeId / graphOutputKey',
  },
};

export const TEMPLATES: PanelTemplate[] = [
  {
    id: 'panel.entity_info',
    name: '实体信息卡',
    blurb: '一张图多出口 → Panel 多引脚（像 Shader Graph）',
    surfaceKind: 'reactive',
    variables: [
      { id: 'hp', label: '血量', valueKind: 'Float' },
      { id: 'lastKill', label: '上一次击杀', valueKind: 'Text' },
      { id: 'curState', label: '当前状态', valueKind: 'Text' },
    ],
    nodes: [
      {
        id: 'col',
        title: 'EntityCollection.Selected',
        detail: 'collectionKey',
        kind: 'source',
        x: 40,
        y: 120,
        outs: ['key'],
      },
      {
        id: 'getCol',
        title: 'QueryFromCollection',
        detail: 'list',
        kind: 'op',
        x: 220,
        y: 120,
        ins: ['key'],
        outs: ['list'],
      },
      {
        id: 'idx',
        title: '0',
        detail: 'ConstInt',
        kind: 'const',
        x: 220,
        y: 260,
        outs: ['value'],
      },
      {
        id: 'at',
        title: 'TargetListGet',
        detail: '主选中实体',
        kind: 'op',
        x: 420,
        y: 160,
        ins: ['list', 'index'],
        outs: ['entity'],
      },
      {
        id: 'hpAttr',
        title: 'LoadAttribute',
        detail: 'health.current',
        kind: 'op',
        x: 620,
        y: 20,
        ins: ['entity'],
        outs: ['value'],
      },
      {
        id: 'bbKill',
        title: 'ReadBlackboard',
        detail: 'BB key · combat.last_kill',
        kind: 'op',
        x: 620,
        y: 140,
        ins: ['entity'],
        outs: ['text'],
      },
      {
        id: 'tagState',
        title: 'ReadGameplayTag',
        detail: 'tag · State.*（当前态）',
        kind: 'op',
        x: 620,
        y: 260,
        ins: ['entity'],
        outs: ['tag'],
      },
      {
        id: 'tagLookup',
        title: 'LookupTagDisplayText',
        detail: 'tag → 文案表',
        kind: 'op',
        x: 800,
        y: 260,
        ins: ['tag'],
        outs: ['text'],
      },
      {
        id: 'panel',
        title: 'Panel · EntityInfoCard',
        detail: '多引脚汇入（作者糖 → outputs/bindings）',
        kind: 'panel',
        x: 1000,
        y: 80,
        ins: ['hp', 'lastKill', 'curState'],
      },
    ],
    edges: [
      { id: 'e1', from: 'col', fromPort: 'key', to: 'getCol', toPort: 'key' },
      { id: 'e2', from: 'getCol', fromPort: 'list', to: 'at', toPort: 'list' },
      { id: 'e3', from: 'idx', fromPort: 'value', to: 'at', toPort: 'index', valueKind: 'Int' },
      { id: 'e4', from: 'at', fromPort: 'entity', to: 'hpAttr', toPort: 'entity' },
      { id: 'e5', from: 'at', fromPort: 'entity', to: 'bbKill', toPort: 'entity' },
      { id: 'e6', from: 'at', fromPort: 'entity', to: 'tagState', toPort: 'entity' },
      {
        id: 'e7',
        from: 'hpAttr',
        fromPort: 'value',
        to: 'panel',
        toPort: 'hp',
        valueKind: 'Float',
      },
      {
        id: 'e8',
        from: 'bbKill',
        fromPort: 'text',
        to: 'panel',
        toPort: 'lastKill',
        valueKind: 'Text',
      },
      {
        id: 'e9',
        from: 'tagState',
        fromPort: 'tag',
        to: 'tagLookup',
        toPort: 'tag',
      },
      {
        id: 'e10',
        from: 'tagLookup',
        fromPort: 'text',
        to: 'panel',
        toPort: 'curState',
        valueKind: 'Text',
      },
    ],
    bindings: {
      hp: {
        sourceKind: 'graphOutput',
        graphOutputKey: 'panel.entity_info.hp',
        fromNodeId: 'hpAttr',
        attributeId: 'attribute.health.current',
      },
      lastKill: {
        sourceKind: 'graphOutput',
        graphOutputKey: 'panel.entity_info.lastKill',
        fromNodeId: 'bbKill',
      },
      curState: {
        sourceKind: 'graphOutput',
        graphOutputKey: 'panel.entity_info.curState',
        fromNodeId: 'tagLookup',
      },
    },
    copyTemplate: '血量: {hp}\n上一次击杀的对象: {lastKill}\n当前状态: {curState}',
  },
  {
    id: 'panel.player_aggregate',
    name: '玩家资源总览',
    blurb: '一张 Query 图双出口 → Panel 双引脚',
    surfaceKind: 'webui',
    variables: [
      { id: 'oreTotal', label: '矿石合计', valueKind: 'Float' },
      { id: 'crystalTotal', label: '晶体合计', valueKind: 'Float' },
    ],
    nodes: [
      {
        id: 'owner',
        title: 'LoadCaster',
        detail: '势力 Owner',
        kind: 'source',
        x: 40,
        y: 140,
        outs: ['entity'],
      },
      {
        id: 'all',
        title: 'QueryAllMapEntities',
        detail: 'list',
        kind: 'op',
        x: 220,
        y: 140,
        outs: ['list'],
      },
      {
        id: 'team',
        title: 'QueryFilterTeam',
        detail: 'self',
        kind: 'op',
        x: 420,
        y: 140,
        ins: ['list'],
        outs: ['list'],
      },
      {
        id: 'sumOre',
        title: 'AggSumAttribute',
        detail: 'Resource.Ore',
        kind: 'op',
        x: 620,
        y: 60,
        ins: ['list'],
        outs: ['value'],
      },
      {
        id: 'sumCrystal',
        title: 'AggSumAttribute',
        detail: 'Resource.Crystal',
        kind: 'op',
        x: 620,
        y: 220,
        ins: ['list'],
        outs: ['value'],
      },
      {
        id: 'panel',
        title: 'Panel · ResourceStrip',
        detail: '多引脚汇入（作者糖 → Summary keys）',
        kind: 'panel',
        x: 860,
        y: 100,
        ins: ['oreTotal', 'crystalTotal'],
      },
    ],
    edges: [
      { id: 'a1', from: 'all', fromPort: 'list', to: 'team', toPort: 'list' },
      { id: 'a2', from: 'team', fromPort: 'list', to: 'sumOre', toPort: 'list' },
      { id: 'a3', from: 'team', fromPort: 'list', to: 'sumCrystal', toPort: 'list' },
      {
        id: 'a4',
        from: 'sumOre',
        fromPort: 'value',
        to: 'panel',
        toPort: 'oreTotal',
        valueKind: 'Float',
      },
      {
        id: 'a5',
        from: 'sumCrystal',
        fromPort: 'value',
        to: 'panel',
        toPort: 'crystalTotal',
        valueKind: 'Float',
      },
    ],
    bindings: {
      oreTotal: {
        sourceKind: 'aggregateProjection',
        graphOutputKey: 'ui.panel.player.ore.total',
        fromNodeId: 'sumOre',
      },
      crystalTotal: {
        sourceKind: 'aggregateProjection',
        graphOutputKey: 'ui.panel.player.crystal.total',
        fromNodeId: 'sumCrystal',
      },
    },
    copyTemplate: '矿 {oreTotal} · 晶 {crystalTotal}',
  },
];

export function pascal(id: string): string {
  return id
    .split(/[^a-zA-Z0-9]+/)
    .filter(Boolean)
    .map((p) => p.charAt(0).toUpperCase() + p.slice(1))
    .join('');
}

export function csharpType(kind: ValueKind): string {
  switch (kind) {
    case 'Float':
      return 'float';
    case 'Int':
      return 'int';
    case 'Bool':
      return 'bool';
    case 'Text':
      return 'string';
  }
}

export function pinY(node: CanvasNode, port: string, side: 'in' | 'out'): number {
  const ports = side === 'in' ? node.ins ?? [] : node.outs ?? ['out'];
  const idx = Math.max(0, ports.indexOf(port));
  const n = Math.max(1, ports.length);
  const top = node.kind === 'panel' ? 56 : 44;
  const span = node.kind === 'panel' ? 28 : 22;
  return node.y + top + idx * span;
}

export function pinX(node: CanvasNode, side: 'in' | 'out'): number {
  const w = node.kind === 'panel' ? 220 : 168;
  return side === 'in' ? node.x : node.x + w;
}
