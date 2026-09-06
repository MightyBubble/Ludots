import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  SANGO_BATTLES_TOPIC,
  SANGO_CITIES_TOPIC,
  SANGO_CITY_TOPIC,
  SANGO_DIPLOMACY_TOPIC,
  SANGO_FORCES_TOPIC,
  SANGO_MESSAGES_TOPIC,
  SANGO_TECHNIQUES_TOPIC,
  SANGO_TROOPS_TOPIC,
  SANGO_TURN_TOPIC,
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './dataplane/client.js';
import './styles.css';

// HUD 七区布局(U4 重构,用户规格):
//   左上=时间,左中=选中实体详情,左下=事件 log,右上=小地图,
//   右中=实体列表(可按势力过滤),右下=回合推进,中下=实体指令面板(RTS/MoBA 式)。
// 地图本体是引擎 3D 画布,HUD 以 grid 覆盖;空格 pointer-events 穿透。

const PERSON_STATE_NAMES = {
  1: '君主',
  2: '都督',
  3: '太守',
  4: '普通',
  5: '在野',
  6: '俘虏',
  8: '未发现'
};

const EMPTY_CITIES = { tick: 0, turnCount: 0, cities: [] };
const EMPTY_FORCES = { tick: 0, turnCount: 0, forces: [] };
const EMPTY_TURN = {
  turnCount: 0,
  year: 0,
  month: 0,
  day: 0,
  dateText: '----',
  summary: '',
  playerForceId: 0,
  playerForceName: '',
  awaitingPlayer: false
};
const EMPTY_MESSAGES = { tick: 0, turnCount: 0, messages: [] };
const EMPTY_DETAIL = null;
const EMPTY_TROOPS = { tick: 0, turnCount: 0, troops: [] };
const EMPTY_BATTLES = { tick: 0, turnCount: 0, battles: [] };
const EMPTY_DIPLOMACY = {
  tick: 0,
  turnCount: 0,
  playerForceId: 0,
  playerForceName: '',
  alliances: [],
  relations: []
};
const EMPTY_TECHNIQUES = {
  tick: 0,
  turnCount: 0,
  forceId: 0,
  forceName: '',
  techniquePoint: 0,
  researching: null,
  techniques: []
};

// 内核格坐标范围(地图 256×256,格距 GridSize 米)。
const MAP_CELLS = 256;

// 势力色板(按势力 id 稳定取色;经典三国志色系近似,自创不抄原图)。
const FORCE_COLORS = [
  '#e25822', '#3f7fbf', '#4f9e4f', '#c2b04c', '#8c5fbf', '#bf5f8f',
  '#4fb3a8', '#c97f4f', '#7f8fbf', '#a84f4f', '#5fae5f', '#bf9f4f',
  '#8f4fbf', '#4f8fbf', '#bf5f5f', '#5fbf8f', '#bf8f5f', '#6f6fbf',
  '#bf4f8f', '#4fbfbf', '#9fbf4f', '#bf4f4f', '#4f5fbf', '#bfbf5f',
  '#7fbf4f', '#bf6f9f', '#4fbf7f', '#bf7f7f', '#7f4fbf', '#4fbf5f'
];

function forceColor(forceId) {
  return FORCE_COLORS[(forceId - 1) % FORCE_COLORS.length] ?? '#999999';
}

const CITY_COMMANDS = [
  { type: 'train', label: '训练', hint: '提升城市士气(消耗资金)', executors: true },
  { type: 'search', label: '探索', hint: '下回合结算,可能发现人才或资金', executors: true },
  { type: 'reward', label: '奖励', hint: '消耗资金提升忠诚(+10)', mode: 'reward' },
  { type: 'recruit', label: '招揽', hint: '1 名执行武将招揽 1 名在野人才', mode: 'recruit' },
  { type: 'expedition', label: '出征', hint: '编成部队出征(兵力/金/粮与行动力)', mode: 'expedition' }
];

function usePersistentState(key, initial) {
  const [value, setValue] = useState(() => {
    try {
      const raw = window.localStorage.getItem(key);
      return raw === null ? initial : JSON.parse(raw);
    } catch {
      return initial;
    }
  });

  useEffect(() => {
    try {
      window.localStorage.setItem(key, JSON.stringify(value));
    } catch {
      /* 会话内退化即可,不打断 UI */
    }
  }, [key, value]);

  return [value, setValue];
}

