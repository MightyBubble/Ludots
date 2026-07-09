import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  ENTITY_COMMAND_PANEL_SET_PROFILE_COMMAND,
  ENTITY_COMMAND_PANEL_TOPIC,
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './dataplane/client.js';
import './styles.css';

const EMPTY_SNAPSHOT = {
  ready: false,
  mapId: '',
  activeProfile: 'Family',
  activeProfileId: '',
  revision: 0,
  sourceActorCount: 0,
  tileCount: 0,
  expectedTileCount: 8,
  profiles: [
    { buttonId: 'profile.by_template', label: 'Template', profileId: '', active: false, accentColorHex: '#6EC6FF' },
    { buttonId: 'profile.by_family', label: 'Family', profileId: '', active: true, accentColorHex: '#F6D37A' },
    { buttonId: 'profile.by_ability_id', label: 'Ability', profileId: '', active: false, accentColorHex: '#9EE493' }
  ],
  owners: [],
  groupCount: 0,
  groupLabel: '',
  tiles: [],
  given: 'Given Arcweaver, Vanguard, and Commander are active.',
  when: 'When the Family command view is active.',
  then: 'Then shared command families collapse into one visible button with contributor labels.',
  visibleResult: 'Waiting for the live hero command roster.',
  lastCommand: 'snapshot',
  error: ''
};

const INITIAL_CONNECTION = {
  phase: 'boot',
  transport: 'none',
  sessionId: 'pending',
  topic: ENTITY_COMMAND_PANEL_TOPIC,
  lastPacket: 'none',
  error: ''
};

function App() {
  const { snapshot, connection, setProfile } = useEntityCommandPanelDataPlane();
  const profileClass = snapshot.activeProfile.toLowerCase();
  const templateGroups = useMemo(() => groupTemplateTiles(snapshot), [snapshot]);

  return (
    <main className={`war3-command-panel ${profileClass}`} data-ready={snapshot.ready ? 'true' : 'false'}>
      <section className="portrait-zone" aria-label="active heroes">
        <div className="portrait-frame">
          <div className="portrait-gem" />
          <div className="portrait-runes">
            <span />
            <span />
            <span />
          </div>
        </div>
        <div className="selection-copy">
          <h1>Command Panel</h1>
          <strong>{snapshot.sourceActorCount || 0} active heroes</strong>
          <div className="owner-stack">
            {ownersForDisplay(snapshot).map((owner) => (
              <span key={owner.name} style={{ '--accent': owner.accentColorHex }}>
                {owner.name}
              </span>
            ))}
          </div>
        </div>
      </section>

      <section className="profile-zone" aria-label="command view modes">
        <header>
          <span>View Mode</span>
          <strong>{snapshot.activeProfile}</strong>
        </header>
        <div className="profile-buttons">
          {snapshot.profiles.map((profile) => (
            <button
              key={profile.buttonId}
              type="button"
              className={profile.active ? 'active' : ''}
              style={{ '--accent': profile.accentColorHex }}
              onClick={() => setProfile(profile)}
            >
              <span>{profile.label}</span>
              <small>{profileDescription(profile.label)}</small>
            </button>
          ))}
        </div>
        <div className="profile-proof">
          <strong>{snapshot.tileCount}/{snapshot.expectedTileCount}</strong>
          <span>{snapshot.visibleResult}</span>
        </div>
      </section>

      <section className="command-zone" aria-label="War3 command grid">
        <div className="scenario-strip">
          <ScenarioLine label="Given" text={snapshot.given} />
          <ScenarioLine label="When" text={snapshot.when} />
          <ScenarioLine label="Then" text={snapshot.then} />
        </div>

        {!snapshot.ready ? (
          <div className="not-ready">
            <strong>Command panel waiting</strong>
            <span>{formatPanelError(connection.error || snapshot.error)}</span>
          </div>
        ) : snapshot.activeProfile === 'Template' ? (
          <TemplateGrid groups={templateGroups} />
        ) : (
          <AggregateGrid tiles={snapshot.tiles} mode={snapshot.activeProfile.toLowerCase()} />
        )}
      </section>

      <section className="audit-zone" aria-label="profile evidence">
        <EvidenceMeter label="Template" value="3x8" active={snapshot.activeProfile === 'Template'} />
        <EvidenceMeter label="Family" value="catalog" active={snapshot.activeProfile === 'Family'} />
        <EvidenceMeter label="Ability" value="shared" active={snapshot.activeProfile === 'Ability'} />
        <div className="transport-readout">
          <span>{snapshot.ready ? 'Live roster' : 'Roster pending'}</span>
          <strong>{formatPanelCommand(snapshot.lastCommand || connection.lastPacket)}</strong>
          <small>{snapshot.ready ? 'Fresh command data' : 'Waiting for heroes'}</small>
        </div>
      </section>
    </main>
  );
}

