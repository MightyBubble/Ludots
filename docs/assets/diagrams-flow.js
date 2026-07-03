import React, { useCallback, useEffect, useMemo, useState } from 'https://esm.sh/react@19.2.0';
import { createRoot } from 'https://esm.sh/react-dom@19.2.0/client';
import {
  Background,
  BackgroundVariant,
  Controls,
  Handle,
  MarkerType,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  Position,
  useReactFlow
} from 'https://esm.sh/@xyflow/react@12.11.0?deps=react@19.2.0,react-dom@19.2.0';

const h = React.createElement;
const FIT_PADDING = 0.12;
const NODE_TEXT_PADDING = 9;
const MIN_EDGE_DISTANCE = 18;
const NODE_GAP = 18;
const VIEWBOX_MARGIN = 18;

export const categories = [
  {
    id: 'engine',
    code: 'ENG',
    name: 'Engine & Architecture',
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
    diagrams: [
      ['vision-fog-flow.svg', '视野与迷雾', 'VisionEmitter → ChunkedField → FogLayer → Visual 的 6 阶段计算']
    ]
  },
  {
    id: 'showcase',
    code: 'UAT',
    name: 'Capability & Showcase',
    diagrams: [
      ['capability-matrix.svg', 'Capability 矩阵', '8 模块 × 4 成熟度等级的热力图'],
      ['showcase-topology.svg', 'Showcase 拓扑', '5 个演示套件与底层引擎能力的映射关系']
    ]
  }
];

export const allDiagrams = categories.flatMap((category) =>
  category.diagrams.map(([file, title, desc]) => ({
    id: file.replace(/\.svg$/, ''),
    file,
    title,
    desc,
    categoryId: category.id,
    categoryName: category.name,
    categoryCode: category.code
  }))
);

const diagramByFile = new Map(allDiagrams.map((diagram) => [diagram.file, diagram]));
const nodeTypes = {
  diagramNode: DiagramNode,
  annotationNode: AnnotationNode
};

function DiagramNode({ data }) {
  const lines = data.lines.length > 0 ? data.lines : [data.title];
  const title = lines[0] ?? data.title;
  const detail = lines.slice(1);

  return h(
    'div',
    {
      className: 'rf-node',
      style: {
        width: `${data.width}px`,
        minHeight: `${Math.max(data.height, 44)}px`,
        '--node-fill': normalizeColor(data.fill, '#ffffff'),
        '--node-stroke': normalizeColor(data.stroke, '#9aa6b2')
      }
    },
    renderHandles(),
    h('div', { className: 'rf-node-title' }, renderTextWithBreaks(title)),
    detail.length > 0
      ? h(
        'div',
        { className: 'rf-node-detail' },
        detail.map((line, index) =>
          h('div', { key: `${line}-${index}`, className: 'rf-node-detail-line' }, renderTextWithBreaks(line))
        )
      )
      : null
  );
}

function AnnotationNode({ data }) {
  return h(
    'div',
    {
      className: `rf-annotation${data.weight >= 700 ? ' strong' : ''}`,
      style: { width: `${data.width}px` }
    },
    data.text
  );
}

function renderHandles() {
  return [
    h(Handle, { key: 't-in', id: 't-target', type: 'target', position: Position.Top, className: 'rf-handle' }),
    h(Handle, { key: 'r-in', id: 'r-target', type: 'target', position: Position.Right, className: 'rf-handle' }),
    h(Handle, { key: 'b-in', id: 'b-target', type: 'target', position: Position.Bottom, className: 'rf-handle' }),
    h(Handle, { key: 'l-in', id: 'l-target', type: 'target', position: Position.Left, className: 'rf-handle' }),
    h(Handle, { key: 't-out', id: 't-source', type: 'source', position: Position.Top, className: 'rf-handle' }),
    h(Handle, { key: 'r-out', id: 'r-source', type: 'source', position: Position.Right, className: 'rf-handle' }),
    h(Handle, { key: 'b-out', id: 'b-source', type: 'source', position: Position.Bottom, className: 'rf-handle' }),
    h(Handle, { key: 'l-out', id: 'l-source', type: 'source', position: Position.Left, className: 'rf-handle' })
  ];
}

