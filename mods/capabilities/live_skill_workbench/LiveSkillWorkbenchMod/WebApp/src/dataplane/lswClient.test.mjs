import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { isExplicitPreviewMode, resolveWorkbenchBootMode } from './lswClient.js';

describe('lswClient boot mode', () => {
  it('requires explicit preview query', () => {
    assert.equal(isExplicitPreviewMode(''), false);
    assert.equal(isExplicitPreviewMode('?foo=1'), false);
    assert.equal(isExplicitPreviewMode('?preview=1'), true);
    assert.equal(isExplicitPreviewMode('?preview=true'), true);
  });

  it('fails visibly when host is missing outside preview', () => {
    const boot = resolveWorkbenchBootMode({ root: { location: { search: '' } } });
    assert.equal(boot.mode, 'missing-host');
    assert.match(boot.error, /window\.ludotsDataplane/);
  });

  it('selects preview without installing a fake host transport', () => {
    const boot = resolveWorkbenchBootMode({
      root: { location: { search: '?preview=1' } },
      search: '?preview=1'
    });
    assert.equal(boot.mode, 'preview');
    assert.equal(boot.preview, true);
    assert.equal(boot.hostPresent, false);
  });

  it('uses host mode when ludotsDataplane exists', () => {
    const boot = resolveWorkbenchBootMode({
      root: {
        location: { search: '' },
        ludotsDataplane: { postMessage() {} }
      }
    });
    assert.equal(boot.mode, 'host');
    assert.equal(boot.hostPresent, true);
  });
});
