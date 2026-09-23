using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Knowledge;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace MassNavigationPresentationAdapter;

public static class MassNavigationObserverDisclosure
{
    private static readonly QueryDescription AgentQuery = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex>()
        .WithNone<PresentationDestroyPending>();

    public static LocalObserverLiveDisclosureSystem CreateLocalAgentDisclosure(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new LocalObserverLiveDisclosureSystem(engine, AgentQuery, new StartupMapAgentTarget());
    }

    private sealed class StartupMapAgentTarget : ILocalObserverDisclosureTarget
    {
        public bool IsActivated(GameEngine engine)
        {
            return IsStartupMapFocused(engine) &&
                MassNavigationIds.IsCurrentNavigationRuntimeReady(engine) &&
                engine.GetService(MassNavigationKeys.RuntimeBinding) is { IsReady: true, Current: { } } &&
                engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry) is PresenterDefinitionRegistry;
        }

        public int ResolveExpectedTargetCount(GameEngine engine)
        {
            return TryResolveSimulation(engine, out var simulation)
                ? checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam)
                : 0;
        }

        public int ResolveObservedTargetCount(GameEngine engine)
        {
            return TryResolveSimulation(engine, out var simulation)
                ? simulation.NavigationAgentCount
                : 0;
        }

        public KnowledgeIdMask256 ResolveAttributeMask(GameEngine engine)
        {
            var presenters = RequirePresenterRegistry(engine);
            if (!TryResolveSimulation(engine, out var simulation))
            {
                return KnowledgeIdMask256.Empty;
            }

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

        public LocalDisclosureChangeStamp ResolveChangeStamp(GameEngine engine)
        {
            if (!TryResolveSimulation(engine, out var simulation))
            {
                return default;
            }

            var presenters = RequirePresenterRegistry(engine);
            return new LocalDisclosureChangeStamp(simulation.StructuralChangeRevision, presenters.Version);
        }

        private static bool IsStartupMapFocused(GameEngine engine)
        {
            string? startupMapId = engine.MergedConfig?.StartupMapId;
            return !string.IsNullOrWhiteSpace(startupMapId) &&
                   string.Equals(
                       engine.CurrentMapSession?.MapId.Value,
                       startupMapId,
                       StringComparison.Ordinal);
        }

        private static bool TryResolveSimulation(GameEngine engine, out MassNavigationSimulationRuntime simulation)
        {
            return MassNavigationIds.TryGetCurrentNavigationRuntime(engine, out simulation);
        }

        private static PresenterDefinitionRegistry RequirePresenterRegistry(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry) as PresenterDefinitionRegistry
                ?? throw new InvalidOperationException(
                    "MassNavigation local observer disclosure requires PresenterDefinitionRegistry.");
        }

        private static KnowledgeIdMask256 ResolveHudAttributeMask(
            PresenterDefinitionRegistry presenters,
            string presenterKey)
        {
            int definitionId = presenters.GetId(presenterKey);
            if (definitionId <= 0 || !presenters.TryGet(definitionId, out PresenterDefinition definition))
            {
                throw new InvalidOperationException(
                    $"MassNavigation local observer disclosure requires presenter definition '{presenterKey}'.");
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
                        $"MassNavigation local observer disclosure requires child presenter definition id {childDefinitionId}.");
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
    }
}
