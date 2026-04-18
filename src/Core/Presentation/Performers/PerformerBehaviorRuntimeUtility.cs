namespace Ludots.Core.Presentation.Performers
{
    public static class PerformerBehaviorRuntimeUtility
    {
        public static int ComposeBehaviorStableId(int performerStableId, int slotIndex)
        {
            unchecked
            {
                return (performerStableId * 397) ^ (slotIndex + 1);
            }
        }
    }
}
