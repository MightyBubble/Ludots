import { useState } from "react";
import { useLauncherStore } from "@/stores/launcherStore";
import { thumbnailUrl, buildMod } from "@/lib/api";
import { cn } from "@/lib/utils";
import { X, GitBranch, User, Tag, FileText, BookOpen, History, Hammer, FileCode } from "lucide-react";

export function ModDetail() {
  const { mods, selectedModId, selectMod, detailTab, setDetailTab, readme, changelog, appendLog, generateSlnForMod } = useLauncherStore();
  const mod = mods.find((m) => m.id === selectedModId);
  const [buildingThis, setBuildingThis] = useState(false);
  const [slnPath, setSlnPath] = useState<string | null>(null);
  const [generatingSln, setGeneratingSln] = useState(false);
  if (!mod) return null;

  const handleBuild = async () => {
    setBuildingThis(true);
    appendLog(`Building ${mod.id}...`);
    try {
      const res = await buildMod(mod.id);
      appendLog(`[${mod.id}] ${res.ok ? "OK" : "FAIL"} (exit ${res.exitCode})`);
      if (res.output) appendLog(res.output);
    } catch (e) {
      appendLog(`Build error: ${e}`);
    }
    setBuildingThis(false);
  };

  const handleGenerateSln = async () => {
    setGeneratingSln(true);
    const path = await generateSlnForMod(mod.id);
    if (path) setSlnPath(path);
    setGeneratingSln(false);
  };

  const deps = Object.entries(mod.dependencies);
  const tabs = [
    { id: "info" as const, label: "Info", icon: <FileText size={12} /> },
    ...(mod.hasReadme ? [{ id: "readme" as const, label: "README", icon: <BookOpen size={12} /> }] : []),
    ...(mod.changelogFile ? [{ id: "changelog" as const, label: "Changelog", icon: <History size={12} /> }] : []),
  ];

  return (
    <div className="w-[360px] shrink-0 border-l border-white/5 bg-surface-light flex flex-col overflow-hidden">
      {/* Header with thumbnail */}
      <div className="relative">
        {mod.hasThumbnail && (
          <img src={thumbnailUrl(mod.id)} alt="" className="w-full h-28 object-cover opacity-40" />
        )}
        <div className={cn("px-4 py-3", mod.hasThumbnail && "absolute bottom-0 left-0 right-0 bg-gradient-to-t from-surface-light")}>
          <div className="flex items-center justify-between">
            <span className="text-lg font-bold">{mod.name}</span>
            <button onClick={() => selectMod(null)} className="text-gray-500 hover:text-white transition">
              <X size={16} />
            </button>
          </div>
          <div className="flex items-center gap-2 text-xs text-gray-400 mt-0.5">
            <span className="font-mono">v{mod.version}</span>
            {mod.author && (<span className="flex items-center gap-0.5"><User size={10} />{mod.author}</span>)}
          </div>
          <div className="flex items-center gap-1.5 mt-2">
            <button
              onClick={handleBuild}
              disabled={buildingThis}
              className="flex items-center gap-1 px-2.5 py-1 text-[10px] bg-white/5 hover:bg-white/10 border border-white/10 rounded transition disabled:opacity-40"
            >
              <Hammer size={10} />
              {buildingThis ? "Building..." : "Build"}
            </button>
            <button
              onClick={handleGenerateSln}
              disabled={generatingSln}
              className="flex items-center gap-1 px-2.5 py-1 text-[10px] bg-white/5 hover:bg-white/10 border border-white/10 rounded transition disabled:opacity-40"
            >
              <FileCode size={10} />
              {generatingSln ? "Generating..." : "Open in IDE"}
            </button>
          </div>
          {slnPath && (
            <p className="text-[9px] text-gray-500 mt-1 break-all">.sln: {slnPath}</p>
          )}
        </div>
      </div>

      {/* Tabs */}
      <div className="flex border-b border-white/5">
        {tabs.map((t) => (
          <button
            key={t.id}
            onClick={() => setDetailTab(t.id)}
            className={cn(
              "flex items-center gap-1 px-3 py-2 text-xs transition border-b-2",
              detailTab === t.id ? "border-accent text-accent" : "border-transparent text-gray-500 hover:text-gray-300"
            )}
          >
            {t.icon}{t.label}
          </button>
        ))}
      </div>

      {/* Tab Content */}
      <div className="flex-1 overflow-y-auto p-4 space-y-4">
        {detailTab === "info" && (
          <>
            {mod.description && (
              <Section title="Description">
                <p className="text-xs text-gray-300 leading-relaxed">{mod.description}</p>
              </Section>
            )}

            {mod.tags && mod.tags.length > 0 && (
              <Section title="Tags">
                <div className="flex flex-wrap gap-1">
                  {mod.tags.map((t) => (
                    <span key={t} className="flex items-center gap-0.5 text-[10px] px-2 py-0.5 bg-accent/10 text-accent rounded">
                      <Tag size={8} />{t}
                    </span>
                  ))}
                </div>
              </Section>
            )}

            {deps.length > 0 && (
              <Section title="Dependencies">
                <div className="space-y-1">
                  {deps.map(([name, range]) => (
                    <div key={name} className="flex items-center justify-between text-xs">
                      <span className="flex items-center gap-1 text-gray-300"><GitBranch size={10} />{name}</span>
                      <span className="text-gray-500 font-mono">{range}</span>
                    </div>
                  ))}
                </div>
              </Section>
            )}

            <Section title="Path">
              <code className="text-[10px] text-gray-500 break-all">{mod.rootPath}</code>
            </Section>
          </>
        )}

        {detailTab === "readme" && (
          <div className="text-xs text-gray-300 leading-relaxed whitespace-pre-wrap font-mono">
            {readme ?? "Loading..."}
          </div>
        )}

        {detailTab === "changelog" && (
          <div className="text-xs text-gray-300 leading-relaxed whitespace-pre-wrap font-mono">
            {changelog ?? "Loading..."}
          </div>
        )}
      </div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <h3 className="text-[10px] uppercase tracking-wider text-gray-500 mb-1.5">{title}</h3>
      {children}
    </div>
  );
}
