import { useCallback, useEffect, useMemo, useRef } from 'react';
import {
  Background,
  Controls,
  MiniMap,
  ReactFlow,
  useEdgesState,
  useNodesState,
  type Connection,
  type Edge,
  type Node,
  type OnConnect,
  type ReactFlowInstance,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { STUDIO_CHROME, STUDIO_THEME } from '../authoring-studio/authoringTheme';
import { DialogueStatementNode } from './DialogueStatementNode';
import {
  applyFlowToDialogue,
  choiceHandle,
  dialogueToFlow,
  NEXT_HANDLE,
  uniqueChoiceId,
  uniqueDialogueNodeId,
  type DialogueChoice,
  type DialogueNode,
  type DialogueStatementData,
  type DialogueTree,
  type LinePreview,
} from './dialogueTreeModel';

const nodeTypes = { dialogueStatement: DialogueStatementNode };

type Props = {
  tree: DialogueTree;
  lines: readonly LinePreview[];
  selectedNodeId: string;
  onSelectNode: (nodeId: string) => void;
  onChange: (tree: DialogueTree) => void;
};

export function DialogueTreeCanvas({ tree, lines, selectedNodeId, onSelectNode, onChange }: Props) {
  const reactFlowRef = useRef<ReactFlowInstance | null>(null);
  const treeRef = useRef(tree);
  treeRef.current = tree;

  const [nodes, setNodes, onNodesChange] = useNodesState<Node<DialogueStatementData>>([]);
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
      const placed = new Map(prev.map((node) => [node.id, node.position]));
      return flow.nodes.map((node) => ({
        ...node,
        position: placed.get(node.id) ?? node.position,
      }));
    });
    setEdges(flow.edges);
  }, [topologyKey, lines, tree, setEdges, setNodes]);

  useEffect(() => {
    requestAnimationFrame(() => reactFlowRef.current?.fitView({ padding: 0.2 }));
  }, [tree.id]);

  const selectedNode = useMemo(
    () => tree.nodes.find((node) => node.id === selectedNodeId) ?? null,
    [tree.nodes, selectedNodeId],
  );

  const commit = useCallback(
    (nextNodes: Node<DialogueStatementData>[], nextEdges: Edge[], base = treeRef.current) => {
      onChange(applyFlowToDialogue(base, nextNodes, nextEdges));
    },
    [onChange],
  );

  const onConnect: OnConnect = useCallback(
    (connection: Connection) => {
      if (!connection.source || !connection.target || connection.source === connection.target) return;
      const nextEdges = edges.filter((edge) => {
        if (edge.source !== connection.source) return true;
        return edge.sourceHandle !== connection.sourceHandle;
      });
      nextEdges.push({
        id: `${connection.sourceHandle ?? NEXT_HANDLE}:${connection.source}->${connection.target}`,
        source: connection.source,
        target: connection.target,
        sourceHandle: connection.sourceHandle,
        targetHandle: connection.targetHandle,
      });
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
    patchNode({ ...selectedNode, choices: [...(selectedNode.choices ?? []), choice] });
  };

  const relayout = () => {
    const flow = dialogueToFlow(tree, lines);
    setNodes(flow.nodes);
    setEdges(flow.edges);
    requestAnimationFrame(() => reactFlowRef.current?.fitView({ padding: 0.2 }));
  };

  return (
    <div className="grid h-full min-h-[32rem] grid-cols-12 gap-0 overflow-hidden rounded-lg border border-studio-elevated">
      <div className="relative col-span-8 bg-studio-bg">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          onInit={(instance) => {
            reactFlowRef.current = instance;
          }}
          onNodesChange={onNodesChange}
          onEdgesChange={(changes) => {
            onEdgesChange(changes);
          }}
          onConnect={onConnect}
          onEdgesDelete={(deleted) => {
            const ids = new Set(deleted.map((edge) => edge.id));
            const nextEdges = edges.filter((edge) => !ids.has(edge.id));
            commit(nodes, nextEdges);
          }}
          onNodeClick={(_, node) => onSelectNode(node.id)}
          onPaneClick={() => onSelectNode('')}
          fitView
          minZoom={0.15}
          maxZoom={1.8}
          proOptions={{ hideAttribution: true }}
        >
          <Background gap={20} color={STUDIO_THEME.fill} />
          <Controls />
          <MiniMap
            pannable
            zoomable
            bgColor={STUDIO_THEME.bg}
            maskColor="rgba(28,28,30,0.45)"
            nodeColor={(node) =>
              (node.data as DialogueStatementData).isEntry ? STUDIO_THEME.blue : STUDIO_THEME.silver
            }
          />
        </ReactFlow>
        <div className="absolute bottom-3 left-3 z-10 flex gap-2">
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
            node={selectedNode}
            lines={lines}
            onChange={patchNode}
            onAddChoice={addChoice}
            onRemove={() => {
              onChange({
                ...tree,
                nodes: tree.nodes.filter((node) => node.id !== selectedNode.id),
              });
              onSelectNode('');
            }}
          />
        ) : (
          <p className="text-xs text-studio-muted">点画布上的说话节点。黄线是选项，蓝线是无选项时的下一句。条件和副作用仍挂蓝图 id。</p>
        )}
      </aside>
    </div>
  );
}

