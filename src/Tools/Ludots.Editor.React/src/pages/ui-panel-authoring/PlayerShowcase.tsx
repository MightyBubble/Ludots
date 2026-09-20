import React, { useEffect, useState } from 'react';
import type { PanelTemplate, SurfaceKind } from './model';
import { SURFACE_META } from './model';

type Beat = {
  id: string;
  label: string;
  narrate: string;
  values: Record<string, string>;
};

const ENTITY_BEATS: Beat[] = [
  {
    id: 'idle',
    label: '点选斥候',
    narrate: '你点中了斥候 Scout-7。信息卡按作者配好的引脚亮起来——不是界面自己编的数。',
    values: { hp: '840', lastKill: '—', curState: '待命' },
  },
  {
    id: 'kill',
    label: '刚击杀敌军',
    narrate: '黑板记下上一次击杀对象；状态仍是移动。文案来自变量表，不是硬编码 HTML。',
    values: { hp: '720', lastKill: 'Raider-3', curState: '移动中' },
  },
  {
    id: 'fight',
    label: '交火',
    narrate: '状态 tag 变成交战；查表后面板显示本地化状态。换表面语言，这三个引脚不用重算。',
    values: { hp: '510', lastKill: 'Raider-3', curState: '交战中' },
  },
];

const AGG_BEATS: Beat[] = [
  {
    id: 'full',
    label: '三座矿站开工',
    narrate: '顶栏资源是图算出来的合计。拆掉一座，数字会跟着变——不是 UI 自己加的。',
    values: { oreTotal: '1200', crystalTotal: '450' },
  },
  {
    id: 'cut',
    label: '停工一座',
    narrate: '一座产出归零后，合计立刻掉下来。同一套变量，Web UI / Reactive 只换画法。',
    values: { oreTotal: '800', crystalTotal: '300' },
  },
];

function beatsFor(tpl: PanelTemplate): Beat[] {
  return tpl.id === 'panel.player_aggregate' ? AGG_BEATS : ENTITY_BEATS;
}

function fillCopy(template: string, values: Record<string, string>): string {
  let text = template;
  for (const [id, value] of Object.entries(values)) {
    text = text.split(`{${id}}`).join(value);
  }
  return text;
}

export function PlayerShowcase({
  tpl,
  surface,
}: {
  tpl: PanelTemplate;
  surface: SurfaceKind;
}) {
  const beats = beatsFor(tpl);
  const [beatId, setBeatId] = useState(beats[0].id);
  const beat = beats.find((b) => b.id === beatId) ?? beats[0];

  useEffect(() => {
    setBeatId(beatsFor(tpl)[0].id);
  }, [tpl.id]);

  return (
    <div className="upa-play">
      <div className="upa-play-story">
        <p className="upa-play-kicker">试玩 · 玩家视角</p>
        <h3>{tpl.name}</h3>
        <p className="upa-play-narrate" key={beat.id}>
          {beat.narrate}
        </p>
        <div className="upa-play-beats" role="group" aria-label="情景">
          {beats.map((b) => (
            <button
              key={b.id}
              type="button"
              className={`upa-play-beat ${b.id === beat.id ? 'is-active' : ''}`}
              onClick={() => setBeatId(b.id)}
            >
              {b.label}
            </button>
          ))}
        </div>
      </div>

      <div className="upa-play-stage">
        <article className={`upa-play-card surface-${surface}`} key={`${tpl.id}-${beat.id}`}>
          <header>
            <span className="upa-play-card-title">
              {tpl.id === 'panel.player_aggregate' ? '资源总览' : '实体信息'}
            </span>
            <span className="upa-play-card-surface">{SURFACE_META[surface].label}</span>
          </header>
          <pre className="upa-play-card-body">{fillCopy(tpl.copyTemplate, beat.values)}</pre>
          <ul className="upa-play-pins">
            {tpl.variables.map((v) => (
              <li key={v.id}>
                <span>{v.label}</span>
                <strong>{beat.values[v.id] ?? '—'}</strong>
              </li>
            ))}
          </ul>
        </article>
        <p className="upa-play-footnote">
          引脚来自作者配置的同一张变量表；表面只决定怎么画，不算第二套数。
        </p>
      </div>
    </div>
  );
}
