// Text bank model: mirrors the engine's locale-template contract
// (PresentationTextCatalogLoader) so the editor's preview and quick checks stay
// aligned with what the game will accept at load time. The authoritative gate is
// still the bridge validate endpoint, which runs the real loader.

export type TextTokenRow = { id: string; argCount: number };

export type LocaleRoot = {
  defaultLocale: string;
  locales: Record<string, Record<string, string>>;
};

export const MAX_ARGS = 4;

export type BankIssue = {
  where: string;
  message: string;
};

export type PreviewRun = {
  text: string;
  bold?: boolean;
  italic?: boolean;
  color?: string;
  placeholder?: number;
};

export type PreviewResult = {
  runs: PreviewRun[];
  error?: string;
};

type MarkupStyle = { bold?: boolean; italic?: boolean; color?: string };

const COLOR_HEX_RE = /^#[0-9a-fA-F]{8}/;

function isTagNameChar(ch: string): boolean {
  return /[a-zA-Z]/.test(ch);
}

/**
 * Parses one template into styled runs using the engine's restricted markup:
 * <b>, <i>, <color=#AARRGGBB> (paired, non-nested), {N} placeholders,
 * {{ / }} escapes. Returns the first contract violation as `error`.
 */
export function parseTemplatePreview(source: string, argCount: number): PreviewResult {
  const runs: PreviewRun[] = [];
  let literal = '';
  let activeStyle: MarkupStyle = {};
  let openTagName: string | null = null;

  const flush = () => {
    if (literal.length > 0) {
      runs.push({ text: literal, ...activeStyle });
      literal = '';
    }
  };

  let i = 0;
  while (i < source.length) {
    const ch = source[i];
    if (ch === '{') {
      if (source[i + 1] === '{') {
        literal += '{';
        i += 2;
        continue;
      }
      const close = source.indexOf('}', i + 1);
      if (close < 0) {
        return { runs, error: '占位符没有闭合的 }' };
      }
      const placeholder = source.slice(i + 1, close);
      if (placeholder.includes(':')) {
        return { runs, error: `不支持的占位符格式 {${placeholder}}` };
      }
      if (!/^\d+$/.test(placeholder)) {
        return { runs, error: `占位符 {${placeholder}} 不是数字` };
      }
      const argIndex = Number(placeholder);
      if (argIndex >= argCount) {
        return { runs, error: `占位符 {${argIndex}} 超出 argCount=${argCount}` };
      }
      flush();
      runs.push({ text: `{${argIndex}}`, placeholder: argIndex, ...activeStyle });
      i = close + 1;
      continue;
    }
    if (ch === '}') {
      if (source[i + 1] === '}') {
        literal += '}';
        i += 2;
        continue;
      }
      return { runs, error: "出现落单的 '}'" };
    }
    if (ch === '<') {
      const tag = tryParseMarkupTag(source, i);
      if (!tag) {
        return { runs, error: `位置 ${i} 附近有非法标记` };
      }
      if (tag.isClose) {
        if (openTagName === null || openTagName !== tag.name) {
          return { runs, error: `</${tag.name}> 没有对应的打开标记` };
        }
        flush();
        activeStyle = {};
        openTagName = null;
      } else {
        if (openTagName !== null) {
          return { runs, error: '标记不能嵌套' };
        }
        flush();
        activeStyle = tag.style;
        openTagName = tag.name;
      }
      i = tag.end + 1;
      continue;
    }
    literal += ch;
    i += 1;
  }

  if (openTagName !== null) {
    return { runs, error: `<${openTagName}> 没有闭合` };
  }
  flush();
  return { runs };
}

function tryParseMarkupTag(
  source: string,
  start: number,
): { name: string; isClose: boolean; style: MarkupStyle; end: number } | null {
  if (source[start] !== '<') return null;
  let cursor = start + 1;
  let isClose = false;
  if (source[cursor] === '/') {
    isClose = true;
    cursor += 1;
  }
  const nameStart = cursor;
  while (cursor < source.length && isTagNameChar(source[cursor])) {
    cursor += 1;
  }
  if (cursor === nameStart) return null;
  const name = source.slice(nameStart, cursor);
  while (cursor < source.length && /\s/.test(source[cursor])) {
    cursor += 1;
  }

  let style: MarkupStyle = {};
  if (!isClose) {
    if (name === 'b') {
      style = { bold: true };
    } else if (name === 'i') {
      style = { italic: true };
    } else if (name === 'color') {
      if (source[cursor] !== '=') return null;
      cursor += 1;
      const match = COLOR_HEX_RE.exec(source.slice(cursor));
      if (!match) return null;
      cursor += match[0].length;
      style = { color: argbToCss(match[0].slice(1)) };
      while (cursor < source.length && /\s/.test(source[cursor])) {
        cursor += 1;
      }
    } else {
      return null;
    }
  } else if (name !== 'b' && name !== 'i' && name !== 'color') {
    return null;
  }

  if (source[cursor] !== '>') return null;
  return { name, isClose, style, end: cursor };
}

