using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;

namespace Ludots.Core.MassNavigation.Runtime;

internal sealed class MassNavigationAuthoringContract
{
    private readonly Dictionary<string, EntityTemplate> _templates;
    private readonly EntityTemplateKeyRegistry _templateKeys;
    private readonly PerformerDefinitionRegistry _performers;
    private readonly MeshAssetRegistry _meshAssets;
    private readonly MassNavigationConfig _config;

    private MassNavigationAuthoringContract(
        Dictionary<string, EntityTemplate> templates,
        EntityTemplateKeyRegistry templateKeys,
        PerformerDefinitionRegistry performers,
        MeshAssetRegistry meshAssets,
        MassNavigationConfig config)
    {
        _templates = templates;
        _templateKeys = templateKeys;
        _performers = performers;
        _meshAssets = meshAssets;
        _config = config;
    }

    public IReadOnlyDictionary<string, EntityTemplate> Templates => _templates;

    public static MassNavigationAuthoringContract Require(GameEngine engine, MassNavigationConfig config)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("MassNavigation runtime requires EntityTemplateKeyRegistry.");
        PerformerDefinitionRegistry performers = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
            ?? throw new InvalidOperationException("MassNavigation runtime requires PerformerDefinitionRegistry.");
        MeshAssetRegistry meshAssets = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
            ?? throw new InvalidOperationException("MassNavigation runtime requires PresentationMeshAssetRegistry.");
        IVisualHeightmap visualHeightmap = engine.GetService(CoreServiceKeys.VisualHeightmap)
            ?? throw new InvalidOperationException("MassNavigation runtime requires a map-owned VisualHeightmapAsset bound through CoreServiceKeys.VisualHeightmap.");
        if (visualHeightmap is not IVisualHeightmapRenderSource)
        {
            throw new InvalidOperationException("MassNavigation runtime requires VisualHeightmap to implement IVisualHeightmapRenderSource so the large-world terrain is visible.");
        }

        var templates = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
        foreach (EntityTemplate template in engine.MapLoader.TemplateRegistry.GetAll())
        {
            if (template == null || string.IsNullOrWhiteSpace(template.Id))
            {
                continue;
            }

            templates[template.Id] = template;
        }

        var contract = new MassNavigationAuthoringContract(templates, templateKeys, performers, meshAssets, config);
        contract.ValidateAll();
        return contract;
    }

    public int RequireTemplateKey(string templateId)
    {
        if (!_templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            throw new InvalidOperationException($"MassNavigation runtime template '{templateId}' was not registered in EntityTemplateKeyRegistry.");
        }

        return templateKeyId;
    }

    public void ValidateTemplate(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException("MassNavigation runtime template id must be non-empty.");
        }

        if (!_templates.ContainsKey(templateId))
        {
            throw new InvalidOperationException($"MassNavigation runtime requires configured entity template '{templateId}'.");
        }

        RequireTemplateKey(templateId);
    }

    public MassNavigationAgentLayer RequireAgentLayer(string templateId)
    {
        ValidateTemplate(templateId);
        return MassNavigationTemplateLayerResolver.RequireAgentLayer(_templates[templateId], templateId);
    }

    private void ValidateAll()
    {
        ValidateRequiredMeshAssets();

        if (!_config.ScenarioRuntime.AutoSpawnConfiguredScenario)
        {
            return;
        }

        ValidatePerformer(_config.Presentation.BlockerPerformerId);
        ValidatePerformer(_config.Presentation.HotspotPerformerId);
        ValidateTemplate(_config.Presentation.BlockerTemplateId);
        ValidateTemplate(_config.Presentation.HotspotTemplateId);

        for (int i = 0; i < _config.Presentation.Teams.Length; i++)
        {
            MassNavigationTeamPresentationConfig team = _config.Presentation.Teams[i];
            ValidateTemplate(team.LightTemplateId);
            ValidateTemplate(team.HeavyTemplateId);
            MassNavigationTemplateLayerResolver.RequireAgentLayer(_templates[team.LightTemplateId], team.LightTemplateId);
            MassNavigationTemplateLayerResolver.RequireAgentLayer(_templates[team.HeavyTemplateId], team.HeavyTemplateId);
            ValidatePerformer(team.LightPerformerId);
            ValidatePerformer(team.HeavyPerformerId);
        }
    }

    private void ValidatePerformer(string performerId)
    {
        if (string.IsNullOrWhiteSpace(performerId))
        {
            throw new InvalidOperationException("MassNavigation runtime performer id must be non-empty.");
        }

        int performerDefinitionId = _performers.GetId(performerId);
        if (performerDefinitionId <= 0 || !_performers.TryGet(performerDefinitionId, out _))
        {
            throw new InvalidOperationException($"MassNavigation runtime requires configured performer definition '{performerId}'.");
        }
    }

    private void ValidateRequiredMeshAssets()
    {
        for (int i = 0; i < _config.Presentation.RequiredMeshAssetIds.Length; i++)
        {
            string meshAssetId = _config.Presentation.RequiredMeshAssetIds[i];
            int runtimeId = _meshAssets.GetId(meshAssetId);
            if (runtimeId <= 0 ||
                !_meshAssets.TryGetDescriptor(runtimeId, out MeshAssetDescriptor descriptor))
            {
                throw new InvalidOperationException($"MassNavigation runtime requires configured mesh asset '{meshAssetId}'.");
            }

            if (descriptor.Type != MeshAssetType.Model && descriptor.Type != MeshAssetType.Billboard)
            {
                throw new InvalidOperationException(
                    $"MassNavigation runtime mesh asset '{meshAssetId}' must be a configured Model or Billboard asset, actual={descriptor.Type}.");
            }

            if (descriptor.SourceUris == null || descriptor.SourceUris.Length == 0)
            {
                throw new InvalidOperationException($"MassNavigation runtime mesh asset '{meshAssetId}' must have backend sourceUris from Presentation/host_assets.json.");
            }

            for (int uriIndex = 0; uriIndex < descriptor.SourceUris.Length; uriIndex++)
            {
                if (string.IsNullOrWhiteSpace(descriptor.SourceUris[uriIndex]))
                {
                    throw new InvalidOperationException($"MassNavigation runtime mesh asset '{meshAssetId}' has an empty sourceUri at index {uriIndex}.");
                }
            }
        }
    }
}
