import React from 'react';
import { useEditorStore, ToolCategory, ToolMode } from './EditorStore';
import { Mountain, Waves, Trees, Palette, Grid3X3, Layers, Plus, Minus, Activity, Square, Circle, ChevronDown, ChevronRight } from 'lucide-react';

const catIcons: Record<string, React.ReactNode> = {
    Height: <Mountain size={16} />, Water: <Waves size={16} />, Area: <Palette size={16} />,
    Blocked: <Grid3X3 size={16} />, Biome: <Trees size={16} />, Vegetation: <Trees size={16} />,
    Ramp: <Activity size={16} />, Layers: <Layers size={16} />, Territory: <Activity size={16} />,
    Entities: <Plus size={16} />, Obstacle: <Square size={16} />,
};
const Section: React.FC<{ title: string; defaultOpen?: boolean; children: React.ReactNode }> =
    ({ title, defaultOpen = true, children }) => {
        const [open, setOpen] = React.useState(defaultOpen);
        return (
            <div className="border-b border-white/5 last:border-b-0">
                <button onClick={() => setOpen(!open)} className="w-full flex items-center gap-1.5 px-3 py-2 text-[11px] font-medium text-slate-500 hover:text-slate-300">
                    {open ? <ChevronDown size={10} /> : <ChevronRight size={10} />} {title}
                </button>
                {open && <div className="px-3 pb-3 space-y-2">{children}</div>}
            </div>
        );
    };

