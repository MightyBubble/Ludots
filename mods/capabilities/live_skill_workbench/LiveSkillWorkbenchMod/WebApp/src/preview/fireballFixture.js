export function createFireballPreviewSnapshot() {
  return {
    ready: true,
    preview: true,
    connectionState: 'preview',
    modName: 'LiveSkillWorkbenchMod',
    sessionId: 'preview-session',
    revision: 0,
    stateVersion: 1,
    isDirty: false,
    hasDocument: true,
    documentSourceUri: 'fixture://preview/fireball',
    selectedCatalogId: 'ability.Fireball',
    selectedCatalogKind: 'ability',
    applyMode: 'NotClassified',
    applySupported: false,
    applyStatusLabel: '尚未预检；不会应用（预览示意，非真实运行时）',
    catalog: [
      { id: 'actor.mage', kind: 'actor', label: '法师', parentId: null, tags: ['角色'] },
      { id: 'ability.Fireball', kind: 'ability', label: '火球术', parentId: 'actor.mage', tags: ['技能'] },
      { id: 'effect.FireballDamage', kind: 'effect', label: '火球伤害', parentId: 'ability.Fireball', tags: ['效果'] },
      { id: 'graph.FireballCast', kind: 'graph', label: '火球施放图', parentId: 'ability.Fireball', tags: ['Graph'] },
      { id: 'tag.State.Burning', kind: 'tag', label: 'State.Burning', parentId: null, tags: ['标签'] },
      { id: 'attr.Health', kind: 'attribute', label: 'Health', parentId: null, tags: ['属性'] }
    ],
    fields: [
      {
        fieldPath: 'damage',
        label: '伤害',
        valueKind: 'number',
        numericValue: 50,
        baselineValue: 50,
        unit: '点',
        group: '数值',
        readOnly: false,
        min: 0,
        max: 9999,
        step: 1,
        description: '火球基础伤害（预览夹具）',
        sourceUri: 'fixture://preview/fireball/damage'
      },
      {
        fieldPath: 'manaCost',
        label: '蓝耗',
        valueKind: 'number',
        numericValue: 25,
        baselineValue: 25,
        unit: '点',
        group: '数值',
        readOnly: false,
        min: 0,
        max: 999,
        step: 1,
        description: '施放蓝耗（预览夹具）',
        sourceUri: 'fixture://preview/fireball/manaCost'
      },
      {
        fieldPath: 'cooldown',
        label: '冷却',
        valueKind: 'number',
        numericValue: 3,
        baselineValue: 3,
        unit: '秒',
        group: '时间',
        readOnly: false,
        min: 0,
        max: 120,
        step: 0.1,
        description: '冷却时间（预览夹具）',
        sourceUri: 'fixture://preview/fireball/cooldown'
      },
      {
        fieldPath: 'radius',
        label: '范围',
        valueKind: 'number',
        numericValue: 2.5,
        baselineValue: 2.5,
        unit: '米',
        group: '空间',
        readOnly: false,
        min: 0.1,
        max: 50,
        step: 0.1,
        description: '爆炸半径（预览夹具）',
        sourceUri: 'fixture://preview/fireball/radius'
      }
    ],
    changes: [],
    diagnostics: [],
    graph: {
      definitionId: 'graph.FireballCast',
      nodes: [
        { id: 'cast', label: '施放', kind: 'cast', x: 40, y: 80 },
        { id: 'query', label: '目标查询', kind: 'query', x: 220, y: 80 },
        { id: 'damage', label: '伤害效果', kind: 'effect', x: 420, y: 80 },
        { id: 'delta', label: '属性变化', kind: 'attribute', x: 620, y: 80 }
      ],
      edges: [
        { id: 'e1', source: 'cast', target: 'query', label: '提交' },
        { id: 'e2', source: 'query', target: 'damage', label: '目标' },
        { id: 'e3', source: 'damage', target: 'delta', label: '生命' }
      ]
    },
    effectChain: [
      { id: 'evt.1', phase: '施放', label: '开始施放', definitionId: 'ability.Fireball', detail: '火球术', sequence: 1 },
      { id: 'evt.2', phase: '查询', label: '目标查询完成', definitionId: 'graph.FireballCast', detail: '1 个目标', sequence: 2 },
      { id: 'evt.3', phase: '效果', label: '请求伤害效果', definitionId: 'effect.FireballDamage', detail: '等待生效', sequence: 3 },
      { id: 'evt.4', phase: '属性', label: '属性变化', definitionId: 'attr.Health', detail: '-50（基线）', sequence: 4 }
    ],
    unavailableActions: [
      { actionId: 'undo', label: '撤销', reason: '会话撤销栈尚未接入。' },
      { actionId: 'redo', label: '重做', reason: '会话重做栈尚未接入。' },
      { actionId: 'aiDraft', label: 'AI 生成', reason: 'AI 草稿尚未接入（#623）。' },
      { actionId: 'saveMod', label: '保存 Mod', reason: '草稿落盘尚未接入（#624）。' }
    ],
    error: null
  };
}

export function applyPreviewStageEdit(snapshot, edit) {
  if (!snapshot?.preview) {
    throw new Error('Preview stage edits require an explicit preview snapshot.');
  }

  const fields = (snapshot.fields ?? []).map((field) => {
    if (field.fieldPath !== edit.fieldPath) {
      return field;
    }
    return { ...field, numericValue: edit.numericValue };
  });

  const existing = (snapshot.changes ?? []).filter(
    (change) => !(change.definitionId === edit.definitionId && change.fieldPath === edit.fieldPath)
  );
  const baseline = snapshot.fields?.find((field) => field.fieldPath === edit.fieldPath)?.baselineValue ?? null;
  const changes = [
    ...existing,
    {
      definitionId: edit.definitionId,
      fieldPath: edit.fieldPath,
      beforeValue: baseline,
      afterValue: edit.numericValue,
      applyMode: 'NotClassified'
    }
  ];

  return {
    ...snapshot,
    revision: Number(snapshot.revision ?? 0) + 1,
    stateVersion: Number(snapshot.stateVersion ?? 0) + 1,
    isDirty: true,
    fields,
    changes,
    diagnostics: [],
    applyMode: 'NotClassified',
    applySupported: false,
    applyStatusLabel: snapshot.applyStatusLabel || '尚未预检；不会应用（预览示意，非真实运行时）'
  };
}
