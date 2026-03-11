import { useState } from "react";
import { Plus, X } from "lucide-react";
import { useLauncherStore } from "@/stores/launcherStore";

export function CreateModDialog() {
  const { createNewMod, toggleCreateDialog } = useLauncherStore();
  const [modId, setModId] = useState("");
  const [template, setTemplate] = useState("gameplay");
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState("");

  const handleCreate = async () => {
    if (!modId.trim()) {
      return;
    }

    if (!/^[A-Za-z][A-Za-z0-9_]*$/.test(modId)) {
      setError("Mod ID must start with a letter and contain only letters, numbers, or underscore.");
      return;
    }

    setCreating(true);
    setError("");
    const ok = await createNewMod(modId.trim(), template);
    setCreating(false);
    if (!ok) {
      setError("Creation failed. Check the build log for backend output.");
    }
  };

  return (
    <div
      className="animate-slide-in fixed inset-0 z-50 flex items-center justify-center bg-black/70"
      onClick={toggleCreateDialog}
    >
      <div
        className="w-[440px] rounded-2xl border border-bg-border bg-bg-panel shadow-2xl"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-bg-border px-5 py-4">
          <div className="flex items-center gap-2">
            <Plus size={16} className="text-accent" />
            <h2 className="font-semibold">Create New Mod</h2>
          </div>
          <button onClick={toggleCreateDialog} className="text-gray-500 transition hover:text-white">
            <X size={16} />
          </button>
        </div>

        <div className="space-y-4 p-5">
          <div>
            <label className="mb-1.5 block text-[11px] uppercase tracking-[0.25em] text-gray-400">Mod ID</label>
            <input
              value={modId}
              onChange={(event) => setModId(event.target.value)}
              placeholder="MyAwesomeMod"
              autoFocus
              className="w-full rounded-lg border border-bg-border bg-bg px-3 py-2 text-sm transition focus:border-accent focus:outline-none"
            />
          </div>

          <div>
            <label className="mb-1.5 block text-[11px] uppercase tracking-[0.25em] text-gray-400">Template</label>
            <div className="grid grid-cols-2 gap-2">
              {[
                ["empty", "Empty", "Minimal mod scaffold"],
                ["gameplay", "Gameplay", "Map, triggers, and core gameplay references"],
              ].map(([id, name, description]) => (
                <button
                  key={id}
                  onClick={() => setTemplate(id)}
                  className={`rounded-lg border p-3 text-left transition ${
                    template === id ? "border-accent bg-accent/5" : "border-bg-border bg-bg hover:bg-bg-hover"
                  }`}
                >
                  <span className="block text-sm font-medium">{name}</span>
                  <span className="text-[11px] text-gray-500">{description}</span>
                </button>
              ))}
            </div>
          </div>

          {error ? <p className="text-xs text-err">{error}</p> : null}
        </div>

        <div className="flex justify-end gap-2 border-t border-bg-border px-5 py-3">
          <button onClick={toggleCreateDialog} className="rounded-lg px-4 py-2 text-xs text-gray-400 transition hover:text-white">
            Cancel
          </button>
          <button
            onClick={() => void handleCreate()}
            disabled={creating || !modId.trim()}
            className="rounded-lg bg-accent px-5 py-2 text-xs font-medium text-white transition hover:bg-accent-hover disabled:opacity-40"
          >
            {creating ? "Creating..." : "Create Mod"}
          </button>
        </div>
      </div>
    </div>
  );
}
