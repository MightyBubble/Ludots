const SCHEMA_VERSION = 1;
const CONFIRM_COMMAND = 'activity.confirm';
const TRIGGER_COMMAND = 'activity.showcase.trigger';
const SET_ATTRIBUTE_COMMAND = 'activity.showcase.setAttribute';

const routeParams = new URLSearchParams(
  (window.location.search && window.location.search.length > 1)
    ? window.location.search
    : (window.__LUDOTS_NAV_QUERY__ || '')
);
const panelId = routeParams.get('panelId') || 'panel.activity.events';
const dataPlaneTopic = routeParams.get('topic') || 'wpk.activity.dispatch';
const dataPlaneSessionId = `activity-dispatch-${Date.now().toString(16)}`;

let requestSequence = 0;
let commandSequence = 0;
let connected = false;
let lastError = '';
let knobHigh = true;

const refs = {
  status: document.getElementById('status'),
  activities: document.getElementById('activities'),
  history: document.getElementById('history'),
  cues: document.getElementById('cues')
};

function postHostMessage(payload) {
  if (window.ludotsDataplane && typeof window.ludotsDataplane.postMessage === 'function') {
    window.ludotsDataplane.postMessage(payload);
    return true;
  }
  if (window.ludotsBrowser && typeof window.ludotsBrowser.postMessage === 'function') {
    window.ludotsBrowser.postMessage(payload);
    return true;
  }
  return false;
}

function postDataPlaneEnvelope(kind, topic, payload) {
  const envelope = {
    schemaVersion: SCHEMA_VERSION,
    sessionId: dataPlaneSessionId,
    requestId: ++requestSequence,
    kind,
    topic,
    payload
  };
  if (!postHostMessage(envelope)) {
    throw new Error('Ludots DataPlane host bridge is not available.');
  }
}

function setStatus(text) {
  refs.status.textContent = text;
}

function connect() {
  try {
    postDataPlaneEnvelope('handshake', 'system', {
      app: 'activity-dispatch-showcase',
      panelId,
      requiredCapabilities: ['message', 'control', 'reliable-ordered']
    });
    postDataPlaneEnvelope('subscribe', dataPlaneTopic, { panelId, snapshot: true });
    connected = true;
    setStatus('DataPlane：已连接，等待快照');
  } catch (error) {
    connected = false;
    setStatus(`DataPlane：等待宿主（${error instanceof Error ? error.message : String(error)}）`);
    window.setTimeout(connect, 500);
  }
}

function sendCommand(name, payload) {
  if (!connected) {
    setStatus('DataPlane：未连接，命令未发送');
    return;
  }
  try {
    postDataPlaneEnvelope('command', dataPlaneTopic, {
      name,
      clientSeq: ++commandSequence,
      entityRefs: [],
      payload
    });
  } catch (error) {
    setStatus(`命令发送失败：${error instanceof Error ? error.message : String(error)}`);
  }
}

function parseEnvelope(event) {
  const data = event.data;
  if (typeof data === 'string') {
    try { return JSON.parse(data); } catch { return null; }
  }
  return data && typeof data === 'object' ? data : null;
}

function handleHostMessage(event) {
  const envelope = parseEnvelope(event);
  if (!envelope || envelope.sessionId && envelope.sessionId !== dataPlaneSessionId) {
    return;
  }
  if (envelope.kind === 'error') {
    lastError = envelope.payload && envelope.payload.message ? envelope.payload.message : JSON.stringify(envelope.payload);
    setStatus(`宿主返回错误：${lastError}`);
    return;
  }
  if (envelope.kind !== 'snapshot' && envelope.kind !== 'data') {
    return;
  }
  if (envelope.topic !== dataPlaneTopic) {
    return;
  }
  const payload = typeof envelope.payload === 'string' ? JSON.parse(envelope.payload) : envelope.payload;
  if (!payload) {
    return;
  }
  render(payload);
  setStatus('DataPlane：实时同步中');
}

function render(snapshot) {
  renderActivities(snapshot.activities || []);
  renderHistory(snapshot.history || []);
  renderCues(snapshot.cues || []);
}

function renderActivities(activities) {
  refs.activities.textContent = '';
  if (activities.length === 0) {
    const empty = document.createElement('p');
    empty.className = 'empty';
    empty.textContent = '当前没有等待拍板的活动。按上方任意一条触发轨道开始。';
    refs.activities.appendChild(empty);
    return;
  }
  for (const activity of activities) {
    refs.activities.appendChild(renderActivityCard(activity));
  }
}

