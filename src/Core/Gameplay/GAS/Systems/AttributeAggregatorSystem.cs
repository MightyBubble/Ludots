using Arch.Core;
using Arch.Core.Extensions;
using Arch.Buffer;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Mathematics;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class AttributeAggregatorSystem : BaseSystem<World, float>
    {
        private readonly GraphProgramRegistry _graphPrograms;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly TagOps _tagOps;
        private readonly AttributeAggregateDirtyRegistry _aggregateDirty;
        private readonly CommandBuffer _commandBuffer = new();
        private readonly List<Entity> _drainedEntities = new(256);

        public AttributeAggregatorSystem(World world, GraphProgramRegistry graphPrograms = null, IGraphRuntimeApi graphApi = null, TagOps tagOps = null, AttributeAggregateDirtyRegistry aggregateDirty = null) : base(world)
        {
            _graphPrograms = graphPrograms;
            _graphApi = graphApi;
            _tagOps = tagOps ?? throw new InvalidOperationException(TagOps.MissingTagOpsError);
            _aggregateDirty = aggregateDirty ?? throw new InvalidOperationException(AttributeAggregateDirtyRegistry.MissingRegistryError);
        }

        public AttributeAggregateDirtyRegistry DirtyRegistry => _aggregateDirty;

        /// <summary>上一次 Update 的耗时（毫秒）；稳态零脏实体帧应近零，供预算守卫测试读取。</summary>
        public double LastUpdateElapsedMs { get; private set; }

        /// <summary>上一次 Update 实际聚合的脏实体数；零属性变更帧必须为 0。</summary>
        public int LastProcessedEntities { get; private set; }

        public override unsafe void Update(in float dt)
        {
            long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            int processed = 0;
            _drainedEntities.Clear();
            _aggregateDirty.Drain(_drainedEntities);

            int i = 0;
            try
            {
                for (; i < _drainedEntities.Count; i++)
                {
                    Entity entity = _drainedEntities[i];
                    if (!World.IsAlive(entity) ||
                        !World.Has<AttributeBuffer>(entity) ||
                        !World.Has<ActiveEffectContainer>(entity))
                    {
                        continue;
                    }

                    if (!World.Has<DirtyFlags>(entity))
                    {
                        throw new InvalidOperationException(
                            $"{TagOps.MissingDirtyFlagsError}: entity={entity.Id}, system=AttributeAggregatorSystem.");
                    }

                    ProcessDirtyEntity(World, entity, _commandBuffer, _graphPrograms, _graphApi, _tagOps, ref processed);
                }
            }
            catch
            {
                // 抛点起（含抛错实体）回灌注册表：聚合失败保持脏、下一 tick 重试，对齐旧 tag 未消费即滞留的合同。
                for (; i < _drainedEntities.Count; i++)
                {
                    _aggregateDirty.MarkDirty(_drainedEntities[i]);
                }

                throw;
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }

            LastProcessedEntities = processed;
            LastUpdateElapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void ExecuteDerivedGraphs(
            World world, Entity entity, ref AttributeBuffer attributes,
            GraphProgramRegistry graphPrograms, IGraphRuntimeApi graphApi)
        {
            if (!world.Has<AttributeDerivedGraphBinding>(entity)) return;

            ref var binding = ref world.Get<AttributeDerivedGraphBinding>(entity);
            if (binding.Count <= 0) return;
            if (binding.Count > AttributeDerivedGraphBinding.MAX_BINDINGS)
            {
                throw new InvalidOperationException(
                    $"AttributeDerivedGraphBinding count {binding.Count} exceeds capacity {AttributeDerivedGraphBinding.MAX_BINDINGS}.");
            }

            if (graphPrograms == null || graphApi == null)
            {
                throw new InvalidOperationException(
                    "AttributeDerivedGraphBinding requires configured graph program registry, graph runtime API, and graph handler table.");
            }
            if (graphApi is not IDerivedAttributeGraphRuntimeApi derivedAttributeApi)
            {
                throw new InvalidOperationException(IDerivedAttributeGraphRuntimeApi.MissingContractError);
            }

            derivedAttributeApi.BeginDerivedAttributeWrites(entity, in attributes);
            bool commit = false;
            try
            {
                for (int g = 0; g < binding.Count; g++)
                {
                    int programId = binding.GraphProgramIds[g];
                    if (programId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"AttributeDerivedGraphBinding contains invalid graph program id {programId}.");
                    }

                    if (!graphPrograms.TryGetProgram(programId, out var program))
                    {
                        throw new InvalidOperationException(
                            $"AttributeDerivedGraphBinding references missing graph program {programId}.");
                    }

                    GraphKind kind = graphPrograms.RequireKind(programId, GraphKind.Derived);
                    NodeLibraries.GASGraph.GraphExecutor.ExecuteDerived(
                        world,
                        entity,
                        program,
                        graphApi,
                        kind,
                        graphPrograms);
                }

                commit = true;
            }
            finally
            {
                derivedAttributeApi.EndDerivedAttributeWrites(entity, ref attributes, commit);
            }
        }

        /// <summary>
        /// 高槽位（[64, Plan)）聚合：current=base 叠加已提交效果的修饰符（聚合车道），
        /// cap 捕获聚合值、current 恢复持久值（与内嵌 RestorePersistentCurrentValues 同语义；
        /// 派生图高槽位车道属 P3，本切不接）。cap/current 变化标行级高脏位，由延迟触发器消费。
        /// </summary>
        private static void ProcessHighSlots(World world, Entity entity)
        {
            WorldAttributeStore store = WorldAttributeStoreAmbient.Current;
            if (store == null || store.SlotCount <= AttributeBuffer.MAX_ATTRS)
            {
                return;
            }

            if (!store.TryGetRow(entity, out int row))
            {
                return;
            }

            int first = AttributeBuffer.MAX_ATTRS;
            int count = store.SlotCount - first;
            Span<float> oldCurrent = count <= 512 ? stackalloc float[512] : new float[count];
            Span<float> oldCap = count <= 512 ? stackalloc float[512] : new float[count];
            oldCurrent = oldCurrent.Slice(0, count);
            oldCap = oldCap.Slice(0, count);
            for (int i = 0; i < count; i++)
            {
                oldCurrent[i] = store.GetCurrent(row, first + i);
                oldCap[i] = store.GetCap(row, first + i);
            }

            for (int i = 0; i < count; i++)
            {
                if (store.IsDefined(row, first + i))
                {
                    store.SetCurrentRaw(row, first + i, store.GetBase(row, first + i));
                }
            }

            if (world.Has<ActiveEffectContainer>(entity))
            {
                ref ActiveEffectContainer effects = ref world.Get<ActiveEffectContainer>(entity);
                for (int e = 0; e < effects.Count; e++)
                {
                    Entity effectEntity = effects.GetEntity(e);
                    if (!world.IsAlive(effectEntity) || !world.Has<GameplayEffect>(effectEntity))
                    {
                        continue;
                    }

                    ref readonly GameplayEffect effect = ref world.Get<GameplayEffect>(effectEntity);
                    if (effect.CancelRequested || effect.State < EffectState.Committed || !effect.AggregatesModifiers)
                    {
                        continue;
                    }

                    ref readonly var modifiers = ref world.Get<EffectModifiers>(effectEntity);
                    EffectModifierOps.ApplyAggregatedHigh(in modifiers, store, row);
                }
            }

            for (int i = 0; i < count; i++)
            {
                int slot = first + i;
                if (!store.IsDefined(row, slot))
                {
                    continue;
                }

                store.SetCapRaw(row, slot, store.GetCurrent(row, slot));
                store.SetCurrentRaw(row, slot, oldCurrent[i]);
                if (oldCap[i] != store.GetCap(row, slot) || oldCurrent[i] != store.GetCurrent(row, slot))
                {
                    store.MarkAttributeDirtyHigh(row, slot);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ulong RecomputeEffectiveValues(
            World world, Entity entity,
            ref AttributeBuffer attrBuffer,
            ref ActiveEffectContainer effects,
            GraphProgramRegistry graphPrograms, IGraphRuntimeApi graphApi)
        {
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

                    if (world.Has<GameplayEffect>(effectEntity))
                    {
                        ref readonly GameplayEffect effect = ref world.Get<GameplayEffect>(effectEntity);
                        if (effect.CancelRequested ||
                            effect.State < EffectState.Committed ||
                            !effect.AggregatesModifiers)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    ref readonly var modifiers = ref world.Get<EffectModifiers>(effectEntity);
                    EffectModifierOps.ApplyAggregated(in modifiers, ref attrBuffer);
                }
            }

            if (!world.Has<AttributeDerivedGraphBinding>(entity))
            {
                return 0UL;
            }

            Span<float> beforeDerived = stackalloc float[AttributeBuffer.MAX_ATTRS];
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                beforeDerived[i] = attrBuffer.CurrentValues[i];
            }

            ExecuteDerivedGraphs(world, entity, ref attrBuffer, graphPrograms, graphApi);

            ulong derivedWrittenMask = 0UL;
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                if (beforeDerived[i] != attrBuffer.CurrentValues[i])
                {
                    derivedWrittenMask |= 1UL << i;
                }
            }

            return derivedWrittenMask;
        }

        /// <summary>
        /// 旧 AttributeAggregatorWithDirtyJob 的逐实体聚合体；DirtyFlags 缺失由调用方先行抛错，
        /// 本方法入口即假定四组件齐备。CommandBuffer 仅承载 GameplayAttributeChangedBits 的
        /// 首次结构 Add（该组件由 ClearPresentationFlagsSystem 每 tick 清除并移除，结构合同属表现消费方）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void ProcessDirtyEntity(
            World world,
            Entity entity,
            CommandBuffer commandBuffer,
            GraphProgramRegistry graphPrograms,
            IGraphRuntimeApi graphApi,
            TagOps tagOps,
            ref int processedEntities)
        {
            processedEntities++;
            ref AttributeBuffer attrBuffer = ref world.Get<AttributeBuffer>(entity);
            ref ActiveEffectContainer effects = ref world.Get<ActiveEffectContainer>(entity);
            ref DirtyFlags dirtyFlags = ref world.Get<DirtyFlags>(entity);
            DirtyFlags dirtyBefore = dirtyFlags;
            Span<float> oldValues = stackalloc float[AttributeBuffer.MAX_ATTRS];
            Span<float> oldCaps = stackalloc float[AttributeBuffer.MAX_ATTRS];
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                oldValues[i] = attrBuffer.CurrentValues[i];
                oldCaps[i] = attrBuffer.CapValues[i];
            }

            ulong derivedWrittenMask = RecomputeEffectiveValues(
                world,
                entity,
                ref attrBuffer,
                ref effects,
                graphPrograms,
                graphApi);
            RestorePersistentCurrentValues(ref attrBuffer, oldValues, derivedWrittenMask);
            bool hasPresentationChanged = world.Has<GameplayAttributeChangedBits>(entity);
            GameplayAttributeChangedBits presentationChangedLocal = default;

            // 4. 标记脏属性（用于延迟触发器）
            ulong changedMask = 0UL;
            for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
            {
                if (oldValues[i] != attrBuffer.CurrentValues[i] ||
                    oldCaps[i] != attrBuffer.CapValues[i])
                {
                    dirtyFlags.MarkAttributeDirty(i);
                    changedMask |= 1UL << i;
                }
            }

            if (changedMask != 0UL)
            {
                try
                {
                    tagOps.MarkDirtyEntity(world, entity);
                }
                catch
                {
                    // 回滚只涉及 CurrentValues/CapValues/DirtyFlags：BaseValues 与 DefinedMask
                    // 在本作业内不可变（派生图只写 Current，见 EndDerivedAttributeWrites 契约）。
                    for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                    {
                        attrBuffer.CurrentValues[i] = oldValues[i];
                        attrBuffer.CapValues[i] = oldCaps[i];
                    }

                    dirtyFlags = dirtyBefore;
                    throw;
                }

                for (int i = 0; i < AttributeBuffer.MAX_ATTRS; i++)
                {
                    if ((changedMask & (1UL << i)) != 0UL)
                    {
                        MarkPresentationChanged(world, entity, i, ref presentationChangedLocal, ref hasPresentationChanged);
                    }
                }
            }

            ProcessHighSlots(world, entity);

            if (!hasPresentationChanged && presentationChangedLocal.IsAnyBitSet())
            {
                commandBuffer.Add(entity, presentationChangedLocal);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void RestorePersistentCurrentValues(
            ref AttributeBuffer attrBuffer,
            Span<float> previousCurrentValues,
            ulong derivedWrittenMask)
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
                if ((derivedWrittenMask & bit) != 0UL)
                {
                    continue;
                }

                attrBuffer.SetCurrent(i, previousCurrentValues[i]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MarkPresentationChanged(
            World world,
            Entity entity,
            int attributeId,
            ref GameplayAttributeChangedBits presentationChangedLocal,
            ref bool hasPresentationChanged)
        {
            if (hasPresentationChanged)
            {
                world.Get<GameplayAttributeChangedBits>(entity).Mark(attributeId);
                return;
            }

            presentationChangedLocal.Mark(attributeId);
        }
    }
}