export function enhanceDiagramImages(root = document) {
  const images = [...root.querySelectorAll('main img[src*="diagrams/"][src$=".svg"], .main img[src*="diagrams/"][src$=".svg"]')];

  for (const image of images) {
    if (image.dataset.reactFlowDiagram === 'true') continue;

    const src = image.getAttribute('src');
    const title = image.getAttribute('alt') || diagramByFile.get(fileName(src))?.title || 'Diagram';
    const figure = document.createElement('figure');
    const mount = document.createElement('div');
    const fallback = image.cloneNode(true);

    image.dataset.reactFlowDiagram = 'true';
    figure.className = 'rf-inline-diagram';
    mount.className = 'rf-inline-mount';
    fallback.className = 'rf-source-image';
    fallback.removeAttribute('id');

    image.replaceWith(figure);
    figure.append(mount, fallback);

    renderDiagramFlow(mount, {
      src,
      title,
      mode: 'inline',
      onError: () => figure.classList.add('flow-failed')
    });
  }
}

export function renderDiagramFlow(element, options) {
  const root = createRoot(element);
  root.render(h(DiagramFlowApp, options));
  return root;
}

function DiagramFlowApp({ src, title, mode = 'standalone', onError }) {
  const [state, setState] = useState({ status: 'loading', data: null, error: null });

  useEffect(() => {
    let cancelled = false;

    setState({ status: 'loading', data: null, error: null });
    fetch(src)
      .then((response) => {
        if (!response.ok) throw new Error(`Diagram request failed: ${response.status}`);
        return response.text();
      })
      .then((svgText) => {
        if (cancelled) return;
        setState({ status: 'ready', data: parseSvgToFlow(svgText, title), error: null });
      })
      .catch((error) => {
        if (cancelled) return;
        onError?.(error);
        setState({ status: 'error', data: null, error });
      });

    return () => {
      cancelled = true;
    };
  }, [onError, src, title]);

  if (state.status === 'loading') {
    return h('div', { className: 'rf-loading' }, '加载中');
  }

  if (state.status === 'error' || !state.data) {
    return h('div', { className: 'rf-loading' }, '图表加载失败');
  }

  return h(
    ReactFlowProvider,
    null,
    h(DiagramFlowCanvas, {
      data: state.data,
      mode
    })
  );
}

function DiagramFlowCanvas({ data, mode }) {
  const { fitView } = useReactFlow();
  const fitOnLoad = true;
  const defaultViewport = useMemo(() => ({
    x: 18,
    y: 18,
    zoom: Math.min(0.9, Math.max(0.56, 860 / Math.max(data.viewBox.width, 1)))
  }), [data.viewBox.width]);

  useEffect(() => {
    if (!fitOnLoad) return undefined;

    const frame = window.requestAnimationFrame(() => {
      fitView({ padding: FIT_PADDING, duration: 180, minZoom: 0.18, maxZoom: 1.35 });
    });

    return () => window.cancelAnimationFrame(frame);
  }, [data.id, fitOnLoad, fitView]);

  return h(
    'div',
    { className: `rf-canvas rf-canvas-${mode}` },
    h(
      ReactFlow,
      {
        nodes: data.nodes,
        edges: data.edges,
        nodeTypes,
        defaultViewport,
        fitView: fitOnLoad,
        fitViewOptions: { padding: FIT_PADDING, minZoom: 0.18, maxZoom: 1.35 },
        minZoom: 0.12,
        maxZoom: 3,
        nodesDraggable: false,
        nodesConnectable: false,
        elementsSelectable: false,
        panOnScroll: true,
        panOnDrag: true,
        zoomOnDoubleClick: false,
        proOptions: { hideAttribution: true }
      },
      h(Background, { variant: BackgroundVariant.Dots, gap: 26, size: 1, color: 'rgba(100, 116, 139, 0.22)' }),
      mode === 'standalone' ? h(MiniMap, {
        pannable: true,
        zoomable: true,
        nodeColor: (node) => node.data?.stroke ?? '#94a3b8',
        maskColor: 'rgba(241, 245, 249, 0.76)'
      }) : null,
      h(Controls, { position: 'top-left', showInteractive: false })
    )
  );
}

