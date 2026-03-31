using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Bindings
{
    public sealed class GameplayControlStateSink : IAttributeSink
    {
        public const byte MoveBlockCountChannel = 0;
        public const byte ActionBlockCountChannel = 1;

        private readonly QueryDescription _query = new QueryDescription()
            .WithAll<AttributeBuffer, GameplayControlState>();

        public void Apply(World world, AttributeBindingEntry[] entries, int start, int count)
        {
            var job = new ApplyJob
            {
                Entries = entries,
                Start = start,
                Count = count,
            };
            world.InlineEntityQuery<ApplyJob, AttributeBuffer, GameplayControlState>(in _query, ref job);
        }

        private struct ApplyJob : IForEachWithEntity<AttributeBuffer, GameplayControlState>
        {
            public AttributeBindingEntry[] Entries;
            public int Start;
            public int Count;

            public void Update(Entity entity, ref AttributeBuffer attributes, ref GameplayControlState controlState)
            {
                float moveBlockCount = 0f;
                float actionBlockCount = 0f;

                for (int i = 0; i < Count; i++)
                {
                    ref readonly var entry = ref Entries[Start + i];
                    float value = attributes.GetCurrent(entry.AttributeId) * entry.Scale;
                    switch (entry.Channel)
                    {
                        case MoveBlockCountChannel:
                            ApplyValue(ref moveBlockCount, value, entry.Mode);
                            break;
                        case ActionBlockCountChannel:
                            ApplyValue(ref actionBlockCount, value, entry.Mode);
                            break;
                    }
                }

                controlState.ActionBlocked = actionBlockCount > 0f ? (byte)1 : (byte)0;
                controlState.MoveBlocked = moveBlockCount > 0f ? (byte)1 : (byte)0;
            }

            private static void ApplyValue(ref float channelValue, float value, AttributeBindingMode mode)
            {
                if (mode == AttributeBindingMode.Override)
                {
                    channelValue = value;
                    return;
                }

                channelValue += value;
            }
        }
    }
}
