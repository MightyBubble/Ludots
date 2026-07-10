import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  CONTROL_PLANE_TOGGLE_PROXY_COMMAND,
  CONTROL_PLANE_TOPIC,
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './dataplane/client.js';
import './styles.css';

const EMPTY_SNAPSHOT = {
  mode: 'control-plane',
  title: 'Command Grant',
  proxyActive: false,
  ownedMembers: [],
  proxiedMembers: [],
  p2DomainMembers: [],
  revision: 0,
  givenText: 'My squad starts with one owned unit and one temporary ally grant',
  whenText: 'The ally command grant is off',
  thenText: 'No temporary ally is commandable yet',
  ownedTitle: 'My Units',
  ownedSubtitle: 'Always under my command',
  proxyTitle: 'Temporary Allies',
  proxySubtitle: 'Granted by the scenario',
  domainTitle: 'Ally Roster'
};

const AUTO_TIMELINE_QUERY_VALUE = 'toggle-revoke';
const AUTO_TIMELINE_ENABLE_DELAY_MS = 2200;
const AUTO_TIMELINE_REVOKE_DELAY_MS = 3300;

const INITIAL_CONNECTION = {
  phase: 'boot',
  transport: 'none',
  sessionId: 'pending',
  topic: CONTROL_PLANE_TOPIC,
  lastPacket: 'none',
  lastCommand: 'none',
  commandAcks: 0,
  error: '',
  demo: false,
  uatTimeline: false
};

function App() {
  const { snapshot, connection, toggleProxy } = useControlPlaneDataPlane();
  const totalVisible = snapshot.ownedMembers.length + snapshot.proxiedMembers.length;
  const isReferee = snapshot.mode === 'referee';
  const commandStatus = formatCommandStatus(connection);
  const panelPurpose = isReferee
    ? 'Referee view: marked units only'
    : 'Temporary ally command grant';
  const thenText = snapshot.thenText || (totalVisible > 0
    ? `${snapshot.ownedMembers.length} owned and ${snapshot.proxiedMembers.length} temporary ally unit(s) are commandable`
    : 'No commandable units are visible yet');

  return (
    <main className="control-plane-panel">
      <header className="panel-header">
        <div>
          <h1>{snapshot.title || 'Command Grant'}</h1>
          <p>{panelPurpose} / {commandStatus}</p>
        </div>
        <button
          type="button"
          className={snapshot.proxyActive ? 'toggle active' : 'toggle'}
          onClick={toggleProxy}
        >
          <span aria-hidden="true" />
          {isReferee
            ? (snapshot.proxyActive ? 'Grant On' : 'Grant Revoked')
            : (snapshot.proxyActive ? 'Ally On' : 'Ally Off')}
        </button>
      </header>

      <section className="summary-strip" aria-label="projection summary">
        <SummaryStat label={isReferee ? 'marked' : 'mine'} value={snapshot.ownedMembers.length} tone="owned" />
        <SummaryStat label={isReferee ? 'grants' : 'ally'} value={snapshot.proxiedMembers.length} tone="proxy" />
        <SummaryStat label="total" value={totalVisible} tone="view" />
      </section>

      <section className={connection.demo ? 'scenario-line demo' : 'scenario-line'}>
        <strong>Given</strong>
        <span>{snapshot.givenText}</span>
        <strong>When</strong>
        <span>{snapshot.whenText}</span>
        <strong>Then</strong>
        <span>{thenText}</span>
      </section>

      <div className="member-columns">
        <MemberGroup
          title={snapshot.ownedTitle}
          subtitle={snapshot.ownedSubtitle}
          members={snapshot.ownedMembers}
          tone="owned"
        />
        <MemberGroup
          title={snapshot.proxyTitle}
          subtitle={snapshot.proxySubtitle}
          members={snapshot.proxiedMembers}
          tone="proxy"
        />
      </div>

      <section className="domain-readout">
        <div className="domain-header">
          <span>{snapshot.domainTitle}</span>
          <strong>{snapshot.p2DomainMembers.length}</strong>
        </div>
        <div className="domain-members">
          {snapshot.p2DomainMembers.length === 0 ? (
            <span className="empty">No ally units visible.</span>
          ) : snapshot.p2DomainMembers.map((member) => (
            <span key={memberKey(member)}>{member.name || 'Unnamed unit'}</span>
          ))}
        </div>
      </section>

      <footer className="transport-row">
        <span>{connection.transport === 'none' ? 'Waiting for host' : 'Panel online'}</span>
        <strong>{commandStatus}</strong>
        <small>{totalVisible} commandable</small>
      </footer>
    </main>
  );
}

function formatCommandStatus(connection) {
  if (connection.error) {
    return 'Host panel unavailable';
  }

  if (connection.commandAcks <= 0) {
    return 'No command sent';
  }

  if (connection.lastCommand === 'toggleProxy:on:ack') {
    return `Grant confirmed x${connection.commandAcks}`;
  }

  if (connection.lastCommand === 'toggleProxy:revoke:ack') {
    return `Revoke confirmed x${connection.commandAcks}`;
  }

  if (connection.lastCommand === 'toggleProxy:on:pending') {
    return 'Grant pending';
  }

  if (connection.lastCommand === 'toggleProxy:revoke:pending') {
    return 'Revoke pending';
  }

  return `Command confirmed x${connection.commandAcks}`;
}

