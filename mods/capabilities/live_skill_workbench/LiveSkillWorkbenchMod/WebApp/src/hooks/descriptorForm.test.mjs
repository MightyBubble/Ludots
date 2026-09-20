import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { buildDraftValues, collectDirtyEdits, groupFieldsByGroup } from '../hooks/descriptorForm.js';

describe('descriptor-driven form helpers', () => {
  const fields = [
    { fieldPath: 'damage', label: '伤害', numericValue: 50, baselineValue: 50, group: '数值', min: 0, max: 999 },
    { fieldPath: 'cooldown', label: '冷却', numericValue: 3, baselineValue: 3, group: '时间' },
    {
      fieldPath: 'arcaneChargeDensity',
      label: '奥术充能密度',
      numericValue: 1,
      baselineValue: 1,
      group: '数值',
      min: 0.5,
      max: 4,
      step: 0.25,
      sourceUri: 'fixture://custom/arcaneChargeDensity'
    }
  ];

  it('builds drafts from arbitrary field paths without special-casing names', () => {
    const draft = buildDraftValues(fields);
    assert.deepEqual(draft, { damage: 50, cooldown: 3, arcaneChargeDensity: 1 });
  });

  it('collects only dirty finite edits for the selected definition', () => {
    const { edits, validationErrors } = collectDirtyEdits(
      fields,
      { damage: 80, cooldown: 3, arcaneChargeDensity: 2 },
      'ability.CustomBolt'
    );
    assert.equal(validationErrors.length, 0);
    assert.equal(edits.length, 2);
    assert.deepEqual(
      edits.map((edit) => edit.fieldPath).sort(),
      ['arcaneChargeDensity', 'damage']
    );
    assert.ok(edits.every((edit) => edit.definitionId === 'ability.CustomBolt'));
    assert.equal(
      edits.find((edit) => edit.fieldPath === 'arcaneChargeDensity').sourceUri,
      'fixture://custom/arcaneChargeDensity'
    );
  });

  it('reports invalid non-finite drafts instead of silently skipping them', () => {
    const { edits, validationErrors } = collectDirtyEdits(
      fields,
      { damage: 'not-a-number', cooldown: 3, arcaneChargeDensity: 1 },
      'ability.CustomBolt'
    );
    assert.equal(edits.length, 0);
    assert.equal(validationErrors.length, 1);
    assert.equal(validationErrors[0].fieldPath, 'damage');
    assert.match(validationErrors[0].message, /非有限数字|无效/);
  });

  it('enforces generic min/max on arbitrary custom field names', () => {
    const { edits, validationErrors } = collectDirtyEdits(
      fields,
      { damage: 50, cooldown: 3, arcaneChargeDensity: 9 },
      'ability.CustomBolt'
    );
    assert.equal(edits.length, 0);
    assert.equal(validationErrors.length, 1);
    assert.equal(validationErrors[0].fieldPath, 'arcaneChargeDensity');
    assert.match(validationErrors[0].message, /不能大于/);
  });

  it('groups fields by descriptor group metadata', () => {
    const groups = groupFieldsByGroup(fields);
    assert.equal(groups.length, 2);
    assert.equal(groups[0][0], '数值');
    assert.equal(groups[0][1].length, 2);
  });
});
