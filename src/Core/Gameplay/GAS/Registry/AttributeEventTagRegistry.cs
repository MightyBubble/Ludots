using Ludots.Core.Gameplay.GAS.Capacity;

namespace Ludots.Core.Gameplay.GAS.Registry
{
    public static class AttributeEventTagRegistry
    {
        private static readonly int[] _attributeIdToEventTagId =
            new int[GasLoadTimeCapacityPlan.AbsoluteMaxAttributeSlots];

        public static void Register(int attributeId, int eventTagId)
        {
            if ((uint)attributeId >= (uint)AttributeRegistry.MaxAttributes)
            {
                throw new System.ArgumentOutOfRangeException(nameof(attributeId));
            }

            _attributeIdToEventTagId[attributeId] = eventTagId;
        }

        public static void Register(string attributeName, string eventTagName)
        {
            int attributeId = AttributeRegistry.Register(attributeName);
            int eventTagId = TagRegistry.Register(eventTagName);
            Register(attributeId, eventTagId);
        }

        public static int GetEventTagId(int attributeId)
        {
            if ((uint)attributeId >= (uint)AttributeRegistry.MaxAttributes)
            {
                return TagRegistry.InvalidId;
            }

            return _attributeIdToEventTagId[attributeId];
        }
    }
}
