import React from 'react';
import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';

export type GasNodeViewEntry = {
  label: string;
  event: string;
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

function pinClass(kind: 'exec' | 'value' | 'list'): string {
  if (kind === 'value') return 'text-violet-300';
  if (kind === 'list') return 'text-emerald-300';
  return 'text-sky-300';
}

function LivePinStrip({ pins }: { pins: { pinIndex: number; value: string }[] }) {
  if (pins.length === 0) return null;
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

function liveHeaderBadge(data: GasNodeViewData): React.ReactNode {
  if (!data.liveDebug) return null;
  if (data.liveDebug.current) return <span className="gas-live-badge">LIVE</span>;
  if (data.liveDebug.intensity > 0.66) return <span className="gas-live-badge gas-live-badge-hot">HOT</span>;
  if (data.liveDebug.intensity > 0) return <span className="gas-live-badge gas-live-badge-trail">RUN</span>;
  return null;
}

function liveCardClass(data: GasNodeViewData, selected: boolean, baseSelected: string, baseIdle: string): string {
  const live = data.liveDebug;
  if (live?.current) return `border-emerald-300 shadow-[0_0_22px_rgba(74,222,128,.65)]`;
  if (live && live.intensity > 0.66) return `border-cyan-300 shadow-[0_0_16px_rgba(34,211,238,.55)]`;
  if (live && live.intensity > 0) return `border-sky-400 shadow-[0_0_12px_rgba(56,189,248,.4)]`;
  return selected ? baseSelected : baseIdle;
}

export function GasNode({ data, selected }: NodeProps<Node<GasNodeViewData>>) {
  const isEvent = data.role === 'event-entry';
  const inputs = collectInputPorts(data);
  const outputs = outputPorts(data);
  const livePins = data.liveDebug?.pins ?? [];

  if (isEvent) {
    const params = data.schema?.parameters.filter((param) => param.type !== 'String') ?? [];
    return (
      <div
        className={`min-w-[220px] overflow-hidden rounded-md border shadow-lg ${liveCardClass(
          data,
          selected,
          'border-rose-200',
          'border-rose-800',
        )}`}
      >
        <div className="bg-rose-700 px-3 py-1.5">
          <div className="flex items-center justify-between gap-2">
            <div className="text-[9px] font-bold uppercase tracking-[.18em] text-rose-100">Event</div>
            {liveHeaderBadge(data)}
          </div>
          <div className="text-sm font-semibold text-white">{data.entry?.event ?? 'Event'}</div>
        </div>
        <div className="relative bg-slate-950 px-3 py-2">
          <div className="text-[11px] text-rose-100">{data.entry?.label}</div>
          {filterChips(data.entry).length > 0 ? (
            <div className="mt-1 flex flex-wrap gap-1">
              {filterChips(data.entry).map((chip) => (
                <span key={chip} className="rounded bg-rose-950 px-1 text-[9px] text-rose-100">{chip}</span>
              ))}
            </div>
          ) : null}
          <div className="mt-2 space-y-1">
            <div className="flex h-5 items-center text-[10px] font-medium text-amber-200">
              <Handle
                id="owner"
                type="source"
                position={Position.Right}
                className={`gas-pin gas-pin-right ${pinClass('exec')}`}
              />
              owner (mount)
            </div>
            <div className="flex h-5 items-center text-[10px] font-medium text-amber-200">
              <Handle
                id="caster"
                type="source"
                position={Position.Right}
                className={`gas-pin gas-pin-right ${pinClass('exec')}`}
              />
              caster (event actor)
            </div>
            {params.map((param) => (
              <div key={param.key} className="flex h-5 items-center text-[10px] font-medium text-violet-200">
                <Handle
                  id={`payload:${param.key}`}
                  type="source"
                  position={Position.Right}
                  className={`gas-pin gas-pin-right ${pinClass('value')}`}
                />
                {param.name}
                <span className="ml-1 text-[8px] uppercase text-slate-500">{param.type}</span>
              </div>
            ))}
          </div>
          <div className="mt-3 flex items-center justify-end text-[10px] font-medium text-sky-200">
            Then
            <Handle
              id="exec"
              type="source"
              position={Position.Right}
              className={`gas-pin gas-pin-right ${pinClass('exec')}`}
            />
          </div>
          <LivePinStrip pins={livePins} />
        </div>
      </div>
    );
  }

  if (isPureValueOp(data.op)) {
    return (
      <div
        className={`min-w-[140px] overflow-hidden rounded-md border shadow-lg ${liveCardClass(
          data,
          selected,
          'border-violet-300',
          'border-violet-800',
        )}`}
      >
        <div className="bg-violet-800 px-3 py-1.5">
          <div className="flex items-center justify-between gap-2">
            <div className="text-sm font-semibold text-white">{data.op}</div>
            {liveHeaderBadge(data)}
          </div>
        </div>
        <div className="relative bg-slate-950 px-3 py-2">
          <div className="text-lg font-semibold tabular-nums text-violet-100">{literalText(data)}</div>
          <div className="mt-2 flex items-center justify-end text-[10px] font-medium text-violet-200">
            Value
            <Handle
              id="value"
              type="source"
              position={Position.Right}
              className={`gas-pin gas-pin-right ${pinClass('value')}`}
            />
          </div>
          <LivePinStrip pins={livePins} />
        </div>
      </div>
    );
  }

  return (
    <div
      className={`min-w-[210px] overflow-hidden rounded-md border shadow-lg ${liveCardClass(
        data,
        selected,
        'border-sky-300',
        'border-slate-600',
      )}`}
    >
      <div className="bg-slate-700 px-3 py-1.5">
        <div className="flex items-center justify-between gap-2">
          <div className="text-sm font-semibold text-white">{data.op}</div>
          {liveHeaderBadge(data)}
        </div>
        {authoredCaption(data) ? (
          <div className="mt-0.5 truncate text-[10px] text-amber-200">{authoredCaption(data)}</div>
        ) : null}
      </div>
      <div className="grid grid-cols-2 gap-x-6 bg-slate-950 px-3 py-2">
        <div className="space-y-1">
          <div className="flex h-5 items-center text-[10px] font-medium text-sky-200">
            <Handle
              id="control-in"
              type="target"
              position={Position.Left}
              className={`gas-pin gas-pin-left ${pinClass('exec')}`}
            />
            Exec
          </div>
          {inputs.map((port) => (
            <div key={port} className="flex h-5 items-center text-[10px] font-medium text-emerald-200">
              <Handle
                id={port}
                type="target"
                position={Position.Left}
                className={`gas-pin gas-pin-left ${pinClass('list')}`}
              />
              {port}
            </div>
          ))}
        </div>
        <div className="space-y-1">
          {outputs.map((port) => (
            <div key={port.id} className={`flex h-5 items-center justify-end text-[10px] font-medium ${pinClass(port.kind)}`}>
              {port.label}
              <Handle
                id={port.id}
                type="source"
                position={Position.Right}
                className={`gas-pin gas-pin-right ${pinClass(port.kind)}`}
              />
            </div>
          ))}
        </div>
      </div>
      <LivePinStrip pins={livePins} />
    </div>
  );
}