function StatementInspector({
  node,
  lines,
  onChange,
  onAddChoice,
  onRemove,
}: {
  node: DialogueNode;
  lines: readonly LinePreview[];
  onChange: (node: DialogueNode) => void;
  onAddChoice: () => void;
  onRemove: () => void;
}) {
  const lineIds = lines.map((line) => line.id);
  return (
    <div className="space-y-3">
      <label className={STUDIO_CHROME.label}>
        节点 ID
        <input className={STUDIO_CHROME.field} value={node.id} readOnly />
      </label>
      <label className={STUDIO_CHROME.label}>
        台词
        <select
          className={STUDIO_CHROME.field}
          value={node.lineId}
          onChange={(e) => onChange({ ...node, lineId: e.target.value })}
        >
          <option value="">选择台词</option>
          {(node.lineId && !lineIds.includes(node.lineId) ? [node.lineId, ...lineIds] : lineIds).map((id) => (
            <option key={id} value={id}>
              {id}
            </option>
          ))}
        </select>
      </label>
      <label className={STUDIO_CHROME.label}>
        表现配置
        <input
          className={STUDIO_CHROME.field}
          value={node.presentationProfile ?? ''}
          onChange={(e) => onChange({ ...node, presentationProfile: e.target.value })}
        />
      </label>
      <label className={STUDIO_CHROME.label}>
        镜头
        <input
          className={STUDIO_CHROME.field}
          value={node.cameraId ?? ''}
          onChange={(e) => onChange({ ...node, cameraId: e.target.value })}
        />
      </label>
      <label className={STUDIO_CHROME.label}>
        进句动作图
        <input
          className={STUDIO_CHROME.field}
          value={node.onEnterActionGraphId ?? ''}
          onChange={(e) => onChange({ ...node, onEnterActionGraphId: e.target.value })}
        />
      </label>
      <label className={STUDIO_CHROME.label}>
        自动接下句（秒，0 表示等玩家）
        <input
          className={STUDIO_CHROME.field}
          type="number"
          min={0}
          step={0.1}
          value={node.autoAdvanceSeconds ?? 0}
          onChange={(e) => onChange({ ...node, autoAdvanceSeconds: Number(e.target.value) || 0 })}
        />
      </label>
      <div className="flex items-center justify-between">
        <div className="text-xs text-studio-muted">选项（黄端口）</div>
        <button type="button" className={STUDIO_CHROME.btnGhost} onClick={onAddChoice}>
          + 加选项
        </button>
      </div>
      {(node.choices ?? []).map((choice, index) => (
        <div key={choice.id} className="space-y-2 rounded-md border border-studio-elevated p-2">
          <label className={STUDIO_CHROME.label}>
            选项 ID
            <input
              className={STUDIO_CHROME.field}
              value={choice.id}
              onChange={(e) => {
                const choices = (node.choices ?? []).slice();
                choices[index] = { ...choice, id: e.target.value };
                onChange({ ...node, choices });
              }}
            />
          </label>
          <label className={STUDIO_CHROME.label}>
            台词
            <select
              className={STUDIO_CHROME.field}
              value={choice.lineId}
              onChange={(e) => {
                const choices = (node.choices ?? []).slice();
                choices[index] = { ...choice, lineId: e.target.value };
                onChange({ ...node, choices });
              }}
            >
              <option value="">选择台词</option>
              {(choice.lineId && !lineIds.includes(choice.lineId) ? [choice.lineId, ...lineIds] : lineIds).map((id) => (
                <option key={id} value={id}>
                  {id}
                </option>
              ))}
            </select>
          </label>
          <label className={STUDIO_CHROME.label}>
            条件图
            <input
              className={STUDIO_CHROME.field}
              value={choice.conditionGraphId ?? ''}
              onChange={(e) => {
                const choices = (node.choices ?? []).slice();
                choices[index] = { ...choice, conditionGraphId: e.target.value };
                onChange({ ...node, choices });
              }}
            />
          </label>
          <label className={STUDIO_CHROME.label}>
            动作图
            <input
              className={STUDIO_CHROME.field}
              value={choice.actionGraphId ?? ''}
              onChange={(e) => {
                const choices = (node.choices ?? []).slice();
                choices[index] = { ...choice, actionGraphId: e.target.value };
                onChange({ ...node, choices });
              }}
            />
          </label>
          <p className="text-[10px] text-studio-muted">下一句用画布从「{choiceHandle(choice.id)}」端口拉线。</p>
        </div>
      ))}
      <button type="button" className={STUDIO_CHROME.btnDanger} onClick={onRemove}>
        删除此句
      </button>
    </div>
  );
}
