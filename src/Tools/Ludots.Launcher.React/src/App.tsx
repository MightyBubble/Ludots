import { useEffect } from "react";
import { useLauncherStore } from "@/stores/launcherStore";
import { TopBar } from "@/components/TopBar";
import { ModList } from "@/components/ModList";
import { DetailPanel } from "@/components/DetailPanel";
import { BuildLog } from "@/components/BuildLog";
import { CreateModDialog } from "@/components/CreateModDialog";
import { WorkspacePanel } from "@/components/WorkspacePanel";
import { Loader2 } from "lucide-react";

export default function App() {
  const { init, loading, bridgeOnline, showCreateDialog, showWorkspace } = useLauncherStore();
  useEffect(() => { init(); }, [init]);

  if (loading) return (
    <div className="flex flex-col items-center justify-center h-screen gap-3">
      <Loader2 className="animate-spin text-accent" size={32} />
      <span className="text-sm text-gray-500">Connecting to Ludots Bridge...</span>
    </div>
  );

  if (!bridgeOnline) return (
    <div className="flex flex-col items-center justify-center h-screen gap-5">
      <div className="text-5xl">⚡</div>
      <h1 className="text-xl font-bold">Bridge Offline</h1>
      <p className="text-gray-500 text-sm text-center max-w-sm">Start the Ludots Editor Bridge to use the launcher.</p>
      <code className="bg-bg-panel px-4 py-2 rounded-lg text-xs font-mono text-accent">
        dotnet run --project src/Tools/Ludots.Editor.Bridge
      </code>
      <button onClick={init} className="px-6 py-2 bg-accent text-white text-sm rounded-lg hover:bg-accent-hover transition">
        Retry
      </button>
    </div>
  );

  return (
    <div className="flex flex-col h-screen">
      <TopBar />
      {showWorkspace && <WorkspacePanel />}
      <div className="flex flex-1 min-h-0">
        <ModList />
        <DetailPanel />
      </div>
      <BuildLog />
      {showCreateDialog && <CreateModDialog />}
    </div>
  );
}