function useEntityCommandPanelDataPlane() {
  const clientRef = useRef(null);
  const [snapshot, setSnapshot] = useState(EMPTY_SNAPSHOT);
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
      setConnection((current) => ({
        ...current,
        phase: 'host-required',
        error: error instanceof Error ? error.message : String(error)
      }));
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

        setConnection((current) => ({
          ...current,
          error: diagnostic.message,
          lastPacket: diagnostic.type
        }));
      }
    });
    clientRef.current = client;

    setConnection((current) => ({
      ...current,
      phase: 'connecting',
      transport: transport?.name ?? 'unknown',
      error: ''
    }));

    client
      .handshake({ app: 'entity-command-panel-showcase' })
      .then((handshake) => {
        if (!active) {
          return null;
        }

        setConnection((current) => ({
          ...current,
          phase: 'connected',
          sessionId: handshake.sessionId ?? handshake.payload?.sessionId ?? current.sessionId,
          transport: handshake.payload?.transportName ?? transport?.name ?? current.transport
        }));

        return client.subscribe(ENTITY_COMMAND_PANEL_TOPIC, (event) => {
          if (!active) {
            return;
          }

          setSnapshot((current) => normalizeSnapshot(current, event.payload));
          setConnection((current) => ({
            ...current,
            phase: 'streaming',
            sessionId: event.sessionId ?? current.sessionId,
            lastPacket: event.kind,
            topic: event.topic ?? current.topic,
            error: ''
          }));
        });
      })
      .catch((error) => {
        if (!active) {
          return;
        }

        setConnection((current) => ({
          ...current,
          phase: 'stream-error',
          error: error instanceof Error ? error.message : String(error)
        }));
      });

    return () => {
      active = false;
      client.close();
      clientRef.current = null;
    };
  }, []);

  const setProfile = useCallback(async (profile) => {
    const client = clientRef.current;
    if (!client || !profile?.buttonId) {
      return;
    }

    setConnection((current) => ({
      ...current,
      lastPacket: `${profile.label}:pending`,
      error: ''
    }));

    try {
      await client.command(ENTITY_COMMAND_PANEL_SET_PROFILE_COMMAND, {
        buttonId: profile.buttonId,
        profile: profile.label
      });
      setConnection((current) => ({
        ...current,
        lastPacket: `${profile.label}:ack`
      }));
    } catch (error) {
      setConnection((current) => ({
        ...current,
        lastPacket: `${profile.label}:error`,
        error: error instanceof Error ? error.message : String(error)
      }));
    }
  }, []);

  return { snapshot, connection, setProfile };
}

function ScenarioLine({ label, text }) {
  return (
    <div className="scenario-line">
      <strong>{label}</strong>
      <span>{text}</span>
    </div>
  );
}

function AggregateGrid({ tiles, mode }) {
  const visibleTiles = sortAggregateTiles(tiles, mode);
  return (
    <div className={`${mode}-grid aggregate-grid`}>
      {visibleTiles.map((tile) => (
        <CommandTile key={`${tile.slotIndex}:${tile.abilityId}:${tile.label}`} tile={tile} mode={mode} />
      ))}
    </div>
  );
}