function useControlPlaneDataPlane() {
  const clientRef = useRef(null);
  const autoTimelineRequested = useMemo(() => isAutoTimelineRequested(), []);
  const [snapshot, setSnapshot] = useState(EMPTY_SNAPSHOT);
  const [connection, setConnection] = useState(INITIAL_CONNECTION);

  useEffect(() => {
    let active = true;
    let client = null;
    let retryTimeout = null;
    let autoTimelineScheduled = false;
    const autoTimelineTimeouts = [];

    const scheduleTransportRetry = () => {
      if (!active || retryTimeout != null) {
        return;
      }

      React.startTransition(() => {
        setConnection((current) => ({
          ...current,
          phase: 'waiting-for-host',
          transport: 'none',
          demo: false,
          uatTimeline: autoTimelineRequested,
          lastCommand: 'none',
          commandAcks: 0,
          error: ''
        }));
        setSnapshot(EMPTY_SNAPSHOT);
      });

      retryTimeout = globalThis.setTimeout?.(() => {
        retryTimeout = null;
        connect();
      }, 100) ?? null;
    };

    const runAutoTimelineCommand = async (commandLabel) => {
      if (!active || !client) {
        return;
      }

      React.startTransition(() => {
        setConnection((current) => ({
          ...current,
          lastCommand: `${commandLabel}:pending`,
          error: ''
        }));
      });

      try {
        const response = await client.command(CONTROL_PLANE_TOGGLE_PROXY_COMMAND);
        if (!active) {
          return;
        }

        React.startTransition(() => {
          setConnection((current) => ({
            ...current,
            lastCommand: `${commandLabel}:ack`,
            commandAcks: current.commandAcks + 1,
            error: response.payload?.message ?? ''
          }));
        });
      } catch (error) {
        if (!active) {
          return;
        }

        React.startTransition(() => {
          setConnection((current) => ({
            ...current,
            lastCommand: `${commandLabel}:error`,
            error: error instanceof Error ? error.message : String(error)
          }));
        });
      }
    };

    const scheduleAutoTimeline = () => {
      if (!autoTimelineRequested || autoTimelineScheduled) {
        return;
      }

      autoTimelineScheduled = true;
      const enableTimeout = globalThis.setTimeout?.(
        () => runAutoTimelineCommand('toggleProxy:on'),
        AUTO_TIMELINE_ENABLE_DELAY_MS);
      const revokeTimeout = globalThis.setTimeout?.(
        () => runAutoTimelineCommand('toggleProxy:revoke'),
        AUTO_TIMELINE_REVOKE_DELAY_MS);
      if (enableTimeout != null) {
        autoTimelineTimeouts.push(enableTimeout);
      }

      if (revokeTimeout != null) {
        autoTimelineTimeouts.push(revokeTimeout);
      }
    };

    const connect = () => {
      if (!active || client) {
        return;
      }

      let resolved;
      try {
        resolved = ensureLudotsDataPlaneTransport();
      } catch {
        scheduleTransportRetry();
        return;
      }

      const transport = resolved.transport;
      client = createLudotsDataPlaneClient({
        transport,
        hostBacked: resolved.hostBacked,
        diagnostics: (diagnostic) => {
          if (!active || diagnostic.level !== 'error') {
            return;
          }

          React.startTransition(() => {
            setConnection((current) => ({
              ...current,
              error: diagnostic.message,
              lastPacket: diagnostic.type
            }));
          });
        }
      });
      clientRef.current = client;

      React.startTransition(() => {
        setConnection((current) => ({
          ...current,
          phase: 'connecting',
          transport: transport?.name ?? 'unknown',
          demo: false,
          uatTimeline: autoTimelineRequested,
          error: ''
        }));
      });

      client
        .handshake({ app: 'control-plane-projection-showcase' })
        .then((handshake) => {
          if (!active) {
            return null;
          }

          React.startTransition(() => {
            setConnection((current) => ({
              ...current,
              phase: 'connected',
              sessionId: handshake.sessionId ?? handshake.payload?.sessionId ?? current.sessionId,
              transport: handshake.payload?.transportName ?? transport?.name ?? current.transport
            }));
          });

          scheduleAutoTimeline();

          return client.subscribe(CONTROL_PLANE_TOPIC, (event) => {
            if (!active) {
              return;
            }

            React.startTransition(() => {
              setSnapshot((current) => mergeSnapshot(current, event.payload));
              setConnection((current) => ({
                ...current,
                phase: 'streaming',
                demo: false,
                sessionId: event.sessionId ?? current.sessionId,
                lastPacket: event.kind,
                topic: event.topic ?? current.topic
              }));
            });
          });
        })
        .catch((error) => {
          if (!active) {
            return;
          }

          React.startTransition(() => {
            setConnection((current) => ({
              ...current,
              phase: 'stream-error',
              transport: transport?.name ?? current.transport,
              demo: false,
              uatTimeline: autoTimelineRequested,
              lastCommand: 'none',
              commandAcks: current.commandAcks,
              error: error instanceof Error ? `stream unavailable: ${error.message}` : `stream unavailable: ${String(error)}`
            }));
            setSnapshot(EMPTY_SNAPSHOT);
          });
        });
    };

    connect();

    return () => {
      active = false;
      if (retryTimeout != null) {
        globalThis.clearTimeout?.(retryTimeout);
      }

      for (const timeoutId of autoTimelineTimeouts) {
        globalThis.clearTimeout?.(timeoutId);
      }

      client?.close();
      clientRef.current = null;
    };
  }, [autoTimelineRequested]);

  useEffect(() => {
    window.__LUDOTS_CONTROL_PLANE_READY__ = {
      source: 'control-plane-projection-showcase',
      type: 'ready',
      topic: CONTROL_PLANE_TOPIC,
      alpha: 'transparent-hud'
    };
  }, []);

  const toggleProxy = useCallback(async () => {
    const client = clientRef.current;
    if (!client) {
      return;
    }

    React.startTransition(() => {
      setConnection((current) => ({
        ...current,
        lastCommand: 'toggleProxy:pending',
        error: ''
      }));
    });

    try {
      const response = await client.command(CONTROL_PLANE_TOGGLE_PROXY_COMMAND);
      React.startTransition(() => {
        setConnection((current) => ({
          ...current,
          lastCommand: 'toggleProxy:ack',
          commandAcks: current.commandAcks + 1,
          error: response.payload?.message ?? ''
        }));
      });
    } catch (error) {
      React.startTransition(() => {
      setConnection((current) => ({
        ...current,
        lastCommand: 'toggleProxy:error',
          error: error instanceof Error ? error.message : String(error)
        }));
      });
    }
  }, []);

  return { snapshot, connection, toggleProxy };
}

