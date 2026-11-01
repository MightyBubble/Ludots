using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    public sealed class EntityCommandPanelDataToolbarProvider : IEntityCommandPanelToolbarProvider
    {
        private readonly GameEngine _engine;
        private readonly EntityCommandPanelToolbarCatalog _catalog;
        private readonly string _panelId;

        public EntityCommandPanelDataToolbarProvider(
            GameEngine engine,
            EntityCommandPanelToolbarCatalog catalog,
            string panelId)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _panelId = panelId ?? throw new ArgumentNullException(nameof(panelId));
        }

        public bool IsVisible => _catalog.IsPanelVisible(_engine, _panelId, VisibilityRep());

        public uint Revision
        {
            get
            {
                if (!IsVisible)
                {
                    return 0;
                }

                return RevisionFor(RenderRep());
            }
        }

        public uint RevisionFor(Entity rep)
        {
            if (!_catalog.IsPanelVisible(_engine, _panelId, rep))
            {
                return 0;
            }

            return 1u ^ _catalog.HighlightRevision(_engine, _panelId, rep);
        }

        public string Title => Text(title: true);

        public string Subtitle
        {
            get
            {
                if (!IsVisible)
                {
                    return string.Empty;
                }

                return _catalog.VisibleSubtitle(_engine, _panelId, RenderRep());
            }
        }

        public int CopyButtons(Span<EntityCommandPanelToolbarButtonView> destination)
        {
            return CopyButtons(RenderRep(), destination);
        }

        public int CopyButtons(Entity rep, Span<EntityCommandPanelToolbarButtonView> destination)
        {
            if (destination.IsEmpty || !_catalog.IsPanelVisible(_engine, _panelId, rep))
            {
                return 0;
            }

            return _catalog.CopyVisibleButtons(_engine, _panelId, rep, destination, out _);
        }

        public void Activate(string buttonId)
        {
            Activate(RenderRep(), buttonId);
        }

        public void Activate(Entity rep, string buttonId)
        {
            if (string.IsNullOrWhiteSpace(buttonId))
            {
                throw new InvalidOperationException($"'{EntityCommandPanelToolbarCatalog.RelativePath}' button id is empty.");
            }

            _catalog.Activate(_engine, _panelId, rep, buttonId);
        }

        private string Text(bool title)
        {
            Entity rep = VisibilityRep();
            if (!_catalog.IsPanelVisible(_engine, _panelId, rep))
            {
                return string.Empty;
            }

            return title
                ? _catalog.VisibleTitle(_engine, _panelId, rep)
                : _catalog.VisibleSubtitle(_engine, _panelId, rep);
        }

        private Entity VisibilityRep()
        {
            return _catalog.PanelUsesContextVisibility(_panelId)
                ? EntityCommandPanelToolbarRep.Require(_engine)
                : Entity.Null;
        }

        private Entity RenderRep()
        {
            return EntityCommandPanelToolbarRep.Require(_engine);
        }
    }
}
