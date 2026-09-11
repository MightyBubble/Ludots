import type { Edge, Node } from '@xyflow/react';

const GAP_X = 300;
const GAP_Y = 190;
const ORIGIN_X = 48;
const ORIGIN_Y = 40;

function layoutDialogueNodes(
  nodes: Node[],
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

  const positions: Record<string, { x: number; y: number }> = {};
  const visiting = new Set<string>();
  let leafCursor = 0;

  const place = (id: string, depth: number): number => {
    if (positions[id]) return positions[id].x;
    visiting.add(id);
    const kids = (children.get(id) ?? []).filter((child) => !visiting.has(child) && child !== id);
    if (kids.length === 0) {
      const x = ORIGIN_X + leafCursor * GAP_X;
      positions[id] = { x, y: ORIGIN_Y + depth * GAP_Y };
      leafCursor += 1;
      return x;
    }
    const childXs = kids.map((child) => place(child, depth + 1));
    const x = (Math.min(...childXs) + Math.max(...childXs)) / 2;
    positions[id] = { x, y: ORIGIN_Y + depth * GAP_Y };
    return x;
  };

  if (nodes.some((node) => node.id === rootId)) {
    place(rootId, 0);
  }
  for (const node of nodes) {
    if (positions[node.id]) continue;
    positions[node.id] = { x: ORIGIN_X + leafCursor * GAP_X, y: ORIGIN_Y };
    leafCursor += 1;
  }
  return positions;
}

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

export type DialogueStatementData = {
  nodeId: string;
  lineId: string;
  linePreview?: string;
  speakerId?: string;
  isEntry: boolean;
  choiceIds: string[];
  missingLine: boolean;
};

export const NEXT_HANDLE = 'next';
export const IN_HANDLE = 'in';

export function choiceHandle(choiceId: string): string {
  return `choice:${choiceId}`;
}

export function parseChoiceHandle(handle: string | null | undefined): string | null {
  if (!handle || !handle.startsWith('choice:')) return null;
  const id = handle.slice('choice:'.length);
  return id.length > 0 ? id : null;
}

export type LinePreview = { id: string; speakerId?: string; textToken?: string };

function previewOf(lineId: string, lines: readonly LinePreview[]): string | undefined {
  const row = lines.find((line) => line.id === lineId);
  if (!row) return undefined;
  return row.textToken || lineId;
}

export function dialogueToFlow(
  tree: DialogueTree,
  lines: readonly LinePreview[] = [],
): { nodes: Node<DialogueStatementData>[]; edges: Edge[] } {
  const nodes: Node<DialogueStatementData>[] = tree.nodes.map((node) => {
    const line = lines.find((row) => row.id === node.lineId);
    return {
      id: node.id,
      type: 'dialogueStatement',
      position: { x: 0, y: 0 },
      data: {
        nodeId: node.id,
        lineId: node.lineId,
        linePreview: previewOf(node.lineId, lines),
        speakerId: line?.speakerId,
        isEntry: node.id === tree.entryNode,
        choiceIds: (node.choices ?? []).map((choice) => choice.id),
        missingLine: !node.lineId,
      },
    };
  });

  const edges: Edge[] = [];
  for (const node of tree.nodes) {
    if (node.nextNode) {
      edges.push({
        id: `next:${node.id}->${node.nextNode}`,
        source: node.id,
        target: node.nextNode,
        sourceHandle: NEXT_HANDLE,
        targetHandle: IN_HANDLE,
        data: { kind: 'next' },
        style: { stroke: '#0a84ff', strokeWidth: 2 },
        label: '接下句',
        labelStyle: { fill: '#8e8e93', fontSize: 10 },
        labelBgStyle: { fill: '#2c2c2e', fillOpacity: 0.9 },
      });
    }
    for (const choice of node.choices ?? []) {
      if (!choice.nextNode) continue;
      edges.push({
        id: `choice:${node.id}:${choice.id}->${choice.nextNode}`,
        source: node.id,
        target: choice.nextNode,
        sourceHandle: choiceHandle(choice.id),
        targetHandle: IN_HANDLE,
        data: { kind: 'choice', choiceId: choice.id, conditionGraphId: choice.conditionGraphId },
        style: { stroke: '#ffd60a', strokeWidth: 2 },
        animated: Boolean(choice.conditionGraphId),
        label: choice.conditionGraphId ? `${choice.id} · 条件` : choice.id,
        labelStyle: { fill: '#ffd60a', fontSize: 10 },
        labelBgStyle: { fill: '#1c1c1e', fillOpacity: 0.9 },
      });
    }
  }

  const positions = layoutDialogueNodes(nodes, edges, tree.entryNode);
  for (const node of nodes) {
    node.position = positions[node.id] ?? node.position;
  }
  return { nodes, edges };
}

export function applyFlowToDialogue(
  tree: DialogueTree,
  flowNodes: Node<DialogueStatementData>[],
  edges: Edge[],
): DialogueTree {
  const prev = new Map(tree.nodes.map((node) => [node.id, node]));
  const nextBySource = new Map<string, string>();
  const choiceNext = new Map<string, string>();

  for (const edge of edges) {
    if (!edge.source || !edge.target) continue;
    const choiceId = parseChoiceHandle(edge.sourceHandle);
    if (choiceId) {
      choiceNext.set(`${edge.source}::${choiceId}`, edge.target);
      continue;
    }
    if (edge.sourceHandle === NEXT_HANDLE || edge.data?.kind === 'next') {
      nextBySource.set(edge.source, edge.target);
    }
  }

  const nodes: DialogueNode[] = flowNodes.map((flowNode) => {
    const prior = prev.get(flowNode.id);
    const choices = (prior?.choices ?? []).map((choice) => {
      const next = choiceNext.get(`${flowNode.id}::${choice.id}`);
      return next ? { ...choice, nextNode: next } : { ...choice, nextNode: undefined };
    });
    const nextNode = nextBySource.get(flowNode.id);
    return {
      ...prior,
      id: flowNode.id,
      lineId: prior?.lineId ?? flowNode.data.lineId ?? '',
      presentationProfile: prior?.presentationProfile,
      cameraId: prior?.cameraId,
      autoAdvanceSeconds: prior?.autoAdvanceSeconds,
      onEnterActionGraphId: prior?.onEnterActionGraphId,
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

export function uniqueDialogueNodeId(existing: Set<string>): string {
  let i = 1;
  while (existing.has(`say_${i}`)) i += 1;
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
