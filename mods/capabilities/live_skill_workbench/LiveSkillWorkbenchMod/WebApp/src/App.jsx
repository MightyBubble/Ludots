import { CatalogRail } from './components/CatalogRail.jsx';
import { EffectChainTimeline } from './components/EffectChainTimeline.jsx';
import { GraphWorkspace } from './components/GraphWorkspace.jsx';
import { Inspector } from './components/Inspector.jsx';
import { NumericForm } from './components/NumericForm.jsx';
import { Toolbar } from './components/Toolbar.jsx';
import { useWorkbenchSession } from './hooks/useWorkbenchSession.js';

const TABS = [
  { id: 'numeric', label: '数值' },
  { id: 'graph', label: 'Graph' },
  { id: 'effects', label: '效果链' }
];

export function App() {
  const session = useWorkbenchSession();
  const emptyDocument = !session.boot.preview
    && session.connection.phase === 'connected'
    && session.snapshot.hasDocument === false;

  if (session.boot.mode === 'missing-host') {
    return (
      <div className="lsw-shell lsw-shell--error">
        <Toolbar
          preview={false}
          connection={session.connection}
          snapshot={session.snapshot}
          onPrecheck={() => {}}
          onApply={() => {}}
          localError={session.localError}
        />
        <main className="lsw-fatal">
          <h1>无法连接真实游戏宿主</h1>
          <p>{session.boot.error}</p>
          <p>工作台不会自动加载假数据。若仅做浏览器开发，请使用 <code>?preview=1</code>。</p>
        </main>
      </div>
    );
  }

  return (
    <div className={`lsw-shell ${session.boot.preview ? 'is-preview' : ''}`}>
      <Toolbar
        preview={session.boot.preview}
        connection={session.connection}
        snapshot={session.snapshot}
        onPrecheck={session.precheck}
        onApply={session.applyNextCast}
        localError={session.localError}
      />
      {emptyDocument ? (
        <main className="lsw-fatal lsw-fatal--empty">
          <h1>尚未加载技能文档</h1>
          <p>当前宿主没有注入可编辑的技能目录或字段描述。工作台不会回退到示例火球术。</p>
          <p>请接入真实文档源后再编辑；浏览器开发可用 <code>?preview=1</code> 查看示意流程。</p>
        </main>
      ) : (
        <div className="lsw-body">
          <CatalogRail
            catalog={session.snapshot.catalog}
            selectedId={session.snapshot.selectedCatalogId}
            query={session.catalogQuery}
            onQueryChange={session.setCatalogQuery}
            onSelect={session.selectCatalogItem}
          />
          <main className="lsw-workspace">
            <div className="lsw-tabs" role="tablist">
              {TABS.map((tab) => (
                <button
                  key={tab.id}
                  type="button"
                  role="tab"
                  aria-selected={session.activeTab === tab.id}
                  className={session.activeTab === tab.id ? 'is-active' : ''}
                  onClick={() => session.setActiveTab(tab.id)}
                >
                  {tab.label}
                </button>
              ))}
            </div>
            <div className="lsw-workspace__content">
              {session.activeTab === 'numeric' ? (
                <NumericForm
                  fields={session.snapshot.fields}
                  draftValues={session.draftValues}
                  validationErrors={session.validationErrors}
                  onChange={session.updateDraftValue}
                  onStage={session.stageDrafts}
                  selectedId={session.snapshot.selectedCatalogId}
                />
              ) : null}
              {session.activeTab === 'graph' ? (
                <GraphWorkspace graph={session.snapshot.graph} />
              ) : null}
              {session.activeTab === 'effects' ? (
                <EffectChainTimeline events={session.snapshot.effectChain} />
              ) : null}
            </div>
          </main>
          <Inspector snapshot={session.snapshot} />
        </div>
      )}
    </div>
  );
}
