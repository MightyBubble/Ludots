import { Gamepad2, Wifi } from "lucide-react";

export function Header() {
  return (
    <header className="flex items-center justify-between px-6 py-3 bg-surface-light border-b border-white/5">
      <div className="flex items-center gap-3">
        <Gamepad2 className="text-accent" size={28} />
        <h1 className="text-xl font-bold tracking-wide">LUDOTS LAUNCHER</h1>
      </div>
      <div className="flex items-center gap-2 text-xs text-green-400">
        <Wifi size={14} />
        <span>Bridge Connected</span>
      </div>
    </header>
  );
}
