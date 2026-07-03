import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  Background,
  BackgroundVariant,
  Controls,
  Handle,
  MiniMap,
  Panel,
  Position,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow
} from '@xyflow/react';

const h = React.createElement;
const INITIAL_FLOW_VIEWPORT = { x: 56, y: 76, zoom: 0.82 };

const categories = [
  {
    id: 'engine',
    code: 'ENG',
    name: 'Engine & Architecture',
    color: '#6f7d8a',
    diagrams: [
      ['engine-architecture.svg', '整体引擎架构', '以 GameEngine 为核心，放射状展示 8 大子系统与平台适配器'],
      ['systemgroup-pipeline.svg', 'SystemGroup 流水线', '10 个 Phase 的完整执行顺序，每个 Phase 展开注册的实际系统'],
      ['hexagonal-architecture.svg', '六边形架构', 'Core Domain → Ports → Adapters 三层，实线/虚线区分依赖与插件'],
      ['tick-loop.svg', 'Game Tick 循环', '从 Tick 入口到 Render 输出的 5 大阶段完整流程'],
      ['feature-mind-map.svg', '全功能拓扑脑图', '以 Engine 为中心放射状展开 10 大能力域'],
      ['business-game-tick-e2e.svg', '端到端 Tick 流程', 'Input → ECS → GAS → Navigation → Physics → Presentation → Render']
    ]
  },
  {
    id: 'gas',
    code: 'GAS',
    name: 'GAS & Combat',
    color: '#607f78',
    diagrams: [
      ['gas-component-relation.svg', 'GAS 组件关系', 'Ability / Effect / Attribute / Tag / Cue 围绕 GasRuntimeState 的协调关系'],
      ['ability-activation-flow.svg', 'Ability 激活流程', 'Input → Order → 校验 → 时间线执行 → Effect → Attribute → Cue'],
      ['effect-lifecycle.svg', 'Effect 生命周期', 'Apply → Update → Lifetime → Expire → Cleanup → Publish 六阶段'],
      ['attribute-calculation.svg', 'Attribute 计算链路', 'Mod → Calc → Sink → UI 的完整计算与汇出路径'],
      ['target-resolver.svg', 'Target 解析器', 'Query → Filter → Dispatch 三层目标解析架构']
    ]
  },
  {
    id: 'ecs',
    code: 'ECS',
    name: 'ECS & Data Model',
    color: '#61798f',
    diagrams: [
      ['ecs-datamodel-ecs-hierarchy.svg', 'ECS 层级结构', 'World → Entity → Component → System → Archetype 的完整层级'],
      ['ecs-datamodel-chunk-storage.svg', 'Chunk SoA 存储', 'Chunk Header + ComponentArray 的内存布局细节'],
      ['ecs-datamodel-entity-lifecycle.svg', 'Entity 生命周期', 'Create → Active → Destroy 的完整流程与分支判断'],
      ['ecs-datamodel-query-execution.svg', 'Query 执行流程', 'System.Update → EntityQuery → Filter → Iterator 五步链'],
      ['ecs-datamodel-template-inheritance.svg', 'EntityTemplate 继承', 'Base → Category → Concrete 三层继承与批量生成']
    ]
  },
  {
    id: 'navigation',
    code: 'NAV',
    name: 'Navigation & Movement',
    color: '#56758c',
    diagrams: [
      ['nav-navigation-stack.svg', '导航技术栈', 'CDT → NavMesh → HPA → MassNav → ORCA/Sonar 五层架构'],
      ['nav-navmesh-baking.svg', 'NavMesh 烘焙', 'Geometry → CDT → Polygon → Graph 的完整烘焙管线'],
      ['nav-pathfinding-flow.svg', '寻路流程', 'Query → HPA → Path → Smooth → Execute 的完整链路'],
      ['nav-avoidance-orca.svg', 'ORCA 避障', 'Agent → Neighbor → VO → LP → New Velocity 的避障流程'],
      ['nav-mass-navigation.svg', 'MassNavigation 架构', 'Runtime → 13 步仿真管线 → WorldPosition 的完整系统'],
      ['spatial-query-modes.svg', '空间查询模式', '8 种查询模式（AABB/Radius/Cone/Rectangle/Line/Segment/Hex/Point）对比'],
      ['chunk-spatial-partition.svg', 'Chunk 空间分区', 'World → Chunk → Cell → Entity 四级层级与查询执行'],
      ['hex-grid-coordinates.svg', '六边形坐标系', 'Axial/Cube/Pixel 转换与 Ring/Disk/Spiral 查询']
    ]
  },
  {
    id: 'presentation',
    code: 'PRS',
    name: 'Presentation & Rendering',
    color: '#817671',
    diagrams: [
      ['performer-lifecycle.svg', 'Performer 生命周期', 'Definition → Compile → Bootstrap → Create → Update → Destroy'],
      ['performer-command-flow.svg', 'PerformerCommand 流', 'Event → CommandBuffer → EmitSystem → EntityRuntime 的完整流'],
      ['behavior-activation.svg', 'BehaviorSlot 激活', '7 种行为的激活、条件与输出关系'],
      ['presentation-pipeline.svg', 'Presentation 渲染管线', 'Logic → 10 Phase → 8 Buffer → Platform → Screen'],
      ['minimap-architecture.svg', 'Minimap 架构', 'Data → MarkerBuffer → Presentation → ScreenOverlay 的完整链路'],
      ['raylib-render-loop.svg', 'Raylib 渲染循环', 'Input → Update → Draw → Swap 的环形管线']
    ]
  },
  {
    id: 'modding',
    code: 'MOD',
    name: 'Modding & Configuration',
    color: '#7c7768',
    diagrams: [
      ['mod-loading-flow.svg', 'Mod 加载流程', 'Discover → Resolve → Sort → Load → OnLoad 四阶段'],
      ['alc-isolation.svg', 'ALC 隔离', 'Host App → ALC → 三层解析 → Mod Assembly Types'],
      ['vfs-hierarchy.svg', 'VFS 层级', 'VirtualFileSystem → Mod → Path → Physical File 的挂载层级'],
      ['dependency-resolver.svg', '依赖解析', 'mod.json → DependencyResolver → 拓扑排序 → SemVer 匹配'],
      ['mod-extension-points.svg', '扩展点时序', 'IMod → OnLoad → 6 个扩展点的注册链路']
    ]
  },
  {
    id: 'graph',
    code: 'VM',
    name: 'GraphRuntime & Scripting',
    color: '#756f86',
    diagrams: [
      ['graph-vm-structure.svg', 'GraphVM 结构', 'Instruction → Executor → Handler Table → Register 的 VM 架构'],
      ['graph-compiler-flow.svg', 'GraphCompiler 编译', 'JSON → Validator → Compiler → Bytecode → Program 的完整流程'],
      ['opcode-taxonomy.svg', 'OpCode 分类', '133 个 OpCode 按 9 大类别网格展开'],
      ['graph-execution-loop.svg', '执行循环', 'GraphExecutor 38 行核心循环的展开流程图']
    ]
  },
  {
    id: 'input',
    code: 'IN',
    name: 'Input & Interaction',
    color: '#6c728a',
    diagrams: [
      ['input-order-flow.svg', 'Input → Order → Command', 'Raw Input → Adapter → Order → Command → GAS 的完整链路'],
      ['selection-state-machine.svg', 'Selection 状态机', 'Empty/Single/Multi/Group/View/Formation 六状态与转换边'],
      ['interaction-modes.svg', '交互模式对比', 'SmartCast/AimCast/ContextScored/PressReleaseAimCast 四种模式'],
      ['input-systemgroup.svg', 'Input 在 SystemGroup 中', 'Input Phase 在 10 个 Phase 中的位置与 8 步详细执行']
    ]
  },
  {
    id: 'adapter',
    code: 'ADP',
    name: 'Adapter & Platform',
    color: '#5d7f82',
    diagrams: [
      ['adapter-comparison.svg', '三平台对比', 'Raylib / Web / UE5 的架构对比与 IAdapter 接口层'],
      ['web-streaming.svg', 'Web 串流', 'Server → FrameProtocol → DeltaCompressor → WebSocket → Client'],
      ['adapter-sync.svg', 'AdapterSync', 'Simulation → Presentation → Adapter 的同步机制与帧时间轴']
    ]
  },
  {
    id: 'business',
    code: 'BIZ',
    name: 'Business Process',
    color: '#657187',
    diagrams: [
      ['business-map-load-flow.svg', '地图加载', 'Launcher → Bootstrap → Mod → Map → Entity 的加载流程'],
      ['business-save-restore-flow.svg', '存档/读档', 'Save 与 Restore 的双流程对比与 .ldsave 格式'],
      ['business-web-client-flow.svg', 'Web 客户端帧', 'Connect → Receive → Decode → Render → Input → Send']
    ]
  },
  {
    id: 'registry',
    code: 'REG',
    name: 'Registry & Dependencies',
    color: '#687281',
    diagrams: [
      ['registry-dependency.svg', 'Registry 依赖网络', '30+ 个注册表按 8 大子系统分类的依赖关系图'],
      ['registry-extension.svg', 'Registry 扩展时序', 'ModLoader → ALC → IMod → Registry → GameEngine 的时序'],
      ['system-dependency-matrix.svg', '系统依赖矩阵', '14×14 矩阵热力图，颜色深浅表示耦合强度'],
      ['module-coupling.svg', '模块耦合网络', '14 个模块的力导向耦合图，粗线/暖色=高耦合'],
      ['call-graph.svg', '核心调用图', 'GameEngine.Tick() 经 7 个 Phase 到 20+ 子系统的调用链']
    ]
  },
  {
    id: 'vision',
    code: 'VIS',
    name: 'Spatial & Vision',
    color: '#607d89',
    diagrams: [
      ['vision-fog-flow.svg', '视野与迷雾', 'VisionEmitter → ChunkedField → FogLayer → Visual 的 6 阶段计算']
    ]
  },
  {
    id: 'showcase',
    code: 'UAT',
    name: 'Capability & Showcase',
    color: '#75808a',
    diagrams: [
      ['capability-matrix.svg', 'Capability 矩阵', '8 模块 × 4 成熟度等级的热力图'],
      ['showcase-topology.svg', 'Showcase 拓扑', '5 个演示套件与底层引擎能力的映射关系']
    ]
  }
];

