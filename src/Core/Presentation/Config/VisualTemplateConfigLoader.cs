using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Config
{
    public sealed class VisualTemplateConfigLoader
    {
        private readonly ConfigPipeline _configs;
        private readonly VisualTemplateRegistry _templates;
        private readonly MeshAssetRegistry _meshes;
        private readonly AnimatorControllerRegistry _animators;

        public VisualTemplateConfigLoader(
            ConfigPipeline configs,
            VisualTemplateRegistry templates,
            MeshAssetRegistry meshes,
            AnimatorControllerRegistry animators)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
            _animators = animators ?? throw new ArgumentNullException(nameof(animators));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Presentation/visual_templates.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                string key = node["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("Visual template is missing required 'id'.");

                _templates.Register(key, Parse(node, key));
            }
        }

        private VisualTemplateDefinition Parse(JsonNode node, string key)
        {
            string renderPathText = node["renderPath"]?.GetValue<string>() ?? string.Empty;
            if (!Enum.TryParse(renderPathText, ignoreCase: true, out VisualRenderPath renderPath))
                throw new InvalidOperationException($"Visual template '{key}' has invalid renderPath '{renderPathText}'.");

            string mobilityText = node["mobility"]?.GetValue<string>() ?? nameof(VisualMobility.Movable);
            if (!Enum.TryParse(mobilityText, ignoreCase: true, out VisualMobility mobility))
                throw new InvalidOperationException($"Visual template '{key}' has invalid mobility '{mobilityText}'.");

            string meshKey = node["meshAssetId"]?.GetValue<string>() ?? string.Empty;
            int meshAssetId = string.IsNullOrWhiteSpace(meshKey) ? 0 : _meshes.GetId(meshKey);
            if (renderPath != VisualRenderPath.None && meshAssetId <= 0)
                throw new InvalidOperationException($"Visual template '{key}' references unknown mesh asset '{meshKey}'.");

            string animatorKey = node["animatorControllerId"]?.GetValue<string>() ?? string.Empty;
            int animatorControllerId = string.IsNullOrWhiteSpace(animatorKey) ? 0 : _animators.Register(animatorKey);
            PresentationRenderContract.ValidateTemplate($"Visual template '{key}'", renderPath, animatorControllerId);

            return new VisualTemplateDefinition
            {
                MeshAssetId = meshAssetId,
                MaterialId = node["materialId"]?.GetValue<int>() ?? 0,
                AnimatorControllerId = animatorControllerId,
                BaseScale = node["baseScale"]?.GetValue<float>() ?? 1f,
                RenderPath = renderPath,
                Mobility = mobility,
                VisibleByDefault = node["visibleByDefault"]?.GetValue<bool>() ?? true,
            };
        }
    }
}
