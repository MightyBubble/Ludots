import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  SANGO_BATTLES_TOPIC,
  SANGO_CITIES_TOPIC,
  SANGO_CITY_TOPIC,
  SANGO_DIPLOMACY_TOPIC,
  SANGO_FORCES_TOPIC,
  SANGO_MESSAGES_TOPIC,
  SANGO_TROOPS_TOPIC,
  SANGO_TURN_TOPIC,
  createLudotsDataPlaneClient,
  ensureLudotsDataPlaneTransport
} from './dataplane/client.js';
import './styles.css';

// 命令名与窗口术语:招揽=人事/登庸武将,探索=人事/探索人才,训练=军事/训练,奖励=人事/褒赏
// (sango-src Game/System/City 各 CityXxx 系统的 customMenuName)。
const CITY_COMMAND_LABELS = {
  train: '训练',
  search: '探索',
  reward: '奖励',
  recruit: '招揽'
};

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

// 外交面板的行动术语(原版 CityDiplomacy* 系统的 customMenuName)。
const DIPLOMACY_COMMAND_LABELS = {
  sendGift: '送礼',
  alliance: '结盟',
  discardAlliance: '摒弃同盟'
};
const ALLIANCE_TYPE_LABELS = {
  Alliance: '同盟',
  Truce: '停战协议',
  Trade: '通商协议'
};

// 战报卡终局/事件型标签(SangoCombatAnnals 的 result/event kind 中文投影)。
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

// map-first 布局的折叠记忆键:抽屉/底部条/活动页签跨会话保留(M3.b)。
const STORAGE_KEYS = {
  drawerOpen: 'sango.ui.drawerOpen',
  bottomOpen: 'sango.ui.bottomOpen',
  tab: 'sango.ui.tab'
};

