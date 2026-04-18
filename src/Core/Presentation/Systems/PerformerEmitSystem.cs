using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Wave 4 asset emitter. Iterates performer instances and emits only AssetBinding behavior.
    /// </summary>
    public sealed class PerformerEmitSystem : BaseSystem<World, float>
    {
        private readonly PerformerInstanceBuffer _instances;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly Dictionary<string, object> _globals;
        private readonly PerformerAssetEmitRuntime _assetEmitter;

        public PerformerEmitSystem(
            World world,
            PerformerInstanceBuffer instances,
            PerformerDefinitionRegistry definitions,
            PresentationRequestBuffer requests,
            System.Collections.Generic.Dictionary<string, object> globals,
            PerformerAnimatorStateBuffer animatorStates = null,
            SoundRequestBuffer soundRequests = null)
            : base(world)
        {
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _globals = globals ?? new Dictionary<string, object>();
            _assetEmitter = new PerformerAssetEmitRuntime(
                world,
                _instances,
                _definitions,
                requests,
                globals,
                animatorStates,
                soundRequests);
        }

        public override void Update(in float dt)
        {
            _instances.ProcessActive(dt, (int handle, ref PerformerInstance instance) =>
            {
                if (!_definitions.TryGet(instance.DefId, out PerformerDefinition definition))
                {
                    return;
                }

                if (instance.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(instance.Owner))
                {
                    _instances.Release(handle);
                    return;
                }

                if (definition.DefaultLifetime > 0f && instance.Elapsed >= definition.DefaultLifetime)
                {
                    _instances.Release(handle);
                    return;
                }

                if (!EvaluateVisibility(definition, instance.Owner))
                {
                    return;
                }

                EmitAssetBindings(handle, in instance, definition);
            });
        }

        private void EmitAssetBindings(int handle, in PerformerInstance instance, PerformerDefinition definition)
        {
            BehaviorSlot[] behaviors = definition.Behaviors ?? Array.Empty<BehaviorSlot>();
            LODLevel lod = ResolveOwnerLod(instance.Owner);
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind != BehaviorKind.AssetBinding || !IsBehaviorActive(instance.BehaviorActiveMask, slot.SlotIndex))
                {
                    continue;
                }

                _assetEmitter.Emit(handle, instance.DefId, in instance, definition, slot.SlotIndex, slot.AssetBinding, lod);
            }
        }

        private LODLevel ResolveOwnerLod(Entity owner)
        {
            if (!World.IsAlive(owner) || !World.Has<CullState>(owner))
            {
                return LODLevel.High;
            }

            return World.Get<CullState>(owner).LOD;
        }

        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

        private bool EvaluateVisibility(in PerformerDefinition definition, Entity owner)
        {
            ref readonly ConditionRef condition = ref definition.VisibilityCondition;
            if (condition.Inline == InlineConditionKind.None && condition.GraphProgramId <= 0)
            {
                return true;
            }

            if (condition.GraphProgramId > 0)
            {
                return true;
            }

            return condition.Inline switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceIsLocalPlayer => IsLocalPlayer(owner),
                InlineConditionKind.TargetIsLocalPlayer => IsLocalPlayer(owner),
                InlineConditionKind.SourceIsAlive => World.IsAlive(owner),
                InlineConditionKind.TargetIsAlive => World.IsAlive(owner),
                InlineConditionKind.OwnerCullVisible => IsOwnerCullVisible(owner),
                InlineConditionKind.SourceHasAttributes => World.IsAlive(owner) && World.Has<AttributeBuffer>(owner),
                InlineConditionKind.SourceHasVisualTransform => World.IsAlive(owner) && World.Has<VisualTransform>(owner),
                _ => true,
            };
        }

        private bool IsLocalPlayer(Entity owner)
        {
            return _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? candidate) &&
                   candidate is Entity localPlayer &&
                   localPlayer == owner;
        }

        private bool IsOwnerCullVisible(Entity owner)
        {
            if (!World.IsAlive(owner))
            {
                return false;
            }

            return !World.Has<CullState>(owner) || World.Get<CullState>(owner).IsVisible;
        }
    }
}