function TemplateGrid({ groups }) {
  return (
    <div className="template-grid">
      {groups.map((group) => (
        <div className="owner-lane" key={group.owner}>
          <header style={{ '--accent': group.accent }}>
            <strong>{group.owner}</strong>
            <span>{group.tiles.length} commands</span>
          </header>
          <div>
            {group.tiles.map((tile) => (
              <CommandTile key={`${tile.slotIndex}:${tile.abilityId}:${tile.label}`} tile={tile} mode="template" />
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}

function CommandTile({ tile, mode }) {
  const contributors = contributorNames(tile);
  const isAggregate = mode === 'ability' || mode === 'family';
  return (
    <article
      className={isAggregate && contributors.length > 1 ? 'command-tile repeated' : 'command-tile'}
      style={{ '--accent': tile.accentColorHex }}
    >
      <span className="hotkey">{tile.hotkey || tile.slotIndex + 1}</span>
      <strong>{tile.label}</strong>
      <small>{tileSubtitle(tile, mode, contributors)}</small>
      {isAggregate ? (
        <div className="contributor-labels">
          {contributors.map((name) => (
            <span key={name}>{shortName(name)}</span>
          ))}
        </div>
      ) : (
        <em>{tileStatus(tile)}</em>
      )}
    </article>
  );
}

function profileDescription(label) {
  if (label === 'Template') {
    return 'unit template layout';
  }

  if (label === 'Ability') {
    return 'shared ability once';
  }

  return 'catalog family';
}

function formatPanelError(message) {
  return message ? 'Waiting for live command data.' : 'No live snapshot yet.';
}

function formatPanelCommand(command) {
  if (!command || command === 'snapshot') {
    return 'Panel ready';
  }

  if (String(command).endsWith(':pending')) {
    return 'Switching view';
  }

  if (String(command).endsWith(':ack')) {
    return 'View updated';
  }

  if (String(command).endsWith(':error')) {
    return 'View switch failed';
  }

  return 'Panel ready';
}

function EvidenceMeter({ label, value, active }) {
  return (
    <div className={active ? 'evidence-meter active' : 'evidence-meter'}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function normalizeSnapshot(previous, payload) {
  if (!payload || typeof payload !== 'object') {
    return previous;
  }

  return {
    ...previous,
    ...payload,
    profiles: Array.isArray(payload.profiles) ? payload.profiles : previous.profiles,
    owners: Array.isArray(payload.owners) ? payload.owners : previous.owners,
    tiles: Array.isArray(payload.tiles) ? payload.tiles : previous.tiles,
    ready: Boolean(payload.ready),
    sourceActorCount: Number.isFinite(payload.sourceActorCount) ? payload.sourceActorCount : previous.sourceActorCount,
    tileCount: Number.isFinite(payload.tileCount) ? payload.tileCount : previous.tileCount,
    expectedTileCount: Number.isFinite(payload.expectedTileCount) ? payload.expectedTileCount : previous.expectedTileCount,
    revision: Number.isFinite(payload.revision) ? payload.revision : previous.revision
  };
}

function groupTemplateTiles(snapshot) {
  const ownerOrder = ownersForDisplay(snapshot).map((owner) => owner.name);
  const groups = new Map(ownerOrder.map((owner) => [owner, []]));
  for (const tile of snapshot.tiles) {
    const contributors = contributorNames(tile);
    const owner = tile.owner || contributors[0] || 'Unassigned';
    if (!groups.has(owner)) {
      groups.set(owner, []);
    }
    groups.get(owner).push(tile);
  }

  return [...groups.entries()]
    .filter(([, tiles]) => tiles.length > 0)
    .map(([owner, tiles]) => ({
      owner,
      accent: tiles[0]?.accentColorHex ?? '#D8B15E',
      tiles: [...tiles].sort((left, right) => left.slotIndex - right.slotIndex || left.abilityId - right.abilityId)
    }));
}

function sortAggregateTiles(tiles, mode) {
  const sorted = Array.isArray(tiles) ? [...tiles] : [];
  if (mode === 'ability') {
    sorted.sort((left, right) => {
      const leftContributorCount = contributorNames(left).length || left.ownerCount || 0;
      const rightContributorCount = contributorNames(right).length || right.ownerCount || 0;
      return rightContributorCount - leftContributorCount ||
        String(left.label).localeCompare(String(right.label)) ||
        left.slotIndex - right.slotIndex ||
        left.abilityId - right.abilityId;
    });
    return sorted;
  }

  sorted.sort((left, right) =>
    left.slotIndex - right.slotIndex ||
    left.abilityId - right.abilityId ||
    String(left.label).localeCompare(String(right.label)));
  return sorted;
}

function ownersForDisplay(snapshot) {
  if (Array.isArray(snapshot.owners) && snapshot.owners.length > 0) {
    return [...snapshot.owners].sort((left, right) => (left.order ?? 0) - (right.order ?? 0));
  }

  return [];
}

function contributorNames(tile) {
  if (Array.isArray(tile.contributorNames) && tile.contributorNames.length > 0) {
    return tile.contributorNames.filter((name) => typeof name === 'string' && name.trim().length > 0);
  }

  return tile.owner ? [tile.owner] : [];
}

function tileSubtitle(tile, mode, contributors) {
  if (mode === 'family') {
    return tile.detail || `${contributors.length || tile.ownerCount || 0} contributors`;
  }

  if (mode === 'ability') {
    return contributors.join(', ') || tile.detail || 'Ready';
  }

  return tile.owner || contributors[0] || 'Ready';
}

function shortName(name) {
  return String(name).replace(/^Showcase\s+/i, '').trim();
}

function tileStatus(tile) {
  return tile.stateFlags && tile.stateFlags !== 'None' ? tile.stateFlags : 'Ready';
}

createRoot(document.getElementById('root')).render(<App />);
