using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.UI.PanelProjection;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.UI.PanelHosting
{
    /// <summary>
    /// Config-pipeline loader for panel templates (ArrayById merge across Core and mods,
    /// same shape as GraphLookupTableLoader). Every entry goes through the strict
    /// <see cref="PanelTemplateLoader"/> — bad templates fail the load, naming the id.
    /// </summary>
    public sealed class PanelTemplateCatalogLoader
    {
        public const string ConfigPath = "Panels/panel_templates.json";

        private readonly ConfigPipeline _configs;

        public PanelTemplateCatalogLoader(ConfigPipeline configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public PanelTemplateRegistry Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            var registry = new PanelTemplateRegistry();
            var entry = ConfigPipeline.RequireEntry(catalog, ConfigPath, ConfigMergePolicy.ArrayById, "id");
            IReadOnlyList<MergedConfigEntry> nodes = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Node is not JsonObject templateObject)
                {
                    throw new InvalidOperationException($"Panel template entry #{i} in '{ConfigPath}' must be a JSON object.");
                }

                registry.Register(PanelTemplateLoader.Load(templateObject));
            }

            registry.Freeze();
            foreach (PanelTemplate template in registry.Snapshot())
            {
                // Unregistered graph is a data-plane miss, not a structural error: pins stay
                // on their defaults (no error, no empty) until the graph ships.
                int graphId = NodeLibraries.GASGraph.Host.GraphIdRegistry.GetId(template.Graph);
                if (graphId == NodeLibraries.GASGraph.Host.GraphIdRegistry.InvalidId)
                {
                    graphId = -1;
                }

                template.GraphId = graphId;
                PanelListProjector.BindSymbols(template);
            }

            return registry;
        }
    }
}
