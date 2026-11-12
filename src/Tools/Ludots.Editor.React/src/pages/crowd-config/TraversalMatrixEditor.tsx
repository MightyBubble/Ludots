import {
  LINK_FIELDS,
  readNumber,
  setAreaIgnoresSlope,
  setCellNumber,
  setEndpoint,
  setLinkField,
  setLinkSpeedRatio,
  setMaxSlope,
  toggleLink,
  togglePassable,
  type LinkField,
  type Traversal,
} from './traversalMatrix';

type Props = {
  traversal: Traversal;
  defaults: Traversal | null;
  mapAreas: string[];
  onChange: (next: Traversal) => void;
};

const LINK_LABELS: Record<LinkField, string> = {
  upCm: '上跳 cm',
  downCm: '下跳 cm',
  rangeNavCells: '跨度 格',
  costPerNavCell: '每格代价',
};

const NUMBER_CLASS = 'w-14 rounded border border-studio-elevated bg-studio-bg px-1 py-0.5 font-mono text-studio-label';

/** 失焦才提交，输入到一半的 "1." 不会被改写成 1。值从外面变了（重置、重读）就换 key 重新挂载。 */
function NumberField({ id, value, title, onCommit }: { id: string; value: number; title: string; onCommit: (value: number) => void }) {
  return (
    <input
      key={`${id}:${String(value)}`}
      data-traversal-field={id}
      className={NUMBER_CLASS}
      title={title}
      defaultValue={Number.isFinite(value) ? String(value) : ''}
      onBlur={(event) => {
        const next = readNumber(event.target.value);
        if (!Object.is(next, value)) onCommit(next);
      }}
    />
  );
}

