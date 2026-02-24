using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Consumes PresentationCommands and manages performer lifecycle.
    ///
    /// Handles both legacy one-shot performers (PlayOneShotPerformer → TransientMarker)
    /// and the new persistent performer commands (CreatePerformer / DestroyPerformer /
    /// DestroyPerformerScope / SetPerformerParam → PerformerInstanceBuffer).
    /// </summary>
    public sealed class PerformerRuntimeSystem : BaseSystem<World, float>
    {
        private readonly PrefabRegistry _prefabs;
        private readonly PresentationCommandBuffer _commands;
        private readonly PrimitiveDrawBuffer _draw;
        private readonly TransientMarkerBuffer _markers;
        private readonly PerformerInstanceBuffer _instances;

        public PerformerRuntimeSystem(
            World world,
            PrefabRegistry prefabs,
            PresentationCommandBuffer commands,
            PrimitiveDrawBuffer draw,
            TransientMarkerBuffer markers,
            PerformerInstanceBuffer instances)
            : base(world)
        {
            _prefabs = prefabs ?? throw new ArgumentNullException(nameof(prefabs));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _draw = draw ?? throw new ArgumentNullException(nameof(draw));
            _markers = markers ?? throw new ArgumentNullException(nameof(markers));
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        }

        public override void Update(in float dt)
        {
            // 1. Process all commands
            var cmdSpan = _commands.GetSpan();
            for (int i = 0; i < cmdSpan.Length; i++)
            {
                ref readonly var cmd = ref cmdSpan[i];
                switch (cmd.Kind)
                {
                    case PresentationCommandKind.PlayOneShotPerformer:
                        HandlePlayOneShot(in cmd);
                        break;

                    case PresentationCommandKind.CreatePerformer:
                        HandleCreatePerformer(in cmd);
                        break;

                    case PresentationCommandKind.DestroyPerformer:
                        _instances.Release(cmd.IdA);
                        break;

                    case PresentationCommandKind.DestroyPerformerScope:
                        _instances.ReleaseScope(cmd.IdA);
                        break;

                    case PresentationCommandKind.SetPerformerParam:
                        if (_instances.IsActive(cmd.IdA))
                        {
                            _instances.SetParamOverride(cmd.IdA, cmd.IdB, cmd.Param1);
                        }
                        break;
                }
            }
            _commands.Clear();

            // 2. Tick transient markers and emit to PrimitiveDrawBuffer
            _markers.TickAndEmit(_draw, dt, World);
        }

        private void HandlePlayOneShot(in PresentationCommand cmd)
        {
            if (!_prefabs.TryGet(cmd.IdA, out var prefab)) return;

            var color = cmd.Param0.W == 0 ? new Vector4(0f, 1f, 1f, 1f) : cmd.Param0;
            float lifetime = cmd.Param1 > 0f ? cmd.Param1 : 0.35f;
            var scale = new Vector3(prefab.BaseScale);

            bool follow = World.IsAlive(cmd.Target) && World.Has<VisualTransform>(cmd.Target);
            if (follow)
            {
                _markers.TryAddAnchored(prefab.MeshAssetId, scale, color, lifetime, cmd.Target, new Vector3(0f, 0.2f, 0f));
            }
            else
            {
                _markers.TryAdd(prefab.MeshAssetId, cmd.Position, scale, color, lifetime);
            }
        }

        private void HandleCreatePerformer(in PresentationCommand cmd)
        {
            // IdA = PerformerDefinitionId, IdB = ScopeId, Source = Owner
            _instances.TryAllocate(cmd.IdA, cmd.Source, cmd.IdB, out _);
        }
    }
}
