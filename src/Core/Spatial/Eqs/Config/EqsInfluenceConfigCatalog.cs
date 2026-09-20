using Ludots.Core.Config;

namespace Ludots.Core.Spatial.Eqs.Config
{
    public static class EqsInfluenceConfigCatalog
    {
        public static ConfigCatalog CreateDefault()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("Spatial/influence_fields.json", ConfigMergePolicy.ArrayById));
            catalog.Add(new ConfigCatalogEntry("Spatial/eqs_queries.json", ConfigMergePolicy.ArrayById));
            catalog.Add(new ConfigCatalogEntry("Spatial/eqs_scenarios.json", ConfigMergePolicy.ArrayById));
            return catalog;
        }
    }
}
