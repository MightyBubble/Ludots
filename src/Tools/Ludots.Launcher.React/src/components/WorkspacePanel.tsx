import { useState } from "react";
import { useLauncherStore } from "@/stores/launcherStore";
import { Folder, Plus, X } from "lucide-react";

export function WorkspacePanel() {
  const { workspaceSources, toggleWorkspace, addSource, init } = useLauncherStore();
  const [newPath, setNewPath] = useState("");
  const [error, setError] = useState("");
  const [adding, setAdding] = useState(false);

  const handleAdd = async () => {
    if (!newPath.trim()) return;
    setAdding(true); setError("");
    const ok = await addSource(newPath.trim());
    if (ok) { setNewPath(""); await init(); }
    else setError("Directory not found or already added");
    setAdding(false);
  };

  return (
    <div className="border-b border-bg-border bg-bg-panel px-4 py-2.5 animate-slide-in">
      <div className="flex items-center justify-between mb-2">
        <h3 className="text-xs font-semibold">Mod Source Directories</h3>
        <button onClick={toggleWorkspace} className="text-gray-500 hover:text-white transition"><X size={13} /></button>
      </div>
      <div className="space-y-1 mb-2">
        {workspaceSources.map((s, i) => (
          <div key={i} className="flex items-center gap-1.5 text-2xs text-gray-400">
            <Folder size={10} className="text-accent shrink-0" />
            <span className="truncate font-mono">{s}</span>
          </div>
        ))}
      </div>
      <div className="flex gap-2">
        <input value={newPath} onChange={e => setNewPath(e.target.value)} onKeyDown={e => e.key === "Enter" && handleAdd()}
          placeholder="Absolute path to mod directory..."
          className="flex-1 bg-bg border border-bg-border rounded-lg px-3 py-1.5 text-2xs focus:outline-none focus:border-accent/50 transition" />
        <button onClick={handleAdd} disabled={adding}
          className="flex items-center gap-1 px-3 py-1.5 bg-accent text-white text-2xs rounded-lg hover:bg-accent-hover transition disabled:opacity-50">
          <Plus size={10} />Add
        </button>
      </div>
      {error && <p className="text-2xs text-err mt-1">{error}</p>}
    </div>
  );
}
