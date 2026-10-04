// Inline authoring sync: the dialogue node is the author's only surface — pick a
// speaker, type the line text. This planner materializes those drafts into the
// three engine catalogs (lines.json / text_tokens.json / text_locales.json) with
// derived ids, so the author never touches a token by hand. The engine contract
// is untouched: nodes still store lineId only, and the bridge validate endpoint
// remains the save gate.

import type { LocaleRoot, TextTokenRow } from '../text-bank/textBankModel';
import type { DialogueTree } from './dialogueTreeModel';

export type LineRow = { id: string; speakerId: string; textToken: string; tags?: string[] };

export type SpeakerRow = {
  id: string;
  displayNameToken: string;
  portraitImageId?: string;
  standingImageId?: string;
};

/** Edited inline fields for one statement. Keyed by lineId when bound. */
export type LineDraft = { speakerId?: string; text?: string };

/** Quick-add speaker form result; materialized with a derived displayName token. */
export type SpeakerDraft = {
  id: string;
  displayName: string;
  portraitImageId?: string;
  standingImageId?: string;
};

export const FALLBACK_DEFAULT_LOCALE = 'zh-CN';

export function slug(source: string): string {
  const clean = source
    .trim()
    .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
    .toLowerCase()
    .replace(/[^a-z0-9_]+/g, '_')
    .replace(/_+/g, '_')
    .replace(/^_+|_+$/g, '');
  return clean.length > 0 ? clean : 'x';
}

export function draftKeyForNode(tree: DialogueTree, nodeId: string): string {
  return `@node/${tree.id}/${nodeId}`;
}

export function draftKeyForChoice(tree: DialogueTree, nodeId: string, choiceId: string): string {
  return `@choice/${tree.id}/${nodeId}/${choiceId}`;
}

function uniqueId(candidate: string, taken: Set<string>): string {
  if (!taken.has(candidate)) return candidate;
  let n = 2;
  while (taken.has(`${candidate}_${n}`)) n += 1;
  return `${candidate}_${n}`;
}

export type InlineSyncInput = {
  dialogues: DialogueTree[];
  lines: LineRow[];
  speakers: SpeakerRow[];
  tokens: TextTokenRow[];
  localeRoot: LocaleRoot | null;
  drafts: Record<string, LineDraft>;
  newSpeakers: SpeakerDraft[];
};

export type InlineSyncPlan = {
  dialogues: DialogueTree[];
  lines: LineRow[];
  speakers: SpeakerRow[];
  tokens: TextTokenRow[];
  localeRoot: LocaleRoot;
  createdLineIds: string[];
  createdTokenIds: string[];
  orphanLineIds: string[];
};

export type InlineSyncResult = { ok: true; plan: InlineSyncPlan } | { ok: false; error: string };

/**
 * Applies drafts to a bound line in place: speaker swap, and text written into
 * the default-locale template of the line's token (token derived when empty).
 */
function applyDraftToBoundLine(
  line: LineRow,
  draft: LineDraft,
  tokens: TextTokenRow[],
  tokenTaken: Set<string>,
  defaultTable: Record<string, string>,
): string[] {
  const created: string[] = [];
  if (typeof draft.speakerId === 'string' && draft.speakerId !== line.speakerId) {
    line.speakerId = draft.speakerId;
  }
  if (typeof draft.text === 'string') {
    if (!line.textToken) {
      const token = uniqueId(`story.${line.id}`, tokenTaken);
      line.textToken = token;
      tokens.push({ id: token, argCount: 0 });
      tokenTaken.add(token);
      created.push(token);
    }
    defaultTable[line.textToken] = draft.text;
  }
  return created;
}

