import React from 'react';
import { useEditorStore, ToolCategory, ToolMode } from './EditorStore';
import { Download, Upload, Mountain, Droplets, TreePine, Map as MapIcon, ArrowUp, ArrowDown, Type, Layers, PaintBucket, Grid, BoxSelect, Footprints, Flag } from 'lucide-react';
import { readNavTile } from '../../Core/NavMesh/NavTileBinary';

export const Toolbar: React.FC = () => {
    const { 
        activeCategory, setCategory, 
        activeMode, setMode, 
        brushSize, setBrushSize, 
        brushValue, setBrushValue,
        activeLayer, setActiveLayer,
        terrain, loadMap, initMap,
        bridgeBaseUrl,
        mods, selectedModId, maps, selectedMapId,
        refreshMods, selectMod, selectMap, loadSelectedMap, saveSelectedMap,
        templates, selectedTemplateId, selectTemplate,
        spawnEntities, selectedEntityIndex, updateSelectedEntityOverridesJson, deleteSelectedEntityOverride,
        showGrid, toggleGrid,
        showChunkBorders, toggleChunkBorders,
        showNavMesh, toggleNavMesh,
        bakeNavMesh, // Added
        setBakedNavTiles,
        clearBakedNavTiles,
        bakedNavTiles,
        navDirtyChunks,
        clearNavDirty,
        setLoading,
        loadingState 
    } = useEditorStore();

    const [showNewMap, setShowNewMap] = React.useState(false);
    const [newWidth, setNewWidth] = React.useState(8);
    const [newHeight, setNewHeight] = React.useState(8);
    const [mapId, setMapId] = React.useState('entry');
    const [navScope, setNavScope] = React.useState<'dirty' | 'full'>('dirty');
    const [navIncludeNeighbors, setNavIncludeNeighbors] = React.useState(true);
    const [navParallel, setNavParallel] = React.useState(true);
    const [navTileVersion, setNavTileVersion] = React.useState(1);
    const navAbortRef = React.useRef<AbortController | null>(null);

    React.useEffect(() => {
        let cancelled = false;
        const run = async () => {
            try {
                await useEditorStore.getState().refreshMods();
                if (cancelled) return;
                const s = useEditorStore.getState();
                if (!s.selectedModId && s.mods.length > 0) {
                    await s.selectMod(s.mods[0].id);
                }
            } catch {
            }
        };
        run();
        return () => { cancelled = true; };
    }, []);

    React.useEffect(() => {
        if (selectedMapId) setMapId(selectedMapId);
    }, [selectedMapId]);

    const downloadBlob = (filename: string, blob: Blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(url);
    };

    const formatTimestamp = () => {
        const d = new Date();
        const pad = (n: number) => n.toString().padStart(2, '0');
        return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
    };

    const handleNewMap = () => {
        initMap(newWidth, newHeight);
        setShowNewMap(false);
    };

    const categories: { id: ToolCategory, icon: React.ReactNode, label: string }[] = [
        { id: 'Height', icon: <Mountain size={18} />, label: 'Height' },
        { id: 'Water', icon: <Droplets size={18} />, label: 'Water' },
        { id: 'Biome', icon: <MapIcon size={18} />, label: 'Biome' },
        { id: 'Vegetation', icon: <TreePine size={18} />, label: 'Veg' },
        { id: 'Ramp', icon: <Type size={18} />, label: 'Ramp' },
        { id: 'Layers', icon: <Layers size={18} />, label: 'Layers' },
        { id: 'Territory', icon: <Flag size={18} />, label: 'Territory' },
        { id: 'Entities', icon: <BoxSelect size={18} />, label: 'Ent' },
    ];

    const modes: { id: ToolMode, icon: React.ReactNode, label: string }[] = [
        { id: 'Set', icon: <div className="w-4 h-4 bg-current rounded-full" />, label: 'Set' },
        { id: 'Raise', icon: <ArrowUp size={18} />, label: 'Raise' },
        { id: 'Lower', icon: <ArrowDown size={18} />, label: 'Lower' },
        { id: 'Bucket', icon: <PaintBucket size={18} />, label: 'Bucket' }, // Added Bucket
    ];

    const buildMapBlob = () => {
        const data = terrain.serialize();
        // Header: width(4), height(4), stride(1)
        
        const header = new Uint8Array(9);
        const view = new DataView(header.buffer);
        view.setInt32(0, terrain.widthChunks, true);
        view.setInt32(4, terrain.heightChunks, true);
        view.setUint8(8, 2); // Stride 2

        return new Blob([header, data], { type: 'application/octet-stream' });
    };

    const handleDownload = () => {
        downloadBlob('map_data.bin', buildMapBlob());
    };

    const handleBakeNavTiles = async () => {
        const ts = formatTimestamp();
        const mapFile = `map_data_${ts}.bin`;
        const dirtyFile = `dirty_chunks_${ts}.json`;

        downloadBlob(mapFile, buildMapBlob());

        const dirtySet = new Set<string>();
        for (const k of navDirtyChunks.values()) dirtySet.add(k);
        for (const k of terrain.dirtyChunks.values()) dirtySet.add(k);
        const dirtyChunks = Array.from(dirtySet.values());
        downloadBlob(dirtyFile, new Blob([JSON.stringify(dirtyChunks, null, 2)], { type: 'application/json' }));

        const cmd = [
            'dotnet run --project .\\src\\Tools\\Ludots.Tool\\Ludots.Tool.csproj -- nav bake-recast-react',
            `  --mapId ${mapId}`,
            `  --in ${mapFile}`,
            `  --dirty ${dirtyFile}`,
            '  --artifact true',
            '  --parallel true'
        ].join('\r\n');

        try {
            await navigator.clipboard.writeText(cmd);
            alert('已导出 map_data.bin + dirty_chunks.json，并复制了 bake 命令到剪贴板。');
        } catch {
            alert(`已导出 map_data.bin + dirty_chunks.json。\n\n在仓库根目录运行：\n${cmd}`);
        }
    };

    const base64ToArrayBuffer = (b64: string) => {
        const bin = atob(b64);
        const bytes = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        return bytes.buffer;
    };

    const handleBakeNavTilesLocal = async () => {
        const endpoint = `${bridgeBaseUrl}/api/nav/bake-recast-react`;
        const form = new FormData();
        form.append('map', buildMapBlob(), 'map_data.bin');
        form.append('mapId', mapId);

        const dirtySet = new Set<string>();
        for (const k of navDirtyChunks.values()) dirtySet.add(k);
        for (const k of terrain.dirtyChunks.values()) dirtySet.add(k);
        const dirtyCount = dirtySet.size;
        if (navScope === 'dirty') {
            if (dirtyCount === 0) {
                alert(`没有 dirty chunks（nav=${navDirtyChunks.size} render=${terrain.dirtyChunks.size}），不会触发全量 bake。请先修改地形，或把策略切到 Full。`);
                return;
            }
            const dirtyChunks = Array.from(dirtySet.values());
            form.append('dirty', JSON.stringify(dirtyChunks));
            form.append('dirtyOnly', 'true');
        }

        form.append('includeNeighbors', navIncludeNeighbors ? 'true' : 'false');
        form.append('parallel', navParallel ? 'true' : 'false');
        form.append('artifact', 'false');
        form.append('tileVersion', String(navTileVersion));

        let timeoutId: number | null = null;
        try {
            navAbortRef.current?.abort();
            navAbortRef.current = new AbortController();
            const scopeLabel = navScope === 'dirty' ? `Dirty(${dirtyCount})${navIncludeNeighbors ? '+N' : ''}` : 'Full';
            setLoading(true, `Baking NavTiles: ${scopeLabel}...`, 30);
            timeoutId = window.setTimeout(() => navAbortRef.current?.abort(), 120000);
            const res = await fetch(endpoint, { method: 'POST', body: form, signal: navAbortRef.current.signal });
            if (!res.ok) {
                const text = await res.text();
                throw new Error(`Bridge error ${res.status}: ${text}`);
            }
            const json = await res.json();
            const tilesRaw: Array<{ base64: string }> = json.tiles ?? [];
            if (tilesRaw.length === 0) {
                const targetsCount = Number(json.targetsCount ?? 0);
                if (targetsCount === 0) {
                    alert('没有目标 chunk 需要 bake（dirtyOnly=true 且 dirty 为空）。');
                    return;
                }
                throw new Error('No tiles returned.');
            }

            const tiles = [];
            for (let i = 0; i < tilesRaw.length; i++) {
                const buf = base64ToArrayBuffer(tilesRaw[i].base64);
                tiles.push(readNavTile(buf));
            }

            setBakedNavTiles(tiles);
            if (!showNavMesh) toggleNavMesh();
            terrain.clearDirty();
            clearNavDirty();
            setLoading(false);
        } catch (err: any) {
            setLoading(false);
            if (err?.name === 'AbortError') {
                alert('已取消 NavTiles bake。');
                return;
            }
            alert(`本地 Bridge 未启动或请求失败。\n\n请先运行：\n  dotnet run --project .\\src\\Tools\\Ludots.Editor.Bridge\\Ludots.Editor.Bridge.csproj\n\n错误：${err?.message ?? err}`);
        } finally {
            if (timeoutId !== null) window.clearTimeout(timeoutId);
            navAbortRef.current = null;
        }
    };

    const handleLoadNavTiles = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const files = Array.from(e.target.files ?? []);
        e.target.value = '';
        if (files.length === 0) return;

        const tiles = [];
        for (let i = 0; i < files.length; i++) {
            const f = files[i];
            if (!f.name.toLowerCase().endsWith('.ntil')) continue;
            const buf = await f.arrayBuffer();
            tiles.push(readNavTile(buf));
        }
        if (tiles.length === 0) return;
        setBakedNavTiles(tiles);
    };

    const handleUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = (ev) => {
            const buffer = ev.target?.result as ArrayBuffer;
            if (!buffer) return;

            const view = new DataView(buffer);
            const w = view.getInt32(0, true);
            const h = view.getInt32(4, true);
            const stride = view.getUint8(8);
            
            if (stride !== 2) {
                alert(`Invalid map stride. Expected 2, got ${stride}. Please recreate map.`);
                return;
            }

            const data = new Uint8Array(buffer.slice(9));
            loadMap(data, w, h);
        };
        reader.readAsArrayBuffer(file);
    };

    return (
        <div className="absolute top-4 left-4 bg-gray-900/95 text-white p-4 rounded-xl shadow-2xl backdrop-blur-md flex flex-col gap-5 w-72 border border-gray-700/50">
            <h1 className="text-xl font-bold bg-gradient-to-r from-blue-400 to-purple-400 bg-clip-text text-transparent px-1">
                Ludots Editor
            </h1>

            {/* Loading Overlay */}
            {loadingState.isLoading && (
                <div className="absolute inset-0 bg-black/80 z-50 rounded-xl flex flex-col items-center justify-center p-4">
                    <div className="w-10 h-10 border-4 border-blue-500 border-t-transparent rounded-full animate-spin mb-3"></div>
                    <div className="text-sm font-medium text-white mb-1">{loadingState.message}</div>
                    <div className="w-full bg-gray-700 h-2 rounded-full overflow-hidden">
                        <div 
                            className="bg-blue-500 h-full transition-all duration-100" 
                            style={{ width: `${loadingState.progress}%` }}
                        />
                    </div>
                    {loadingState.message.startsWith('Baking NavTiles') && (
                        <button
                            onClick={() => {
                                navAbortRef.current?.abort();
                                navAbortRef.current = null;
                                setLoading(false);
                            }}
                            className="mt-4 px-3 py-1 rounded bg-red-700 text-white text-xs pointer-events-auto"
                        >
                            Cancel
                        </button>
                    )}
                </div>
            )}

            <div className="flex flex-col gap-2 border-b border-gray-700/50 pb-4">
                <div className="flex gap-2">
                    <select
                        value={selectedModId ?? ''}
                        onChange={(e) => selectMod(e.target.value).catch((err: any) => alert(err?.message ?? err))}
                        className="flex-1 px-2 py-1 rounded bg-gray-800 border border-gray-700 text-gray-200 text-xs"
                        title="Mod"
                    >
                        {mods.map((m) => (
                            <option key={m.id} value={m.id}>{m.id}</option>
                        ))}
                    </select>
                    <select
                        value={selectedMapId ?? ''}
                        onChange={(e) => selectMap(e.target.value)}
                        className="flex-1 px-2 py-1 rounded bg-gray-800 border border-gray-700 text-gray-200 text-xs"
                        title="Map"
                    >
                        {maps.map((id) => (
                            <option key={id} value={id}>{id}</option>
                        ))}
                    </select>
                </div>
                <div className="flex gap-2">
                    <button
                        onClick={() => loadSelectedMap().catch((err: any) => alert(err?.message ?? err))}
                        className="flex-1 btn btn-sm bg-gray-800 hover:bg-gray-700 border border-gray-600 p-2 rounded-lg flex justify-center items-center gap-2 transition-all"
                        title="Load from repo/mods via Bridge"
                        disabled={!selectedModId || !selectedMapId}
                    >
                        <Upload size={14} className="text-blue-400" /> <span className="text-sm font-medium">Load Repo</span>
                    </button>
                    <button
                        onClick={() => saveSelectedMap().catch((err: any) => alert(err?.message ?? err))}
                        className="flex-1 btn btn-sm bg-gray-800 hover:bg-gray-700 border border-gray-600 p-2 rounded-lg flex justify-center items-center gap-2 transition-all"
                        title="Save MapConfig + Terrain to selected mod via Bridge"
                        disabled={!selectedModId || !selectedMapId}
                    >
                        <Download size={14} className="text-green-400" /> <span className="text-sm font-medium">Save Repo</span>
                    </button>
                </div>
            </div>

            {/* View Options */}
            <div className="flex gap-2 justify-end border-b border-gray-700/50 pb-4">
                <button 
                    onClick={toggleGrid} 
                    className={`p-2 rounded ${showGrid ? 'bg-purple-600 text-white' : 'bg-gray-800 text-gray-400 hover:bg-gray-750'}`}
                    title="Toggle Grid"
                >
                    <Grid size={16} />
                </button>
                <button 
                    onClick={toggleChunkBorders} 
                    className={`p-2 rounded ${showChunkBorders ? 'bg-purple-600 text-white' : 'bg-gray-800 text-gray-400 hover:bg-gray-750'}`}
                    title="Toggle Chunk Borders"
                >
                    <BoxSelect size={16} />
                </button>
                <button 
                    onClick={toggleNavMesh} 
                    className={`p-2 rounded ${showNavMesh ? 'bg-green-600 text-white' : 'bg-gray-800 text-gray-400 hover:bg-gray-750'}`}
                    title="Toggle NavMesh Visualization"
                >
                    <Footprints size={16} />
                </button>
                <input
                    value={mapId}
                    onChange={(e) => setMapId(e.target.value)}
                    className="px-2 py-1 rounded bg-gray-800 border border-gray-700 text-gray-200 text-xs w-24"
                    title="mapId"
                />
                <select
                    value={navScope === 'full' ? 'full' : (navIncludeNeighbors ? 'dirtyN' : 'dirty')}
                    onChange={(e) => {
                        const v = e.target.value;
                        if (v === 'full') {
                            setNavScope('full');
                            setNavIncludeNeighbors(true);
                        } else if (v === 'dirtyN') {
                            setNavScope('dirty');
                            setNavIncludeNeighbors(true);
                        } else {
                            setNavScope('dirty');
                            setNavIncludeNeighbors(false);
                        }
                    }}
                    className="px-2 py-1 rounded bg-gray-800 border border-gray-700 text-gray-200 text-xs"
                    title={`Nav bake scope (dirty=${navDirtyChunks.size})`}
                >
                    <option value="dirtyN">{`Dirty+N (${navDirtyChunks.size})`}</option>
                    <option value="dirty">{`Dirty (${navDirtyChunks.size})`}</option>
                    <option value="full">Full</option>
                </select>
                <button
                    onClick={() => setNavParallel(!navParallel)}
                    className={`px-2 py-1 rounded border text-xs ${navParallel ? 'bg-gray-800 border-gray-700 text-gray-200' : 'bg-gray-900 border-gray-800 text-gray-400'}`}
                    title={`Parallel: ${navParallel ? 'on' : 'off'}`}
                >
                    P
                </button>
                <select
                    value={String(navTileVersion)}
                    onChange={(e) => setNavTileVersion(parseInt(e.target.value) || 1)}
                    className="px-2 py-1 rounded bg-gray-800 border border-gray-700 text-gray-200 text-xs"
                    title="NavTile tileVersion"
                >
                    <option value="1">V1</option>
                    <option value="2">V2</option>
                </select>
                <button 
                    onClick={handleBakeNavTilesLocal} 
                    className="p-2 rounded bg-orange-700 text-white hover:bg-orange-600"
                    title="Bake NavTiles via local bridge and load into editor"
                >
                    <span className="text-xs font-bold">BAKE</span>
                </button>
            </div>

            {/* File Ops */}
            <div className="flex gap-2 border-b border-gray-700/50 pb-4">
                <button 
                    onClick={() => setShowNewMap(true)}
                    className="flex-1 btn btn-sm bg-gray-800 hover:bg-gray-700 border border-gray-600 p-2 rounded-lg flex justify-center items-center gap-2 transition-all"
                >
                    <span className="text-yellow-400 font-bold text-lg leading-none">+</span> <span className="text-sm font-medium">New</span>
                </button>
                <label className="flex-1 btn btn-sm bg-gray-800 hover:bg-gray-700 border border-gray-600 p-2 rounded-lg cursor-pointer flex justify-center items-center gap-2 transition-all">
                    <Upload size={14} className="text-blue-400" /> <span className="text-sm font-medium">Load</span>
                    <input type="file" className="hidden" onChange={handleUpload} />
                </label>
                <button 
                    onClick={handleDownload}
                    className="flex-1 btn btn-sm bg-gray-800 hover:bg-gray-700 border border-gray-600 p-2 rounded-lg flex justify-center items-center gap-2 transition-all"
                >
                    <Download size={14} className="text-green-400" /> <span className="text-sm font-medium">Save</span>
                </button>
            </div>

            <div className="flex gap-2 border-b border-gray-700/50 pb-4">
                <button
                    onClick={handleBakeNavTiles}
                    className="flex-1 btn btn-sm bg-gray-800 hover:bg-gray-700 border border-gray-600 p-2 rounded-lg flex justify-center items-center gap-2 transition-all"
                    title="Export map_data.bin + dirty list, then bake via CLI"
                >
                    <Footprints size={14} className="text-orange-400" /> <span className="text-sm font-medium">NavTiles</span>
                </button>
            </div>

            <div className="flex gap-2 border-b border-gray-700/50 pb-4">
                <label className="flex-1 btn btn-sm bg-gray-800 hover:bg-gray-700 border border-gray-600 p-2 rounded-lg cursor-pointer flex justify-center items-center gap-2 transition-all">
                    <Upload size={14} className="text-orange-400" /> <span className="text-sm font-medium">Load .ntil</span>
                    <input type="file" className="hidden" multiple accept=".ntil" onChange={handleLoadNavTiles} />
                </label>
                <button
                    onClick={clearBakedNavTiles}
                    className="flex-1 btn btn-sm bg-gray-800 hover:bg-gray-700 border border-gray-600 p-2 rounded-lg flex justify-center items-center gap-2 transition-all"
                    title="Clear baked NavTiles"
                    disabled={bakedNavTiles.size === 0}
                >
                    <span className="text-sm font-medium">Clear</span>
                </button>
            </div>

            {/* New Map Modal (Simple overlay) */}
            {showNewMap && (
                <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center backdrop-blur-sm" onClick={() => setShowNewMap(false)}>
                    <div className="bg-gray-900 p-6 rounded-xl border border-gray-600 shadow-2xl w-80" onClick={e => e.stopPropagation()}>
                        <h3 className="text-lg font-bold mb-4 text-white">Create New Map</h3>
                        
                        <div className="space-y-4">
                            <div>
                                <label className="block text-sm text-gray-400 mb-1">Width (Chunks)</label>
                                <input 
                                    type="number" min="1" max="32" 
                                    value={newWidth} 
                                    onChange={e => setNewWidth(parseInt(e.target.value) || 1)}
                                    className="w-full bg-gray-800 border border-gray-700 rounded p-2 text-white"
                                />
                            </div>
                            <div>
                                <label className="block text-sm text-gray-400 mb-1">Height (Chunks)</label>
                                <input 
                                    type="number" min="1" max="32" 
                                    value={newHeight} 
                                    onChange={e => setNewHeight(parseInt(e.target.value) || 1)}
                                    className="w-full bg-gray-800 border border-gray-700 rounded p-2 text-white"
                                />
                            </div>
                            
                            <div className="flex gap-2 pt-2">
                                <button 
                                    onClick={() => setShowNewMap(false)}
                                    className="flex-1 py-2 bg-gray-700 hover:bg-gray-600 rounded text-gray-300 font-medium"
                                >
                                    Cancel
                                </button>
                                <button 
                                    onClick={handleNewMap}
                                    className="flex-1 py-2 bg-blue-600 hover:bg-blue-500 rounded text-white font-medium"
                                >
                                    Create
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* Categories */}
            <div className="space-y-2">
                <label className="text-xs font-semibold text-gray-500 uppercase tracking-wider px-1">Tools</label>
                <div className="grid grid-cols-3 gap-2">
                    {categories.map(c => (
                        <button
                            key={c.id}
                            onClick={() => setCategory(c.id)}
                            className={`p-2 rounded-lg flex flex-col items-center justify-center gap-1 transition-all border ${
                                activeCategory === c.id 
                                    ? 'bg-blue-600/20 border-blue-500/50 text-blue-400' 
                                    : 'bg-gray-800 border-gray-700 text-gray-400 hover:bg-gray-750 hover:border-gray-600'
                            }`}
                            title={c.id}
                        >
                            {c.icon}
                            <span className="text-[10px] font-medium">{c.label}</span>
                        </button>
                    ))}
                </div>
            </div>

            {/* Modes */}
            <div className="space-y-2">
                <label className="text-xs font-semibold text-gray-500 uppercase tracking-wider px-1">Mode</label>
                <div className="grid grid-cols-3 gap-2">
                    {modes.map(m => (
                        <button
                            key={m.id}
                            onClick={() => setMode(m.id)}
                            className={`p-2 rounded-lg flex flex-col items-center justify-center gap-1 transition-all border ${
                                activeMode === m.id 
                                    ? 'bg-purple-600/20 border-purple-500/50 text-purple-400' 
                                    : 'bg-gray-800 border-gray-700 text-gray-400 hover:bg-gray-750 hover:border-gray-600'
                            }`}
                            title={m.id}
                        >
                            {m.icon}
                            <span className="text-[10px] font-medium">{m.label}</span>
                        </button>
                    ))}
                </div>
            </div>


            {/* Brush Settings */}
            <div className="space-y-3">
                <div className="flex justify-between text-sm text-gray-400">
                    <span>Size: {brushSize}</span>
                </div>
                <input 
                    type="range" min="1" max="10" 
                    value={brushSize} 
                    onChange={(e) => setBrushSize(parseInt(e.target.value))}
                    className="w-full accent-blue-500"
                />

                <div className="flex justify-between text-sm text-gray-400">
                    <span>
                        {activeCategory === 'Biome' ? 'Biome Type' : 
                         activeCategory === 'Vegetation' ? 'Veg Type' : 
                         activeCategory === 'Layers' ? 'Layer Type' :
                         activeCategory === 'Territory' ? 'Faction ID' :
                         activeCategory === 'Entities' ? 'Template' :
                         'Value'}
                    </span>
                    <span className="text-xs text-gray-500">{brushValue}</span>
                </div>

                {activeCategory === 'Biome' ? (
                     <div className="grid grid-cols-2 gap-2">
                         {[
                             { id: 0, label: 'Dirt', color: 'bg-[#8B4513]' },
                             { id: 1, label: 'Sand', color: 'bg-[#F4A460]' },
                             { id: 2, label: 'Rock', color: 'bg-[#808080]' },
                             { id: 3, label: 'Grass', color: 'bg-[#3d6c2e]' },
                             { id: 4, label: 'Wasteland', color: 'bg-[#696969]' },
                             { id: 5, label: 'Swamp', color: 'bg-[#556B2F]' },
                         ].map(b => (
                             <button
                                 key={b.id}
                                 onClick={() => {
                                     setBrushValue(b.id);
                                     setMode('Set'); // Force Set Mode
                                 }}
                                 className={`p-2 rounded text-xs font-bold border transition-all ${
                                     brushValue === b.id 
                                     ? 'border-white scale-105 shadow-md' 
                                     : 'border-transparent opacity-70 hover:opacity-100'
                                 } ${b.color}`}
                             >
                                 {b.label}
                             </button>
                         ))}
                     </div>
                ) : activeCategory === 'Vegetation' ? (
                    <div className="grid grid-cols-2 gap-2">
                        {[
                             { id: 0, label: 'None', icon: '❌' },
                             { id: 1, label: 'Small Tree', icon: '🌲' },
                             { id: 2, label: 'Big Tree', icon: '🌳' },
                             { id: 3, label: 'Dense', icon: '🌲🌲' },
                             { id: 4, label: 'Crop', icon: '🌾' }
                        ].map(v => (
                             <button
                                 key={v.id}
                                 onClick={() => {
                                     setBrushValue(v.id);
                                     setMode('Set'); // Force Set Mode
                                 }}
                                 className={`p-2 rounded border transition-all flex flex-col items-center gap-1 ${
                                     brushValue === v.id 
                                     ? 'bg-green-600/30 border-green-500 text-green-300' 
                                     : 'bg-gray-800 border-gray-700 text-gray-400 hover:bg-gray-750'
                                 }`}
                             >
                                 <span className="text-lg">{v.icon}</span>
                                 <span className="text-[10px]">{v.label}</span>
                             </button>
                        ))}
                    </div>
                ) : activeCategory === 'Layers' ? (
                    <div className="grid grid-cols-1 gap-2">
                         {[
                             { id: 'Snow', label: 'Snow', color: 'bg-white text-black' },
                             { id: 'Mud', label: 'Mud', color: 'bg-[#5c4033] text-white' },
                             { id: 'Ice', label: 'Ice', color: 'bg-cyan-200 text-black' }
                         ].map(l => (
                             <button
                                 key={l.id}
                                 onClick={() => {
                                     setActiveLayer(l.id as any);
                                     setBrushValue(1); // Auto-set to 'On' for layer logic
                                 }}
                                 className={`p-2 rounded text-xs font-bold border transition-all flex justify-between items-center ${
                                     activeLayer === l.id 
                                     ? 'border-blue-400 scale-105' 
                                     : 'border-transparent opacity-70 hover:opacity-100'
                                 } ${l.color}`}
                             >
                                 <span>{l.label}</span>
                                 {activeLayer === l.id && <span className="text-xs bg-black/20 px-1 rounded">Active</span>}
                             </button>
                         ))}
                         <div className="text-[10px] text-gray-400 mt-1">
                            Mode: Raise = Add, Lower = Remove
                         </div>
                    </div>
                ) : activeCategory === 'Territory' ? (
                    <div className="flex flex-col gap-2">
                        <input 
                            type="range" min="0" max="255" 
                            value={brushValue} 
                            onChange={(e) => setBrushValue(parseInt(e.target.value))}
                            className="w-full accent-purple-500"
                        />
                        <div className="flex justify-between text-xs text-gray-400">
                            <button onClick={() => setBrushValue(0)} className="hover:text-white">Neutral (0)</button>
                            <button onClick={() => setBrushValue(1)} className="hover:text-white">F1</button>
                            <button onClick={() => setBrushValue(128)} className="hover:text-white">F128</button>
                            <button onClick={() => setBrushValue(255)} className="hover:text-white">F255</button>
                        </div>
                    </div>
                ) : activeCategory === 'Entities' ? (
                    <div className="flex flex-col gap-2">
                        <select
                            value={selectedTemplateId ?? ''}
                            onChange={(e) => selectTemplate(e.target.value.length > 0 ? e.target.value : null)}
                            className="w-full px-2 py-1 rounded bg-gray-800 border border-gray-700 text-gray-200 text-xs"
                            title="Template"
                        >
                            {templates.map((t: any, i: number) => {
                                const id = String(t?.Id ?? t?.id ?? `template_${i}`);
                                return <option key={id} value={id}>{id}</option>;
                            })}
                        </select>

                        <div className="text-[10px] text-gray-400">
                            Set: Place / Replace<br/>
                            Lower: Erase<br/>
                            Raise: Select
                        </div>

                        {selectedEntityIndex != null && selectedEntityIndex >= 0 && selectedEntityIndex < spawnEntities.length ? (
                            <div className="bg-gray-800/60 border border-gray-700 rounded p-2 flex flex-col gap-2">
                                <div className="text-xs text-gray-300">
                                    Selected: {spawnEntities[selectedEntityIndex].template} @ ({spawnEntities[selectedEntityIndex].position.x},{spawnEntities[selectedEntityIndex].position.y})
                                </div>

                                <div className="text-[10px] text-gray-400">Overrides (componentName: JSON)</div>
                                {Object.keys(spawnEntities[selectedEntityIndex].overrides ?? {}).length === 0 ? (
                                    <div className="text-[10px] text-gray-500">No overrides.</div>
                                ) : (
                                    Object.entries(spawnEntities[selectedEntityIndex].overrides ?? {}).map(([k, v]) => (
                                        <div key={k} className="flex flex-col gap-1">
                                            <div className="flex justify-between items-center">
                                                <div className="text-[11px] text-gray-200">{k}</div>
                                                <button
                                                    onClick={() => deleteSelectedEntityOverride(k)}
                                                    className="text-[10px] text-red-300 hover:text-red-200"
                                                >
                                                    Delete
                                                </button>
                                            </div>
                                            <textarea
                                                className="w-full h-20 bg-gray-900 border border-gray-700 rounded p-1 text-[10px] font-mono text-gray-200"
                                                defaultValue={JSON.stringify(v, null, 2)}
                                                onBlur={(e) => updateSelectedEntityOverridesJson(k, e.target.value)}
                                            />
                                        </div>
                                    ))
                                )}
                            </div>
                        ) : (
                            <div className="text-[10px] text-gray-500">No entity selected.</div>
                        )}
                    </div>
                ) : (
                    <input 
                        type="range" min="0" max="15" 
                        value={brushValue} 
                        onChange={(e) => setBrushValue(parseInt(e.target.value))}
                        className="w-full accent-purple-500"
                    />
                )}
            </div>
            
            <div className="text-xs text-gray-500 mt-2">
                Middle Click: Pan<br/>
                Right Click: Rotate<br/>
                Left Click: {activeCategory === 'Entities' ? 'Place/Erase/Select' : 'Paint'}
            </div>
        </div>
    );
};
