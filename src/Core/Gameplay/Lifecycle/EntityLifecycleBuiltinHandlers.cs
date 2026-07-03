using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.Lifecycle
{
    public static class EntityLifecycleBuiltinHandlers
    {
        public static void RegisterAll(BuiltinHandlerRegistry registry)
        {
            registry.Register(BuiltinHandlerId.MaterializeTemplate, HandleMaterializeTemplate);
            registry.Register(BuiltinHandlerId.CopyIdentityComponents, HandleCopyIdentityComponents);
            registry.Register(BuiltinHandlerId.CopyAttributeSlice, HandleCopyAttributeSlice);
            registry.Register(BuiltinHandlerId.ClearActiveEffects, HandleClearActiveEffects);
            registry.Register(BuiltinHandlerId.TransferStableId, HandleTransferStableId);
            registry.Register(BuiltinHandlerId.RewireSelection, HandleRewireSelection);
            registry.Register(BuiltinHandlerId.ConsumeEntity, HandleConsumeEntity);
        }

        public static void HandleMaterializeTemplate(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var state = RequireTransactionState();
            var services = RequireServices();
            state.Target = EntityLifecycleAtomicOps.MaterializeTemplate(
                services,
                state.Source,
                state.TargetTemplateId,
                state.PlacementCm);
            state.HasMaterializedTarget = true;
        }

        public static void HandleCopyIdentityComponents(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var state = RequireTransactionState();
            EntityLifecycleAtomicOps.CopyIdentityComponents(world, state.Target, in state.Snapshot);
        }

        public static void HandleCopyAttributeSlice(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var state = RequireTransactionState();
            EntityLifecycleAtomicOps.CopyAttributeSlice(
                world,
                state.Target,
                in state.Snapshot,
                state);
        }

        public static void HandleClearActiveEffects(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var state = RequireTransactionState();
            EntityLifecycleAtomicOps.ClearActiveEffects(world, state.Target);
        }

        public static void HandleTransferStableId(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var state = RequireTransactionState();
            EntityLifecycleAtomicOps.TransferStableId(world, state.Target, in state.Snapshot);
        }

        public static void HandleRewireSelection(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var state = RequireTransactionState();
            EntityLifecycleAtomicOps.RewireSelection(RequireServices(), state.Source, state.Target);
        }

        public static void HandleConsumeEntity(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var state = RequireTransactionState();
            EntityLifecycleAtomicOps.ConsumeEntity(world, state.Source, "Entity lifecycle ConsumeEntity");
            state.HasMaterializedTarget = false;
        }

        private static LifecycleTransactionState RequireTransactionState()
        {
            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.LifecycleTransaction == null)
            {
                throw new InvalidOperationException("Lifecycle atomic handler requires LifecycleTransaction on BuiltinHandlerExecutionContext.");
            }

            return runtime.LifecycleTransaction;
        }

        private static EntityLifecycleRuntimeServices RequireServices()
        {
            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.LifecycleServices == null)
            {
                throw new InvalidOperationException("Lifecycle atomic handler requires LifecycleServices on BuiltinHandlerExecutionContext.");
            }

            return runtime.LifecycleServices;
        }
    }
}
