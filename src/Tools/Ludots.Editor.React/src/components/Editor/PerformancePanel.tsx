import React from 'react';
import { Activity, Cpu, HardDrive, Box } from 'lucide-react';

export interface PerformanceStats {
    visibleChunks: number;
    totalChunks: number;
    cacheSize: number;
    cacheHitRate: number;
    frameBuildCount: number;
    frameEvictCount: number;
    visibleTriangles: number;
    drawTimeMs: number;
    fps: number;
    streamerMemoryMB: number;
}

interface PerformancePanelProps {
    stats: PerformanceStats;
    visible: boolean;
    onToggle: () => void;
}

/**
 * PerformancePanel - heads-up display for rendering and streaming metrics.
 * Designed for large-world debugging and nav bake performance monitoring.
 */
export const PerformancePanel: React.FC<PerformancePanelProps> = ({ stats, visible, onToggle }) => {
    if (!visible) {
        return (
            <button
                onClick={onToggle}
                className="absolute bottom-4 right-4 p-2 rounded bg-gray-900/80 text-gray-400 hover:text-white z-50"
                title="Show Performance Panel"
            >
                <Activity size={16} />
            </button>
        );
    }

    const rows: Array<{ label: string; value: string; icon: React.ReactNode; color?: string }> = [
        { label: 'FPS', value: stats.fps.toFixed(0), icon: <Activity size={12} /> },
        { label: 'Draw', value: `${stats.drawTimeMs.toFixed(1)}ms`, icon: <Cpu size={12} /> },
        { label: 'Visible Chunks', value: `${stats.visibleChunks}/${stats.totalChunks}`, icon: <Box size={12} /> },
        {
            label: 'Cache',
            value: `${stats.cacheSize} (${(stats.cacheHitRate * 100).toFixed(0)}% hit)`,
            icon: <HardDrive size={12} />,
            color: stats.cacheHitRate < 0.5 ? 'text-yellow-400' : undefined,
        },
        { label: 'Builds/Evicts', value: `+${stats.frameBuildCount} / -${stats.frameEvictCount}`, icon: <Cpu size={12} /> },
        {
            label: 'Triangles',
            value: stats.visibleTriangles.toLocaleString(),
            icon: <Box size={12} />,
        },
        {
            label: 'Memory',
            value: `${stats.streamerMemoryMB.toFixed(1)} MB`,
            icon: <HardDrive size={12} />,
            color: stats.streamerMemoryMB > 500 ? 'text-red-400' : undefined,
        },
    ];

    return (
        <div className="absolute bottom-4 right-4 bg-gray-900/90 border border-gray-700/50 rounded-lg p-3 z-50 min-w-[220px] backdrop-blur-sm">
            <div className="flex items-center justify-between mb-2">
                <span className="text-xs font-semibold text-gray-400 uppercase tracking-wider">Performance</span>
                <button
                    onClick={onToggle}
                    className="text-gray-500 hover:text-gray-300"
                    title="Hide"
                >
                    ×
                </button>
            </div>
            <div className="space-y-1">
                {rows.map((row) => (
                    <div key={row.label} className="flex items-center justify-between text-xs">
                        <span className="flex items-center gap-1 text-gray-500">
                            {row.icon}
                            {row.label}
                        </span>
                        <span className={`font-mono ${row.color ?? 'text-gray-200'}`}>
                            {row.value}
                        </span>
                    </div>
                ))}
            </div>
        </div>
    );
};