function App() {
  const { clientRef, data, connection, command } = useSangoSession();
  const [listTab, setListTab] = usePersistentState('sango.ui.listTab', 'cities');
  const [forceFilter, setForceFilter] = useState(0);
  // 选中实体:城/部队统一槽位;详情(左中)与指令(中下)都从它派生。
  const [selection, setSelection] = useState(null);
  const [commandCount, setCommandCount] = useState(0);
  const [lastOrder, setLastOrder] = useState({ text: '尚无指令', tone: 'idle' });
  const [selectDismissed, setSelectDismissed] = useState(false);

  const selectedCityId = selection?.kind === 'city' ? selection.id : 0;
  const selectedTroopId = selection?.kind === 'troop' ? selection.id : 0;
  const detailRevision = `${data.turn.turnCount}:${commandCount}`;
  const cityDetail = useCityDetail(clientRef, selectedCityId, detailRevision);
  const selectedTroop = data.troops.troops.find((troop) => troop.id === selectedTroopId) ?? null;
  const selectedCityRow = data.cities.cities.find((city) => city.id === selectedCityId) ?? null;

  const playerForceId = data.turn.playerForceId ?? 0;
  const awaitingPlayer = data.turn.awaitingPlayer ?? false;
  const showForceSelect = !selectDismissed && data.turn.turnCount === 0 && playerForceId === 0;

  const selectPlayerForce = useCallback(async (forceId) => {
    setLastOrder({ text: '势力选择:以所选势力重整开局……', tone: 'pending' });
    const result = await command('sango.selectPlayerForce', { forceId });
    setLastOrder(result.ok
      ? { text: '势力选择:已按原版玩家路径重装世界,内政指令即刻可用', tone: 'ok' }
      : { text: `势力选择失败:${result.message}`, tone: 'error' });
    return result;
  }, [command]);

  const issueCityCommand = useCallback(async (type, payload, label) => {
    setLastOrder({ text: `${label ?? type}:下达中……`, tone: 'pending' });
    const result = await command('sango.cityCommand', { type, ...payload });
    if (result.ok) {
      setCommandCount((count) => count + 1);
      setLastOrder({ text: `${label ?? type}:已受理,下轮话题刷新后可见变化`, tone: 'ok' });
    } else {
      setLastOrder({ text: `${label ?? type} 被拒绝:${result.message}`, tone: 'error' });
    }
    return result;
  }, [command]);

  const rawCommand = useCallback(async (label, name, payload, okText) => {
    setLastOrder({ text: `${label}:下达中……`, tone: 'pending' });
    const result = await command(name, payload);
    if (result.ok) {
      setCommandCount((count) => count + 1);
      setLastOrder({ text: okText ?? `${label}:已受理`, tone: 'ok' });
    } else {
      setLastOrder({ text: `${label} 被拒绝:${result.message}`, tone: 'error' });
    }
    return result;
  }, [command]);

  const diplomacyCommand = useCallback(async (type, payload) => {
    const labels = { sendGift: '送礼', alliance: '结盟', discardAlliance: '摒弃同盟' };
    return rawCommand(labels[type] ?? type, 'sango.diplomacyCommand', { type, ...payload }, '使者已出发,结果见事件 log');
  }, [rawCommand]);

  const researchCommand = useCallback(async (payload) => {
    return rawCommand('研究', 'sango.researchCommand', payload, '研究已立项,完成时事件 log 刷新');
  }, [rawCommand]);

  const endTurn = useCallback(async () => {
    setLastOrder({ text: '结束回合:等待时钟推进……', tone: 'pending' });
    const result = await command('sango.endTurn', {});
    setLastOrder(result.ok
      ? { text: '结束回合:已请求时钟推进,回合结算后刷新', tone: 'ok' }
      : { text: `结束回合失败:${result.message}`, tone: 'error' });
  }, [command]);

  const saveGame = useCallback(async () => {
    const result = await command('sango.save', {});
    setLastOrder(result.ok
      ? { text: '存档:已写入存档槽(世界态 + 随机流)', tone: 'ok' }
      : { text: `存档失败:${result.message}`, tone: 'error' });
  }, [command]);

  const loadGame = useCallback(async () => {
    const result = await command('sango.load', {});
    setLastOrder(result.ok
      ? { text: '读档:已回灌最近存档', tone: 'ok' }
      : { text: `读档失败:${result.message}`, tone: 'error' });
  }, [command]);

  return (
    <main className="app hud">
      {/* 3 左上:时间 */}
      <TimeDock turn={data.turn} connection={connection} />
      {/* 1 左中:选中实体详情 */}
      <DetailDock
        selection={selection}
        cityRow={selectedCityRow}
        cityDetail={cityDetail}
        troop={selectedTroop}
        forces={data.forces.forces}
        onClose={() => setSelection(null)}
      />
      {/* 2 左下:事件 log */}
      <EventLogDock messages={data.messages.messages} lastOrder={lastOrder} />
      {/* 4 右上:小地图 */}
      <MinimapDock
        cities={data.cities.cities}
        troops={data.troops.troops}
        playerForceId={playerForceId}
        selection={selection}
        onSelect={(kind, id) => setSelection({ kind, id })}
      />
      {/* 5 右中:实体列表(可过滤势力) */}
      <EntityListDock
        activeTab={listTab}
        onTab={setListTab}
        cities={data.cities.cities}
        troops={data.troops.troops}
        forces={data.forces.forces}
        battles={data.battles.battles}
        diplomacy={data.diplomacy}
        techniques={data.techniques}
        turn={data.turn}
        forceFilter={forceFilter}
        onForceFilter={setForceFilter}
        selection={selection}
        onSelect={(kind, id) => setSelection({ kind, id })}
        clientRef={clientRef}
        commandRevision={detailRevision}
        playerGate={{ playerForceId, awaitingPlayer }}
        onDiplomacyCommand={diplomacyCommand}
        onResearchCommand={researchCommand}
      />
      {/* 6 右下:回合推进 */}
      <TurnDock
        turn={data.turn}
        onEndTurn={endTurn}
        onSave={saveGame}
        onLoad={loadGame}
        connection={connection}
      />
      {/* 7 中下:实体指令面板(RTS/MoBA 式) */}
      <CommandDock
        selection={selection}
        cityRow={selectedCityRow}
        cityDetail={cityDetail}
        troop={selectedTroop}
        playerGate={{ playerForceId, awaitingPlayer }}
        onCityCommand={issueCityCommand}
        onTroopMove={(payload) => rawCommand('移动', 'sango.moveTroop', payload, '移动已受理:范围内即时落地,范围外转委任行军')}
        onExpedition={(payload) => rawCommand('出征', 'sango.createTroop', payload, '出征已受理:部队已在城外列队')}
      />
      {showForceSelect
        ? <ForceSelectOverlay turn={data.turn} forces={data.forces.forces} cities={data.cities.cities} onSelect={selectPlayerForce} onDismiss={() => setSelectDismissed(true)} />
        : null}
    </main>
  );
}

function useSangoSession() {
  const clientRef = useRef(null);
  const [data, setData] = useState({
    cities: EMPTY_CITIES,
    forces: EMPTY_FORCES,
    turn: EMPTY_TURN,
    messages: EMPTY_MESSAGES,
    troops: EMPTY_TROOPS,
    battles: EMPTY_BATTLES,
    diplomacy: EMPTY_DIPLOMACY,
    techniques: EMPTY_TECHNIQUES
  });
  const [connection, setConnection] = useState({ phase: 'boot', transport: 'none', error: '' });

  useEffect(() => {
    let active = true;
    let transport = null;
    try {
      transport = ensureLudotsDataPlaneTransport().transport;
    } catch (error) {
      setConnection({ phase: 'error', transport: 'none', error: String(error?.message ?? error) });
      return () => {
        active = false;
      };
    }

    const client = createLudotsDataPlaneClient({ transport, hostBacked: true });
    clientRef.current = client;
    setConnection({ phase: 'connecting', transport: transport?.name ?? 'unknown', error: '' });

    const topicHandlers = new Map([
      [SANGO_CITIES_TOPIC, (cities) => setData((current) => ({ ...current, cities }))],
      [SANGO_FORCES_TOPIC, (forces) => setData((current) => ({ ...current, forces }))],
      [SANGO_TURN_TOPIC, (turn) => setData((current) => ({ ...current, turn }))],
      [SANGO_MESSAGES_TOPIC, (messages) => setData((current) => ({ ...current, messages }))],
      [SANGO_TROOPS_TOPIC, (troops) => setData((current) => ({ ...current, troops }))],
      [SANGO_BATTLES_TOPIC, (battles) => setData((current) => ({ ...current, battles }))],
      [SANGO_DIPLOMACY_TOPIC, (diplomacy) => setData((current) => ({ ...current, diplomacy }))],
      [SANGO_TECHNIQUES_TOPIC, (techniques) => setData((current) => ({ ...current, techniques }))]
    ]);

    client
      .handshake({ app: 'sango-webui' })
      .then(async () => {
        if (!active) {
          return;
        }

        setConnection((current) => ({ ...current, phase: 'connected' }));
        for (const [topic, apply] of topicHandlers) {
          await client.subscribe(topic, (event) => {
            if (!active) {
              return;
            }

            apply(event.payload);
            setConnection((current) => ({ ...current, phase: 'streaming' }));
          });
        }
      })
      .catch((error) => {
        if (!active) {
          return;
        }

        setConnection((current) => ({
          ...current,
          phase: 'error',
          error: String(error?.message ?? error)
        }));
      });

    return () => {
      active = false;
      client.close();
      clientRef.current = null;
    };
  }, []);

  const command = useCallback(async (name, payload) => {
    const client = clientRef.current;
    if (!client) {
      return { ok: false, message: 'DataPlane 客户端未就绪' };
    }

    try {
      await client.command(name, payload);
      return { ok: true, message: '' };
    } catch (error) {
      return { ok: false, message: String(error?.message ?? error) };
    }
  }, []);

  return { clientRef, data, connection, command };
}

