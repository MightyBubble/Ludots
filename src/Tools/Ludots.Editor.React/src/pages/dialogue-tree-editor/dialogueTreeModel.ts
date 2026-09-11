import { MarkerType, type Edge, type Node } from '@xyflow/react';

const EDGE_STRUCTURE = 'var(--studio-blue)';
const EDGE_CHOICE = 'var(--studio-yellow)';

const GAP_X = 360;
const GAP_Y = 176;
const ORIGIN_X = 48;
const ORIGIN_Y = 40;

export const CHOICE_HUB_SUFFIX = '__mc';
export const NEXT_HANDLE = 'next';
export const IN_HANDLE = 'in';

export type DialogueChoice = {
  id: string;
  lineId: string;
  conditionGraphId?: string;
  actionGraphId?: string;
  nextNode?: string;
};

export type DialogueNode = {
  id: string;
  lineId: string;
  presentationProfile?: string;
  cameraId?: string;
  nextNode?: string;
  autoAdvanceSeconds?: number;
  onEnterActionGraphId?: string;
  choices?: DialogueChoice[];
};

export type DialogueTree = {
  id: string;
  displayName: string;
  displayToken?: string;
  entryNode: string;
  nodes: DialogueNode[];
};

export type DialogueSayData = {
  kind: 'say';
  nodeId: string;
  lineId: string;
  linePreview?: string;
  speakerId?: string;
  isEntry: boolean;
  isFinish: boolean;
  hasChoices: boolean;
  missingLine: boolean;
};

export type DialogueChoicePort = {
  id: string;
  lineId: string;
  preview?: string;
  hasCondition: boolean;
};

export type DialogueChoiceHubData = {
  kind: 'choiceHub';
  ownerId: string;
  choices: DialogueChoicePort[];
};

export type DialogueCanvasData = DialogueSayData | DialogueChoiceHubData;
export type DialogueCanvasNode = Node<DialogueCanvasData>;

export type DialogueEdgeKind = 'next' | 'choice' | 'hub';

export type LinePreview = { id: string; speakerId?: string; textToken?: string };

export function choiceHubId(statementId: string): string {
  return `${statementId}${CHOICE_HUB_SUFFIX}`;
}

export function parseChoiceHubOwner(id: string | null | undefined): string | null {
  if (!id || !id.endsWith(CHOICE_HUB_SUFFIX)) return null;
  const owner = id.slice(0, -CHOICE_HUB_SUFFIX.length);
  return owner.length > 0 ? owner : null;
}

export function isChoiceHubId(id: string | null | undefined): boolean {
  return parseChoiceHubOwner(id) !== null;
}

export function choiceHandle(choiceId: string): string {
  return `choice:${choiceId}`;
}

export function parseChoiceHandle(handle: string | null | undefined): string | null {
  if (!handle || !handle.startsWith('choice:')) return null;
  const id = handle.slice('choice:'.length);
  return id.length > 0 ? id : null;
}

export function canvasOwnerId(node: DialogueCanvasNode): string {
  return node.data.kind === 'choiceHub' ? node.data.ownerId : node.data.nodeId;
}

export function hubWidth(choiceCount: number): number {
  return Math.max(248, choiceCount * 108);
}

function previewOf(lineId: string, lines: readonly LinePreview[]): string | undefined {
  const row = lines.find((line) => line.id === lineId);
  if (!row) return undefined;
  return row.textToken || lineId;
}

