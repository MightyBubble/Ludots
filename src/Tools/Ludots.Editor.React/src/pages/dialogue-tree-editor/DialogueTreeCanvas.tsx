import { useCallback, useEffect, useMemo, useRef } from 'react';
import {
  Background,
  ConnectionLineType,
  Controls,
  MiniMap,
  ReactFlow,
  useEdgesState,
  useNodesState,
  type Connection,
  type Edge,
  type OnConnect,
  type ReactFlowInstance,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import './dialogueTree.css';
import { STUDIO_CHROME, STUDIO_THEME } from '../authoring-studio/authoringTheme';
import { DialogueChoiceNode } from './DialogueChoiceNode';
import { DialogueFlowEdge } from './DialogueFlowEdge';
import { DialogueSayNode } from './DialogueSayNode';
import { StatementInspector } from './StatementInspector';
import type { LineDraft, SpeakerDraft, SpeakerRow } from './inlineAuthoring';
import {
  applyFlowToDialogue,
  canvasOwnerId,
  choiceHubId,
  dialogueToFlow,
  isChoiceHubId,
  makeDialogueEdge,
  parseChoiceHubOwner,
  NEXT_HANDLE,
  uniqueChoiceId,
  uniqueDialogueNodeId,
  removeDialogueChoice,
  removeDialogueStatement,
  type DialogueCanvasNode,
  type DialogueChoice,
  type DialogueEdgeKind,
  type DialogueNode,
  type DialogueTree,
  type LinePreview,
} from './dialogueTreeModel';

const nodeTypes = { dialogueSay: DialogueSayNode, dialogueChoice: DialogueChoiceNode };
const edgeTypes = { dialogueFlow: DialogueFlowEdge };

type Props = {
  tree: DialogueTree;
  lines: readonly LinePreview[];
  selectedNodeId: string;
  onSelectNode: (nodeId: string) => void;
  onChange: (tree: DialogueTree) => void;
  speakers: readonly SpeakerRow[];
  speakerNameOf: (speakerId: string) => string | undefined;
  defaultTextOf: (token: string) => string;
  drafts: Record<string, LineDraft>;
  onDraft: (key: string, patch: LineDraft) => void;
  onClearDraft: (key: string) => void;
  onQuickAddSpeaker: (draft: SpeakerDraft) => string | null;
};

export function DialogueTreeCanvas({
  tree,
  lines,
  selectedNodeId,
  onSelectNode,
  onChange,
  speakers,
  speakerNameOf,
  defaultTextOf,
  drafts,
  onDraft,
  onClearDraft,
  onQuickAddSpeaker,
}: Props) {
  const reactFlowRef = useRef<ReactFlowInstance | null>(null);
  const treeRef = useRef(tree);
  treeRef.current = tree;

  const [nodes, setNodes, onNodesChange] = useNodesState<DialogueCanvasNode>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);

  const topologyKey = useMemo(
    () =>
      `${tree.id}|${tree.entryNode}|${tree.nodes
        .map((node) => `${node.id}:${(node.choices ?? []).map((choice) => choice.id).join(',')}`)
        .join(';')}`,
    [tree.entryNode, tree.id, tree.nodes],
  );

  useEffect(() => {
    const flow = dialogueToFlow(tree, lines);
    setNodes((prev) => {
      const prevIds = prev.map((node) => node.id).join('|');
      const nextIds = flow.nodes.map((node) => node.id).join('|');
      const sameTopology = prevIds === nextIds;
      const placed = new Map(prev.map((node) => [node.id, node.position]));
      return flow.nodes.map((node) => ({
        ...node,
        position: sameTopology ? (placed.get(node.id) ?? node.position) : node.position,
      }));
    });
    setEdges(flow.edges);
  }, [topologyKey, lines, tree, setEdges, setNodes]);

  const paintedNodes = useMemo(
    () => nodes.map((node) => ({ ...node, selected: canvasOwnerId(node) === selectedNodeId })),
    [nodes, selectedNodeId],
  );

  useEffect(() => {
    requestAnimationFrame(() => reactFlowRef.current?.fitView({ padding: 0.18 }));
  }, [tree.id]);

  const selectedNode = useMemo(
    () => tree.nodes.find((node) => node.id === selectedNodeId) ?? null,
    [tree.nodes, selectedNodeId],
  );

  const commit = useCallback(
    (nextNodes: DialogueCanvasNode[], nextEdges: Edge[], base = treeRef.current) => {
      onChange(applyFlowToDialogue(base, nextNodes, nextEdges));
    },
    [onChange],
  );

  const onConnect: OnConnect = useCallback(
    (connection: Connection) => {
      if (!connection.source || !connection.target || connection.source === connection.target) return;
      if (isChoiceHubId(connection.target)) return;
      const sourceIsHub = isChoiceHubId(connection.source);
      const sourceNode = treeRef.current.nodes.find((node) => node.id === connection.source);
      if (!sourceIsHub && (sourceNode?.choices?.length ?? 0) > 0) {
        if (connection.target !== choiceHubId(connection.source)) return;
      }
      const kind: DialogueEdgeKind = sourceIsHub
        ? 'choice'
        : connection.target === choiceHubId(connection.source)
          ? 'hub'
          : 'next';
      const nextEdges = edges.filter((edge) => {
        if (edge.source !== connection.source) return true;
        return edge.sourceHandle !== connection.sourceHandle;
      });
      nextEdges.push(
        makeDialogueEdge({
          id: `${connection.sourceHandle ?? NEXT_HANDLE}:${connection.source}->${connection.target}`,
          source: connection.source,
          target: connection.target,
          sourceHandle: connection.sourceHandle ?? NEXT_HANDLE,
          targetHandle: connection.targetHandle ?? undefined,
          kind,
        }),
      );
      setEdges(nextEdges);
      commit(nodes, nextEdges);
    },
    [commit, edges, nodes, setEdges],
  );

  const addStatement = () => {
    const ids = new Set(tree.nodes.map((node) => node.id));
    const id = uniqueDialogueNodeId(ids);
    const next: DialogueTree = {
      ...tree,
      nodes: [
        ...tree.nodes,
        { id, lineId: '', presentationProfile: tree.nodes[0]?.presentationProfile || 'story.dialogue_overlay', choices: [] },
      ],
    };
    onChange(next);
    onSelectNode(id);
  };

  const patchNode = (next: DialogueNode) => {
    onChange({
      ...tree,
      nodes: tree.nodes.map((node) => (node.id === next.id ? next : node)),
    });
  };

  const addChoice = () => {
    if (!selectedNode) return;
    const used = new Set(tree.nodes.flatMap((node) => (node.choices ?? []).map((choice) => choice.id)));
    const choice: DialogueChoice = { id: uniqueChoiceId(used), lineId: '' };
    patchNode({ ...selectedNode, choices: [...(selectedNode.choices ?? []), choice], nextNode: undefined });
  };

  const relayout = () => {
    const flow = dialogueToFlow(tree, lines);
    setNodes(flow.nodes);
    setEdges(flow.edges);
    requestAnimationFrame(() => reactFlowRef.current?.fitView({ padding: 0.18 }));
  };

  return (
    <div className="grid h-full min-h-[32rem] grid-cols-12 gap-0 overflow-hidden rounded-lg border border-studio-elevated">
      <div className="relative col-span-8 bg-studio-bg">
        <ReactFlow
          nodes={paintedNodes}
          edges={edges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          defaultEdgeOptions={{ type: 'dialogueFlow' }}
          connectionLineType={ConnectionLineType.Bezier}
          connectionLineStyle={{ stroke: STUDIO_THEME.silver, strokeWidth: 2 }}
          onInit={(instance) => {
            reactFlowRef.current = instance;
          }}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onConnect={onConnect}
          onEdgesDelete={(deleted) => {
            const ids = new Set(deleted.map((edge) => edge.id));
            const nextEdges = edges.filter((edge) => !ids.has(edge.id));
            commit(nodes, nextEdges);
          }}
          onNodesDelete={(deleted) => {
            const ids = new Set(deleted.map((node) => node.id));
            const remaining = nodes.filter((node) => {
              if (ids.has(node.id)) return false;
              const owner = parseChoiceHubOwner(node.id);
              return !(owner && ids.has(owner));
            });
            if (remaining.filter((node) => node.type === 'dialogueSay').length === 0) {
              const flow = dialogueToFlow(treeRef.current, lines);
              setNodes(flow.nodes);
              setEdges(flow.edges);
              return;
            }
            const remainingIds = new Set(remaining.map((node) => node.id));
            const remainingEdges = edges.filter((edge) => remainingIds.has(edge.source) && remainingIds.has(edge.target));
            commit(remaining, remainingEdges);
            if (selectedNodeId && (ids.has(selectedNodeId) || ids.has(choiceHubId(selectedNodeId)))) {
              onSelectNode('');
            }
          }}
          onNodeClick={(_, node) => onSelectNode(canvasOwnerId(node as DialogueCanvasNode))}
          onPaneClick={() => onSelectNode('')}
          fitView
          fitViewOptions={{ padding: 0.22 }}
          minZoom={0.12}
          maxZoom={1.8}
          proOptions={{ hideAttribution: true }}
        >
          <Background gap={22} color={STUDIO_THEME.fill} />
          <Controls position="top-left" />
          <MiniMap
            pannable
            zoomable
            position="bottom-right"
            bgColor={STUDIO_THEME.bg}
            maskColor="color-mix(in srgb, var(--studio-bg) 55%, transparent)"
            nodeColor={(node) => (node.type === 'dialogueChoice' ? STUDIO_THEME.yellow : STUDIO_THEME.blue)}
          />
        </ReactFlow>
        <div className="absolute right-3 top-3 z-10 flex gap-2">
          <button type="button" className={STUDIO_CHROME.btnGhost} onClick={addStatement}>
            加一句
          </button>
          <button type="button" className={STUDIO_CHROME.btnGhost} onClick={relayout}>
            自动排版
          </button>
        </div>
      </div>
      <aside className="col-span-4 space-y-3 overflow-auto border-l border-studio-elevated bg-studio-surface p-4">
        <div className="text-[10px] uppercase tracking-wide text-studio-muted">检查器</div>
        <label className={STUDIO_CHROME.label}>
          对话 ID
          <input
            className={STUDIO_CHROME.field}
            value={tree.id}
            onChange={(e) => onChange({ ...tree, id: e.target.value })}
          />
        </label>
        <label className={STUDIO_CHROME.label}>
          显示名
          <input
            className={STUDIO_CHROME.field}
            value={tree.displayName ?? ''}
            onChange={(e) => onChange({ ...tree, displayName: e.target.value })}
          />
        </label>
        <label className={STUDIO_CHROME.label}>
          入口节点
          <select
            className={STUDIO_CHROME.field}
            value={tree.entryNode}
            onChange={(e) => onChange({ ...tree, entryNode: e.target.value })}
          >
            {tree.nodes.map((node) => (
              <option key={node.id} value={node.id}>
                {node.id}
              </option>
            ))}
          </select>
        </label>
        {selectedNode ? (
          <StatementInspector
            tree={tree}
            node={selectedNode}
            lines={lines}
            speakers={speakers}
            speakerNameOf={speakerNameOf}
            defaultTextOf={defaultTextOf}
            drafts={drafts}
            onDraft={onDraft}
            onClearDraft={onClearDraft}
            onQuickAddSpeaker={onQuickAddSpeaker}
            onChange={patchNode}
            onAddChoice={addChoice}
            onRemoveChoice={(choiceId) => onChange(removeDialogueChoice(tree, selectedNode.id, choiceId))}
            canRemove={tree.nodes.length > 1}
            onRemove={() => {
              onChange(removeDialogueStatement(tree, selectedNode.id));
              onSelectNode('');
            }}
          />
        ) : (
          <p className="text-xs text-studio-muted">
            蓝头是说话，黄头是选项。线从下口接到上口，线上不写字。黄线是选项，蓝线是接下句。
            选中节点后直接在右边写说话人和正文，保存时自动进台词本和文本表。
          </p>
        )}
      </aside>
    </div>
  );
}