function useCityDetail(clientRef, cityId, revision) {
  const [detail, setDetail] = useState(EMPTY_DETAIL);

  useEffect(() => {
    if (!cityId) {
      setDetail(EMPTY_DETAIL);
      return undefined;
    }

    let active = true;
    let unsubscribe = null;
    const client = clientRef.current;
    if (!client) {
      setDetail(EMPTY_DETAIL);
      return undefined;
    }

    client
      .subscribe(SANGO_CITY_TOPIC, (event) => {
        if (active) {
          setDetail(event.payload);
        }
      }, { params: { cityId } })
      .then((subscription) => {
        if (!active) {
          subscription.unsubscribe();
          return;
        }

        unsubscribe = subscription.unsubscribe;
      })
      .catch(() => {
        if (active) {
          setDetail(EMPTY_DETAIL);
        }
      });

    return () => {
      active = false;
      if (unsubscribe) {
        unsubscribe();
      }
    };
  }, [clientRef, cityId, revision]);

  return detail;
}

// ── 3 左上:时间 ──────────────────────────────────────────────
function TimeDock({ turn, connection }) {
  const playerForceId = turn.playerForceId ?? 0;
  return (
    <section className="dock dock-tl panel time-dock">
      <div className="time-main">
        <strong>{turn.dateText}</strong>
        <span>第 {turn.turnCount} 回合</span>
      </div>
      {playerForceId > 0
        ? (
          <span className={turn.awaitingPlayer ? 'player-badge active' : 'player-badge'}>
            {turn.playerForceName || `#${playerForceId}`} · {turn.awaitingPlayer ? '待命' : '结算中'}
          </span>
          )
        : <span className="player-badge">全托管观察</span>}
      {connection.error ? <span className="error-line">{connection.error}</span> : null}
    </section>
  );
}

// ── 1 左中:选中实体详情 ──────────────────────────────────────
function DetailDock({ selection, cityRow, cityDetail, troop, forces, onClose }) {
  const forceName = (forceId) => forces.find((force) => force.id === forceId)?.name ?? '无主';
  return (
    <section className="dock dock-lc panel detail-dock">
      <div className="panel-title">
        <h2>
          {selection?.kind === 'city'
            ? (cityRow?.name ?? cityDetail?.name ?? '城池')
            : selection?.kind === 'troop'
              ? (troop?.name ?? '部队')
              : '未选中实体'}
        </h2>
        {selection
          ? (
            <button type="button" className="detail-close" title="取消选中" onClick={onClose}>✕</button>
            )
          : null}
      </div>
      <div className="dock-scroll">
        {selection?.kind === 'city' && cityDetail
          ? <CityInfo detail={cityDetail} row={cityRow} />
          : null}
        {selection?.kind === 'city' && !cityDetail && cityRow
          ? (
            <div className="stats-grid">
              <div><span>势力</span><strong>{forceName(cityRow.forceId)}</strong></div>
              <div><span>人口</span><strong>{formatNumber(cityRow.population)}</strong></div>
              <div><span>金</span><strong>{formatNumber(cityRow.gold)}</strong></div>
              <div><span>粮</span><strong>{formatNumber(cityRow.food)}</strong></div>
              <div><span>武将</span><strong>{cityRow.personCount}</strong></div>
            </div>
            )
          : null}
        {selection?.kind === 'troop' && troop
          ? <TroopInfo troop={troop} />
          : null}
        {!selection
          ? <p className="empty">在右侧列表点选城池或部队;指令在中下指令面板下达。</p>
          : null}
      </div>
    </section>
  );
}

