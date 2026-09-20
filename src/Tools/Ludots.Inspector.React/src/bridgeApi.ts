export type JsonSchema = {
  type?: string;
  properties?: Record<string, JsonSchemaProperty>;
  required?: string[];
  description?: string;
};

export type JsonSchemaProperty = {
  type?: string | string[];
  enum?: Array<string | number | boolean>;
  description?: string;
  default?: unknown;
  minimum?: number;
  maximum?: number;
  items?: JsonSchemaProperty;
};

export type AgentTool = {
  name: string;
  description: string;
  inputSchema?: JsonSchema | null;
};

export type HealthResponse = {
  ok: boolean;
  pumpCount?: number;
  pendingRequests?: number;
  lastPumpUtc?: string | null;
  instance?: {
    pid?: number;
    port?: number;
    mapId?: string | null;
  };
};

function normalizeBase(url: string): string {
  return url.trim().replace(/\/+$/, "");
}

export async function fetchHealth(baseUrl: string): Promise<HealthResponse> {
  const response = await fetch(`${normalizeBase(baseUrl)}/health`);
  if (!response.ok) {
    throw new Error(`GET /health → HTTP ${response.status}`);
  }
  return (await response.json()) as HealthResponse;
}

export async function fetchTools(baseUrl: string): Promise<AgentTool[]> {
  const response = await fetch(`${normalizeBase(baseUrl)}/tools`);
  if (!response.ok) {
    throw new Error(`GET /tools → HTTP ${response.status}`);
  }
  const body = (await response.json()) as { tools?: AgentTool[] };
  if (!Array.isArray(body.tools)) {
    throw new Error("GET /tools missing tools array");
  }
  return body.tools;
}

export async function callTool(
  baseUrl: string,
  method: string,
  params: Record<string, unknown>
): Promise<unknown> {
  const response = await fetch(`${normalizeBase(baseUrl)}/rpc`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      jsonrpc: "2.0",
      id: 1,
      method,
      params,
    }),
  });
  if (!response.ok) {
    throw new Error(`POST /rpc → HTTP ${response.status}`);
  }
  return response.json();
}

export function domainOf(toolName: string): string {
  const parts = toolName.split(".");
  return parts.length >= 2 ? parts[1] : "other";
}

export function emptyParamsFromSchema(schema?: JsonSchema | null): Record<string, unknown> {
  const result: Record<string, unknown> = {};
  if (!schema?.properties) return result;
  for (const [key, prop] of Object.entries(schema.properties)) {
    if (prop.default !== undefined) {
      result[key] = prop.default;
      continue;
    }
    if (prop.enum && prop.enum.length > 0) {
      result[key] = prop.enum[0];
      continue;
    }
    const type = Array.isArray(prop.type) ? prop.type[0] : prop.type;
    if (type === "boolean") result[key] = false;
    else if (type === "number" || type === "integer") result[key] = 0;
    else if (type === "string") result[key] = "";
    else if (type === "object") result[key] = {};
    else if (type === "array") result[key] = [];
  }
  return result;
}