function renderActivityCard(activity) {
  const card = document.createElement('article');
  card.className = 'activity-card';
  const head = document.createElement('header');
  const title = document.createElement('h3');
  title.textContent = `${activity.displayName} · #${activity.instanceId}`;
  const meta = document.createElement('span');
  meta.className = 'badge';
  meta.textContent = `派发 ${activity.dispatchPolicy.toLowerCase()}`;
  head.appendChild(title);
  head.appendChild(meta);
  card.appendChild(head);

  const summary = document.createElement('p');
  summary.className = 'summary';
  summary.textContent = activity.summary;
  card.appendChild(summary);

  const list = document.createElement('ul');
  list.className = 'options';
  for (const option of activity.options || []) {
    list.appendChild(renderOption(activity, option));
  }
  const hiddenNote = document.createElement('li');
  hiddenNote.className = 'option-hidden-note';
  hiddenNote.textContent = '※ 「向盟友求援」不在列表里：它的 Gate（显示条件）未通过，选项被整个隐藏——这是设计，不是 bug。';
  list.appendChild(hiddenNote);
  card.appendChild(list);
  return card;
}

function renderOption(activity, option) {
  const item = document.createElement('li');
  item.className = option.executable ? 'option executable' : 'option blocked';
  const label = document.createElement('div');
  label.className = 'option-title';
  label.textContent = option.isBaseline ? `${option.title}〔基础选项〕` : option.title;
  const body = document.createElement('div');
  body.className = 'option-body';
  body.textContent = option.body;
  item.appendChild(label);
  item.appendChild(body);

  if (!option.executable) {
    const reason = document.createElement('div');
    reason.className = 'option-reason';
    reason.textContent = `不可执行 · ${option.blockReason}`;
    item.appendChild(reason);
  }

  const button = document.createElement('button');
  button.className = 'confirm';
  button.disabled = !option.executable;
  button.textContent = option.executable ? '确认' : '无法选择';
  button.addEventListener('click', () => {
    sendCommand(CONFIRM_COMMAND, {
      instanceId: activity.instanceId,
      optionId: option.optionId
    });
  });
  item.appendChild(button);
  return item;
}

function renderHistory(history) {
  refs.history.textContent = '';
  if (history.length === 0) {
    const empty = document.createElement('p');
    empty.className = 'empty';
    empty.textContent = '尚无已结算活动。';
    refs.history.appendChild(empty);
    return;
  }
  for (let i = history.length - 1; i >= 0; i--) {
    const row = history[i];
    const item = document.createElement('div');
    item.className = 'history-row';
    const text = document.createElement('span');
    text.textContent = `#${row.instanceId} ${row.displayName} → ${row.automatic ? '自动结算' : `已选「${row.selectedOptionId}」`}`;
    item.appendChild(text);
    refs.history.appendChild(item);
  }
}

function renderCues(cues) {
  refs.cues.textContent = '';
  if (cues.length === 0) {
    const empty = document.createElement('p');
    empty.className = 'empty';
    empty.textContent = '本帧无派发/准入反馈。';
    refs.cues.appendChild(empty);
    return;
  }
  for (const cue of cues) {
    const item = document.createElement('div');
    item.className = cue.kind === 'AdmissionRejected' ? 'cue cue-rejected' : 'cue';
    const text = `${cue.kind} · ${cue.activityId} #${cue.instanceId}${cue.optionId ? ' · ' + cue.optionId : ''}${cue.reason ? ' · ' + cue.reason : ''}`;
    item.textContent = text;
    refs.cues.appendChild(item);
  }
}

document.getElementById('trigger-forced').addEventListener('click', () => {
  sendCommand(TRIGGER_COMMAND, { eventKey: 'ActivityShowcase.Forced' });
});
document.getElementById('trigger-pooled').addEventListener('click', () => {
  sendCommand(TRIGGER_COMMAND, { eventKey: 'ActivityShowcase.Pooled' });
});
document.getElementById('trigger-automatic').addEventListener('click', () => {
  sendCommand(TRIGGER_COMMAND, { eventKey: 'ActivityShowcase.Automatic' });
});
document.getElementById('knob-attribute').addEventListener('click', () => {
  knobHigh = !knobHigh;
  sendCommand(SET_ATTRIBUTE_COMMAND, { attributeKey: 'Health', value: knobHigh ? 60 : 20 });
});

window.addEventListener('message', handleHostMessage);
connect();
