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
import './ui-panel-authoring/authoring.css';

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
      '// Reactive — 变量落在 TState',
      `public sealed record ${pascal(tpl.id)}State(`,
      stateFields,
      ');',
      '',
      '// 投影：GraphOutput / Attribute → State → Ui.*',
      'Ui.Column(',
      '    Ui.Text($"… {state.' + pascal(tpl.variables[0]?.id ?? 'Value') + '}"),',
      '    …',
      ');',
    ].join('\n');
    return <pre className="upa-code">{code}</pre>;
  }

  if (surface === 'compose') {
    const fields = tpl.variables
      .map((v) => `${csharpType(v.valueKind)} _${v.id};`)
      .join('\n');
    const code = [
      '// Compose — 变量落在控制器字段',
      fields,
      '',
      'void Rebuild() {',
      '    // 先 RequireSummary / Attribute，再：',
      '    root = Ui.Panel(',
      `        Ui.Text($"… {_${tpl.variables[0]?.id ?? 'value'}}"),`,
      '        …',
      '    );',
      '}',
    ].join('\n');
    return <pre className="upa-code">{code}</pre>;
  }

  if (surface === 'markup') {
    const code = [
      '<!-- Markup：布局 + ui-click；无引擎级 {{var}} 绑定 -->',
      '<section class="entity-card">',
      ...tpl.variables.map(
        (v) => `  <p data-field="${v.id}">${v.label}: <!-- code-behind 写入 --></p>`,
      ),
      '</section>',
      '',
      '// code-behind',
      'void Refresh() {',
      ...tpl.variables.map((v) => `    SetFieldText("${v.id}", ${pascal(v.id)}.ToString());`),
      '}',
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
    <pre className="upa-code">{`// Web UI — WPK descriptor（母语）
{
  "descriptorId": "${tpl.id}",
  "fields": [
${fields}
  ]
}`}</pre>
  );
}

export function UiPanelAuthoringPage() {
  const [templateId, setTemplateId] = useState(TEMPLATES[0].id);
  const [surface, setSurface] = useState<SurfaceKind>('reactive');
  const [selectedVar, setSelectedVar] = useState<string | null>('hp');
  const [highlightStep, setHighlightStep] = useState<string | null>(null);

  const tpl = useMemo(
    () => TEMPLATES.find((t) => t.id === templateId) ?? TEMPLATES[0],
    [templateId],
  );

  const activeVar = selectedVar && tpl.variables.some((v) => v.id === selectedVar)
    ? selectedVar
    : tpl.variables[0]?.id ?? null;

  const binding = activeVar ? tpl.bindings[activeVar] : undefined;
  const demo = demoValues(tpl);

  React.useEffect(() => {
    setSurface(tpl.surfaceKind);
    setSelectedVar(tpl.variables[0]?.id ?? null);
    setHighlightStep(null);
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
            先声明面板变量，再用计算图算满；Compose / Markup / Reactive / Web UI
            只是四种表面投影，不另造一套 Panel 图宇宙。
          </p>
        </div>
        <aside className="upa-contract" aria-label="第一性原则">
          <h2>第一性原则</h2>
          <ol>
            <li>变量在模板声明，文案只写 {'{variableId}'}</li>
            <li>图终点是变量槽，不是 PanelNode</li>
            <li>四种表面共用变量表与绑定，保持各自母语</li>
            <li>跨实体合计必须走图投影，禁止界面私算</li>
          </ol>
        </aside>
      </header>

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

      <div className="upa-stage">
        <section className="upa-col" aria-labelledby="upa-vars-h">
          <div className="upa-col-h">
            <h3 id="upa-vars-h">1 · 变量</h3>
            <small>模板声明</small>
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
                      onClick={() => {
                        setSelectedVar(v.id);
                        setHighlightStep(
                          Object.entries(tpl.stepToVariable).find(([, vid]) => vid === v.id)?.[0]
                            ?? b?.graphStepId
                            ?? null,
                        );
                      }}
                    >
                      <span className="upa-var-id">{v.id}</span>
                      <span className="upa-var-label">{v.label}</span>
                      <span className="upa-var-meta">
                        {v.valueKind}
                        {b ? ` · ${b.sourceKind}` : ''}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
            <div className="upa-preview">
              <div className="upa-preview-label">模板预览 · 引用变量</div>
              <pre>{renderCopy(tpl.copyTemplate, tpl.variables, demo)}</pre>
            </div>
          </div>
        </section>

        <section className="upa-col upa-col-wide" aria-labelledby="upa-graph-h">
          <div className="upa-col-h">
            <h3 id="upa-graph-h">2 · 计算图 → 变量槽</h3>
            <small>共用 L1，无 Presentation Kind</small>
          </div>
          <div className="upa-col-b">
            <div className="upa-flow" role="list">
              {tpl.steps.map((step, index) => {
                const sinkVar = tpl.stepToVariable[step.id];
                const lit =
                  highlightStep === step.id ||
                  (activeVar != null && sinkVar === activeVar) ||
                  (activeVar != null && tpl.bindings[activeVar]?.graphStepId === step.id);
                return (
                  <React.Fragment key={step.id}>
                    {index > 0 ? <div className="upa-flow-arrow" aria-hidden /> : null}
                    <button
                      type="button"
                      role="listitem"
                      className={`upa-step kind-${step.kind} ${lit ? 'is-lit' : ''}`}
                      onClick={() => {
                        setHighlightStep(step.id);
                        if (sinkVar) setSelectedVar(sinkVar);
                      }}
                    >
                      <span className="upa-step-kind">{step.kind}</span>
                      <span className="upa-step-title">{step.title}</span>
                      <span className="upa-step-detail">{step.detail}</span>
                      {sinkVar ? (
                        <span className="upa-step-sink">→ {'{' + sinkVar + '}'}</span>
                      ) : null}
                    </button>
                  </React.Fragment>
                );
              })}
            </div>
            {binding ? (
              <div className="upa-bind-card">
                <h4>绑定 · {activeVar}</h4>
                <dl>
                  <div>
                    <dt>sourceKind</dt>
                    <dd>{binding.sourceKind}</dd>
                  </div>
                  {binding.attributeId ? (
                    <div>
                      <dt>attributeId</dt>
                      <dd>{binding.attributeId}</dd>
                    </div>
                  ) : null}
                  {binding.graphOutputKey ? (
                    <div>
                      <dt>graphOutputKey</dt>
                      <dd>{binding.graphOutputKey}</dd>
                    </div>
                  ) : null}
                </dl>
              </div>
            ) : null}
          </div>
        </section>

        <section className="upa-col" aria-labelledby="upa-surface-h">
          <div className="upa-col-h">
            <h3 id="upa-surface-h">3 · {SURFACE_META[surface].label} 投影</h3>
            <small>{SURFACE_META[surface].note}</small>
          </div>
          <div className="upa-col-b">
            <SurfaceArtifact surface={surface} tpl={tpl} />
            <p className="upa-footnote">
              切换上方表面标签时，左侧变量与中间图不变——变的只是母语投影。
            </p>
          </div>
        </section>
      </div>
    </div>
  );
}

export default UiPanelAuthoringPage;