const allDiagrams = categories.flatMap((category) =>
  category.diagrams.map(([file, title, desc]) => ({
    id: file.replace(/\.svg$/, ''),
    file,
    png: file.replace(/\.svg$/, '.png'),
    title,
    desc,
    categoryId: category.id,
    categoryName: category.name,
    categoryCode: category.code,
    color: category.color
  }))
);

const nodeTypes = {
  categoryNode: CategoryNode,
  diagramNode: DiagramNode
};

function CategoryNode({ data, selected }) {
  return h(
    'div',
    { className: `flow-node category-node${selected ? ' selected' : ''}`, style: { '--node-color': data.color } },
    h(Handle, { className: 'node-handle', type: 'source', position: Position.Right }),
    h('div', { className: 'node-bar' }),
    h(
      'div',
      { className: 'node-body' },
      h('div', { className: 'node-kicker' }, data.code),
      h('div', { className: 'node-title' }, data.name),
      h('div', { className: 'node-desc' }, `${data.count} diagrams`),
      h('div', { className: 'node-meta' }, h('span', { className: 'node-pill' }, 'category'))
    )
  );
}

function DiagramNode({ data, selected }) {
  return h(
    'div',
    { className: `flow-node${selected ? ' selected' : ''}`, style: { '--node-color': data.color } },
    h(Handle, { className: 'node-handle', type: 'target', position: Position.Left }),
    h('div', { className: 'node-bar' }),
    h(
      'div',
      { className: 'node-body' },
      h('div', { className: 'node-kicker' }, data.categoryCode),
      h('div', { className: 'node-title' }, data.title),
      h('div', { className: 'node-desc' }, data.desc),
      h(
        'div',
        { className: 'node-meta' },
        h('span', { className: 'node-pill' }, 'svg'),
        h('span', { className: 'node-pill' }, 'png')
      )
    )
  );
}

