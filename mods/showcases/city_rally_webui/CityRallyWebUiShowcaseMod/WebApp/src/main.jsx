import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  RTS_PRODUCTION_TOPIC,
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './dataplane/client.js';
import './styles.css';
import catapultArt from '../../../../rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Presentation/billboards/catapult.png';
import cityArt from '../../../../rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Presentation/billboards/city.png';
import mineArt from '../../../../rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Presentation/billboards/mine.png';
import ramArt from '../../../../rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Presentation/billboards/ram.png';
import stableArt from '../../../../rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Presentation/billboards/stable.png';
import towerArt from '../../../../rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Presentation/billboards/tower.png';
import workshopArt from '../../../../rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Presentation/billboards/workshop.png';
import workerArt from '../../../../ux_prototype/UxPrototypeMod/assets/Presentation/billboards/worker.png';

const ENTITY_ART = {
  catapult: catapultArt,
  city: cityArt,
  mine: mineArt,
  ram: ramArt,
  stable: stableArt,
  tower: towerArt,
  worker: workerArt,
  workshop: workshopArt
};

const EMPTY_STATE = {
  tick: 0,
  mapId: '',
  flavor: 'shared',
  activeFactionId: 'team-1',
  resources: [],
  factions: [],
  entities: [],
  garrison: [],
  selection: {
    entityKey: '',
    name: 'No entity selected',
    kind: '',
    teamId: 0,
    health: 0,
    shield: 0,
    members: []
  },
  commands: {
    targetEntityKey: '',
    revision: 0,
    canActivate: false,
    groups: [],
    statuses: [],
    queue: [],
    message: ''
  },
  buildables: [],
  productionQueue: [],
  techTree: { nodes: [], edges: [] },
  diplomacy: { rows: [], proposals: [] },
  diagnostics: {
    messages: [],
    lastCommand: 'none',
    lastCommandStatus: 'idle',
    commandCount: 0
  }
};

const INITIAL_CONNECTION = {
  phase: 'boot',
  transport: 'none',
  sessionId: 'pending',
  topic: RTS_PRODUCTION_TOPIC,
  tick: 0,
  entityCount: 0,
  lastPacket: 'none',
  lastCommand: 'none',
  commandAcks: 0,
  error: '',
  hostBacked: false
};

function App() {
  const { snapshot, connection, command } = useRtsDataPlane();
  const activeFaction = snapshot.factions.find((faction) => faction.id === snapshot.activeFactionId);
  const diagnosticMessages = useMemo(() => {
    return snapshot.diagnostics?.messages ?? [];
  }, [snapshot.diagnostics]);

  const activateSlot = useCallback((groupIndex, slotIndex) => {
    command('activateAbilitySlot', {
      entityKey: snapshot.selection.entityKey,
      groupIndex,
      slotIndex
    });
  }, [command, snapshot.selection.entityKey]);

  return (
    <main className={`app flavor-${snapshot.flavor}`}>
      <div className="hud-layer">
        <TopHud
          snapshot={snapshot}
          activeFaction={activeFaction}
          connection={connection}
        />
        <EntityRoster
          entities={snapshot.entities}
          selectedKey={snapshot.selection.entityKey}
          onSelect={(entityKey) => command('selectEntity', { entityKey })}
        />
        <RightHud
          selection={snapshot.selection}
          garrison={snapshot.garrison ?? []}
          productionQueue={snapshot.productionQueue}
          statuses={snapshot.commands.statuses}
          diplomacy={snapshot.diplomacy}
          techTree={snapshot.techTree}
          onSelect={command}
          onCancelPlanting={command}
        />
        <BottomHud
          commands={snapshot.commands}
          selection={snapshot.selection}
          onActivate={activateSlot}
        />
        <DiagnosticsHud
          snapshot={snapshot}
          connection={connection}
          messages={diagnosticMessages}
        />
      </div>
    </main>
  );
}