function CityInfo({ detail }) {
  return (
    <>
      <div className="panel-title sub">
        <span>{detail.forceName || '无主'} · 行动力 {detail.actionPoint}</span>
      </div>
      <div className="stats-grid">
        <div><span>人口</span><strong>{formatNumber(detail.population)}</strong></div>
        <div><span>资金</span><strong>{formatNumber(detail.gold)}</strong></div>
        <div><span>军粮</span><strong>{formatNumber(detail.food)}</strong></div>
        <div><span>士气</span><strong>{detail.morale} / {detail.maxMorale}</strong></div>
        <div><span>兵力</span><strong>{formatNumber(detail.troops)} / {formatNumber(detail.troopsLimit)}</strong></div>
        <div><span>待命武将</span><strong>{detail.freePersonCount}</strong></div>
      </div>
      <div className="panel-title sub"><h3>武将({detail.persons.length})</h3></div>
      <table className="person-table compact">
        <thead>
          <tr><th>姓名</th><th>身份</th><th>忠</th><th>统</th><th>武</th><th>智</th><th>政</th><th>魅</th></tr>
        </thead>
        <tbody>
          {detail.persons.map((person) => (
            <tr key={person.id} className={person.free ? 'free' : ''}>
              <td><strong>{person.name}</strong></td>
              <td>{PERSON_STATE_NAMES[person.state] ?? person.state}</td>
              <td>{person.loyalty}</td>
              <td>{person.command}</td>
              <td>{person.strength}</td>
              <td>{person.intelligence}</td>
              <td>{person.politics}</td>
              <td>{person.glamour}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {(detail.fieldTroops ?? []).length > 0
        ? (
          <>
            <div className="panel-title sub"><h3>本城部队</h3></div>
            <table className="force-table compact">
              <tbody>
                {detail.fieldTroops.map((troop) => (
                  <tr key={troop.id}>
                    <td><strong>{troop.name}</strong></td>
                    <td>({troop.x},{troop.y})</td>
                    <td>{formatNumber(troop.troops)}兵</td>
                    <td>{troop.actionOver ? '已行动' : '待命'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
          )
        : null}
    </>
  );
}

function TroopInfo({ troop }) {
  return (
    <>
      <div className="panel-title sub">
        <span>{troop.forceName} · {troop.landTroopTypeName} · 移动力 {troop.moveAbility}</span>
      </div>
      <div className="stats-grid">
        <div><span>兵力</span><strong>{formatNumber(troop.troops)}</strong></div>
        <div><span>携粮</span><strong>{formatNumber(troop.food)}</strong></div>
        <div><span>士气</span><strong>{troop.morale}</strong></div>
        <div><span>格坐标</span><strong>({troop.x}, {troop.y})</strong></div>
        <div><span>军团</span><strong>#{troop.corpsId}</strong></div>
        <div><span>状态</span><strong>{troop.actionOver ? '已行动' : '待命'}</strong></div>
      </div>
      <div className="mission-line">
        {troop.missionType > 0
          ? <span><strong>任务:</strong>{troop.missionLabel || `#${troop.missionType}`}</span>
          : <span className="hint">无任务(自由行动)</span>}
      </div>
    </>
  );
}

// ── 2 左下:事件 log ─────────────────────────────────────────
function EventLogDock({ messages, lastOrder }) {
  const rows = useMemo(() => [...messages].reverse(), [messages]);
  const scrollRef = useRef(null);

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = 0;
    }
  }, [rows.length]);

  return (
    <section className="dock dock-bl panel event-dock">
      <div className="panel-title">
        <h2>事件</h2>
        <span>{rows.length} 条</span>
      </div>
      <div className={`strip-order ${lastOrder.tone}`}>{lastOrder.text}</div>
      <div className="dock-scroll event-scroll" ref={scrollRef}>
        <ul className="message-list">
          {rows.length === 0 ? <li className="empty">等待回合摘要……</li> : null}
          {rows.map((message) => (
            <li key={message.seq}>
              {message.date ? <small>{message.date}</small> : null}
              <span>{message.text}</span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}

// ── 4 右上:小地图 ───────────────────────────────────────────
function MinimapDock({ cities, troops, playerForceId, selection, onSelect }) {
  const canvasRef = useRef(null);
  const size = 240;

  const draw = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) {
      return;
    }

    const ctx = canvas.getContext('2d');
    const scale = size / MAP_CELLS;
    ctx.clearRect(0, 0, size, size);
    ctx.fillStyle = '#12151d';
    ctx.fillRect(0, 0, size, size);

    // 领地晕染:每城按势力色画半透明圆,重叠成势力范围感。
    cities.forEach((city) => {
      if (city.forceId <= 0) {
        return;
      }

      ctx.beginPath();
      ctx.fillStyle = forceColor(city.forceId);
      ctx.globalAlpha = 0.16;
      ctx.arc(city.x * scale, city.y * scale, 22, 0, Math.PI * 2);
      ctx.fill();
      ctx.globalAlpha = 1;
    });

    // 部队点。
    troops.forEach((troop) => {
      ctx.fillStyle = '#e8e2c8';
      ctx.fillRect(troop.x * scale - 1.5, troop.y * scale - 1.5, 3, 3);
    });

    // 城点:势力色方块;玩家城加白框。
    cities.forEach((city) => {
      const color = city.forceId > 0 ? forceColor(city.forceId) : '#8a8f9c';
      ctx.fillStyle = color;
      ctx.fillRect(city.x * scale - 4, city.y * scale - 4, 8, 8);
      if (playerForceId > 0 && city.forceId === playerForceId) {
        ctx.strokeStyle = '#ffffff';
        ctx.lineWidth = 1.5;
        ctx.strokeRect(city.x * scale - 6, city.y * scale - 6, 12, 12);
      }
      if (selection?.kind === 'city' && selection.id === city.id) {
        ctx.strokeStyle = '#ffd75e';
        ctx.lineWidth = 2;
        ctx.strokeRect(city.x * scale - 7, city.y * scale - 7, 14, 14);
      }
    });
  }, [cities, troops, playerForceId, selection]);

  useEffect(() => {
    draw();
  }, [draw]);

  const pick = (event) => {
    const rect = event.currentTarget.getBoundingClientRect();
    const cx = ((event.clientX - rect.left) / rect.width) * MAP_CELLS;
    const cy = ((event.clientY - rect.top) / rect.height) * MAP_CELLS;
    let best = null;
    let bestDist = 12;
    cities.forEach((city) => {
      const dist = Math.hypot(city.x - cx, city.y - cy);
      if (dist < bestDist) {
        bestDist = dist;
        best = city;
      }
    });
    if (best) {
      onSelect('city', best.id);
    }
  };

  return (
    <section className="dock dock-tr panel minimap-dock">
      <div className="panel-title">
        <h2>地图</h2>
        <span>{cities.length} 城 · {troops.length} 部队</span>
      </div>
      <canvas ref={canvasRef} width={size} height={size} onClick={pick} title="点城点选中(小地图 v1:势力色城点+领地晕染+部队点;不含地形)" />
    </section>
  );
}

// ── 5 右中:实体列表 ─────────────────────────────────────────
function EntityListDock({
  activeTab, onTab, cities, troops, forces, battles, diplomacy, techniques, turn,
  forceFilter, onForceFilter, selection, onSelect, clientRef, commandRevision, playerGate,
  onDiplomacyCommand, onResearchCommand
}) {
  const visibleCities = forceFilter === 0
    ? cities
    : cities.filter((city) => city.forceId === forceFilter);
  const visibleTroops = forceFilter === 0
    ? troops
    : troops.filter((troop) => troop.forceId === forceFilter);

  return (
    <section className="dock dock-rc panel list-dock">
      <nav className="tab-bar">
        {[
          ['cities', '城市'],
          ['troops', '部队'],
          ['forces', '势力'],
          ['battles', '战报'],
          ['diplomacy', '外交'],
          ['techniques', '科技']
        ].map(([key, label]) => (
          <button
            key={key}
            type="button"
            className={activeTab === key ? 'tab active' : 'tab'}
            onClick={() => onTab(key)}
          >
            {label}
          </button>
        ))}
      </nav>
      <div className="dock-scroll list-content">
        {activeTab === 'cities'
          ? (
            <EntityList
              rows={visibleCities.map((city) => ({
                id: city.id,
                kind: 'city',
                title: city.name,
                subtitle: city.forceName || '无主',
                meta: `人口 ${formatNumber(city.population)} · 金 ${formatNumber(city.gold)} · 粮 ${formatNumber(city.food)} · 武将 ${city.personCount}`,
                forceId: city.forceId
              }))}
              forces={forces}
              forceFilter={forceFilter}
              onForceFilter={onForceFilter}
              selection={selection}
              onSelect={onSelect}
              emptyText="等待城市快照……"
            />
            )
          : activeTab === 'troops'
            ? (
              <EntityList
                rows={visibleTroops.map((troop) => ({
                  id: troop.id,
                  kind: 'troop',
                  title: troop.name,
                  subtitle: troop.forceName || '无主',
                  meta: `(${troop.x},${troop.y}) · 兵 ${formatNumber(troop.troops)} · 士气 ${troop.morale}${troop.actionOver ? ' · 已行动' : ''}`,
                  forceId: troop.forceId
                }))}
                forces={forces}
                forceFilter={forceFilter}
                onForceFilter={onForceFilter}
                selection={selection}
                onSelect={onSelect}
                emptyText="暂无部队(城详情可出征编成)"
              />
              )
            : activeTab === 'forces'
              ? <ForceOverview forces={forces} />
              : activeTab === 'battles'
                ? <BattlePanel battles={battles} />
                : activeTab === 'diplomacy'
                  ? (
                    <DiplomacyPanel
                      diplomacy={diplomacy}
                      forces={forces}
                      cities={cities}
                      turn={turn}
                      clientRef={clientRef}
                      commandRevision={commandRevision}
                      playerGate={playerGate}
                      onCommand={onDiplomacyCommand}
                    />
                    )
                  : (
                    <TechniquePanel
                      techniques={techniques}
                      cities={cities}
                      turn={turn}
                      clientRef={clientRef}
                      commandRevision={commandRevision}
                      playerGate={playerGate}
                      onCommand={onResearchCommand}
                    />
                    )}
      </div>
    </section>
  );
}

// 通用实体行列表:势力色条 + 标题 + 元信息;点选即选中。
function EntityList({ rows, forces, forceFilter, onForceFilter, selection, onSelect, emptyText }) {
  return (
    <section className="entity-list">
      <div className="panel-title">
        <span>按势力过滤</span>
        <select value={forceFilter} onChange={(event) => onForceFilter(Number(event.target.value))}>
          <option value={0}>全部势力</option>
          {forces.map((force) => (
            <option key={force.id} value={force.id}>{force.name}</option>
          ))}
        </select>
      </div>
      <div className="city-rows">
        {rows.length === 0 ? <p className="empty">{emptyText}</p> : null}
        {rows.map((row) => {
          const selected = selection?.kind === row.kind && selection.id === row.id;
          return (
            <button
              key={`${row.kind}-${row.id}`}
              type="button"
              className={selected ? 'city-row selected' : 'city-row'}
              onClick={() => onSelect(row.kind, row.id)}
            >
              <span
                className="force-chip"
                style={{ background: row.forceId > 0 ? forceColor(row.forceId) : '#6b7280' }}
                aria-hidden="true"
              />
              <span className="row-body">
                <strong>{row.title}</strong>
                <span>{row.subtitle}</span>
                <small>{row.meta}</small>
              </span>
            </button>
          );
        })}
      </div>
    </section>
  );
}

// ── 6 右下:回合推进 ─────────────────────────────────────────
function TurnDock({ turn, onEndTurn, onSave, onLoad, connection }) {
  const playerForceId = turn.playerForceId ?? 0;
  const awaitingPlayer = turn.awaitingPlayer ?? false;
  return (
    <section className="dock dock-br panel turn-dock">
      <div className="turn-date">
        <strong>{turn.dateText}</strong>
        <span>第 {turn.turnCount} 回合</span>
      </div>
      <button
        type="button"
        className="end-turn"
        onClick={onEndTurn}
        title={playerForceId > 0 ? '结束玩家回合(原版「进行」):放行君主军团并推进世界' : '推进一个回合'}
      >
        {playerForceId > 0 ? (awaitingPlayer ? '结束回合(进行)' : '推进回合') : '结束回合'}
      </button>
      <div className="turn-minor">
        <button type="button" className="secondary" onClick={onSave}>存档</button>
        <button type="button" className="secondary" onClick={onLoad}>读档</button>
        <span className={`conn ${connection.phase}`}>{connection.phase}</span>
      </div>
    </section>
  );
}

// ── 7 中下:实体指令面板(RTS/MoBA 式指令条) ─────────────────
// 选中城池 = 指令按钮条(训练/探索/奖励/招揽/出征),点按展开参数行(执行武将 chips、
// 目标、资源输入)+ 执行;选中部队 = 移动参数行。无选中 = 引导文案。
function CommandDock({ selection, cityRow, cityDetail, troop, playerGate, onCityCommand, onTroopMove, onExpedition }) {
  const [activeCommand, setActiveCommand] = useState('');
  const [executorIds, setExecutorIds] = useState([]);
  const [rewardIds, setRewardIds] = useState([]);
  const [recruitTargetId, setRecruitTargetId] = useState(0);
  const [expeditionIds, setExpeditionIds] = useState([]);
  const [landTypeId, setLandTypeId] = useState(0);
  const [expeditionTroops, setExpeditionTroops] = useState(3000);
  const [expeditionGold, setExpeditionGold] = useState(500);
  const [expeditionFood, setExpeditionFood] = useState(20000);
  const [targetX, setTargetX] = useState(0);
  const [targetY, setTargetY] = useState(0);
  const [seeded, setSeeded] = useState(false);

  useEffect(() => {
    setActiveCommand('');
    setExecutorIds([]);
    setRewardIds([]);
    setRecruitTargetId(0);
    setExpeditionIds([]);
    setLandTypeId(0);
  }, [selection?.kind, selection?.id]);

  useEffect(() => {
    setSeeded(false);
  }, [selection?.id]);

  useEffect(() => {
    if (troop && !seeded) {
      setTargetX(troop.x);
      setTargetY(troop.y);
      setSeeded(true);
    }
  }, [troop, seeded]);

  if (!selection) {
    return (
      <section className="dock dock-bc panel command-dock">
        <p className="empty">点右侧列表(或小地图城点)选中城池/部队,指令在此下达。</p>
      </section>
    );
  }

  if (selection.kind === 'troop') {
    if (!troop) {
      return (
        <section className="dock dock-bc panel command-dock">
          <p className="empty">部队已消散(溃灭/回城)。</p>
        </section>
      );
    }

    const moveAllowed = playerGate.playerForceId === 0 ||
      (troop.forceId === playerGate.playerForceId && playerGate.awaitingPlayer);
    return (
      <section className="dock dock-bc panel command-dock">
        <div className="command-title">
          <strong>{troop.name}</strong>
          <span>{troop.actionOver ? '已行动' : '待命'} · ({troop.x}, {troop.y})</span>
        </div>
        <div className="command-args">
          <label>目标格 x(北)<input type="number" min={0} max={255} value={targetX} onChange={(e) => setTargetX(Math.max(0, Number(e.target.value)))} /></label>
          <label>目标格 y(东)<input type="number" min={0} max={255} value={targetY} onChange={(e) => setTargetY(Math.max(0, Number(e.target.value)))} /></label>
          <button
            type="button"
            className="execute"
            disabled={troop.actionOver || !moveAllowed}
            title="范围内即时落地;范围外转多回合委任行军"
            onClick={() => onTroopMove({ troopId: troop.id, x: targetX, y: targetY })}
          >
            移动
          </button>
          {!moveAllowed ? <span className="hint">玩家局只在本势力回合开放移动</span> : null}
        </div>
      </section>
    );
  }

  // ── 城池指令 ──
  if (!cityDetail) {
    return (
      <section className="dock dock-bc panel command-dock">
        <p className="empty">{cityRow ? `${cityRow.name}:详情加载中……` : '城池已易主或消失。'}</p>
      </section>
    );
  }

  const playerForceId = playerGate?.playerForceId ?? 0;
  const commandsAllowed = playerForceId === 0 || (cityDetail.forceId === playerForceId && playerGate.awaitingPlayer);
  const freePersons = cityDetail.persons.filter((person) => person.free);
  const rewardTargets = cityDetail.persons.filter(
    (person) => person.state >= 2 && person.state <= 4 && person.loyalty < 100
  );
  const wildPersons = cityDetail.wildPersons ?? [];
  const recruitTarget = wildPersons.find((person) => person.id === recruitTargetId) ?? null;
  const landTypes = (cityDetail.troopTypes ?? []).filter((type) => type.isLand);
  const expeditionPersons = freePersons.filter((person) => expeditionIds.includes(person.id));
  const executors = freePersons.filter((person) => executorIds.includes(person.id));
  const rewardPersons = rewardTargets.filter((person) => rewardIds.includes(person.id));

  const toggle = (ids, setIds, personId, cap) => {
    if (ids.includes(personId)) {
      setIds(ids.filter((id) => id !== personId));
    } else if (ids.length < cap) {
      setIds([...ids, personId]);
    }
  };

  const chips = (persons, ids, setIds, cap) => (
    <div className="person-chips">
      {persons.length === 0 ? <span className="hint">无候选</span> : null}
      {persons.map((person) => (
        <button
          key={person.id}
          type="button"
          className={ids.includes(person.id) ? 'chip selected' : 'chip'}
          title={`统${person.command} 武${person.strength} 智${person.intelligence} 政${person.politics}`}
          onClick={() => toggle(ids, setIds, person.id, cap)}
        >
          {person.name}
        </button>
      ))}
    </div>
  );

  const gateHint = playerForceId === 0
    ? null
    : (cityDetail.forceId === playerForceId
        ? (playerGate.awaitingPlayer ? null : '非本势力回合,指令待玩家回合开放')
        : '玩家局只对本势力城市开放内政指令');

  return (
    <section className="dock dock-bc panel command-dock">
      <div className="command-title">
        <strong>{cityDetail.name}</strong>
        <span>行动力 {cityDetail.actionPoint} · 待命武将 {cityDetail.freePersonCount}</span>
        {gateHint ? <span className="hint">{gateHint}</span> : null}
      </div>
      <div className="command-bar">
        {CITY_COMMANDS.map((command) => (
          <button
            key={command.type}
            type="button"
            className={activeCommand === command.type ? 'command-btn active' : 'command-btn'}
            onClick={() => setActiveCommand(activeCommand === command.type ? '' : command.type)}
          >
            {command.label}
          </button>
        ))}
      </div>
      {activeCommand
        ? (
          <div className="command-args">
            {activeCommand === 'train' || activeCommand === 'search'
              ? (
                <>
                  {chips(freePersons, executorIds, setExecutorIds, 3)}
                  <button
                    type="button"
                    className="execute"
                    disabled={executors.length === 0 || !commandsAllowed}
                    title={CITY_COMMANDS.find((command) => command.type === activeCommand)?.hint}
                    onClick={() => onCityCommand(activeCommand, { cityId: cityDetail.id, personIds: executors.map((p) => p.id) })}
                  >
                    执行({executors.length})
                  </button>
                </>
                )
              : null}
            {activeCommand === 'reward'
              ? (
                <>
                  {chips(rewardTargets, rewardIds, setRewardIds, 10)}
                  <button
                    type="button"
                    className="execute"
                    disabled={rewardPersons.length === 0 || !commandsAllowed}
                    onClick={() => onCityCommand('reward', { cityId: cityDetail.id, personIds: rewardPersons.map((p) => p.id) })}
                  >
                    执行({rewardPersons.length})
                  </button>
                </>
                )
              : null}
            {activeCommand === 'recruit'
              ? (
                <>
                  <span className="hint">执行(1):</span>
                  {chips(freePersons, executorIds, setExecutorIds, 1)}
                  <span className="hint">在野目标(1):</span>
                  <select value={recruitTargetId} onChange={(e) => setRecruitTargetId(Number(e.target.value))}>
                    <option value={0}>选择在野人才</option>
                    {wildPersons.map((person) => (
                      <option key={person.id} value={person.id}>
                        {person.name}(统{person.command} 智{person.intelligence})
                      </option>
                    ))}
                  </select>
                  <button
                    type="button"
                    className="execute"
                    disabled={executors.length !== 1 || !recruitTarget || !commandsAllowed}
                    onClick={() => onCityCommand('recruit', {
                      cityId: cityDetail.id,
                      personIds: executors.map((p) => p.id),
                      targetPersonId: recruitTarget?.id ?? 0
                    })}
                  >
                    执行
                  </button>
                </>
                )
              : null}
            {activeCommand === 'expedition'
              ? (
                <>
                  <span className="hint">编成(≤3):</span>
                  {chips(freePersons, expeditionIds, setExpeditionIds, 3)}
                  <label>
                    兵种
                    <select value={landTypeId} onChange={(e) => setLandTypeId(Number(e.target.value))}>
                      {landTypes.map((type) => (
                        <option key={type.id} value={type.id}>{type.name}</option>
                      ))}
                    </select>
                  </label>
                  <label>兵力<input type="number" min={1} max={cityDetail.troops} value={expeditionTroops} onChange={(e) => setExpeditionTroops(Math.max(0, Number(e.target.value)))} /></label>
                  <label>携金<input type="number" min={0} max={cityDetail.gold} value={expeditionGold} onChange={(e) => setExpeditionGold(Math.max(0, Number(e.target.value)))} /></label>
                  <label>携粮<input type="number" min={0} max={cityDetail.food} value={expeditionFood} onChange={(e) => setExpeditionFood(Math.max(0, Number(e.target.value)))} /></label>
                  <button
                    type="button"
                    className="execute"
                    disabled={expeditionPersons.length === 0 || landTypes.length === 0 || !commandsAllowed}
                    onClick={() => onExpedition({
                      cityId: cityDetail.id,
                      personIds: expeditionPersons.map((person) => person.id),
                      landTroopTypeId: landTypeId || landTypes[0]?.id,
                      troops: expeditionTroops,
                      gold: expeditionGold,
                      food: expeditionFood
                    })}
                  >
                    出征({expeditionPersons.length})
                  </button>
                </>
                )
              : null}
        </div>
          )
        : null}
    </section>
  );
}

// ── 以下为右列页签复用面板(战报/外交/科技/势力概览) ──

function BattlePanel({ battles }) {
  const [turnFilter, setTurnFilter] = useState(0);
  const turns = useMemo(() => {
    const set = new Set();
    battles.forEach((battle) => {
      for (let turn = battle.turnStart; turn <= battle.turnLast; turn += 1) {
        set.add(turn);
      }
    });
    return [...set].sort((a, b) => b - a);
  }, [battles]);

  const visible = turnFilter === 0
    ? battles
    : battles.filter((battle) => battle.turnStart <= turnFilter && turnFilter <= battle.turnLast);
  const cards = [...visible].reverse();

  return (
    <section className="battle-panel">
      <div className="panel-title">
        <h2>战报</h2>
        <select value={turnFilter} onChange={(event) => setTurnFilter(Number(event.target.value))}>
          <option value={0}>全部回合({battles.length} 战)</option>
          {turns.map((turn) => (
            <option key={turn} value={turn}>第 {turn} 回合</option>
          ))}
        </select>
      </div>
      <div className="battle-list">
        {cards.length === 0 ? <p className="empty">尚无交战记录</p> : null}
        {cards.map((battle) => <BattleCard key={battle.seq} battle={battle} />)}
      </div>
    </section>
  );
}

const BATTLE_RESULT_LABELS = {
  ongoing: '进行中',
  'defender-destroyed': '守方溃灭',
  'attacker-destroyed': '攻方溃灭',
  'city-fallen': '城陷',
  'force-fallen': '势力灭亡'
};
const BATTLE_EVENT_LABELS = {
  strike: '打击',
  counter: '反击',
  'siege-garrison': '守军杀伤',
  'siege-durability': '城防破坏',
  'troop-destroyed': '溃灭',
  'city-fall': '城陷',
  'force-fall': '灭亡'
};
const PARTICIPANT_KIND_LABELS = { troop: '部队', city: '城池', building: '据点' };

function BattleCard({ battle }) {
  return (
    <article className={`battle-card result-${battle.result}`}>
      <header>
        <div className="battle-title">
          <span className="side">
            <strong>{battle.attacker.name}</strong>
            <small>{battle.attacker.forceName}</small>
          </span>
          <span className="vs">对战</span>
          <span className="side">
            <strong>{battle.defender.name}</strong>
            <small>{battle.defender.forceName || '无主'}</small>
          </span>
        </div>
        <div className="battle-meta">
          <span className={`result-badge ${battle.result}`}>{BATTLE_RESULT_LABELS[battle.result] ?? battle.result}</span>
          <span>第 {battle.turnStart}–{battle.turnLast} 回合</span>
          <span>总杀伤 {formatNumber(battle.damageDealt)}</span>
        </div>
      </header>
      <div className="event-rows">
        {(battle.events ?? []).map((event, index) => (
          <div key={index} className="event-row">
            <span className="turn">T{event.turn}</span>
            <span className="kind">{BATTLE_EVENT_LABELS[event.kind] ?? event.kind}</span>
            <span className="actors">{event.attacker} → {event.defender}</span>
            {event.damage > 0 ? <span className="damage">伤 {formatNumber(event.damage)}</span> : null}
          </div>
        ))}
      </div>
    </article>
  );
}

const ALLIANCE_TYPE_LABELS = {
  Alliance: '同盟',
  Truce: '停战协议',
  Trade: '通商协议'
};

function DiplomacyPanel({ diplomacy, forces, cities, turn, clientRef, commandRevision, playerGate, onCommand }) {
  const [dispatchCityId, setDispatchCityId] = useState(0);
  const [diplomatId, setDiplomatId] = useState(0);
  const [targetForceId, setTargetForceId] = useState(0);
  const [actionType, setActionType] = useState('sendGift');
  const [allianceGold, setAllianceGold] = useState(3000);

  const playerForceId = playerGate?.playerForceId ?? diplomacy.playerForceId ?? 0;
  const playerCities = useMemo(
    () => cities.filter((city) => city.forceId === playerForceId),
    [cities, playerForceId]
  );

  const dispatchCityDetail = useCityDetail(clientRef, dispatchCityId || 0, `diplomacy:${commandRevision}`);
  const freePersons = dispatchCityDetail?.persons?.filter((person) => person.free) ?? [];

  useEffect(() => {
    if (!dispatchCityId && playerCities.length > 0) {
      setDispatchCityId(playerCities[0].id);
    }
  }, [dispatchCityId, playerCities]);

  useEffect(() => {
    setDiplomatId(0);
  }, [dispatchCityId]);

  useEffect(() => {
    if (targetForceId === 0 && diplomacy.relations.length > 0) {
      setTargetForceId(diplomacy.relations[0].forceId);
    }
  }, [targetForceId, diplomacy.relations]);

  const dispatchCity = playerCities.find((city) => city.id === dispatchCityId) ?? null;
  const gatesOk = Boolean(dispatchCity && freePersons.length > 0 && dispatchCity.gold >= 1000);
  const commandsAllowed = playerForceId === 0 || playerGate.awaitingPlayer;
  const canOrder = gatesOk && commandsAllowed && diplomatId > 0 && targetForceId > 0;
  const relationRows = [...diplomacy.relations].sort((a, b) => b.relation - a.relation);

  return (
    <section className="diplomacy-panel">
      <div className="panel-title">
        <h2>外交</h2>
        <span>{playerForceId > 0 ? `${diplomacy.playerForceName || `#${playerForceId}`} 的邦交面` : '全图邦交观察'}</span>
      </div>
      <table className="force-table compact">
        <thead>
          <tr><th>势力</th><th>关系</th><th>协议</th></tr>
        </thead>
        <tbody>
          {relationRows.map((relation) => (
            <tr
              key={relation.forceId}
              className={relation.forceId === targetForceId ? 'selected' : ''}
              onClick={() => setTargetForceId(relation.forceId)}
            >
              <td><strong>{relation.forceName}</strong></td>
              <td className={relation.relation >= 0 ? 'relation-positive' : 'relation-negative'}>
                {relation.relation > 0 ? '+' : ''}{relation.relation}
              </td>
              <td>{relation.allied ? '同盟' : relation.hasAgreement ? '有协议' : '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="expedition-form diplomacy-form">
        <label>
          派出城市
          <select value={dispatchCityId} onChange={(event) => setDispatchCityId(Number(event.target.value))}>
            {playerCities.map((city) => (
              <option key={city.id} value={city.id}>{city.name}(金 {formatNumber(city.gold)})</option>
            ))}
          </select>
        </label>
        <label>
          使者
          <select value={diplomatId} onChange={(event) => setDiplomatId(Number(event.target.value))}>
            <option value={0}>选择使者</option>
            {freePersons.map((person) => (
              <option key={person.id} value={person.id}>{person.name}</option>
            ))}
          </select>
        </label>
        <label>
          行动
          <select value={actionType} onChange={(event) => setActionType(event.target.value)}>
            <option value="sendGift">送礼(1000 金)</option>
            <option value="alliance">结盟</option>
            <option value="discardAlliance">摒弃同盟</option>
          </select>
        </label>
        {actionType === 'alliance'
          ? (
            <label>
              结盟金
              <input type="number" min={0} step={500} value={allianceGold} onChange={(event) => setAllianceGold(Math.max(0, Number(event.target.value)))} />
            </label>
            )
          : null}
        <button type="button" disabled={!canOrder} onClick={() => onCommand(actionType, {
          cityId: dispatchCityId,
          personIds: [diplomatId],
          targetForceId,
          resourceValue: actionType === 'alliance' ? allianceGold : 0
        })}>
          派遣
        </button>
      </div>
    </section>
  );
}

const TECHNIQUE_KIND_LABELS = {
  1: '枪兵系',
  2: '戟兵系',
  3: '弩兵系',
  4: '骑兵系',
  5: '军制系',
  6: '兵器系',
  7: '设施系',
  8: '火器系',
  9: '政略系'
};

function TechniquePanel({ techniques, cities, turn, clientRef, commandRevision, playerGate, onCommand }) {
  const [dispatchCityId, setDispatchCityId] = useState(0);
  const [expandedKind, setExpandedKind] = useState('');

  const playerForceId = playerGate?.playerForceId ?? techniques.forceId ?? 0;
  const playerCities = useMemo(
    () => cities.filter((city) => city.forceId === playerForceId),
    [cities, playerForceId]
  );

  const dispatchCityDetail = useCityDetail(clientRef, dispatchCityId || 0, `technique:${commandRevision}`);
  const freePersons = dispatchCityDetail?.persons?.filter((person) => person.free) ?? [];

  useEffect(() => {
    if (!dispatchCityId && playerCities.length > 0) {
      setDispatchCityId(playerCities[0].id);
    }
  }, [dispatchCityId, playerCities]);

  const dispatchCity = playerCities.find((city) => city.id === dispatchCityId) ?? null;
  const commandsAllowed = playerForceId === 0 || playerGate.awaitingPlayer;
  const researching = techniques.researching ?? null;

  const groups = useMemo(() => {
    const map = new Map();
    (techniques.techniques ?? []).forEach((technique) => {
      const key = technique.kind ?? '';
      if (!map.has(key)) {
        map.set(key, []);
      }
      map.get(key).push(technique);
    });
    return [...map.entries()]
      .map(([kind, rows]) => [kind, rows.sort((a, b) => (a.level ?? 0) - (b.level ?? 0) || a.id - b.id)]);
  }, [techniques.techniques]);

  return (
    <section className="technique-panel">
      <div className="panel-title">
        <h2>科技</h2>
        <span>
          {techniques.forceName || '——'} · 技巧点 {formatNumber(techniques.techniquePoint)}
          {researching ? ` · 研究中:${researching.name}(余 ${researching.leftCounter} 回合)` : ''}
        </span>
      </div>
      <div className="technique-groups">
        {groups.map(([kind, rows]) => {
          const ownedCount = rows.filter((row) => row.owned).length;
          const open = expandedKind === String(kind);
          return (
            <div key={kind} className="technique-group">
              <button
                type="button"
                className="technique-group-head"
                onClick={() => setExpandedKind(open ? '' : String(kind))}
              >
                <strong>{TECHNIQUE_KIND_LABELS[kind] ?? `系 ${kind}`}</strong>
                <span>{ownedCount} / {rows.length}</span>
              </button>
              {open
                ? (
                  <table className="force-table compact">
                    <tbody>
                      {rows.map((row) => (
                        <tr key={row.id} className={row.owned ? 'owned' : ''}>
                          <td>
                            <strong>{row.name}</strong>
                            <small className="technique-desc">{row.desc}</small>
                          </td>
                          <td>{row.owned ? '已拥有' : row.canResearch ? '可研究' : '前置未满足'}</td>
                          <td>
                            {row.owned || !commandsAllowed || researching
                              ? null
                              : (
                                <button
                                  type="button"
                                  disabled={!row.canResearch || !dispatchCity}
                                  onClick={() => onCommand({
                                    cityId: dispatchCity.id,
                                    techniqueId: row.id,
                                    personIds: []
                                  })}
                                >
                                  研究
                                </button>
                                )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  )
                : null}
            </div>
          );
        })}
      </div>
    </section>
  );
}

function ForceOverview({ forces }) {
  return (
    <section className="force-overview">
      <table className="force-table compact">
        <thead>
          <tr><th>名称</th><th>君主</th><th>城市</th><th>武将</th><th>状态</th></tr>
        </thead>
        <tbody>
          {forces.map((force) => (
            <tr key={force.id}>
              <td><strong>{force.name}</strong></td>
              <td>{force.governorName || '—'}</td>
              <td>{force.cityCount}</td>
              <td>{force.personCount}</td>
              <td>{force.alive ? '存续' : '灭亡'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

// 开局势力选择(保留原交互;走 sango.selectPlayerForce 原版玩家路径)。
function ForceSelectOverlay({ turn, forces, cities, onSelect, onDismiss }) {
  const [selected, setSelected] = useState(0);
  const [busy, setBusy] = useState(false);
  const alive = forces.filter((force) => force.alive);
  const cityCountByForce = useMemo(() => {
    const counts = new Map();
    cities.forEach((city) => {
      if (city.forceId > 0) {
        counts.set(city.forceId, (counts.get(city.forceId) ?? 0) + 1);
      }
    });
    return counts;
  }, [cities]);
  const selectedForce = alive.find((force) => force.id === selected) ?? null;

  const confirm = async () => {
    if (!selectedForce || busy) {
      return;
    }

    setBusy(true);
    await onSelect(selectedForce.id);
    setBusy(false);
  };

  return (
    <div className="force-select-overlay">
      <section className="panel force-select-panel">
        <div className="panel-title">
          <h2>选择开局势力</h2>
          <span>{alive.length} 家 · 原版玩家接入(君主军团回合制,行动力约束生效)</span>
        </div>
        <p className="force-select-heading">开局 · {turn.dateText} · 第 {turn.turnCount} 回合</p>
        <div className="force-select-list">
          {alive.map((force) => (
            <button
              key={force.id}
              type="button"
              className={force.id === selected ? 'city-row selected' : 'city-row'}
              onClick={() => setSelected(force.id)}
            >
              <span
                className="force-chip"
                style={{ background: forceColor(force.id) }}
                aria-hidden="true"
              />
              <strong>{force.name}</strong>
              <span>君主 {force.governorName || '—'}</span>
              <small>城市 {cityCountByForce.get(force.id) ?? 0} · 武将 {force.personCount}</small>
            </button>
          ))}
        </div>
        <div className="force-select-actions">
          <button
            type="button"
            className="end-turn"
            disabled={!selectedForce || busy}
            onClick={confirm}
          >
            {busy ? '重整世界中……' : selectedForce ? `以${selectedForce.name}开局` : '选择一家势力'}
          </button>
          <button
            type="button"
            className="secondary"
            disabled={busy}
            title="保持全托管观察模式:不设玩家势力,回合自由推进"
            onClick={onDismiss}
          >
            旁观模式(全托管)
          </button>
        </div>
      </section>
    </div>
  );
}

function formatNumber(value) {
  return Number(value ?? 0).toLocaleString('zh-CN');
}

createRoot(document.getElementById('root')).render(<App />);
