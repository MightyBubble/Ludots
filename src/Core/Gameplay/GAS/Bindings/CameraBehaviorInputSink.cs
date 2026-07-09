using System;
using Arch.Core;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Bindings
{
    public sealed class CameraBehaviorInputSink : IAttributeSink
    {
        private readonly CameraBehaviorInputState _state;
        private readonly QueryDescription _targetQuery =
            new QueryDescription().WithAll<AttributeBuffer, CameraBehaviorInputTarget>();

        public CameraBehaviorInputSink(CameraBehaviorInputState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void Apply(World world, AttributeBindingEntry[] entries, int start, int count)
        {
            _state.Clear();
            Entity target = ResolveTarget(world);

            ref AttributeBuffer attr = ref world.Get<AttributeBuffer>(target);
            for (int i = 0; i < count; i++)
            {
                AttributeBindingEntry binding = entries[start + i];
                CameraBehaviorInputChannels.Validate(binding.Channel, binding.AttributeId.ToString());

                float value = attr.GetCurrent(binding.AttributeId) * binding.Scale;
                _state.Apply(binding.Channel, value, binding.Mode);

                if (binding.ResetPolicy == AttributeBindingResetPolicy.ResetToZeroPerLogicFrame)
                {
                    attr.SetCurrent(binding.AttributeId, 0f);
                }
            }
        }

        private Entity ResolveTarget(World world)
        {
            Entity resolved = Entity.Null;
            int targetCount = 0;
            world.Query(
                in _targetQuery,
                (Entity entity, ref AttributeBuffer _, ref CameraBehaviorInputTarget __) =>
                {
                    targetCount++;
                    if (targetCount == 1)
                    {
                        resolved = entity;
                    }
                });

            if (targetCount == 1)
            {
                return resolved;
            }

            throw new InvalidOperationException(
                $"Camera behavior input sink requires exactly one entity with {nameof(CameraBehaviorInputTarget)} and {nameof(AttributeBuffer)}; found {targetCount}.");
        }
    }
}
