/**
 * Descriptor-driven numeric draft helpers.
 * Components must not special-case gameplay field names.
 */

export function buildDraftValues(fields = []) {
  const draft = {};
  for (const field of fields) {
    if (!field?.fieldPath) {
      continue;
    }
    draft[field.fieldPath] = field.numericValue ?? field.baselineValue ?? '';
  }
  return draft;
}

/**
 * Collects dirty edits and explicit validation errors.
 * Invalid/non-finite drafts are reported, never silently skipped.
 * @returns {{ edits: object[], validationErrors: { fieldPath: string, message: string }[] }}
 */
export function collectDirtyEdits(fields = [], draftValues = {}, definitionId) {
  const edits = [];
  const validationErrors = [];

  if (!definitionId) {
    return { edits, validationErrors };
  }

  for (const field of fields) {
    if (!field?.fieldPath || field.readOnly) {
      continue;
    }

    const raw = draftValues[field.fieldPath];
    if (raw === '' || raw === undefined || raw === null) {
      const current = field.numericValue;
      if (current == null) {
        continue;
      }
      validationErrors.push({
        fieldPath: field.fieldPath,
        message: `字段「${field.label || field.fieldPath}」需要有效数值，不能为空。`
      });
      continue;
    }

    const numericValue = typeof raw === 'number' ? raw : Number(raw);
    if (!Number.isFinite(numericValue)) {
      validationErrors.push({
        fieldPath: field.fieldPath,
        message: `字段「${field.label || field.fieldPath}」的值无效（非有限数字）。`
      });
      continue;
    }

    if (field.min != null && Number.isFinite(Number(field.min)) && numericValue < Number(field.min)) {
      validationErrors.push({
        fieldPath: field.fieldPath,
        message: `字段「${field.label || field.fieldPath}」不能小于 ${field.min}。`
      });
      continue;
    }

    if (field.max != null && Number.isFinite(Number(field.max)) && numericValue > Number(field.max)) {
      validationErrors.push({
        fieldPath: field.fieldPath,
        message: `字段「${field.label || field.fieldPath}」不能大于 ${field.max}。`
      });
      continue;
    }

    const current = field.numericValue;
    if (current === numericValue) {
      continue;
    }

    edits.push({
      definitionId,
      fieldPath: field.fieldPath,
      numericValue,
      sourceUri: field.sourceUri || `workbench://${definitionId}/${field.fieldPath}`
    });
  }

  return { edits, validationErrors };
}

export function groupFieldsByGroup(fields = []) {
  const groups = new Map();
  for (const field of fields) {
    const key = field.group || '字段';
    if (!groups.has(key)) {
      groups.set(key, []);
    }
    groups.get(key).push(field);
  }
  return [...groups.entries()];
}
