import React, { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  TEMPLATES,
  SURFACE_META,
  pascal,
  csharpType,
  type PanelTemplate,
  type SurfaceKind,
  type PanelVariable,
} from './ui-panel-authoring/model';
import { ShaderGraphCanvas } from './ui-panel-authoring/ShaderGraphCanvas';
import { PlayerShowcase } from './ui-panel-authoring/PlayerShowcase';
import { authoringConfigJson, toAuthoringTemplate } from './ui-panel-authoring/authoringConfig';
import './ui-panel-authoring/authoring.css';

type WorkspaceMode = 'author' | 'play' | 'config';

function renderCopy(template: string, vars: PanelVariable[], demo: Record<string, string>) {
  let text = template;
  for (const v of vars) {
    text = text.split(`{${v.id}}`).join(demo[v.id] ?? `‹${v.id}›`);
  }
  return text;
}

function demoValues(tpl: PanelTemplate): Record<string, string> {
  if (tpl.id === 'panel.player_aggregate') {
    return { oreTotal: '1200', crystalTotal: '450' };
  }
  return { hp: '840', lastKill: 'Scout-7', curState: '交战中' };
}

function loweredOutputs(tpl: PanelTemplate): string {
  const lines = tpl.variables.map((v) => {
    const b = tpl.bindings[v.id];
    return `  { "id": "${v.id}", "type": "${v.valueKind}", "key": "${b?.graphOutputKey ?? v.id}", "source": "${b?.fromNodeId ?? '?'}" }`;
  });
  return ['// 落盘：Panel 多引脚 → outputs[]（不是 GraphNodeOp.Panel）', '"outputs": [', lines.join(',\n'), ']'].join(
    '\n',
  );
}

function SurfaceArtifact({
  surface,
  tpl,
}: {
  surface: SurfaceKind;
  tpl: PanelTemplate;
}) {
  const stateFields = tpl.variables
    .map((v) => `    ${csharpType(v.valueKind)} ${pascal(v.id)}`)
    .join(',\n');

  if (surface === 'reactive') {
    const code = [
      '// Reactive — 每个引脚 → TState 一个字段',
      `public sealed record ${pascal(tpl.id)}State(`,
      stateFields,
      ');',
      '',
      '// 一张图多出口写满 State，再 Ui.* 画',
      'Ui.Column(',
      '    Ui.Text($"… {state.' + pascal(tpl.variables[0]?.id ?? 'Value') + '}"),',
      '    …',
      ');',
    ].join('\n');
    return <pre className="upa-code">{code}</pre>;
  }

  if (surface === 'compose') {
    const fields = tpl.variables.map((v) => `${csharpType(v.valueKind)} _${v.id};`).join('\n');
    const code = [
      '// Compose — 每个引脚 → 控制器字段',
      fields,
      '',
      'void Rebuild() {',
      '    root = Ui.Panel(…);',
      '}',
    ].join('\n');
    return <pre className="upa-code">{code}</pre>;
  }

  if (surface === 'markup') {
    const code = [
      '<!-- Markup：布局；引脚值由 code-behind 写入 -->',
      '<section class="entity-card">',
      ...tpl.variables.map(
        (v) => `  <p data-field="${v.id}">${v.label}: <!-- code-behind --></p>`,
      ),
      '</section>',
    ].join('\n');
    return <pre className="upa-code">{code}</pre>;
  }

  const fields = tpl.variables
    .map((v) => {
      const b = tpl.bindings[v.id];
      const lines = [
        `    {`,
        `      "fieldId": "${v.id}",`,
        `      "sourceKind": "${b?.sourceKind ?? 'graphOutput'}",`,
      ];
      if (b?.attributeId) lines.push(`      "attributeId": "${b.attributeId}",`);
      if (b?.graphOutputKey) lines.push(`      "graphOutputKey": "${b.graphOutputKey}",`);
      lines.push(`    }`);
      return lines.join('\n');
    })
    .join(',\n');

  return (
    <pre className="upa-code">{`// Web UI — 每个引脚 → fields[] 一项
{
  "descriptorId": "${tpl.id}",
  "fields": [
${fields}
  ]
}`}</pre>
  );
}

async function copyText(text: string): Promise<void> {
  await navigator.clipboard.writeText(text);
}

