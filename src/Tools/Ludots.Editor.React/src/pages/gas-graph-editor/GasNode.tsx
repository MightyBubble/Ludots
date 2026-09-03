import React from 'react';
import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';
import { pickLiveValueLabel } from './liveVisualDebug';

export type GasNodeViewEntry = {
  label: string;
  /** Lifecycle / custom event key. Mutually exclusive with action. */
  event?: string;
  /** Input action id (action-bound entry). Mutually exclusive with event. */
  action?: string;
  start: string;
  once?: boolean;
  refire?: string | null;
  filters?: {
    region?: string | null;
    tag?: string | null;
    team?: number | null;
    threshold?: number | null;
    direction?: string | null;
    action?: string | null;
    instanceId?: string | null;
    varName?: string | null;
  } | null;
};

export type EventSchemaParam = {
  name: string;
  type: 'Entity' | 'Int' | 'Float' | 'String';
  key: string;
  optional: boolean;
};

export type EventSchemaView = {
  name: string;
  scope: string;
  parameters: EventSchemaParam[];
};

export type GasNodeViewData = {
  id: string;
  op: string;
  role?: 'op' | 'event-entry';
  entry?: GasNodeViewEntry;
  intValue?: number;
  floatValue?: number;
  boolValue?: boolean;
  text?: string | null;
  textKey?: string | null;
  presentationSurface?: string | null;
  decoratorKind?: string | null;
  var?: string | null;
  template?: string | null;
  panelType?: string | null;
  event?: string | null;
  argKey?: string | null;
  entryLabel?: string | null;
  schema?: EventSchemaView | null;
  descriptor?: {
    linearInputPorts: string[];
    queryInputPorts: string[];
    scriptInputPorts: string[];
    queryOutputType: string;
    linearOutputType: string;
  };
  sugar?: { valueInputPorts: string[] };
  controlOutputPorts?: string[];
  liveDebug?: {
    intensity: number;
    current: boolean;
    pins: { pinIndex: number; value: string }[];
  };
};

export function isPureValueOp(op: string): boolean {
  return op === 'ConstInt' || op === 'ConstFloat' || op === 'ConstBool' || op === 'ConstText' || op === 'LoadTextKey';
}

function literalText(data: GasNodeViewData): string | null {
  if (data.op === 'ConstInt') return String(data.intValue ?? 0);
  if (data.op === 'ConstFloat') return String(data.floatValue ?? 0);
  if (data.op === 'ConstBool') return data.boolValue ? 'true' : 'false';
  if (data.op === 'ConstText' || data.op === 'FormatText') return data.text ?? '';
  if (data.op === 'LoadTextKey') return data.textKey ?? '';
  return null;
}

function authoredCaption(data: GasNodeViewData): string | null {
  if (data.textKey) return data.textKey;
  if (data.var) return data.var;
  if (data.template) return data.template;
  if (data.panelType) return data.panelType;
  if (data.event) return data.event;
  if (data.argKey) return `arg ${data.argKey}`;
  if (data.op === 'InvokeGraph' && data.entryLabel) return `@${data.entryLabel}`;
  if (data.op === 'HaltReturnInt') return 'end this run';
  if (data.op === 'Yield') return 'wait one tick';
  if (data.decoratorKind) return data.decoratorKind;
  return null;
}

function collectInputPorts(data: GasNodeViewData): string[] {
  return Array.from(new Set([
    ...(data.descriptor?.linearInputPorts ?? []),
    ...(data.descriptor?.queryInputPorts ?? []),
    ...(data.descriptor?.scriptInputPorts ?? []),
    ...(data.sugar?.valueInputPorts ?? []),
  ]));
}

function outputPorts(data: GasNodeViewData): { id: string; label: string; kind: 'exec' | 'value' | 'list' }[] {
  const ports: { id: string; label: string; kind: 'exec' | 'value' | 'list' }[] = [];
  if (!isPureValueOp(data.op)) {
    for (const port of data.controlOutputPorts ?? []) {
      ports.push({ id: port, label: port === 'next' ? 'Then' : port, kind: 'exec' });
    }
  }
  const outputType = data.descriptor?.queryOutputType !== 'Void'
    ? data.descriptor?.queryOutputType
    : data.descriptor?.linearOutputType;
  if (outputType && outputType !== 'Void' && outputType !== 'TargetList') {
    ports.push({ id: 'value', label: 'Value', kind: 'value' });
  }
  if (outputType === 'TargetList') {
    ports.push({ id: 'list', label: 'List', kind: 'list' });
  }
  return ports;
}

