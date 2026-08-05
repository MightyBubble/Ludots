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
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;

namespace CapabilityStandardMassNavigationLargeWorld10kMod.Systems;

internal sealed class MassNavigationObserverVisibilityBindingSystem : BaseSystem<World, float>
{
    private const int LiveKnowledgeConfidencePermille = 1000;

    private static readonly QueryDescription AgentQuery = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex>()
        .WithNone<PresentationDestroyPending>();

    private readonly GameEngine _engine;
    private Entity _publishedViewer = Entity.Null;
    private int _publishedStructuralRevision = -1;
    private int _publishedPerformerDefinitionVersion = -1;
    private KnowledgeIdMask256 _publishedAttributeMask;

    public MassNavigationObserverVisibilityBindingSystem(GameEngine engine)
        : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
    {
        _engine = engine;
    }

    public override void Update(in float dt)
    {
        if (!CapabilityStandardMassNavigationLargeWorld10kMapFocus.IsStartupMapFocused(_engine) ||
            !MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine) ||
            _engine.GetService(MassNavigationKeys.RuntimeBinding) is not { IsReady: true, Current: { } simulation } ||
            _engine.GetService(CoreServiceKeys.KnowledgeProjectionStore) is not KnowledgeProjectionStore knowledge ||
            _engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry) is not PerformerDefinitionRegistry performers ||
            !TryResolveViewer(out Entity viewer))
        {
            return;
        }

        int expectedAgentCount = ResolveExpectedAgentCount(simulation);
        if (expectedAgentCount <= 0 || simulation.NavigationAgentCount < expectedAgentCount)
        {
            return;
        }

        KnowledgeIdMask256 attributeMask = ResolveHudAttributeMask(simulation, performers);
        if (_publishedViewer == viewer &&
            _publishedStructuralRevision == simulation.StructuralChangeRevision &&
            _publishedPerformerDefinitionVersion == performers.Version &&
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
        _publishedPerformerDefinitionVersion = performers.Version;
        _publishedAttributeMask = attributeMask;
    }

    private bool TryResolveViewer(out Entity viewer)
    {
        Entity candidate = _engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        viewer = candidate;
        return candidate != Entity.Null &&
               World.IsAlive(candidate) &&
               viewer != Entity.Null;
    }

    private static int ResolveExpectedAgentCount(MassNavigationSimulationRuntime simulation)
    {
        return checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
    }

    private static KnowledgeIdMask256 ResolveHudAttributeMask(
        MassNavigationSimulationRuntime simulation,
        PerformerDefinitionRegistry performers)
    {
        KnowledgeIdMask256 mask = KnowledgeIdMask256.Empty;
        ReadOnlySpan<int> teamIds = simulation.TeamIds;
        for (int i = 0; i < teamIds.Length; i++)
        {
            int teamId = teamIds[i];
            mask = mask.Union(ResolveHudAttributeMask(
                performers,
                simulation.Config.Presentation.ResolveAgentPerformerId(teamId, heavy: false)));
            mask = mask.Union(ResolveHudAttributeMask(
                performers,
                simulation.Config.Presentation.ResolveAgentPerformerId(teamId, heavy: true)));
        }

        return mask;
    }

    private static KnowledgeIdMask256 ResolveHudAttributeMask(
        PerformerDefinitionRegistry performers,
        string performerKey)
    {
        int definitionId = performers.GetId(performerKey);
        if (definitionId <= 0 || !performers.TryGet(definitionId, out PerformerDefinition definition))
        {
            throw new InvalidOperationException(
                $"MassNavigation observer visibility requires performer definition '{performerKey}'.");
        }

        return ResolveHudAttributeMask(performers, definition);
    }

    private static KnowledgeIdMask256 ResolveHudAttributeMask(
        PerformerDefinitionRegistry performers,
        PerformerDefinition definition)
    {
        KnowledgeIdMask256 mask = HasHudAssetBinding(definition)
            ? BuildMask(definition.RequiredAttributeIds)
            : KnowledgeIdMask256.Empty;

        ChildPerformerRef[] children = definition.Children;
        for (int i = 0; i < children.Length; i++)
        {
            int childDefinitionId = children[i].DefinitionId;
            if (childDefinitionId <= 0 || !performers.TryGet(childDefinitionId, out PerformerDefinition child))
            {
                throw new InvalidOperationException(
                    $"MassNavigation observer visibility requires child performer definition id {childDefinitionId}.");
            }

            mask = mask.Union(ResolveHudAttributeMask(performers, child));
        }

        return mask;
    }

    private static bool HasHudAssetBinding(PerformerDefinition definition)
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