function useRtsDataPlane() {
  const clientRef = useRef(null);
  const [snapshot, setSnapshot] = useState(EMPTY_STATE);
  const [connection, setConnection] = useState(INITIAL_CONNECTION);

  useEffect(() => {
    let active = true;
    let transport = null;
    let hostBacked = false;
    try {
      const resolved = ensureLudotsDataPlaneTransport();
      transport = resolved.transport;
      hostBacked = resolved.hostBacked;
    } catch (error) {
      React.startTransition(() => {
        setConnection((current) => ({
          ...current,
          phase: 'error',
          error: error instanceof Error ? error.message : String(error)
        }));
      });
      return () => {
        active = false;
      };
    }

    const client = createLudotsDataPlaneClient({
      transport,
      hostBacked,
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
        hostBacked,
        error: ''
      }));
    });

    client
      .handshake({ app: 'browser-rts-production-showcase' })
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

        return client.subscribe(RTS_PRODUCTION_TOPIC, (event) => {
          if (!active) {
            return;
          }

          React.startTransition(() => {
            setSnapshot((current) => mergeSnapshot(current, event.payload));
            setConnection((current) => reduceDataPlaneEvent(current, event));
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
            phase: 'error',
            error: error instanceof Error ? error.message : String(error)
          }));
        });
      });

    return () => {
      active = false;
      client.close();
      if (transport?.dispose) {
        transport.dispose();
      }
    };
  }, []);

  useEffect(() => {
    const ready = {
      source: 'browser-rts-production-showcase',
      type: 'ready',
      topic: RTS_PRODUCTION_TOPIC,
      alpha: 'transparent-hud',
      noCenterMap: true
    };
    window.__LUDOTS_RTS_PRODUCTION_READY__ = ready;
  }, []);

  useEffect(() => {
    const publishResize = () => {
      const payload = {
        source: 'browser-rts-production-showcase',
        type: 'viewport-resize',
        width: window.innerWidth,
        height: window.innerHeight,
        devicePixelRatio: window.devicePixelRatio || 1
      };
      window.__LUDOTS_RTS_PRODUCTION_VIEWPORT__ = payload;
    };

    publishResize();
    window.addEventListener('resize', publishResize);
    return () => window.removeEventListener('resize', publishResize);
  }, []);

  const command = useCallback(async (name, payload = {}) => {
    const client = clientRef.current;
    if (!client) {
      return;
    }

    React.startTransition(() => {
      setConnection((current) => ({
        ...current,
        lastCommand: `${name}:pending`,
        error: ''
      }));
    });

    try {
      const response = await client.command(name, payload);
      React.startTransition(() => {
        setConnection((current) => ({
          ...current,
          lastCommand: `${name}:ack`,
          commandAcks: current.commandAcks + 1,
          error: response.payload?.message ?? ''
        }));
      });
    } catch (error) {
      React.startTransition(() => {
        setConnection((current) => ({
          ...current,
          lastCommand: `${name}:error`,
          error: error instanceof Error ? error.message : String(error)
        }));
      });
    }
  }, []);

  return { snapshot, connection, command };
}

function TopHud({ snapshot, activeFaction, connection }) {
  return (
    <header className="top-hud hud-panel">
      <div className="brand-block">
        <span className="brand-mark" />
        <div>
          <h1>{titleForFlavor(snapshot.flavor)}</h1>
          <p>{subtitleForFlavor(snapshot.flavor, snapshot.mapId, snapshot.tick)}</p>
        </div>
      </div>
      <div className="resource-strip">
        {snapshot.resources.map((resource) => (
          <div className="resource-chip" key={resource.name}>
            <span>{resource.name}</span>
            <strong>{formatNumber(resource.amount)}</strong>
            <small>{resource.rate}</small>
          </div>
        ))}
      </div>
      <div className="faction-strip">
        {snapshot.factions.map((faction) => (
          <div
            className={faction.active ? 'faction-button active readonly' : 'faction-button readonly'}
            key={faction.id}
            style={{ '--team-color': faction.color }}
          >
            <span>{shortFactionName(faction.name)}</span>
            <small>{faction.entityCount} entities - {faction.relationship}</small>
          </div>
        ))}
      </div>
      <div className="runtime-block">
        <strong>{activeFaction?.name ?? 'No faction'}</strong>
        <span>{connection.phase} - {connection.transport}</span>
      </div>
    </header>
  );
}