export function TraversalMatrixEditor({ traversal, defaults, mapAreas, onChange }: Props) {
  const areaIds = Object.keys(traversal.areas);
  const kinds = Object.keys(traversal.linkKinds);
  const onMap = new Set(mapAreas);

  return (
    <div className="mt-4 overflow-auto rounded-xl border border-studio-elevated bg-studio-surface p-4" data-cost-matrix="">
      <h2 className="text-sm font-semibold">移动类型 × 区域</h2>
      <p className="mt-1 text-xs leading-relaxed text-studio-muted">
        每一格都要写明：不勾“可走”就是 false。可走的格写代价和速度除数，再勾这一格能不能做链接的起点、落点。
        标“本图”的列是这张图的地形会落到的区域，其余列来自结构模板或别的图。
      </p>

      {kinds.length > 0 ? (
        <div className="mt-3 flex flex-wrap gap-4 text-xs">
          {kinds.map((kind) => (
            <label key={kind} className="flex items-center gap-2">
              <span className="font-mono">{kind}</span>
              <span className="text-studio-muted">走链接时的速度倍率</span>
              <NumberField
                id={`linkKinds.${kind}.traversalSpeedRatio`}
                value={traversal.linkKinds[kind].traversalSpeedRatio}
                title="traversalSpeedRatio"
                onCommit={(value) => onChange(setLinkSpeedRatio(traversal, kind, value))}
              />
            </label>
          ))}
        </div>
      ) : null}

      <table className="mt-3 w-full border-collapse text-left text-xs">
        <thead>
          <tr className="align-bottom text-studio-muted">
            <th className="min-w-[11rem] py-1 pr-3 font-normal">移动类型</th>
            {areaIds.map((areaId) => (
              <th
                key={areaId}
                data-traversal-area={areaId}
                data-on-map={onMap.has(areaId) ? '' : undefined}
                className={`min-w-[8rem] whitespace-nowrap border-b-2 px-2 py-1 font-normal ${onMap.has(areaId) ? 'border-studio-blue bg-studio-elevated text-studio-label' : 'border-transparent'}`}
              >
                <span className="block font-mono">
                  {areaId}
                  {onMap.has(areaId) ? <span className="ml-1 text-studio-blue">本图</span> : null}
                </span>
                <label className="mt-1 flex items-center gap-1">
                  <input
                    type="checkbox"
                    checked={traversal.areas[areaId].ignoresSlope}
                    onChange={(event) => onChange(setAreaIgnoresSlope(traversal, areaId, event.target.checked))}
                  />
                  不看坡度
                </label>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {Object.entries(traversal.agentTypes).map(([agentType, row]) => (
            <tr key={agentType} data-traversal-row={agentType} className="border-t border-studio-elevated align-top">
              <th className="whitespace-nowrap py-2 pr-3 font-normal">
                <span className="block font-mono text-studio-label">{agentType}</span>
                <span className="mt-1 flex items-center gap-1">
                  最大坡度
                  {row.maxSlopeDeg === null ? null : (
                    <NumberField
                      id={`agentTypes.${agentType}.maxSlopeDeg`}
                      value={row.maxSlopeDeg}
                      title="maxSlopeDeg（度）"
                      onCommit={(value) => onChange(setMaxSlope(traversal, agentType, value))}
                    />
                  )}
                  <label className="flex items-center gap-1">
                    <input
                      type="checkbox"
                      checked={row.maxSlopeDeg === null}
                      onChange={(event) => {
                        const fallback = defaults?.agentTypes[agentType]?.maxSlopeDeg ?? 30;
                        onChange(setMaxSlope(traversal, agentType, event.target.checked ? null : fallback));
                      }}
                    />
                    不限
                  </label>
                </span>
                {kinds.map((kind) => {
                  const link = row.links[kind];
                  return (
                    <span key={kind} className="mt-1 block" data-traversal-link={`${agentType}.${kind}`}>
                      <label className="flex items-center gap-1">
                        <input
                          type="checkbox"
                          checked={link !== false}
                          onChange={(event) => onChange(toggleLink(traversal, defaults, agentType, kind, event.target.checked))}
                        />
                        <span className="font-mono">{kind}</span>
                      </label>
                      {link === false ? null : (
                        <span className="mt-1 grid grid-cols-2 gap-1">
                          {LINK_FIELDS.map((field) => (
                            <label key={field} className="flex flex-col text-studio-muted">
                              {LINK_LABELS[field]}
                              <NumberField
                                id={`agentTypes.${agentType}.links.${kind}.${field}`}
                                value={link[field]}
                                title={field}
                                onCommit={(value) => onChange(setLinkField(traversal, agentType, kind, field, value))}
                              />
                            </label>
                          ))}
                        </span>
                      )}
                    </span>
                  );
                })}
              </th>
              {areaIds.map((areaId) => {
                const cell = row.cells[areaId];
                return (
                  <td
                    key={areaId}
                    data-traversal-cell={`${agentType}.${areaId}`}
                    className={`whitespace-nowrap px-2 py-2 ${onMap.has(areaId) ? 'bg-studio-elevated' : ''}`}
                  >
                    <label className="flex items-center gap-1">
                      <input
                        type="checkbox"
                        checked={cell !== false}
                        onChange={(event) => onChange(togglePassable(traversal, defaults, agentType, areaId, event.target.checked))}
                      />
                      可走
                    </label>
                    {cell === false ? null : (
                      <>
                        <span className="mt-1 flex gap-1">
                          <NumberField
                            id={`agentTypes.${agentType}.cells.${areaId}.cost`}
                            value={cell.cost}
                            title="cost（寻路代价）"
                            onCommit={(value) => onChange(setCellNumber(traversal, agentType, areaId, 'cost', value))}
                          />
                          <NumberField
                            id={`agentTypes.${agentType}.cells.${areaId}.speedDivisor`}
                            value={cell.speedDivisor}
                            title="speedDivisor（速度除数）"
                            onCommit={(value) => onChange(setCellNumber(traversal, agentType, areaId, 'speedDivisor', value))}
                          />
                        </span>
                        {kinds
                          .filter((kind) => row.links[kind] !== false)
                          .map((kind) => (
                            <span key={kind} className="mt-1 flex gap-2 text-studio-muted">
                              {(['depart', 'arrive'] as const).map((side) => (
                                <label key={side} className="flex items-center gap-1">
                                  <input
                                    type="checkbox"
                                    data-traversal-endpoint={`${agentType}.${areaId}.${side}.${kind}`}
                                    checked={cell[side].includes(kind)}
                                    onChange={(event) => onChange(setEndpoint(traversal, agentType, areaId, side, kind, event.target.checked))}
                                  />
                                  {side === 'depart' ? '起' : '落'}
                                </label>
                              ))}
                            </span>
                          ))}
                      </>
                    )}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