function layoutDialogueNodes(
  nodes: DialogueCanvasNode[],
  edges: Edge[],
  rootId: string,
): Record<string, { x: number; y: number }> {
  const children = new Map<string, string[]>();
  for (const edge of edges) {
    if (!edge.source || !edge.target) continue;
    const list = children.get(edge.source) ?? [];
    if (!list.includes(edge.target)) list.push(edge.target);
    children.set(edge.source, list);
  }

  const parentsOf = (id: string) => edges.filter((edge) => edge.target === id).map((edge) => edge.source);

  const isUnder = (root: string, node: string) => {
    const seen = new Set<string>();
    const stack = [...(children.get(root) ?? [])];
    while (stack.length > 0) {
      const cur = stack.pop();
      if (!cur || seen.has(cur)) continue;
      if (cur === node) return true;
      seen.add(cur);
      stack.push(...(children.get(cur) ?? []));
    }
    return false;
  };

  const isPeerJoin = (hubId: string, target: string) =>
    parentsOf(target).some((parent) => parent !== hubId && !isUnder(hubId, parent));

  const positions: Record<string, { x: number; y: number }> = {};
  const visiting = new Set<string>();
  let leafCursor = 0;

  const at = (depth: number, x: number) => ({ x, y: ORIGIN_Y + depth * GAP_Y });
  const depthOf = (id: string) => Math.round((positions[id].y - ORIGIN_Y) / GAP_Y);
  const kidsOf = (id: string) =>
    (children.get(id) ?? []).filter((child) => !visiting.has(child) && child !== id);

  const expandSay = (id: string, depth: number): number => {
    if (visiting.has(id) && positions[id]) return positions[id].x;
    visiting.add(id);
    const kids = kidsOf(id);
    if (!positions[id]) {
      if (kids.length === 0) {
        positions[id] = at(depth, ORIGIN_X + leafCursor * GAP_X);
        leafCursor += 1;
      } else {
        const childXs = kids.map((child) =>
          isChoiceHubId(child) ? placeHub(child, depth + 1) : expandSay(child, depth + 1),
        );
        positions[id] = at(depth, (Math.min(...childXs) + Math.max(...childXs)) / 2);
      }
    } else {
      for (const child of kids) {
        if (isChoiceHubId(child)) placeHub(child, depth + 1);
        else expandSay(child, depth + 1);
      }
    }
    visiting.delete(id);
    return positions[id].x;
  };

  const placeHub = (id: string, depth: number): number => {
    if (positions[id] && !visiting.has(id)) return positions[id].x;
    visiting.add(id);
    const targets = kidsOf(id).filter((target) => !isPeerJoin(id, target));
    const owned: string[] = [];
    for (const target of targets) {
      if (positions[target]) continue;
      positions[target] = at(depth + 1, ORIGIN_X + leafCursor * GAP_X);
      leafCursor += 1;
      owned.push(target);
    }
    for (const target of targets) {
      expandSay(target, depth + 1);
    }
    const owner = parseChoiceHubOwner(id);
    if (owned.length > 0) {
      const xs = owned.map((target) => positions[target].x);
      positions[id] = at(depth, (Math.min(...xs) + Math.max(...xs)) / 2);
    } else if (owner && positions[owner]) {
      positions[id] = at(depth, positions[owner].x);
    } else {
      positions[id] = at(depth, ORIGIN_X + leafCursor * GAP_X);
      leafCursor += 1;
    }
    visiting.delete(id);
    return positions[id].x;
  };

  if (nodes.some((node) => node.id === rootId)) {
    expandSay(rootId, 0);
  }

  let progressed = true;
  let guard = 0;
  while (progressed && guard < nodes.length) {
    progressed = false;
    guard += 1;
    for (const node of nodes) {
      if (positions[node.id]) continue;
      if (isChoiceHubId(node.id)) {
        const owner = parseChoiceHubOwner(node.id);
        if (!owner || !positions[owner]) continue;
        placeHub(node.id, depthOf(owner) + 1);
        progressed = true;
        continue;
      }
      const parents = parentsOf(node.id);
      if (parents.length === 0 || parents.some((parent) => !positions[parent])) continue;
      const parentXs = parents.map((parent) => positions[parent].x);
      const parentDepth = Math.max(...parents.map((parent) => depthOf(parent)));
      positions[node.id] = at(parentDepth + 1, (Math.min(...parentXs) + Math.max(...parentXs)) / 2);
      progressed = true;
    }
  }

  for (const node of nodes) {
    if (positions[node.id]) continue;
    positions[node.id] = at(0, ORIGIN_X + leafCursor * GAP_X);
    leafCursor += 1;
  }

  const shift = (id: string, dx: number, seen: Set<string>) => {
    if (dx === 0 || seen.has(id) || !positions[id]) return;
    seen.add(id);
    positions[id] = { x: positions[id].x + dx, y: positions[id].y };
    for (const child of children.get(id) ?? []) {
      if (!positions[child] || positions[child].y <= positions[id].y) continue;
      shift(child, dx, seen);
    }
  };
  for (const node of nodes) {
    if (!isChoiceHubId(node.id) || !positions[node.id]) continue;
    const owner = parseChoiceHubOwner(node.id);
    if (!owner || !positions[owner]) continue;
    shift(node.id, positions[owner].x - positions[node.id].x, new Set());
  }

  return positions;
}

export function makeDialogueEdge(args: {
  id: string;
  source: string;
  target: string;
  sourceHandle: string;
  targetHandle?: string;
  kind: DialogueEdgeKind;
  animated?: boolean;
}): Edge {
  const stroke = args.kind === 'choice' ? EDGE_CHOICE : EDGE_STRUCTURE;
  return {
    id: args.id,
    type: 'dialogueFlow',
    source: args.source,
    target: args.target,
    sourceHandle: args.sourceHandle,
    targetHandle: args.targetHandle ?? IN_HANDLE,
    data: { kind: args.kind },
    animated: Boolean(args.animated),
    style: { stroke, strokeWidth: args.kind === 'hub' ? 2.25 : 2.75 },
    markerEnd: {
      type: MarkerType.ArrowClosed,
      width: 14,
      height: 14,
      color: stroke,
    },
  };
}

