#!/usr/bin/env node
/**
 * Fail-closed check for public/samples/panel_templates.json
 * (mirrors Core PanelVariableBinding sourceKind ↔ ref exclusivity).
 */
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ATTRIBUTE_KINDS = new Set(['singleAttribute', 'derivedAttribute']);
const GRAPH_KINDS = new Set(['aggregateProjection', 'graphOutput']);

function nonEmpty(value) {
  return typeof value === 'string' && value.trim().length > 0;
}

function assertBinding(binding, path) {
  const variableId = binding?.variableId?.trim();
  if (!variableId) {
    throw new Error(`${path}: variableId is required.`);
  }
  const sourceKind = binding?.sourceKind?.trim();
  if (!sourceKind) {
    throw new Error(`${path}: sourceKind is required.`);
  }
  const hasAttribute = nonEmpty(binding.attributeId);
  const hasGraphKey = nonEmpty(binding.graphOutputKey);

  if (ATTRIBUTE_KINDS.has(sourceKind)) {
    if (!hasAttribute) {
      throw new Error(`${path}: sourceKind '${sourceKind}' requires attributeId.`);
    }
    if (hasGraphKey) {
      throw new Error(`${path}: sourceKind '${sourceKind}' must not declare graphOutputKey.`);
    }
    return;
  }

  if (GRAPH_KINDS.has(sourceKind)) {
    if (!hasGraphKey) {
      throw new Error(`${path}: sourceKind '${sourceKind}' requires graphOutputKey.`);
    }
    if (hasAttribute) {
      throw new Error(`${path}: sourceKind '${sourceKind}' must not declare attributeId.`);
    }
    return;
  }

  throw new Error(`${path}: unknown sourceKind '${sourceKind}'.`);
}

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const samplePath = join(root, 'public/samples/panel_templates.json');
const config = JSON.parse(readFileSync(samplePath, 'utf8'));

if (config.schema !== 'ludots.ui.panel_template/v1') {
  throw new Error(`Unsupported schema '${config.schema}'.`);
}

for (const [ti, template] of (config.templates ?? []).entries()) {
  for (const [bi, binding] of (template.bindings ?? []).entries()) {
    assertBinding(binding, `templates[${ti}].bindings[${bi}] (${binding?.variableId ?? '?'})`);
  }
}

console.log(`OK: ${samplePath} bindings match PanelVariableBinding contract.`);
