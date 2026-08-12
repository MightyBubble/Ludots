import type { PanelTemplate, SurfaceKind } from './model';

/** Formal authoring config shape (runtime-facing contract sample). */
export type PanelAuthoringConfig = {
  schema: 'ludots.ui.panel_template/v1';
  templates: PanelAuthoringTemplate[];
};

export type PanelAuthoringTemplate = {
  templateId: string;
  label: string;
  blurb: string;
  surfaceKind: SurfaceKind;
  defaultGraphId: string;
  variables: Array<{
    variableId: string;
    label: string;
    valueKind: string;
  }>;
  bindings: Array<{
    variableId: string;
    sourceKind: string;
    graphOutputKey?: string;
    attributeId?: string;
    /** presentationToken when valueKind is Text from tag/table lookup */
    semantic?: string;
  }>;
  outputs: Array<{
    id: string;
    type: string;
    key: string;
    source: string;
  }>;
  copyTemplate: string;
};

export function toAuthoringTemplate(tpl: PanelTemplate): PanelAuthoringTemplate {
  return {
    templateId: tpl.id,
    label: tpl.name,
    blurb: tpl.blurb,
    surfaceKind: tpl.surfaceKind,
    defaultGraphId: `graph.${tpl.id.replace(/^panel\./, '')}`,
    variables: tpl.variables.map((v) => ({
      variableId: v.id,
      label: v.label,
      valueKind: v.valueKind,
    })),
    bindings: tpl.variables.map((v) => {
      const b = tpl.bindings[v.id];
      const binding: PanelAuthoringTemplate['bindings'][number] = {
        variableId: v.id,
        sourceKind: b?.sourceKind ?? 'graphOutput',
      };
      if (b?.graphOutputKey) binding.graphOutputKey = b.graphOutputKey;
      if (b?.attributeId) binding.attributeId = b.attributeId;
      if (v.valueKind === 'Text' && (v.id === 'curState' || v.id === 'lastKill')) {
        binding.semantic = 'presentationToken';
      }
      return binding;
    }),
    outputs: tpl.variables.map((v) => {
      const b = tpl.bindings[v.id];
      return {
        id: v.id,
        type: v.valueKind === 'Text' ? 'TextToken' : v.valueKind,
        key: b?.graphOutputKey ?? `panel.${v.id}`,
        source: b?.fromNodeId ?? v.id,
      };
    }),
    copyTemplate: tpl.copyTemplate,
  };
}

export function toAuthoringConfig(templates: PanelTemplate[]): PanelAuthoringConfig {
  return {
    schema: 'ludots.ui.panel_template/v1',
    templates: templates.map(toAuthoringTemplate),
  };
}

export function authoringConfigJson(templates: PanelTemplate[], space = 2): string {
  return JSON.stringify(toAuthoringConfig(templates), null, space);
}