function isAutoTimelineRequested() {
  try {
    const params = new URLSearchParams(globalThis.location?.search ?? '');
    return params.get('uat') === AUTO_TIMELINE_QUERY_VALUE;
  } catch {
    return false;
  }
}

function SummaryStat({ label, value, tone }) {
  return (
    <div className={`summary-stat ${tone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function MemberGroup({ title, subtitle, members, tone }) {
  const sortedMembers = useMemo(() => {
    return [...members].sort((left, right) =>
      String(left.name ?? '').localeCompare(String(right.name ?? '')) ||
      (left.entityId ?? 0) - (right.entityId ?? 0)
    );
  }, [members]);

  return (
    <section className={`member-group ${tone}`}>
      <header>
        <div>
          <h2>{title}</h2>
          <p>{subtitle}</p>
        </div>
        <strong>{sortedMembers.length}</strong>
      </header>
      <div className="member-list">
        {sortedMembers.length === 0 ? (
          <span className="empty">No members.</span>
        ) : sortedMembers.map((member) => (
          <article className="member-row" key={memberKey(member)}>
            <i aria-hidden="true" />
            <div>
              <strong>{member.name || 'Unnamed unit'}</strong>
              <span>{tone === 'owned' ? 'Owned unit' : 'Temporary ally'}</span>
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

function mergeSnapshot(previous, payload) {
  if (!payload || typeof payload !== 'object') {
    return previous;
  }

  return {
    mode: typeof payload.mode === 'string' ? payload.mode : previous.mode,
    title: typeof payload.title === 'string' ? payload.title : previous.title,
    proxyActive: Boolean(payload.proxyActive),
    ownedMembers: Array.isArray(payload.ownedMembers) ? payload.ownedMembers : previous.ownedMembers,
    proxiedMembers: Array.isArray(payload.proxiedMembers) ? payload.proxiedMembers : previous.proxiedMembers,
    p2DomainMembers: Array.isArray(payload.p2DomainMembers) ? payload.p2DomainMembers : previous.p2DomainMembers,
    revision: Number.isFinite(payload.revision) ? payload.revision : previous.revision,
    givenText: typeof payload.givenText === 'string' ? payload.givenText : previous.givenText,
    whenText: typeof payload.whenText === 'string' ? payload.whenText : previous.whenText,
    thenText: typeof payload.thenText === 'string' ? payload.thenText : previous.thenText,
    ownedTitle: typeof payload.ownedTitle === 'string' ? payload.ownedTitle : previous.ownedTitle,
    ownedSubtitle: typeof payload.ownedSubtitle === 'string' ? payload.ownedSubtitle : previous.ownedSubtitle,
    proxyTitle: typeof payload.proxyTitle === 'string' ? payload.proxyTitle : previous.proxyTitle,
    proxySubtitle: typeof payload.proxySubtitle === 'string' ? payload.proxySubtitle : previous.proxySubtitle,
    domainTitle: typeof payload.domainTitle === 'string' ? payload.domainTitle : previous.domainTitle
  };
}

function memberKey(member) {
  return `${member.entityId ?? 0}:${member.worldId ?? 0}:${member.version ?? 0}`;
}

createRoot(document.getElementById('root')).render(<App />);
