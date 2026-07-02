const timeline = document.getElementById('timeline');
const status = document.getElementById('status');
const bridge = document.getElementById('bridge');
const refresh = document.getElementById('refresh');
const toggle = document.getElementById('toggle');

let active = false;
const entries = [
  'Mod bundle loaded',
  'Browser runtime handshake',
  'Canvas embedding path ready',
  'Message bridge waiting'
];

function render() {
  timeline.innerHTML = entries.map((entry, index) => `<div class="panel">${index + 1}. ${entry}</div>`).join('');
  timeline.classList.toggle('active', active);
  status.textContent = active ? 'Highlight state enabled.' : 'Idle host state.';
}

function postHostMessage(payload, attempt = 0) {
  const host = window.ludotsBrowser;
  if (host && typeof host.postMessage === 'function') {
    host.postMessage({
      source: 'browser-ui-showcase',
      payload
    });
    return;
  }

  if (attempt < 20) {
    window.setTimeout(() => postHostMessage(payload, attempt + 1), 25);
    return;
  }

  bridge.textContent = 'Ludots browser host bridge is not exposed yet.';
}

refresh.addEventListener('click', () => {
  bridge.textContent = 'Host requested refresh at ' + new Date().toLocaleTimeString();
  postHostMessage('refresh');
  render();
});

toggle.addEventListener('click', () => {
  active = !active;
  bridge.textContent = active ? 'Highlight enabled.' : 'Highlight disabled.';
  postHostMessage(active ? 'highlight:on' : 'highlight:off');
  render();
});

window.addEventListener('message', event => {
  if (typeof event.data === 'string') {
    bridge.textContent = event.data;
    return;
  }

  bridge.textContent = event.data && event.data.payload ? event.data.payload : JSON.stringify(event.data);
});

render();
postHostMessage('loaded');
