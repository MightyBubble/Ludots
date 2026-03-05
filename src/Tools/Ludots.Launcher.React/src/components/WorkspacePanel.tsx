import { useState } from "react";
import { useLauncherStore } from "@/stores/launcherStore";
import { Folder, Plus, X } from "lucide-react";

export function WorkspacePanel() {
  const { workspaceSources, showWorkspace, toggleWorkspace, addSource, init } = useLauncherStore();
  const [newPath, setNewPath] = useState("");
  const [error, setError] = useState("");
  const [adding, setAdding] = useState(false);

  if (!showWorkspace) return null;

  const handleAdd = async () => {
    if (!newPath.trim()) return;
    setAdding(true);
    setError("");
    const ok = await addSource(newPath.trim());
    if (ok) {
      setNewPath("");
      await init();
    } else {
      setError("Directory not found or already added");
    }
    setAdding(false);
  };

  return (
    <div className="border-b border-white/5 bg-surface-lighter px-6 py-3">
      <div className="flex items-center justify-between mb-2">
        <h2 className="text-sm font-semibold">Mod Source Directories</h2>
        <button onClick={toggleWorkspace} className="text-gray-500 hover:text-white transition">
          <X size={14} />
        </button>
      </div>

      <div className="space-y-1 mb-3">
        {workspaceSources.map((s, i) => (
          <div key={i} className="flex items-center gap-2 text-xs text-gray-400">
            <Folder size={12} className="text-accent shrink-0" />
            <span className="truncate font-mono">{s}</span>
          </div>
        ))}
        {workspaceSources.length === 0 && (
          <span className="text-xs text-gray-500">No sources configured</span>
        )}
      </div>

      <div className="flex gap-2">
        <input
          value={newPath}
          onChange={(e) => setNewPath(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && handleAdd()}
          placeholder="Absolute path to mod directory..."
          className="flex-1 bg-surface border border-white/10 rounded px-3 py-1.5 text-xs focus:outline-none focus:border-accent"
        />
        <button
          onClick={handleAdd}
          disabled={adding}
          className="flex items-center gap-1 px-3 py-1.5 bg-accent text-white text-xs rounded hover:bg-accent-hover transition disabled:opacity-50"
        >
          <Plus size={12} />
          Add
        </button>
      </div>
      {error && <p className="text-[10px] text-red-400 mt-1">{error}</p>}
    </div>
  );
}
