import { useEffect, useMemo, useState } from "react";
import { Copy, FlaskConical, Layers, Loader2, PackageOpen, Search } from "lucide-react";
import { cn } from "@/lib/utils";
import { fetchShowcaseRegistry, launchHint, type ShowcaseEntry } from "@/lib/showcase";

const TIER_ORDER = ["T1", "T2", "T3", "T4"];

const TIER_LABELS: Record<string, string> = {
  T1: "核心验收",
  T2: "能力展示",
  T3: "综合示例",
  T4: "边界与负向",
};

const TIER_BADGE: Record<string, string> = {
  T1: "border-ok/30 bg-ok/10 text-ok",
  T2: "border-accent/30 bg-accent-dim text-accent",
  T3: "border-warn/30 bg-warn/10 text-warn",
  T4: "border-err/30 bg-err/10 text-err",
};

const STATUS_BADGE: Record<string, string> = {
  experimental: "border-warn/30 bg-warn/10 text-warn",
  retired: "border-bg-border bg-bg-hover text-gray-500",
};

function groupByCategory(entries: ShowcaseEntry[]): Array<[string, ShowcaseEntry[]]> {
  const groups = new Map<string, ShowcaseEntry[]>();
  for (const entry of entries) {
    const list = groups.get(entry.category) ?? [];
    list.push(entry);
    groups.set(entry.category, list);
  }

  return [...groups.entries()].sort((left, right) => left[0].localeCompare(right[0]));
}

function ShowcaseCard({ entry }: { entry: ShowcaseEntry }) {
  const [copied, setCopied] = useState(false);
  const hint = launchHint(entry);

  const handleCopy = async () => {
    if (!hint) {
      return;
    }

    try {
      await navigator.clipboard.writeText(hint);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard may be unavailable (non-secure context); keep the hint read-only.
    }
  };

  return (
    <div className="flex flex-col gap-2 rounded-xl border border-bg-border bg-bg-card p-3 transition hover:border-accent/40">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="truncate text-xs font-semibold text-gray-100" title={entry.title}>
            {entry.title}
          </div>
          <div className="truncate font-mono text-[10px] text-gray-500" title={entry.path}>
            {entry.id}
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-1">
          <span
            className={cn(
              "rounded-full border px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.15em]",
              TIER_BADGE[entry.tier] ?? "border-bg-border bg-bg-hover text-gray-400",
            )}
          >
            {entry.tier}
          </span>
          {entry.status !== "active" ? (
            <span
              className={cn(
                "rounded-full border px-2 py-0.5 text-[10px] uppercase tracking-[0.15em]",
                STATUS_BADGE[entry.status] ?? "border-bg-border bg-bg-hover text-gray-500",
              )}
            >
              {entry.status}
            </span>
          ) : null}
        </div>
      </div>

      <p className="line-clamp-2 text-[11px] leading-relaxed text-gray-400" title={entry.summary}>
        {entry.summary}
      </p>

      {entry.tags.length > 0 ? (
        <div className="flex flex-wrap gap-1">
          {entry.tags.map((tag) => (
            <span key={tag} className="rounded bg-bg-hover px-1.5 py-0.5 text-[10px] text-gray-500">
              {tag}
            </span>
          ))}
        </div>
      ) : null}

      {hint ? (
        <button
          onClick={() => void handleCopy()}
          title="Click to copy launch command"
          className="mt-auto flex items-center gap-1.5 rounded-lg border border-bg-border/70 bg-bg px-2 py-1.5 text-left font-mono text-[10px] text-accent transition hover:border-accent/40 hover:text-accent-hover"
        >
          <span className="flex-1 truncate">{hint}</span>
          <Copy size={11} className="shrink-0" />
          {copied ? <span className="shrink-0 text-ok">copied</span> : null}
        </button>
      ) : (
        <div className="mt-auto rounded-lg border border-bg-border/50 bg-bg px-2 py-1.5 text-[10px] text-gray-600">
          no launch selector (asset-only fixture)
        </div>
      )}
    </div>
  );
}

