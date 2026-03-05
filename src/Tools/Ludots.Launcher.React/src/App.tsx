import { useEffect } from "react";
import { useLauncherStore } from "@/stores/launcherStore";
import { Header } from "@/components/Header";
import { PresetBar } from "@/components/PresetBar";
import { ModGrid } from "@/components/ModGrid";
import { ModDetail } from "@/components/ModDetail";
import { Loader2 } from "lucide-react";

export default function App() {
  const { init, loading, bridgeOnline } = useLauncherStore();

  useEffect(() => { init(); }, [init]);

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
        <button onClick={init} className="mt-4 px-6 py-2 bg-accent rounded hover:bg-accent-hover transition">
          Retry
        </button>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-screen">
      <Header />
      <PresetBar />
      <div className="flex flex-1 overflow-hidden">
        <ModGrid />
        <ModDetail />
      </div>
    </div>
  );
}
