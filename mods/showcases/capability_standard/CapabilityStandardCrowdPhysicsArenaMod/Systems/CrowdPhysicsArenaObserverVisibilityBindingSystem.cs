using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Knowledge;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Client;
using Ludots.Core.Scripting;

namespace CapabilityStandardCrowdPhysicsArenaMod.Systems;

/// <summary>
/// Publishes live knowledge for every auto-spawned arena squad agent to the local
/// observer so presenters, minimap markers, and command-source selection work
/// (same contract as the 10k mass-navigation showcase).
/// </summary>
internal sealed class CrowdPhysicsArenaObserverVisibilityBindingSystem : BaseSystem<World, float>
{
    private const int LiveKnowledgeConfidencePermille = 1000;

    private static readonly QueryDescription AgentQuery = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex>()
        .WithNone<PresentationDestroyPending>();

    private readonly GameEngine _engine;
    private Entity _publishedViewer = Entity.Null;
    private int _publishedStructuralRevision = -1;
    private int _publishedPresenterDefinitionVersion = -1;
    private KnowledgeIdMask256 _publishedAttributeMask;

    public CrowdPhysicsArenaObserverVisibilityBindingSystem(GameEngine engine)
        : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
    {
        _engine = engine;
    }

    public override void Update(in float dt)
    {
        if (!CapabilityStandardCrowdPhysicsArenaMapFocus.IsStartupMapFocused(_engine) ||
            !MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine) ||
            _engine.GetService(MassNavigationKeys.RuntimeBinding) is not { IsReady: true, Current: { } simulation } ||
            _engine.GetService(CoreServiceKeys.KnowledgeProjectionStore) is not KnowledgeProjectionStore knowledge ||
            _engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry) is not PresenterDefinitionRegistry presenters ||
            !TryResolveViewer(out Entity viewer))
        {
            return;
        }

        int expectedAgentCount = ResolveExpectedAgentCount(simulation);
        if (expectedAgentCount <= 0 || simulation.NavigationAgentCount < expectedAgentCount)
        {
            return;
        }

        KnowledgeIdMask256 attributeMask = ResolveHudAttributeMask(simulation, presenters);
        if (_publishedViewer == viewer &&
            _publishedStructuralRevision == simulation.StructuralChangeRevision &&
            _publishedPresenterDefinitionVersion == presenters.Version &&
            _publishedAttributeMask == attributeMask)
        {
            return;
        }

        int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(_engine.GlobalContext);
        var emptyMask = KnowledgeIdMask256.Empty;
        var record = new KnowledgeDisclosureRecord(
            KnowledgePresence.LiveVisible,
            KnowledgePositionAccess.Live,
            attributeMask,
            emptyMask,
            emptyMask,
            viewer,
            observedTick,
            expiryTick: 0,
            confidencePermille: LiveKnowledgeConfidencePermille,
            revision: 0);

        int published = PublishAgentKnowledge(knowledge, viewer, in record);
        if (published < expectedAgentCount)
        {
            return;
        }

        _publishedViewer = viewer;
        _publishedStructuralRevision = simulation.StructuralChangeRevision;
        _publishedPresenterDefinitionVersion = presenters.Version;
        _publishedAttributeMask = attributeMask;
    }

    private bool TryResolveViewer(out Entity viewer)
    {
        Entity candidate = ClientLocalSeatAccess.RequireSolePossessedRep(_engine);
        viewer = candidate;
        return candidate != Entity.Null && World.IsAlive(candidate);
    }

    private static int ResolveExpectedAgentCount(MassNavigationSimulationRuntime simulation)
    {
        return checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
    }

    private static KnowledgeIdMask256 ResolveHudAttributeMask(
        MassNavigationSimulationRuntime simulation,
        PresenterDefinitionRegistry presenters)
    {
        KnowledgeIdMask256 mask = KnowledgeIdMask256.Empty;
        ReadOnlySpan<int> teamIds = simulation.TeamIds;
        for (int i = 0; i < teamIds.Length; i++)
        {
            int teamId = teamIds[i];
            mask = mask.Union(ResolveHudAttributeMask(
                presenters,
                simulation.Config.Presentation.ResolveAgentPresenterId(teamId, heavy: false)));
            mask = mask.Union(ResolveHudAttributeMask(
                presenters,
                simulation.Config.Presentation.ResolveAgentPresenterId(teamId, heavy: true)));
        }

        return mask;
    }

    private static KnowledgeIdMask256 ResolveHudAttributeMask(
        PresenterDefinitionRegistry presenters,
        string presenterKey)
    {
        int definitionId = presenters.GetId(presenterKey);
        if (definitionId <= 0 || !presenters.TryGet(definitionId, out PresenterDefinition definition))
        {
            throw new InvalidOperationException(
                $"CrowdPhysicsArena observer visibility requires presenter definition '{presenterKey}'.");
        }

        return ResolveHudAttributeMask(presenters, definition);
    }

    private static KnowledgeIdMask256 ResolveHudAttributeMask(
        PresenterDefinitionRegistry presenters,
        PresenterDefinition definition)
    {
        KnowledgeIdMask256 mask = HasHudAssetBinding(definition)
            ? BuildMask(definition.RequiredAttributeIds)
            : KnowledgeIdMask256.Empty;

        ChildPresenterRef[] children = definition.Children;
        for (int i = 0; i < children.Length; i++)
        {
            int childDefinitionId = children[i].DefinitionId;
            if (childDefinitionId <= 0 || !presenters.TryGet(childDefinitionId, out PresenterDefinition child))
            {
                throw new InvalidOperationException(
                    $"CrowdPhysicsArena observer visibility requires child presenter definition id {childDefinitionId}.");
            }

            mask = mask.Union(ResolveHudAttributeMask(presenters, child));
        }

        return mask;
    }

    private static bool HasHudAssetBinding(PresenterDefinition definition)
    {
        BehaviorSlot[] behaviors = definition.Behaviors;
        for (int i = 0; i < behaviors.Length; i++)
        {
            if ((behaviors[i].Kind == BehaviorKind.AssetBinding ||
                 behaviors[i].Kind == BehaviorKind.WorldText) &&
                behaviors[i].AssetBinding.AssetKind is AssetKind.WorldHud or AssetKind.WorldText)
            {
                return true;
            }
        }

        return false;
    }

    private static KnowledgeIdMask256 BuildMask(ReadOnlySpan<int> attributeIds)
    {
        KnowledgeIdMask256 mask = KnowledgeIdMask256.Empty;
        for (int i = 0; i < attributeIds.Length; i++)
        {
            mask = mask.WithId(attributeIds[i]);
        }

        return mask;
    }

    private int PublishAgentKnowledge(
        KnowledgeProjectionStore knowledge,
        Entity viewer,
        in KnowledgeDisclosureRecord record)
    {
        int published = 0;
        foreach (ref var chunk in World.Query(in AgentQuery))
        {
            ref Entity firstEntity = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity target = Unsafe.Add(ref firstEntity, index);
                knowledge.Upsert(viewer, target, in record);
                published++;
            }
        }

        return published;
    }
}
