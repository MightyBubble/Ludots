using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Attributes;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Systems
{
    public sealed class InputActionAttributeBindingSystem : BaseSystem<World, float>
    {
        private readonly Dictionary<string, object> _globals;
        private readonly InputActionAttributeBindingRegistry _registry;
        private readonly QueryDescription _cameraBehaviorInputTargetQuery =
            new QueryDescription().WithAll<AttributeBuffer, CameraBehaviorInputTarget>();
        private Entity _cameraBehaviorInputCarrier = Entity.Null;

        public InputActionAttributeBindingSystem(
            World world,
            Dictionary<string, object> globals,
            InputActionAttributeBindingRegistry registry)
            : base(world)
        {
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public override void Update(in float dt)
        {
            InputActionAttributeBindingEntry[] entries = _registry.Entries;
            if (entries.Length == 0)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object? inputObj) ||
                inputObj is not IInputActionReader input)
            {
                throw new InvalidOperationException(
                    "InputActionAttributeBindingSystem requires CoreServiceKeys.AuthoritativeInput.");
            }

            bool uiCaptured = _globals.TryGetValue(CoreServiceKeys.UiCaptured.Name, out object? capturedObj) &&
                              capturedObj is bool captured &&
                              captured;

            for (int i = 0; i < entries.Length; i++)
            {
                InputActionAttributeBindingEntry entry = entries[i];
                if (!TryResolveTarget(entry.Target, out Entity target))
                {
                    continue;
                }

                if (!World.Has<AttributeBuffer>(target))
                {
                    throw new InvalidOperationException(
                        $"Input action '{entry.ActionId}' targets entity {target.Id}, but it has no AttributeBuffer.");
                }

                float value = uiCaptured && entry.ZeroWhenUiCaptured
                    ? 0f
                    : ReadValue(input, entry);

                ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(target);
                attributes.SetCurrent(entry.AttributeId, value);
            }
        }

        private bool TryResolveTarget(InputActionAttributeTargetKind target, out Entity entity)
        {
            entity = Entity.Null;
            switch (target)
            {
                case InputActionAttributeTargetKind.LocalPlayerEntity:
                    if (_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? entityObj) &&
                        entityObj is Entity local &&
                        local != Entity.Null &&
                        World.IsAlive(local))
                    {
                        entity = local;
                        return true;
                    }

                    return false;

                case InputActionAttributeTargetKind.CameraBehaviorInput:
                    entity = ResolveCameraBehaviorInputCarrier();
                    return true;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported input action attribute target '{target}'.");
            }
        }

        private Entity ResolveCameraBehaviorInputCarrier()
        {
            if (_cameraBehaviorInputCarrier != Entity.Null &&
                World.IsAlive(_cameraBehaviorInputCarrier) &&
                World.Has<AttributeBuffer>(_cameraBehaviorInputCarrier) &&
                World.Has<CameraBehaviorInputTarget>(_cameraBehaviorInputCarrier))
            {
                return _cameraBehaviorInputCarrier;
            }

            var job = new ResolveCameraBehaviorInputCarrierJob();
            World.InlineEntityQuery<ResolveCameraBehaviorInputCarrierJob, AttributeBuffer, CameraBehaviorInputTarget>(
                in _cameraBehaviorInputTargetQuery,
                ref job);

            if (job.Count == 1)
            {
                _cameraBehaviorInputCarrier = job.Resolved;
                return job.Resolved;
            }

            throw new InvalidOperationException(
                $"InputActionAttributeBindingSystem requires exactly one entity with {nameof(CameraBehaviorInputTarget)} and {nameof(AttributeBuffer)}; found {job.Count}.");
        }

        private struct ResolveCameraBehaviorInputCarrierJob : IForEachWithEntity<AttributeBuffer, CameraBehaviorInputTarget>
        {
            public Entity Resolved;
            public int Count;

            public void Update(Entity entity, ref AttributeBuffer attributes, ref CameraBehaviorInputTarget target)
            {
                Count++;
                if (Count == 1)
                {
                    Resolved = entity;
                }
            }
        }

        private static float ReadValue(IInputActionReader input, in InputActionAttributeBindingEntry entry)
        {
            float value = entry.ValueKind switch
            {
                InputActionAttributeValueKind.Axis1D => input.ReadAction<float>(entry.ActionId),
                InputActionAttributeValueKind.Axis2D => ReadAxis2D(input, entry.ActionId, entry.SourceChannel),
                InputActionAttributeValueKind.Button => input.IsDown(entry.ActionId) ? 1f : 0f,
                InputActionAttributeValueKind.Constant => 1f,
                _ => throw new InvalidOperationException(
                    $"Unsupported input action attribute value kind '{entry.ValueKind}'.")
            };

            value *= entry.Scale;
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException(
                    $"Input action '{entry.ActionId}' produced a non-finite attribute value.");
            }

            return value;
        }

        private static float ReadAxis2D(IInputActionReader input, string actionId, byte sourceChannel)
        {
            Vector2 value = input.ReadAction<Vector2>(actionId);
            return sourceChannel == 0 ? value.X : value.Y;
        }
    }
}
