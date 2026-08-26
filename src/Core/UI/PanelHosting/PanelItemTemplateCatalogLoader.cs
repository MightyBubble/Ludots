using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.UI.PanelProjection;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.UI.PanelHosting
{
    public sealed class PanelItemTemplateCatalogLoader
    {
        public const string ConfigPath = "Panels/item_templates.json";

        private readonly ConfigPipeline _configs;

        public PanelItemTemplateCatalogLoader(ConfigPipeline configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public PanelItemTemplateRegistry Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            var registry = new PanelItemTemplateRegistry();
            var entry = ConfigPipeline.RequireEntry(catalog, ConfigPath, ConfigMergePolicy.ArrayById, "id");
            IReadOnlyList<MergedConfigEntry> nodes = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Node is not JsonObject templateObject)
                {
                    throw new InvalidOperationException($"Item template entry #{i} in '{ConfigPath}' must be a JSON object.");
                }

                registry.Register(PanelItemTemplateLoader.Load(templateObject));
            }

            registry.Freeze();
            return registry;
        }
    }
}
