import { useLauncherStore } from "@/stores/launcherStore";
import { Terminal, ChevronDown, ChevronUp } from "lucide-react";
import { useState } from "react";

export function BuildLog() {
  const { buildLog, building } = useLauncherStore();
  const [expanded, setExpanded] = useState(false);

  if (!buildLog && !building) return null;

  return (
    <div className="border-t border-white/5 bg-surface">
      <button
        onClick={() => setExpanded(!expanded)}
        className="flex items-center gap-2 w-full px-4 py-1.5 text-xs text-gray-400 hover:text-gray-200"
      >
        <Terminal size={12} />
        <span>Build Output</span>
        {building && <span className="text-yellow-400 animate-pulse">Building...</span>}
        {expanded ? <ChevronDown size={12} className="ml-auto" /> : <ChevronUp size={12} className="ml-auto" />}
      </button>
      {expanded && (
        <pre className="px-4 pb-3 text-[10px] text-gray-500 font-mono max-h-48 overflow-auto whitespace-pre-wrap">
          {buildLog || "No output yet."}
        </pre>
      )}
    </div>
  );
}
