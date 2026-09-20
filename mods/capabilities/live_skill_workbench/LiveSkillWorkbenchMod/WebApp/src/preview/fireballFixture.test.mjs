import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { applyPreviewStageEdit, createFireballPreviewSnapshot } from './fireballFixture.js';

describe('fireball preview fixture', () => {
  it('exposes descriptor-driven fireball fields and timeline only in explicit preview', () => {
    const snapshot = createFireballPreviewSnapshot();
    assert.equal(snapshot.preview, true);
    assert.equal(snapshot.selectedCatalogId, 'ability.Fireball');
    assert.ok(snapshot.fields.some((field) => field.fieldPath === 'damage'));
    assert.equal(snapshot.effectChain.length, 4);
    assert.equal(snapshot.graph.nodes.length, 4);
    assert.equal(snapshot.applySupported, false);
    assert.equal(snapshot.applyMode, 'NotClassified');
    assert.match(snapshot.applyStatusLabel, /尚未预检/);
    assert.match(snapshot.applyStatusLabel, /预览/);
  });

  it('stages numeric edits without claiming live next-cast apply', () => {
    const staged = applyPreviewStageEdit(createFireballPreviewSnapshot(), {
      definitionId: 'ability.Fireball',
      fieldPath: 'damage',
      numericValue: 80
    });
    assert.equal(staged.revision, 1);
    assert.equal(staged.isDirty, true);
    assert.equal(staged.fields.find((field) => field.fieldPath === 'damage').numericValue, 80);
    assert.equal(staged.changes[0].beforeValue, 50);
    assert.equal(staged.changes[0].afterValue, 80);
    assert.equal(staged.changes[0].applyMode, 'NotClassified');
    assert.equal(staged.applySupported, false);
    assert.equal(staged.applyMode, 'NotClassified');
  });
});