function DiagramExplorer() {
  const [query, setQuery] = useState('');
  const [categoryId, setCategoryId] = useState('all');
  const visibleDiagrams = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    return allDiagrams.filter((diagram) => {
      if (categoryId !== 'all' && diagram.categoryId !== categoryId) return false;
      if (!normalizedQuery) return true;
      return `${diagram.title} ${diagram.desc} ${diagram.categoryName}`.toLowerCase().includes(normalizedQuery);
    });
  }, [categoryId, query]);
  const [selectedId, setSelectedId] = useState(allDiagrams[0].id);
  const selectedDiagram = visibleDiagrams.find((diagram) => diagram.id === selectedId) ?? visibleDiagrams[0] ?? allDiagrams[0];

  useEffect(() => {
    if (selectedDiagram && selectedDiagram.id !== selectedId) {
      setSelectedId(selectedDiagram.id);
    }
  }, [selectedDiagram, selectedId]);

  const onQueryChange = useCallback((event) => setQuery(event.target.value), []);
  const onCategoryChange = useCallback((event) => setCategoryId(event.target.value), []);

  return h(
    'div',
    { className: 'diagram-explorer' },
    h(
      'div',
      { className: 'diagram-explorer-head' },
      h('h1', null, '架构图'),
      h(
        'div',
        { className: 'diagram-explorer-tools' },
        h('input', {
          value: query,
          onChange: onQueryChange,
          placeholder: '搜索图表',
          'aria-label': '搜索图表'
        }),
        h(
          'select',
          { value: categoryId, onChange: onCategoryChange, 'aria-label': '筛选领域' },
          h('option', { value: 'all' }, '全部'),
          categories.map((category) => h('option', { key: category.id, value: category.id }, category.name))
        )
      )
    ),
    h(
      'div',
      { className: 'diagram-explorer-body' },
      h(
        'div',
        { className: 'diagram-list' },
        visibleDiagrams.map((diagram) =>
          h(
            'button',
            {
              key: diagram.id,
              type: 'button',
              className: diagram.id === selectedDiagram.id ? 'active' : '',
              onClick: () => setSelectedId(diagram.id)
            },
            h('span', null, diagram.title),
            h('small', null, diagram.categoryCode)
          )
        )
      ),
      h(
        'section',
        { className: 'diagram-active-view' },
        h('div', { className: 'diagram-active-title' }, selectedDiagram.title),
        h('div', { className: 'diagram-active-canvas' },
          h(DiagramFlowApp, {
            key: selectedDiagram.file,
            src: `diagrams/${selectedDiagram.file}`,
            title: selectedDiagram.title,
            mode: 'standalone'
          })
        )
      )
    )
  );
}

export function parseSvgToFlow(svgText, fallbackTitle = 'Diagram') {
  const parser = new DOMParser();
  const doc = parser.parseFromString(svgText, 'image/svg+xml');
  const svg = doc.querySelector('svg');

  if (!svg || doc.querySelector('parsererror')) {
    throw new Error('Invalid SVG');
  }

  const viewBox = parseViewBox(svg);
  const labels = extractTextLabels(svgText, doc);
  const boxes = extractBoxCandidates(doc, viewBox);
  const { nodes, assignedLabels } = buildNodes(boxes, labels);
  const annotationNodes = buildAnnotations(labels, assignedLabels, viewBox, fallbackTitle);
  relaxAnnotations(annotationNodes, nodes);
  const edges = buildEdges(doc, nodes, viewBox);
  const allNodes = [...nodes, ...annotationNodes];

  if (nodes.length === 0) {
    throw new Error('No diagram structure found');
  }

  return {
    id: fallbackTitle,
    nodes: allNodes,
    edges,
    viewBox: expandViewBox(viewBox, allNodes)
  };
}

function parseViewBox(svg) {
  const raw = svg.getAttribute('viewBox');
  if (raw) {
    const [x, y, width, height] = raw.split(/[\s,]+/).map(Number);
    if (Number.isFinite(width) && Number.isFinite(height)) return { x, y, width, height };
  }

  return {
    x: 0,
    y: 0,
    width: parseUnit(svg.getAttribute('width')) || 1000,
    height: parseUnit(svg.getAttribute('height')) || 700
  };
}

