using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Core.Components;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Vision;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Knowledge
{
    /// <summary>
    /// Default local-observer disclosure for maps that declare no fog layers: without vision
    /// authoring there is nothing to hide, so every command-source-selectable entity is
    /// LiveVisible to the sole local seat. Fog-authored maps keep the vision pipeline's
    /// own presence semantics and never activate this disclosure.
    /// </summary>
    public static class VisionlessMapDisclosure
    {
        private static readonly QueryDescription SelectableQuery = new QueryDescription()
            .WithAll<WorldPositionCm, CommandSourceSelectableTag>();

        private static readonly QueryDescription EmitterQuery = new QueryDescription()
            .WithAll<Ludots.Core.Vision.VisionEmitterCm>();

        public static LocalObserverLiveDisclosureSystem Create(GameEngine engine, FogLayerRegistry fogLayers)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(fogLayers);
            return new LocalObserverLiveDisclosureSystem(engine, SelectableQuery, new Target(fogLayers));
        }

        private sealed class Target : ILocalObserverDisclosureTarget
        {
            private readonly FogLayerRegistry _fogLayers;

            public Target(FogLayerRegistry fogLayers)
            {
                _fogLayers = fogLayers;
            }

            public bool IsActivated(GameEngine engine)
            {
                // Exactly one local seat is required: the disclosure publishes to the sole
                // possessed rep, and zero-seat (headless) or multi-seat engines must never
                // reach RequireSolePossessedRep's throw path.
                return engine.CurrentMapSession != null &&
                    engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry) is Ludots.Core.Client.ClientLocalSeatRegistry seats &&
                    seats.Count == 1 &&
                    !HasVisionEmitter(engine.World);
            }

            public int ResolveExpectedTargetCount(GameEngine engine)
            {
                return 1;
            }

            public int ResolveObservedTargetCount(GameEngine engine)
            {
                return CountSelectable(engine) > 0 ? 1 : 0;
            }

            public KnowledgeIdMask256 ResolveAttributeMask(GameEngine engine)
            {
                if (engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry) is not PresenterDefinitionRegistry presenters)
                {
                    return KnowledgeIdMask256.Empty;
                }

                KnowledgeIdMask256 mask = KnowledgeIdMask256.Empty;
                IReadOnlyList<int> ids = presenters.RegisteredIds;
                for (int i = 0; i < ids.Count; i++)
                {
                    mask = mask.Union(ResolveHudAttributeMask(presenters, ids[i]));
                }

                return mask;
            }

            public LocalDisclosureChangeStamp ResolveChangeStamp(GameEngine engine)
            {
                return new LocalDisclosureChangeStamp(
                    CountSelectable(engine),
                    engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry) is PresenterDefinitionRegistry presenters
                        ? presenters.Version
                        : 0);
            }

            // Child links are authored mod data the registry does not cycle-check,
            // so the walk is iterative with a visited guard: a cyclic child chain
            // must not recurse without bound.
            private static KnowledgeIdMask256 ResolveHudAttributeMask(
                PresenterDefinitionRegistry presenters,
                int rootDefinitionId)
            {
                KnowledgeIdMask256 mask = KnowledgeIdMask256.Empty;
                var visited = new HashSet<int>();
                var pending = new Stack<int>();
                pending.Push(rootDefinitionId);
                while (pending.Count > 0)
                {
                    int definitionId = pending.Pop();
                    if (!visited.Add(definitionId) || !presenters.TryGet(definitionId, out PresenterDefinition definition))
                    {
                        continue;
                    }

                    if (HasHudAssetBinding(definition))
                    {
                        mask = mask.Union(BuildMask(definition.RequiredAttributeIds));
                    }

                    ChildPresenterRef[] children = definition.Children;
                    for (int i = 0; i < children.Length; i++)
                    {
                        pending.Push(children[i].DefinitionId);
                    }
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

            private static bool HasVisionEmitter(World world)
            {
                foreach (Chunk chunk in world.Query(in EmitterQuery))
                {
                    if (chunk.Count > 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static int CountSelectable(GameEngine engine)
            {
                int count = 0;
                foreach (Chunk chunk in engine.World.Query(in SelectableQuery))
                {
                    count += chunk.Count;
                }

                return count;
            }
        }
    }
}
