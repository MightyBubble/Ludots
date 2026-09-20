import { formatApplyModeLabel, resolveFieldLabel } from '../displayLabels.js';

export function Inspector({ snapshot }) {
  const changes = snapshot.changes ?? [];
  const diagnostics = snapshot.diagnostics ?? [];
  const unavailable = snapshot.unavailableActions ?? [];
  const fields = snapshot.fields ?? [];

  return (
    <aside className="lsw-inspector">
      <section>
        <header>改动清单</header>
        {changes.length === 0 ? (
          <p className="lsw-muted">尚无暂存改动</p>
        ) : (
          <ul className="lsw-change-list">
            {changes.map((change) => {
              const fieldLabel = resolveFieldLabel(fields, change.fieldPath);
              return (
                <li key={`${change.definitionId}:${change.fieldPath}:${change.afterValue}`}>
                  <strong>{fieldLabel}</strong>
                  {fieldLabel !== change.fieldPath ? (
                    <small className="lsw-muted">{change.fieldPath}</small>
                  ) : null}
                  <span>
                    {formatValue(change.beforeValue)} → {formatValue(change.afterValue)}
                  </span>
                  <small>{formatApplyModeLabel(change.applyMode)}</small>
                </li>
              );
            })}
          </ul>
        )}
      </section>

      <section>
        <header>校验 / 诊断</header>
        {diagnostics.length === 0 ? (
          <p className="lsw-muted">无诊断</p>
        ) : (
          <ul className="lsw-diag-list">
            {diagnostics.map((diag, index) => (
              <li key={`${diag.code}-${index}`} className={`sev-${String(diag.severity).toLowerCase()}`}>
                <strong>{diag.code}</strong>
                <span>{diag.message}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <header>生效方式</header>
        <p className="lsw-apply-mode">
          <strong>{formatApplyModeLabel(snapshot.applyMode)}</strong>
          <span>{snapshot.applyStatusLabel || '尚未预检；不会应用'}</span>
        </p>
        <p className="lsw-muted">
          {snapshot.applySupported
            ? '可以提交到下一次释放。'
            : '尚未预检；不会应用。暂存改动不会写入运行中的游戏。'}
        </p>
      </section>

      <section>
        <header>尚未接入</header>
        <ul className="lsw-unavailable">
          {unavailable.map((action) => (
            <li key={action.actionId}>
              <strong>{action.label}</strong>
              <span>{action.reason}</span>
            </li>
          ))}
        </ul>
      </section>
    </aside>
  );
}

function formatValue(value) {
  if (value == null || Number.isNaN(Number(value))) {
    return '—';
  }
  return String(value);
}
