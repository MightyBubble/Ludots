const SCHEMA_VERSION = 1;
const CONTROL_CHANNEL = 'ludots.dataplane.control';
const SESSION_ID = 'dataschema-web-skin';
const routeParams = new URLSearchParams(window.location.search);
const dataPlaneTopic = routeParams.get('topic') || 'ludots.panel.panel.mixed.schema.workbench';

const refs = {
  panel: document.getElementById('workbench-panel'),
  nameText: document.getElementById('name-text'),
  scoreText: document.getElementById('score-text'),
  posText: document.getElementById('pos-text'),
  rarityText: document.getElementById('rarity-text'),
  tagsText: document.getElementById('tags-text'),
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

function formatTags(value) {
  if (Array.isArray(value)) {
    return value.join(', ');
  }
  if (value == null) {
    return '--';
  }
  return String(value);
}

function renderSnapshot(payload) {
  if (!payload || payload.ready !== true) {
    refs.panel.dataset.ready = 'false';
    return;
  }

  refs.panel.dataset.ready = 'true';
  refs.nameText.textContent = payload.name != null ? String(payload.name) : '--';
  refs.scoreText.textContent = payload.score != null ? String(payload.score) : '--';
  const x = payload.x != null ? payload.x : '--';
  const y = payload.y != null ? payload.y : '--';
  refs.posText.textContent = `${x}, ${y}`;
  refs.rarityText.textContent = payload.rarity != null ? String(payload.rarity) : '--';
  refs.tagsText.textContent = formatTags(payload.tags);
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
        app: 'dataschema-web-skin',
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