function buildFlow(activeCategory, query) {
  const normalizedQuery = query.trim().toLowerCase();
  const diagramColumns = activeCategory === 'all' && normalizedQuery.length === 0 ? 3 : 2;
  const nodes = [];
  const edges = [];
  let y = 0;

  for (const category of categories) {
    if (activeCategory !== 'all' && activeCategory !== category.id) continue;

    const diagrams = allDiagrams.filter((diagram) => {
      if (diagram.categoryId !== category.id) return false;
      if (!normalizedQuery) return true;
      return `${diagram.title} ${diagram.desc} ${diagram.categoryName} ${diagram.file}`.toLowerCase().includes(normalizedQuery);
    });

    if (diagrams.length === 0) continue;

    nodes.push({
      id: `category:${category.id}`,
      type: 'categoryNode',
      position: { x: 0, y },
      data: {
        code: category.code,
        name: category.name,
        count: diagrams.length,
        color: category.color
      },
      selectable: true
    });

    diagrams.forEach((diagram, index) => {
      const row = Math.floor(index / diagramColumns);
      const column = index % diagramColumns;
      const id = `diagram:${diagram.id}`;

      nodes.push({
        id,
        type: 'diagramNode',
        position: { x: 320 + column * 292, y: y + row * 136 },
        data: diagram,
        selectable: true
      });

      edges.push({
        id: `edge:${category.id}:${diagram.id}`,
        source: `category:${category.id}`,
        target: id,
        type: 'smoothstep',
        style: { stroke: category.color, strokeWidth: 1.6, opacity: 0.52 }
      });
    });

    y += Math.ceil(diagrams.length / diagramColumns) * 136 + 118;
  }

  return { nodes, edges };
}