function filterChips(entry?: GasNodeViewEntry): string[] {
  if (!entry?.filters) return [];
  const chips: string[] = [];
  const filters = entry.filters;
  if (filters.instanceId) chips.push(`@${filters.instanceId}`);
  if (filters.varName) chips.push(`$${filters.varName}`);
  if (filters.region) chips.push(filters.region);
  if (filters.action) chips.push(filters.action);
  if (filters.tag) chips.push(filters.tag);
  if (filters.team != null) chips.push(`team ${filters.team}`);
  if (filters.direction) chips.push(filters.direction);
  if (filters.threshold != null) chips.push(`< ${filters.threshold}`);
  if (entry.once) chips.push('once');
  if (entry.refire) chips.push(entry.refire);
  return chips;
}

function pinKindClass(kind: 'exec' | 'value' | 'list'): string {
  if (kind === 'value') return 'gas-pin-kind-value';
  if (kind === 'list') return 'gas-pin-kind-list';
  return 'gas-pin-kind-exec';
}

function liveShellClass(data: GasNodeViewData, selected: boolean): string {
  const parts = ['gas-node'];
  if (selected) parts.push('is-selected');
  const live = data.liveDebug;
  if (live?.current) parts.push('is-live-current');
  else if (live && live.intensity > 0.66) parts.push('is-live-hot');
  else if (live && live.intensity > 0) parts.push('is-live-trail');
  return parts.join(' ');
}

function liveHeaderBadge(data: GasNodeViewData): React.ReactNode {
  if (!data.liveDebug) return null;
  if (data.liveDebug.current) return <span className="gas-live-badge">NOW</span>;
  if (data.liveDebug.intensity > 0.66) return <span className="gas-live-badge gas-live-badge-hot">HOT</span>;
  if (data.liveDebug.intensity > 0) return <span className="gas-live-badge gas-live-badge-trail">RUN</span>;
  return null;
}

function PortLiveValue({ value }: { value: string | null }) {
  if (!value) return null;
  return <span className="gas-port-live-value">{value}</span>;
}

/** Extra pin chips only when values are not already shown beside ports. */
function LivePinStrip({
  pins,
  shownOnPorts,
}: {
  pins: { pinIndex: number; value: string }[];
  shownOnPorts: boolean;
}) {
  if (pins.length === 0 || shownOnPorts) return null;
  return (
    <div className="gas-live-pin-strip">
      {pins.map((pin) => (
        <span key={pin.pinIndex} className="gas-live-pin-chip">
          [{pin.pinIndex}] {pin.value}
        </span>
      ))}
    </div>
  );
}