function downloadJson(filename: string, text: string) {
  const blob = new Blob([text], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

export function UiPanelAuthoringPage() {
  const [templateId, setTemplateId] = useState(TEMPLATES[0].id);
  const [surface, setSurface] = useState<SurfaceKind>('reactive');
  const [selectedVar, setSelectedVar] = useState<string | null>('hp');
  const [mode, setMode] = useState<WorkspaceMode>('author');
  const [copied, setCopied] = useState(false);

  const tpl = useMemo(
    () => TEMPLATES.find((t) => t.id === templateId) ?? TEMPLATES[0],
    [templateId],
  );

  const configJson = useMemo(() => authoringConfigJson(TEMPLATES), []);
  const activeConfigJson = useMemo(
    () => JSON.stringify({ schema: 'ludots.ui.panel_template/v1', templates: [toAuthoringTemplate(tpl)] }, null, 2),
    [tpl],
  );

  const activeVar =
    selectedVar && tpl.variables.some((v) => v.id === selectedVar)
      ? selectedVar
      : tpl.variables[0]?.id ?? null;

  const binding = activeVar ? tpl.bindings[activeVar] : undefined;
  const demo = demoValues(tpl);

  React.useEffect(() => {
    setSurface(tpl.surfaceKind);
    setSelectedVar(tpl.variables[0]?.id ?? null);
  }, [tpl.id, tpl.surfaceKind, tpl.variables]);

  return (
    <div className="upa-root">
      <header className="upa-hero">
        <div>
          <p className="upa-kicker">
            <Link to="/">← 地图编辑器</Link>
            <span aria-hidden> · </span>
            Ludots Editor
          </p>
          <h1 className="upa-brand">
            Panel <span>Authoring</span>
          </h1>
          <p className="upa-lede">
            像编 Shader Graph：一张图、多种类型、右边一个带多引脚的 Panel
            汇入。引脚就是面板变量；表面只决定怎么画。可导出作者配置，也可切到试玩看玩家视角。
          </p>
        </div>
        <aside className="upa-contract" aria-label="第一性原则">
          <h2>和你原型的对应</h2>
          <ol>
            <li>PanelNode 多引脚 → 画布右侧 Panel 汇入（作者糖）</li>
            <li>一张图多出口 → 多条边进不同引脚（Float/Text/…）</li>
            <li>落盘 → outputs[] / bindings，不是新的 Graph 操作码</li>
            <li>四种表面只换投影母语，不拆成多张算数图</li>
          </ol>
        </aside>
      </header>

      <div className="upa-mode-row" role="tablist" aria-label="工作区">
        {(
          [
            ['author', '编排'],
            ['play', '试玩'],
            ['config', '配置'],
          ] as const
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={mode === id}
            className={`upa-mode ${mode === id ? 'is-active' : ''}`}
            onClick={() => setMode(id)}
          >
            {label}
          </button>
        ))}
      </div>

      <div className="upa-tpl-row" role="tablist" aria-label="模板">
        {TEMPLATES.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={t.id === tpl.id}
            className={`upa-tpl ${t.id === tpl.id ? 'is-active' : ''}`}
            onClick={() => setTemplateId(t.id)}
          >
            <span className="upa-tpl-id">{t.id}</span>
            <span className="upa-tpl-name">{t.name}</span>
            <span className="upa-tpl-blurb">{t.blurb}</span>
          </button>
        ))}
      </div>

      <div className="upa-surfaces" role="tablist" aria-label="表面语言">
        {(Object.keys(SURFACE_META) as SurfaceKind[]).map((kind) => (
          <button
            key={kind}
            type="button"
            role="tab"
            aria-selected={surface === kind}
            className={`upa-surface ${surface === kind ? 'is-active' : ''}`}
            onClick={() => setSurface(kind)}
          >
            <strong>{SURFACE_META[kind].label}</strong>
            <small>{SURFACE_META[kind].native}</small>
          </button>
        ))}
      </div>

      {mode === 'play' ? <PlayerShowcase tpl={tpl} surface={surface} /> : null}

      {mode === 'config' ? (
        <div className="upa-config-panel">
          <div className="upa-config-actions">
            <button
              type="button"
              onClick={async () => {
                await copyText(activeConfigJson);
                setCopied(true);
                window.setTimeout(() => setCopied(false), 1200);
              }}
            >
              {copied ? '已复制当前模板' : '复制当前模板 JSON'}
            </button>
            <button type="button" onClick={() => downloadJson(`${tpl.id}.json`, activeConfigJson)}>
              下载当前模板
            </button>
            <button type="button" onClick={() => downloadJson('panel_templates.json', configJson)}>
              下载全部模板
            </button>
          </div>
          <p className="upa-play-footnote">
            schema <code>ludots.ui.panel_template/v1</code> — variables / bindings / outputs /
            surfaceKind；运行时读这份配置，不读画布糖节点。
          </p>
          <pre className="upa-config-json">{activeConfigJson}</pre>
        </div>
      ) : null}

      {mode === 'author' ? (
        <div className="upa-stage upa-stage-shader">
          <section className="upa-col upa-col-graph" aria-labelledby="upa-graph-h">
            <div className="upa-col-h">
              <h3 id="upa-graph-h">计算图 · 多引脚 Panel 汇入</h3>
              <small>一眼看完：像材质输出节点</small>
            </div>
            <div className="upa-col-b upa-col-b-canvas">
              <ShaderGraphCanvas tpl={tpl} activeVar={activeVar} onSelectVar={setSelectedVar} />
            </div>
          </section>

          <aside className="upa-side">
            <section className="upa-col" aria-labelledby="upa-vars-h">
              <div className="upa-col-h">
                <h3 id="upa-vars-h">引脚 = 变量</h3>
                <small>点引脚高亮连线</small>
              </div>
              <div className="upa-col-b">
                <ul className="upa-var-list">
                  {tpl.variables.map((v) => {
                    const b = tpl.bindings[v.id];
                    return (
                      <li key={v.id}>
                        <button
                          type="button"
                          className={`upa-var ${activeVar === v.id ? 'is-active' : ''}`}
                          onClick={() => setSelectedVar(v.id)}
                        >
                          <span className="upa-var-id">{v.id}</span>
                          <span className="upa-var-label">{v.label}</span>
                          <span className="upa-var-meta">
                            {v.valueKind}
                            {b ? ` · ← ${b.fromNodeId ?? b.sourceKind}` : ''}
                          </span>
                        </button>
                      </li>
                    );
                  })}
                </ul>
                <div className="upa-preview">
                  <div className="upa-preview-label">模板文案 · {'{引脚}'}</div>
                  <pre>{renderCopy(tpl.copyTemplate, tpl.variables, demo)}</pre>
                </div>
                {binding ? (
                  <div className="upa-bind-card">
                    <h4>选中引脚 · {activeVar}</h4>
                    <dl>
                      <div>
                        <dt>sourceKind</dt>
                        <dd>{binding.sourceKind}</dd>
                      </div>
                      {binding.fromNodeId ? (
                        <div>
                          <dt>from node</dt>
                          <dd>{binding.fromNodeId}</dd>
                        </div>
                      ) : null}
                      {binding.graphOutputKey ? (
                        <div>
                          <dt>output key</dt>
                          <dd>{binding.graphOutputKey}</dd>
                        </div>
                      ) : null}
                    </dl>
                    {activeVar === 'lastKill' ? (
                      <p className="upa-debt">
                        作者意图：实体 BB 读上次击杀文案。现有 L0 仅有 Float/Int/Entity
                        黑板读；Text BB 仍欠（勿用 Attribute 假装）。也可改为 BB Entity +
                        表面解析显示名。
                      </p>
                    ) : null}
                    {activeVar === 'curState' ? (
                      <p className="upa-debt">
                        作者意图：ReadGameplayTag → LookupTagDisplayText。L0 快捷 op 已在运行时线
                        #868 落地；本编辑器仍是作者糖。仍欠表资产装载与表面 token→文案接线。
                      </p>
                    ) : null}
                  </div>
                ) : null}
              </div>
            </section>

            <section className="upa-col" aria-labelledby="upa-lower-h">
              <div className="upa-col-h">
                <h3 id="upa-lower-h">落盘 / {SURFACE_META[surface].label}</h3>
                <small>{SURFACE_META[surface].note}</small>
              </div>
              <div className="upa-col-b">
                <pre className="upa-code upa-code-tight">{loweredOutputs(tpl)}</pre>
                <SurfaceArtifact surface={surface} tpl={tpl} />
              </div>
            </section>
          </aside>
        </div>
      ) : null}
    </div>
  );
}

export default UiPanelAuthoringPage;
