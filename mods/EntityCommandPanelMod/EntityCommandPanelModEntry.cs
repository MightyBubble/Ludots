using System.Threading.Tasks;
using EntityCommandPanelMod.Runtime;
using EntityCommandPanelMod.Systems;
using EntityCommandPanelMod.UI;
using Ludots.Core.EntityCollections;
using Ludots.Core.Modding;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod
{
    public sealed class EntityCommandPanelModEntry : IMod
    {
        private const string InstalledKey = "EntityCommandPanelMod.Installed";

        /// <summary>
        /// Default panel aggregation profile for collection selections (RFC-0065 PNL-4, DEC-10).
        /// The profile itself is declared in this mod's own
        /// <c>assets/Configs/UI/ability_aggregation_profiles.json</c> fragment (ArrayById merge into
        /// the Core structural profiles); switchable at runtime via
        /// <see cref="Runtime.CollectionGasEntityCommandPanelSource.SetAggregationProfile"/>.
        /// </summary>
        private const string DefaultAggregationProfileId = "aggregation.by_family";

        public void OnLoad(IModContext context)
        {
            context.Log("[EntityCommandPanelMod] Loaded");
            context.OnEvent(GameEvents.GameStart, ctx => InstallAsync(context, ctx));
        }

        public void OnUnload()
        {
        }

        private static Task InstallAsync(IModContext modContext, ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[InstalledKey] = true;

            var sources = new EntityCommandPanelSourceRegistry();
            var handles = new EntityCommandPanelAliasStore();
            var collectionQueries = new EntityCommandPanelCollectionQueryConfigRegistry();
            collectionQueries.Register(new EntityCommandPanelCollectionQueryConfig
            {
                Id = EntityCollectionKeys.CommandSource,
                CollectionKey = EntityCollectionKeys.CommandSource,
                Title = "Collection Commands",
                Filter = EntityCommandPanelCollectionFilter.Any,
                Sort = EntityCommandPanelCollectionSortKind.SlotThenOwnerCountThenLabel
            });
            collectionQueries.Register(new EntityCommandPanelCollectionQueryConfig
            {
                Id = EntityViewKeys.ControlPlaneCommand,
                CollectionKey = EntityViewKeys.ControlPlaneCommand,
                Title = "Control Plane Commands",
                Filter = EntityCommandPanelCollectionFilter.Any,
                Sort = EntityCommandPanelCollectionSortKind.SlotThenOwnerCountThenLabel
            });
            collectionQueries.Register(new EntityCommandPanelCollectionQueryConfig
            {
                Id = EntityViewKeys.CommandDeckFiltered,
                CollectionKey = EntityViewKeys.CommandDeckFiltered,
                Title = "CommandDeck Filtered Commands",
                Filter = EntityCommandPanelCollectionFilter.Any,
                Sort = EntityCommandPanelCollectionSortKind.SlotThenOwnerCountThenLabel
            });

            var gasSource = new GasEntityCommandPanelSource(engine);
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new System.InvalidOperationException("EntityCollectionStore must be registered before EntityCommandPanelMod installs.");
            var aggregationProfiles = engine.GetService(CoreServiceKeys.AbilityAggregationProfileRegistry)
                ?? throw new System.InvalidOperationException("AbilityAggregationProfileRegistry must be registered before EntityCommandPanelMod installs.");
            sources.Register(GasEntityCommandPanelSource.SourceId, gasSource);
            sources.Register(
                CollectionGasEntityCommandPanelSource.SourceId,
                new CollectionGasEntityCommandPanelSource(
                    engine, collections, gasSource, collectionQueries, aggregationProfiles, DefaultAggregationProfileId));

            var runtime = new EntityCommandPanelRuntime(engine, sources, handles);

            engine.SetService(CoreServiceKeys.EntityCommandPanelSourceRegistry, sources);
            engine.SetService(CoreServiceKeys.EntityCommandPanelCollectionQueryConfigRegistry, collectionQueries);
            engine.SetService(CoreServiceKeys.EntityCommandPanelHandleStore, handles);
            engine.SetService(CoreServiceKeys.EntityCommandPanelService, runtime);

            bool hasPresentationHost =
                engine.GetService(CoreServiceKeys.UiTextMeasurer) != null &&
                engine.GetService(CoreServiceKeys.UiImageSizeProvider) != null;
            if (!hasPresentationHost)
            {
                modContext.Log("[EntityCommandPanelMod] Installed GAS entity command panel runtime without presentation; this host provides no UI services.");
                return Task.CompletedTask;
            }

            var controller = new EntityCommandPanelController(engine, runtime);
            engine.RegisterPresentationSystem(new EntityCommandPanelPresentationSystem(engine, runtime, controller));

            modContext.Log("[EntityCommandPanelMod] Installed GAS entity command panel runtime.");
            return Task.CompletedTask;
        }
    }
}
