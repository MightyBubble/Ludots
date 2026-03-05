import { useState } from "react";
import { useLauncherStore } from "@/stores/launcherStore";
import { X, Plus } from "lucide-react";

export function CreateModDialog() {
  const { createNewMod, toggleCreateDialog } = useLauncherStore();
  const [modId, setModId] = useState("");
  const [template, setTemplate] = useState("gameplay");
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState("");

  const handleCreate = async () => {
    if (!modId.trim()) return;
    if (!/^[A-Za-z][A-Za-z0-9_]*$/.test(modId)) { setError("ID must start with a letter, alphanumeric + _ only"); return; }
    setCreating(true); setError("");
    const ok = await createNewMod(modId.trim(), template);
    setCreating(false);
    if (!ok) setError("Creation failed — check build log");
  };

  return (
    <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50 animate-slide-in" onClick={toggleCreateDialog}>
      <div className="bg-bg-panel border border-bg-border rounded-2xl w-[420px] shadow-2xl" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between px-5 py-4 border-b border-bg-border">
          <div className="flex items-center gap-2"><Plus size={16} className="text-accent" /><h2 className="font-semibold">Create New Mod</h2></div>
          <button onClick={toggleCreateDialog} className="text-gray-500 hover:text-white transition"><X size={16} /></button>
        </div>

        <div className="p-5 space-y-4">
          <div>
            <label className="text-2xs text-gray-400 uppercase tracking-wider block mb-1.5">Mod ID</label>
            <input value={modId} onChange={e => setModId(e.target.value)} placeholder="MyAwesomeMod" autoFocus
              className="w-full bg-bg border border-bg-border rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-accent transition" />
          </div>
          <div>
            <label className="text-2xs text-gray-400 uppercase tracking-wider block mb-1.5">Template</label>
            <div className="grid grid-cols-2 gap-2">
              {[["empty", "Empty", "Minimal mod scaffold"], ["gameplay", "Gameplay", "Map + triggers + LudotsCoreMod"]].map(([id, name, desc]) => (
                <button key={id} onClick={() => setTemplate(id)}
                  className={`p-3 rounded-lg border text-left transition ${template === id ? "border-accent bg-accent/5" : "border-bg-border bg-bg hover:bg-bg-hover"}`}>
                  <span className="text-xs font-medium block">{name}</span>
                  <span className="text-2xs text-gray-500">{desc}</span>
                </button>
              ))}
            </div>
          </div>
          {error && <p className="text-xs text-err">{error}</p>}
        </div>

        <div className="flex justify-end gap-2 px-5 py-3 border-t border-bg-border">
          <button onClick={toggleCreateDialog} className="px-4 py-2 text-xs text-gray-400 hover:text-white rounded-lg transition">Cancel</button>
          <button onClick={handleCreate} disabled={creating || !modId.trim()}
            className="px-5 py-2 text-xs font-medium bg-accent text-white rounded-lg hover:bg-accent-hover transition disabled:opacity-40">
            {creating ? "Creating..." : "Create Mod"}
          </button>
        </div>
      </div>
    </div>
  );
}
