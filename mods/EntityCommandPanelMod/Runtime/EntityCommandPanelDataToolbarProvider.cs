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

        public bool IsVisible => _catalog.IsPanelVisible(_engine, _panelId);

        public uint Revision
        {
            get
            {
                if (!IsVisible)
                {
                    return 0;
                }

                uint revision = 1u ^ _catalog.HighlightRevision(_engine, _panelId);
                IEntityCommandPanelToolbarTail? tail = VisibleTail();
                if (tail != null)
                {
                    revision ^= tail.Revision;
                }

                return revision;
            }
        }

        public string Title => Text(title: true);

        public string Subtitle => Text(title: false);

        public int CopyButtons(Span<EntityCommandPanelToolbarButtonView> destination)
        {
            if (destination.IsEmpty || !IsVisible)
            {
                return 0;
            }

            int dataWritten = _catalog.CopyVisibleButtons(_engine, _panelId, destination, out int dataTotal);
            IEntityCommandPanelToolbarTail? tail = VisibleTail();
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
            if (string.IsNullOrWhiteSpace(buttonId))
            {
                throw new InvalidOperationException($"'{EntityCommandPanelToolbarCatalog.RelativePath}' button id is empty.");
            }

            if (_catalog.TryActivate(_engine, _panelId, buttonId))
            {
                return;
            }

            IEntityCommandPanelToolbarTail? tail = VisibleTail();
            if (tail != null && tail.TryActivate(buttonId))
            {
                return;
            }

            throw new InvalidOperationException($"'{EntityCommandPanelToolbarCatalog.RelativePath}' has no button '{buttonId}'.");
        }

        private string Text(bool title)
        {
            if (!IsVisible)
            {
                return string.Empty;
            }

            IEntityCommandPanelToolbarTail? tail = VisibleTail();
            string? tailText = title ? tail?.Title : tail?.Subtitle;
            if (!string.IsNullOrEmpty(tailText))
            {
                return tailText;
            }

            return title
                ? _catalog.VisibleTitle(_engine, _panelId)
                : _catalog.VisibleSubtitle(_engine, _panelId);
        }

        private IEntityCommandPanelToolbarTail? VisibleTail()
        {
            if (!IsVisible)
            {
                return null;
            }

            return _tailFor(_catalog.VisibleToolbarId(_engine, _panelId));
        }
    }
}
