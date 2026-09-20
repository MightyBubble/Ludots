type ToolDebugSnapshot = {
  at: string;
  ms: number;
  request: Record<string, unknown>;
  response: unknown;
  error: string | null;
};

type Props = {
  toolName: string;
  snapshot: ToolDebugSnapshot | null;
};

export type { ToolDebugSnapshot };

export function ToolDebugView({ toolName, snapshot }: Props) {
  if (!snapshot) {
    return (
      <div className="debug" data-tool={toolName}>
        <div className="debug-head">
          <span>debug</span>
        </div>
        <div className="debug-empty">尚未调用</div>
      </div>
    );
  }

  const status = snapshot.error ? "err" : "ok";
  const body = snapshot.error
    ? snapshot.error
    : JSON.stringify(snapshot.response, null, 2);

  return (
    <div className="debug" data-tool={toolName}>
      <div className="debug-head">
        <span>debug</span>
        <span className={`tag ${status}`}>{status}</span>
        <span className="meta">{snapshot.ms} ms</span>
        <span className="meta">{formatTime(snapshot.at)}</span>
        <button
          type="button"
          className="ghost"
          onClick={() => void navigator.clipboard.writeText(body)}
        >
          复制
        </button>
      </div>
      <div className="debug-grid">
        <div>
          <h3>req</h3>
          <pre>{JSON.stringify(snapshot.request, null, 2)}</pre>
        </div>
        <div>
          <h3>res</h3>
          <pre className={snapshot.error ? "err" : undefined}>{body}</pre>
        </div>
      </div>
    </div>
  );
}

function formatTime(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleTimeString();
}
