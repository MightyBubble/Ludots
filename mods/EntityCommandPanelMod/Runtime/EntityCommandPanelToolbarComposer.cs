using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    /// <summary>
    /// The command panel's one toolbar slot. Data toolbars bind by the panel id in
    /// <c>UI/entity_command_panel_toolbars.json</c>. Code toolbars register on that same id.
    /// Two visible toolbars on this panel fail. A different panel id is not this slot.
    /// </summary>
    public sealed class EntityCommandPanelToolbarComposer : IEntityCommandPanelToolbarProvider
    {
        public const string CommandPanelId = "entity-command-panel";

        public static readonly ServiceKey<EntityCommandPanelToolbarComposer> ServiceKey = new("EntityCommandPanelMod.ToolbarComposer");

        private static readonly object Gate = new();

        private readonly GameEngine _engine;
        private readonly object _gate = new();
        private readonly List<Contributor> _contributors = new();
        private EntityCommandPanelToolbarCatalog? _catalog;
        private EntityCommandPanelDataToolbarProvider? _data;

        private EntityCommandPanelToolbarComposer(GameEngine engine)
        {
            _engine = engine;
        }

        public static EntityCommandPanelToolbarComposer Require(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            lock (Gate)
            {
                if (engine.TryGetService(ServiceKey, out EntityCommandPanelToolbarComposer? existing) && existing != null)
                {
                    IEntityCommandPanelToolbarProvider? current = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider);
                    if (!ReferenceEquals(current, existing))
                    {
                        engine.SetService(CoreServiceKeys.EntityCommandPanelToolbarProvider, existing);
                    }

                    return existing;
                }

                var created = new EntityCommandPanelToolbarComposer(engine);
                engine.SetService(ServiceKey, created);
                engine.SetService(CoreServiceKeys.EntityCommandPanelToolbarProvider, created);
                return created;
            }
        }

        public void Bind(EntityCommandPanelToolbarCatalog catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            lock (_gate)
            {
                if (_catalog != null && !ReferenceEquals(_catalog, catalog))
                {
                    throw new InvalidOperationException($"'{EntityCommandPanelToolbarCatalog.RelativePath}' is already bound.");
                }

                _catalog = catalog;
                _data ??= new EntityCommandPanelDataToolbarProvider(_engine, catalog, CommandPanelId);
            }
        }

        public void RegisterContributor(string panelId, string contributorId, IEntityCommandPanelToolbarProvider provider)
        {
            if (string.IsNullOrWhiteSpace(panelId))
            {
                throw new ArgumentException("Panel id is required.", nameof(panelId));
            }

            if (string.IsNullOrWhiteSpace(contributorId))
            {
                throw new ArgumentException("Contributor id is required.", nameof(contributorId));
            }

            ArgumentNullException.ThrowIfNull(provider);
            EnsureCatalog();
            lock (_gate)
            {
                if (!_catalog!.ContainsPanel(panelId))
                {
                    throw new InvalidOperationException(
                        $"Toolbar contributor '{contributorId}' cannot bind to panel '{panelId}'. Declare that panel in '{EntityCommandPanelToolbarCatalog.PanelsRelativePath}'.");
                }

                for (int i = 0; i < _contributors.Count; i++)
                {
                    if (string.Equals(_contributors[i].Id, contributorId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Toolbar contributor '{contributorId}' is already registered.");
                    }
                }

                _contributors.Add(new Contributor(panelId, contributorId, provider));
            }
        }

        public void UnregisterContributor(string contributorId)
        {
            lock (_gate)
            {
                for (int i = 0; i < _contributors.Count; i++)
                {
                    if (!string.Equals(_contributors[i].Id, contributorId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    _contributors.RemoveAt(i);
                    return;
                }
            }

            throw new InvalidOperationException($"Toolbar contributor '{contributorId}' is not registered.");
        }

        public bool IsVisible => Resolve()?.IsVisible ?? false;

        public uint Revision => Resolve()?.Revision ?? 0;

        public string Title => Resolve()?.Title ?? string.Empty;

        public string Subtitle => Resolve()?.Subtitle ?? string.Empty;

        public bool ActiveToolbarReadsRep
        {
            get
            {
                IEntityCommandPanelToolbarProvider? provider = Resolve();
                return ReferenceEquals(provider, _data) && provider.IsVisible;
            }
        }

        public int CopyButtons(Span<EntityCommandPanelToolbarButtonView> destination)
        {
            return CopyResolved(Resolve(), rep: null, destination);
        }

        public int CopyButtons(Entity rep, Span<EntityCommandPanelToolbarButtonView> destination)
        {
            return CopyResolved(Resolve(), rep, destination);
        }

        public void Activate(string buttonId)
        {
            ActivateResolved(ResolveProvider(), rep: null, buttonId);
        }

        public void Activate(Entity rep, string buttonId)
        {
            ActivateResolved(ResolveProvider(), rep, buttonId);
        }

        private int CopyResolved(
            IEntityCommandPanelToolbarProvider? provider,
            Entity? rep,
            Span<EntityCommandPanelToolbarButtonView> destination)
        {
            if (provider == null || destination.IsEmpty)
            {
                return 0;
            }

            if (ReferenceEquals(provider, _data))
            {
                if (rep == null && !_data!.IsVisible)
                {
                    return 0;
                }

                return _data!.CopyButtons(rep ?? EntityCommandPanelToolbarRep.Require(_engine), destination);
            }

            return provider.CopyButtons(destination);
        }

        private void ActivateResolved(IEntityCommandPanelToolbarProvider provider, Entity? rep, string buttonId)
        {
            if (ReferenceEquals(provider, _data))
            {
                _data!.Activate(rep ?? EntityCommandPanelToolbarRep.Require(_engine), buttonId);
                return;
            }

            provider.Activate(buttonId);
        }

        private IEntityCommandPanelToolbarProvider ResolveProvider()
        {
            return Resolve()
                ?? throw new InvalidOperationException(
                    $"'{EntityCommandPanelToolbarCatalog.RelativePath}' panel '{CommandPanelId}' has no toolbar.");
        }

        private void EnsureCatalog()
        {
            if (_catalog != null)
            {
                return;
            }

            Bind(EntityCommandPanelToolbarCatalog.Require(_engine));
        }

        private IEntityCommandPanelToolbarProvider? Resolve()
        {
            EntityCommandPanelToolbarCatalog? catalog;
            EntityCommandPanelDataToolbarProvider? data;
            Contributor[] contributors;
            lock (_gate)
            {
                catalog = _catalog;
                data = _data;
                int count = 0;
                for (int i = 0; i < _contributors.Count; i++)
                {
                    if (string.Equals(_contributors[i].PanelId, CommandPanelId, StringComparison.Ordinal))
                    {
                        count++;
                    }
                }

                contributors = new Contributor[count];
                int written = 0;
                for (int i = 0; i < _contributors.Count; i++)
                {
                    if (string.Equals(_contributors[i].PanelId, CommandPanelId, StringComparison.Ordinal))
                    {
                        contributors[written++] = _contributors[i];
                    }
                }
            }

            if (catalog == null || data == null)
            {
                throw new InvalidOperationException($"'{EntityCommandPanelToolbarCatalog.RelativePath}' is not loaded.");
            }

            bool hasData = catalog.HasToolbarForPanel(CommandPanelId);
            Entity visibilityRep = hasData && catalog.PanelUsesContextVisibility(CommandPanelId)
                ? EntityCommandPanelToolbarRep.Require(_engine)
                : Entity.Null;
            bool dataVisible = hasData && catalog.IsPanelVisible(_engine, CommandPanelId, visibilityRep);
            var visibleIds = new List<string>();
            Contributor? visibleContributor = null;
            if (dataVisible)
            {
                visibleIds.Add(catalog.VisibleToolbarId(_engine, CommandPanelId, visibilityRep));
            }

            for (int i = 0; i < contributors.Length; i++)
            {
                if (!contributors[i].Provider.IsVisible)
                {
                    continue;
                }

                visibleIds.Add(contributors[i].Id);
                visibleContributor = contributors[i];
            }

            if (visibleIds.Count > 1)
            {
                throw new InvalidOperationException(
                    $"'{EntityCommandPanelToolbarCatalog.RelativePath}' panel '{CommandPanelId}' has more than one visible toolbar: {string.Join(", ", visibleIds)}.");
            }

            if (dataVisible)
            {
                return data;
            }

            if (visibleContributor != null)
            {
                return visibleContributor.Provider;
            }

            // One bound toolbar that is not visible still answers. The aggregation showcase
            // reads title and buttons while IsVisible is false.
            int bound = (hasData ? 1 : 0) + contributors.Length;
            if (bound != 1)
            {
                return null;
            }

            return contributors.Length == 1 ? contributors[0].Provider : data;
        }

        private sealed class Contributor
        {
            public Contributor(string panelId, string id, IEntityCommandPanelToolbarProvider provider)
            {
                PanelId = panelId;
                Id = id;
                Provider = provider;
            }

            public string PanelId { get; }
            public string Id { get; }
            public IEntityCommandPanelToolbarProvider Provider { get; }
        }
    }
}
