import type { InputEncoder } from '../input/InputEncoder';

export class ControlPanel {
  private readonly _encoder: InputEncoder;
  private _el: HTMLDivElement;

  constructor(encoder: InputEncoder) {
    this._encoder = encoder;
    this._el = document.createElement('div');
    this._el.id = 'control-panel';
    Object.assign(this._el.style, {
      position: 'fixed', bottom: '12px', left: '12px',
      background: 'rgba(0,0,0,0.75)', color: '#ddd',
      padding: '10px 14px', borderRadius: '6px',
      font: '13px sans-serif', zIndex: '20',
      display: 'flex', flexDirection: 'column', gap: '6px',
    });
    document.body.appendChild(this._el);
    this._buildUI();
  }

  private _buildUI(): void {
    const rows: [string, string][] = [
      ['G: +Agents', 'KeyG'],
      ['H: -Agents', 'KeyH'],
      ['J: Toggle Flow', 'KeyJ'],
      ['K: Flow Mode', 'KeyK'],
      ['L: Flow Debug', 'KeyL'],
      ['Y: Reset', 'KeyY'],
      ['U: +Iters', 'KeyU'],
    ];

    for (const [label, code] of rows) {
      const btn = document.createElement('button');
      btn.textContent = label;
      Object.assign(btn.style, {
        background: '#333', color: '#eee', border: '1px solid #555',
        padding: '4px 10px', borderRadius: '3px', cursor: 'pointer',
        fontSize: '12px',
      });
      btn.addEventListener('mousedown', (e) => {
        e.stopPropagation();
        this._simulateKeyPress(code);
      });
      this._el.appendChild(btn);
    }
  }

  private _simulateKeyPress(code: string): void {
    this._encoder.onKey(code, true);
    setTimeout(() => this._encoder.onKey(code, false), 80);
  }

  updateStats(fps: number, kbps: number, entities: number, tick: number): void {
    const existing = this._el.querySelector('#cp-stats') as HTMLDivElement | null;
    const el = existing || document.createElement('div');
    el.id = 'cp-stats';
    el.textContent = `FPS: ${fps} | ${kbps.toFixed(1)} KB/s | Entities: ${entities} | Tick: ${tick}`;
    el.style.color = '#0f0';
    el.style.marginTop = '6px';
    if (!existing) this._el.appendChild(el);
  }
}
