import React from 'react';
import { useEditorStore } from './EditorStore';
import { Grid3X3, Eye, EyeOff, Layers, FilePlus, Upload, Download, FolderOpen, RefreshCw, Save } from 'lucide-react';

export const TopBar: React.FC = () => {
    const store = useEditorStore;
    const {
        mods, selectedModId, selectMod, maps, selectedMapId, selectMap,
        loadSelectedMap, saveSelectedMap,
        showGrid, toggleGrid, showChunkBorders, toggleChunkBorders,
        showNavMesh, toggleNavMesh, showLogicTerrain, toggleLogicTerrain,
        logicTerrainMode, setLogicTerrainMode, initMap,
    } = store();

    const [showNewMap, setShowNewMap] = React.useState(false);
    const [newW, setNewW] = React.useState(8);
    const [newH, setNewH] = React.useState(8);

    // Auto-load mods on mount
    React.useEffect(() => { store.getState().refreshMods().catch(() => {}); }, []);

    const handleUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0]; if (!file) return;
        const reader = new FileReader();
        reader.onload = (ev) => {
            const buffer = ev.target?.result as ArrayBuffer;
            if (!buffer || buffer.byteLength < 9) return;
            const view = new DataView(buffer);
            const w = view.getInt32(0, true); const h = view.getInt32(4, true);
            if (view.getUint8(8) !== 4) return;
            store.getState().loadMap(new Uint8Array(buffer.slice(9)), w, h);
        };
        reader.readAsArrayBuffer(file);
    };

    const handleDownload = () => {
        const s = store.getState();
        const data = s.terrain.serialize();
        const header = new Uint8Array(9);
        const v = new DataView(header.buffer);
        v.setInt32(0, s.terrain.widthChunks, true); v.setInt32(4, s.terrain.heightChunks, true); v.setUint8(8, 4);
        const blob = new Blob([header, data], { type: 'application/octet-stream' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a'); a.href = url; a.download = 'map_data.bin'; a.click(); URL.revokeObjectURL(url);
    };

    const handleSaveToMod = async () => {
        try { await saveSelectedMap(); } catch {}
    };

    const handleRefreshMods = async () => {
        try { await store.getState().refreshMods(); } catch {}
    };

    return (
        <>
            <div className="absolute top-0 left-0 right-0 h-11 bg-slate-950/95 backdrop-blur-xl border-b border-white/10 flex items-center px-3 gap-2 z-50">
                <div className="flex items-center gap-1.5">
                    <FolderOpen size={14} className="text-slate-500 shrink-0" />
                    <select value={selectedModId ?? ''}
                        onChange={(e) => { const v = e.target.value; if (v) selectMod(v); }}
                        className="bg-transparent border border-slate-700/50 rounded-md px-2 py-1 text-xs text-slate-300 w-36">
                        <option value="">Mod...</option>
                        {mods.map((m) => <option key={m.id} value={m.id}>{m.id}</option>)}
                    </select>
                    <button onClick={handleRefreshMods} className="p-1 rounded text-slate-600 hover:text-slate-400" title="Refresh mods">
                        <RefreshCw size={12} />
                    </button>
                    <select value={selectedMapId ?? ''}
                        onChange={(e) => { const v = e.target.value; if (v) selectMap(v); }}
                        className="bg-transparent border border-slate-700/50 rounded-md px-2 py-1 text-xs text-slate-300 w-36">
                        <option value="">Map...</option>
                        {maps.map((m) => <option key={m} value={m}>{m}</option>)}
                    </select>
                    <button onClick={loadSelectedMap} disabled={!selectedModId || !selectedMapId}
                        className="px-2.5 py-1 rounded-md bg-indigo-600 text-white text-[11px] font-medium hover:bg-indigo-500 disabled:opacity-30 transition-all">
                        Load
                    </button>
                    <button onClick={handleSaveToMod} disabled={!selectedModId || !selectedMapId}
                        className="px-2 py-1 rounded-md bg-emerald-700 text-white text-[11px] hover:bg-emerald-600 disabled:opacity-30 transition-all"
                        title="Save map to mod">
                        <Save size={11} className="inline mr-0.5" />Save
                    </button>
                    <span className="text-slate-700 select-none">|</span>
                    <button onClick={() => setShowNewMap(true)}
                        className="px-2 py-1 rounded-md bg-slate-800/80 text-slate-300 text-[11px] hover:bg-slate-700 transition-all">
                        <FilePlus size={12} className="inline mr-0.5" />New
                    </button>
                    <label className="px-2 py-1 rounded-md bg-slate-800/80 text-slate-300 text-[11px] hover:bg-slate-700 cursor-pointer transition-all">
                        <Upload size={12} className="inline mr-0.5" />Load
                        <input type="file" className="hidden" onChange={handleUpload} />
                    </label>
                    <button onClick={handleDownload}
                        className="px-2 py-1 rounded-md bg-slate-800/80 text-slate-300 text-[11px] hover:bg-slate-700 transition-all">
                        <Download size={12} className="inline mr-0.5" />Save
                    </button>
                </div>

                <div className="flex-1" />

                <div className="flex items-center gap-0.5">
                    <button onClick={toggleGrid} className={`px-2 py-1 rounded-md transition-all ${showGrid ? 'bg-indigo-500/20 text-indigo-400' : 'text-slate-600 hover:text-slate-400'}`} title="Grid">
                        <Grid3X3 size={14} />
                    </button>
                    <button onClick={toggleChunkBorders} className={`px-2 py-1 rounded-md text-[11px] font-mono transition-all ${showChunkBorders ? 'bg-emerald-500/20 text-emerald-400' : 'text-slate-600 hover:text-slate-400'}`} title="Chunks">
                        ▦
                    </button>
                    <button onClick={toggleNavMesh} className={`px-2 py-1 rounded-md transition-all ${showNavMesh ? 'bg-green-500/20 text-green-400' : 'text-slate-600 hover:text-slate-400'}`} title="NavMesh">
                        {showNavMesh ? <Eye size={14} /> : <EyeOff size={14} />}
                    </button>
                    <button onClick={toggleLogicTerrain} className={`px-2 py-1 rounded-md transition-all ${showLogicTerrain ? 'bg-cyan-500/20 text-cyan-400' : 'text-slate-600 hover:text-slate-400'}`} title="LogicTerrain">
                        <Layers size={14} />
                    </button>
                    {showLogicTerrain && (
                        <select value={logicTerrainMode} onChange={(e) => setLogicTerrainMode(e.target.value as any)}
                            className="bg-transparent border border-slate-700/50 rounded-md px-1.5 py-0.5 text-[10px] text-slate-400">
                            <option value="heightLevel">H</option>
                            <option value="surfaceFlags">F</option>
                            <option value="areaId">A</option>
                            <option value="combined">C</option>
                        </select>
                    )}
                </div>
            </div>

            {showNewMap && (
                <div className="fixed inset-0 bg-black/60 z-50 flex items-center justify-center backdrop-blur-sm" onClick={() => setShowNewMap(false)}>
                    <div className="bg-slate-900 p-6 rounded-2xl border border-slate-700 shadow-2xl w-80" onClick={(e) => e.stopPropagation()}>
                        <h3 className="text-base font-bold text-slate-100 mb-4">Create New Map</h3>
                        <div className="space-y-4">
                            <label className="text-[11px] text-slate-400 block">Width (Chunks)
                                <input type="number" min="1" max="64" value={newW} onChange={(e) => setNewW(parseInt(e.target.value) || 1)}
                                    className="mt-1 w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-slate-200" />
                            </label>
                            <label className="text-[11px] text-slate-400 block">Height (Chunks)
                                <input type="number" min="1" max="64" value={newH} onChange={(e) => setNewH(parseInt(e.target.value) || 1)}
                                    className="mt-1 w-full bg-slate-800 border border-slate-700 rounded-lg px-3 py-1.5 text-sm text-slate-200" />
                            </label>
                            <div className="flex gap-2 pt-2">
                                <button onClick={() => setShowNewMap(false)} className="flex-1 py-2.5 rounded-xl bg-slate-800 text-slate-300 text-sm">Cancel</button>
                                <button onClick={() => { initMap(newW, newH); setShowNewMap(false); }}
                                    className="flex-1 py-2.5 rounded-xl bg-indigo-600 text-white text-sm font-semibold">Create</button>
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </>
    );
};
