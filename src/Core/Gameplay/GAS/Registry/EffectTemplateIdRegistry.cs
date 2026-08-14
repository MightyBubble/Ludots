using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.GAS.Registry
{
    public static class EffectTemplateIdRegistry
    {
        public const int InvalidId = 0;
        public const int MaxTemplates = 4095;

        private static IdentityTable Table => ModRegistryAmbient.Current.EffectTemplateIds;

        public static bool IsFrozen => Table.IsFrozen;

        public static void Freeze() => Table.Freeze();

        public static void Clear() => ModRegistryAmbient.Current.ReplaceEffectTemplateIds();

        public static int Register(string name) => Table.Register(name);

        public static int GetId(string name) => Table.GetId(name);

        public static string GetName(int id) => Table.GetName(id);

        public static RegistryMapping[] SnapshotMappings() => Table.SnapshotMappings();
    }
}
