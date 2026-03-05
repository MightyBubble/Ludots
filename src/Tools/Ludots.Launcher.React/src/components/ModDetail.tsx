import { useLauncherStore } from "@/stores/launcherStore";
import { X, Package, GitBranch, FileText } from "lucide-react";

export function ModDetail() {
  const { mods, selectedModId, selectMod } = useLauncherStore();
  const mod = mods.find((m) => m.id === selectedModId);

  if (!mod) return null;

  const deps = Object.entries(mod.dependencies);

  return (
    <div className="w-[340px] shrink-0 border-l border-white/5 bg-surface-light overflow-y-auto">
      <div className="flex items-center justify-between px-4 py-3 border-b border-white/5">
        <h2 className="font-semibold text-sm">Mod Details</h2>
        <button onClick={() => selectMod(null)} className="text-gray-500 hover:text-white transition">
          <X size={16} />
        </button>
      </div>

      <div className="p-4 space-y-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <Package size={18} className="text-accent" />
            <span className="text-lg font-bold">{mod.name}</span>
          </div>
          <span className="text-xs text-gray-500 font-mono">v{mod.version} · priority {mod.priority}</span>
        </div>

        <Section icon={<FileText size={14} />} title="Path">
          <code className="text-xs text-gray-400 break-all">{mod.rootPath}</code>
        </Section>

        {deps.length > 0 && (
          <Section icon={<GitBranch size={14} />} title="Dependencies">
            <div className="space-y-1">
              {deps.map(([name, range]) => (
                <div key={name} className="flex justify-between text-xs">
                  <span className="text-gray-300">{name}</span>
                  <span className="text-gray-500 font-mono">{range}</span>
                </div>
              ))}
            </div>
          </Section>
        )}
      </div>
    </div>
  );
}

function Section({ icon, title, children }: { icon: React.ReactNode; title: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="flex items-center gap-1.5 text-xs text-gray-400 mb-1.5">
        {icon}
        <span className="uppercase tracking-wider">{title}</span>
      </div>
      {children}
    </div>
  );
}
