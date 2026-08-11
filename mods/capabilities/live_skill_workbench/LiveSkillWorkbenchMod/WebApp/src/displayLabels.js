/** Visible Chinese labels for Data Plane apply-mode codes. Codes stay unchanged in payloads. */
const APPLY_MODE_LABELS = Object.freeze({
  NotClassified: '尚未分类',
  NotSupportedYet: '暂不支持'
});

/**
 * @param {string | null | undefined} applyMode
 * @returns {string}
 */
export function formatApplyModeLabel(applyMode) {
  if (!applyMode) {
    return APPLY_MODE_LABELS.NotClassified;
  }
  return APPLY_MODE_LABELS[applyMode] ?? applyMode;
}

/**
 * Resolve a field descriptor label for a change row.
 * @param {{ fieldPath?: string, label?: string }[] | null | undefined} fields
 * @param {string | null | undefined} fieldPath
 * @returns {string}
 */
export function resolveFieldLabel(fields, fieldPath) {
  if (!fieldPath) {
    return '—';
  }
  const match = (fields ?? []).find((field) => field.fieldPath === fieldPath);
  return match?.label || fieldPath;
}
