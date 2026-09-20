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
            c.Add(new ConfigCatalogEntry("AI/behavior_trees.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/hfsm.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/profiles.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/decision_makers.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/decisions.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/target_filters.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/inputs.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/normalizations.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/curves.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/tasks.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/stances.json", ConfigMergePolicy.ArrayById));
            c.Add(new ConfigCatalogEntry("AI/actuators.json", ConfigMergePolicy.ArrayById));
            return c;
        }
    }
}