function DiagramExplorer() {
  const [query, setQuery] = useState('');
  const [activeCategory, setActiveCategory] = useState('all');
  const [selectedId, setSelectedId] = useState(allDiagrams[0].id);
  const [viewerOpen, setViewerOpen] = useState(false);
  const [viewerZoom, setViewerZoom] = useState(1);
  const flowData = useMemo(() => buildFlow(activeCategory, query), [activeCategory, query]);
  const visibleIds = useMemo(
    () => new Set(flowData.nodes.filter((node) => node.type === 'diagramNode').map((node) => node.data.id)),
    [flowData.nodes]
  );
  const selectedDiagram = useMemo(() => {
    if (visibleIds.has(selectedId)) {
      return allDiagrams.find((diagram) => diagram.id === selectedId) ?? null;
    }

    const firstVisible = flowData.nodes.find((node) => node.type === 'diagramNode');
    return firstVisible?.data ?? null;
  }, [flowData.nodes, selectedId, visibleIds]);
  const visibleDiagramCount = visibleIds.size;

  const onNodeClick = useCallback((_, node) => {
    if (node.type === 'diagramNode') {
      setSelectedId(node.data.id);
      return;
    }

    if (node.id.startsWith('category:')) {
      setActiveCategory(node.id.slice('category:'.length));
    }
  }, []);

  const openViewer = useCallback(() => {
    setViewerZoom(1);
    setViewerOpen(true);
  }, []);

  const closeViewer = useCallback(() => {
    setViewerOpen(false);
  }, []);

  useEffect(() => {
    window.__LUDOTS_DIAGRAM_FLOW_READY__ = {
      diagrams: allDiagrams.length,
      categories: categories.length,
      reactFlow: true
    };
  }, []);

  useEffect(() => {
    window.__LUDOTS_DIAGRAM_FLOW_STATE__ = {
      activeCategory,
      query,
      visibleDiagramCount,
      selectedId: selectedDiagram?.id ?? null,
      viewerOpen
    };
  }, [activeCategory, query, selectedDiagram, visibleDiagramCount, viewerOpen]);

  return h(
    'div',
    { className: 'diagram-shell' },
    h(Header, { visibleDiagramCount }),
    h(Toolbar, { activeCategory, query, setActiveCategory, setQuery }),
    h(
      'div',
      { className: 'diagram-workspace' },
      h(FlowCanvas, { activeCategory, flowData, onNodeClick, query, visibleDiagramCount }),
      h(DetailPanel, { diagram: selectedDiagram, openViewer })
    ),
    viewerOpen && selectedDiagram
      ? h(DiagramViewer, { diagram: selectedDiagram, zoom: viewerZoom, setZoom: setViewerZoom, onClose: closeViewer })
      : null
  );
}

