// LLM 接入层：OpenAI-compatible chat completions 直调。
// 端点/密钥存浏览器本地，不进仓库；没有配置时 AgentDock 走 echo 模式与工具直调。

export type AgentConfig = {
  baseUrl: string;
  apiKey: string;
  model: string;
};

export type AgentChatMessage = { role: 'user' | 'assistant' | 'system'; content: string };

const STORAGE_KEY = 'ludots-agent-config';

export function loadAgentConfig(): AgentConfig {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return { baseUrl: '', apiKey: '', model: '' };
    const parsed = JSON.parse(raw) as Partial<AgentConfig>;
    return {
      baseUrl: typeof parsed.baseUrl === 'string' ? parsed.baseUrl : '',
      apiKey: typeof parsed.apiKey === 'string' ? parsed.apiKey : '',
      model: typeof parsed.model === 'string' ? parsed.model : '',
    };
  } catch {
    return { baseUrl: '', apiKey: '', model: '' };
  }
}

export function saveAgentConfig(config: AgentConfig): void {
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(config));
}

export function isConfigured(config: AgentConfig): boolean {
  return config.baseUrl.trim().length > 0 && config.apiKey.trim().length > 0 && config.model.trim().length > 0;
}

export async function chatCompletion(config: AgentConfig, messages: AgentChatMessage[]): Promise<string> {
  const response = await fetch(`${config.baseUrl.replace(/\/+$/, '')}/chat/completions`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${config.apiKey}`,
    },
    body: JSON.stringify({
      model: config.model,
      messages,
      stream: false,
    }),
  });
  const data = (await response.json()) as {
    choices?: Array<{ message?: { content?: string } }>;
    error?: { message?: string };
  };
  if (!response.ok || data.error) {
    throw new Error(data.error?.message ?? `模型端点返回 ${response.status}`);
  }
  const text = data.choices?.[0]?.message?.content;
  if (typeof text !== 'string' || text.length === 0) {
    throw new Error('模型端点没有返回文本。');
  }
  return text;
}
