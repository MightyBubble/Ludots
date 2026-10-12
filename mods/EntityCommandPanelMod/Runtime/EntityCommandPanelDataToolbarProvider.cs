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
        private readonly IEntityCommandPanelToolbarTail? _tail;

        public EntityCommandPanelDataToolbarProvider(
            GameEngine engine,
            EntityCommandPanelToolbarCatalog catalog,
            IEntityCommandPanelToolbarTail? tail = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _tail = tail;
        }

        public bool IsVisible => _catalog.IsSingleToolbarVisible(_engine);

        public uint Revision
        {
            get
            {
                if (!IsVisible)
                {
                    return 0;
                }

                uint revision = 1u ^ _catalog.HighlightRevision(_engine);
                if (_tail != null)
                {
                    revision ^= _tail.Revision;
                }

                return revision;
            }
        }

        public string Title
        {
            get
            {
                if (!IsVisible)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrEmpty(_tail?.Title))
                {
                    return _tail.Title;
                }

                return _catalog.VisibleTitle(_engine);
            }
        }

        public string Subtitle
        {
            get
            {
                if (!IsVisible)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrEmpty(_tail?.Subtitle))
                {
                    return _tail.Subtitle;
                }

                return _catalog.VisibleSubtitle(_engine);
            }
        }

        public int CopyButtons(Span<EntityCommandPanelToolbarButtonView> destination)
        {
            if (destination.IsEmpty || !IsVisible)
            {
                return 0;
            }

            int dataWritten = _catalog.CopyVisibleButtons(_engine, destination, out int dataTotal);
            int tailTotal = _tail?.ButtonCount ?? 0;
            int want = Math.Min(destination.Length, dataTotal + tailTotal);
            if (want <= dataWritten)
            {
                return want;
            }

            int tailWant = want - dataWritten;
            int tailWritten = _tail!.CopyButtons(destination.Slice(dataWritten, tailWant));
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

            if (_catalog.TryActivate(_engine, buttonId))
            {
                return;
            }

            if (_tail != null && _tail.TryActivate(buttonId))
            {
                return;
            }

            throw new InvalidOperationException($"'{EntityCommandPanelToolbarCatalog.RelativePath}' has no button '{buttonId}'.");
        }
    }
}
