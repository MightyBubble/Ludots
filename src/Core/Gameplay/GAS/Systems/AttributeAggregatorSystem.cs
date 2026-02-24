using Arch.Core;
using Arch.Core.Extensions;
using Arch.Buffer;
using Ludots.Core.Gameplay.GAS.Components;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class AttributeAggregatorSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _withDirtyFlagsQuery = new QueryDescription()
            .WithAll<AttributeBuffer, ActiveEffectContainer, DirtyFlags>();
        
        private static readonly QueryDescription _withoutDirtyFlagsQuery = new QueryDescription()
            .WithAll<AttributeBuffer, ActiveEffectContainer>()
            .WithNone<DirtyFlags>();

        private readonly CommandBuffer _commandBuffer = new CommandBuffer();

        public AttributeAggregatorSystem(World world) : base(world) { }

        public override unsafe void Update(in float dt)
        {
            var withDirtyJob = new AttributeAggregatorWithDirtyJob { World = World };
            World.InlineEntityQuery<AttributeAggregatorWithDirtyJob, AttributeBuffer, ActiveEffectContainer, DirtyFlags>(in _withDirtyFlagsQuery, ref withDirtyJob);

            var withoutDirtyJob = new AttributeAggregatorWithoutDirtyJob
            {
                World = World,
                CommandBuffer = _commandBuffer
            };
            World.InlineEntityQuery<AttributeAggregatorWithoutDirtyJob, AttributeBuffer, ActiveEffectContainer>(in _withoutDirtyFlagsQuery, ref withoutDirtyJob);
            
            _commandBuffer.Playback(World, dispose: true);
        }

        struct AttributeAggregatorWithDirtyJob : IForEachWithEntity<AttributeBuffer, ActiveEffectContainer, DirtyFlags>
        {
            public World World;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void Update(Entity entity, ref AttributeBuffer attrBuffer, ref ActiveEffectContainer effects, ref DirtyFlags dirtyFlags)
            {
                Span<float> oldValues = stackalloc float[AttributeBuffer.MAX_ATTRS];
                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    oldValues[i] = attrBuffer.CurrentValues[i];
                }
                
                // 1. Reset Current = Base
                for(int i=0; i<AttributeBuffer.MAX_ATTRS; i++)
                {
                    attrBuffer.CurrentValues[i] = attrBuffer.BaseValues[i];
                }

                // 2. Aggregate Active Effects
                if (effects.Count > 0)
                {
                for (int i = 0; i < effects.Count; i++)
                {
                    Entity effectEntity = effects.GetEntity(i);
                    
                    if (World.IsAlive(effectEntity))
                    {
                        ref var modifiers = ref World.Get<EffectModifiers>(effectEntity);
                        EffectModifierOps.Apply(in modifiers, ref attrBuffer);
                    }
                    }
                }
                
                // 3. 标记脏属性（用于延迟触发器）
                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    if (oldValues[i] != attrBuffer.CurrentValues[i])
                    {
                        dirtyFlags.MarkAttributeDirty(i);
                    }
                }
            }

        }
        
        struct AttributeAggregatorWithoutDirtyJob : IForEachWithEntity<AttributeBuffer, ActiveEffectContainer>
        {
            public World World;
            public CommandBuffer CommandBuffer;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void Update(Entity entity, ref AttributeBuffer attrBuffer, ref ActiveEffectContainer effects)
            {
                Span<float> oldValues = stackalloc float[AttributeBuffer.MAX_ATTRS];
                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    oldValues[i] = attrBuffer.CurrentValues[i];
                }

                for(int i=0; i<AttributeBuffer.MAX_ATTRS; i++)
                {
                    attrBuffer.CurrentValues[i] = attrBuffer.BaseValues[i];
                }

                if (effects.Count > 0)
                {
                    for (int i = 0; i < effects.Count; i++)
                    {
                        Entity effectEntity = effects.GetEntity(i);
                        if (!World.IsAlive(effectEntity)) continue;

                        ref var modifiers = ref World.Get<EffectModifiers>(effectEntity);
                        EffectModifierOps.Apply(in modifiers, ref attrBuffer);
                    }
                }

                var dirtyFlags = new DirtyFlags();
                bool anyDirty = false;
                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    if (oldValues[i] != attrBuffer.CurrentValues[i])
                    {
                        dirtyFlags.MarkAttributeDirty(i);
                        anyDirty = true;
                    }
                }
                
                if (anyDirty)
                {
                    CommandBuffer.Add(entity, dirtyFlags);
                }
            }

        }
    }
}
