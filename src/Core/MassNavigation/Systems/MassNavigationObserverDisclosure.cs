using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Knowledge;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.MassNavigation.Systems;

/// <summary>
/// Discloses every spawned mass-navigation agent (MassNavigationAgent + index, no pending
/// presentation destroy) as LiveVisible to the local observer of the startup map, with the HUD
/// attribute mask derived from the scenario's agent presenter definitions.
/// </summary>
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
                ? simulation.NavigationAgentCount
                : 0;
        }

        public int ResolveObservedTargetCount(GameEngine engine)
        {
            return TryResolveSimulation(engine, out var simulation)
                ? simulation.NavigationAgentCount
                : 0;
        }

        /// <summary>
        /// HUD 属性掩码从携带 MassNavigationAgent 的模板推导：对每个模板经
        /// presenter bootstrap 规则找到其 presenter 定义，取 HUD 资产绑定的属性集。
        /// </summary>
        public KnowledgeIdMask256 ResolveAttributeMask(GameEngine engine)
        {
            var presenters = RequirePresenterRegistry(engine);
            EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
                as EntityTemplateKeyRegistry
                ?? throw new InvalidOperationException(
                    "MassNavigation local observer disclosure requires EntityTemplateKeyRegistry.");
            KnowledgeIdMask256 mask = KnowledgeIdMask256.Empty;
            foreach (EntityTemplate template in engine.MapLoader.TemplateRegistry.GetAll())
            {
                if (template?.Components == null ||
                    !template.Components.ContainsKey("MassNavigationAgent") ||
                    !templateKeys.TryGetId(template.Id, out int templateKeyId))
                {
                    continue;
                }

                if (!presenters.BootstrapRegistry.TryGetEntitySpawnCreates(templateKeyId, out CompiledPresenterBootstrapRegistry.BootstrapCreateRule[] rules))
                {
                    continue;
                }

                for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
                {
                    mask = mask.Union(ResolveHudAttributeMask(presenters, rules[ruleIndex].PresenterDefinitionId));
                }
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

        private static bool TryResolveSimulation(GameEngine engine, out Runtime.MassNavigationSimulationRuntime simulation)
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
            int definitionId)
        {
            if (definitionId <= 0 || !presenters.TryGet(definitionId, out PresenterDefinition definition))
            {
                throw new InvalidOperationException(
                    $"MassNavigation local observer disclosure requires presenter definition id {definitionId}.");
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
