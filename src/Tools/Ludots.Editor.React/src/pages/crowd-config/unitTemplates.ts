export type FieldSource = { sourceMod?: string; sourceFile?: string; template?: string };
export type UnitField = { name: string; kind: 'string' | 'number' | 'bool'; reference?: string | null };
export type PresenterLink = FieldSource & { definitionId: string; ruleOwner: string; rule: string; templateKey: string };
export type ProfileView = {
  id: string;
  navProfileId?: string;
  values: Record<string, unknown>;
  fields: Record<string, FieldSource>;
  ownedBySelection: boolean;
  usedBy: string[];
};
export type UnitTemplateItem = {
  id: string;
  ownedBySelection: boolean;
  inherits: string[];
  agent: Record<string, unknown>;
  fields: Record<string, FieldSource>;
  profile: ProfileView | null;
  presenters: PresenterLink[];
};
export type AgentProfileOption = { id: string; radiusCm?: number; navProfileId?: string };
export type UnitTemplatesView = {
  component: string;
  fields: UnitField[];
  profileFields: UnitField[];
  agentProfiles: AgentProfileOption[];
  items: UnitTemplateItem[];
  issues: Issue[];
};
export type Issue = { path?: string; message?: string; sourceMod?: string; sourceFile?: string };

type Entry = { id?: string; components?: Record<string, Record<string, unknown>> } & Record<string, unknown>;
type TemplatesDraft = { unitTypes?: Entry[]; agentProfiles?: Entry[] } & Record<string, unknown>;

/** 数字框里不是有限数的内容原样交给装载器，由装载器给出它的报错。 */
export function authoredValue(kind: UnitField['kind'], raw: string | boolean): unknown {
  if (kind === 'bool') return raw === true || raw === 'true';
  const text = String(raw);
  if (kind === 'number') {
    const trimmed = text.trim();
    return trimmed !== '' && Number.isFinite(Number(trimmed)) ? Number(trimmed) : text;
  }
  return text;
}

function parse(draftText: string): TemplatesDraft {
  const parsed = JSON.parse(draftText || '{}') as unknown;
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('实体模板草稿的根必须是对象。');
  }
  return parsed as TemplatesDraft;
}

function entry(list: Entry[] | undefined, id: string): Entry | undefined {
  return (list ?? []).find((item) => item?.id === id);
}

export function draftAgent(draftText: string, component: string, templateId: string): Record<string, unknown> | undefined {
  try {
    return entry(parse(draftText).unitTypes, templateId)?.components?.[component];
  } catch {
    return undefined;
  }
}

export function draftProfile(draftText: string, profileId: string): Record<string, unknown> | undefined {
  try {
    return entry(parse(draftText).agentProfiles, profileId);
  } catch {
    return undefined;
  }
}

export function setAgentField(draftText: string, component: string, templateId: string, field: string, value: unknown): string {
  const draft = parse(draftText);
  const unit = entry(draft.unitTypes, templateId);
  if (!unit) throw new Error(`草稿的 unitTypes 里没有模板 "${templateId}"。`);
  unit.components ??= {};
  unit.components[component] ??= {};
  unit.components[component][field] = value;
  return JSON.stringify(draft, null, 2);
}

export function setProfileField(draftText: string, profileId: string, field: string, value: unknown): string {
  const draft = parse(draftText);
  const profile = entry(draft.agentProfiles, profileId);
  if (!profile) throw new Error(`草稿的 agentProfiles 里没有档案 "${profileId}"。`);
  profile[field] = value;
  return JSON.stringify(draft, null, 2);
}

function names(message: string, id: string): boolean {
  return message.includes(`"${id}"`) || message.includes(`'${id}'`);
}

function mentionsField(issue: Issue, field: string): boolean {
  const path = issue.path ?? '';
  if (path === field || path.endsWith(`.${field}`)) return true;
  return new RegExp(`(^|[^A-Za-z])${field}([^A-Za-z]|$)`, 'i').test(issue.message ?? '');
}

/** 装载器报错里用引号点名了这个模板或它的档案。 */
export function issuesFor(issues: Issue[], templateId: string, profileId?: string): Issue[] {
  return issues.filter((issue) => {
    const message = issue.message ?? '';
    if (names(message, templateId)) return true;
    if (profileId && names(message, profileId)) return true;
    return (issue.path ?? '').startsWith(`unitTypes.${templateId}.`);
  });
}

export const PRESENTERS_FILE = 'Presentation/presenters.json';

/** 和这个模板相关、点名了表现文件的报错：群体会话的表现检查，或表现装载对生成规则的检查。 */
export function presenterIssues(related: Issue[]): Issue[] {
  return related.filter((issue) => (issue.message ?? '').includes(PRESENTERS_FILE));
}

export function issueForField(issues: Issue[], field: string): Issue | undefined {
  return issues.find((issue) => mentionsField(issue, field));
}

export function describeSource(source: FieldSource | undefined, templateId?: string): string {
  if (!source?.sourceFile) return '没有片段写过';
  const mod = source.sourceMod ?? '';
  const colon = source.sourceFile.indexOf(':');
  const file = colon >= 0 ? source.sourceFile.slice(colon + 1) : source.sourceFile;
  const inherited = source.template && templateId && source.template !== templateId ? ` · 继承自 ${source.template}` : '';
  return `${mod} · ${file}${inherited}`;
}

export function sameValue(left: unknown, right: unknown): boolean {
  return JSON.stringify(left ?? null) === JSON.stringify(right ?? null);
}
