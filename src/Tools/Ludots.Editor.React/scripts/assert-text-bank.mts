import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  MAX_ARGS,
  argbToCss,
  cssToArgbHex,
  parseTemplatePreview,
  validateBank,
  wrapSelection,
  type LocaleRoot,
  type TextTokenRow,
} from '../src/pages/text-bank/textBankModel.ts';

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

// 1. Real mod data round-trips the editor's quick validation with zero issues.
const here = dirname(fileURLToPath(import.meta.url));
const kitRoot = join(
  here,
  '../../../../mods/showcases/dialogue_author_kit/DialogueAuthorKitShowcaseMod/assets/Presentation',
);
const tokens = JSON.parse(readFileSync(join(kitRoot, 'text_tokens.json'), 'utf8')) as TextTokenRow[];
const localeRoot = JSON.parse(readFileSync(join(kitRoot, 'text_locales.json'), 'utf8')) as LocaleRoot;
const realIssues = validateBank(tokens, localeRoot);
assert(realIssues.length === 0, `author kit bank must validate clean, got: ${realIssues.map((i) => i.message).join('; ')}`);
assert(localeRoot.locales[localeRoot.defaultLocale] !== undefined, 'author kit defaultLocale must name a real table');

// 2. Preview parsing mirrors the engine's restricted markup contract.
const bold = parseTemplatePreview('灯还亮着，<b>别走神</b>，山谷在等', 0);
assert(bold.error === undefined, 'bold template must parse');
assert(bold.runs.some((run) => run.bold && run.text === '别走神'), 'bold run must carry the styled literal');

const colored = parseTemplatePreview('山谷在等<color=#FFF6C56B>见证者</color>', 0);
assert(colored.error === undefined, 'color template must parse');
const colorRun = colored.runs.find((run) => run.color !== undefined);
assert(colorRun?.text === '见证者', 'color run must carry the styled literal');
assert(colorRun?.color === argbToCss('FFF6C56B'), 'AARRGGBB must convert alpha-first to rgba');

const italic = parseTemplatePreview('<i>风声</i>', 0);
assert(italic.runs.some((run) => run.italic && run.text === '风声'), 'italic run must carry the styled literal');

const arg = parseTemplatePreview('第{0}章', 1);
assert(arg.error === undefined, 'in-range placeholder must parse');
assert(arg.runs.some((run) => run.placeholder === 0), 'placeholder run must be flagged');
assert(parseTemplatePreview('第{0}章', 0).error !== undefined, 'out-of-range placeholder must fail');
assert(parseTemplatePreview(`第{${MAX_ARGS}}章`, MAX_ARGS).error !== undefined, 'argIndex == argCount must fail');

const escaped = parseTemplatePreview(' brace {{ }} ok', 0);
assert(escaped.error === undefined, 'escaped braces must parse');
assert(escaped.runs[0]?.text === ' brace { } ok', 'escaped braces must collapse to single braces');

assert(parseTemplatePreview('<b>未闭合', 0).error !== undefined, 'unclosed tag must fail');
assert(parseTemplatePreview('<b><i>嵌套</i></b>', 0).error !== undefined, 'nested tags must fail');
assert(parseTemplatePreview('</b>落单', 0).error !== undefined, 'stray close tag must fail');
assert(parseTemplatePreview('<color=#ZZ>坏色</color>', 0).error !== undefined, 'bad color must fail');
assert(parseTemplatePreview('落单}', 0).error !== undefined, 'unmatched brace must fail');
assert(parseTemplatePreview('<u>下划线</u>', 0).error !== undefined, 'unsupported tag must fail');
assert(parseTemplatePreview('<b>{0}</b>', 1).error === undefined, 'markup wrapping a placeholder is legal');

// 3. Bank-level checks: coverage and identity rules the loader enforces at boot.
const missing = validateBank([{ id: 'a.b', argCount: 0 }], { defaultLocale: 'zh-CN', locales: { 'zh-CN': {}, 'en-US': {} } });
assert(
  missing.filter((issue) => issue.message.includes('缺这条翻译')).length === 2,
  'every locale must cover every token',
);
const unknownRef = validateBank(
  [{ id: 'a.b', argCount: 0 }],
  { defaultLocale: 'zh-CN', locales: { 'zh-CN': { 'a.b': 'x', 'ghost.id': 'y' } } },
);
assert(unknownRef.some((issue) => issue.message.includes('不存在的词条')), 'unknown token reference must fail');
const duplicate = validateBank(
  [
    { id: 'a.b', argCount: 0 },
    { id: 'a.b', argCount: 0 },
  ],
  null,
);
assert(duplicate.some((issue) => issue.message.includes('重复')), 'duplicate ids must fail');
const badArgCount = validateBank([{ id: 'a.b', argCount: MAX_ARGS + 1 }], null);
assert(badArgCount.some((issue) => issue.message.includes('argCount')), 'argCount above MaxArgs must fail');
const noDefault = validateBank([{ id: 'a.b', argCount: 0 }], { defaultLocale: '', locales: { 'zh-CN': { 'a.b': 'x' } } });
assert(noDefault.some((issue) => issue.message.includes('defaultLocale')), 'empty defaultLocale must fail');
const defaultNotListed = validateBank([{ id: 'a.b', argCount: 0 }], {
  defaultLocale: 'fr-FR',
  locales: { 'zh-CN': { 'a.b': 'x' } },
});
assert(defaultNotListed.some((issue) => issue.message.includes('不在语言列表')), 'defaultLocale must exist in tables');

// 4. Toolbar markup insertion wraps the selection and restores it inside the tags.
const wrapped = wrapSelection('前中后', 1, 2, 'b');
assert(wrapped.text === '前<b>中</b>后', 'bold wrap must insert paired tags around selection');
assert(wrapped.selectionStart === 4 && wrapped.selectionEnd === 5, 'selection must move inside the tags');
const colorWrapped = wrapSelection('前中后', 1, 2, 'color', 'FFF6C56B');
assert(colorWrapped.text === '前<color=#FFF6C56B>中</color>后', 'color wrap must use engine AARRGGBB form');
assert(cssToArgbHex('#f00') === null, 'short hex must be rejected for toolbar conversion');
assert(cssToArgbHex('#FF6C56') === 'FFFF6C56', 'css hex must become alpha-first AARRGGBB');

console.log('assert-text-bank: ok');
