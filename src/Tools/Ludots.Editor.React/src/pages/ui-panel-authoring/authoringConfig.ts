import type { PanelTemplate, SourceKind, SurfaceKind } from './model';

/** Formal authoring config shape (runtime-facing contract sample). */
export type PanelAuthoringConfig = {
  schema: 'ludots.ui.panel_template/v1';
  templates: PanelAuthoringTemplate[];
};

export type PanelAuthoringBinding = {
  variableId: string;
  sourceKind: string;
  graphOutputKey?: string;
  attributeId?: string;
  /** presentationToken when valueKind is Text from tag/table lookup */
  semantic?: string;
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
  bindings: PanelAuthoringBinding[];
  outputs: Array<{
    id: string;
    type: string;
    key: string;
    source: string;
  }>;
  copyTemplate: string;
};

const ATTRIBUTE_SOURCE_KINDS = new Set<SourceKind>(['singleAttribute', 'derivedAttribute']);
const GRAPH_SOURCE_KINDS = new Set<SourceKind>(['aggregateProjection', 'graphOutput']);

function nonEmpty(value: string | undefined | null): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

/**
 * Fail-closed mirror of Core PanelVariableBinding: sourceKind selects exactly one ref.
 * attributeId ↔ graphOutputKey are mutually exclusive.
 */
export function assertPanelBindingContract(binding: {
  variableId: string;
  sourceKind: string;
  attributeId?: string | null;
  graphOutputKey?: string | null;
}): void {
  const variableId = binding.variableId?.trim();
  if (!variableId) {
    throw new Error('Panel binding requires variableId.');
  }

  const sourceKind = binding.sourceKind?.trim() as SourceKind;
  if (!sourceKind) {
    throw new Error(`Binding '${variableId}' requires sourceKind.`);
  }

  const hasAttribute = nonEmpty(binding.attributeId);
  const hasGraphKey = nonEmpty(binding.graphOutputKey);

  if (ATTRIBUTE_SOURCE_KINDS.has(sourceKind)) {
    if (!hasAttribute) {
      throw new Error(
        `Binding '${variableId}' with sourceKind '${sourceKind}' requires attributeId.`,
      );
    }
    if (hasGraphKey) {
      throw new Error(
        `Binding '${variableId}' with sourceKind '${sourceKind}' must not declare graphOutputKey.`,
      );
    }
    return;
  }

  if (GRAPH_SOURCE_KINDS.has(sourceKind)) {
    if (!hasGraphKey) {
      throw new Error(
        `Binding '${variableId}' with sourceKind '${sourceKind}' requires graphOutputKey.`,
      );
    }
    if (hasAttribute) {
      throw new Error(
        `Binding '${variableId}' with sourceKind '${sourceKind}' must not declare attributeId.`,
      );
    }
    return;
  }

  throw new Error(`Binding '${variableId}' has unknown sourceKind '${sourceKind}'.`);
}

function toAuthoringBinding(
  variableId: string,
  sourceKind: SourceKind,
  attributeId: string | undefined,
  graphOutputKey: string | undefined,
  semantic: string | undefined,
): PanelAuthoringBinding {
  assertPanelBindingContract({ variableId, sourceKind, attributeId, graphOutputKey });

  const binding: PanelAuthoringBinding = { variableId, sourceKind };
  if (ATTRIBUTE_SOURCE_KINDS.has(sourceKind) && attributeId) {
    binding.attributeId = attributeId.trim();
  }
  if (GRAPH_SOURCE_KINDS.has(sourceKind) && graphOutputKey) {
    binding.graphOutputKey = graphOutputKey.trim();
  }
  if (semantic) {
    binding.semantic = semantic;
  }
  return binding;
}

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
      const sourceKind = b?.sourceKind ?? 'graphOutput';
      const semantic =
        v.valueKind === 'Text' && (v.id === 'curState' || v.id === 'lastKill')
          ? 'presentationToken'
          : undefined;
      return toAuthoringBinding(v.id, sourceKind, b?.attributeId, b?.graphOutputKey, semantic);
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

/** Validate a already-shaped ludots.ui.panel_template/v1 document (samples / pasted JSON). */
export function assertPanelAuthoringConfig(config: PanelAuthoringConfig): void {
  if (config.schema !== 'ludots.ui.panel_template/v1') {
    throw new Error(`Unsupported panel template schema '${String(config.schema)}'.`);
  }
  for (const template of config.templates ?? []) {
    for (const binding of template.bindings ?? []) {
      assertPanelBindingContract(binding);
    }
  }
}
