import { useState, type ReactNode } from "react";
import {
  AlertTriangle,
  BookOpen,
  Check,
  FileCode2,
  FileText,
  GitBranch,
  Hammer,
  History,
  Loader2,
  Package,
  Tag,
  Wrench,
} from "lucide-react";
import { thumbnailUrl } from "@/lib/api";
import { cn } from "@/lib/utils";
import { useLauncherStore } from "@/stores/launcherStore";
import { DependencyGraphPanel } from "@/components/DependencyGraphPanel";

export function DetailPanel() {
  const {
    mods,
    selectedModId,
    activeMods,
    detailTab,
    setDetailTab,
    readme,
    changelog,
    buildSingleMod,
    fixProjectForMod,
    generateSlnForMod,
  } = useLauncherStore();

  const mod = mods.find((candidate) => candidate.id === selectedModId) ?? null;
  const [building, setBuilding] = useState(false);
  const [fixing, setFixing] = useState(false);
  const [buildResult, setBuildResult] = useState<"idle" | "ok" | "error">("idle");
  const [projectPath, setProjectPath] = useState<string | null>(null);
  const [solutionPath, setSolutionPath] = useState<string | null>(null);

  if (!mod) {
    return (
      <div className="flex w-[460px] shrink-0 items-center justify-center bg-bg-panel">
        <div className="text-center text-gray-600">
          <Package size={32} className="mx-auto mb-2" />
          <p className="text-sm">Select a mod to inspect launcher state</p>
        </div>
      </div>
    );
  }

  const dependencyEntries = Object.entries(mod.dependencies);
  const dependents = mods.filter((candidate) => Object.prototype.hasOwnProperty.call(candidate.dependencies, mod.id));
  const missingDependencies = dependencyEntries
    .filter(([dependencyId]) => !mods.some((candidate) => candidate.id === dependencyId))
    .map(([dependencyId]) => dependencyId);
  const isActive = activeMods.has(mod.id);

  const tabs = [
    { id: "info" as const, label: "Info", icon: <FileText size={12} /> },
    { id: "graph" as const, label: "Graph", icon: <GitBranch size={12} /> },
    ...(mod.hasReadme ? [{ id: "readme" as const, label: "README", icon: <BookOpen size={12} /> }] : []),
    ...(mod.changelogFile ? [{ id: "changelog" as const, label: "Changelog", icon: <History size={12} /> }] : []),
  ];

  const handleBuild = async () => {
    setBuilding(true);
    setBuildResult("idle");
    const ok = await buildSingleMod(mod.id);
    setBuildResult(ok ? "ok" : "error");
    setBuilding(false);
  };

  const handleFixProject = async () => {
    setFixing(true);
    const path = await fixProjectForMod(mod.id);
    setProjectPath(path);
    setFixing(false);
  };

  const handleGenerateSolution = async () => {
    const path = await generateSlnForMod(mod.id);
    setSolutionPath(path);
  };

  return (
    <div className="flex w-[460px] shrink-0 flex-col bg-bg-panel">
      <div className="relative shrink-0 border-b border-bg-border">
        {mod.hasThumbnail ? (
          <img src={thumbnailUrl(mod.id)} alt="" className="h-32 w-full object-cover opacity-30" />
        ) : (
          <div className="h-32 bg-gradient-to-br from-bg via-bg-panel to-bg-card" />
        )}

        <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-bg-panel via-bg-panel/90 px-5 py-4">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <h2 className="truncate text-lg font-bold">{mod.name}</h2>
                <span className="rounded-full bg-bg px-2 py-0.5 text-[10px] font-mono text-gray-400">
                  v{mod.version}
                </span>
              </div>
              <div className="mt-1 flex flex-wrap items-center gap-2 text-[11px] text-gray-400">
                <span>{mod.author || "Unknown author"}</span>
                <span className="rounded-full bg-bg px-2 py-0.5 uppercase tracking-[0.2em] text-gray-300">
                  {formatBuildState(mod.buildState)}
                </span>
                {isActive ? (
                  <span className="rounded-full bg-ok/10 px-2 py-0.5 uppercase tracking-[0.2em] text-ok">
                    Active
                  </span>
                ) : null}
              </div>
            </div>
          </div>
        </div>
      </div>

      {missingDependencies.length > 0 ? (
        <div className="flex items-center gap-1.5 border-b border-bg-border bg-warn/10 px-5 py-2 text-xs text-warn">
          <AlertTriangle size={12} />
          Missing dependencies: {missingDependencies.join(", ")}
        </div>
      ) : null}

      <div className="flex items-center gap-2 border-b border-bg-border px-5 py-3">
        <button
          onClick={() => void handleBuild()}
          disabled={building}
          className={cn(
            "flex items-center gap-1 rounded-lg px-3 py-1.5 text-xs font-medium transition",
            buildResult === "ok"
              ? "bg-ok/10 text-ok"
              : buildResult === "error"
                ? "bg-err/10 text-err"
                : "bg-bg-hover text-gray-300 hover:bg-bg-border",
          )}
        >
          {building ? <Loader2 size={12} className="animate-spin" /> : buildResult === "ok" ? <Check size={12} /> : <Hammer size={12} />}
          Build Mod
        </button>

        <button
          onClick={() => void handleFixProject()}
          disabled={fixing}
          className="flex items-center gap-1 rounded-lg bg-bg-hover px-3 py-1.5 text-xs font-medium text-gray-300 transition hover:bg-bg-border disabled:opacity-50"
        >
          {fixing ? <Loader2 size={12} className="animate-spin" /> : <Wrench size={12} />}
          Fix Project
        </button>

        <button
          onClick={() => void handleGenerateSolution()}
          className="flex items-center gap-1 rounded-lg bg-bg-hover px-3 py-1.5 text-xs font-medium text-gray-300 transition hover:bg-bg-border"
        >
          <FileCode2 size={12} />
          Generate .sln
        </button>
      </div>

      {(projectPath || solutionPath) ? (
        <div className="space-y-1 border-b border-bg-border px-5 py-2 text-[11px] text-gray-500">
          {projectPath ? <div>Project: {projectPath}</div> : null}
          {solutionPath ? <div>Solution: {solutionPath}</div> : null}
        </div>
      ) : null}

      <div className="flex shrink-0 border-b border-bg-border">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setDetailTab(tab.id)}
            className={cn(
              "flex items-center gap-1 border-b-2 px-4 py-2 text-[11px] uppercase tracking-[0.2em] transition",
              detailTab === tab.id
                ? "border-accent text-accent"
                : "border-transparent text-gray-500 hover:text-gray-200",
            )}
          >
            {tab.icon}
            {tab.label}
          </button>
        ))}
      </div>

      <div className="flex-1 overflow-y-auto p-5">
        {detailTab === "info" ? (
          <div className="space-y-4">
            {mod.description ? (
              <Section title="Description">
                <p className="text-sm leading-relaxed text-gray-300">{mod.description}</p>
              </Section>
            ) : null}

            {mod.tags.length > 0 ? (
              <Section title="Tags">
                <div className="flex flex-wrap gap-2">
                  {mod.tags.map((tag) => (
                    <span key={tag} className="rounded-full bg-accent/10 px-2 py-1 text-[11px] text-accent">
                      <Tag size={10} className="mr-1 inline" />
                      {tag}
                    </span>
                  ))}
                </div>
              </Section>
            ) : null}

            <Section title="Build">
              <InfoRow label="State" value={formatBuildState(mod.buildState)} />
              <InfoRow label="Project" value={mod.hasProject ? "Ready" : "Missing"} />
              <InfoRow label="Last Result" value={mod.lastBuildMessage || "No build recorded"} />
              <InfoRow label="Assembly" value={mod.mainAssemblyPath || "Not configured"} mono />
            </Section>

            <Section title="Dependencies">
              {dependencyEntries.length === 0 ? (
                <p className="text-xs text-gray-500">No declared dependencies.</p>
              ) : (
                <div className="space-y-2">
                  {dependencyEntries.map(([dependencyId, range]) => {
                    const dependencyMod = mods.find((candidate) => candidate.id === dependencyId) ?? null;
                    const isDependencyActive = activeMods.has(dependencyId);
                    return (
                      <div key={dependencyId} className="flex items-center justify-between text-sm">
                        <div className="flex items-center gap-2">
                          <GitBranch size={12} className={dependencyMod ? "text-accent" : "text-err"} />
                          <span className={dependencyMod ? "text-gray-200" : "text-err"}>{dependencyId}</span>
                          {!dependencyMod ? <AlertTriangle size={12} className="text-err" /> : null}
                          {isDependencyActive ? (
                            <span className="rounded-full bg-ok/10 px-2 py-0.5 text-[10px] uppercase tracking-[0.15em] text-ok">
                              Active
                            </span>
                          ) : null}
                        </div>
                        <span className="font-mono text-[11px] text-gray-500">{range}</span>
                      </div>
                    );
                  })}
                </div>
              )}
            </Section>

            <Section title="Dependents">
              {dependents.length === 0 ? (
                <p className="text-xs text-gray-500">No mods depend on this mod.</p>
              ) : (
                <div className="flex flex-wrap gap-2">
                  {dependents.map((dependent) => (
                    <span key={dependent.id} className="rounded-full bg-bg px-2 py-1 text-[11px] text-gray-300">
                      {dependent.id}
                    </span>
                  ))}
                </div>
              )}
            </Section>

            <Section title="Location">
              <code className="break-all text-[11px] text-gray-500">{mod.rootPath}</code>
            </Section>
          </div>
        ) : null}

        {detailTab === "graph" ? (
          <DependencyGraphPanel mods={mods} activeMods={activeMods} selectedModId={mod.id} />
        ) : null}

        {detailTab === "readme" ? (
          <pre className="whitespace-pre-wrap text-sm leading-relaxed text-gray-300">{readme ?? "Loading README..."}</pre>
        ) : null}

        {detailTab === "changelog" ? (
          <pre className="whitespace-pre-wrap text-sm leading-relaxed text-gray-300">
            {changelog ?? "Loading changelog..."}
          </pre>
        ) : null}
      </div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section>
      <h3 className="mb-2 text-[11px] uppercase tracking-[0.25em] text-gray-500">{title}</h3>
      {children}
    </section>
  );
}

function InfoRow({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex items-center justify-between gap-3 py-1 text-sm">
      <span className="text-gray-500">{label}</span>
      <span className={cn("text-right text-gray-300", mono && "font-mono text-[11px]")}>{value}</span>
    </div>
  );
}

function formatBuildState(buildState: string): string {
  switch (buildState) {
    case "Succeeded":
      return "Built";
    case "Outdated":
      return "Outdated";
    case "NoProject":
      return "No Project";
    case "Idle":
      return "Not Built";
    default:
      return buildState;
  }
}
