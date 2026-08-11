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
        </div>
    );
};