// 折叠状态记忆:localStorage 不可用(隐私模式/桥接异常)时静默退化为会话内状态。
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
  const [activeTab, setActiveTab] = usePersistentState(STORAGE_KEYS.tab, 'cities');
  const [drawerOpen, setDrawerOpen] = usePersistentState(STORAGE_KEYS.drawerOpen, false);
  const [bottomOpen, setBottomOpen] = usePersistentState(STORAGE_KEYS.bottomOpen, false);
  const [forceFilter, setForceFilter] = useState(0);
  // 浮动卡片槽:城市详情与部队详情共用右侧卡片位,一次只显示一张(M3.b map-first)。
  const [detailCard, setDetailCard] = useState(null);
  const [commandCount, setCommandCount] = useState(0);
  const [lastOrder, setLastOrder] = useState({ text: '尚无指令', tone: 'idle' });
  const [selectDismissed, setSelectDismissed] = useState(false);

  const selectedCityId = detailCard?.kind === 'city' ? detailCard.id : 0;
  const detailRevision = `${data.turn.turnCount}:${commandCount}`;
  const detail = useCityDetail(clientRef, selectedCityId, detailRevision);

  // 开局势力选择(M3.a):第 0 回合且未选玩家时覆盖一层选势力面板;旁观模式可关闭。
  const showForceSelect = !selectDismissed && data.turn.turnCount === 0 && data.turn.playerForceId === 0;
  const playerForceId = data.turn.playerForceId ?? 0;
  const awaitingPlayer = data.turn.awaitingPlayer ?? false;

  const visibleCities = useMemo(() => {
    if (forceFilter === 0) {
      return data.cities.cities;
    }

    return data.cities.cities.filter((city) => city.forceId === forceFilter);
  }, [data.cities.cities, forceFilter]);

  const openCityCard = useCallback((cityId) => {
    setDetailCard({ kind: 'city', id: cityId });
  }, []);

  const selectPlayerForce = useCallback(async (forceId) => {
    setLastOrder({ text: '势力选择:以所选势力重整开局……', tone: 'pending' });
    const result = await command('sango.selectPlayerForce', { forceId });
    setLastOrder(result.ok
      ? { text: '势力选择:已按原版玩家路径重装世界,内政指令即刻可用', tone: 'ok' }
      : { text: `势力选择失败:${result.message}`, tone: 'error' });
    return result;
  }, [command]);

  const issueCommand = useCallback(async (type, payload) => {
    setLastOrder({ text: `${CITY_COMMAND_LABELS[type] ?? type}:下达中……`, tone: 'pending' });
    const result = await command('sango.cityCommand', { type, ...payload });
    if (result.ok) {
      setCommandCount((count) => count + 1);
      setLastOrder({ text: `${CITY_COMMAND_LABELS[type] ?? type}:已受理,下一轮话题刷新后可见变化`, tone: 'ok' });
    } else {
      setLastOrder({ text: `${CITY_COMMAND_LABELS[type] ?? type} 被拒绝:${result.message}`, tone: 'error' });
    }

    return result;
  }, [command]);

  const troopCommand = useCallback(async (label, name, payload) => {
    setLastOrder({ text: `${label}:下达中……`, tone: 'pending' });
    const result = await command(name, payload);
    if (result.ok) {
      setCommandCount((count) => count + 1);
      setLastOrder({ text: `${label}:已受理,部队面板与地图标记随后刷新`, tone: 'ok' });
    } else {
      setLastOrder({ text: `${label} 被拒绝:${result.message}`, tone: 'error' });
    }

    return result;
  }, [command]);

  const diplomacyCommand = useCallback(async (type, payload) => {
    const label = DIPLOMACY_COMMAND_LABELS[type] ?? type;
    setLastOrder({ text: `${label}:派遣使者中……`, tone: 'pending' });
    const result = await command('sango.diplomacyCommand', { type, ...payload });
    if (result.ok) {
      setCommandCount((count) => count + 1);
      setLastOrder({
        text: `${label}:使者已出发(按路程逐日赶赴对方君主城,结果见消息流)`,
        tone: 'ok'
      });
    } else {
      setLastOrder({ text: `${label} 被拒绝:${result.message}`, tone: 'error' });
    }

    return result;
  }, [command]);

  const endTurn = useCallback(async () => {
    setLastOrder({ text: '结束回合:等待时钟推进……', tone: 'pending' });
    const result = await command('sango.endTurn', {});
    setLastOrder(result.ok
      ? { text: '结束回合:已请求时钟推进,回合结算后刷新', tone: 'ok' }
      : { text: `结束回合失败:${result.message}`, tone: 'error' });
  }, [command]);

  const saveGame = useCallback(async () => {
    setLastOrder({ text: '存档:写入引擎存档槽……', tone: 'pending' });
    const result = await command('sango.save', {});
    setLastOrder(result.ok
      ? { text: '存档:已写入存档槽(世界态 + 随机流)', tone: 'ok' }
      : { text: `存档失败:${result.message}`, tone: 'error' });
  }, [command]);

  const loadGame = useCallback(async () => {
    setLastOrder({ text: '读档:回灌最近存档……', tone: 'pending' });
    const result = await command('sango.load', {});
    setLastOrder(result.ok
      ? { text: '读档:已回灌最近存档,话题下一轮刷新', tone: 'ok' }
      : { text: `读档失败:${result.message}`, tone: 'error' });
  }, [command]);

  return (
    <main className="app">
      <TopBar
        turn={data.turn}
        connection={connection}
        onEndTurn={endTurn}
        onSave={saveGame}
        onLoad={loadGame}
      />
      <LeftDrawer
        open={drawerOpen}
        onToggle={() => setDrawerOpen((open) => !open)}
        activeTab={activeTab}
        onTab={setActiveTab}
      >
        {activeTab === 'forces'
          ? <ForceOverview forces={data.forces.forces} />
          : activeTab === 'troops'
          ? (
            <TroopListPanel
              troops={data.troops.troops}
              forces={data.forces.forces}
              forceFilter={forceFilter}
              onForceFilter={setForceFilter}
              selectedTroopId={detailCard?.kind === 'troop' ? detailCard.id : 0}
              onSelect={(troopId) => setDetailCard({ kind: 'troop', id: troopId })}
            />
            )
          : activeTab === 'battles'
          ? <BattlePanel battles={data.battles.battles} />
          : activeTab === 'diplomacy'
          ? (
            <DiplomacyPanel
              diplomacy={data.diplomacy}
              forces={data.forces.forces}
              cities={data.cities.cities}
              turn={data.turn}
              clientRef={clientRef}
              commandRevision={detailRevision}
              playerGate={{ playerForceId, awaitingPlayer }}
              onCommand={diplomacyCommand}
            />
            )
          : (
            <CityListPanel
              cities={visibleCities}
              forces={data.forces.forces}
              forceFilter={forceFilter}
              onForceFilter={setForceFilter}
              selectedCityId={selectedCityId}
              onSelect={openCityCard}
            />
            )}
      </LeftDrawer>
      {detailCard?.kind === 'city' && detail
        ? (
          <CityDetailCard
            detail={detail}
            onClose={() => setDetailCard(null)}
            playerGate={{ playerForceId, awaitingPlayer }}
            onCommand={issueCommand}
            onExpedition={(payload) => troopCommand('出征', 'sango.createTroop', payload)}
          />
          )
        : null}
      {detailCard?.kind === 'troop'
        ? (
          <TroopDetailCard
            troops={data.troops.troops}
            troopId={detailCard.id}
            onClose={() => setDetailCard(null)}
            playerGate={{ playerForceId, awaitingPlayer }}
            onMove={(payload) => troopCommand('移动', 'sango.moveTroop', payload)}
          />
          )
        : null}
      <BottomStrip
        open={bottomOpen}
        onToggle={() => setBottomOpen((open) => !open)}
        messages={data.messages.messages}
        lastOrder={lastOrder}
        connection={connection}
        counts={{
          cities: data.cities.cities.length,
          forces: data.forces.forces.length,
          troops: data.troops.troops.length,
          battles: data.battles.battles.length
        }}
      />
      {showForceSelect
        ? <ForceSelectOverlay turn={data.turn} forces={data.forces.forces} cities={data.cities.cities} onSelect={selectPlayerForce} onDismiss={() => setSelectDismissed(true)} />
        : null}
    </main>
  );
}

// 开局势力选择(M3.a,原版 window_scenario_force_select 的 Web 面):一次性开局动作,
// 走 sango.selectPlayerForce 命令(原版 CheckPlayer 数据面)带玩家重装世界。
// M3.b:自带「开局 · 日期 · 第 0 回合」标题,与地图渲染解耦,避免暗背景下误读顶栏。
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
            title="以所选势力开局:世界按原版玩家路径重整(仅开局一次)"
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

