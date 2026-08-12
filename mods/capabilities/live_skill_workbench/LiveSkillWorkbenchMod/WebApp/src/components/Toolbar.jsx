import {
  CheckCircle2,
  CircleAlert,
  Redo2,
  ShieldAlert,
  Undo2
} from 'lucide-react';

export function Toolbar({
  preview,
  connection,
  snapshot,
  onPrecheck,
  onApply,
  localError
}) {
  const connectionLabel = preview
    ? '预览模式'
    : connection.phase === 'connected'
      ? '已连接'
      : connection.phase === 'error'
        ? '连接失败'
        : connection.phase;

  return (
    <header className="lsw-toolbar">
      <div className="lsw-toolbar__brand">
        <strong>实时技能工作台</strong>
        {preview ? <span className="lsw-badge lsw-badge--preview">预览</span> : null}
      </div>
      <div className="lsw-toolbar__meta">
        <span>Mod: {snapshot.modName || '—'}</span>
        <span>会话: {snapshot.sessionId || connection.sessionId || '—'}</span>
        <span className={`lsw-status lsw-status--${connection.phase}`}>{connectionLabel}</span>
        <span>版本 {snapshot.revision ?? 0}</span>
      </div>
      <div className="lsw-toolbar__actions">
        <button type="button" className="lsw-icon-btn" title="撤销（尚未接入）" disabled>
          <Undo2 size={16} />
        </button>
        <button type="button" className="lsw-icon-btn" title="重做（尚未接入）" disabled>
          <Redo2 size={16} />
        </button>
        <button type="button" className="lsw-btn" onClick={onPrecheck} title="候选编译与热应用分级（LiveGasEditPipeline）">
          <ShieldAlert size={14} />
          预检
        </button>
        <button
          type="button"
          className="lsw-btn lsw-btn--primary"
          onClick={onApply}
          disabled={!snapshot.applySupported}
          title={snapshot.applyStatusLabel || '尚未预检；不会应用'}
        >
          <CheckCircle2 size={14} />
          应用到下一次释放
        </button>
      </div>
      {localError || connection.error ? (
        <div className="lsw-toolbar__error" role="alert">
          <CircleAlert size={14} />
          <span>{localError || connection.error}</span>
        </div>
      ) : null}
    </header>
  );
}
