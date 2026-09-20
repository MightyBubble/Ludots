using System;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Zero-GC storage for dynamic attributes.
    /// Uses a fixed buffer to avoid heap allocations.
    /// </summary>
    public unsafe struct AttributeBuffer
    {
        public const int MAX_ATTRS = AttributeRegistry.MaxAttributes;

        public fixed float BaseValues[MAX_ATTRS];
        public fixed float CapValues[MAX_ATTRS];
        public fixed float CurrentValues[MAX_ATTRS];
        public ulong DefinedMask;

        public float GetCurrent(int attributeId)
        {
            ValidateAttributeId(attributeId);
            return CurrentValues[attributeId];
        }

        public float GetCap(int attributeId)
        {
            ValidateAttributeId(attributeId);
            return CapValues[attributeId];
        }

        public float GetBase(int attributeId)
        {
            ValidateAttributeId(attributeId);
            if (AttributeRegistry.TryGetConstraints(attributeId, out var constraints) &&
                constraints.ClampCurrentToBase)
            {
                return CapValues[attributeId];
            }

            return BaseValues[attributeId];
        }

        public bool HasAttribute(int attributeId)
        {
            if ((uint)attributeId >= (uint)MAX_ATTRS)
            {
                return false;
            }

            return (DefinedMask & (1UL << attributeId)) != 0UL;
        }

        public void SetBase(int attributeId, float value)
        {
            ValidateAttributeId(attributeId);
            DefinedMask |= 1UL << attributeId;
            BaseValues[attributeId] = value;
            CapValues[attributeId] = value;
            SetCurrent(attributeId, value);
        }

        public void SetCurrent(int attributeId, float value)
        {
            SetCurrentInternal(attributeId, value, clampToCapacity: true);
        }

        public void SetAggregatedCurrent(int attributeId, float value)
        {
            SetCurrentInternal(attributeId, value, clampToCapacity: false);
        }

        public static void ValidateAttributeId(int attributeId)
        {
            if ((uint)attributeId >= (uint)MAX_ATTRS)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attributeId),
                    attributeId,
                    $"attributeId must be in [0, {MAX_ATTRS}).");
            }
        }

        private void SetCurrentInternal(int attributeId, float value, bool clampToCapacity)
        {
            ValidateAttributeId(attributeId);
            DefinedMask |= 1UL << attributeId;
            if (AttributeRegistry.TryGetConstraints(attributeId, out var constraints))
            {
                if (clampToCapacity && constraints.ClampCurrentToBase)
                {
                    float max = GetBase(attributeId);
                    if (value > max) value = max;
                }
                if (constraints.HasMin && value < constraints.Min) value = constraints.Min;
                if (constraints.HasMax && value > constraints.Max) value = constraints.Max;
            }
            CurrentValues[attributeId] = value;
        }
    }
}
