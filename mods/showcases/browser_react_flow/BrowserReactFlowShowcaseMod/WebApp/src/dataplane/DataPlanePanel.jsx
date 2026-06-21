import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  DATA_PLANE_DEFAULT_TOPIC,
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './client.js';

const MAX_EVENTS = 7;
const MAX_DIAGNOSTICS = 6;

const initialWorldState = {
  tick: 0,
  selectedEntityId: null,
  entities: [],
  metrics: null,
  lastEnvelopeKind: 'none',
  lastBinaryChunks: []
};

export function DataPlanePanel() {
  const [connection, setConnection] = useState({
    phase: 'boot',
    transportName: 'probing',
    installedFake: false,
    sessionId: null,
    topic: DATA_PLANE_DEFAULT_TOPIC,
    subscribed: false,
    error: null
  });
  const [worldState, setWorldState] = useState(initialWorldState);
  const [events, setEvents] = useState([]);
  const [diagnostics, setDiagnostics] = useState([]);
  const [commandState, setCommandState] = useState({
    pending: false,
    lastAck: 'none',
    count: 0,
    target: { x: 10, y: 4 }
  });
  const clientRef = useRef(null);
  const subscriptionRef = useRef(null);

  const selectedEntity = useMemo(
    () => worldState.entities.find((entity) => entity.id === worldState.selectedEntityId) ?? worldState.entities[0] ?? null,
    [worldState.entities, worldState.selectedEntityId]
  );

  useEffect(() => {
    let active = true;
    const root = window;
    const { transport, installedFake } = ensureLudotsDataPlaneTransport({ root });
    const client = createLudotsDataPlaneClient({
      root,
      transport,
      installedFake,
      diagnostics: (diagnostic) => {
        if (!active) {
          return;
        }

        setDiagnostics((current) => [diagnostic, ...current].slice(0, MAX_DIAGNOSTICS));
      }
    });

    clientRef.current = client;
    setConnection((current) => ({
      ...current,
      phase: 'connecting',
      transportName: client.getStatus().transportName,
      installedFake,
      error: null
    }));

    async function connect() {
      try {
        const handshake = await client.handshake({
          showcase: 'BrowserReactFlowShowcaseMod',
          surface: '@xyflow/react',
          page: location.pathname
        });

        if (!active) {
          return;
        }

        setConnection((current) => ({
          ...current,
          phase: 'connected',
          transportName: client.getStatus().transportName,
          sessionId: handshake.sessionId ?? handshake.payload?.sessionId ?? client.getStatus().sessionId,
          error: null
        }));

        const subscription = await client.subscribe(DATA_PLANE_DEFAULT_TOPIC, (event) => {
          setWorldState((current) => applyDataPlaneEvent(current, event));
          setEvents((current) => [formatDataPlaneEvent(event), ...current].slice(0, MAX_EVENTS));
        });

        if (!active) {
          subscription.unsubscribe();
          return;
        }

        subscriptionRef.current = subscription;
        setConnection((current) => ({
          ...current,
          subscribed: true,
          topic: subscription.topic
        }));
      } catch (error) {
        if (!active) {
          return;
        }

        setConnection((current) => ({
          ...current,
          phase: 'error',
          error: error instanceof Error ? error.message : String(error)
        }));
      }
    }

    connect();

    return () => {
      active = false;
      subscriptionRef.current?.unsubscribe();
      subscriptionRef.current = null;
      client.close();
      clientRef.current = null;
    };
  }, []);

  const issueCommand = useCallback(async (commandType, payload) => {
    const client = clientRef.current;
    if (!client) {
      return;
    }

    setCommandState((current) => ({
      ...current,
      pending: true,
      lastAck: `${commandType}: sending`
    }));

    try {
      const ack = await client.command(commandType, payload);
      setCommandState((current) => ({
        ...current,
        pending: false,
        count: current.count + 1,
        lastAck: `${commandType}: tick ${ack.payload?.acceptedAtTick ?? 'ack'}`
      }));
      setEvents((current) => [
        {
          kind: 'command',
          detail: commandType,
          at: Date.now(),
          accent: 'command'
        },
        ...current
      ].slice(0, MAX_EVENTS));
    } catch (error) {
      setCommandState((current) => ({
        ...current,
        pending: false,
        lastAck: error instanceof Error ? error.message : String(error)
      }));
    }
  }, []);

  const selectEntity = useCallback(
    (entityId) => issueCommand('select', { entityId }),
    [issueCommand]
  );

  const issueMoveOrder = useCallback(() => {
    const entity = selectedEntity;
    if (!entity) {
      return;
    }

    const target = {
      x: commandState.target.x,
      y: commandState.target.y
    };
    issueCommand('issueMoveOrder', {
      entityId: entity.id,
      target
    });
  }, [commandState.target.x, commandState.target.y, issueCommand, selectedEntity]);

  const bumpTarget = useCallback((axis, amount) => {
    setCommandState((current) => ({
      ...current,
      target: {
        ...current.target,
        [axis]: clamp(current.target[axis] + amount, 0, 12)
      }
    }));
  }, []);

  return (
    <section className="dataplane-panel" aria-label="Ludots WebUI DataPlane showcase">
      <header className="dataplane-header">
        <div>
          <span className="dataplane-title">WebUI DataPlane</span>
          <strong>{connection.phase}</strong>
        </div>
        <span className={`dataplane-status dataplane-status-${resolveStatusTone(connection)}`}>
          {connection.subscribed ? 'streaming' : connection.installedFake ? 'fake transport' : 'host'}
        </span>
      </header>

      <div className="dataplane-grid">
        <DataPlaneStat label="transport" value={connection.transportName} />
        <DataPlaneStat label="topic" value={connection.topic.split('.').slice(-2).join('.')} />
        <DataPlaneStat label="tick" value={worldState.tick || '-'} />
        <DataPlaneStat label="binary" value={formatBinaryChunks(worldState.lastBinaryChunks)} />
      </div>

      {connection.error ? (
        <div className="dataplane-error">{connection.error}</div>
      ) : null}

      <div className="dataplane-content">
        <div className="dataplane-world">
          <div className="dataplane-section-title">
            <span>snapshot / delta</span>
            <strong>{worldState.lastEnvelopeKind}</strong>
          </div>
          <div className="entity-list">
            {worldState.entities.map((entity) => (
              <button
                key={entity.id}
                className={`entity-row ${entity.id === selectedEntity?.id ? 'selected' : ''}`}
                type="button"
                onClick={() => selectEntity(entity.id)}
                disabled={commandState.pending}
              >
                <span className="entity-main">
                  <strong>{entity.label}</strong>
                  <span>{entity.role} · {entity.order}</span>
                </span>
                <span className="entity-position">{entity.position.x},{entity.position.y}</span>
                <span className="entity-health">
                  <i style={{ width: `${entity.hp}%` }} />
                </span>
              </button>
            ))}
          </div>
        </div>

        <div className="dataplane-command">
          <div className="dataplane-section-title">
            <span>command</span>
            <strong>{commandState.count}</strong>
          </div>
          <div className="command-card">
            <div className="command-target">
              <span>{selectedEntity?.label ?? 'no entity'}</span>
              <strong>{commandState.target.x},{commandState.target.y}</strong>
            </div>
            <div className="command-steppers">
              <button type="button" onClick={() => bumpTarget('x', -1)} aria-label="Move target left">←</button>
              <button type="button" onClick={() => bumpTarget('x', 1)} aria-label="Move target right">→</button>
              <button type="button" onClick={() => bumpTarget('y', -1)} aria-label="Move target up">↑</button>
              <button type="button" onClick={() => bumpTarget('y', 1)} aria-label="Move target down">↓</button>
            </div>
            <button
              className="command-primary"
              type="button"
              onClick={issueMoveOrder}
              disabled={commandState.pending || !selectedEntity}
            >
              issue move order
            </button>
            <span className="command-ack">{commandState.lastAck}</span>
          </div>
        </div>
      </div>

      <div className="dataplane-footer">
        <EventRail events={events} />
        <DiagnosticRail diagnostics={diagnostics} />
      </div>
    </section>
  );
}

