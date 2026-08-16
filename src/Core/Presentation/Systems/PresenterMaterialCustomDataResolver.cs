using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Systems
{
    internal static class PresenterMaterialCustomDataResolver
    {
        public static MaterialCustomDataPayload Resolve(
            PresenterEntityRuntime runtime,
            Entity entity,
            in MaterialCustomDataBinding binding)
        {
            MaterialCustomDataSlotBinding[] slots = binding.Slots;
            if (slots == null || slots.Length == 0)
            {
                return default;
            }

            var payload = new MaterialCustomDataPayload
            {
                Count = (byte)slots.Length,
            };

            for (int i = 0; i < slots.Length; i++)
            {
                ref readonly MaterialCustomDataSlotBinding slot = ref slots[i];
                Vector4 value = slot.Lane switch
                {
                    MaterialCustomDataLane.Float => new Vector4(
                        slot.ParamKey >= 0
                            ? RequireFloatParam(runtime, entity, slot.ParamKey, "AssetBinding.materialCustomData.paramKey")
                            : slot.DefaultFloatValue,
                        0f,
                        0f,
                        0f),
                    MaterialCustomDataLane.Int => new Vector4(
                        slot.ParamKey >= 0
                            ? RequireIntParam(runtime, entity, slot.ParamKey, "AssetBinding.materialCustomData.paramKey")
                            : slot.DefaultIntValue,
                        0f,
                        0f,
                        0f),
                    MaterialCustomDataLane.Vector => slot.ParamKey >= 0
                        ? RequireVectorParam(runtime, entity, slot.ParamKey, "AssetBinding.materialCustomData.paramKey")
                        : slot.DefaultVectorValue,
                    _ => default,
                };
                payload.SetSlot(slot.Slot, value);
            }

            return payload;
        }

        private static int RequireIntParam(PresenterEntityRuntime runtime, Entity entity, int paramKey, string context)
        {
            if (!runtime.TryResolveInt(entity, paramKey, out int value))
            {
                throw new InvalidOperationException($"{context} {paramKey} did not resolve to an int param value.");
            }

            return value;
        }

        private static float RequireFloatParam(PresenterEntityRuntime runtime, Entity entity, int paramKey, string context)
        {
            if (!runtime.TryResolveFloat(entity, paramKey, out float value))
            {
                throw new InvalidOperationException($"{context} {paramKey} did not resolve to a float param value.");
            }

            return value;
        }

        private static Vector4 RequireVectorParam(PresenterEntityRuntime runtime, Entity entity, int paramKey, string context)
        {
            if (!runtime.TryResolveVector(entity, paramKey, out Vector4 value))
            {
                throw new InvalidOperationException($"{context} {paramKey} did not resolve to a vector param value.");
            }

            return value;
        }
    }
}