export function planInlineSync(input: InlineSyncInput): InlineSyncResult {
  const lines = input.lines.map((line) => ({ ...line, tags: line.tags ? [...line.tags] : undefined }));
  const speakers = input.speakers.map((speaker) => ({ ...speaker }));
  const tokens = input.tokens.map((token) => ({ ...token }));
  const localeRoot: LocaleRoot = input.localeRoot
    ? structuredClone(input.localeRoot)
    : { defaultLocale: FALLBACK_DEFAULT_LOCALE, locales: { [FALLBACK_DEFAULT_LOCALE]: {} } };
  const defaultName = localeRoot.defaultLocale?.trim() ? localeRoot.defaultLocale : FALLBACK_DEFAULT_LOCALE;
  localeRoot.defaultLocale = defaultName;
  if (!localeRoot.locales[defaultName] || typeof localeRoot.locales[defaultName] !== 'object') {
    localeRoot.locales[defaultName] = {};
  }
  const defaultTable = localeRoot.locales[defaultName]!;

  const lineTaken = new Set(lines.map((line) => line.id));
  const tokenTaken = new Set(tokens.map((token) => token.id));
  const createdLineIds: string[] = [];
  const createdTokenIds: string[] = [];

  for (const draft of input.newSpeakers) {
    const id = draft.id.trim();
    if (!id) return { ok: false, error: '新建说话人缺 id。' };
    if (speakers.some((speaker) => speaker.id === id)) {
      return { ok: false, error: `说话人 ${id} 已存在。` };
    }
    const speakerKey = id.replace(/^speaker\./, '');
    const token = uniqueId(`story.speaker.${speakerKey}.name`, tokenTaken);
    speakers.push({ id, displayNameToken: token, portraitImageId: draft.portraitImageId, standingImageId: draft.standingImageId });
    tokens.push({ id: token, argCount: 0 });
    tokenTaken.add(token);
    createdTokenIds.push(token);
    defaultTable[token] = draft.displayName;
  }

  const referenced = new Set<string>();
  const dialogues = input.dialogues.map((tree) => ({
    ...tree,
    nodes: tree.nodes.map((node) => ({ ...node })),
  }));

  for (const tree of dialogues) {
    for (let nodeIndex = 0; nodeIndex < tree.nodes.length; nodeIndex += 1) {
      const node = tree.nodes[nodeIndex]!;
      const line = node.lineId ? lines.find((row) => row.id === node.lineId) : undefined;
      if (node.lineId && !line) {
        return { ok: false, error: `节点 ${node.id} 绑定的台词 ${node.lineId} 不在台词本里。` };
      }
      if (line) {
        const draft = input.drafts[line.id];
        if (draft) {
          createdTokenIds.push(...applyDraftToBoundLine(line, draft, tokens, tokenTaken, defaultTable));
        }
        referenced.add(line.id);
      } else {
        const draft = input.drafts[draftKeyForNode(tree, node.id)];
        if (draft && ((draft.text ?? '').trim() !== '' || (draft.speakerId ?? '').trim() !== '')) {
          const lineId = uniqueId(`line.${slug(tree.id)}.${slug(node.id)}`, lineTaken);
          const token = uniqueId(`story.${lineId}`, tokenTaken);
          lines.push({ id: lineId, speakerId: draft.speakerId ?? '', textToken: token, tags: [] });
          lineTaken.add(lineId);
          tokens.push({ id: token, argCount: 0 });
          tokenTaken.add(token);
          defaultTable[token] = draft.text ?? '';
          createdLineIds.push(lineId);
          createdTokenIds.push(token);
          node.lineId = lineId;
          referenced.add(lineId);
        }
      }

      const choices = (node.choices ?? []).map((choice) => ({ ...choice }));
      for (let choiceIndex = 0; choiceIndex < choices.length; choiceIndex += 1) {
        const choice = choices[choiceIndex]!;
        const choiceLine = choice.lineId ? lines.find((row) => row.id === choice.lineId) : undefined;
        if (choice.lineId && !choiceLine) {
          return { ok: false, error: `选项 ${choice.id} 绑定的台词 ${choice.lineId} 不在台词本里。` };
        }
        if (choiceLine) {
          const draft = input.drafts[choiceLine.id];
          if (draft) {
            createdTokenIds.push(...applyDraftToBoundLine(choiceLine, draft, tokens, tokenTaken, defaultTable));
          }
          referenced.add(choiceLine.id);
          continue;
        }
        const draft = input.drafts[draftKeyForChoice(tree, node.id, choice.id)];
        if (draft && ((draft.text ?? '').trim() !== '' || (draft.speakerId ?? '').trim() !== '')) {
          const lineId = uniqueId(`line.${slug(tree.id)}.${slug(node.id)}__${slug(choice.id)}`, lineTaken);
          const token = uniqueId(`story.${lineId}`, tokenTaken);
          lines.push({ id: lineId, speakerId: draft.speakerId ?? '', textToken: token, tags: [] });
          lineTaken.add(lineId);
          tokens.push({ id: token, argCount: 0 });
          tokenTaken.add(token);
          defaultTable[token] = draft.text ?? '';
          createdLineIds.push(lineId);
          createdTokenIds.push(token);
          choice.lineId = lineId;
          referenced.add(lineId);
        }
      }
      if (choices.length > 0) tree.nodes[nodeIndex] = { ...node, choices };
    }
  }

  const orphanLineIds = lines.map((line) => line.id).filter((id) => !referenced.has(id));

  return {
    ok: true,
    plan: { dialogues, lines, speakers, tokens, localeRoot, createdLineIds, createdTokenIds, orphanLineIds },
  };
}
