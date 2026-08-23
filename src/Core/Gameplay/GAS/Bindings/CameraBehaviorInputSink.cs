using System;
using Arch.Core;
using Arch.System;
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

        public void ValidateBinding(byte channel, string bindingId, string relativePath)
        {
            CameraBehaviorInputChannels.Validate(channel, bindingId, relativePath);
        }

        public void Apply(World world, AttributeBindingEntry[] entries, int start, int count)
        {
            _state.Clear();
            Entity target = ResolveTarget(world);

            ref AttributeBuffer attr = ref world.Get<AttributeBuffer>(target);
            for (int i = 0; i < count; i++)
            {
                AttributeBindingEntry binding = entries[start + i];
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
            var job = new ResolveTargetJob();
            world.InlineEntityQuery<ResolveTargetJob, AttributeBuffer, CameraBehaviorInputTarget>(
                in _targetQuery,
                ref job);

            if (job.TargetCount == 1)
            {
                return job.Resolved;
            }

            throw new InvalidOperationException(
                $"Camera behavior input sink requires exactly one entity with {nameof(CameraBehaviorInputTarget)} and {nameof(AttributeBuffer)}; found {job.TargetCount}.");
        }

        private struct ResolveTargetJob : IForEachWithEntity<AttributeBuffer, CameraBehaviorInputTarget>
        {
            public Entity Resolved;
            public int TargetCount;

            public void Update(Entity entity, ref AttributeBuffer _, ref CameraBehaviorInputTarget __)
            {
                TargetCount++;
                if (TargetCount == 1)
                {
                    Resolved = entity;
                }
            }
        }
    }
}
