using Arch.Core;
using Arch.Core.Extensions;
using Arch.Buffer;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Mathematics;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class AttributeAggregatorSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _withDirtyFlagsQuery = new QueryDescription()
            .WithAll<AttributeBuffer, ActiveEffectContainer, AttributeAggregateDirty, DirtyFlags>();

        private static readonly QueryDescription _withoutDirtyFlagsQuery = new QueryDescription()
            .WithAll<AttributeBuffer, ActiveEffectContainer, AttributeAggregateDirty>()
            .WithNone<DirtyFlags>();

        private readonly GraphProgramRegistry _graphPrograms;
        private readonly IGraphRuntimeApi _graphApi;

        public AttributeAggregatorSystem(World world, GraphProgramRegistry graphPrograms = null, IGraphRuntimeApi graphApi = null) : base(world)
        {
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
        }

        public override unsafe void Update(in float dt)
        {
            var commandBuffer = new CommandBuffer();
            var withDirtyJob = new AttributeAggregatorWithDirtyJob
            {
                World = World,
                CommandBuffer = commandBuffer,
                GraphPrograms = _graphPrograms,
                GraphApi = _graphApi,
            };
            World.InlineEntityQuery<AttributeAggregatorWithDirtyJob, AttributeBuffer, ActiveEffectContainer, DirtyFlags>(in _withDirtyFlagsQuery, ref withDirtyJob);

            var withoutDirtyJob = new AttributeAggregatorWithoutDirtyJob
            {
                World = World,
                CommandBuffer = commandBuffer,
                GraphPrograms = _graphPrograms,
                GraphApi = _graphApi,
            };
            World.InlineEntityQuery<AttributeAggregatorWithoutDirtyJob, AttributeBuffer, ActiveEffectContainer>(in _withoutDirtyFlagsQuery, ref withoutDirtyJob);

            commandBuffer.Playback(World, dispose: true);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ulong RecomputeEffectiveValues(
            World world,
            Entity entity,
            ref AttributeBuffer attrBuffer,
            ref ActiveEffectContainer effects,
            GraphProgramRegistry graphPrograms,
            IGraphRuntimeApi graphApi)
        {
            ulong touchedMask = 0UL;

            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                attrBuffer.CurrentValues[i] = attrBuffer.BaseValues[i];
            }

            if (effects.Count > 0)
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    Entity effectEntity = effects.GetEntity(i);
                    if (!world.IsAlive(effectEntity))
                    {
                        continue;
                    }

                    if (world.Has<GameplayEffect>(effectEntity) &&
                        !world.Get<GameplayEffect>(effectEntity).AggregatesModifiers)
                    {
                        continue;
                    }

                    ref readonly var modifiers = ref world.Get<EffectModifiers>(effectEntity);
                    touchedMask |= BuildTouchedMask(in modifiers);
                    EffectModifierOps.ApplyAggregated(in modifiers, ref attrBuffer);
                }
            }

            Span<float> beforeDerived = stackalloc float[AttributeBuffer.MAX_ATTRS];
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                beforeDerived[i] = attrBuffer.CurrentValues[i];
            }

            ExecuteDerivedGraphs(world, entity, graphPrograms, graphApi);

            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                if (beforeDerived[i] != attrBuffer.CurrentValues[i])
                {
                    touchedMask |= 1UL << i;
                }
            }

            return touchedMask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ulong BuildTouchedMask(in EffectModifiers modifiers)
        {
            ulong mask = 0UL;
            for (int i = 0; i < modifiers.Count; i++)
            {
                int attributeId = modifiers.Get(i).AttributeId;
                if ((uint)attributeId < AttributeBuffer.MAX_ATTRS)
                {
                    mask |= 1UL << attributeId;
                }
            }

            return mask;
        }

        struct AttributeAggregatorWithDirtyJob : IForEachWithEntity<AttributeBuffer, ActiveEffectContainer, DirtyFlags>
        {
            public World World;
            public CommandBuffer CommandBuffer;
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

                ulong touchedMask = RecomputeEffectiveValues(
                    World,
                    entity,
                    ref attrBuffer,
                    ref effects,
                    GraphPrograms,
                    GraphApi);
                RestorePersistentCurrentValues(ref attrBuffer, oldValues, touchedMask);

                // 4. 标记脏属性（用于延迟触发器）
                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    if (oldValues[i] != attrBuffer.CurrentValues[i])
                    {
                        dirtyFlags.MarkAttributeDirty(i);
                    }
                }

                CommandBuffer.Remove<AttributeAggregateDirty>(entity);
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

                ulong touchedMask = RecomputeEffectiveValues(
                    World,
                    entity,
                    ref attrBuffer,
                    ref effects,
                    GraphPrograms,
                    GraphApi);
                RestorePersistentCurrentValues(ref attrBuffer, oldValues, touchedMask);

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

                if (!anyDirty)
                {
                    return;
                }

                if (World.Has<DirtyFlags>(entity))
                {
                    ref DirtyFlags existingDirty = ref World.Get<DirtyFlags>(entity);
                    for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                    {
                        if (dirtyFlags.IsAttributeDirty(i))
                        {
                            existingDirty.MarkAttributeDirty(i);
                        }
                    }
                }
                else
                {
                    CommandBuffer.Add(entity, dirtyFlags);
                }

                CommandBuffer.Remove<AttributeAggregateDirty>(entity);
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void RestorePersistentCurrentValues(ref AttributeBuffer attrBuffer, Span<float> previousCurrentValues, ulong touchedMask)
        {
            ulong definedMask = attrBuffer.DefinedMask;
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                ulong bit = 1UL << i;
                if ((definedMask & bit) == 0UL)
                {
                    continue;
                }

                attrBuffer.CapValues[i] = attrBuffer.CurrentValues[i];
                bool touchedByAggregation = (touchedMask & bit) != 0UL;
                bool clampsToEffectiveCap =
                    AttributeRegistry.TryGetConstraints(i, out var constraints) &&
                    constraints.ClampCurrentToBase;
                if (!touchedByAggregation || clampsToEffectiveCap)
                {
                    attrBuffer.SetCurrent(i, previousCurrentValues[i]);
                }
            }
        }
    }
}