export function dialogueToFlow(
  tree: DialogueTree,
  lines: readonly LinePreview[] = [],
): { nodes: DialogueCanvasNode[]; edges: Edge[] } {
  const nodes: DialogueCanvasNode[] = [];
  const edges: Edge[] = [];

  for (const node of tree.nodes) {
    const line = lines.find((row) => row.id === node.lineId);
    const choices = node.choices ?? [];
    const hasChoices = choices.length > 0;
    const isFinish = !hasChoices && !node.nextNode;
    nodes.push({
      id: node.id,
      type: 'dialogueSay',
      position: { x: 0, y: 0 },
      style: { width: 248 },
      data: {
        kind: 'say',
        nodeId: node.id,
        lineId: node.lineId,
        linePreview: previewOf(node.lineId, lines),
        speakerId: line?.speakerId,
        isEntry: node.id === tree.entryNode,
        isFinish,
        hasChoices,
        missingLine: !node.lineId,
      },
    });

    if (!hasChoices) {
      if (node.nextNode) {
        edges.push(
          makeDialogueEdge({
            id: `next:${node.id}->${node.nextNode}`,
            source: node.id,
            target: node.nextNode,
            sourceHandle: NEXT_HANDLE,
            kind: 'next',
          }),
        );
      }
      continue;
    }

    const hubId = choiceHubId(node.id);
    nodes.push({
      id: hubId,
      type: 'dialogueChoice',
      position: { x: 0, y: 0 },
      deletable: false,
      style: { width: hubWidth(choices.length) },
      data: {
        kind: 'choiceHub',
        ownerId: node.id,
        choices: choices.map((choice) => ({
          id: choice.id,
          lineId: choice.lineId,
          preview: previewOf(choice.lineId, lines),
          hasCondition: Boolean(choice.conditionGraphId),
        })),
      },
    });
    edges.push(
      makeDialogueEdge({
        id: `hub:${node.id}->${hubId}`,
        source: node.id,
        target: hubId,
        sourceHandle: NEXT_HANDLE,
        kind: 'hub',
      }),
    );
    for (const choice of choices) {
      if (!choice.nextNode) continue;
      edges.push(
        makeDialogueEdge({
          id: `choice:${node.id}:${choice.id}->${choice.nextNode}`,
          source: hubId,
          target: choice.nextNode,
          sourceHandle: choiceHandle(choice.id),
          kind: 'choice',
          animated: Boolean(choice.conditionGraphId),
        }),
      );
    }
  }

  applyCenteredLayout(nodes, layoutDialogueNodes(nodes, edges, tree.entryNode));
  return { nodes, edges };
}

function nodeWidth(node: DialogueCanvasNode): number {
  return typeof node.style?.width === 'number' ? node.style.width : 248;
}

function applyCenteredLayout(
  nodes: DialogueCanvasNode[],
  centers: Record<string, { x: number; y: number }>,
): void {
  if (nodes.length === 0) return;
  for (const node of nodes) {
    const center = centers[node.id] ?? node.position;
    node.position = { x: center.x - nodeWidth(node) / 2, y: center.y };
  }
  const minX = Math.min(...nodes.map((node) => node.position.x));
  const dx = ORIGIN_X - minX;
  if (dx === 0) return;
  for (const node of nodes) {
    node.position = { x: node.position.x + dx, y: node.position.y };
  }
}

