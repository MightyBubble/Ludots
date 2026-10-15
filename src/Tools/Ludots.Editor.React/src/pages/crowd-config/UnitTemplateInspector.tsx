import { useState } from 'react';
import { STUDIO_CHROME } from '../authoring-studio/authoringTheme';
import {
  authoredValue,
  describeSource,
  draftAgent,
  draftProfile,
  issueForField,
  issuesFor,
  presenterIssues,
  PRESENTERS_FILE,
  sameValue,
  setAgentField,
  setProfileField,
  type Issue,
  type UnitField,
  type UnitTemplatesView,
} from './unitTemplates';

type Props = {
  modId: string;
  view: UnitTemplatesView | undefined;
  draftText: string;
  issues: Issue[];
  onDraftChange: (text: string) => void;
  onDraftError: (message: string) => void;
};

function shown(value: unknown): string {
  return value === undefined || value === null ? '' : String(value);
}

export function UnitTemplateInspector({ modId, view, draftText, issues, onDraftChange, onDraftError }: Props) {
  const items = view?.items ?? [];
  const [selectedId, setSelectedId] = useState('');
  const item = items.find((candidate) => candidate.id === selectedId) ?? items[0];

  if (!view) return null;
  if (!item) {
    return (
      <div data-template-inspector="" className="mt-4 rounded-xl border border-studio-elevated bg-studio-surface p-4 text-xs text-studio-muted">
        加载序里没有带 {view.component} 的模板。
      </div>
    );
  }

  const component = view.component;
  const draft = draftAgent(draftText, component, item.id) ?? {};
  const profileId = shown(draft.profileId ?? item.agent.profileId);
  const profile = item.profile && item.profile.id === profileId ? item.profile : null;
  const profileDraft = profileId ? draftProfile(draftText, profileId) : undefined;
  const related = issuesFor(issues, item.id, profileId);
  const presenterErrors = presenterIssues(related);
  const otherIssues = related.filter((issue) => !presenterErrors.includes(issue));

  function edit(apply: () => string) {
    try {
      onDraftChange(apply());
    } catch (err) {
      onDraftError(err instanceof Error ? err.message : '草稿写不回去');
    }
  }

  function options(field: UnitField): string[] {
    if (field.reference === 'agentTypes') return view?.agentTypes ?? [];
    if (field.reference === 'agentProfiles') return (view?.agentProfiles ?? []).map((option) => option.id);
    return [];
  }

  function hint(field: string) {
    const issue = issueForField(related, field);
    if (!issue) return null;
    return (
      <span data-field-error={field} className="mt-1 block rounded border border-studio-red/40 bg-studio-red/10 px-2 py-1 text-xs text-studio-red">
        {issue.message}
        {issue.sourceMod || issue.sourceFile ? (
          <span className="mt-0.5 block font-mono text-[11px] text-studio-secondary">
            {issue.sourceMod ?? ''} {issue.sourceFile ?? ''}
          </span>
        ) : null}
      </span>
    );
  }

  return (
    <div data-template-inspector="" className="mt-4 rounded-xl border border-studio-elevated bg-studio-surface p-4">
      <div className="flex flex-wrap items-end gap-3">
        <h2 className="text-sm font-semibold">单位模板</h2>
        <label className={STUDIO_CHROME.label}>
          模板
          <select
            data-template-select=""
            className={STUDIO_CHROME.field}
            value={item.id}
            onChange={(event) => setSelectedId(event.target.value)}
          >
            {items.map((candidate) => {
              const count = issuesFor(issues, candidate.id, shown(candidate.agent.profileId)).length;
              return (
                <option key={candidate.id} value={candidate.id}>
                  {candidate.id}
                  {count > 0 ? ` · ${count} 个错误` : ''}
                </option>
              );
            })}
          </select>
        </label>
      </div>
      <p className="mt-2 text-xs text-studio-muted" data-template-owner="">
        {item.ownedBySelection
          ? `${modId} 自己写了这个模板，保存改的是 ${modId} 的文件。`
          : `这个模板只在依赖里定义。保存时只把改动的键写进 ${modId} 的 Entities/templates.json，依赖的文件不动。`}
        {item.inherits.length > 0 ? ` 继承：${item.inherits.join('、')}。` : ''}
        {' '}数值由装载器校验，没通过不写文件。
      </p>

      <table className="mt-3 w-full text-left text-xs">
        <thead>
          <tr className="text-studio-muted">
            <th className="py-1 pr-3 font-normal">{component}</th>
            <th className="py-1 pr-3 font-normal">值</th>
            <th className="py-1 font-normal">来源</th>
          </tr>
        </thead>
        <tbody>
          {view.fields.map((field) => {
            const value = field.name in draft ? draft[field.name] : item.agent[field.name];
            const changed = !sameValue(value, item.agent[field.name]);
            const listId = `unit-field-${field.name}`;
            const choices = options(field);
            return (
              <tr key={field.name} className="border-t border-studio-elevated align-top">
                <td className="py-1.5 pr-3 font-mono">{field.name}</td>
                <td className="py-1.5 pr-3">
                  {field.kind === 'bool' ? (
                    <input
                      type="checkbox"
                      data-unit-field={field.name}
                      checked={value === true}
                      onChange={(event) =>
                        edit(() => setAgentField(draftText, component, item.id, field.name, authoredValue('bool', event.target.checked)))
                      }
                    />
                  ) : (
                    <>
                      <input
                        data-unit-field={field.name}
                        className={STUDIO_CHROME.field}
                        list={choices.length > 0 ? listId : undefined}
                        value={shown(value)}
                        onChange={(event) =>
                          edit(() => setAgentField(draftText, component, item.id, field.name, authoredValue(field.kind, event.target.value)))
                        }
                      />
                      {choices.length > 0 ? (
                        <datalist id={listId}>
                          {choices.map((choice) => (
                            <option key={choice} value={choice} />
                          ))}
                        </datalist>
                      ) : null}
                    </>
                  )}
                  {hint(field.name)}
                </td>
                <td className="py-1.5 font-mono text-studio-secondary" data-field-source={field.name}>
                  {changed ? <span className="mr-1 rounded bg-studio-yellow/20 px-1 text-studio-yellow">草稿</span> : null}
                  {describeSource(item.fields[field.name], item.id)}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      <h3 className="mt-4 text-xs font-semibold text-studio-secondary">体型档案 {profileId || '（没有）'}</h3>
      {profileDraft ? (
        <>
          <p className="mt-1 text-xs text-studio-muted">
            半径级就是档案的 radiusCm。在 Navigation/agent_profiles.json，导航能力档 {shown(profileDraft.navProfileId)}。
            {profile ? ` 用这份档案的模板：${profile.usedBy.join('、')}。` : ' 换了档案，来源在保存后显示。'}
          </p>
          <table className="mt-2 w-full text-left text-xs">
            <tbody>
              {view.profileFields.map((field) => {
                const value = profileDraft[field.name];
                const changed = profile ? !sameValue(value, profile.values[field.name]) : false;
                return (
                  <tr key={field.name} className="border-t border-studio-elevated align-top">
                    <td className="py-1.5 pr-3 font-mono">{field.name}</td>
                    <td className="py-1.5 pr-3">
                      <input
                        data-profile-field={field.name}
                        className={STUDIO_CHROME.field}
                        value={shown(value)}
                        onChange={(event) =>
                          edit(() => setProfileField(draftText, profileId, field.name, authoredValue('number', event.target.value)))
                        }
                      />
                      {hint(field.name)}
                    </td>
                    <td className="py-1.5 font-mono text-studio-secondary">
                      {changed ? <span className="mr-1 rounded bg-studio-yellow/20 px-1 text-studio-yellow">草稿</span> : null}
                      {profile ? describeSource(profile.fields[field.name]) : ''}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </>
      ) : (
        <p className="mt-1 text-xs text-studio-muted">草稿里的 profileId 不在 Navigation/agent_profiles.json，档案字段没法显示。校验结果见下方。</p>
      )}

      <h3 className="mt-4 text-xs font-semibold text-studio-secondary">表现</h3>
      {item.presenters.length > 0 ? (
        <ul className="mt-1 space-y-1 text-xs" data-presenter-links="">
          {item.presenters.map((link) => (
            <li key={`${link.ruleOwner}:${link.rule}:${link.definitionId}`} className="font-mono">
              {link.definitionId}
              <span className="text-studio-secondary">
                {' '}
                · {link.ruleOwner} {link.rule}
                {link.templateKey === item.id ? '' : ` · 键 ${link.templateKey}`} · {describeSource(link)}
              </span>
            </li>
          ))}
        </ul>
      ) : presenterErrors.length > 0 ? null : (
        <p className="mt-1 text-xs text-studio-muted" data-presenter-links="">
          {PRESENTERS_FILE} 里没有在 EntitySpawned 时为它创建表现的规则。
        </p>
      )}
      {presenterErrors.map((issue, index) => (
        <span
          key={`presenter-error-${index}`}
          data-presenter-error=""
          className="mt-1 block whitespace-pre-wrap rounded border border-studio-red/40 bg-studio-red/10 px-2 py-1 text-xs text-studio-red"
        >
          {issue.message}
          <span className="mt-0.5 block font-mono text-studio-secondary">
            {issue.sourceMod ?? ''} {issue.sourceFile ?? ''}
          </span>
        </span>
      ))}

      {otherIssues.length > 0 ? (
        <ul className="mt-4 space-y-2" data-template-issues="">
          {otherIssues.map((issue, index) => (
            <li key={`${issue.path}-${index}`} className="rounded-md border border-studio-red/40 bg-studio-red/10 px-3 py-2 text-xs">
              <span className="block">{issue.message}</span>
              <span className="mt-1 block font-mono text-studio-secondary">
                {issue.sourceMod ?? ''} {issue.sourceFile ?? ''}
              </span>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
