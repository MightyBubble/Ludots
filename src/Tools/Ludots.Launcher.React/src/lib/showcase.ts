export interface ShowcaseEntry {
  id: string;
  path: string;
  projectPath: string | null;
  title: string;
  summary: string;
  tier: string;
  category: string;
  tags: string[];
  binding: string | null;
  preset: string | null;
  docsPath: string | null;
  readmePath: string | null;
  acceptanceTest: string | null;
  artifactDir: string | null;
  screenshot: string | null;
  status: "active" | "experimental" | "retired" | string;
  notes?: string | null;
}

export interface ShowcaseRegistry {
  schemaVersion: number;
  showcases: ShowcaseEntry[];
}

/**
 * Fetches the static showcase registry copied into `public/` by
 * `scripts/copy-registry.mjs` (predev/prebuild). Returns null when the
 * registry is unavailable so the panel can render a friendly empty state
 * instead of crashing.
 */
export async function fetchShowcaseRegistry(): Promise<ShowcaseRegistry | null> {
  try {
    const response = await fetch(`${import.meta.env.BASE_URL}showcase.registry.json`, {
      headers: { Accept: "application/json" },
    });
    if (!response.ok) {
      return null;
    }

    const data = (await response.json()) as ShowcaseRegistry;
    if (!data || !Array.isArray(data.showcases)) {
      return null;
    }

    return data;
  } catch {
    return null;
  }
}

/**
 * Best-effort launch hint for a showcase entry, following the unified CLI
 * selector grammar (RFC-0001): `$binding` or `preset:<id>`.
 */
export function launchHint(entry: ShowcaseEntry): string | null {
  if (entry.status === "retired") {
    return null;
  }

  if (entry.binding) {
    return `ludots launch $${entry.binding} --adapter raylib`;
  }

  if (entry.preset) {
    return `ludots launch preset:${entry.preset}`;
  }

  return null;
}
