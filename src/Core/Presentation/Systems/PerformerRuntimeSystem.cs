using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Consumes PresentationCommands and manages performer lifecycle.
    ///
    /// Handles both one-shot performers (PlayOneShotPerformer → TransientMarker)
    /// and the new persistent performer commands (CreatePerformer / DestroyPerformer /
    /// DestroyPerformerScope / SetPerformerParam → PerformerInstanceBuffer).
    /// </summary>
    public sealed class PerformerRuntimeSystem : BaseSystem<World, float>
    {
        private readonly PrefabRegistry _prefabs;
        private readonly PresentationCommandBuffer _commands;
        private readonly PresentationVisualProxyEmitter _proxyEmitter;
        private readonly TransientMarkerBuffer _markers;
        private readonly PerformerInstanceBuffer _instances;
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly PerformerDefinitionRegistry _definitions;

        public PerformerRuntimeSystem(
            World world,
            PrefabRegistry prefabs,
            PresentationCommandBuffer commands,
            PrimitiveDrawBuffer draw,
            TransientMarkerBuffer markers,
            PerformerInstanceBuffer instances,
            PresentationStableIdAllocator stableIds,
            PerformerDefinitionRegistry definitions,
            PrimitiveDrawBuffer? snapshotBuffer = null,
            PresentationVisualProxyBuffer? proxyBuffer = null,
            SkinnedVisualBatchBuffer? skinnedBatchBuffer = null)
            : base(world)
        {
            _prefabs = prefabs ?? throw new ArgumentNullException(nameof(prefabs));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _proxyEmitter = new PresentationVisualProxyEmitter(draw ?? throw new ArgumentNullException(nameof(draw)), snapshotBuffer, proxyBuffer, skinnedBatchBuffer);
            _markers = markers ?? throw new ArgumentNullException(nameof(markers));
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        }

        public override void Update(in float dt)
        {
            _instances.ReleaseDeadEntityAnchors(World);

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
            _markers.TickAndEmit(_proxyEmitter, dt, World);
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
            if (!_definitions.TryGet(cmd.IdA, out var definition))
            {
                throw new InvalidOperationException($"Performer definition id={cmd.IdA} is not registered.");
            }

            if (ShouldSkipDuplicatePersistentScopedCreate(in cmd, definition))
            {
                return;
            }

            if (!_instances.TryAllocate(
                    cmd.IdA,
                    cmd.Source,
                    cmd.IdB,
                    cmd.AnchorKind,
                    cmd.Position,
                    _stableIds.Allocate(),
                    out int handle))
            {
                string performerKey = _definitions.GetName(cmd.IdA);
                string ownerText = cmd.Source == Entity.Null
                    ? "Entity.Null"
                    : $"Entity(Id={cmd.Source.Id},World={cmd.Source.WorldId},Ver={cmd.Source.Version})";
                throw new InvalidOperationException(
                    $"PerformerInstanceBuffer is full while creating performer '{performerKey}' (defId={cmd.IdA}, scopeId={cmd.IdB}, owner={ownerText}, active={_instances.ActiveCount}, capacity={_instances.Capacity}).");
            }

            _instances.InitializeAnimatorSlots(handle, definition);
        }

        private bool ShouldSkipDuplicatePersistentScopedCreate(in PresentationCommand cmd, PerformerDefinition definition)
        {
            if (definition.DefaultLifetime > 0f || cmd.IdB <= 0)
            {
                return false;
            }

            return _instances.HasActiveScopedInstance(
                cmd.IdA,
                cmd.Source,
                cmd.IdB,
                cmd.AnchorKind,
                cmd.Position);
        }
    }
}
