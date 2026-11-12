using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.GAS.Registry
{
    public static class AttributeEventTagRegistry
    {
        private static int[] AttributeIdToEventTagId => ModRegistryAmbient.Current.AttributeEventTagIds;

        public static void Register(int attributeId, int eventTagId)
        {
            if (attributeId < 0 || attributeId >= AttributeRegistry.MaxAttributes) return;
            AttributeIdToEventTagId[attributeId] = eventTagId;
        }

        public static void Register(string attributeName, string eventTagName)
        {
            int attributeId = AttributeRegistry.Register(attributeName);
            int eventTagId = TagRegistry.Register(eventTagName);
            Register(attributeId, eventTagId);
        }

        public static int GetEventTagId(int attributeId)
        {
            if (attributeId < 0 || attributeId >= AttributeRegistry.MaxAttributes) return TagRegistry.InvalidId;
            return AttributeIdToEventTagId[attributeId];
        }
    }
}

