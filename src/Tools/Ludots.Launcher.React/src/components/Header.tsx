import { useState } from "react";
import { useLauncherStore } from "@/stores/launcherStore";
import { Gamepad2, Wifi, FolderPlus, Plus } from "lucide-react";
import { CreateModDialog } from "@/components/CreateModDialog";

export function Header() {
  const { toggleWorkspace } = useLauncherStore();
  const [showCreate, setShowCreate] = useState(false);

  return (
    <header className="flex items-center justify-between px-6 py-3 bg-surface-light border-b border-white/5">
      <div className="flex items-center gap-3">
        <Gamepad2 className="text-accent" size={28} />
        <h1 className="text-xl font-bold tracking-wide">LUDOTS LAUNCHER</h1>
      </div>
      <div className="flex items-center gap-4">
        <button
          onClick={() => setShowCreate(true)}
          className="flex items-center gap-1.5 text-xs text-gray-400 hover:text-accent transition px-2 py-1 rounded hover:bg-white/5"
        >
          <Plus size={14} />
          <span>New Mod</span>
        </button>
        <button
          onClick={toggleWorkspace}
          className="flex items-center gap-1.5 text-xs text-gray-400 hover:text-accent transition px-2 py-1 rounded hover:bg-white/5"
        >
          <FolderPlus size={14} />
          <span>Mod Sources</span>
        </button>
        <div className="flex items-center gap-2 text-xs text-green-400">
          <Wifi size={14} />
          <span>Bridge Connected</span>
        </div>
      </div>
      {showCreate && <CreateModDialog onClose={() => setShowCreate(false)} />}
    </header>
  );
}
