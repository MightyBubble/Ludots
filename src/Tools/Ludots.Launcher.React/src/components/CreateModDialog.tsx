import { useState } from "react";
import { useLauncherStore } from "@/stores/launcherStore";
import { X } from "lucide-react";

interface Props { onClose: () => void; }

export function CreateModDialog({ onClose }: Props) {
  const { createNewMod } = useLauncherStore();
  const [modId, setModId] = useState("");
  const [template, setTemplate] = useState("gameplay");
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState("");

  const handleCreate = async () => {
    if (!modId.trim()) return;
    if (!/^[A-Za-z][A-Za-z0-9_]*$/.test(modId)) { setError("ID must be alphanumeric (start with letter)"); return; }
    setCreating(true); setError("");
    const ok = await createNewMod(modId.trim(), template);
    setCreating(false);
    if (ok) onClose();
    else setError("Creation failed — check build log");
  };

  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50" onClick={onClose}>
      <div className="bg-surface-light border border-white/10 rounded-xl w-[400px] p-6" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-bold">Create New Mod</h2>
          <button onClick={onClose} className="text-gray-500 hover:text-white"><X size={16} /></button>
        </div>

        <div className="space-y-3">
          <div>
            <label className="text-xs text-gray-400 block mb-1">Mod ID</label>
            <input value={modId} onChange={e => setModId(e.target.value)} placeholder="MyAwesomeMod"
              className="w-full bg-surface border border-white/10 rounded px-3 py-2 text-sm focus:outline-none focus:border-accent" />
          </div>
          <div>
            <label className="text-xs text-gray-400 block mb-1">Template</label>
            <select value={template} onChange={e => setTemplate(e.target.value)}
              className="w-full bg-surface border border-white/10 rounded px-3 py-2 text-sm focus:outline-none focus:border-accent">
              <option value="empty">Empty (minimal)</option>
              <option value="gameplay">Gameplay (with map + triggers)</option>
            </select>
          </div>
          {error && <p className="text-xs text-red-400">{error}</p>}
        </div>

        <div className="flex justify-end gap-2 mt-6">
          <button onClick={onClose} className="px-4 py-2 text-sm text-gray-400 hover:text-white transition">Cancel</button>
          <button onClick={handleCreate} disabled={creating}
            className="px-6 py-2 text-sm bg-accent text-white rounded hover:bg-accent-hover transition disabled:opacity-50">
            {creating ? "Creating..." : "Create"}
          </button>
        </div>
      </div>
    </div>
  );
}
