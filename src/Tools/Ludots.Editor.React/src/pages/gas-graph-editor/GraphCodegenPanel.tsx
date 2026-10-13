import React from 'react';

export type CodegenUnsupportedOp = {
  op: string;
  instructionIndex: number;
  nodeId?: string | null;
  reason: string;
};

export type CodegenPreviewResult = {
  ok: boolean;
  eligible?: boolean;
  emitMode?: string;
  backendRecommended?: string;
  instructionCount?: number;
  yieldPoints?: number[];
  unsupportedOps?: CodegenUnsupportedOp[];
  source?: string | null;
  diagnostics?: string[];
  usesSpecialize?: boolean;
  error?: string;
};

export type CodegenParityResult = {
  ok: boolean;
  matches?: boolean;
  interpretReturnInt?: number;
  codegenReturnInt?: number;
  interpretStatus?: string;
  codegenStatus?: string;
  detail?: string | null;
  emitMode?: string;
  error?: string;
};

type Props = {
  modId: string;
  graphId: string;
  graphBody: unknown;
  executionBackendLabel?: string;
};

export const GraphCodegenPanel: React.FC<Props> = ({
  modId,
  graphId,
  graphBody,
  executionBackendLabel = 'Interpret',
}) => {
  const [preview, setPreview] = React.useState<CodegenPreviewResult | null>(null);
  const [parity, setParity] = React.useState<CodegenParityResult | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [status, setStatus] = React.useState('待命');

  const runPreview = React.useCallback(async () => {
    if (!modId || !graphId) {
      setStatus('先选一张图。');
      return;
    }
    setBusy(true);
    setStatus('预览中…');
    setParity(null);
    try {
      const res = await fetch(
        `/api/mods/${encodeURIComponent(modId)}/gas/graphs/${encodeURIComponent(graphId)}/codegen/preview`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(graphBody ?? {}),
        },
      );
      const payload = (await res.json()) as CodegenPreviewResult;
      setPreview(payload);
      setStatus(
        payload.eligible
          ? `可生成 · ${payload.emitMode ?? '?'} · ${payload.instructionCount ?? 0} 条指令`
          : payload.error ?? '不满足生成条件',
      );
    } catch (err) {
      setStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }, [modId, graphId, graphBody]);

  const runParity = React.useCallback(async () => {
    if (!modId || !graphId) {
      setStatus('先选一张图。');
      return;
    }
    setBusy(true);
    setStatus('对拍中…');
    try {
      const res = await fetch(
        `/api/mods/${encodeURIComponent(modId)}/gas/graphs/${encodeURIComponent(graphId)}/codegen/parity`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(graphBody ?? {}),
        },
      );
      const payload = (await res.json()) as CodegenParityResult;
      setParity(payload);
      setStatus(
        payload.matches
          ? `对拍一致 · 解释=${payload.interpretReturnInt} 生成=${payload.codegenReturnInt}`
          : payload.error ?? payload.detail ?? '对拍不一致',
      );
    } catch (err) {
      setStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  }, [modId, graphId, graphBody]);

  const copySource = React.useCallback(async () => {
    if (!preview?.source) {
      setStatus('还没有可复制的生成源码。');
      return;
    }
    await navigator.clipboard.writeText(preview.source);
    setStatus('已复制生成的 C#。');
  }, [preview]);

  const eligible = preview?.eligible === true;
  const lightClass = preview == null
    ? 'bg-studio-fill'
    : eligible
      ? 'bg-studio-blue'
      : 'bg-studio-red';

  return (
    <div className="space-y-2 border-t border-studio-elevated p-3 text-xs">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 font-semibold uppercase tracking-wide text-studio-blue">
          <span className={`inline-block h-2.5 w-2.5 rounded-full ${lightClass}`} />
          代码生成
        </div>
        <span className="rounded border border-studio-fill px-1.5 py-0.5 font-mono text-[10px] text-studio-secondary">
          后端：{executionBackendLabel}
        </span>
      </div>
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={busy}
          onClick={() => void runPreview()}
          className="rounded bg-studio-blue px-2 py-1 font-semibold text-studio-label hover:brightness-110 disabled:opacity-50"
        >
          预览 C#
        </button>
        <button
          type="button"
          disabled={busy || !eligible}
          onClick={() => void runParity()}
          className="rounded bg-studio-fill px-2 py-1 font-semibold text-studio-label hover:bg-studio-elevated disabled:opacity-50"
        >
          对拍
        </button>
        <button
          type="button"
          disabled={!preview?.source}
          onClick={() => void copySource()}
          className="rounded border border-studio-fill px-2 py-1 text-studio-label hover:bg-studio-elevated disabled:opacity-50"
        >
          复制
        </button>
      </div>
      <div className="text-[10px] text-studio-muted">{status}</div>
      {preview?.unsupportedOps && preview.unsupportedOps.length > 0 ? (
        <div className="max-h-24 overflow-auto rounded border border-studio-red/40 bg-studio-bg p-2 font-mono text-[10px] text-studio-red">
          {preview.unsupportedOps.map((op) => (
            <div key={`${op.op}:${op.instructionIndex}`}>
              [{op.instructionIndex}] {op.op}
              {op.nodeId ? ` · ${op.nodeId}` : ''} — {op.reason}
            </div>
          ))}
        </div>
      ) : null}
      {parity ? (
        <div className={`rounded border p-2 font-mono text-[10px] ${parity.matches ? 'border-studio-blue/50 text-studio-blue' : 'border-studio-red/50 text-studio-red'}`}>
          {parity.matches
            ? `一致 · 返回 ${parity.codegenReturnInt} · ${parity.codegenStatus}`
            : parity.detail ?? parity.error ?? '不一致'}
        </div>
      ) : null}
      <pre className="max-h-48 overflow-auto whitespace-pre-wrap rounded border border-studio-elevated bg-studio-bg p-2 font-mono text-[10px] text-studio-secondary">
        {preview?.source || '点「预览 C#」查看当前图的生成代码。'}
      </pre>
    </div>
  );
};