/** AARRGGBB (alpha first, engine order) → CSS rgba. */
export function argbToCss(argb: string): string {
  const a = Number.parseInt(argb.slice(0, 2), 16) / 255;
  const r = Number.parseInt(argb.slice(2, 4), 16);
  const g = Number.parseInt(argb.slice(4, 6), 16);
  const b = Number.parseInt(argb.slice(6, 8), 16);
  return `rgba(${r}, ${g}, ${b}, ${a})`;
}

/** CSS rgb → engine AARRGGBB with full alpha, for toolbar color insertion. */
export function cssToArgbHex(cssColor: string): string | null {
  const hex = cssColor.replace('#', '');
  if (hex.length !== 6 || !/^[0-9a-fA-F]{6}$/.test(hex)) return null;
  return `FF${hex.toUpperCase()}`;
}

export function wrapSelection(
  source: string,
  selectionStart: number,
  selectionEnd: number,
  kind: 'b' | 'i' | 'color',
  argbHex?: string,
): { text: string; selectionStart: number; selectionEnd: number } {
  const open = kind === 'color' ? `<color=#${argbHex ?? 'FFFFFFFF'}>` : `<${kind}>`;
  const close = kind === 'color' ? '</color>' : `</${kind}>`;
  const next =
    source.slice(0, selectionStart) + open + source.slice(selectionStart, selectionEnd) + close + source.slice(selectionEnd);
  return {
    text: next,
    selectionStart: selectionStart + open.length,
    selectionEnd: selectionEnd + open.length,
  };
}

export type BankTables = {
  tokens: TextTokenRow[];
  localeRoot: LocaleRoot | null;
};

/**
 * Editor-side quick checks mirroring loader fail-closed rules: token identity,
 * locale coverage (every locale must define every token), and per-template
 * markup. The bridge validate call remains the save gate.
 */
export function validateBank(tokens: TextTokenRow[], localeRoot: LocaleRoot | null): BankIssue[] {
  const issues: BankIssue[] = [];
  const seen = new Set<string>();
  for (const token of tokens) {
    if (!token.id.trim()) {
      issues.push({ where: token.id || '(空 id)', message: '词条 id 不能为空' });
    } else if (seen.has(token.id)) {
      issues.push({ where: token.id, message: '词条 id 重复' });
    }
    seen.add(token.id);
    if (!Number.isInteger(token.argCount) || token.argCount < 0 || token.argCount > MAX_ARGS) {
      issues.push({ where: token.id || '(空 id)', message: `argCount 必须在 0 到 ${MAX_ARGS} 之间` });
    }
  }

  if (tokens.length === 0) return issues;
  if (!localeRoot) {
    issues.push({ where: '(语言表)', message: '有词条就必须有语言表 text_locales' });
    return issues;
  }
  if (!localeRoot.defaultLocale?.trim()) {
    issues.push({ where: '(语言表)', message: '必须声明非空 defaultLocale' });
  }
  const localeNames = Object.keys(localeRoot.locales ?? {});
  if (localeNames.length === 0) {
    issues.push({ where: '(语言表)', message: '至少要有一张语言表' });
    return issues;
  }
  if (localeRoot.defaultLocale && !localeNames.includes(localeRoot.defaultLocale)) {
    issues.push({ where: '(语言表)', message: `defaultLocale "${localeRoot.defaultLocale}" 不在语言列表里` });
  }

  for (const localeName of localeNames) {
    const table = localeRoot.locales[localeName] ?? {};
    for (const token of tokens) {
      const template = table[token.id];
      if (typeof template !== 'string') {
        issues.push({ where: `${localeName} / ${token.id}`, message: '缺这条翻译(引擎装载会失败)' });
        continue;
      }
      const preview = parseTemplatePreview(template, token.argCount);
      if (preview.error) {
        issues.push({ where: `${localeName} / ${token.id}`, message: preview.error });
      }
    }
    for (const tokenKey of Object.keys(table)) {
      if (!seen.has(tokenKey)) {
        issues.push({ where: `${localeName} / ${tokenKey}`, message: '引用了不存在的词条 id' });
      }
    }
  }
  return issues;
}

export function issueKey(issue: BankIssue): string {
  return `${issue.where}::${issue.message}`;
}