function useSangoSession() {
  const clientRef = useRef(null);
  const [data, setData] = useState({
    cities: EMPTY_CITIES,
    forces: EMPTY_FORCES,
    turn: EMPTY_TURN,
    messages: EMPTY_MESSAGES,
    troops: EMPTY_TROOPS,
    battles: EMPTY_BATTLES,
    diplomacy: EMPTY_DIPLOMACY
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
      [SANGO_DIPLOMACY_TOPIC, (diplomacy) => setData((current) => ({ ...current, diplomacy }))]
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

// map-first 细悬浮顶栏(M3.b):单行高度,半透明 + blur,不再横向铺满。
function TopBar({ turn, connection, onEndTurn, onSave, onLoad }) {
  const playerForceId = turn.playerForceId ?? 0;
  const awaitingPlayer = turn.awaitingPlayer ?? false;
  return (
    <header className="top-bar">
      <h1>三国 · 内政</h1>
      <div className="date-block">
        <strong>{turn.dateText}</strong>
        <span>第 {turn.turnCount} 回合</span>
        {playerForceId > 0
          ? (
            <span className={awaitingPlayer ? 'player-badge active' : 'player-badge'}>
              玩家:{turn.playerForceName || `#${playerForceId}`}{awaitingPlayer ? ' · 待玩家行动' : ' · 结算中'}
            </span>
            )
          : null}
      </div>
      <div className="top-actions">
        <button type="button" className="secondary" onClick={onSave}>存档</button>
        <button type="button" className="secondary" onClick={onLoad}>读档</button>
        <button
          type="button"
          className="end-turn"
          onClick={onEndTurn}
          title={playerForceId > 0 ? '结束玩家回合(原版「进行」):放行君主军团并推进世界' : '推进一个回合'}
        >
          {playerForceId > 0 ? (awaitingPlayer ? '结束回合(进行)' : '推进回合') : '结束回合'}
        </button>
      </div>
      {connection.error ? <span className="error-line">{connection.error}</span> : null}
    </header>
  );
}

// 左侧可折叠抽屉(M3.b):收起时只剩竖把手,城市/势力/部队/战报列表全部入住抽屉体。
function LeftDrawer({ open, onToggle, activeTab, onTab, children }) {
  return (
    <aside className={open ? 'left-drawer open' : 'left-drawer'}>
      <button
        type="button"
        className="drawer-handle"
        title={open ? '收起面板,让地图全屏' : '展开面板(城市/势力/部队/战报)'}
        aria-expanded={open}
        onClick={onToggle}
      >
        <span className="drawer-glyph" aria-hidden="true">{open ? '‹' : '›'}</span>
        <span className="drawer-label">面板</span>
      </button>
      {open
        ? (
          <div className="drawer-body panel">
            <nav className="tab-bar">
              <button
                type="button"
                className={activeTab === 'cities' ? 'tab active' : 'tab'}
                onClick={() => onTab('cities')}
              >
                城市
              </button>
              <button
                type="button"
                className={activeTab === 'forces' ? 'tab active' : 'tab'}
                onClick={() => onTab('forces')}
              >
                势力
              </button>
              <button
                type="button"
                className={activeTab === 'troops' ? 'tab active' : 'tab'}
                onClick={() => onTab('troops')}
              >
                部队
              </button>
              <button
                type="button"
                className={activeTab === 'battles' ? 'tab active' : 'tab'}
                onClick={() => onTab('battles')}
              >
                战报
              </button>
              <button
                type="button"
                className={activeTab === 'diplomacy' ? 'tab active' : 'tab'}
                onClick={() => onTab('diplomacy')}
              >
                外交
              </button>
            </nav>
            <div className="drawer-content">{children}</div>
          </div>
          )
        : null}
    </aside>
  );
}

// 底部可折叠窄条(M3.b):收起时一行(最新消息 + 指令回执 + 连接/计数),展开为消息流。
function BottomStrip({ open, onToggle, messages, lastOrder, connection, counts }) {
  const rows = useMemo(() => [...messages].reverse(), [messages]);
  const latest = rows[0] ?? null;
  return (
    <footer className={open ? 'bottom-strip open' : 'bottom-strip'}>
      <div className="strip-head">
        <button
          type="button"
          className="strip-toggle"
          title={open ? '收起消息流' : '展开消息流'}
          aria-expanded={open}
          onClick={onToggle}
        >
          <span className="drawer-glyph" aria-hidden="true">{open ? '▾' : '▴'}</span>
          消息 {rows.length}
        </button>
        <span className={`strip-order ${lastOrder.tone}`}>{lastOrder.text}</span>
        {!open && latest
          ? (
            <span className="strip-latest">
              <small>{latest.date}</small>
              {latest.text}
            </span>
            )
          : null}
        <span className="strip-meta">
          {connection.phase} · {connection.transport} · 城市 {counts.cities} · 势力 {counts.forces} · 部队 {counts.troops} · 战报 {counts.battles}
        </span>
      </div>
      {open
        ? (
          <div className="strip-body">
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
          )
        : null}
    </footer>
  );
}

function CityListPanel({ cities, forces, forceFilter, onForceFilter, selectedCityId, onSelect }) {
  return (
    <section className="drawer-panel city-list-panel">
      <div className="panel-title">
        <h2>城市</h2>
        <select value={forceFilter} onChange={(event) => onForceFilter(Number(event.target.value))}>
          <option value={0}>全部势力</option>
          {forces.map((force) => (
            <option key={force.id} value={force.id}>{force.name}</option>
          ))}
        </select>
      </div>
      <div className="city-rows">
        {cities.length === 0 ? <p className="empty">等待城市快照……</p> : null}
        {cities.map((city) => (
          <button
            key={city.id}
            type="button"
            className={city.id === selectedCityId ? 'city-row selected' : 'city-row'}
            onClick={() => onSelect(city.id)}
          >
            <strong>{city.name}</strong>
            <span>{city.forceName || '无主'}</span>
            <small>
              人口 {formatNumber(city.population)} · 金 {formatNumber(city.gold)} · 粮 {formatNumber(city.food)} · 武将 {city.personCount}
            </small>
          </button>
        ))}
      </div>
    </section>
  );
}

function CityDetailCard({ detail, onClose, playerGate, onCommand, onExpedition }) {
  return (
    <section className="detail-card panel">
      <CityDetailPanel detail={detail} onClose={onClose} playerGate={playerGate} onCommand={onCommand} onExpedition={onExpedition} />
    </section>
  );
}

function CityDetailPanel({ detail, onClose, playerGate, onCommand, onExpedition }) {
  const [executorIds, setExecutorIds] = useState([]);
  const [rewardIds, setRewardIds] = useState([]);
  const [recruitTargetId, setRecruitTargetId] = useState(0);
  const [expeditionIds, setExpeditionIds] = useState([]);
  const [landTypeId, setLandTypeId] = useState(0);
  const [expeditionTroops, setExpeditionTroops] = useState(3000);
  const [expeditionGold, setExpeditionGold] = useState(500);
  const [expeditionFood, setExpeditionFood] = useState(20000);

  useEffect(() => {
    setExecutorIds([]);
    setRewardIds([]);
    setRecruitTargetId(0);
    setExpeditionIds([]);
    setLandTypeId(0);
  }, [detail.id]);

  // M3.a 玩家门(原版城菜单门):玩家局只对本势力且本回合的城开放命令。
  const playerForceId = playerGate?.playerForceId ?? 0;
  const commandsAllowed = playerForceId === 0 || (detail.forceId === playerForceId && playerGate.awaitingPlayer);
  const commandsHint = playerForceId === 0
    ? null
    : (detail.forceId === playerForceId
        ? (playerGate.awaitingPlayer ? null : '当前非本势力回合,内政指令待玩家回合开放')
        : '玩家局只对本势力城市开放内政指令');

  const freePersons = detail.persons.filter((person) => person.free);
  const rewardTargets = detail.persons.filter(
    (person) => person.state >= 2 && person.state <= 4 && person.loyalty < 100
  );
  const executors = freePersons.filter((person) => executorIds.includes(person.id));
  const rewardPersons = rewardTargets.filter((person) => rewardIds.includes(person.id));
  const recruitTarget = detail.wildPersons.find((person) => person.id === recruitTargetId) ?? null;
  const expeditionPersons = freePersons.filter((person) => expeditionIds.includes(person.id));
  const landTypes = (detail.troopTypes ?? []).filter((type) => type.isLand);

  const toggle = (ids, setIds, personId, cap) => {
    if (ids.includes(personId)) {
      setIds(ids.filter((id) => id !== personId));
    } else if (ids.length < cap) {
      setIds([...ids, personId]);
    }
  };

  const send = (type, payload) => onCommand(type, payload);

  return (
    <>
      <div className="panel-title">
        <h2>{detail.name}</h2>
        <span>{detail.forceName || '无主'} · 行动力 {detail.actionPoint}</span>
        <button
          type="button"
          className="detail-close"
          title="关闭城市详情,回到地图"
          onClick={onClose}
        >
          ✕
        </button>
      </div>
      <div className="detail-scroll">
        <div className="stats-grid">
          <div><span>人口</span><strong>{formatNumber(detail.population)}</strong></div>
          <div><span>资金</span><strong>{formatNumber(detail.gold)}</strong></div>
          <div><span>军粮</span><strong>{formatNumber(detail.food)}</strong></div>
          <div><span>士气</span><strong>{detail.morale} / {detail.maxMorale}</strong></div>
          <div><span>兵力</span><strong>{formatNumber(detail.troops)} / {formatNumber(detail.troopsLimit)}</strong></div>
          <div><span>在城武将</span><strong>{detail.persons.length}({detail.freePersonCount} 待命)</strong></div>
        </div>

        <div className="command-bar">
          {commandsHint ? <span className="hint">{commandsHint}</span> : null}
          <button
            type="button"
            disabled={executors.length === 0 || !commandsAllowed}
            title="军事/训练:提升城市士气(消耗资金)"
            onClick={() => send('train', { cityId: detail.id, personIds: executors.map((p) => p.id) })}
          >
            训练({executors.length})
          </button>
          <button
            type="button"
            disabled={executors.length === 0 || !commandsAllowed}
            title="人事/探索人才:下回合结算,可能发现人才或资金"
            onClick={() => send('search', { cityId: detail.id, personIds: executors.map((p) => p.id) })}
          >
            探索({executors.length})
          </button>
          <button
            type="button"
            disabled={rewardPersons.length === 0 || !commandsAllowed}
            title="人事/褒赏:消耗资金提升忠诚(+10)"
            onClick={() => send('reward', { cityId: detail.id, personIds: rewardPersons.map((p) => p.id) })}
          >
            奖励({rewardPersons.length})
          </button>
          <button
            type="button"
            disabled={executors.length !== 1 || !recruitTarget || !commandsAllowed}
            title="人事/登庸武将:任命执行武将招揽在野人才"
            onClick={() => send('recruit', {
              cityId: detail.id,
              personIds: executors.map((p) => p.id),
              targetPersonId: recruitTarget?.id ?? 0
            })}
          >
            招揽
          </button>
          {recruitTarget
            ? <span className="hint">招揽目标:{recruitTarget.name}</span>
            : <span className="hint">招揽需选 1 名执行武将 + 1 名在野人才</span>}
        </div>

        <div className="person-section">
          <div className="panel-title">
            <h3>武将</h3>
            <span>勾「执」为执行武将(≤3),勾「赏」为奖励对象,勾「征」为出征编成(≤3)</span>
          </div>
          <table className="person-table">
            <thead>
              <tr>
                <th>执</th>
                <th>赏</th>
                <th>征</th>
                <th>姓名</th>
                <th>身份</th>
                <th>忠诚</th>
                <th>统率</th>
                <th>武力</th>
                <th>智力</th>
                <th>政治</th>
                <th>魅力</th>
              </tr>
            </thead>
            <tbody>
              {detail.persons.map((person) => (
                <tr key={person.id} className={person.free ? 'free' : ''}>
                  <td>
                    {person.free
                      ? (
                        <input
                          type="checkbox"
                          checked={executorIds.includes(person.id)}
                          onChange={() => toggle(executorIds, setExecutorIds, person.id, 3)}
                        />
                      )
                      : null}
                  </td>
                  <td>
                    {rewardTargets.some((target) => target.id === person.id)
                      ? (
                        <input
                          type="checkbox"
                          checked={rewardIds.includes(person.id)}
                          onChange={() => toggle(rewardIds, setRewardIds, person.id, 10)}
                        />
                      )
                      : null}
                  </td>
                  <td>
                    {person.free
                      ? (
                        <input
                          type="checkbox"
                          checked={expeditionIds.includes(person.id)}
                          onChange={() => toggle(expeditionIds, setExpeditionIds, person.id, 3)}
                        />
                      )
                      : null}
                  </td>
                  <td><strong>{person.name}</strong></td>
                  <td>{PERSON_STATE_NAMES[person.state] ?? person.state}{person.free ? '·待命' : ''}</td>
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
        </div>

        {detail.wildPersons.length > 0
          ? (
            <div className="person-section">
              <div className="panel-title">
                <h3>在野人才(招揽目标)</h3>
              </div>
              <div className="wild-list">
                {detail.wildPersons.map((person) => (
                  <button
                    key={person.id}
                    type="button"
                    className={person.id === recruitTargetId ? 'wild-row selected' : 'wild-row'}
                    onClick={() => setRecruitTargetId(person.id === recruitTargetId ? 0 : person.id)}
                  >
                    <strong>{person.name}</strong>
                    <small>统{person.command} 武{person.strength} 智{person.intelligence} 政{person.politics} 魅{person.glamour}</small>
                  </button>
                ))}
              </div>
            </div>
            )
          : null}

        <div className="person-section">
          <div className="panel-title">
            <h3>出征(军事/出征)</h3>
            <span>勾「征」选编成武将(≤3,首位为主将)</span>
          </div>
          <div className="expedition-form">
            <label>
              陆战兵种
              <select
                value={landTypeId}
                onChange={(event) => setLandTypeId(Number(event.target.value))}
              >
                {landTypes.map((type) => (
                  <option key={type.id} value={type.id}>{type.name}</option>
                ))}
              </select>
            </label>
            <label>
              兵力
              <input
                type="number"
                min={1}
                max={detail.troops}
                value={expeditionTroops}
                onChange={(event) => setExpeditionTroops(Math.max(0, Number(event.target.value)))}
              />
            </label>
            <label>
              携金
              <input
                type="number"
                min={0}
                max={detail.gold}
                value={expeditionGold}
                onChange={(event) => setExpeditionGold(Math.max(0, Number(event.target.value)))}
              />
            </label>
            <label>
              携粮
              <input
                type="number"
                min={0}
                max={detail.food}
                value={expeditionFood}
                onChange={(event) => setExpeditionFood(Math.max(0, Number(event.target.value)))}
              />
            </label>
            <button
              type="button"
              disabled={expeditionPersons.length === 0 || landTypes.length === 0 || !commandsAllowed}
              title="军事/出征:按城内可组兵种编成部队(消耗兵力/金钱/军粮与军团行动力)"
              onClick={() => onExpedition({
                cityId: detail.id,
                personIds: expeditionPersons.map((person) => person.id),
                landTroopTypeId: landTypeId || landTypes[0]?.id,
                troops: expeditionTroops,
                gold: expeditionGold,
                food: expeditionFood
              })}
            >
              出征({expeditionPersons.length})
            </button>
          </div>
        </div>

        <div className="person-section">
          <div className="panel-title">
            <h3>本城部队</h3>
            <span>{(detail.fieldTroops ?? []).length} 支</span>
          </div>
          {(detail.fieldTroops ?? []).length === 0
            ? <p className="empty">本城暂无在外部队</p>
            : (
              <table className="force-table">
                <thead>
                  <tr><th>部队</th><th>格坐标</th><th>兵力</th><th>携粮</th><th>状态</th></tr>
                </thead>
                <tbody>
                  {(detail.fieldTroops ?? []).map((troop) => (
                    <tr key={troop.id}>
                      <td><strong>{troop.name}</strong></td>
                      <td>({troop.x}, {troop.y})</td>
                      <td>{formatNumber(troop.troops)}</td>
                      <td>{formatNumber(troop.food)}</td>
                      <td>{troop.actionOver ? '已行动' : '待命'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              )}
        </div>
      </div>
    </>
  );
}

// 部队列表(M2.b 拆半):势力过滤 + 选中即弹右侧浮动卡片;M3.a 玩家局移动门在卡片侧生效。
function TroopListPanel({ troops, forces, forceFilter, onForceFilter, selectedTroopId, onSelect }) {
  const visibleTroops = forceFilter === 0
    ? troops
    : troops.filter((troop) => troop.forceId === forceFilter);
  return (
    <section className="drawer-panel city-list-panel">
      <div className="panel-title">
        <h2>部队</h2>
        <select value={forceFilter} onChange={(event) => onForceFilter(Number(event.target.value))}>
          <option value={0}>全部势力</option>
          {forces.map((force) => (
            <option key={force.id} value={force.id}>{force.name}</option>
          ))}
        </select>
      </div>
      <div className="city-rows">
        {visibleTroops.length === 0 ? <p className="empty">暂无部队(开局无人出征;城市详情可出征编成)</p> : null}
        {visibleTroops.map((troop) => (
          <button
            key={troop.id}
            type="button"
            className={troop.id === selectedTroopId ? 'city-row selected' : 'city-row'}
            onClick={() => onSelect(troop.id)}
          >
            <strong>{troop.name}</strong>
            <span>{troop.forceName || '无主'}</span>
            <small>
              ({troop.x}, {troop.y}) · 兵 {formatNumber(troop.troops)} · 粮 {formatNumber(troop.food)} · 士气 {troop.morale}
              {' '}{troop.actionOver ? '· 已行动' : ''}{troop.missionType > 0 ? ' · 委任中' : ''}
            </small>
          </button>
        ))}
      </div>
    </section>
  );
}

// 部队详情浮动卡片(M3.b map-first):目标格是内核格坐标(x=北、y=东,范围 0..255);
// 地图点击选格需要引擎侧 pick,接入前用坐标输入。M3.a:玩家局只对玩家势力且当前
// 回合的部队开放移动(原版部队命令门)。
function TroopDetailCard({ troops, troopId, onClose, playerGate, onMove }) {
  const [targetX, setTargetX] = useState(0);
  const [targetY, setTargetY] = useState(0);
  const [seeded, setSeeded] = useState(false);

  const selected = troops.find((troop) => troop.id === troopId) ?? null;

  useEffect(() => {
    if (selected && !seeded) {
      setTargetX(selected.x);
      setTargetY(selected.y);
      setSeeded(true);
    }
  }, [selected, seeded]);

  useEffect(() => {
    setSeeded(false);
  }, [troopId]);

  if (!selected) {
    return (
      <section className="detail-card panel detail empty">
        <button
          type="button"
          className="detail-close"
          title="关闭部队详情"
          onClick={onClose}
        >
          ✕
        </button>
        部队已消散(溃灭/回城),卡片可关闭
      </section>
    );
  }

  const playerForceId = playerGate?.playerForceId ?? 0;
  const moveAllowed = playerForceId === 0 ||
    (selected.forceId === playerForceId && playerGate.awaitingPlayer);

  return (
    <section className="detail-card panel detail">
      <div className="panel-title">
        <h2>{selected.name}</h2>
        <span>{selected.forceName} · {selected.landTroopTypeName} · 移动力 {selected.moveAbility}</span>
        <button
          type="button"
          className="detail-close"
          title="关闭部队详情,回到地图"
          onClick={onClose}
        >
          ✕
        </button>
      </div>
      <div className="detail-scroll">
        <div className="stats-grid">
          <div><span>兵力</span><strong>{formatNumber(selected.troops)}</strong></div>
          <div><span>携粮</span><strong>{formatNumber(selected.food)}</strong></div>
          <div><span>士气</span><strong>{selected.morale}</strong></div>
          <div><span>格坐标</span><strong>({selected.x}, {selected.y})</strong></div>
          <div><span>军团</span><strong>#{selected.corpsId}</strong></div>
          <div><span>状态</span><strong>{selected.actionOver ? '已行动' : '待命'}</strong></div>
        </div>
        <div className="mission-line">
          {selected.missionType > 0
            ? (
              <span>
                <strong>任务:</strong>{selected.missionLabel || `#${selected.missionType}`}
                <small>(机会主义执行:占城任务沿途将顺势打击敌据点)</small>
              </span>
            )
            : <span className="hint">无任务(自由行动;无任务部队下回合可能被回城吸收)</span>}
        </div>
        <div className="expedition-form">
          <label>
            目标格 x(北)
            <input
              type="number"
              min={0}
              max={255}
              value={targetX}
              onChange={(event) => setTargetX(Math.max(0, Number(event.target.value)))}
            />
          </label>
          <label>
            目标格 y(东)
            <input
              type="number"
              min={0}
              max={255}
              value={targetY}
              onChange={(event) => setTargetY(Math.max(0, Number(event.target.value)))}
            />
          </label>
          <button
            type="button"
            disabled={selected.actionOver || !moveAllowed}
            title="选中部队移动到目标格(范围内即时落地;范围外可驻空格转为多回合委任移动,逐回合自动推进;玩家局限本势力回合)"
            onClick={() => onMove({ troopId: selected.id, x: targetX, y: targetY })}
          >
            移动
          </button>
          <p className="hint expedition-hint">
            范围外可驻空格 = 委任移动:授多回合行军任务,每回合按移动力推进,抵达后待命;
            途中目标格被敌占则就近平驻等待。占位格(城/建筑/敌据点)仍按越程拒绝。
          </p>
        </div>
      </div>
    </section>
  );
}

// 战报面板(M2.d):SangoCombatAnnals 结构化 per-battle 卡(参战方/技能/伤害序列/结果/
// 城池归属变化),按回合过滤在 UI 侧完成(卡自带回合区间)。M3.b 入住左侧抽屉。
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
    <section className="drawer-panel battle-panel">
      <div className="panel-title">
        <h2>战报</h2>
        <span>
          <select value={turnFilter} onChange={(event) => setTurnFilter(Number(event.target.value))}>
            <option value={0}>全部回合({battles.length} 战)</option>
            {turns.map((turn) => (
              <option key={turn} value={turn}>第 {turn} 回合</option>
            ))}
          </select>
        </span>
      </div>
      <div className="battle-list">
        {cards.length === 0 ? <p className="empty">尚无交战记录(播种部队并推进回合后,战斗在此聚合呈现)</p> : null}
        {cards.map((battle) => <BattleCard key={battle.seq} battle={battle} />)}
      </div>
    </section>
  );
}

function BattleCard({ battle }) {
  return (
    <article className={`battle-card result-${battle.result}`}>
      <header>
        <div className="battle-title">
          <span className="side">
            <strong>{battle.attacker.name}</strong>
            <small>{battle.attacker.forceName} · {PARTICIPANT_KIND_LABELS[battle.attacker.kind] ?? ''}</small>
          </span>
          <span className="vs">对战</span>
          <span className="side">
            <strong>{battle.defender.name}</strong>
            <small>{battle.defender.forceName || '无主'} · {PARTICIPANT_KIND_LABELS[battle.defender.kind] ?? ''}</small>
          </span>
        </div>
        <div className="battle-meta">
          <span className={`result-badge ${battle.result}`}>{BATTLE_RESULT_LABELS[battle.result] ?? battle.result}</span>
          <span>第 {battle.turnStart}{battle.turnLast > battle.turnStart ? `–${battle.turnLast}` : ''} 回合</span>
          <span>总杀伤 {formatNumber(battle.damageDealt)}</span>
        </div>
      </header>
      {(battle.skills ?? []).length > 0
        ? (
          <div className="skill-chips">
            {(battle.skills ?? []).map((skill) => <span key={skill} className="chip">{skill}</span>)}
          </div>
        )
        : null}
      {(battle.troopChanges ?? []).length > 0
        ? (
          <div className="troop-changes">
            {(battle.troopChanges ?? []).map((change, index) => (
              <span key={`${change.name}-${index}`} className={change.end < change.start ? 'loss' : ''}>
                {change.name}({PARTICIPANT_KIND_LABELS[change.kind] ?? ''}) {formatNumber(change.start)} → {formatNumber(change.end)}
              </span>
            ))}
          </div>
        )
        : null}
      {battle.city
        ? (
          <div className="city-change">
            城池易主:{battle.city.name} · {battle.city.oldForce} → <strong>{battle.city.newForce}</strong>
          </div>
        )
        : null}
      <div className="event-rows">
        {(battle.events ?? []).map((event, index) => (
          <div key={index} className="event-row">
            <span className="turn">T{event.turn}</span>
            <span className="kind">{BATTLE_EVENT_LABELS[event.kind] ?? event.kind}</span>
            <span className="actors">{event.attacker} → {event.defender}</span>
            {event.skill ? <span className="chip">{event.skill}</span> : null}
            {event.damage > 0 ? <span className="damage">伤 {formatNumber(event.damage)}</span> : null}
            {event.targetTroopsAfter > 0 ? <span className="after">余 {formatNumber(event.targetTroopsAfter)}</span> : null}
          </div>
        ))}
      </div>
    </article>
  );
}

// 外交面板(M3.d):势力关系表(真源 RelationMap)+ 同盟列表 + 使者派遣表单。
// 可下达动作照原版活跃面:送礼(1000 金,关系必升)/ 结盟(金额滑条,成功率=基础+
// 关系+使者+金额)/ 摒弃同盟;宣战/停战/通商/和亲等在原版是空桩,不在此发明。
// 原版门槛:城 freePersons>0 && 军团行动力≥30 && 城金≥1000(不扣 AP,只扣金)。
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

  // 使者候选:派遣城的待命武将(单城详情话题按 cityId 订阅,与城市卡同一真源)。
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
  const gatesOk = Boolean(
    dispatchCity && freePersons.length > 0 && dispatchCity.gold >= 1000
  );
  const commandsAllowed = playerForceId === 0 || playerGate.awaitingPlayer;
  const canOrder = gatesOk && commandsAllowed && diplomatId > 0 && targetForceId > 0;

  const relationRows = [...diplomacy.relations].sort((a, b) => b.relation - a.relation);

  const send = () => {
    if (!canOrder) {
      return;
    }

    onCommand(actionType, {
      cityId: dispatchCityId,
      personIds: [diplomatId],
      targetForceId,
      resourceValue: actionType === 'alliance' ? allianceGold : 0
    });
  };

  return (
    <section className="drawer-panel diplomacy-panel">
      <div className="panel-title">
        <h2>外交</h2>
        <span>
          {playerForceId > 0
            ? `${diplomacy.playerForceName || `#${playerForceId}`} 的邦交面 · 关系随月势演化`
            : '全图邦交观察(无玩家局)'}
        </span>
      </div>

      <div className="diplomacy-section">
        <div className="panel-title">
          <h3>势力关系</h3>
          <span>{relationRows.length} 家</span>
        </div>
        <table className="force-table">
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
        <p className="hint">点选势力作为外交对象;关系门槛:结盟需 ≥2000,月度自然衰减中同盟缓升。</p>
      </div>

      {diplomacy.alliances.length > 0
        ? (
          <div className="diplomacy-section">
            <div className="panel-title">
              <h3>同盟与协议</h3>
              <span>{diplomacy.alliances.length} 项</span>
            </div>
            <ul className="alliance-list">
              {diplomacy.alliances.map((alliance) => (
                <li key={alliance.id}>
                  <strong>{ALLIANCE_TYPE_LABELS[alliance.type] ?? alliance.type}</strong>
                  <span>{alliance.forceNames.join(' · ')}</span>
                  <small>余 {alliance.leftCount} 旬</small>
                </li>
              ))}
            </ul>
          </div>
        )
        : null}

      <div className="diplomacy-section">
        <div className="panel-title">
          <h3>派遣使者</h3>
          <span>原版活跃面:送礼 / 结盟 / 摒弃同盟</span>
        </div>
        <div className="expedition-form diplomacy-form">
          <label>
            派出城市
            <select
              value={dispatchCityId}
              onChange={(event) => setDispatchCityId(Number(event.target.value))}
            >
              {playerCities.map((city) => (
                <option key={city.id} value={city.id}>{city.name}(金 {formatNumber(city.gold)})</option>
              ))}
            </select>
          </label>
          <label>
            使者(待命武将)
            <select
              value={diplomatId}
              onChange={(event) => setDiplomatId(Number(event.target.value))}
            >
              <option value={0}>选择使者</option>
              {freePersons.map((person) => (
                <option key={person.id} value={person.id}>
                  {person.name}(政 {person.politics} 魅 {person.glamour})
                </option>
              ))}
            </select>
          </label>
          <label>
            行动
            <select value={actionType} onChange={(event) => setActionType(event.target.value)}>
              <option value="sendGift">送礼(1000 金,关系必升)</option>
              <option value="alliance">结盟(按关系与使者定成败)</option>
              <option value="discardAlliance">摒弃同盟(即时生效)</option>
            </select>
          </label>
          {actionType === 'alliance'
            ? (
              <label>
                结盟金
                <input
                  type="number"
                  min={0}
                  step={500}
                  value={allianceGold}
                  onChange={(event) => setAllianceGold(Math.max(0, Number(event.target.value)))}
                />
              </label>
            )
            : null}
          <button type="button" disabled={!canOrder} onClick={send}>
            派遣
          </button>
          <p className="hint expedition-hint">
            {playerForceId === 0
              ? '全托管局无玩家势力,外交为观察面。'
              : !commandsAllowed
                ? '外交与城内政同门:待玩家回合方可下令。'
                : !gatesOk
                  ? '门槛:城有待命武将且城金 ≥1000(行动力 ≥30)。'
                  : '使者按路程逐日赶赴对方君主城;送达后结果行进消息流(送礼必成,结盟按成功率)。'}
          </p>
        </div>
      </div>
    </section>
  );
}

function ForceOverview({ forces }) {
  return (
    <section className="drawer-panel force-overview">
      <div className="panel-title"><h2>势力概览</h2><span>{forces.length} 家</span></div>
      <table className="force-table">
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

function formatNumber(value) {
  return Number(value ?? 0).toLocaleString('zh-CN');
}

createRoot(document.getElementById('root')).render(<App />);
