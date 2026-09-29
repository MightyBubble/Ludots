import { useCallback, useState } from 'react';

const STUDIO_MOD_KEY = 'ludots-studio-mod';

export function readStudioMod(): string | null {
  try {
    return window.localStorage.getItem(STUDIO_MOD_KEY);
  } catch {
    return null;
  }
}

export function writeStudioMod(modId: string): void {
  try {
    window.localStorage.setItem(STUDIO_MOD_KEY, modId);
  } catch {
    // 隐身模式等存储不可用时静默降级为会话内记忆
  }
}

/**
 * 工作室当前的 Mod 上下文：房间之间共用同一份记忆——
 * 在对话房切到 NarrativeShowcaseMod，去时间轴房还是它。
 */
export function useStudioMod(fallback: string) {
  const [modId, setModId] = useState(() => readStudioMod() ?? fallback);
  const selectMod = useCallback((next: string) => {
    setModId(next);
    writeStudioMod(next);
  }, []);
  return [modId, selectMod] as const;
}
