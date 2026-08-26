import React from 'react';
import { Link } from 'react-router-dom';
import { HexRenderer } from './HexRenderer';
import { Toolbar } from './Toolbar';

export const EditorLayout: React.FC = () => {
    return (
        <div className="w-screen h-screen bg-black overflow-hidden relative">
            <HexRenderer />
            <Toolbar />
            <Link
                to="/ui-panel-authoring"
                className="absolute bottom-4 right-4 z-50 rounded-md border border-emerald-500/40 bg-black/80 px-3 py-2 font-mono text-xs text-emerald-300 hover:bg-emerald-500/10"
            >
                Panel Authoring →
            </Link>
            <Link
                to="/story-authoring"
                className="absolute bottom-4 right-44 z-50 rounded-md border border-amber-500/40 bg-black/80 px-3 py-2 font-mono text-xs text-amber-300 hover:bg-amber-500/10"
            >
                叙事配置 →
            </Link>
            <Link
                to="/gas-graphs"
                className="absolute bottom-4 right-96 z-50 rounded-md border border-sky-500/40 bg-black/80 px-3 py-2 font-mono text-xs text-sky-300 hover:bg-sky-500/10"
            >
                Graph Editor →
            </Link>
            <Link
                to="/timeline"
                className="absolute bottom-4 right-[34rem] z-50 rounded-md border border-fuchsia-500/40 bg-black/80 px-3 py-2 font-mono text-xs text-fuchsia-300 hover:bg-fuchsia-500/10"
            >
                时间轴 →
            </Link>
        </div>
    );
};
