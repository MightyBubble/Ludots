using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
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
        private readonly PresentationEventStream _events;
        private readonly TransientMarkerBuffer _markers;
        private readonly PresentationRequestBuffer _requests;
        private readonly PerformerInstanceBuffer _instances;
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly PerformerDefinitionRegistry _definitions;

        public PerformerRuntimeSystem(
            World world,
            PrefabRegistry prefabs,
            PresentationCommandBuffer commands,
            PresentationEventStream events,
            TransientMarkerBuffer markers,
            PresentationRequestBuffer requests,
            PerformerInstanceBuffer instances,
            PresentationStableIdAllocator stableIds,
            PerformerDefinitionRegistry definitions)
            : base(world)
        {
            _prefabs = prefabs ?? throw new ArgumentNullException(nameof(prefabs));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _markers = markers ?? throw new ArgumentNullException(nameof(markers));
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        }

        public override void Update(in float dt)
        {
            _instances.ReleaseDeadEntityAnchors(World, EmitDestroyedEvent);

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
                        if (_instances.TryGetActive(cmd.IdA, out var instance) && _instances.Release(cmd.IdA))
                        {
                            EmitDestroyedEvent(cmd.IdA, instance);
                        }
                        break;

                    case PresentationCommandKind.DestroyPerformerScope:
                        _instances.ReleaseScope(cmd.IdB != 0 ? cmd.IdB : cmd.IdA, EmitDestroyedEvent);
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

            _markers.TickAndRequest(_requests, dt, World);
        }

        private void HandlePlayOneShot(in PresentationCommand cmd)
        {
            if (!_prefabs.TryGet(cmd.IdA, out var prefab))
            {
                throw new InvalidOperationException($"PlayOneShotPerformer references unknown prefab id={cmd.IdA}.");
            }

            var color = cmd.Param0.W == 0 ? new Vector4(0f, 1f, 1f, 1f) : cmd.Param0;
            float lifetime = cmd.Param1 > 0f ? cmd.Param1 : 0.35f;
            var scale = new Vector3(prefab.BaseScale);

            bool follow = World.IsAlive(cmd.Target) && World.Has<VisualTransform>(cmd.Target);
            if (follow)
            {
                if (!_markers.TryAddAnchoredPrefab(cmd.IdA, scale, color, lifetime, cmd.Target, new Vector3(0f, 0.2f, 0f)))
                {
                    throw new InvalidOperationException("TransientMarkerBuffer is full while creating anchored one-shot prefab instance.");
                }
            }
            else
            {
                if (!_markers.TryAddPrefab(cmd.IdA, cmd.Position, scale, color, lifetime))
                {
                    throw new InvalidOperationException("TransientMarkerBuffer is full while creating one-shot prefab instance.");
                }
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

            EmitCreatedEvent(handle, _instances.Get(handle));
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

        private void EmitCreatedEvent(int handle, in PerformerInstance instance)
        {
            if (!_events.TryAdd(new PresentationEvent
                {
                    Kind = PresentationEventKind.PerformerCreated,
                    KeyId = instance.DefId,
                    Source = instance.Owner,
                    Target = instance.Owner,
                    PayloadA = handle,
                    PayloadB = instance.ScopeId,
                    Magnitude = instance.StableId,
                }))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing PerformerCreated.");
            }
        }

        private void EmitDestroyedEvent(int handle, PerformerInstance instance)
        {
            if (!_events.TryAdd(new PresentationEvent
                {
                    Kind = PresentationEventKind.PerformerDestroyed,
                    KeyId = instance.DefId,
                    Source = instance.Owner,
                    Target = instance.Owner,
                    PayloadA = handle,
                    PayloadB = instance.ScopeId,
                    Magnitude = instance.StableId,
                }))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing PerformerDestroyed.");
            }
        }
    }
}
