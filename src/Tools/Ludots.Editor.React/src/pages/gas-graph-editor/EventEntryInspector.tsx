import React from 'react';
import type { EventSchemaView } from './GasNode';
import {
  EVENT_DIRECTIONS,
  EVENT_REFIRE_IGNORE,
  EVENT_REFIRE_RESTART,
  entryTriggerKind,
  entryTriggerName,
  parseOptionalFloat,
  parseOptionalInt,
  setEntryTrigger,
  type EntryTriggerKind,
  type EventEntryConfig,
  type EventEntryFilters,
} from './eventEntry';

/** Payload schema an action-bound entry captures from, mirroring the runtime constant. */
export const INPUT_ACTION_SCHEMA_NAME = 'InputAction';

export type InputActionView = {
  id: string;
  type: string;
  source: string;
};

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <div className="mb-1 text-studio-muted">{label}</div>
      {children}
    </label>
  );
}

function TextInput({
  value,
  onChange,
  placeholder,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  return (
    <input
      type="text"
      value={value}
      placeholder={placeholder}
      onChange={(event) => onChange(event.target.value)}
      className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono"
    />
  );
}

export function EventEntryInspector({
  entry,
  eventSchemas,
  inputActions,
  startOptions,
  instanceOptions,
  variableOptions,
  onChange,
  onAdd,
}: {
  entry: EventEntryConfig;
  eventSchemas: EventSchemaView[];
  inputActions: InputActionView[];
  startOptions: string[];
  instanceOptions: string[];
  variableOptions: string[];
  onChange: (next: EventEntryConfig) => void;
  onAdd?: () => void;
}) {
  const filters = entry.filters ?? {};
  const startChoices = entry.start && !startOptions.includes(entry.start)
    ? [entry.start, ...startOptions]
    : startOptions;
  const triggerKind = entryTriggerKind(entry);
  const triggerName = entryTriggerName(entry);
  const schemaNames = new Set(eventSchemas.map((schema) => schema.name));
  const actionIds = new Set(inputActions.map((action) => action.id));
  // Action-bound entries capture from the shared InputAction schema, so the payload pins
  // an author can drag are the same either way — only the lookup key differs.
  const selectedSchema = eventSchemas.find((schema) => schema.name === (
    triggerKind === 'action' ? INPUT_ACTION_SCHEMA_NAME : triggerName
  )) ?? null;
  const catalogValue = triggerKind === 'event' && selectedSchema ? triggerName : '';

  const patchFilters = (patch: EventEntryFilters) => {
    onChange({
      ...entry,
      filters: { ...filters, ...patch },
    });
  };

  const setTrigger = (kind: EntryTriggerKind, name: string) => {
    const autoLabel = name !== '' && (entry.label.trim() === '' || entry.label.startsWith('on_'))
      ? `on_${name.replace(/[^A-Za-z0-9_]+/g, '_')}`
      : entry.label;
    onChange({ ...setEntryTrigger(entry, kind, name), label: autoLabel });
  };

  return (
    <div className="space-y-2 rounded border border-studio-red/40 bg-studio-red/15 p-2">
      <div className="flex items-center justify-between">
        <div className="text-[10px] font-semibold uppercase tracking-wide text-studio-red">事件入口</div>
        {onAdd ? (
          <button
            type="button"
            onClick={onAdd}
            className="rounded border border-studio-red/50 px-2 py-0.5 text-[10px] text-studio-label hover:bg-studio-red/20"
          >
            新增事件
          </button>
        ) : null}
      </div>
      <Field label="触发方式">
        <div className="flex gap-1">
          {(['event', 'action'] as const).map((kind) => (
            <button
              key={kind}
              type="button"
              onClick={() => triggerKind === kind || setTrigger(kind, '')}
              className={triggerKind === kind
                ? 'flex-1 rounded border border-studio-red bg-studio-red/40 px-2 py-1 font-semibold text-studio-label'
                : 'flex-1 rounded border border-studio-fill bg-studio-bg px-2 py-1 text-studio-muted hover:bg-studio-surface'}
            >
              {kind === 'event' ? '游戏事件' : '输入动作'}
            </button>
          ))}
        </div>
      </Field>
      {triggerKind === 'event' ? (
        <>
          <Field label="事件 schema">
            <select
              value={catalogValue}
              onChange={(event) => setTrigger('event', event.target.value)}
              className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono"
            >
              <option value="">{eventSchemas.length === 0 ? '未装载 schema' : '选择已注册事件…'}</option>
              {eventSchemas.map((schema) => (
                <option key={schema.name} value={schema.name}>
                  {schema.name} · {schema.scope}
                </option>
              ))}
            </select>
          </Field>
          <Field label="事件名">
            <TextInput
              value={triggerName}
              placeholder="EntityDied"
              onChange={(eventName) => setTrigger('event', eventName)}
            />
          </Field>
          {triggerName && !schemaNames.has(triggerName) ? (
            <p className="rounded border border-studio-yellow/40 bg-studio-yellow/10 p-2 text-[11px] leading-5 text-studio-secondary">
              这个名字不在 schema 目录里。选中已注册事件之前，载荷引脚保持无类型。
            </p>
          ) : null}
        </>
      ) : (
        <>
          <Field label="输入动作">
            <select
              value={actionIds.has(triggerName) ? triggerName : ''}
              onChange={(event) => setTrigger('action', event.target.value)}
              className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono"
            >
              <option value="">{inputActions.length === 0 ? '未装载输入动作' : '选择已注册动作…'}</option>
              {inputActions.map((action) => (
                <option key={action.id} value={action.id}>
                  {action.id} · {action.type}
                </option>
              ))}
            </select>
          </Field>
          {triggerName && !actionIds.has(triggerName) ? (
            <p className="rounded border border-studio-red/50 bg-studio-red/15 p-2 text-[11px] leading-5 text-studio-label">
              <span className="font-mono">{triggerName}</span> 不是已注册的输入动作。从列表选一个，或写进对应 Mod 的
              <span className="font-mono">Input/default_input.json</span>。
            </p>
          ) : null}
          <p className="rounded border border-studio-red/30 bg-studio-bg/80 p-2 text-[11px] leading-5 text-studio-secondary">
            此入口直接监听动作本身，不进事件总线。下方
            <span className="font-mono"> Action </span> 载荷过滤留空即可。
          </p>
        </>
      )}
      {selectedSchema ? (
        <div className="rounded border border-studio-red/30 bg-studio-bg/80 p-2 text-[11px] leading-5 text-studio-secondary">
          <div className="mb-1 font-semibold text-studio-label">
            载荷引脚
            {triggerKind === 'action'
              ? <span className="ml-1 font-mono font-normal text-studio-muted">{INPUT_ACTION_SCHEMA_NAME}</span>
              : null}
          </div>
          {selectedSchema.parameters.length === 0 ? (
            <div>无参数。</div>
          ) : (
            <ul className="space-y-0.5 font-mono text-[10px]">
              {selectedSchema.parameters.map((param) => (
                <li key={param.key}>
                  {param.name}
                  <span className="text-studio-muted"> : {param.type}</span>
                  {param.optional ? <span className="text-studio-muted">（可选）</span> : null}
                  {param.type === 'String' ? <span className="text-studio-yellow">—— String 引脚尚未接线</span> : null}
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : null}
      <Field label="标签">
        <TextInput
          value={entry.label}
          placeholder="on_raider_died"
          onChange={(label) => onChange({ ...entry, label })}
        />
      </Field>
      <Field label="起点节点">
        <select
          value={entry.start}
          onChange={(event) => onChange({ ...entry, start: event.target.value })}
          className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono"
        >
          <option value="">接 Then 端，或选一个节点</option>
          {startChoices.map((id) => (
            <option key={id} value={id}>{id}</option>
          ))}
        </select>
      </Field>
      <label className="flex items-center gap-2">
        <input
          type="checkbox"
          checked={Boolean(entry.once)}
          onChange={(event) => onChange({ ...entry, once: event.target.checked })}
        />
        <span className="text-studio-muted">只触发一次</span>
      </label>
      <Field label="已在运行时">
        <select
          value={entry.refire === EVENT_REFIRE_RESTART ? EVENT_REFIRE_RESTART : EVENT_REFIRE_IGNORE}
          onChange={(event) => onChange({ ...entry, refire: event.target.value })}
          className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono"
        >
          <option value={EVENT_REFIRE_IGNORE}>忽略——保留当前运行</option>
          <option value={EVENT_REFIRE_RESTART}>重启——丢弃重来</option>
        </select>
      </Field>
      <div className="border-t border-studio-red/40 pt-2 text-[10px] font-semibold uppercase tracking-wide text-studio-red">
        谁能触发
      </div>
      <Field label="实例（精确放置单位）">
        <select
          value={filters.instanceId ?? ''}
          onChange={(event) => patchFilters({ instanceId: event.target.value || null })}
          className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono"
        >
          <option value="">任意来源</option>
          {instanceOptions.map((instanceId) => (
            <option key={instanceId} value={instanceId}>{instanceId}</option>
          ))}
        </select>
      </Field>
      <Field label="变量（精确地图变量）">
        <select
          value={filters.varName ?? ''}
          onChange={(event) => patchFilters({ varName: event.target.value || null })}
          className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono"
        >
          <option value="">任意变量</option>
          {variableOptions.map((varName) => (
            <option key={varName} value={varName}>{varName}</option>
          ))}
        </select>
      </Field>
      <Field label="区域">
        <TextInput value={filters.region ?? ''} placeholder="raid_circle" onChange={(region) => patchFilters({ region })} />
      </Field>
      <Field label="事件载荷携带的 Action">
        {triggerKind === 'action' ? (
          <div className="rounded border border-studio-elevated bg-studio-bg/80 px-2 py-1 text-[11px] leading-5 text-studio-muted">
            不适用——此入口已由输入动作触发。
          </div>
        ) : (
          <TextInput
            value={filters.action ?? ''}
            placeholder="CommandSourceAcquire"
            onChange={(action) => patchFilters({ action })}
          />
        )}
      </Field>
      <Field label="Tag">
        <TextInput value={filters.tag ?? ''} onChange={(tag) => patchFilters({ tag })} />
      </Field>
      <Field label="队伍">
        <input
          type="number"
          step="1"
          value={filters.team ?? ''}
          onChange={(event) => patchFilters({ team: parseOptionalInt(event.target.value) })}
          className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono"
        />
      </Field>
      <Field label="计数方向">
        <select
          value={filters.direction ?? ''}
          onChange={(event) => patchFilters({ direction: event.target.value || null })}
          className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono"
        >
          <option value="">不限</option>
          {EVENT_DIRECTIONS.map((direction) => (
            <option key={direction} value={direction}>{direction}</option>
          ))}
        </select>
      </Field>
      <Field label="计数阈值">
        <input
          type="number"
          value={filters.threshold ?? ''}
          onChange={(event) => patchFilters({ threshold: parseOptionalFloat(event.target.value) })}
          className="w-full rounded border border-studio-fill bg-studio-bg px-2 py-1 font-mono"
        />
      </Field>
      <p className="rounded border border-studio-red/30 bg-studio-bg/80 p-2 text-[11px] leading-5 text-studio-secondary">
        这张卡只决定链何时启动。上面的命名引脚交出本次发生的内容；把引脚拖到值输入上即可放置读取节点。
      </p>
    </div>
  );
}
