import React, { useMemo, useState } from 'react';
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
import {
  assertPanelBindingContract,
  authoringConfigJson,
  toAuthoringTemplate,
} from './ui-panel-authoring/authoringConfig';
import { Button } from '@/components/ui/Button';
import { WorkspaceLayout } from '@/components/ui/WorkspaceLayout';
import './ui-panel-authoring/authoring.css';

type WorkspaceMode = 'author' | 'play' | 'config';

const MODES: ReadonlyArray<[WorkspaceMode, string]> = [
  ['author', '编排'],
  ['play', '试玩'],
  ['config', '配置'],
];

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
      const sourceKind = b?.sourceKind ?? 'graphOutput';
      assertPanelBindingContract({
        variableId: v.id,
        sourceKind,
        attributeId: b?.attributeId,
        graphOutputKey: b?.graphOutputKey,
      });
      const lines = [
        `    {`,
        `      "fieldId": "${v.id}",`,
        `      "sourceKind": "${sourceKind}",`,
      ];
      if (sourceKind === 'singleAttribute' || sourceKind === 'derivedAttribute') {
        lines.push(`      "attributeId": "${b?.attributeId}",`);
      } else if (b?.graphOutputKey) {
        lines.push(`      "graphOutputKey": "${b.graphOutputKey}",`);
      }
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
  const [exportError, setExportError] = useState('');

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

  const runExport = (action: () => void | Promise<void>) => {
    setExportError('');
    Promise.resolve()
      .then(action)
      .catch((err: unknown) => {
        setExportError(`导出失败（fail-closed）：${err instanceof Error ? err.message : String(err)}`);
      });
  };

  return (
    <WorkspaceLayout
      title="面板"
      blurb="面板模板：一张图多引脚汇入 Panel，四种表面换画法，导出 UI/panel_templates 配置。"
      status={copied ? '已复制当前模板 JSON' : ''}
      error={exportError}
      actions={
        <div className="flex items-center gap-1" role="tablist" aria-label="工作区">
          {MODES.map(([id, label]) => (
            <Button
              key={id}
              variant={mode === id ? 'primary' : 'ghost'}
              size="sm"
              onClick={() => setMode(id)}
            >
              {label}
            </Button>
          ))}
        </div>
      }
      rail={
        <div className="space-y-3">
          <div className="text-xs text-studio-muted">模板</div>
          <ul className="space-y-1">
            {TEMPLATES.map((t) => (
              <li key={t.id}>
                <button
                  type="button"
                  className={`w-full rounded-md border px-2 py-2 text-left ${
                    t.id === tpl.id
                      ? 'border-studio-blue bg-studio-blue/10'
                      : 'border-studio-elevated bg-studio-surface hover:bg-studio-elevated'
                  }`}
                  onClick={() => setTemplateId(t.id)}
                >
                  <div className="font-mono text-[11px] text-studio-label">{t.id}</div>
                  <div className="text-xs text-studio-secondary">{t.name}</div>
                  <div className="mt-0.5 text-[10px] text-studio-muted">{t.blurb}</div>
                </button>
              </li>
            ))}
          </ul>
        </div>
      }
      inspector={
        mode === 'author' ? (
          <div className="space-y-3">
            <div className="text-[10px] uppercase tracking-wide text-studio-muted">检查器</div>
            <div className="text-xs text-studio-muted">引脚 = 变量（点引脚高亮连线）</div>
            <ul className="space-y-1">
              {tpl.variables.map((v) => {
                const b = tpl.bindings[v.id];
                return (
                  <li key={v.id}>
                    <button
                      type="button"
                      className={`w-full rounded-md border px-2 py-1.5 text-left text-xs ${
                        activeVar === v.id
                          ? 'border-studio-blue bg-studio-blue/10'
                          : 'border-studio-elevated bg-studio-surface hover:bg-studio-elevated'
                      }`}
                      onClick={() => setSelectedVar(v.id)}
                    >
                      <span className="font-mono text-[11px] text-studio-label">{v.id}</span>
                      <span className="ml-2 text-studio-secondary">{v.label}</span>
                      <span className="block text-[10px] text-studio-muted">
                        {v.valueKind}
                        {b ? ` · ← ${b.fromNodeId ?? b.sourceKind}` : ''}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>

            <div className="rounded-md border border-studio-elevated bg-studio-bg p-2">
              <div className="mb-1 text-[10px] text-studio-muted">模板文案 · {'{引脚}'}</div>
              <pre className="whitespace-pre-wrap font-mono text-[11px] text-studio-secondary">
                {renderCopy(tpl.copyTemplate, tpl.variables, demo)}
              </pre>
            </div>

            {binding ? (
              <div className="space-y-2 rounded-md border border-studio-elevated bg-studio-bg p-2 text-xs">
                <div className="text-[10px] text-studio-muted">选中引脚 · {activeVar}</div>
                <div className="flex justify-between gap-2">
                  <span className="text-studio-muted">来源种类</span>
                  <code className="font-mono text-studio-label">{binding.sourceKind}</code>
                </div>
                {binding.fromNodeId ? (
                  <div className="flex justify-between gap-2">
                    <span className="text-studio-muted">来源节点</span>
                    <code className="font-mono text-studio-label">{binding.fromNodeId}</code>
                  </div>
                ) : null}
                {binding.graphOutputKey ? (
                  <div className="flex justify-between gap-2">
                    <span className="text-studio-muted">出口键</span>
                    <code className="font-mono text-studio-label">{binding.graphOutputKey}</code>
                  </div>
                ) : null}
                {activeVar === 'lastKill' ? (
                  <p className="text-[10px] leading-4 text-studio-yellow">
                    图出口是 Int（文案 token id）。Text 黑板读仍欠；表面再把 token 收成可见字。勿导出
                    TextToken 类型。
                  </p>
                ) : null}
                {activeVar === 'curState' ? (
                  <p className="text-[10px] leading-4 text-studio-yellow">
                    真链路：状态 tag id → ResolveTableRow → TableReadInt(displayToken) → Int。灰态节点只是「还欠玩法纯读
                    tag id」的意图标注，不是可编译 op。表面 token→文案另接。
                  </p>
                ) : null}
              </div>
            ) : null}

            <div className="space-y-2">
              <div className="text-[10px] text-studio-muted">落盘 / {SURFACE_META[surface].label}</div>
              <pre className="upa-code upa-code-tight">{loweredOutputs(tpl)}</pre>
              <SurfaceArtifact surface={surface} tpl={tpl} />
            </div>
          </div>
        ) : null
      }
    >
      {mode === 'author' ? (
        <div className="flex h-full min-h-0 flex-col">
          <div
            className="flex shrink-0 flex-wrap items-center gap-2 border-b border-studio-elevated bg-studio-surface px-3 py-2"
            role="tablist"
            aria-label="表面语言"
          >
            {(Object.keys(SURFACE_META) as SurfaceKind[]).map((kind) => (
              <button
                key={kind}
                type="button"
                role="tab"
                aria-selected={surface === kind}
                className={`rounded-md border px-2 py-1 text-xs ${
                  surface === kind
                    ? 'border-studio-blue bg-studio-blue/10 text-studio-label'
                    : 'border-studio-elevated text-studio-secondary hover:bg-studio-elevated'
                }`}
                onClick={() => setSurface(kind)}
              >
                <strong className="font-semibold">{SURFACE_META[kind].label}</strong>
                <span className="ml-1 text-[10px] text-studio-muted">{SURFACE_META[kind].native}</span>
              </button>
            ))}
          </div>
          <div className="min-h-0 flex-1 overflow-auto">
            <ShaderGraphCanvas tpl={tpl} activeVar={activeVar} onSelectVar={setSelectedVar} />
          </div>
        </div>
      ) : mode === 'play' ? (
        <div className="h-full overflow-auto p-4">
          <PlayerShowcase tpl={tpl} surface={surface} />
        </div>
      ) : (
        <div className="h-full space-y-3 overflow-auto p-4">
          <div className="flex flex-wrap gap-2">
            <Button
              variant="ghost"
              size="sm"
              onClick={() =>
                runExport(async () => {
                  await copyText(activeConfigJson);
                  setCopied(true);
                  window.setTimeout(() => setCopied(false), 1200);
                })
              }
            >
              复制当前模板 JSON
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => runExport(() => downloadJson(`${tpl.id}.json`, activeConfigJson))}
            >
              下载当前模板
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => runExport(() => downloadJson('panel_templates.json', configJson))}
            >
              下载全部模板
            </Button>
          </div>
          <p className="text-xs text-studio-muted">
            schema <code className="font-mono">ludots.ui.panel_template/v1</code> — variables / bindings / outputs /
            surfaceKind；运行时读这份配置，不读画布糖节点。
          </p>
          <pre className="upa-code">{activeConfigJson}</pre>
        </div>
      )}
    </WorkspaceLayout>
  );
}

export default UiPanelAuthoringPage;
