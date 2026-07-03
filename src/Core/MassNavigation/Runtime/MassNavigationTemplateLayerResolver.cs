using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Layers;

namespace Ludots.Core.MassNavigation.Runtime;

public static class MassNavigationTemplateLayerResolver
{
    public static MassNavigationAgentLayer RequireAgentLayer(EntityTemplate template, string templateId)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (template.Components == null ||
            !template.Components.TryGetValue("EntityLayer", out JsonNode? layerNode))
        {
            throw new InvalidOperationException($"MassNavigation agent template '{templateId}' must author EntityLayer.");
        }

        LayerMask layerMask = EntityLayerAuthoring.ReadLayerMask(
            layerNode,
            $"MassNavigation agent template '{templateId}'");
        return new MassNavigationAgentLayer(layerMask.Category, layerMask.Mask);
    }
}
