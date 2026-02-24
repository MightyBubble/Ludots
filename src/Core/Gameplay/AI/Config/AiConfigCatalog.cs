using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.AI.Config
{
    public static class AiConfigCatalog
    {
        public static ConfigCatalog CreateDefault()
        {
            var c = new ConfigCatalog();
            c.Add(new ConfigCatalogEntry("AI/atoms.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/projection.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/utility.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/goap_actions.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/goap_goals.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/htn_domain.json", ConfigMergePolicy.DeepObject));
            return c;
        }
    }
}

