import { Search } from 'lucide-react';

const KIND_ORDER = ['actor', 'ability', 'effect', 'graph', 'tag', 'attribute'];
const KIND_LABELS = {
  actor: '角色',
  ability: '技能',
  effect: '效果',
  graph: 'Graph',
  tag: '标签',
  attribute: '属性'
};

export function CatalogRail({
  catalog = [],
  selectedId,
  query,
  onQueryChange,
  onSelect
}) {
  const normalized = query.trim().toLowerCase();
  const filtered = catalog.filter((item) => {
    if (!normalized) {
      return true;
    }
    return [item.label, item.id, item.kind, ...(item.tags ?? [])]
      .join(' ')
      .toLowerCase()
      .includes(normalized);
  });

  const grouped = KIND_ORDER
    .map((kind) => ({
      kind,
      label: KIND_LABELS[kind] ?? kind,
      items: filtered.filter((item) => item.kind === kind)
    }))
    .filter((group) => group.items.length > 0);

  return (
    <aside className="lsw-catalog">
      <div className="lsw-catalog__search">
        <Search size={14} />
        <input
          value={query}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder="搜索目录"
          aria-label="搜索目录"
        />
      </div>
      <div className="lsw-catalog__list">
        {grouped.length === 0 ? (
          <p className="lsw-muted">目录为空：尚未加载技能文档。</p>
        ) : (
          grouped.map((group) => (
            <section key={group.kind} className="lsw-catalog__group">
              <header>{group.label}</header>
              <ul>
                {group.items.map((item) => (
                  <li key={item.id}>
                    <button
                      type="button"
                      className={item.id === selectedId ? 'is-selected' : ''}
                      onClick={() => onSelect(item.id)}
                    >
                      <span>{item.label}</span>
                      <small>{item.id}</small>
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          ))
        )}
      </div>
    </aside>
  );
}
