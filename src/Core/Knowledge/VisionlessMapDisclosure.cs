using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Components;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Vision;

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
                var emit = HasVisionEmitter(engine.World);
                System.Console.WriteLine($"[HOVERDBG] Visionless session={engine.CurrentMapSession != null} hasEmitter={emit}");
                return engine.CurrentMapSession != null && !emit;
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
                return KnowledgeIdMask256.Empty;
            }

            public LocalDisclosureChangeStamp ResolveChangeStamp(GameEngine engine)
            {
                return new LocalDisclosureChangeStamp(CountSelectable(engine), 0);
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
