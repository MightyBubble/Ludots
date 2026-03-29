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
        private readonly AnimationProfileRegistry _profiles;

        public VisualTemplateConfigLoader(
            ConfigPipeline configs,
            VisualTemplateRegistry templates,
            MeshAssetRegistry meshes,
            AnimatorControllerRegistry animators,
            AnimationProfileRegistry profiles)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
            _animators = animators ?? throw new ArgumentNullException(nameof(animators));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
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

            string profileKey = node["animationProfileId"]?.GetValue<string>() ?? string.Empty;
            int animationProfileId = ResolveProfileId(profileKey, key);

            if (renderPath.IsSkinnedLane() && animationProfileId <= 0)
            {
                throw new InvalidOperationException(
                    $"Visual template '{key}' uses skinned render path '{renderPath}' but does not define animationProfileId. " +
                    "Skinned template config must bind through AnimationProfileRegistry.");
            }

            string animatorKey = node["animatorControllerId"]?.GetValue<string>() ?? string.Empty;
            int animatorControllerId = ResolveAnimatorControllerId(animatorKey, key);
            if (animationProfileId > 0)
            {
                if (!_profiles.TryGet(animationProfileId, out var profile))
                {
                    throw new InvalidOperationException(
                        $"Visual template '{key}' references unresolved animationProfileId '{profileKey}'.");
                }

                if (profile.AnimatorControllerId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Visual template '{key}' references animation profile '{profileKey}' without an animator controller.");
                }

                if (animatorControllerId > 0 && animatorControllerId != profile.AnimatorControllerId)
                {
                    throw new InvalidOperationException(
                        $"Visual template '{key}' defines animatorControllerId '{animatorKey}' but animationProfileId '{profileKey}' maps to a different controller.");
                }

                animatorControllerId = profile.AnimatorControllerId;
            }

            PresentationRenderContract.ValidateTemplate($"Visual template '{key}'", renderPath, animatorControllerId, animationProfileId);

            return new VisualTemplateDefinition
            {
                MeshAssetId = meshAssetId,
                MaterialId = node["materialId"]?.GetValue<int>() ?? 0,
                AnimatorControllerId = animatorControllerId,
                AnimationProfileId = animationProfileId,
                BaseScale = node["baseScale"]?.GetValue<float>() ?? 1f,
                RenderPath = renderPath,
                Mobility = mobility,
                VisibleByDefault = node["visibleByDefault"]?.GetValue<bool>() ?? true,
            };
        }

        private int ResolveAnimatorControllerId(string animatorKey, string templateKey)
        {
            if (string.IsNullOrWhiteSpace(animatorKey))
            {
                return 0;
            }

            int animatorControllerId = _animators.GetId(animatorKey);
            if (animatorControllerId <= 0)
            {
                throw new InvalidOperationException(
                    $"Visual template '{templateKey}' references unknown animatorControllerId '{animatorKey}'.");
            }

            return animatorControllerId;
        }

        private int ResolveProfileId(string profileKey, string templateKey)
        {
            if (string.IsNullOrWhiteSpace(profileKey))
            {
                return 0;
            }

            int animationProfileId = _profiles.GetId(profileKey);
            if (animationProfileId <= 0)
            {
                throw new InvalidOperationException(
                    $"Visual template '{templateKey}' references unknown animationProfileId '{profileKey}'.");
            }

            return animationProfileId;
        }
    }
}
