using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.GAS.Registry
{
    public static class AttributeEventTagRegistry
    {
        private static int[] Table => ModRegistryAmbient.Current.AttributeEventTagIds;

        public static void Register(int attributeId, int eventTagId)
        {
            int[] table = Table;
            if ((uint)attributeId >= (uint)table.Length) return;
            table[attributeId] = eventTagId;
        }

        public static void Register(string attributeName, string eventTagName)
        {
            int attributeId = AttributeRegistry.Register(attributeName);
            int eventTagId = TagRegistry.Register(eventTagName);
            Register(attributeId, eventTagId);
        }

        public static int GetEventTagId(int attributeId)
        {
            int[] table = Table;
            if ((uint)attributeId >= (uint)table.Length) return TagRegistry.InvalidId;
            return table[attributeId];
        }
    }
}