export function applyFlowToDialogue(
  tree: DialogueTree,
  flowNodes: Node[],
  edges: Edge[],
): DialogueTree {
  const prev = new Map(tree.nodes.map((node) => [node.id, node]));
  const nextBySource = new Map<string, string>();
  const choiceNext = new Map<string, string>();

  for (const edge of edges) {
    if (!edge.source || !edge.target) continue;
    const kind = (edge.data as { kind?: DialogueEdgeKind } | undefined)?.kind;
    if (kind === 'hub') continue;

    const owner = parseChoiceHubOwner(edge.source);
    const choiceId = parseChoiceHandle(edge.sourceHandle);
    if (owner && choiceId) {
      choiceNext.set(`${owner}::${choiceId}`, edge.target);
      continue;
    }
    if (isChoiceHubId(edge.target)) continue;
    if (edge.sourceHandle === NEXT_HANDLE || kind === 'next') {
      nextBySource.set(edge.source, edge.target);
    }
  }

  const sayNodes = flowNodes.filter((flowNode) => flowNode.type === 'dialogueSay' && prev.has(flowNode.id));
  const remainingHubs = new Set(
    flowNodes.filter((flowNode) => flowNode.type === 'dialogueChoice').map((flowNode) => flowNode.id),
  );
  const orderedIds = sayNodes.map((flowNode) => flowNode.id);

  const nodes: DialogueNode[] = orderedIds.map((id) => {
    const prior = prev.get(id);
    if (!prior) {
      return { id, lineId: '', presentationProfile: 'story.dialogue_overlay', choices: [] };
    }
    const hubKept = remainingHubs.has(choiceHubId(id));
    const choices = hubKept
      ? (prior.choices ?? []).map((choice) => {
          const next = choiceNext.get(`${id}::${choice.id}`);
          return next ? { ...choice, nextNode: next } : { ...choice, nextNode: undefined };
        })
      : [];
    const hasChoices = choices.length > 0;
    const nextNode = hasChoices ? undefined : nextBySource.get(id);
    return {
      ...prior,
      id,
      lineId: prior.lineId,
      presentationProfile: prior.presentationProfile,
      cameraId: prior.cameraId,
      autoAdvanceSeconds: prior.autoAdvanceSeconds,
      onEnterActionGraphId: prior.onEnterActionGraphId,
      nextNode,
      choices,
    };
  });

  const stillHaveEntry = nodes.some((node) => node.id === tree.entryNode);
  return {
    ...tree,
    entryNode: stillHaveEntry ? tree.entryNode : (nodes[0]?.id ?? tree.entryNode),
    nodes,
  };
}

export function emptyDialogue(id: string): DialogueTree {
  return {
    id,
    displayName: '新对话',
    entryNode: 'start',
    nodes: [
      {
        id: 'start',
        lineId: '',
        presentationProfile: 'story.dialogue_overlay',
        choices: [],
      },
    ],
  };
}

export function removeDialogueStatement(tree: DialogueTree, nodeId: string): DialogueTree {
  if (tree.nodes.length <= 1) return tree;
  const nodes = tree.nodes
    .filter((node) => node.id !== nodeId)
    .map((node) => ({
      ...node,
      nextNode: node.nextNode === nodeId ? undefined : node.nextNode,
      choices: (node.choices ?? []).map((choice) => ({
        ...choice,
        nextNode: choice.nextNode === nodeId ? undefined : choice.nextNode,
      })),
    }));
  return {
    ...tree,
    entryNode: tree.entryNode === nodeId ? (nodes[0]?.id ?? tree.entryNode) : tree.entryNode,
    nodes,
  };
}

export function removeDialogueChoice(tree: DialogueTree, nodeId: string, choiceId: string): DialogueTree {
  return {
    ...tree,
    nodes: tree.nodes.map((node) =>
      node.id !== nodeId
        ? node
        : { ...node, choices: (node.choices ?? []).filter((choice) => choice.id !== choiceId) },
    ),
  };
}

export function uniqueDialogueNodeId(existing: Set<string>): string {
  let i = 1;
  while (existing.has(`say_${i}`) || existing.has(choiceHubId(`say_${i}`))) i += 1;
  return `say_${i}`;
}

export function uniqueChoiceId(existing: Set<string>): string {
  let i = 1;
  while (existing.has(`choice_${i}`)) i += 1;
  return `choice_${i}`;
}

export function validateDialogueTree(tree: DialogueTree): string | null {
  if (!tree.id.trim()) return '对话必须有 id。';
  if (!tree.entryNode.trim()) return '对话必须有入口节点。';
  if (tree.nodes.length === 0) return '对话至少要有一个节点。';
  const ids = new Set<string>();
  const choiceIds = new Set<string>();
  for (const node of tree.nodes) {
    if (!node.id.trim()) return '有节点没有 id。';
    if (node.id.endsWith(CHOICE_HUB_SUFFIX)) {
      return `节点 id 不能以 ${CHOICE_HUB_SUFFIX} 结尾，那是选项节点用的。`;
    }
    if (ids.has(node.id)) return `节点 id 重复：${node.id}`;
    ids.add(node.id);
    if (!node.lineId.trim()) return `节点 ${node.id} 缺台词 lineId。游戏加载会失败。`;
    if (!node.presentationProfile?.trim()) {
      return `节点 ${node.id} 缺 presentationProfile。游戏加载会失败。`;
    }
    for (const choice of node.choices ?? []) {
      if (!choice.id.trim()) return `节点 ${node.id} 有选项没有 id。`;
      if (choiceIds.has(choice.id)) return `选项 id 重复：${choice.id}`;
      choiceIds.add(choice.id);
      if (!choice.lineId.trim()) return `选项 ${choice.id} 缺台词 lineId。`;
    }
  }
  if (!ids.has(tree.entryNode)) return `入口节点 ${tree.entryNode} 不在树里。`;
  return null;
}