function EntityRoster({ entities, selectedKey, onSelect }) {
  const handleSelect = useCallback((event, entityKey) => {
    event.preventDefault();
    event.stopPropagation();
    onSelect(entityKey);
  }, [onSelect]);

  return (
    <aside className="entity-roster hud-panel">
      <PanelTitle title="Entities" meta={`${entities.length}`} />
      <div className="entity-list">
        {entities.slice(0, 18).map((entity) => (
          <button
            className={entity.key === selectedKey ? 'entity-row selected' : 'entity-row'}
            key={entity.key}
            type="button"
            onPointerDown={(event) => event.stopPropagation()}
            onClick={(event) => handleSelect(event, entity.key)}
          >
            <EntityIcon entity={entity} size="small" />
            <span className="entity-copy">
              <strong>{entity.name}</strong>
              <small>{entity.teamName} - {entityRole(entity)}</small>
            </span>
            <span className="ability-count">{entity.abilityNames?.length ?? 0}</span>
          </button>
        ))}
      </div>
    </aside>
  );
}

function RightHud({ selection, garrison, productionQueue, statuses, diplomacy, techTree, onSelect, onCancelPlanting }) {
  const showStrategic = techTree.nodes.length > 0 || diplomacy.rows.length > 0;
  return (
    <aside className="right-hud">
      <SelectionPanel selection={selection} />
      <GarrisonPanel
        garrison={garrison}
        selectedKey={selection.entityKey}
        onSelect={onSelect}
        onCancelPlanting={onCancelPlanting}
      />
      <ProductionPanel queue={productionQueue} statuses={statuses} />
      {showStrategic ? <StrategicReadout techTree={techTree} diplomacy={diplomacy} /> : null}
    </aside>
  );
}

function GarrisonPanel({ garrison, selectedKey, onSelect, onCancelPlanting }) {
  const handleSelect = useCallback((event, entityKey) => {
    event.preventDefault();
    event.stopPropagation();
    onSelect('selectEntity', { entityKey });
  }, [onSelect]);

  const handleCancel = useCallback((event, entityKey) => {
    event.preventDefault();
    event.stopPropagation();
    onCancelPlanting('cancelPlanting', { entityKey });
  }, [onCancelPlanting]);

  return (
    <section className="hud-panel compact-panel">
      <PanelTitle title="驻军" meta={`${garrison.length}`} />
      {garrison.length === 0 ? <p className="empty">城内暂无驻军。</p> : null}
      <div className="garrison-list">
        {garrison.map((member) => (
          <div
            className={member.entityKey === selectedKey ? 'garrison-row selected' : 'garrison-row'}
            key={member.entityKey}
          >
            <button
              type="button"
              className="garrison-name"
              onClick={(event) => handleSelect(event, member.entityKey)}
            >
              <span>{member.name}</span>
              <small>{member.isGovernor ? '太守' : '平民'}</small>
            </button>
            {member.isPlanting ? (
              <div className="garrison-planting">
                <div className="queue-progress" aria-hidden="true">
                  <i style={{ transform: `scaleX(${progressRatio(member.progressPermille)})` }} />
                </div>
                <button
                  type="button"
                  className="cancel-planting"
                  title="取消立旗"
                  onClick={(event) => handleCancel(event, member.entityKey)}
                >
                  ✕
                </button>
              </div>
            ) : null}
          </div>
        ))}
      </div>
    </section>
  );
}

