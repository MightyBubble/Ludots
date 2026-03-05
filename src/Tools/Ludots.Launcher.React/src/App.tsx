import { useEffect, useState } from "react";
import { useLauncherStore } from "@/stores/launcherStore";
import { Header } from "@/components/Header";
import { PresetBar } from "@/components/PresetBar";
import { WorkspacePanel } from "@/components/WorkspacePanel";
import { ModGrid } from "@/components/ModGrid";
import { ModDetail } from "@/components/ModDetail";
import { DepGraph } from "@/components/DepGraph";
import { BuildLog } from "@/components/BuildLog";
import { Loader2, GitBranch } from "lucide-react";
import { cn } from "@/lib/utils";

export default function App() {
  const { init, loading, bridgeOnline } = useLauncherStore();
  const [showGraph, setShowGraph] = useState(true);

  useEffect(() => {
    init();
  }, [init]);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-screen gap-3 text-gray-400">
        <Loader2 className="animate-spin" size={24} />
        <span>Connecting to Ludots Bridge...</span>
      </div>
    );
  }

  if (!bridgeOnline) {
    return (
      <div className="flex flex-col items-center justify-center h-screen gap-4">
        <div className="text-6xl">⚡</div>
        <h1 className="text-2xl font-bold">Bridge Offline</h1>
        <p className="text-gray-400 max-w-md text-center">
          Start the Ludots Editor Bridge first:
        </p>
        <code className="bg-surface-lighter px-4 py-2 rounded font-mono text-sm text-accent">
          dotnet run --project src/Tools/Ludots.Editor.Bridge
        </code>
        <button
          onClick={init}
          className="mt-4 px-6 py-2 bg-accent rounded hover:bg-accent-hover transition"
        >
          Retry
        </button>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-screen">
      <Header />
      <WorkspacePanel />
      <PresetBar />
      <div className="flex flex-1 overflow-hidden">
        {showGraph && (
          <div className="w-[300px] shrink-0 border-r border-white/5 bg-surface flex flex-col">
            <div className="flex items-center justify-between px-3 py-2 border-b border-white/5">
              <div className="flex items-center gap-1.5 text-xs text-gray-400">
                <GitBranch size={12} />
                <span>Dependency Graph</span>
              </div>
              <button
                onClick={() => setShowGraph(false)}
                className="text-[10px] text-gray-500 hover:text-gray-300"
              >
                hide
              </button>
            </div>
            <DepGraph />
          </div>
        )}
        {!showGraph && (
          <button
            onClick={() => setShowGraph(true)}
            className={cn(
              "w-8 shrink-0 border-r border-white/5 bg-surface",
              "flex items-center justify-center hover:bg-surface-light transition"
            )}
            title="Show dependency graph"
          >
            <GitBranch size={14} className="text-gray-500" />
          </button>
        )}
        <ModGrid />
        <ModDetail />
      </div>
      <BuildLog />
    </div>
  );
}