export function ShowcasePanel() {
  const [entries, setEntries] = useState<ShowcaseEntry[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [search, setSearch] = useState("");
  const [activeTier, setActiveTier] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void fetchShowcaseRegistry().then((registry) => {
      if (cancelled) {
        return;
      }

      if (registry) {
        setEntries(registry.showcases);
      } else {
        setFailed(true);
      }
    });

    return () => {
      cancelled = true;
    };
  }, []);

  const visible = useMemo(() => {
    if (!entries) {
      return [];
    }

    const query = search.trim().toLowerCase();
    return entries.filter((entry) => {
      if (activeTier && entry.tier !== activeTier) {
        return false;
      }

      if (!query) {
        return true;
      }

      return (
        entry.id.toLowerCase().includes(query) ||
        entry.title.toLowerCase().includes(query) ||
        entry.summary.toLowerCase().includes(query) ||
        entry.tags.some((tag) => tag.toLowerCase().includes(query))
      );
    });
  }, [entries, search, activeTier]);

  const tiers = useMemo(() => {
    const present = new Set((entries ?? []).map((entry) => entry.tier));
    return [...TIER_ORDER.filter((tier) => present.has(tier)), ...[...present].filter((tier) => !TIER_ORDER.includes(tier)).sort()];
  }, [entries]);

  const groupedTiers = useMemo(() => {
    const order = activeTier ? [activeTier] : tiers;
    return order
      .map((tier) => [tier, visible.filter((entry) => entry.tier === tier)] as const)
      .filter(([, list]) => list.length > 0);
  }, [visible, tiers, activeTier]);

  if (failed) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-3 px-6 text-center">
        <PackageOpen size={32} className="text-gray-600" />
        <h2 className="text-sm font-semibold text-gray-300">Showcase registry unavailable</h2>
        <p className="max-w-md text-xs leading-relaxed text-gray-500">
          <code className="rounded bg-bg-panel px-1.5 py-0.5 text-[11px] text-accent">showcase.registry.json</code>{" "}
          could not be loaded. It is copied into the launcher by the prebuild step — run{" "}
          <code className="rounded bg-bg-panel px-1.5 py-0.5 text-[11px] text-accent">npm run dev</code> or{" "}
          <code className="rounded bg-bg-panel px-1.5 py-0.5 text-[11px] text-accent">npm run build</code> from{" "}
          <code className="rounded bg-bg-panel px-1.5 py-0.5 text-[11px]">src/Tools/Ludots.Launcher.React</code> to
          regenerate it.
        </p>
      </div>
    );
  }

  if (!entries) {
    return (
      <div className="flex flex-1 items-center justify-center gap-2 text-gray-500">
        <Loader2 size={18} className="animate-spin text-accent" />
        <span className="text-xs">Loading showcase registry...</span>
      </div>
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex items-center gap-3 border-b border-bg-border bg-bg-panel px-4 py-2.5">
        <Layers size={14} className="text-accent" />
        <span className="text-xs font-semibold">Showcases</span>
        <span className="text-[10px] text-gray-500">
          {visible.length} / {entries.length} entries
        </span>

        <div className="h-5 w-px bg-bg-border" />

        <div className="flex items-center gap-1">
          <button
            onClick={() => setActiveTier(null)}
            className={cn(
              "rounded-lg px-2.5 py-1 text-[11px] transition",
              activeTier === null ? "bg-accent text-white" : "text-gray-400 hover:bg-bg-hover hover:text-gray-200",
            )}
          >
            All
          </button>
          {tiers.map((tier) => (
            <button
              key={tier}
              onClick={() => setActiveTier((current) => (current === tier ? null : tier))}
              title={TIER_LABELS[tier] ?? tier}
              className={cn(
                "rounded-lg px-2.5 py-1 text-[11px] transition",
                activeTier === tier ? "bg-accent text-white" : "text-gray-400 hover:bg-bg-hover hover:text-gray-200",
              )}
            >
              {tier}
            </button>
          ))}
        </div>

        <div className="flex-1" />

        <div className="relative">
          <Search size={12} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-500" />
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Filter showcases..."
            className="w-52 rounded-lg border border-bg-border bg-bg py-1.5 pl-7 pr-3 text-[11px] transition focus:border-accent/60 focus:outline-none"
          />
        </div>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto px-4 py-3">
        {groupedTiers.length === 0 ? (
          <div className="flex h-full flex-col items-center justify-center gap-2 text-gray-600">
            <FlaskConical size={24} />
            <span className="text-xs">No showcases match the current filter.</span>
          </div>
        ) : (
          groupedTiers.map(([tier, tierEntries]) => (
            <section key={tier} className="mb-5">
              <div className="mb-2 flex items-baseline gap-2">
                <h3 className="text-xs font-semibold uppercase tracking-[0.2em] text-gray-300">{tier}</h3>
                <span className="text-[10px] text-gray-500">
                  {TIER_LABELS[tier] ?? ""} · {tierEntries.length}
                </span>
              </div>
              {groupByCategory(tierEntries).map(([category, categoryEntries]) => (
                <div key={category} className="mb-3">
                  <div className="mb-1.5 text-[10px] font-semibold uppercase tracking-[0.25em] text-gray-500">
                    {category}
                  </div>
                  <div className="grid grid-cols-[repeat(auto-fill,minmax(280px,1fr))] gap-2.5">
                    {categoryEntries.map((entry) => (
                      <ShowcaseCard key={entry.id} entry={entry} />
                    ))}
                  </div>
                </div>
              ))}
            </section>
          ))
        )}
      </div>
    </div>
  );
}