function Header({ visibleDiagramCount }) {
  return h(
    'header',
    { className: 'diagram-header' },
    h(
      'div',
      { className: 'diagram-title' },
      h('h1', null, 'Ludots 架构图库'),
      h('p', null, '57 张架构图按领域重排为可缩放的 React Flow 地图。复杂 SVG 保留为原始文件，详情面板提供大图查看。')
    ),
    h(
      'div',
      { className: 'diagram-stats' },
      h(StatCell, { value: allDiagrams.length, label: 'diagrams' }),
      h(StatCell, { value: categories.length, label: 'domains' }),
      h(StatCell, { value: visibleDiagramCount, label: 'visible' }),
      h(StatCell, { value: 'React Flow', label: 'viewer' })
    )
  );
}

function StatCell({ value, label }) {
  return h('div', { className: 'stat-cell' }, h('strong', null, value), h('span', null, label));
}

function Toolbar({ activeCategory, query, setActiveCategory, setQuery }) {
  const onQueryChange = useCallback((event) => {
    setQuery(event.target.value);
  }, [setQuery]);

  const onCategorySelect = useCallback((event) => {
    setActiveCategory(event.target.value);
  }, [setActiveCategory]);

  return h(
    'div',
    { className: 'diagram-toolbar' },
    h('input', {
      className: 'diagram-search',
      value: query,
      onChange: onQueryChange,
      placeholder: '搜索图表标题、文件名或领域',
      'aria-label': '搜索图表'
    }),
    h(
      'select',
      { className: 'category-filter', value: activeCategory, onChange: onCategorySelect, 'aria-label': '筛选领域' },
      h('option', { value: 'all' }, 'All domains'),
      categories.map((category) => h('option', { key: category.id, value: category.id }, category.name))
    ),
    h(
      'div',
      { className: 'category-strip' },
      h(CategoryChip, { id: 'all', activeCategory, setActiveCategory, label: 'All' }),
      categories.map((category) =>
        h(CategoryChip, {
          key: category.id,
          id: category.id,
          activeCategory,
          setActiveCategory,
          label: category.code
        })
      )
    )
  );
}

function CategoryChip({ id, activeCategory, setActiveCategory, label }) {
  const onClick = useCallback(() => {
    setActiveCategory(id);
  }, [id, setActiveCategory]);

  return h(
    'button',
    { className: `category-chip${activeCategory === id ? ' active' : ''}`, type: 'button', onClick },
    label
  );
}

function FlowCanvas({ activeCategory, flowData, onNodeClick, query, visibleDiagramCount }) {
  const { fitView } = useReactFlow();
  const shouldFitView = activeCategory !== 'all' || query.trim().length > 0;

  useEffect(() => {
    if (!shouldFitView || flowData.nodes.length === 0) return undefined;

    const frame = window.requestAnimationFrame(() => {
      fitView({ padding: 0.18, duration: 220, minZoom: 0.6, maxZoom: 1.05 });
    });

    return () => window.cancelAnimationFrame(frame);
  }, [activeCategory, fitView, flowData.nodes.length, query, shouldFitView]);

  if (flowData.nodes.length === 0) {
    return h('section', { className: 'flow-shell' }, h('div', { className: 'flow-empty' }, '没有匹配的图表'));
  }

  return h(
    'section',
    { className: 'flow-shell' },
    h(
      ReactFlow,
      {
        nodes: flowData.nodes,
        edges: flowData.edges,
        nodeTypes,
        onNodeClick,
        defaultViewport: INITIAL_FLOW_VIEWPORT,
        minZoom: 0.16,
        maxZoom: 2.6,
        nodesDraggable: false,
        elementsSelectable: true,
        proOptions: { hideAttribution: true }
      },
      h(Background, { variant: BackgroundVariant.Dots, gap: 22, size: 1, color: 'rgba(45, 42, 38, 0.18)' }),
      h(MiniMap, { pannable: true, zoomable: true, nodeColor: (node) => node.data?.color ?? '#c45c3e' }),
      h(Controls, { showInteractive: false }),
      h(FlowPanel, { visibleDiagramCount })
    )
  );
}

