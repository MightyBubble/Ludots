import { useLauncherStore } from "@/stores/launcherStore";
import { thumbnailUrl, buildMod } from "@/lib/api";
import { cn } from "@/lib/utils";
import {
  GitBranch, User, Tag, FileText, BookOpen, History,
  Hammer, FileCode2, Loader2, Check, AlertTriangle, Package,
} from "lucide-react";
import { useState } from "react";

export function DetailPanel() {
  const { mods, selectedModId, activeMods, detailTab, setDetailTab, readme, changelog, generateSlnForMod } = useLauncherStore();
  const mod = mods.find(m => m.id === selectedModId);
  const [buildingThis, setBuildingThis] = useState(false);
  const [buildResult, setBuildResult] = useState<"idle" | "ok" | "err">("idle");
  const [slnPath, setSlnPath] = useState<string | null>(null);

  const byId = new Map(mods.map(m => [m.id, m]));
  const deps = mod ? Object.entries(mod.dependencies) : [];
  const missingDeps = mod ? Object.keys(mod.dependencies).filter(d => activeMods.has(mod.id) && !activeMods.has(d)) : [];
  const active = mod ? activeMods.has(mod.id) : false;

  const handleBuild = async () => {
    if (!mod) return;
    setBuildingThis(true); setBuildResult("idle");
    const r = await buildMod(mod.id);
    setBuildResult(r.ok ? "ok" : "err");
    setBuildingThis(false);
  };

  const handleSln = async () => {
    if (!mod) return;
    const p = await generateSlnForMod(mod.id);
    setSlnPath(p);
  };

  const tabs = [
    { id: "info" as const, label: "Info", icon: <FileText size={12} /> },
    ...(mod?.hasReadme ? [{ id: "readme" as const, label: "README", icon: <BookOpen size={12} /> }] : []),
    ...(mod?.changelogFile ? [{ id: "changelog" as const, label: "Changelog", icon: <History size={12} /> }] : []),
  ];

  if (!mod) return (
    <div className="w-[340px] shrink-0 bg-bg-panel flex items-center justify-center">
      <div className="text-center text-gray-600">
        <Package size={32} className="mx-auto mb-2" />
        <p className="text-xs">Select a mod to view details</p>
      </div>
    </div>
  );

  return (
    <div className="w-[340px] shrink-0 bg-bg-panel flex flex-col">
      {/* Header */}
      <div className="relative shrink-0">
        {mod.hasThumbnail && <img src={thumbnailUrl(mod.id)} alt="" className="w-full h-24 object-cover opacity-30" />}
        <div className={cn("px-4 py-3", mod.hasThumbnail && "absolute bottom-0 left-0 right-0 bg-gradient-to-t from-bg-panel via-bg-panel/90")}>
          <h2 className="text-base font-bold">{mod.name}</h2>
          <div className="flex items-center gap-2 text-2xs text-gray-400 mt-0.5">
            <span className="font-mono">v{mod.version}</span>
            {mod.author && <><span>·</span><User size={9} /><span>{mod.author}</span></>}
            {active && <span className="ml-auto px-1.5 py-px rounded bg-ok/10 text-ok text-2xs">active</span>}
          </div>
        </div>
      </div>

      {/* Warning bar */}
      {missingDeps.length > 0 && active && (
        <div className="flex items-center gap-1.5 px-4 py-1.5 bg-warn/10 text-warn text-2xs border-b border-bg-border">
          <AlertTriangle size={11} />
          <span>Missing dependencies: {missingDeps.join(", ")}</span>
        </div>
      )}

      {/* Action buttons */}
      <div className="flex gap-1.5 px-4 py-2 border-b border-bg-border">
        <button onClick={handleBuild} disabled={buildingThis}
          className={cn("flex items-center gap-1 px-2.5 py-1 rounded text-2xs font-medium transition",
            buildResult === "ok" ? "bg-ok/10 text-ok" : buildResult === "err" ? "bg-err/10 text-err" : "bg-bg-hover text-gray-300 hover:bg-bg-border")}>
          {buildingThis ? <Loader2 size={11} className="animate-spin" /> : buildResult === "ok" ? <Check size={11} /> : <Hammer size={11} />}
          Build
        </button>
        <button onClick={handleSln} className="flex items-center gap-1 px-2.5 py-1 rounded text-2xs font-medium bg-bg-hover text-gray-300 hover:bg-bg-border transition">
          <FileCode2 size={11} />Generate .sln
        </button>
        {slnPath && <span className="text-2xs text-gray-500 truncate self-center">{slnPath.split("/").pop()}</span>}
      </div>

      {/* Tabs */}
      <div className="flex border-b border-bg-border shrink-0">
        {tabs.map(t => (
          <button key={t.id} onClick={() => setDetailTab(t.id)}
            className={cn("flex items-center gap-1 px-3 py-2 text-2xs transition border-b-2",
              detailTab === t.id ? "border-accent text-accent" : "border-transparent text-gray-500 hover:text-gray-300")}>
            {t.icon}{t.label}
          </button>
        ))}
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto p-4 space-y-3">
        {detailTab === "info" && <>
          {mod.description && <Section title="Description"><p className="text-xs text-gray-300 leading-relaxed">{mod.description}</p></Section>}
          {mod.tags?.length > 0 && (
            <Section title="Tags">
              <div className="flex flex-wrap gap-1">
                {mod.tags.map(t => <span key={t} className="text-2xs px-1.5 py-0.5 rounded bg-accent/10 text-accent"><Tag size={8} className="inline mr-0.5" />{t}</span>)}
              </div>
            </Section>
          )}
          {deps.length > 0 && (
            <Section title="Dependencies">
              {deps.map(([name, range]) => (
                <div key={name} className="flex items-center justify-between text-xs py-0.5">
                  <span className={cn("flex items-center gap-1", activeMods.has(name) ? "text-gray-300" : "text-err")}>
                    <GitBranch size={10} />{name}
                    {!activeMods.has(name) && <AlertTriangle size={9} />}
                  </span>
                  <span className="text-gray-500 font-mono text-2xs">{range}</span>
                </div>
              ))}
            </Section>
          )}
          <Section title="Location"><code className="text-2xs text-gray-500 break-all">{mod.rootPath}</code></Section>
        </>}
        {detailTab === "readme" && <pre className="text-xs text-gray-300 leading-relaxed whitespace-pre-wrap">{readme ?? "Loading..."}</pre>}
        {detailTab === "changelog" && <pre className="text-xs text-gray-300 leading-relaxed whitespace-pre-wrap">{changelog ?? "Loading..."}</pre>}
      </div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return <div><h3 className="text-2xs uppercase tracking-wider text-gray-500 mb-1">{title}</h3>{children}</div>;
}