export function GasNode({ data, selected }: NodeProps<Node<GasNodeViewData>>) {
  const isEvent = data.role === 'event-entry';
  const inputs = collectInputPorts(data);
  const outputs = outputPorts(data);
  const livePins = data.liveDebug?.pins ?? [];
  const primaryLive = pickLiveValueLabel(livePins, 'value');

  if (isEvent) {
    const params = data.schema?.parameters.filter((param) => param.type !== 'String') ?? [];
    return (
      <div className={`${liveShellClass(data, selected)} gas-node--event`}>
        <div className="gas-node__header">
          <div className="gas-node__eyebrow-row">
            <div className="gas-node__eyebrow">{data.entry?.action ? 'Action' : 'Event'}</div>
            {liveHeaderBadge(data)}
          </div>
          <div className="gas-node__title">{data.entry?.event ?? data.entry?.action ?? 'Entry'}</div>
        </div>
        <div className="gas-node__body">
          <div className="gas-node__caption">{data.entry?.label}</div>
          {filterChips(data.entry).length > 0 ? (
            <div className="gas-node__chips">
              {filterChips(data.entry).map((chip) => (
                <span key={chip} className="gas-node__chip">{chip}</span>
              ))}
            </div>
          ) : null}
          <div className="gas-node__ports gas-node__ports--stack">
            <div className={`gas-port gas-port--out ${pinKindClass('exec')}`}>
              <Handle
                id="owner"
                type="source"
                position={Position.Right}
                className={`gas-pin gas-pin-right ${pinKindClass('exec')}`}
              />
              owner (mount)
            </div>
            <div className={`gas-port gas-port--out ${pinKindClass('exec')}`}>
              <Handle
                id="caster"
                type="source"
                position={Position.Right}
                className={`gas-pin gas-pin-right ${pinKindClass('exec')}`}
              />
              caster (event actor)
            </div>
            {params.map((param) => (
              <div key={param.key} className={`gas-port gas-port--out ${pinKindClass('value')}`}>
                <Handle
                  id={`payload:${param.key}`}
                  type="source"
                  position={Position.Right}
                  className={`gas-pin gas-pin-right ${pinKindClass('value')}`}
                />
                {param.name}
                <span className="gas-port__type">{param.type}</span>
              </div>
            ))}
          </div>
          <div className={`gas-port gas-port--out gas-port--then ${pinKindClass('exec')}`}>
            Then
            <Handle
              id="exec"
              type="source"
              position={Position.Right}
              className={`gas-pin gas-pin-right ${pinKindClass('exec')}`}
            />
          </div>
          <LivePinStrip pins={livePins} shownOnPorts={false} />
        </div>
      </div>
    );
  }

  if (isPureValueOp(data.op)) {
    const liveBeside = primaryLive;
    return (
      <div className={`${liveShellClass(data, selected)} gas-node--value`}>
        <div className="gas-node__header">
          <div className="gas-node__eyebrow-row">
            <div className="gas-node__title">{data.op}</div>
            {liveHeaderBadge(data)}
          </div>
        </div>
        <div className="gas-node__body">
          <div className="gas-node__literal">{literalText(data)}</div>
          <div className={`gas-port gas-port--out ${pinKindClass('value')}`}>
            Value
            <PortLiveValue value={liveBeside} />
            <Handle
              id="value"
              type="source"
              position={Position.Right}
              className={`gas-pin gas-pin-right ${pinKindClass('value')}`}
            />
          </div>
          <LivePinStrip pins={livePins} shownOnPorts={Boolean(liveBeside)} />
        </div>
      </div>
    );
  }

  const valueOut = outputs.find((port) => port.kind === 'value' || port.kind === 'list');
  const liveBeside = valueOut ? pickLiveValueLabel(livePins, valueOut.id) : null;

  return (
    <div className={`${liveShellClass(data, selected)} gas-node--op`}>
      <div className="gas-node__header">
        <div className="gas-node__eyebrow-row">
          <div className="gas-node__title">{data.op}</div>
          {liveHeaderBadge(data)}
        </div>
        {authoredCaption(data) ? (
          <div className="gas-node__caption gas-node__caption--accent">{authoredCaption(data)}</div>
        ) : null}
      </div>
      <div className="gas-node__ports gas-node__ports--split">
        <div className="gas-node__port-col">
          <div className={`gas-port gas-port--in ${pinKindClass('exec')}`}>
            <Handle
              id="control-in"
              type="target"
              position={Position.Left}
              className={`gas-pin gas-pin-left ${pinKindClass('exec')}`}
            />
            Exec
          </div>
          {inputs.map((port) => (
            <div key={port} className={`gas-port gas-port--in ${pinKindClass('list')}`}>
              <Handle
                id={port}
                type="target"
                position={Position.Left}
                className={`gas-pin gas-pin-left ${pinKindClass('list')}`}
              />
              {port}
            </div>
          ))}
        </div>
        <div className="gas-node__port-col gas-node__port-col--out">
          {outputs.map((port) => (
            <div key={port.id} className={`gas-port gas-port--out ${pinKindClass(port.kind)}`}>
              {port.label}
              {(port.kind === 'value' || port.kind === 'list') ? (
                <PortLiveValue value={liveBeside} />
              ) : null}
              <Handle
                id={port.id}
                type="source"
                position={Position.Right}
                className={`gas-pin gas-pin-right ${pinKindClass(port.kind)}`}
              />
            </div>
          ))}
        </div>
      </div>
      <LivePinStrip pins={livePins} shownOnPorts={Boolean(liveBeside)} />
    </div>
  );
}