function extractTextLabels(svgText, doc) {
  const labels = [];
  const commentTextPattern = /<g id="text_\d+">\s*<!--\s*([\s\S]*?)\s*-->\s*<g\b([^>]*)>/g;
  let match;

  while ((match = commentTextPattern.exec(svgText)) !== null) {
    const text = decodeEntities(match[1]).replace(/\s+/g, ' ').trim();
    const attrs = match[2];
    const transform = attrValue(attrs, 'transform');
    const style = parseStyle(attrValue(attrs, 'style'));
    const point = parseTranslate(transform);
    if (!text || !point) continue;

    labels.push({
      text,
      x: point.x,
      y: point.y,
      size: parseScaleAsFontSize(transform),
      weight: parseFontWeight(style['font-weight']),
      fill: style.fill ?? '#111827'
    });
  }

  for (const textEl of doc.querySelectorAll('text')) {
    const text = decodeEntities(textEl.textContent ?? '').replace(/\s+/g, ' ').trim();
    if (!text) continue;
    const transformPoint = parseTranslate(textEl.getAttribute('transform'));

    labels.push({
      text,
      x: parseFloat(textEl.getAttribute('x')) || transformPoint?.x || 0,
      y: parseFloat(textEl.getAttribute('y')) || transformPoint?.y || 0,
      size: parseFontSize(textEl),
      weight: parseFontWeight(styleFor(textEl)['font-weight']),
      fill: styleFor(textEl).fill ?? '#111827'
    });
  }

  return dedupeLabels(labels);
}

function extractBoxCandidates(doc, viewBox) {
  const boxes = [];
  let index = 0;

  for (const path of doc.querySelectorAll('g[id^="patch_"] > path')) {
    const d = path.getAttribute('d') ?? '';
    if (!/\bz\b/i.test(d)) continue;

    const style = styleFor(path);
    const fill = style.fill ?? path.getAttribute('fill') ?? '';
    if (!fill || fill === 'none' || fill === 'transparent') continue;

    const bbox = pathBBox(d);
    if (!bbox) continue;
    if (bbox.width < 32 || bbox.height < 18) continue;
    if (bbox.width > viewBox.width * 0.94 && bbox.height > viewBox.height * 0.88) continue;

    boxes.push({
      id: `box-${index++}`,
      ...bbox,
      fill,
      stroke: style.stroke ?? path.getAttribute('stroke') ?? '#94a3b8',
      area: bbox.width * bbox.height
    });
  }

  return boxes;
}

function buildNodes(boxes, labels) {
  const assignedLabels = new Set();
  const nodes = [];

  const sortedBoxes = [...boxes].sort((a, b) => a.area - b.area);
  for (const box of sortedBoxes) {
    const boxLabels = labels
      .filter((label, labelIndex) => {
        if (assignedLabels.has(labelIndex)) return false;
        return isLabelInsideBox(label, box);
      })
      .sort((a, b) => a.y - b.y || a.x - b.x);

    if (boxLabels.length === 0) continue;

    if (isDecorativeMarkerBox(box, boxLabels)) {
      continue;
    }

    for (const label of boxLabels) {
      assignedLabels.add(labels.indexOf(label));
    }

    if (box.area < 2600 && boxLabels.every((label) => isBadgeLabel(label.text))) {
      continue;
    }

    const contentLabels = boxLabels.filter((label) => !isBadgeLabel(label.text));
    const visibleLabels = contentLabels.length > 0 ? contentLabels : boxLabels;
    const width = estimateNodeWidth(visibleLabels, box.width);
    const height = estimateNodeHeight(visibleLabels, box.height, width);
    const positionX = box.x - (width - box.width) / 2;

    nodes.push({
      id: `node-${nodes.length}`,
      type: 'diagramNode',
      position: { x: positionX, y: box.y },
      data: {
        title: visibleLabels[0].text,
        lines: visibleLabels.map((label) => label.text),
        fill: box.fill,
        stroke: box.stroke,
        width,
        height,
        bbox: box
      }
    });
  }

  relaxNodeLayout(nodes);
  nodes.sort((a, b) => a.position.y - b.position.y || a.position.x - b.position.x);
  return { nodes, assignedLabels };
}

function buildAnnotations(labels, assignedLabels, viewBox, fallbackTitle) {
  const annotations = [];
  const topCutoff = viewBox.y + viewBox.height * 0.13;

  labels.forEach((label, index) => {
    if (assignedLabels.has(index)) return;
    if (label.y < topCutoff && label.text !== fallbackTitle) return;
    if (label.text.length > 80 && label.y < topCutoff * 1.3) return;

    const width = Math.min(Math.max(label.text.length * Math.max(label.size || 9, 8) * 0.62 + 18, 80), 340);
    annotations.push({
      id: `annotation-${index}`,
      type: 'annotationNode',
      position: { x: label.x, y: label.y - Math.max(label.size || 10, 10) },
      data: {
        text: label.text,
        width,
        height: Math.max(label.size || 10, 12),
        weight: label.weight
      },
      draggable: false,
      selectable: false
    });
  });

  return annotations;
}