function FlowPanel({ visibleDiagramCount }) {
  const { fitView } = useReactFlow();
  const onReset = useCallback(() => {
    fitView({ padding: 0.16, duration: 240 });
  }, [fitView]);

  return h(
    Panel,
    { position: 'top-left', className: 'flow-panel' },
    h('span', null, `${visibleDiagramCount} diagrams`),
    ' · ',
    h('button', { type: 'button', onClick: onReset }, 'Fit')
  );
}

function DetailPanel({ diagram, openViewer }) {
  if (!diagram) {
    return h('aside', { className: 'diagram-detail' }, h('div', { className: 'detail-body' }, '没有选中的图表'));
  }

  return h(
    'aside',
    { className: 'diagram-detail' },
    h(
      'div',
      { className: 'detail-preview' },
      h('img', { src: srcFor(diagram.file), alt: diagram.title, loading: 'lazy' })
    ),
    h(
      'div',
      { className: 'detail-body' },
      h('div', { className: 'detail-category' }, diagram.categoryName),
      h('h2', null, diagram.title),
      h('p', null, diagram.desc)
    ),
    h(
      'div',
      { className: 'detail-actions' },
      h('button', { className: 'primary', type: 'button', onClick: openViewer }, '大图查看'),
      h('a', { href: srcFor(diagram.file), target: '_blank', rel: 'noreferrer' }, '打开 SVG'),
      h('a', { href: srcFor(diagram.png), target: '_blank', rel: 'noreferrer' }, '打开 PNG')
    )
  );
}

function DiagramViewer({ diagram, zoom, setZoom, onClose }) {
  const zoomOut = useCallback(() => {
    setZoom((value) => Math.max(0.35, Number((value - 0.15).toFixed(2))));
  }, [setZoom]);
  const zoomIn = useCallback(() => {
    setZoom((value) => Math.min(3, Number((value + 0.15).toFixed(2))));
  }, [setZoom]);
  const resetZoom = useCallback(() => {
    setZoom(1);
  }, [setZoom]);
  const onBackdropMouseDown = useCallback((event) => {
    if (event.target === event.currentTarget) onClose();
  }, [onClose]);

  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  return h(
    'div',
    { className: 'viewer-backdrop', onMouseDown: onBackdropMouseDown },
    h(
      'div',
      { className: 'viewer-topbar' },
      h('div', null, h('h2', null, diagram.title), h('span', null, `${diagram.categoryName} · ${diagram.file}`)),
      h(
        'div',
        { className: 'viewer-actions' },
        h('button', { type: 'button', onClick: zoomOut }, '-'),
        h('span', { className: 'zoom-value' }, `${Math.round(zoom * 100)}%`),
        h('button', { type: 'button', onClick: zoomIn }, '+'),
        h('button', { type: 'button', onClick: resetZoom }, '100%'),
        h('a', { href: srcFor(diagram.file), target: '_blank', rel: 'noreferrer' }, 'SVG'),
        h('button', { className: 'primary', type: 'button', onClick: onClose }, '关闭')
      )
    ),
    h(
      'div',
      { className: 'viewer-stage' },
      h('img', {
        src: srcFor(diagram.file),
        alt: diagram.title,
        style: { transform: `scale(${zoom})` }
      })
    )
  );
}

function srcFor(file) {
  return `diagrams/${file}`;
}

function renderFailure(error) {
  const root = document.getElementById('diagram-flow-root');
  if (!root) return;
  root.innerHTML = `<div class="load-error"><strong>React Flow 加载失败</strong><br>${escapeHtml(error.message || String(error))}</div>`;
}

function escapeHtml(value) {
  return value.replace(/[&<>"']/g, (char) => ({
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;'
  })[char]);
}

try {
  const root = document.getElementById('diagram-flow-root');
  createRoot(root).render(h(ReactFlowProvider, null, h(DiagramExplorer)));
} catch (error) {
  renderFailure(error);
  throw error;
}
