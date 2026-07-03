using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.Lifecycle
{
    public sealed class LifecycleTransactionState
    {
        public Entity Source;
        public Entity Target;
        public LifecycleSnapshot Snapshot;
        public Fix64Vec2 PlacementCm;
        public string TargetTemplateId = string.Empty;
        public int AttributeSliceCount;
        public int AttributeSlice0;
        public int AttributeSlice1;
        public int AttributeSlice2;
        public int AttributeSlice3;
        public LifecycleAttributeValueSource AttributeSliceSource;
        public bool HasMaterializedTarget;

        public bool TryAddAttributeSliceId(int attributeId)
        {
            if (AttributeSliceCount >= EffectParamKeys.LifecycleAttributeCapacity)
            {
                return false;
            }

            switch (AttributeSliceCount)
            {
                case 0:
                    AttributeSlice0 = attributeId;
                    break;
                case 1:
                    AttributeSlice1 = attributeId;
                    break;
                case 2:
                    AttributeSlice2 = attributeId;
                    break;
                case 3:
                    AttributeSlice3 = attributeId;
                    break;
            }

            AttributeSliceCount++;
            return true;
        }

        public int GetAttributeSliceId(int index)
        {
            return index switch
            {
                0 => AttributeSlice0,
                1 => AttributeSlice1,
                2 => AttributeSlice2,
                3 => AttributeSlice3,
                _ => throw new ArgumentOutOfRangeException(nameof(index), index, "Lifecycle attribute slice index is out of range."),
            };
        }

        public void Reset()
        {
            Source = Entity.Null;
            Target = Entity.Null;
            Snapshot = default;
            PlacementCm = default;
            TargetTemplateId = string.Empty;
            AttributeSliceCount = 0;
            AttributeSlice0 = 0;
            AttributeSlice1 = 0;
            AttributeSlice2 = 0;
            AttributeSlice3 = 0;
            AttributeSliceSource = default;
            HasMaterializedTarget = false;
        }
    }
}