function DataPlaneStat({ label, value }) {
  return (
    <div className="dataplane-stat">
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  );
}

function EventRail({ events }) {
  return (
    <div className="event-rail" aria-label="DataPlane events">
      {events.length === 0 ? (
        <span className="empty-event">waiting for stream</span>
      ) : events.map((event, index) => (
        <div key={`${event.at}-${index}`} className={`event-pill event-${event.accent ?? event.kind}`}>
          <strong>{event.kind}</strong>
          <span>{event.detail}</span>
        </div>
      ))}
    </div>
  );
}

function DiagnosticRail({ diagnostics }) {
  return (
    <div className="diagnostic-rail" aria-label="DataPlane diagnostics">
      {diagnostics.slice(0, 3).map((diagnostic, index) => (
        <span key={`${diagnostic.at}-${index}`} className={`diagnostic-dot diagnostic-${diagnostic.level}`}>
          {diagnostic.type}
        </span>
      ))}
    </div>
  );
}

function applyDataPlaneEvent(current, event) {
  if (event.kind === 'snapshot') {
    return {
      tick: event.payload.tick ?? current.tick,
      selectedEntityId: event.payload.selectedEntityId ?? current.selectedEntityId,
      entities: Array.isArray(event.payload.entities) ? event.payload.entities : current.entities,
      metrics: event.payload.metrics ?? current.metrics,
      lastEnvelopeKind: 'snapshot',
      lastBinaryChunks: event.binaryChunks
    };
  }

  if (event.kind === 'delta') {
    const patchMap = new Map((event.payload.entityPatches ?? []).map((patch) => [patch.id, patch.changes ?? {}]));
    const entities = current.entities.map((entity) => ({
      ...entity,
      ...(patchMap.get(entity.id) ?? {})
    }));

    return {
      ...current,
      tick: event.payload.tick ?? current.tick,
      selectedEntityId: event.payload.selectedEntityId ?? current.selectedEntityId,
      entities,
      metrics: event.payload.metrics ?? current.metrics,
      lastEnvelopeKind: 'delta',
      lastBinaryChunks: event.binaryChunks
    };
  }

  return current;
}

function formatDataPlaneEvent(event) {
  const binaryText = event.binaryChunks.length > 0
    ? ` · ${event.binaryChunks.reduce((sum, chunk) => sum + chunk.byteLength, 0)} bytes`
    : '';

  return {
    kind: event.kind,
    detail: `tick ${event.payload.tick ?? '-'}${binaryText}`,
    at: event.timestamp ?? Date.now(),
    accent: event.kind
  };
}

function formatBinaryChunks(chunks) {
  if (!chunks || chunks.length === 0) {
    return '0 bytes';
  }

  const byteLength = chunks.reduce((sum, chunk) => sum + chunk.byteLength, 0);
  return `${byteLength} bytes`;
}

function resolveStatusTone(connection) {
  if (connection.phase === 'error') {
    return 'error';
  }

  if (connection.subscribed) {
    return 'ok';
  }

  return 'warn';
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}
