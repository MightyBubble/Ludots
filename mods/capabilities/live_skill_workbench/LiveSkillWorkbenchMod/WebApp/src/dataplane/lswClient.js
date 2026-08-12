export const LSW_TOPIC = 'ludots.capability.liveSkillWorkbench.session';
export const LSW_COMMANDS = Object.freeze({
  stageEdit: 'lsw.stageEdit',
  discardEdits: 'lsw.discardEdits',
  selectCatalogItem: 'lsw.selectCatalogItem',
  precheck: 'lsw.precheck',
  applyNextCast: 'lsw.applyNextCast'
});

export function isExplicitPreviewMode(search = globalThis.location?.search ?? '') {
  const params = new URLSearchParams(search.startsWith('?') ? search : `?${search}`);
  const value = params.get('preview');
  return value === '1' || value === 'true';
}

export function resolveWorkbenchBootMode(options = {}) {
  const root = options.root ?? globalThis;
  const search = options.search ?? root.location?.search ?? '';
  const preview = isExplicitPreviewMode(search);

  if (preview) {
    return { mode: 'preview', preview: true, hostPresent: Boolean(root.ludotsDataplane) };
  }

  if (!root.ludotsDataplane) {
    return {
      mode: 'missing-host',
      preview: false,
      hostPresent: false,
      error: '无法连接真实游戏宿主：window.ludotsDataplane 不存在。请在 Ludots 宿主中打开，或使用 ?preview=1 进入显式预览。'
    };
  }

  return { mode: 'host', preview: false, hostPresent: true };
}