export const Toolbar: React.FC = () => {
    const {
        activeCategory, setCategory, activeMode, setMode, brushSize, setBrushSize, brushValue, setBrushValue,
        activeLayer, setActiveLayer, templates, selectedTemplateId, selectTemplate,
        obstacleTemplateId, setObstacleTemplate, obstacleShape, setObstacleShape,
        obstacleRadiusCm, setObstacleRadiusCm, obstacleHalfWidthCm, obstacleHalfHeightCm, setObstacleHalfSizeCm,
        spawnEntities, selectedEntityIndex, updateSelectedEntityOverridesJson, deleteSelectedEntityOverride,
    } = useEditorStore();

    const cat: { id: ToolCategory; label: string }[] = [
        { id: 'Height', label: 'H' }, { id: 'Water', label: 'W' }, { id: 'Area', label: 'A' },
        { id: 'Blocked', label: 'B' }, { id: 'Biome', label: 'Bi' }, { id: 'Vegetation', label: 'V' },
        { id: 'Ramp', label: 'R' }, { id: 'Layers', label: 'L' }, { id: 'Territory', label: 'T' },
        { id: 'Entities', label: 'E' }, { id: 'Obstacle', label: 'Ob' },
    ];
    const md: { id: ToolMode; label: string; icon: React.ReactNode }[] = [
        { id: 'Set', label: 'Set', icon: <div className="w-2.5 h-2.5 bg-current rounded-full" /> },
        { id: 'Raise', label: '+', icon: <Plus size={13} /> },
        { id: 'Lower', label: '−', icon: <Minus size={13} /> },
    ];

    return (
        <div className="absolute top-14 left-3 w-[200px] bg-slate-900/90 backdrop-blur-xl border border-white/5 rounded-2xl shadow-2xl z-30 overflow-hidden">
            <Section title="TOOLS">
                <div className="grid grid-cols-4 gap-1">
                    {cat.map((c) => (
                        <button key={c.id} onClick={() => setCategory(c.id)}
                            className={`flex flex-col items-center gap-0.5 p-2 rounded-xl transition-all border ${activeCategory === c.id
                                ? 'bg-indigo-500/15 border-indigo-500/30 text-indigo-300'
                                : 'bg-transparent border-transparent text-slate-500 hover:bg-slate-800/50 hover:text-slate-300'}`}>
                            <span className="opacity-80">{catIcons[c.id] ?? <Activity size={14} />}</span>
                            <span className="text-[9px] font-medium leading-none">{c.label}</span>
                        </button>
                    ))}
                </div>
            </Section>
            <Section title="MODE">
                <div className="grid grid-cols-3 gap-1">
                    {md.map((m) => (
                        <button key={m.id} onClick={() => setMode(m.id)}
                            className={`flex items-center justify-center gap-1 p-2 rounded-xl transition-all border ${activeMode === m.id
                                ? 'bg-purple-500/15 border-purple-500/30 text-purple-300'
                                : 'bg-transparent border-transparent text-slate-500 hover:bg-slate-800/50 hover:text-slate-300'}`}>
                            {m.icon}<span className="text-[10px] font-medium">{m.label}</span>
                        </button>
                    ))}
                </div>
            </Section>
            <Section title="BRUSH">
                <div className="flex items-center justify-between">
                    <span className="text-[10px] text-slate-500">Size</span>
                    <span className="text-[10px] font-mono text-slate-400 bg-slate-800/60 px-1.5 py-0.5 rounded">{brushSize}</span>
                </div>
                <input type="range" min="1" max="15" value={brushSize} onChange={(e) => setBrushSize(parseInt(e.target.value))}
                    className="w-full accent-indigo-500 h-1.5 rounded-full" />

                {activeCategory === 'Area' && (
                    <div className="grid grid-cols-3 gap-1">
                        {[{ id: 0, label: 'Def', color: 'bg-slate-500' },{ id: 1, label: 'Road', color: 'bg-slate-400' },{ id: 2, label: 'For', color: 'bg-emerald-700' },{ id: 3, label: 'Swp', color: 'bg-lime-700' },{ id: 4, label: 'Shr', color: 'bg-blue-600' },{ id: 5, label: 'Haz', color: 'bg-orange-600' }].map((a) => (
                            <button key={a.id} onClick={() => { setBrushValue(a.id); setMode('Set'); }}
                                className={`py-1 rounded-md text-[9px] font-bold transition-all border ${brushValue === a.id ? 'border-white scale-105' : 'border-transparent opacity-60 hover:opacity-90'} ${a.color} text-white`}>{a.label}</button>
                        ))}
                    </div>
                )}

                {activeCategory === 'Blocked' && (
                    <div className="grid grid-cols-2 gap-1">
                        <button onClick={() => { setBrushValue(1); setMode('Set'); }} className={`py-1.5 rounded-md text-[10px] font-bold ${brushValue > 0 ? 'bg-red-600/40 border border-red-400 text-red-100' : 'bg-slate-800/50 text-slate-500'}`}>Block</button>
                        <button onClick={() => { setBrushValue(0); setMode('Set'); }} className={`py-1.5 rounded-md text-[10px] font-bold ${brushValue === 0 ? 'bg-emerald-600/40 border border-emerald-400 text-emerald-100' : 'bg-slate-800/50 text-slate-500'}`}>Clear</button>
                    </div>
                )}

                {activeCategory === 'Biome' && (
                    <div className="grid grid-cols-3 gap-1">
                        {[{ id: 0, label: 'Dirt', color: 'bg-amber-900' },{ id: 1, label: 'Sand', color: 'bg-amber-600' },{ id: 2, label: 'Rock', color: 'bg-slate-600' },{ id: 3, label: 'Grass', color: 'bg-emerald-700' },{ id: 4, label: 'Waste', color: 'bg-stone-700' },{ id: 5, label: 'Swamp', color: 'bg-lime-800' }].map((b) => (
                            <button key={b.id} onClick={() => { setBrushValue(b.id); setMode('Set'); }}
                                className={`py-1 rounded-md text-[9px] font-bold transition-all border ${brushValue === b.id ? 'border-white scale-105' : 'border-transparent opacity-60 hover:opacity-90'} ${b.color} text-white`}>{b.label}</button>
                        ))}
                    </div>
                )}

                {activeCategory === 'Vegetation' && (
                    <div className="grid grid-cols-3 gap-1">
                        {[{ id: 0, label: 'None', icon: '✕' },{ id: 1, label: 'Sm', icon: '🌲' },{ id: 2, label: 'Bg', icon: '🌳' },{ id: 3, label: 'Dn', icon: '🌿' },{ id: 4, label: 'Cr', icon: '🌾' }].map((v) => (
                            <button key={v.id} onClick={() => { setBrushValue(v.id); setMode('Set'); }}
                                className={`py-1 rounded-md text-[10px] transition-all border ${brushValue === v.id ? 'bg-green-500/20 border-green-500/40 text-green-300' : 'bg-transparent text-slate-500 hover:bg-slate-800/50'}`}>{v.icon}{v.label}</button>
                        ))}
                    </div>
                )}

                {activeCategory === 'Layers' && (
                    <div className="grid grid-cols-3 gap-1">
                        {[{ id: 'Snow', label: 'Snow', color: 'bg-white/80 text-slate-800' },{ id: 'Mud', label: 'Mud', color: 'bg-amber-800/80 text-white' },{ id: 'Ice', label: 'Ice', color: 'bg-cyan-200/80 text-slate-800' }].map((l) => (
                            <button key={l.id} onClick={() => { setActiveLayer(l.id as any); setBrushValue(1); }}
                                className={`py-1 rounded-md text-[10px] font-bold transition-all ${activeLayer === l.id ? 'ring-2 ring-white' : 'opacity-60 hover:opacity-90'} ${l.color}`}>{l.label}</button>
                        ))}
                    </div>
                )}

                {activeCategory === 'Territory' && (
                    <div>
                        <input type="range" min="0" max="255" value={brushValue} onChange={(e) => setBrushValue(parseInt(e.target.value))} className="w-full accent-purple-500" />
                        <div className="flex justify-between text-[9px] text-slate-600 mt-0.5">
                            <button onClick={() => setBrushValue(0)} className="hover:text-slate-400">0</button>
                            <button onClick={() => setBrushValue(64)} className="hover:text-slate-400">64</button>
                            <button onClick={() => setBrushValue(128)} className="hover:text-slate-400">128</button>
                            <button onClick={() => setBrushValue(192)} className="hover:text-slate-400">192</button>
                            <button onClick={() => setBrushValue(255)} className="hover:text-slate-400">255</button>
                        </div>
                    </div>
                )}

                {activeCategory === 'Obstacle' && (
                    <div className="space-y-1.5">
                        <select value={obstacleTemplateId ?? ''} onChange={(e) => setObstacleTemplate(e.target.value || null)}
                            className="w-full bg-slate-800/80 border border-slate-700/50 rounded-md px-2 py-1 text-[10px] text-slate-300">
                            {templates.map((t: any, i: number) => <option key={i} value={String(t?.Id ?? t?.id ?? `t${i}`)}>{String(t?.Id ?? t?.id ?? `t${i}`)}</option>)}
                        </select>
                        <div className="grid grid-cols-2 gap-1">
                            <button onClick={() => setObstacleShape('Circle')} className={`py-1 rounded-md text-[10px] flex items-center justify-center gap-1 ${obstacleShape === 'Circle' ? 'bg-orange-500/20 text-orange-300' : 'bg-slate-800/50 text-slate-500'}`}><Circle size={12} />Circle</button>
                            <button onClick={() => setObstacleShape('Box')} className={`py-1 rounded-md text-[10px] flex items-center justify-center gap-1 ${obstacleShape === 'Box' ? 'bg-orange-500/20 text-orange-300' : 'bg-slate-800/50 text-slate-500'}`}><Square size={12} />Box</button>
                        </div>
                        {obstacleShape === 'Circle' ? (
                            <label className="text-[9px] text-slate-500 block">Radius cm<input type="number" min="1" value={obstacleRadiusCm} onChange={(e) => setObstacleRadiusCm(Number(e.target.value))} className="mt-0.5 w-full bg-slate-800/80 border border-slate-700/50 rounded-md px-2 py-1 text-[10px] text-slate-300" /></label>
                        ) : (
                            <div className="grid grid-cols-2 gap-1">
                                <label className="text-[9px] text-slate-500 block">Half W<input type="number" min="1" value={obstacleHalfWidthCm} onChange={(e) => setObstacleHalfSizeCm(Number(e.target.value), obstacleHalfHeightCm)} className="mt-0.5 w-full bg-slate-800/80 border border-slate-700/50 rounded-md px-2 py-1 text-[10px] text-slate-300" /></label>
                                <label className="text-[9px] text-slate-500 block">Half H<input type="number" min="1" value={obstacleHalfHeightCm} onChange={(e) => setObstacleHalfSizeCm(obstacleHalfWidthCm, Number(e.target.value))} className="mt-0.5 w-full bg-slate-800/80 border border-slate-700/50 rounded-md px-2 py-1 text-[10px] text-slate-300" /></label>
                            </div>
                        )}
                    </div>
                )}

                {activeCategory === 'Entities' && (
                    <div className="space-y-1.5">
                        <select value={selectedTemplateId ?? ''} onChange={(e) => selectTemplate(e.target.value || null)}
                            className="w-full bg-slate-800/80 border border-slate-700/50 rounded-md px-2 py-1 text-[10px] text-slate-300">
                            {templates.map((t: any, i: number) => <option key={i} value={String(t?.Id ?? t?.id ?? `t${i}`)}>{String(t?.Id ?? t?.id ?? `t${i}`)}</option>)}
                        </select>
                        {selectedEntityIndex != null && selectedEntityIndex >= 0 && selectedEntityIndex < spawnEntities.length && (
                            <div className="bg-slate-800/60 border border-slate-700/50 rounded-lg p-2 space-y-1.5 max-h-40 overflow-auto">
                                <div className="text-[10px] text-slate-300 truncate">{spawnEntities[selectedEntityIndex].template}</div>
                                {Object.entries(spawnEntities[selectedEntityIndex].overrides ?? {}).slice(0, 3).map(([k, v]) => (
                                    <div key={k}>
                                        <div className="flex justify-between items-center"><span className="text-[9px] text-slate-500">{k}</span>
                                            <button onClick={() => deleteSelectedEntityOverride(k)} className="text-[9px] text-red-400">×</button></div>
                                        <textarea className="w-full h-12 bg-slate-900 border border-slate-700/50 rounded-md p-1.5 text-[9px] font-mono text-slate-300 resize-none"
                                            defaultValue={JSON.stringify(v, null, 1)} onBlur={(e) => updateSelectedEntityOverridesJson(k, e.target.value)} />
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>
                )}

                {!['Area', 'Blocked', 'Biome', 'Vegetation', 'Layers', 'Territory', 'Obstacle', 'Entities'].includes(activeCategory) && (
                    <div>
                        <div className="flex justify-between text-[10px]"><span className="text-slate-500">Value</span><span className="text-slate-400 font-mono">{brushValue}</span></div>
                        <input type="range" min="0" max="15" value={brushValue} onChange={(e) => setBrushValue(parseInt(e.target.value))} className="w-full accent-indigo-500" />
                    </div>
                )}
            </Section>

            <div className="px-3 py-2 text-[9px] text-slate-600 border-t border-white/5">
                L: Paint · M: Pan · R: Rotate
            </div>
        </div>
    );
};
