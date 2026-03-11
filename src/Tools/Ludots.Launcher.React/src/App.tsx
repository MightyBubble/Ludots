import { useEffect } from "react";
import { Loader2 } from "lucide-react";
import { TopBar } from "@/components/TopBar";
import { ModList } from "@/components/ModList";
import { DetailPanel } from "@/components/DetailPanel";
import { BuildLog } from "@/components/BuildLog";
import { CreateModDialog } from "@/components/CreateModDialog";
import { WorkspacePanel } from "@/components/WorkspacePanel";
import { useLauncherStore } from "@/stores/launcherStore";

export default function App() {
  const { init, loading, bridgeOnline, showCreateDialog, showWorkspace } = useLauncherStore();

  useEffect(() => {
    void init();
  }, [init]);

  if (loading) {
    return (
      <div className="flex h-screen flex-col items-center justify-center gap-3">
        <Loader2 className="animate-spin text-accent" size={32} />
        <span className="text-sm text-gray-500">Connecting to Ludots Bridge...</span>
      </div>
    );
  }

  if (!bridgeOnline) {
    return (
      <div className="flex h-screen flex-col items-center justify-center gap-5 px-6 text-center">
        <div className="rounded-2xl border border-err/20 bg-err/10 px-4 py-3 text-sm text-err">
          Bridge offline
        </div>
        <div className="max-w-md space-y-2">
          <h1 className="text-xl font-bold">Launcher backend unavailable</h1>
          <p className="text-sm text-gray-500">
            Start the editor bridge, then reload the launcher. React UI and CLI now share the same launcher
            backend.
          </p>
        </div>
        <code className="rounded-lg bg-bg-panel px-4 py-2 text-xs text-accent">
          dotnet run --project src/Tools/Ludots.Editor.Bridge
        </code>
        <button
          onClick={() => void init()}
          className="rounded-lg bg-accent px-6 py-2 text-sm text-white transition hover:bg-accent-hover"
        >
          Retry
        </button>
      </div>
    );
  }

  return (
    <div className="flex h-screen flex-col">
      <TopBar />
      {showWorkspace ? <WorkspacePanel /> : null}
      <div className="flex min-h-0 flex-1">
        <ModList />
        <DetailPanel />
      </div>
      <BuildLog />
      {showCreateDialog ? <CreateModDialog /> : null}
    </div>
  );
}
