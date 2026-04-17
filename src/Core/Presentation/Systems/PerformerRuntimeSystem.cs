using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Consumes performer commands and manages performer lifecycle.
    ///
    /// Handles persistent performer commands.
    /// </summary>
    public sealed class PerformerRuntimeSystem : BaseSystem<World, float>
    {
        private readonly PrefabRegistry _prefabs;
        private readonly PerformerCommandBuffer _commands;
        private readonly PresentationEventStream _events;
        private readonly TransientMarkerBuffer _markers;
        private readonly PresentationRequestBuffer _requests;
        private readonly PerformerInstanceBuffer _instances;
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly PerformerDefinitionRegistry _definitions;

        public PerformerRuntimeSystem(
            World world,
            PrefabRegistry prefabs,
            PerformerCommandBuffer commands,
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
                switch (cmd.CommandKind)
                {
                    case PerformerCommandKind.CreatePerformer:
                        HandleCreatePerformer(in cmd);
                        break;

                    case PerformerCommandKind.DestroyPerformer:
                        if (_instances.TryGetActive(cmd.PerformerHandle, out var instance) && _instances.Release(cmd.PerformerHandle))
                        {
                            EmitDestroyedEvent(cmd.PerformerHandle, instance);
                        }
                        break;

                    case PerformerCommandKind.DestroyPerformerScope:
                        _instances.ReleaseScope(cmd.ScopeTag != 0 ? cmd.ScopeTag : cmd.PerformerDefinitionId, EmitDestroyedEvent);
                        break;

                    case PerformerCommandKind.SetParam:
                        if (_instances.IsActive(cmd.PerformerHandle))
                        {
                            _instances.SetParamOverride(cmd.PerformerHandle, cmd.ParamKey, cmd.ParamValue);
                        }
                        break;
                }
            }
            _commands.Clear();

            _markers.TickAndRequest(_requests, dt, World);
        }

        private void HandleCreatePerformer(in PerformerCommand cmd)
        {
            if (!_definitions.TryGet(cmd.PerformerDefinitionId, out var definition))
            {
                throw new InvalidOperationException($"Performer definition id={cmd.PerformerDefinitionId} is not registered.");
            }

            if (ShouldSkipDuplicatePersistentScopedCreate(in cmd, definition))
            {
                return;
            }

            if (!_instances.TryAllocate(
                    cmd.PerformerDefinitionId,
                    cmd.Source,
                    cmd.ScopeTag,
                    cmd.AnchorKind,
                    cmd.Position,
                    _stableIds.Allocate(),
                    out int handle))
            {
                string performerKey = _definitions.GetName(cmd.PerformerDefinitionId);
                string ownerText = cmd.Source == Entity.Null
                    ? "Entity.Null"
                    : $"Entity(Id={cmd.Source.Id},World={cmd.Source.WorldId},Ver={cmd.Source.Version})";
                throw new InvalidOperationException(
                    $"PerformerInstanceBuffer is full while creating performer '{performerKey}' (defId={cmd.PerformerDefinitionId}, scopeTag={cmd.ScopeTag}, owner={ownerText}, active={_instances.ActiveCount}, capacity={_instances.Capacity}).");
            }

            EmitCreatedEvent(handle, _instances.Get(handle));
        }

        private bool ShouldSkipDuplicatePersistentScopedCreate(in PerformerCommand cmd, PerformerDefinition definition)
        {
            if (definition.DefaultLifetime > 0f || cmd.ScopeTag <= 0)
            {
                return false;
            }

            return _instances.HasActiveScopedInstance(
                cmd.PerformerDefinitionId,
                cmd.Source,
                cmd.ScopeTag,
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
