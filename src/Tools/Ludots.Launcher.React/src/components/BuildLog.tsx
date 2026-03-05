import { useLauncherStore } from "@/stores/launcherStore";
import { cn } from "@/lib/utils";
import { Terminal, ChevronUp, ChevronDown, Check, X, Loader2 } from "lucide-react";
import { useState, useRef, useEffect } from "react";

export function BuildLog() {
  const { buildLog, buildState, buildProgress, buildTotal } = useLauncherStore();
  const [expanded, setExpanded] = useState(false);
  const logRef = useRef<HTMLPreElement>(null);

  useEffect(() => {
    if (buildState === "building") setExpanded(true);
  }, [buildState]);

  useEffect(() => {
    if (logRef.current) logRef.current.scrollTop = logRef.current.scrollHeight;
  }, [buildLog]);

  if (buildState === "idle" && !buildLog) return null;

  const pct = buildTotal > 0 ? (buildProgress / buildTotal) * 100 : 0;

  return (
    <div className="border-t border-bg-border bg-bg-panel shrink-0">
      {/* Progress bar */}
      {buildState === "building" && (
        <div className="h-0.5 bg-bg">
          <div className="h-full bg-accent transition-all duration-300" style={{ width: `${pct}%` }} />
        </div>
      )}

      {/* Header */}
      <button onClick={() => setExpanded(!expanded)}
        className="flex items-center gap-2 w-full px-4 py-1.5 text-2xs text-gray-400 hover:text-gray-200 transition">
        <Terminal size={11} />
        <span>Build</span>
        {buildState === "building" && <>
          <Loader2 size={10} className="animate-spin text-accent" />
          <span className="text-accent">{buildProgress}/{buildTotal}</span>
        </>}
        {buildState === "done" && <span className="flex items-center gap-0.5 text-ok"><Check size={10} />Done</span>}
        {buildState === "error" && <span className="flex items-center gap-0.5 text-err"><X size={10} />Errors</span>}
        <span className="ml-auto">{expanded ? <ChevronDown size={11} /> : <ChevronUp size={11} />}</span>
      </button>

      {expanded && (
        <pre ref={logRef}
          className={cn("px-4 pb-2 text-2xs font-mono max-h-40 overflow-auto whitespace-pre-wrap",
            buildState === "error" ? "text-err/70" : "text-gray-500")}>
          {buildLog || "No output."}
        </pre>
      )}
    </div>
  );
}
