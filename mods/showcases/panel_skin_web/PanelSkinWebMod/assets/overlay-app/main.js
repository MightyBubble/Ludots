const SCHEMA_VERSION = 1;
const CONTROL_CHANNEL = 'ludots.dataplane.control';
const SESSION_ID = 'fireball-web-skin';
const routeParams = new URLSearchParams(
  (window.location.search && window.location.search.length > 1)
    ? window.location.search
    : (window.__LUDOTS_NAV_QUERY__ || '')
);
const dataPlaneTopic = routeParams.get('topic') || 'ludots.showcase.fireball.status';

const refs = {
  panel: document.getElementById('fireball-panel'),
  hpFill: document.getElementById('hp-fill'),
  hpText: document.getElementById('hp-text'),
  mpFill: document.getElementById('mp-fill'),
  mpText: document.getElementById('mp-text'),
  atkText: document.getElementById('atk-text'),
  linkStatus: document.getElementById('link-status')
};

let requestSequence = 0;

function setLinkStatus(text) {
  if (refs.linkStatus) {
    refs.linkStatus.textContent = `link: ${text}`;
  }
}

function parseJsonOrNull(text) {
  try {
    return JSON.parse(text);
  } catch (_) {
    return null;
  }
}

function postHostMessage(envelope) {
  if (window.ludotsDataplane && typeof window.ludotsDataplane.postMessage === 'function') {
    window.ludotsDataplane.postMessage(envelope);
    return true;
  }
  return false;
}

function postDataPlaneEnvelope(kind, topic, payload) {
  const envelope = {
    schemaVersion: SCHEMA_VERSION,
    sessionId: SESSION_ID,
    requestId: ++requestSequence,
    kind,
    topic,
    payload
  };
  return postHostMessage(envelope);
}

function normalizeIncoming(rawMessage) {
  const data = rawMessage && typeof rawMessage === 'object' && 'data' in rawMessage
    ? rawMessage.data
    : rawMessage;
  if (!data) {
    return null;
  }

  let envelope = data;
  if (typeof data === 'string') {
    envelope = parseJsonOrNull(data);
  } else if (data.channel === CONTROL_CHANNEL) {
    envelope = typeof data.payload === 'string' ? parseJsonOrNull(data.payload) : data.payload;
  }

  if (!envelope || typeof envelope !== 'object' || envelope.schemaVersion !== SCHEMA_VERSION) {
    return null;
  }

  if (envelope.payload && typeof envelope.payload === 'object' && envelope.payload.schemaVersion === SCHEMA_VERSION) {
    envelope = envelope.payload;
  }

  return envelope;
}

function topicPayload(envelope) {
  const payload = envelope.payload;
  if (typeof payload === 'string') {
    return parseJsonOrNull(payload);
  }
  return payload && typeof payload === 'object' ? payload : null;
}

function renderSnapshot(payload) {
  if (!payload || payload.ready !== true) {
    refs.panel.dataset.ready = 'false';
    return;
  }

  refs.panel.dataset.ready = 'true';
  const healthBase = Math.max(1, Number(payload.healthBase) || 1);
  const manaBase = Math.max(1, Number(payload.manaBase) || 1);
  const health = Number(payload.health) || 0;
  const mana = Number(payload.mana) || 0;

  refs.hpFill.style.width = `${Math.max(0, Math.min(100, (health / healthBase) * 100))}%`;
  refs.mpFill.style.width = `${Math.max(0, Math.min(100, (mana / manaBase) * 100))}%`;
  refs.hpText.textContent = `${Math.round(health)} / ${Math.round(healthBase)}`;
  refs.mpText.textContent = `${Math.round(mana)} / ${Math.round(manaBase)}`;
  refs.atkText.textContent = String(Math.round(Number(payload.attack) || 0));
}

function handleMessage(event) {
  const envelope = normalizeIncoming(event);
  if (!envelope || envelope.topic !== dataPlaneTopic) {
    return;
  }
  const kind = String(envelope.kind || '').toLowerCase();
  if (kind === 'snapshot' || kind === 'delta') {
    renderSnapshot(topicPayload(envelope));
    setLinkStatus('live');
  }
}

function connect() {
  window.addEventListener('message', handleMessage);

  let attempts = 0;
  const tryConnect = () => {
    let posted = false;
    try {
      posted = postDataPlaneEnvelope('handshake', 'system', {
        app: 'fireball-web-skin',
        capabilities: ['handshake', 'subscribe']
      });
      if (posted) {
        postDataPlaneEnvelope('subscribe', dataPlaneTopic, { snapshot: true });
        setLinkStatus('subscribed');
      }
    } catch (_) {
      posted = false;
    }

    if (!posted && ++attempts < 100) {
      window.setTimeout(tryConnect, 100);
    } else if (!posted) {
      setLinkStatus('facade missing');
    }
  };

  tryConnect();
}

connect();
