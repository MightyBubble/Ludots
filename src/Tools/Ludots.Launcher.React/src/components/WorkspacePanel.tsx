import { useState } from "react";
import { Folder, Plus, X } from "lucide-react";
import { useLauncherStore } from "@/stores/launcherStore";

export function WorkspacePanel() {
  const { workspaceSources, toggleWorkspace, addSource } = useLauncherStore();
  const [newPath, setNewPath] = useState("");
  const [error, setError] = useState("");
  const [adding, setAdding] = useState(false);

  const handleAdd = async () => {
    if (!newPath.trim()) {
      return;
    }

    setAdding(true);
    setError("");
    const ok = await addSource(newPath.trim());
    if (ok) {
      setNewPath("");
    } else {
      setError("Directory not found or launcher config rejected the source.");
    }
    setAdding(false);
  };

  return (
    <div className="animate-slide-in border-b border-bg-border bg-bg-panel px-4 py-3">
      <div className="mb-3 flex items-center justify-between">
        <div>
          <h3 className="text-sm font-semibold">Mod Source Directories</h3>
          <p className="text-[11px] text-gray-500">Launcher backend discovers mods from these roots.</p>
        </div>
        <button onClick={toggleWorkspace} className="text-gray-500 transition hover:text-white">
          <X size={14} />
        </button>
      </div>

      <div className="mb-3 space-y-1">
        {workspaceSources.map((source) => (
          <div key={source} className="flex items-center gap-2 text-xs text-gray-400">
            <Folder size={11} className="shrink-0 text-accent" />
            <span className="truncate font-mono">{source}</span>
          </div>
        ))}
      </div>

      <div className="flex gap-2">
        <input
          value={newPath}
          onChange={(event) => setNewPath(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              void handleAdd();
            }
          }}
          placeholder="Absolute path to a mod root directory"
          className="flex-1 rounded-lg border border-bg-border bg-bg px-3 py-2 text-xs transition focus:border-accent/60 focus:outline-none"
        />
        <button
          onClick={() => void handleAdd()}
          disabled={adding}
          className="flex items-center gap-1 rounded-lg bg-accent px-3 py-2 text-xs text-white transition hover:bg-accent-hover disabled:opacity-50"
        >
          <Plus size={11} />
          Add
        </button>
      </div>

      {error ? <p className="mt-2 text-xs text-err">{error}</p> : null}
    </div>
  );
}
