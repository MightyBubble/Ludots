using System;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Zero-GC storage for dynamic attributes.
    /// Uses a fixed buffer to avoid heap allocations.
    /// Max 64 attributes supported per entity with this struct.
    /// </summary>
    public unsafe struct AttributeBuffer
    {
        public const int MAX_ATTRS = 64;

        public fixed float BaseValues[MAX_ATTRS];
        public fixed float CurrentValues[MAX_ATTRS];
        
        // Modifiers could be aggregated here or calculated on the fly.
        // For simplicity in this 0GC version, we update CurrentValues directly 
        // when BaseValues change or when effects are applied.
        
        /// <summary>
        /// Gets the current value of an attribute by ID.
        /// </summary>
        public float GetCurrent(int attributeId)
        {
            if (attributeId < 0 || attributeId >= MAX_ATTRS) return 0f;
            return CurrentValues[attributeId];
        }

        /// <summary>
        /// Gets the base value of an attribute by ID.
        /// </summary>
        public float GetBase(int attributeId)
        {
            if (attributeId < 0 || attributeId >= MAX_ATTRS) return 0f;
            return BaseValues[attributeId];
        }

        /// <summary>
        /// Sets the base value of an attribute by ID.
        /// Also re-applies constraints to CurrentValue (e.g. ClampCurrentToBase, Min/Max).
        /// </summary>
        public void SetBase(int attributeId, float value)
        {
            if (attributeId < 0 || attributeId >= MAX_ATTRS) return;
            BaseValues[attributeId] = value;
            // Re-apply current value through constraints.
            // Uses the new base as default current (reset to base), then SetCurrent applies clamping.
            SetCurrent(attributeId, value);
        }

        public void SetCurrent(int attributeId, float value)
        {
            if (attributeId < 0 || attributeId >= MAX_ATTRS) return;
            if (AttributeRegistry.TryGetConstraints(attributeId, out var constraints))
            {
                if (constraints.ClampCurrentToBase)
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
