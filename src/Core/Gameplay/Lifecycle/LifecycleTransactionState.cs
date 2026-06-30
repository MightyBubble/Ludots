using Arch.Core;
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
        public int[] AttributeSliceIds = [];
        public LifecycleAttributeValueSource AttributeSliceSource = LifecycleAttributeValueSource.Current;
        public bool HasMaterializedTarget;

        public void Reset()
        {
            Source = Entity.Null;
            Target = Entity.Null;
            Snapshot = default;
            PlacementCm = default;
            TargetTemplateId = string.Empty;
            AttributeSliceIds = [];
            AttributeSliceSource = LifecycleAttributeValueSource.Current;
            HasMaterializedTarget = false;
        }
    }
}
