import React from 'react';
import { useEditorStore } from './EditorStore';
import { Play, Zap, Footprints, Upload } from 'lucide-react';
import { readNavTile } from '../../Core/NavMesh/NavTileBinary';
import type { NavTile } from '../../Core/NavMesh/NavTileBinary';

export const NavPanel: React.FC = () => {
    const s = useEditorStore();
    const { bridgeBaseUrl, terrain, selectedModId, navDirtyChunks, bakedNavTiles, setBakedNavTiles, setLoading } = s;
    const [navScope, setNavScope] = React.useState<'dirty'|'full'>('dirty');
    const [navIncludeNeighbors, setNavInc] = React.useState(true);
    const [navParallel, setNavParallel] = React.useState(true);
    const [navHeightScale, setNavHS] = React.useState(2.0);
    const [navMinUpDot, setNavUp] = React.useState(0.6);
    const [navCliff, setNavCliff] = React.useState(1);
    const [navWorkers, setNavW] = React.useState(Math.max(1,(navigator as any).hardwareConcurrency??4));
    const mapId = s.selectedMapId ?? 'nav_editor_grid';
    const [estimate, setEstimate] = React.useState<any>(null);
    const [err, setErr] = React.useState<string|null>(null);

    const b64toBuf = (b64:string) => { const b=atob(b64); const u=new Uint8Array(b.length); for(let i=0;i<b.length;i++)u[i]=b.charCodeAt(i); return u.buffer; };
    const buildForm = () => {
        const f=new FormData(); const ds=new Set<string>();
        for(const k of terrain.dirtyChunks)ds.add(k); for(const k of navDirtyChunks)ds.add(k);
        const dc=Array.from(ds);
        f.append('dirty',JSON.stringify(dc)); f.append('dirtyOnly',navScope==='dirty'?'true':'false');
        f.append('includeNeighbors',navIncludeNeighbors?'true':'false'); f.append('parallel',navParallel?'true':'false');
        f.append('maxDegree',String(navWorkers)); f.append('heightScale',String(navHeightScale));
        f.append('minUpDot',String(navMinUpDot)); f.append('cliffThreshold',String(navCliff));
        f.append('tileVersion','2'); f.append('mapId', mapId ?? 'nav_editor_grid');
        if(selectedModId)f.append('modId',selectedModId);
        f.append('map',new Blob([terrain.serialize()],{type:'application/octet-stream'}),'map_data.bin');
        return f;
    };

    const doEstimate = async () => {
        setErr(null);
        if (!s.selectedModId) { setErr(`No Mod selected. Pick one from the top bar first.`); return; }
        if (!s.selectedMapId) { setErr(`No Map loaded. Select a Map in top bar and click Load.`); return; }
        try{const r=await fetch(`${bridgeBaseUrl}/api/nav/estimate-recast-react`,{method:'POST',body:buildForm()});
        const j=await r.json(); setEstimate(j.estimate);}catch(e:any){setErr(e.message??String(e));}
    };

    const doBake = async () => {
        setErr(null);
        if (!s.selectedModId) { setErr(`No Mod selected. Pick one from the top bar first.`); return; }
        if (!s.selectedMapId) { setErr(`No Map loaded. Select a Map in top bar and click Load.`); return; }
        setLoading(true,'Baking...',30);
        try{const r=await fetch(`${bridgeBaseUrl}/api/nav/bake-recast-react`,{method:'POST',body:buildForm()});
        if(!r.ok){setErr(await r.text());setLoading(false);return;}
        const j=await r.json(); const tiles:NavTile[]=[];
        for(const t of (j.tiles??[])){try{tiles.push(readNavTile(b64toBuf(t.base64)));}catch{}}
        setBakedNavTiles(tiles); setLoading(false);
        }catch(e:any){setErr(e.message??String(e));setLoading(false);}
    };

    const handleLoad = async (e:React.ChangeEvent<HTMLInputElement>) => {
        const fs=Array.from(e.target.files??[]); const tiles:NavTile[]=[];
        for(const f of fs){const b=await f.arrayBuffer();try{tiles.push(readNavTile(b));}catch{}}
        if(tiles.length)setBakedNavTiles(tiles);
    };

    return (
        <div className="absolute bottom-4 right-3 w-[260px] bg-slate-900/90 backdrop-blur-xl border border-white/5 rounded-2xl shadow-2xl z-30 overflow-hidden">
            <div className="flex items-center gap-1.5 px-3 py-2 border-b border-white/5">
                <div className="flex items-center gap-2 text-[11px] font-medium text-slate-500">
                    <Footprints size={13} className="text-orange-400" /> Nav Bake
                </div>
            </div>
            <div className="px-3 pb-3 space-y-2">
                <div className="flex gap-1">
                    
                    <select value={navScope==='full'?'full':(navIncludeNeighbors?'dirtyN':'dirty')}
                        onChange={e=>{const v=e.target.value; if(v==='full'){setNavScope('full');setNavInc(true)}else if(v==='dirtyN'){setNavScope('dirty');setNavInc(true)}else{setNavScope('dirty');setNavInc(false)}}}
                        className="bg-slate-800/80 border border-slate-700/50 rounded-lg px-2 py-1.5 text-xs text-slate-200 w-24">
                        <option value="dirtyN">Dirty+N</option>
                        <option value="dirty">Dirty</option>
                        <option value="full">Full</option>
                    </select>
                </div>
                <div className="flex gap-1.5">
                    <button onClick={doEstimate}
                        className="flex-1 px-3 py-2 rounded-lg bg-sky-600 text-white text-xs font-semibold hover:bg-sky-500 transition-all">
                        <Zap size={13} className="inline mr-1"/>Estimate</button>
                    <button onClick={doBake}
                        className="flex-1 px-3 py-2 rounded-lg bg-orange-600 text-white text-xs font-semibold hover:bg-orange-500 transition-all disabled:opacity-40"
                        disabled={estimate?.budgetStatusText==='reject'}>
                        <Play size={13} className="inline mr-1"/>Bake</button>
                </div>
                <div className="grid grid-cols-2 gap-1.5">
                    <label className="text-[10px] text-slate-500">Height
                        <input type="number" step="0.1" min="0.1" value={navHeightScale} onChange={e=>setNavHS(Number(e.target.value)||0.1)}
                            className="mt-0.5 w-full bg-slate-800/80 border border-slate-700/50 rounded-md px-2 py-1 text-[11px] text-slate-300"/></label>
                    <label className="text-[10px] text-slate-500">Up Dot
                        <input type="number" step="0.05" min="-1" max="1" value={navMinUpDot} onChange={e=>setNavUp(Number(e.target.value))}
                            className="mt-0.5 w-full bg-slate-800/80 border border-slate-700/50 rounded-md px-2 py-1 text-[11px] text-slate-300"/></label>
                    <label className="text-[10px] text-slate-500">Cliff
                        <input type="number" step="1" min="0" value={navCliff} onChange={e=>setNavCliff(Math.max(0,Math.floor(Number(e.target.value)||0)))}
                            className="mt-0.5 w-full bg-slate-800/80 border border-slate-700/50 rounded-md px-2 py-1 text-[11px] text-slate-300"/></label>
                    <label className="text-[10px] text-slate-500">Workers
                        <input type="number" step="1" min="1" value={navWorkers} onChange={e=>setNavW(Math.max(1,Math.floor(Number(e.target.value)||1)))}
                            className="mt-0.5 w-full bg-slate-800/80 border border-slate-700/50 rounded-md px-2 py-1 text-[11px] text-slate-300"/></label>
                </div>
                <div className="flex gap-1">
                    <label className="flex-1 py-1.5 rounded-lg bg-slate-800/80 border border-slate-700/50 text-[10px] text-slate-400 cursor-pointer text-center hover:bg-slate-700 transition-all">
                        <Upload size={11} className="inline mr-1"/>Load .ntil
                        <input type="file" className="hidden" multiple accept=".ntil" onChange={handleLoad}/>
                    </label>
                </div>

                {estimate && (
                    <div className={`rounded-lg border p-2.5 text-[10px] ${
                        estimate.budgetStatusText==='ok'?'bg-emerald-950/30 border-emerald-700/60 text-emerald-100':
                        estimate.budgetStatusText==='large'?'bg-amber-950/30 border-amber-700/60 text-amber-100':
                        'bg-red-950/30 border-red-700/60 text-red-100'}`}>
                        <div className="flex justify-between font-bold mb-1"><span>{estimate.budgetStatusText}</span><span>{estimate.estimatedSecondsLow.toFixed(1)}-{estimate.estimatedSecondsHigh.toFixed(1)}s</span></div>
                        <div className="grid grid-cols-2 gap-x-2 opacity-80">
                            <div>tiles {estimate.targetTileCount}/{estimate.fullTileCount}</div>
                            <div>ops {estimate.bakeOperationCount}</div>
                            <div>work {estimate.budgetWorkUnitCount.toLocaleString()}</div>
                            <div>cols {estimate.recastColumnBudgetTotal.toLocaleString()}</div>
                        </div>
                    </div>
                )}
                {err && <div className="rounded-lg border border-red-800 bg-red-950/40 p-2.5 text-[10px] text-red-200">{err}</div>}
            </div>
        </div>
    );
};
