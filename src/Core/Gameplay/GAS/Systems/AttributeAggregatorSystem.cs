using Arch.Core;
using Arch.Core.Extensions;
using Arch.Buffer;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Mathematics;
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
        private readonly GraphProgramRegistry _graphPrograms;
        private readonly IGraphRuntimeApi _graphApi;

        public AttributeAggregatorSystem(World world, GraphProgramRegistry graphPrograms = null, IGraphRuntimeApi graphApi = null) : base(world)
        {
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
        }

        public override unsafe void Update(in float dt)
        {
            var withDirtyJob = new AttributeAggregatorWithDirtyJob
            {
                World = World,
                GraphPrograms = _graphPrograms,
                GraphApi = _graphApi,
            };
            World.InlineEntityQuery<AttributeAggregatorWithDirtyJob, AttributeBuffer, ActiveEffectContainer, DirtyFlags>(in _withDirtyFlagsQuery, ref withDirtyJob);

            var withoutDirtyJob = new AttributeAggregatorWithoutDirtyJob
            {
                World = World,
                CommandBuffer = _commandBuffer,
                GraphPrograms = _graphPrograms,
                GraphApi = _graphApi,
            };
            World.InlineEntityQuery<AttributeAggregatorWithoutDirtyJob, AttributeBuffer, ActiveEffectContainer>(in _withoutDirtyFlagsQuery, ref withoutDirtyJob);

            _commandBuffer.Playback(World, dispose: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void ExecuteDerivedGraphs(
            World world, Entity entity,
            GraphProgramRegistry graphPrograms, IGraphRuntimeApi graphApi)
        {
            if (graphPrograms == null || graphApi == null) return;
            if (!world.Has<AttributeDerivedGraphBinding>(entity)) return;

            ref var binding = ref world.Get<AttributeDerivedGraphBinding>(entity);
            if (binding.Count <= 0) return;

            for (int g = 0; g < binding.Count; g++)
            {
                int programId = binding.GraphProgramIds[g];
                if (programId <= 0) continue;
                if (!graphPrograms.TryGetProgram(programId, out var program)) continue;

                NodeLibraries.GASGraph.GraphExecutor.Execute(
                    world,
                    caster: entity,        // E[0] = Self
                    explicitTarget: entity, // E[1] = Self (derived graphs operate on self)
                    targetPos: default,
                    program,
                    graphApi);
            }
        }

        struct AttributeAggregatorWithDirtyJob : IForEachWithEntity<AttributeBuffer, ActiveEffectContainer, DirtyFlags>
        {
            public World World;
            public GraphProgramRegistry GraphPrograms;
            public IGraphRuntimeApi GraphApi;

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

                // 3. Execute derived graphs (non-linear attribute formulas)
                ExecuteDerivedGraphs(World, entity, GraphPrograms, GraphApi);

                // 4. 标记脏属性（用于延迟触发器）
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
            public GraphProgramRegistry GraphPrograms;
            public IGraphRuntimeApi GraphApi;

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

                // Execute derived graphs (non-linear attribute formulas)
                ExecuteDerivedGraphs(World, entity, GraphPrograms, GraphApi);

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
