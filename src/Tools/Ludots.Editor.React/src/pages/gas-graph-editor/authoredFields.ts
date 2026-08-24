export type AuthoredFieldKey =
  | 'intValue'
  | 'floatValue'
  | 'boolValue'
  | 'panelType'
  | 'panelAnchor'
  | 'panelSkin'
  | 'panelZOrder'
  | 'template'
  | 'var'
  | 'attribute'
  | 'tag'
  | 'blackboardKey'
  | 'configKey'
  | 'lookupTable'
  | 'lookupField'
  | 'teamId';

export type AuthoredFieldKind = 'string' | 'int' | 'float' | 'bool' | 'anchor';

export type AuthoredFieldSpec = {
  key: AuthoredFieldKey;
  label: string;
  kind: AuthoredFieldKind;
};

const intValue: AuthoredFieldSpec = { key: 'intValue', label: 'Value', kind: 'int' };
const floatValue: AuthoredFieldSpec = { key: 'floatValue', label: 'Value', kind: 'float' };
const boolValue: AuthoredFieldSpec = { key: 'boolValue', label: 'Value', kind: 'bool' };
const panelType: AuthoredFieldSpec = { key: 'panelType', label: 'Panel', kind: 'string' };
const panelAnchor: AuthoredFieldSpec = { key: 'panelAnchor', label: 'Anchor', kind: 'anchor' };
const panelSkin: AuthoredFieldSpec = { key: 'panelSkin', label: 'Skin', kind: 'string' };
const panelZOrder: AuthoredFieldSpec = { key: 'panelZOrder', label: 'Z order', kind: 'float' };
const template: AuthoredFieldSpec = { key: 'template', label: 'Template', kind: 'string' };
const varName: AuthoredFieldSpec = { key: 'var', label: 'Variable', kind: 'string' };
const attribute: AuthoredFieldSpec = { key: 'attribute', label: 'Attribute', kind: 'string' };
const tag: AuthoredFieldSpec = { key: 'tag', label: 'Tag', kind: 'string' };
const blackboardKey: AuthoredFieldSpec = { key: 'blackboardKey', label: 'Blackboard key', kind: 'string' };
const configKey: AuthoredFieldSpec = { key: 'configKey', label: 'Config key', kind: 'string' };
const lookupTable: AuthoredFieldSpec = { key: 'lookupTable', label: 'Lookup table', kind: 'string' };
const lookupField: AuthoredFieldSpec = { key: 'lookupField', label: 'Lookup field', kind: 'string' };
const teamId: AuthoredFieldSpec = { key: 'teamId', label: 'Team', kind: 'int' };

const FIELDS: Record<string, AuthoredFieldSpec[]> = {
  ConstInt: [intValue],
  ConstFloat: [floatValue],
  ConstBool: [boolValue],
  CreatePanel: [panelType, panelAnchor, panelSkin, panelZOrder],
  ShowPanel: [panelType],
  HidePanel: [panelType],
  DestroyPanel: [panelType],
  SpawnTemplate: [template],
  ReadMapVarInt: [varName],
  ReadMapVarFloat: [varName],
  WriteMapVarInt: [varName],
  WriteMapVarFloat: [varName],
  HasTag: [tag],
  SendEvent: [tag],
  LoadAttribute: [attribute],
  LoadSelfAttribute: [attribute],
  WriteSelfAttribute: [attribute],
  ModifyAttributeAdd: [attribute],
  ReadBlackboardInt: [blackboardKey],
  ReadBlackboardFloat: [blackboardKey],
  ReadBlackboardEntity: [blackboardKey],
  WriteBlackboardInt: [blackboardKey],
  WriteBlackboardFloat: [blackboardKey],
  WriteBlackboardEntity: [blackboardKey],
  LoadConfigFloat: [configKey],
  LoadConfigInt: [configKey],
  LoadConfigEffectId: [configKey],
  ResolveTableRow: [lookupTable],
  TableReadInt: [lookupTable, lookupField],
  TableReadFloat: [lookupTable, lookupField],
  QueryFilterTeam: [teamId],
};

export function authoredFieldsForOp(op: string): AuthoredFieldSpec[] {
  return FIELDS[op] ?? [];
}
