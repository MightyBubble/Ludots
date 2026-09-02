import React, { useCallback, useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  SANGO_MAP_STATS_TOPIC,
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './dataplane/client.js';
import './styles.css';

const EMPTY_SNAPSHOT = {
  tick: 0,
  mapName: '',
  width: 0,
  height: 0,
  terrainHistogram: {},
  cities: [],
  diagnostics: {
    lastCommand: 'none',
    lastCommandStatus: 'idle',
    commandCount: 0,
    selectedCityId: ''
  }
};

const INITIAL_CONNECTION = {
  phase: 'boot',
  transport: 'none',
  sessionId: 'pending',
  topic: SANGO_MAP_STATS_TOPIC,
  lastPacket: 'none',
  lastCommand: 'none',
  commandAcks: 0,
  error: ''
};

function App() {
  const { snapshot, connection, command } = useSangoDataPlane();
  const [optimisticCityId, setOptimisticCityId] = useState('');
  const selectedCityId = snapshot.diagnostics.selectedCityId || optimisticCityId;

  const selectCity = useCallback((cityId) => {
    setOptimisticCityId(cityId);
    command('sango.selectCity', { cityId });
  }, [command]);

  return (
    <main className="app">
      <Header snapshot={snapshot} connection={connection} />
      <section className="panels">
        <MapStatsPanel snapshot={snapshot} />
        <CityListPanel
          cities={snapshot.cities}
          selectedCityId={selectedCityId}
          onSelect={selectCity}
        />
      </section>
      <Footer snapshot={snapshot} connection={connection} />
    </main>
  );
}

function useSangoDataPlane() {
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
      .handshake({ app: 'sango-webui-spike' })
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

        return client.subscribe(SANGO_MAP_STATS_TOPIC, (event) => {
          if (!active) {
            return;
          }

          React.startTransition(() => {
            setSnapshot((current) => mergeSnapshot(current, event.payload));
            setConnection((current) => ({ ...current, phase: 'streaming', lastPacket: event.kind }));
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
    };
  }, []);

  const command = useCallback(async (name, payload = {}) => {
    const client = clientRef.current;
    if (!client) {
      return;
    }

    React.startTransition(() => {
      setConnection((current) => ({ ...current, lastCommand: `${name}:pending`, error: '' }));
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

function Header({ snapshot, connection }) {
  return (
    <header className="hud-panel header">
      <h1>三国 · Web UI 尖峰</h1>
      <div className="header-meta">
        <span>{connection.phase} · {connection.transport}</span>
        <span>tick {snapshot.tick}</span>
      </div>
      {connection.error ? <p className="error-line">{connection.error}</p> : null}
    </header>
  );
}

function MapStatsPanel({ snapshot }) {
  const terrainRows = Object.entries(snapshot.terrainHistogram ?? {});
  return (
    <section className="hud-panel">
      <div className="panel-title">
        <h2>地图统计</h2>
        <span>{terrainRows.length} 类地形</span>
      </div>
      <div className="stats-grid">
        <div className="stat-row"><span>地图</span><strong>{snapshot.mapName || '待连接'}</strong></div>
        <div className="stat-row"><span>尺寸</span><strong>{snapshot.width} × {snapshot.height}</strong></div>
      </div>
      <table className="terrain-table">
        <thead>
          <tr><th>地形</th><th>格数</th></tr>
        </thead>
        <tbody>
          {terrainRows.map(([terrain, count]) => (
            <tr key={terrain}>
              <td>{terrain}</td>
              <td>{count}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

function CityListPanel({ cities, selectedCityId, onSelect }) {
  return (
    <section className="hud-panel">
      <div className="panel-title">
        <h2>城市列表</h2>
        <span>{cities.length} 座</span>
      </div>
      <div className="city-list">
        {cities.length === 0 ? <p className="empty">等待 DataPlane 快照……</p> : null}
        {cities.map((city) => (
          <button
            key={city.id}
            type="button"
            className={city.id === selectedCityId || city.selected ? 'city-row selected' : 'city-row'}
            onClick={() => onSelect(city.id)}
          >
            <strong>{city.name}</strong>
            <span>{city.faction}</span>
            <small>{city.population.toLocaleString('zh-CN')} 人</small>
          </button>
        ))}
      </div>
    </section>
  );
}

function Footer({ snapshot, connection }) {
  return (
    <footer className="hud-panel footer">
      <div>
        <strong>{snapshot.diagnostics.lastCommand}</strong>
        <span>{snapshot.diagnostics.lastCommandStatus}</span>
      </div>
      <div>
        <strong>{connection.lastCommand}</strong>
        <span>{connection.topic} · {connection.commandAcks} ack</span>
      </div>
    </footer>
  );
}

function mergeSnapshot(previous, payload) {
  if (!payload || typeof payload !== 'object') {
    return previous;
  }

  return {
    ...previous,
    ...payload,
    terrainHistogram: payload.terrainHistogram ?? previous.terrainHistogram,
    cities: payload.cities ?? previous.cities,
    diagnostics: payload.diagnostics ?? previous.diagnostics
  };
}

createRoot(document.getElementById('root')).render(<App />);
