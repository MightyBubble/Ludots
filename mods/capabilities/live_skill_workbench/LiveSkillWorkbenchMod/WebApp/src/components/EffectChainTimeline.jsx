import { memo } from 'react';

function EffectChainTimelineComponent({ events = [] }) {
  if (!events.length) {
    return <div className="lsw-empty">暂无效果链事件。真实采集尚未接入（#621）。</div>;
  }

  const sorted = [...events].sort((a, b) => (a.sequence ?? 0) - (b.sequence ?? 0));

  return (
    <ol className="lsw-timeline">
      {sorted.map((event) => (
        <li key={event.id}>
          <div className="lsw-timeline__seq">{event.sequence}</div>
          <div className="lsw-timeline__body">
            <strong>{event.label}</strong>
            <span className="lsw-timeline__phase">{event.phase}</span>
            <small>{event.definitionId || '—'}</small>
            {event.detail ? <p>{event.detail}</p> : null}
          </div>
        </li>
      ))}
    </ol>
  );
}

export const EffectChainTimeline = memo(EffectChainTimelineComponent);
