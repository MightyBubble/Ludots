import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  SANGO_CITIES_TOPIC,
  SANGO_CITY_TOPIC,
  SANGO_FORCES_TOPIC,
  SANGO_MESSAGES_TOPIC,
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
const EMPTY_TURN = { turnCount: 0, year: 0, month: 0, day: 0, dateText: '----', summary: '' };
const EMPTY_MESSAGES = { tick: 0, turnCount: 0, messages: [] };
const EMPTY_DETAIL = null;

function App() {
  const { clientRef, data, connection, command } = useSangoSession();
  const [activeTab, setActiveTab] = useState('cities');
  const [forceFilter, setForceFilter] = useState(0);
  const [selectedCityId, setSelectedCityId] = useState(0);
  const [commandCount, setCommandCount] = useState(0);
  const [lastOrder, setLastOrder] = useState({ text: '尚无指令', tone: 'idle' });

  const detailRevision = `${data.turn.turnCount}:${commandCount}`;
  const detail = useCityDetail(clientRef, selectedCityId, detailRevision);

  const visibleCities = useMemo(() => {
    if (forceFilter === 0) {
      return data.cities.cities;
    }

    return data.cities.cities.filter((city) => city.forceId === forceFilter);
  }, [data.cities.cities, forceFilter]);

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

  const endTurn = useCallback(async () => {
    setLastOrder({ text: '结束回合:等待时钟推进……', tone: 'pending' });
    const result = await command('sango.endTurn', {});
    setLastOrder(result.ok
      ? { text: '结束回合:已请求时钟推进,回合结算后刷新', tone: 'ok' }
      : { text: `结束回合失败:${result.message}`, tone: 'error' });
  }, [command]);

  return (
    <main className="app">
      <TopBar turn={data.turn} connection={connection} onEndTurn={endTurn} />
      <div className="app-body">
        <MessageStream messages={data.messages.messages} />
        <section className="main-area">
          <nav className="tab-bar">
            <button
              type="button"
              className={activeTab === 'cities' ? 'tab active' : 'tab'}
              onClick={() => setActiveTab('cities')}
            >
              城市
            </button>
            <button
              type="button"
              className={activeTab === 'forces' ? 'tab active' : 'tab'}
              onClick={() => setActiveTab('forces')}
            >
              势力
            </button>
          </nav>
          {activeTab === 'forces'
            ? <ForceOverview forces={data.forces.forces} />
            : (
              <div className="city-layout">
                <CityListPanel
                  cities={visibleCities}
                  forces={data.forces.forces}
                  forceFilter={forceFilter}
                  onForceFilter={setForceFilter}
                  selectedCityId={selectedCityId}
                  onSelect={setSelectedCityId}
                />
                {detail
                  ? <CityDetailPanel detail={detail} onCommand={issueCommand} />
                  : <section className="panel detail empty">点击左侧城市查看详情与内政指令</section>}
              </div>
            )}
        </section>
      </div>
      <footer className="status-bar">
        <span className={lastOrder.tone}>{lastOrder.text}</span>
        <span>{connection.phase} · {connection.transport} · 城市 {data.cities.cities.length} · 势力 {data.forces.forces.length}</span>
      </footer>
    </main>
  );
}