function buildEdges(doc, nodes, viewBox) {
  const edges = [];
  const nodeRects = nodes.map((node) => ({
    id: node.id,
    x: node.position.x,
    y: node.position.y,
    width: node.data.width,
    height: node.data.height
  }));
  const maxDistance = Math.max(80, Math.min(viewBox.width, viewBox.height) * 0.16);

  for (const group of doc.querySelectorAll('g[id^="patch_"], g[id^="line2d_"]')) {
    const paths = [...group.querySelectorAll('path')];
    const firstPath = [...group.querySelectorAll('path')].find((path) => {
      const d = path.getAttribute('d') ?? '';
      const style = styleFor(path);
      return !/\bz\b/i.test(d) && (style.fill === 'none' || !style.fill) && style.stroke && style.stroke !== 'none';
    });

    if (!firstPath) continue;

    const points = pathPoints(firstPath.getAttribute('d') ?? '');
    if (points.length < 2) continue;

    const start = points[0];
    const end = points[points.length - 1];
    if (distance(start, end) < MIN_EDGE_DISTANCE) continue;

    const source = nearestNode(start, nodeRects);
    const target = nearestNode(end, nodeRects);
    if (!source || !target || source.id === target.id) continue;
    if (source.distance > maxDistance || target.distance > maxDistance) continue;

    const stroke = styleFor(firstPath).stroke ?? '#64748b';
    const directed = group.id.startsWith('patch_') && paths.length > 1;
    edges.push({
      id: `edge-${edges.length}-${source.id}-${target.id}`,
      source: source.id,
      target: target.id,
      sourceHandle: `${sideForPoint(start, source.rect)}-source`,
      targetHandle: `${sideForPoint(end, target.rect)}-target`,
      type: directed ? 'smoothstep' : 'straight',
      markerEnd: directed ? { type: MarkerType.ArrowClosed, color: normalizeColor(stroke, '#64748b') } : undefined,
      style: {
        stroke: normalizeColor(stroke, '#64748b'),
        strokeWidth: 1.7,
        opacity: 0.82
      }
    });
  }

  return edges;
}

function isLabelInsideBox(label, box) {
  const pad = NODE_TEXT_PADDING;
  return label.x >= box.x - pad
    && label.x <= box.x + box.width + pad
    && label.y >= box.y - pad
    && label.y <= box.y + box.height + pad * 1.8;
}

function nearestNode(point, rects) {
  let best = null;
  for (const rect of rects) {
    const currentDistance = distanceToRect(point, rect);
    if (!best || currentDistance < best.distance) {
      best = { id: rect.id, rect, distance: currentDistance };
    }
  }
  return best;
}

function sideForPoint(point, rect) {
  const distances = [
    ['t', Math.abs(point.y - rect.y)],
    ['r', Math.abs(point.x - (rect.x + rect.width))],
    ['b', Math.abs(point.y - (rect.y + rect.height))],
    ['l', Math.abs(point.x - rect.x)]
  ];
  distances.sort((a, b) => a[1] - b[1]);
  return distances[0][0];
}

function distanceToRect(point, rect) {
  const dx = Math.max(rect.x - point.x, 0, point.x - (rect.x + rect.width));
  const dy = Math.max(rect.y - point.y, 0, point.y - (rect.y + rect.height));
  return Math.hypot(dx, dy);
}

function pathBBox(d) {
  const points = pathPoints(d);
  if (points.length === 0) return null;

  const xs = points.map((point) => point.x);
  const ys = points.map((point) => point.y);
  const x = Math.min(...xs);
  const y = Math.min(...ys);
  const maxX = Math.max(...xs);
  const maxY = Math.max(...ys);
  return { x, y, width: maxX - x, height: maxY - y };
}

function pathPoints(d) {
  const values = d.match(/-?(?:\d+\.\d+|\d+|\.\d+)(?:e[-+]?\d+)?/gi)?.map(Number) ?? [];
  const points = [];
  for (let i = 0; i < values.length - 1; i += 2) {
    points.push({ x: values[i], y: values[i + 1] });
  }
  return points.filter((point) => Number.isFinite(point.x) && Number.isFinite(point.y));
}

