import assert from 'node:assert/strict';
import test from 'node:test';
import { formatApplyModeLabel, resolveFieldLabel } from './displayLabels.js';

test('formatApplyModeLabel maps NotClassified to Chinese', () => {
  assert.equal(formatApplyModeLabel('NotClassified'), '尚未分类');
  assert.equal(formatApplyModeLabel(null), '尚未分类');
  assert.equal(formatApplyModeLabel('NotSupportedYet'), '暂不支持');
});

test('resolveFieldLabel prefers descriptor label over fieldPath', () => {
  const fields = [{ fieldPath: 'damage', label: '伤害' }];
  assert.equal(resolveFieldLabel(fields, 'damage'), '伤害');
  assert.equal(resolveFieldLabel(fields, 'unknown'), 'unknown');
});
