import csv, json, math, statistics
from pathlib import Path

root=Path(__file__).parent/'capture'
results={}
for path in sorted(root.glob('*.csv')):
    if path.name in ['summary.csv','frames-all.csv']: continue
    rows=[{k:float(v) for k,v in row.items()} for row in csv.DictReader(path.open())]
    if len(rows)!=720: continue
    rows=rows[360:]
    result={'n':len(rows)}
    for key in rows[0]:
        vals=sorted(row[key] for row in rows)
        result[key]={'mean':statistics.mean(vals),'median':statistics.median(vals),'p95':vals[math.ceil(len(vals)*.95)-1],'max':max(vals),'sum':sum(vals)}
    active=[row for row in rows if row['nav_steps']>0]
    for key in ['nav_steering','nav_hard','nav_flow','neighbor_candidates','hard_pairs','hard_candidates','hard_penetrating','moved_agents']:
        vals=[row[key] for row in active]
        result['active_'+key]={'mean':statistics.mean(vals),'median':statistics.median(vals),'max':max(vals)} if vals else {'mean':0,'median':0,'max':0}
    residual=[row['frame_ms']-row['tick']-row['post']-row['mode3d']-row['overlay']-row['end_draw'] for row in rows]
    result['residual']={'mean':statistics.mean(residual),'median':statistics.median(residual),'max':max(residual)}
    results[path.stem]=result
(root/'summary.json').write_text(json.dumps(results,indent=2))
fields=['frame_ms','simulation','presentation','hud','behavior','transform_sync','emit','animator','minimap_project','nav_steering','nav_hard','nav_flow','alloc','residual']
print('run | ' + ' | '.join(fields))
for name,r in results.items():
    print(name+' | '+' | '.join(f"{r[f]['mean']:.2f}" for f in fields))
print('COUNTS: moved/step, rays, candidates/step, pairs/step, gen0/gen1')
for name,r in results.items():
    print(name, *(round(r[f]['mean'],2) for f in ['active_moved_agents','terrain_rays','active_neighbor_candidates','active_hard_pairs']), r['gen0']['sum'],r['gen1']['sum'])
with (root/'summary.csv').open('w',newline='') as file:
    writer=csv.writer(file)
    writer.writerow(['run']+[f+'_'+s for f in fields for s in ['mean','median','p95','max']])
    for name,r in results.items():
        writer.writerow([name]+[r[f].get(s,'') for f in fields for s in ['mean','median','p95','max']])
