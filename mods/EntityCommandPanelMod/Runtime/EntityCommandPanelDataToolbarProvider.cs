using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    /// <summary>
    /// Appended buttons stay behind the declared ones, so a short copy buffer still truncates from the front.
    /// </summary>
    public interface IEntityCommandPanelToolbarTail
    {
        uint Revision { get; }

        string? Title { get; }

        string? Subtitle { get; }

        int ButtonCount { get; }

        int CopyButtons(Span<EntityCommandPanelToolbarButtonView> destination);

        bool TryActivate(string buttonId);
    }

    public sealed class EntityCommandPanelDataToolbarProvider : IEntityCommandPanelToolbarProvider
    {
        private readonly GameEngine _engine;
        private readonly EntityCommandPanelToolbarCatalog _catalog;
        private readonly string _panelId;
        private readonly Func<string, IEntityCommandPanelToolbarTail?> _tailFor;

        public EntityCommandPanelDataToolbarProvider(
            GameEngine engine,
            EntityCommandPanelToolbarCatalog catalog,
            string panelId,
            Func<string, IEntityCommandPanelToolbarTail?> tailFor)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _panelId = panelId ?? throw new ArgumentNullException(nameof(panelId));
            _tailFor = tailFor ?? throw new ArgumentNullException(nameof(tailFor));
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

            uint revision = 1u ^ _catalog.HighlightRevision(_engine, _panelId, rep);
            IEntityCommandPanelToolbarTail? tail = TailFor(rep);
            if (tail != null)
            {
                revision ^= tail.Revision;
            }

            return revision;
        }

        public string Title => Text(title: true);

        public string Subtitle => Text(title: false);

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

            int dataWritten = _catalog.CopyVisibleButtons(_engine, _panelId, rep, destination, out int dataTotal);
            IEntityCommandPanelToolbarTail? tail = TailFor(rep);
            int tailTotal = tail?.ButtonCount ?? 0;
            int want = Math.Min(destination.Length, dataTotal + tailTotal);
            if (want <= dataWritten)
            {
                return want;
            }

            int tailWant = want - dataWritten;
            int tailWritten = tail!.CopyButtons(destination.Slice(dataWritten, tailWant));
            if (tailWritten != tailWant)
            {
                throw new InvalidOperationException(
                    $"'{EntityCommandPanelToolbarCatalog.RelativePath}' tail wrote {tailWritten} buttons, expected {tailWant}.");
            }

            return want;
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

            if (_catalog.TryActivate(_engine, _panelId, rep, buttonId))
            {
                return;
            }

            IEntityCommandPanelToolbarTail? tail = TailFor(rep);
            if (tail != null && tail.TryActivate(buttonId))
            {
                return;
            }

            throw new InvalidOperationException($"'{EntityCommandPanelToolbarCatalog.RelativePath}' has no button '{buttonId}'.");
        }

        private string Text(bool title)
        {
            Entity rep = VisibilityRep();
            if (!_catalog.IsPanelVisible(_engine, _panelId, rep))
            {
                return string.Empty;
            }

            IEntityCommandPanelToolbarTail? tail = TailFor(rep);
            string? tailText = title ? tail?.Title : tail?.Subtitle;
            if (!string.IsNullOrEmpty(tailText))
            {
                return tailText;
            }

            return title
                ? _catalog.VisibleTitle(_engine, _panelId, rep)
                : _catalog.VisibleSubtitle(_engine, _panelId, rep);
        }

        private IEntityCommandPanelToolbarTail? TailFor(Entity rep)
        {
            if (!_catalog.IsPanelVisible(_engine, _panelId, rep))
            {
                return null;
            }

            return _tailFor(_catalog.VisibleToolbarId(_engine, _panelId, rep));
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