function SelectionPanel({ selection }) {
  return (
    <section className="hud-panel selection-panel">
      <PanelTitle title="Selection" meta={selection.teamId ? `Team ${selection.teamId}` : '-'} />
      <div className="portrait-row">
        <EntityIcon entity={selection} size="large" />
        <div>
          <h2>{selection.name}</h2>
          <p>{selection.kind || 'none'} - live selection</p>
        </div>
      </div>
      <Meter label="HP" value={selection.health} max={Math.max(1, selection.health)} />
      {selection.shield > 0 ? <Meter label="Shield" value={selection.shield} max={selection.shield} /> : null}
      <div className="member-row">
        {selection.members.slice(0, 4).map((member) => (
          <span key={member.entityKey}>{member.name}</span>
        ))}
      </div>
    </section>
  );
}

function ProductionPanel({ queue, statuses }) {
  const rows = queue.length > 0 ? queue : statuses;
  return (
    <section className="hud-panel compact-panel">
      <PanelTitle title="Queue" meta={`${rows.length}`} />
      <div className="queue-list">
        {rows.length === 0 ? <p className="empty">No active production exposed.</p> : null}
        {rows.slice(0, 4).map((item, index) => (
          <div className="queue-row" key={`${item.label}-${index}`}>
            <strong>{item.label || 'In progress'}</strong>
            <span>{formatProgress(item.progressPermille)}%</span>
            <small>{item.detail}</small>
            <div className="queue-progress" aria-hidden="true">
              <i style={{ transform: `scaleX(${progressRatio(item.progressPermille)})` }} />
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

function StrategicReadout({ techTree, diplomacy }) {
  return (
    <section className="hud-panel compact-panel">
      <PanelTitle title="Strategic" meta={`${techTree.nodes.length} tech`} />
      <div className="strategic-readout">
        {techTree.nodes.slice(0, 3).map((node) => (
          <div className="readout-row" key={node.id}>
            <strong>{node.label}</strong>
            <small>{node.state}</small>
          </div>
        ))}
        {diplomacy.rows.slice(0, 2).map((row) => (
          <div className="readout-row" key={row.factionId}>
            <strong>{row.factionName}</strong>
            <small>{row.relationship}</small>
          </div>
        ))}
      </div>
    </section>
  );
}

function BottomHud({ commands, selection, onActivate }) {
  return (
    <section className="bottom-hud">
      <CommandDeck commands={commands} selection={selection} onActivate={onActivate} />
    </section>
  );
}

function CommandDeck({ commands, selection, onActivate }) {
  const groups = commands.groups.length > 0
    ? commands.groups
    : [{ groupIndex: 0, label: 'Live Loadout', slots: [] }];
  const activeGroup = groups[0];
  const handleActivate = useCallback((event, groupIndex, slotIndex) => {
    event.preventDefault();
    event.stopPropagation();
    onActivate(groupIndex, slotIndex);
  }, [onActivate]);

  return (
    <section className="hud-panel command-deck">
      <div className="command-header">
        <div className="command-selected">
          <EntityIcon entity={selection} size="medium" />
          <div>
            <strong>{selection.name}</strong>
            <small>{selection.kind || 'none'} - target {shortKey(commands.targetEntityKey)}</small>
          </div>
        </div>
        <div className="command-title">
          <h2>{activeGroup.label || 'Command Card'}</h2>
          <span>rev {commands.revision ?? 0}</span>
        </div>
      </div>
      {commands.message ? <p className="soft-warning">{commands.message}</p> : null}
      <div className="command-grid">
        {Array.from({ length: 12 }).map((_, index) => {
          const slot = activeGroup.slots.find((item) => item.slotIndex === index);
          const enabled = Boolean(slot?.enabled && slot?.actionId && commands.canActivate);
          return (
            <button
              key={index}
              type="button"
              className={enabled ? 'command-slot enabled' : 'command-slot'}
              disabled={!enabled}
              title={slot?.detail || 'No ability assigned'}
              onPointerDown={(event) => event.stopPropagation()}
              onClick={(event) => slot && handleActivate(event, activeGroup.groupIndex, slot.slotIndex)}
            >
              <AbilityIcon label={slot?.label || ''} size="command" />
              <span>{slot?.label || 'Empty'}</span>
              <small>{slot?.actionId || ''}</small>
            </button>
          );
        })}
      </div>
    </section>
  );
}

function DiagnosticsHud({ snapshot, connection, messages }) {
  const status = connection.error || snapshot.diagnostics.lastCommandStatus || 'idle';
  return (
    <footer className="diagnostics-hud hud-panel">
      <div>
        <strong>{connection.lastCommand || snapshot.diagnostics.lastCommand}</strong>
        <span>{status}</span>
      </div>
      <div>
        <strong>{connection.lastPacket}</strong>
        <span>{connection.topic} - {connection.commandAcks} ack</span>
      </div>
      <div className="message-line">
        <strong>DataPlane</strong>
        <span>{messages.slice(0, 2).join(' | ') || 'Engine-backed state stream'}</span>
      </div>
    </footer>
  );
}

function PanelTitle({ title, meta }) {
  return (
    <div className="panel-title">
      <h2>{title}</h2>
      <span>{meta}</span>
    </div>
  );
}

function AbilityIcon({ label, size }) {
  const art = abilityArtFor(label);
  return (
    <span className={`ability-art ${size}`}>
      {art ? <img src={art} alt="" draggable="false" /> : <span className="ability-art-empty">-</span>}
    </span>
  );
}

function EntityIcon({ entity, size }) {
  const art = entityArtFor(entity);
  const className = `entity-art ${size} ${entity?.kind || 'unit'}`;
  return (
    <span className={className} style={{ '--team-color': entity?.teamColor || '#8db596' }}>
      {art ? <img src={art} alt="" draggable="false" /> : <span className="entity-art-fallback" />}
    </span>
  );
}

function Meter({ label, value, max }) {
  const ratio = max <= 0 ? 0 : clamp(value / max, 0, 1);
  return (
    <div className="meter">
      <span>{label}</span>
      <div><i style={{ transform: `scaleX(${ratio})` }} /></div>
      <strong>{formatNumber(value)}</strong>
    </div>
  );
}

function abilityArtFor(label) {
  const token = String(label ?? '').toLowerCase();
  if (!token) {
    return '';
  }

  if (token.includes('power') || token.includes('reactor') || token.includes('tesla')) {
    return ENTITY_ART.tower;
  }

  if (token.includes('refinery') || token.includes('ore')) {
    return ENTITY_ART.mine;
  }

  if (token.includes('war factory') || token.includes('factory') || token.includes('workshop')) {
    return ENTITY_ART.workshop;
  }

  if (token.includes('rhino') || token.includes('tank')) {
    return ENTITY_ART.catapult;
  }

  if (token.includes('mcv') || token.includes('deploy')) {
    return ENTITY_ART.ram;
  }

  if (token.includes('construction yard') || token.includes('conyard')) {
    return ENTITY_ART.city;
  }

  if (token.includes('harvester')) {
    return ENTITY_ART.stable;
  }

  return '';
}

function entityArtFor(entity) {
  const name = String(entity?.name ?? '').toLowerCase();
  const kind = String(entity?.kind ?? '').toLowerCase();
  if (!name && !kind) {
    return '';
  }

  if (name.includes('construction yard') || name.includes('conyard') || name.includes('city')) {
    return ENTITY_ART.city;
  }

  if (name.includes('power plant') || name.includes('reactor') || name.includes('tesla')) {
    return ENTITY_ART.tower;
  }

  if (name.includes('ore refinery') || name.includes('refinery') || name.includes('ore field')) {
    return ENTITY_ART.mine;
  }

  if (name.includes('war factory') || name.includes('workshop') || name.includes('factory')) {
    return ENTITY_ART.workshop;
  }

  if (name.includes('mcv') || name.includes('construction vehicle')) {
    return ENTITY_ART.ram;
  }

  if (name.includes('tank') || name.includes('rhino')) {
    return ENTITY_ART.catapult;
  }

  if (name.includes('harvester')) {
    return ENTITY_ART.stable;
  }

  if (name.includes('worker') || name.includes('probe') || name.includes('drone') || name.includes('villager') || name.includes('settler')) {
    return ENTITY_ART.worker;
  }

  if (kind === 'vehicle') {
    return ENTITY_ART.catapult;
  }

  if (kind === 'structure') {
    return ENTITY_ART.city;
  }

  if (kind === 'worker') {
    return ENTITY_ART.worker;
  }

  return ENTITY_ART.stable;
}

function entityRole(entity) {
  const count = entity?.abilityNames?.length ?? 0;
  if (count > 0) {
    return `${entity.kind} / ${count} commands`;
  }

  return entity.kind || 'entity';
}

function reduceDataPlaneEvent(current, event) {
  const payload = event.payload ?? {};
  const entities = Array.isArray(payload.entities)
    ? payload.entities
    : Array.isArray(payload.entityPatches)
      ? payload.entityPatches
      : [];
  const diagnostics = payload.diagnostics ?? {};

  return {
    ...current,
    phase: 'streaming',
    topic: event.topic ?? current.topic,
    sessionId: event.sessionId ?? current.sessionId,
    tick: payload.tick ?? current.tick,
    entityCount: diagnostics.entityCount ?? payload.entityCount ?? entities.length ?? current.entityCount,
    lastPacket: event.kind
  };
}

function mergeSnapshot(previous, payload) {
  if (!payload || typeof payload !== 'object') {
    return previous;
  }

  return {
    ...previous,
    ...payload,
    resources: payload.resources ?? previous.resources,
    factions: payload.factions ?? previous.factions,
    entities: payload.entities ?? payload.entityPatches ?? previous.entities,
    selection: payload.selection ?? previous.selection,
    garrison: payload.garrison ?? previous.garrison,
    commands: payload.commands ?? previous.commands,
    buildables: payload.buildables ?? previous.buildables,
    productionQueue: payload.productionQueue ?? previous.productionQueue,
    techTree: payload.techTree ?? previous.techTree,
    diplomacy: payload.diplomacy ?? previous.diplomacy,
    diagnostics: payload.diagnostics ?? previous.diagnostics
  };
}

function subtitleForFlavor(flavor, mapId, tick) {
  const label = {
    'red-alert-like': 'Live command-card slice for structure production and battlefield deployment',
    'starcraft-like': 'Live command-card slice for macro, warp tech, and unit production',
    'empire-like': 'Live command-card slice for villager economy and tech progression',
    'fourx-like': 'Live command-card slice for city production and empire growth'
  }[flavor] ?? 'Live engine-backed RTS production slice';
  return `${label} - ${mapId || 'Waiting for map'} - tick ${tick}`;
}

function titleForFlavor(flavor) {
  return {
    'red-alert-like': 'Red Alert Production',
    'starcraft-like': 'StarCraft Production',
    'empire-like': 'Empire Production',
    'fourx-like': '4X City Production'
  }[flavor] ?? 'RTS Production Showcase';
}

function shortFactionName(name) {
  return String(name ?? '').split('/')[0].trim() || 'Faction';
}

function shortKey(key) {
  return key ? key.split(':')[0] : '-';
}

function formatNumber(value) {
  return new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 }).format(value ?? 0);
}

function formatProgress(value) {
  return Math.round(clamp((value ?? 0) / 10, 0, 100));
}

function progressRatio(value) {
  return clamp((value ?? 0) / 1000, 0, 1);
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

createRoot(document.getElementById('root')).render(<App />);
