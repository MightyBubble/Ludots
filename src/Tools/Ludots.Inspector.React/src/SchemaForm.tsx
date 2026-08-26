import { useMemo, useState } from "react";
import type { JsonSchema, JsonSchemaProperty } from "./bridgeApi";

type Props = {
  schema?: JsonSchema | null;
  value: Record<string, unknown>;
  onChange: (next: Record<string, unknown>) => void;
};

function coerce(raw: string, prop: JsonSchemaProperty): unknown {
  const type = Array.isArray(prop.type) ? prop.type[0] : prop.type;
  if (type === "integer") {
    if (raw.trim() === "") return undefined;
    const n = Number.parseInt(raw, 10);
    if (Number.isNaN(n)) throw new Error(`需要整数`);
    return n;
  }
  if (type === "number") {
    if (raw.trim() === "") return undefined;
    const n = Number.parseFloat(raw);
    if (Number.isNaN(n)) throw new Error(`需要数字`);
    return n;
  }
  if (type === "boolean") return raw === "true";
  return raw;
}

export function SchemaForm({ schema, value, onChange }: Props) {
  const properties = schema?.properties ?? {};
  const required = new Set(schema?.required ?? []);
  const keys = useMemo(() => Object.keys(properties), [properties]);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  if (keys.length === 0) {
    return <p className="muted">此工具无参数。</p>;
  }

  return (
    <div className="schema-form">
      {keys.map((key) => {
        const prop = properties[key];
        const type = Array.isArray(prop.type) ? prop.type[0] : prop.type;
        const current = value[key];
        const isRequired = required.has(key);

        if (prop.enum) {
          return (
            <label key={key} className="field">
              <span>
                {key}
                {isRequired ? " *" : ""}
              </span>
              <select
                value={String(current ?? "")}
                onChange={(e) => {
                  const next = { ...value, [key]: coerce(e.target.value, prop) };
                  onChange(next);
                }}
              >
                {!isRequired && <option value="">—</option>}
                {prop.enum.map((item) => (
                  <option key={String(item)} value={String(item)}>
                    {String(item)}
                  </option>
                ))}
              </select>
              {prop.description && <small>{prop.description}</small>}
            </label>
          );
        }

        if (type === "boolean") {
          return (
            <label key={key} className="field checkbox">
              <input
                type="checkbox"
                checked={Boolean(current)}
                onChange={(e) => onChange({ ...value, [key]: e.target.checked })}
              />
              <span>
                {key}
                {isRequired ? " *" : ""}
              </span>
              {prop.description && <small>{prop.description}</small>}
            </label>
          );
        }

        if (type === "object" || type === "array") {
          const text = typeof current === "string" ? current : JSON.stringify(current ?? (type === "array" ? [] : {}), null, 2);
          return (
            <label key={key} className="field">
              <span>
                {key} (JSON{isRequired ? " *" : ""})
              </span>
              <textarea
                rows={4}
                value={text}
                onChange={(e) => {
                  try {
                    const parsed = JSON.parse(e.target.value || (type === "array" ? "[]" : "{}"));
                    setFieldErrors((prev) => {
                      const next = { ...prev };
                      delete next[key];
                      return next;
                    });
                    onChange({ ...value, [key]: parsed });
                  } catch {
                    setFieldErrors((prev) => ({ ...prev, [key]: "JSON 无效" }));
                    onChange({ ...value, [key]: e.target.value });
                  }
                }}
              />
              {(fieldErrors[key] || prop.description) && (
                <small className={fieldErrors[key] ? "error" : undefined}>
                  {fieldErrors[key] ?? prop.description}
                </small>
              )}
            </label>
          );
        }

        return (
          <label key={key} className="field">
            <span>
              {key}
              {isRequired ? " *" : ""}
            </span>
            <input
              type={type === "number" || type === "integer" ? "number" : "text"}
              value={current === undefined || current === null ? "" : String(current)}
              onChange={(e) => {
                try {
                  const nextValue = coerce(e.target.value, prop);
                  setFieldErrors((prev) => {
                    const next = { ...prev };
                    delete next[key];
                    return next;
                  });
                  const next = { ...value };
                  if (nextValue === undefined || nextValue === "") delete next[key];
                  else next[key] = nextValue;
                  onChange(next);
                } catch (err) {
                  setFieldErrors((prev) => ({
                    ...prev,
                    [key]: err instanceof Error ? err.message : "无效",
                  }));
                }
              }}
            />
            {(fieldErrors[key] || prop.description) && (
              <small className={fieldErrors[key] ? "error" : undefined}>
                {fieldErrors[key] ?? prop.description}
              </small>
            )}
          </label>
        );
      })}
    </div>
  );
}
