import { memo } from 'react';
import { groupFieldsByGroup } from '../hooks/descriptorForm.js';

function NumericFormComponent({
  fields = [],
  draftValues,
  onChange,
  onStage,
  selectedId,
  validationErrors = []
}) {
  const groups = groupFieldsByGroup(fields);
  const errorByPath = new Map(
    (validationErrors ?? []).map((error) => [error.fieldPath, error.message])
  );

  if (!selectedId) {
    return <div className="lsw-empty">从左侧目录选择一个条目。</div>;
  }

  if (groups.length === 0) {
    return <div className="lsw-empty">当前条目没有可编辑数值字段。</div>;
  }

  return (
    <div className="lsw-numeric">
      {groups.map(([groupName, groupFields]) => (
        <section key={groupName} className="lsw-numeric__group">
          <header>{groupName}</header>
          <div className="lsw-numeric__grid">
            {groupFields.map((field) => {
              const draft = draftValues[field.fieldPath];
              const dirty = Number(draft) !== Number(field.numericValue);
              const localError = errorByPath.get(field.fieldPath);
              return (
                <label key={field.fieldPath} className={dirty || localError ? 'is-dirty' : ''}>
                  <span className="lsw-numeric__label">
                    {field.label}
                    {field.unit ? <small>{field.unit}</small> : null}
                  </span>
                  <input
                    type="number"
                    value={draft ?? ''}
                    min={field.min ?? undefined}
                    max={field.max ?? undefined}
                    step={field.step ?? undefined}
                    disabled={field.readOnly}
                    aria-invalid={Boolean(localError)}
                    title={field.description || undefined}
                    onChange={(event) => onChange(field.fieldPath, event.target.value === '' ? '' : Number(event.target.value))}
                  />
                  <span className="lsw-numeric__baseline">
                    当前 {formatNumber(field.numericValue)} · 基线 {formatNumber(field.baselineValue)}
                  </span>
                  {field.description ? (
                    <span className="lsw-numeric__help">{field.description}</span>
                  ) : null}
                  {field.sourceUri ? (
                    <span className="lsw-numeric__source" title={field.sourceUri}>
                      来源 {field.sourceUri}
                    </span>
                  ) : null}
                  {localError ? (
                    <span className="lsw-numeric__error" role="alert">{localError}</span>
                  ) : null}
                </label>
              );
            })}
          </div>
        </section>
      ))}
      <div className="lsw-numeric__footer">
        <button type="button" className="lsw-btn lsw-btn--primary" onClick={onStage}>
          暂存改动
        </button>
      </div>
    </div>
  );
}

function formatNumber(value) {
  if (value == null || Number.isNaN(Number(value))) {
    return '—';
  }
  return String(value);
}

export const NumericForm = memo(NumericFormComponent);
