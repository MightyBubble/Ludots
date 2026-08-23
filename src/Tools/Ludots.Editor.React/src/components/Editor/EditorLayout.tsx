import React from 'react';
import { HexRenderer } from './HexRenderer';
import { TopBar } from './TopBar';
import { Toolbar } from './Toolbar';
import { NavPanel } from './NavPanel';
import { Minimap } from './Minimap';
import { DevPanel } from './DevPanel';
import { PerformancePanel } from './PerformancePanel';
import { useEditorStore } from './EditorStore';

export const EditorLayout: React.FC = () => {
    const showPerf = useEditorStore((s) => s.showPerformance);
    const perfStats = useEditorStore((s) => s.perfStats);
    const togglePerf = () => useEditorStore.setState((s) => ({ showPerformance: !s.showPerformance }));

    return (
        <div className="w-screen h-screen bg-black overflow-hidden relative">
            <HexRenderer />
            <TopBar />
            <Toolbar />
            <NavPanel />
            <Minimap />
            <DevPanel />
            <PerformancePanel stats={perfStats} visible={showPerf} onToggle={togglePerf} />
        </div>
    );
};
