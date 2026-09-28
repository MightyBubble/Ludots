// Agent 副驾 dock：对话（LLM 或 echo 模式）、工具直调（不配模型即可用）、端点设置。
// 组件族结构对照 Vercel AI Elements（MIT），按 studio 主题自写适配。
import { useEffect, useState } from 'react';
import { Conversation } from './Conversation';
import { type AgentMessage } from './Message';
import { PromptInput } from './PromptInput';
import { ToolCard } from './ToolCard';
import { AGENT_TOOLS, validateTextBank, type ToolResult } from './tools';
import { chatCompletion, isConfigured, loadAgentConfig, saveAgentConfig, type AgentConfig } from './llm';
import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { Field, fieldControlClass } from '@/components/ui/Field';
import { labelClass } from '@/components/ui/chrome';

type DockTab = 'chat' | 'tools' | 'settings';

export function AgentDock({ onClose }: { onClose: () => void }) {
  const [tab, setTab] = useState<DockTab>('chat');
  const [config, setConfig] = useState<AgentConfig>(() => loadAgentConfig());
  const [messages, setMessages] = useState<AgentMessage[]>([]);
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [toolResults, setToolResults] = useState<ToolResult[]>([]);
  const [validateModId, setValidateModId] = useState('NarrativeShowcaseMod');
  const [savedNote, setSavedNote] = useState('');

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.ctrlKey && event.key === '`') {
        event.preventDefault();
        onClose();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const submit = async () => {
    const text = input.trim();
    if (!text || busy) return;
    setInput('');
    setMessages((prev) => [...prev, { kind: 'user', text }]);
    setBusy(true);
    try {
      if (!isConfigured(config)) {
        setMessages((prev) => [
          ...prev,
          {
            kind: 'assistant',
            text: `（echo 模式，未配置模型端点）你说：「${text}」。\n去「设置」填 OpenAI-compatible 端点后这里就是真模型；「工具」页现在就能直调编辑器工具。`,
          },
        ]);
      } else {
        const history = messages
          .filter((message): message is Extract<AgentMessage, { kind: 'user' | 'assistant' }> => message.kind !== 'tool')
          .map((message) => ({ role: message.kind, content: message.text }));
        const reply = await chatCompletion(config, [...history, { role: 'user', content: text }]);
        setMessages((prev) => [...prev, { kind: 'assistant', text: reply }]);
      }
    } catch (error) {
      setMessages((prev) => [
        ...prev,
        { kind: 'assistant', text: `模型端点出错：${error instanceof Error ? error.message : String(error)}` },
      ]);
    } finally {
      setBusy(false);
    }
  };

  const runTool = async (run: () => Promise<ToolResult>) => {
    const result = await run();
    setToolResults((prev) => [result, ...prev].slice(0, 12));
  };

  return (
    <aside
      data-agent-dock="open"
      className="flex h-full w-[340px] shrink-0 flex-col border-l border-studio-elevated bg-studio-surface"
    >
      <div className="flex h-9 shrink-0 items-center gap-1 border-b border-studio-elevated px-2">
        <span className="text-xs font-semibold text-studio-label">副驾</span>
        <Badge tone={isConfigured(config) ? 'blue' : 'muted'}>
          {isConfigured(config) ? config.model : 'echo 模式'}
        </Badge>
        <div className="ml-auto flex items-center gap-1">
          {(
            [
              ['chat', '对话'],
              ['tools', '工具'],
              ['settings', '设置'],
            ] as Array<[DockTab, string]>
          ).map(([id, title]) => (
            <button
              key={id}
              type="button"
              onClick={() => setTab(id)}
              className={`rounded-md px-2 py-1 text-[11px] ${
                tab === id ? 'bg-studio-elevated text-studio-label' : 'text-studio-muted hover:bg-studio-elevated'
              }`}
            >
              {title}
            </button>
          ))}
          <button
            type="button"
            title="收起（Ctrl+`）"
            onClick={onClose}
            className="rounded-md px-2 py-1 text-[11px] text-studio-muted hover:bg-studio-elevated"
          >
            ✕
          </button>
        </div>
      </div>

      {tab === 'chat' ? (
        <div className="flex min-h-0 flex-1 flex-col gap-2 p-2">
          <Conversation messages={messages} />
          <PromptInput
            value={input}
            onChange={setInput}
            onSubmit={() => void submit()}
            busy={busy}
            footer={<span className="text-[10px] text-studio-muted">Ctrl+Enter 发送 · Ctrl+\` 收起副驾</span>}
          />
        </div>
      ) : null}

      {tab === 'tools' ? (
        <div className="min-h-0 flex-1 space-y-2 overflow-auto p-2">
          <div className="flex flex-wrap gap-2">
            {AGENT_TOOLS.map((tool) => (
              <Button key={tool.id} size="sm" variant="ghost" onClick={() => void runTool(tool.run)}>
                {tool.label}
              </Button>
            ))}
          </div>
          <label className={labelClass}>
            引擎校验目标 Mod
            <input className={fieldControlClass} value={validateModId} onChange={(e) => setValidateModId(e.target.value)} />
          </label>
          <Button size="sm" variant="ghost" onClick={() => void runTool(() => validateTextBank(validateModId.trim()))}>
            跑文本表引擎校验
          </Button>
          {toolResults.length === 0 ? (
            <p className="px-1 py-4 text-xs text-studio-muted">点上面的工具直接跑，结果在这里出卡片。</p>
          ) : (
            toolResults.map((result) => <ToolCard key={result.key} result={result} />)
          )}
        </div>
      ) : null}

      {tab === 'settings' ? (
        <div className="min-h-0 flex-1 space-y-3 overflow-auto p-3">
          <p className="text-[11px] leading-relaxed text-studio-muted">
            填 OpenAI-compatible 端点（自托管或云上均可）。密钥只存本机浏览器，不进仓库、不发桥。
          </p>
          <Field label="Base URL">
            <input
              value={config.baseUrl}
              placeholder="https://your-endpoint/v1"
              onChange={(e) => setConfig({ ...config, baseUrl: e.target.value })}
            />
          </Field>
          <Field label="模型">
            <input
              value={config.model}
              placeholder="模型 id"
              onChange={(e) => setConfig({ ...config, model: e.target.value })}
            />
          </Field>
          <Field label="API Key" hint="留空 = echo 模式。">
            <input
              type="password"
              value={config.apiKey}
              onChange={(e) => setConfig({ ...config, apiKey: e.target.value })}
            />
          </Field>
          <div className="flex items-center gap-2">
            <Button
              variant="primary"
              size="sm"
              onClick={() => {
                saveAgentConfig(config);
                setSavedNote(isConfigured(config) ? `已保存：${config.model}` : '已保存（未配全，仍是 echo 模式）。');
              }}
            >
              保存配置
            </Button>
            {savedNote ? <span className="text-[11px] text-studio-blue">{savedNote}</span> : null}
          </div>
        </div>
      ) : null}
    </aside>
  );
}
