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

export type GraphStep = {
  id: string;
  title: string;
  detail: string;
  kind: 'source' | 'op' | 'const' | 'sink';
};

export type PanelTemplate = {
  id: string;
  name: string;
  blurb: string;
  surfaceKind: SurfaceKind;
  variables: PanelVariable[];
  steps: GraphStep[];
  /** stepId -> variableId edges into sinks */
  stepToVariable: Record<string, string>;
  bindings: Record<
    string,
    {
      sourceKind: SourceKind;
      attributeId?: string;
      graphOutputKey?: string;
      graphStepId?: string;
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
    blurb: '选中单位 → 读血量与状态 → 填面板变量',
    surfaceKind: 'reactive',
    variables: [
      { id: 'hp', label: '血量', valueKind: 'Float' },
      { id: 'lastKill', label: '上一次击杀', valueKind: 'Text' },
      { id: 'curState', label: '当前状态', valueKind: 'Text' },
    ],
    steps: [
      {
        id: 'col',
        title: 'EntityCollection.Selected',
        detail: 'collectionKey',
        kind: 'source',
      },
      {
        id: 'getCol',
        title: 'QueryFromCollection',
        detail: 'list',
        kind: 'op',
      },
      {
        id: 'idx',
        title: 'ConstInt 0',
        detail: '主选中下标',
        kind: 'const',
      },
      {
        id: 'at',
        title: 'TargetListGet',
        detail: 'entity',
        kind: 'op',
      },
      {
        id: 'attr',
        title: 'LoadAttribute',
        detail: 'attribute.health.current',
        kind: 'op',
      },
      {
        id: 'sinkHp',
        title: '变量槽 hp',
        detail: 'output → hp',
        kind: 'sink',
      },
    ],
    stepToVariable: { sinkHp: 'hp' },
    bindings: {
      hp: {
        sourceKind: 'graphOutput',
        graphOutputKey: 'panel.entity_info.hp',
        graphStepId: 'attr',
        attributeId: 'attribute.health.current',
      },
      lastKill: {
        sourceKind: 'singleAttribute',
        attributeId: 'attribute.combat.last_kill_name',
      },
      curState: {
        sourceKind: 'derivedAttribute',
        attributeId: 'attribute.combat.state_label',
      },
    },
    copyTemplate: '血量: {hp}\n上一次击杀的对象: {lastKill}\n当前状态: {curState}',
  },
  {
    id: 'panel.player_aggregate',
    name: '玩家资源总览',
    blurb: '势力建筑合计 → 顶栏变量（跨实体必须走图）',
    surfaceKind: 'webui',
    variables: [
      { id: 'oreTotal', label: '矿石合计', valueKind: 'Float' },
      { id: 'crystalTotal', label: '晶体合计', valueKind: 'Float' },
    ],
    steps: [
      {
        id: 'owner',
        title: 'LoadCaster',
        detail: '势力 Owner',
        kind: 'source',
      },
      {
        id: 'all',
        title: 'QueryAllMapEntities',
        detail: 'list',
        kind: 'op',
      },
      {
        id: 'team',
        title: 'QueryFilterTeam',
        detail: 'self team',
        kind: 'op',
      },
      {
        id: 'sumOre',
        title: 'AggSumAttribute',
        detail: 'Resource.Ore',
        kind: 'op',
      },
      {
        id: 'sumCrystal',
        title: 'AggSumAttribute',
        detail: 'Resource.Crystal',
        kind: 'op',
      },
      {
        id: 'sinkOre',
        title: '变量槽 oreTotal',
        detail: 'Summary key',
        kind: 'sink',
      },
      {
        id: 'sinkCrystal',
        title: '变量槽 crystalTotal',
        detail: 'Summary key',
        kind: 'sink',
      },
    ],
    stepToVariable: { sinkOre: 'oreTotal', sinkCrystal: 'crystalTotal' },
    bindings: {
      oreTotal: {
        sourceKind: 'aggregateProjection',
        graphOutputKey: 'ui.panel.player.ore.total',
        graphStepId: 'sumOre',
      },
      crystalTotal: {
        sourceKind: 'aggregateProjection',
        graphOutputKey: 'ui.panel.player.crystal.total',
        graphStepId: 'sumCrystal',
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
