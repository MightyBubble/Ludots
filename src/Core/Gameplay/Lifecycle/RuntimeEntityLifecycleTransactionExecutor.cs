using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.Lifecycle
{
    public static class LifecycleTransactionPrograms
    {
        private static readonly LifecycleOpId[] DeployConsumeSourceOps =
        [
            LifecycleOpId.MaterializeTemplate,
            LifecycleOpId.CopyIdentityComponents,
            LifecycleOpId.CopyAttributeSlice,
            LifecycleOpId.ClearActiveEffects,
            LifecycleOpId.TransferStableId,
            LifecycleOpId.ConsumeEntity,
        ];

        public static ReadOnlySpan<LifecycleOpId> DeployConsumeSource => DeployConsumeSourceOps;
    }

    public static class RuntimeEntityLifecycleTransactionExecutor
    {
        public static Entity Execute(
            EntityLifecycleRuntimeServices services,
            LifecycleTransactionState state,
            ReadOnlySpan<LifecycleOpId> ops)
        {
            World world = services.World;
            Entity source = state.Source;
            if (!world.IsAlive(source))
            {
                throw new LifecycleExecutionException("Entity lifecycle transaction failed because the source entity is no longer alive.");
            }

            if (world.Has<PresentationDestroyPending>(source))
            {
                throw new LifecycleExecutionException("Entity lifecycle transaction failed because the source entity is already pending destroy.");
            }

            try
            {
                for (int i = 0; i < ops.Length; i++)
                {
                    ExecuteOp(services, state, ops[i]);
                }

                return state.Target;
            }
            catch
            {
                if (state.HasMaterializedTarget)
                {
                    EntityLifecycleAtomicOps.RollbackMaterializedTarget(world, state.Target);
                    state.HasMaterializedTarget = false;
                    state.Target = Entity.Null;
                }

                throw;
            }
        }

        private static void ExecuteOp(EntityLifecycleRuntimeServices services, LifecycleTransactionState state, LifecycleOpId op)
        {
            World world = services.World;
            switch (op)
            {
                case LifecycleOpId.MaterializeTemplate:
                    if (string.IsNullOrWhiteSpace(state.TargetTemplateId))
                    {
                        throw new InvalidOperationException("MaterializeTemplate requires TargetTemplateId on lifecycle transaction state.");
                    }

                    state.Target = EntityLifecycleAtomicOps.MaterializeTemplate(
                        services,
                        state.Source,
                        state.TargetTemplateId,
                        state.PlacementCm);
                    state.HasMaterializedTarget = true;
                    break;
                case LifecycleOpId.CopyIdentityComponents:
                    EntityLifecycleAtomicOps.CopyIdentityComponents(world, state.Target, in state.Snapshot);
                    break;
                case LifecycleOpId.CopyAttributeSlice:
                    EntityLifecycleAtomicOps.CopyAttributeSlice(
                        world,
                        state.Target,
                        in state.Snapshot,
                        state);
                    break;
                case LifecycleOpId.ClearActiveEffects:
                    EntityLifecycleAtomicOps.ClearActiveEffects(world, state.Target);
                    break;
                case LifecycleOpId.TransferStableId:
                    EntityLifecycleAtomicOps.TransferStableId(world, state.Target, in state.Snapshot);
                    break;
                case LifecycleOpId.ConsumeEntity:
                    EntityLifecycleAtomicOps.ConsumeEntity(world, state.Source, "Entity lifecycle ConsumeEntity");
                    state.HasMaterializedTarget = false;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported lifecycle op '{op}'.");
            }
        }

        public static void ConfigureDeployConsumeSourceFromConfig(
            LifecycleTransactionState state,
            in EffectConfigParams configParams)
        {
            if (!configParams.TryGetLifecycleAttributeValueSource(
                    EffectParamKeys.LifecycleAttributeValueSource,
                    out int rawValueSource) ||
                (rawValueSource != (int)LifecycleAttributeValueSource.Base &&
                 rawValueSource != (int)LifecycleAttributeValueSource.Current))
            {
                throw new InvalidOperationException(
                    "DeployConsumeSource requires config param '_ep.lifecycleAttributeValueSource' with type 'LifecycleAttributeValueSource'.");
            }

            state.AttributeSliceSource = (LifecycleAttributeValueSource)rawValueSource;
            state.AttributeSliceCount = 0;
            state.AttributeSlice0 = 0;
            state.AttributeSlice1 = 0;
            state.AttributeSlice2 = 0;
            state.AttributeSlice3 = 0;

            for (int i = 0; i < EffectParamKeys.LifecycleAttributeCapacity; i++)
            {
                int keyId = EffectParamKeys.GetLifecycleAttributeKey(i);
                if (keyId <= 0)
                {
                    throw new InvalidOperationException("EffectParamKeys must be initialized before lifecycle config is compiled.");
                }

                if (!configParams.TryGetAttributeIdStrict(keyId, out int attributeId))
                {
                    continue;
                }

                if (attributeId < 0)
                {
                    throw new InvalidOperationException(
                        $"DeployConsumeSource lifecycle attribute entry {i} resolved to invalid attribute id '{attributeId}'.");
                }

                if (!state.TryAddAttributeSliceId(attributeId))
                {
                    throw new InvalidOperationException(
                        $"DeployConsumeSource supports at most {EffectParamKeys.LifecycleAttributeCapacity} configured lifecycle attribute slices.");
                }
            }

            if (state.AttributeSliceCount == 0)
            {
                throw new InvalidOperationException(
                    "DeployConsumeSource requires at least one configured lifecycle attribute slice.");
            }
        }
    }
}