function parseTranslate(transform) {
  const match = /translate\(([-\d.eE+]+)[,\s]+([-\d.eE+]+)\)/.exec(transform ?? '');
  if (!match) return null;
  return { x: Number(match[1]), y: Number(match[2]) };
}

function parseScaleAsFontSize(transform) {
  const match = /scale\(([-\d.eE+]+)/.exec(transform ?? '');
  if (!match) return 10;
  return Math.max(7, Math.abs(Number(match[1])) * 100);
}

function parseFontSize(textEl) {
  const style = styleFor(textEl);
  const raw = style['font-size'] ?? textEl.getAttribute('font-size');
  return parseUnit(raw) || 10;
}

function parseFontWeight(value) {
  if (!value) return 400;
  if (value === 'bold') return 700;
  return Number(value) || 400;
}

function parseUnit(value) {
  if (!value) return 0;
  const match = /[-\d.]+/.exec(value);
  return match ? Number(match[0]) : 0;
}

function styleFor(element) {
  return parseStyle(element.getAttribute('style'));
}

function parseStyle(style = '') {
  return Object.fromEntries(
    style
      .split(';')
      .map((part) => part.trim())
      .filter(Boolean)
      .map((part) => {
        const index = part.indexOf(':');
        if (index < 0) return [part, ''];
        return [part.slice(0, index).trim(), part.slice(index + 1).trim()];
      })
  );
}

function attrValue(attrs, name) {
  const match = new RegExp(`${name}="([^"]*)"`).exec(attrs ?? '');
  return match?.[1] ?? '';
}

function decodeEntities(value) {
  const textarea = document.createElement('textarea');
  textarea.innerHTML = value;
  return textarea.value;
}

function dedupeLabels(labels) {
  const seen = new Set();
  return labels.filter((label) => {
    const key = `${label.text}|${Math.round(label.x)}|${Math.round(label.y)}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function distance(a, b) {
  return Math.hypot(a.x - b.x, a.y - b.y);
}

function fileName(src = '') {
  return src.split('/').pop()?.split('?')[0] ?? '';
}

function normalizeColor(color, fallback) {
  if (!color || color === 'none') return fallback;
  return color;
}

function estimateNodeWidth(labels, originalWidth) {
  const longest = labels.reduce((max, label) => Math.max(max, visualTextWidth(label.text)), 0);
  const expansion = originalWidth < 120 ? 1.72 : originalWidth < 170 ? 1.38 : 1.2;
  const readableWidth = Math.min(longest + 28, originalWidth * expansion, 236);
  return Math.max(originalWidth, readableWidth);
}

function estimateNodeHeight(labels, originalHeight, width) {
  const textWidth = Math.max(width - 22, 48);
  const lineCount = labels.reduce((sum, label) => {
    return sum + Math.max(1, Math.ceil(visualTextWidth(label.text) / textWidth));
  }, 0);
  return Math.max(originalHeight, lineCount * 17 + 18, 42);
}

function visualTextWidth(text) {
  return [...text].reduce((sum, char) => sum + (char.charCodeAt(0) > 255 ? 14 : 7.4), 0);
}

function isDecorativeMarkerBox(box, labels) {
  const aspect = box.width / Math.max(box.height, 1);
  const markerSized = box.area < 3200 && aspect >= 0.65 && aspect <= 1.45;
  if (!markerSized) return false;

  return labels.some((label) => visualTextWidth(label.text) > box.width * 1.35);
}

function relaxNodeLayout(nodes) {
  const rows = groupNodesByRow(nodes);

  for (const row of rows) {
    row.sort((a, b) => nodeCenter(a).x - nodeCenter(b).x);
    for (let index = 1; index < row.length; index += 1) {
      const previous = row[index - 1];
      const current = row[index];
      const minX = previous.position.x + previous.data.width + NODE_GAP;
      if (current.position.x < minX) {
        current.position.x = minX;
      }
    }
  }

  resolveNodeCollisions(nodes);
}

function groupNodesByRow(nodes) {
  const rows = [];
  const sorted = [...nodes].sort((a, b) => nodeCenter(a).y - nodeCenter(b).y);

  for (const node of sorted) {
    const center = nodeCenter(node).y;
    const row = rows.find((candidate) => {
      const rowCenter = candidate.reduce((sum, item) => sum + nodeCenter(item).y, 0) / candidate.length;
      return Math.abs(rowCenter - center) < Math.max(32, node.data.height * 0.55);
    });

    if (row) {
      row.push(node);
    } else {
      rows.push([node]);
    }
  }

  return rows;
}

function resolveNodeCollisions(nodes) {
  for (let iteration = 0; iteration < 6; iteration += 1) {
    let moved = false;

    for (let i = 0; i < nodes.length; i += 1) {
      for (let j = i + 1; j < nodes.length; j += 1) {
        const first = nodes[i];
        const second = nodes[j];
        const overlap = nodeOverlap(first, second);
        if (!overlap) continue;

        const firstCenter = nodeCenter(first);
        const secondCenter = nodeCenter(second);
        if (overlap.x < overlap.y) {
          const delta = overlap.x + NODE_GAP;
          if (firstCenter.x <= secondCenter.x) {
            second.position.x += delta;
          } else {
            first.position.x += delta;
          }
        } else {
          const delta = overlap.y + NODE_GAP * 0.55;
          if (firstCenter.y <= secondCenter.y) {
            second.position.y += delta;
          } else {
            first.position.y += delta;
          }
        }

        moved = true;
      }
    }

    if (!moved) break;
  }
}

function nodeOverlap(first, second) {
  const firstRect = nodeRect(first);
  const secondRect = nodeRect(second);
  const x = Math.min(firstRect.right, secondRect.right) - Math.max(firstRect.left, secondRect.left);
  const y = Math.min(firstRect.bottom, secondRect.bottom) - Math.max(firstRect.top, secondRect.top);
  return x > 0 && y > 0 ? { x, y } : null;
}

function nodeRect(node) {
  const width = node.data.width;
  const height = node.data.height ?? 16;
  return {
    left: node.position.x,
    top: node.position.y,
    right: node.position.x + width,
    bottom: node.position.y + height
  };
}

function nodeCenter(node) {
  const width = node.data.width;
  const height = node.data.height ?? 16;
  return {
    x: node.position.x + width / 2,
    y: node.position.y + height / 2
  };
}

function relaxAnnotations(annotations, nodes) {
  for (const annotation of annotations) {
    for (let iteration = 0; iteration < 4; iteration += 1) {
      const collision = nodes.find((node) => nodeOverlap(annotation, node));
      if (!collision) break;

      const overlap = nodeOverlap(annotation, collision);
      const annotationCenter = nodeCenter(annotation);
      const collisionCenter = nodeCenter(collision);
      const horizontalDistance = Math.abs(annotationCenter.x - collisionCenter.x);
      const verticalDistance = Math.abs(annotationCenter.y - collisionCenter.y);

      if (horizontalDistance > verticalDistance * 0.65) {
        const direction = annotationCenter.x >= collisionCenter.x ? 1 : -1;
        annotation.position.x += direction * (overlap.x + 8);
      } else {
        const direction = annotationCenter.y >= collisionCenter.y ? 1 : -1;
        annotation.position.y += direction * (overlap.y + 8);
      }
    }
  }
}

function expandViewBox(viewBox, nodes) {
  if (nodes.length === 0) return viewBox;

  const rects = nodes.map(nodeRect);
  const left = Math.min(viewBox.x, ...rects.map((rect) => rect.left)) - VIEWBOX_MARGIN;
  const top = Math.min(viewBox.y, ...rects.map((rect) => rect.top)) - VIEWBOX_MARGIN;
  const right = Math.max(viewBox.x + viewBox.width, ...rects.map((rect) => rect.right)) + VIEWBOX_MARGIN;
  const bottom = Math.max(viewBox.y + viewBox.height, ...rects.map((rect) => rect.bottom)) + VIEWBOX_MARGIN;

  return {
    x: left,
    y: top,
    width: right - left,
    height: bottom - top
  };
}

function isBadgeLabel(text) {
  return /^[0-9]+$/.test(text.trim());
}

function renderTextWithBreaks(text) {
  const parts = text
    .replace(/([a-z0-9])([A-Z])/g, '$1\u200b$2')
    .replace(/([A-Z])([A-Z][a-z])/g, '$1\u200b$2')
    .split('\u200b');

  return parts.flatMap((part, index) => (
    index === parts.length - 1 ? [part] : [part, h('wbr', { key: `${part}-${index}` })]
  ));
}

function bootstrapExplorer() {
  const root = document.getElementById('diagram-flow-root');
  if (!root) return;

  createRoot(root).render(h(DiagramExplorer));
}

bootstrapExplorer();