function useSangoSession() {
  const clientRef = useRef(null);
  const [data, setData] = useState({
    cities: EMPTY_CITIES,
    forces: EMPTY_FORCES,
    turn: EMPTY_TURN,
    messages: EMPTY_MESSAGES
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
      [SANGO_MESSAGES_TOPIC, (messages) => setData((current) => ({ ...current, messages }))]
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

function TopBar({ turn, connection, onEndTurn }) {
  return (
    <header className="top-bar">
      <h1>三国 · 内政</h1>
      <div className="date-block">
        <strong>{turn.dateText}</strong>
        <span>第 {turn.turnCount} 回合</span>
      </div>
      <button type="button" className="end-turn" onClick={onEndTurn}>结束回合</button>
      {connection.error ? <span className="error-line">{connection.error}</span> : null}
    </header>
  );
}

function MessageStream({ messages }) {
  const rows = [...messages].reverse();
  return (
    <aside className="panel message-stream">
      <div className="panel-title"><h2>消息</h2><span>{rows.length} 条</span></div>
      <ul className="message-list">
        {rows.length === 0 ? <li className="empty">等待回合摘要……</li> : null}
        {rows.map((message) => (
          <li key={message.seq}>
            {message.date ? <small>{message.date}</small> : null}
            <span>{message.text}</span>
          </li>
        ))}
      </ul>
    </aside>
  );
}

function CityListPanel({ cities, forces, forceFilter, onForceFilter, selectedCityId, onSelect }) {
  return (
    <section className="panel city-list-panel">
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

function CityDetailPanel({ detail, onCommand }) {
  const [executorIds, setExecutorIds] = useState([]);
  const [rewardIds, setRewardIds] = useState([]);
  const [recruitTargetId, setRecruitTargetId] = useState(0);

  useEffect(() => {
    setExecutorIds([]);
    setRewardIds([]);
    setRecruitTargetId(0);
  }, [detail.id]);

  const freePersons = detail.persons.filter((person) => person.free);
  const rewardTargets = detail.persons.filter(
    (person) => person.state >= 2 && person.state <= 4 && person.loyalty < 100
  );
  const executors = freePersons.filter((person) => executorIds.includes(person.id));
  const rewardPersons = rewardTargets.filter((person) => rewardIds.includes(person.id));
  const recruitTarget = detail.wildPersons.find((person) => person.id === recruitTargetId) ?? null;

  const toggle = (ids, setIds, personId, cap) => {
    if (ids.includes(personId)) {
      setIds(ids.filter((id) => id !== personId));
    } else if (ids.length < cap) {
      setIds([...ids, personId]);
    }
  };

  const send = (type, payload) => onCommand(type, payload);

  return (
    <section className="panel detail">
      <div className="panel-title">
        <h2>{detail.name}</h2>
        <span>{detail.forceName || '无主'} · 行动力 {detail.actionPoint}</span>
      </div>
      <div className="stats-grid">
        <div><span>人口</span><strong>{formatNumber(detail.population)}</strong></div>
        <div><span>资金</span><strong>{formatNumber(detail.gold)}</strong></div>
        <div><span>军粮</span><strong>{formatNumber(detail.food)}</strong></div>
        <div><span>士气</span><strong>{detail.morale} / {detail.maxMorale}</strong></div>
        <div><span>兵力</span><strong>{formatNumber(detail.troops)} / {formatNumber(detail.troopsLimit)}</strong></div>
        <div><span>在城武将</span><strong>{detail.persons.length}({detail.freePersonCount} 待命)</strong></div>
      </div>

      <div className="command-bar">
        <button
          type="button"
          disabled={executors.length === 0}
          title="军事/训练:提升城市士气(消耗资金)"
          onClick={() => send('train', { cityId: detail.id, personIds: executors.map((p) => p.id) })}
        >
          训练({executors.length})
        </button>
        <button
          type="button"
          disabled={executors.length === 0}
          title="人事/探索人才:下回合结算,可能发现人才或资金"
          onClick={() => send('search', { cityId: detail.id, personIds: executors.map((p) => p.id) })}
        >
          探索({executors.length})
        </button>
        <button
          type="button"
          disabled={rewardPersons.length === 0}
          title="人事/褒赏:消耗资金提升忠诚(+10)"
          onClick={() => send('reward', { cityId: detail.id, personIds: rewardPersons.map((p) => p.id) })}
        >
          奖励({rewardPersons.length})
        </button>
        <button
          type="button"
          disabled={executors.length !== 1 || !recruitTarget}
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
          <span>勾「执」为执行武将(≤3),勾「赏」为奖励对象</span>
        </div>
        <table className="person-table">
          <thead>
            <tr>
              <th>执</th>
              <th>赏</th>
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
    </section>
  );
}

function ForceOverview({ forces }) {
  return (
    <section className="panel force-overview">
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
