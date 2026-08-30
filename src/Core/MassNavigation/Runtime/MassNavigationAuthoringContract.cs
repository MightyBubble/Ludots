using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.MassNavigation.Runtime;

internal sealed class MassNavigationAuthoringContract
{
    private readonly Dictionary<string, EntityTemplate> _templates;
    private readonly EntityTemplateKeyRegistry? _templateKeys;
    private readonly PresenterDefinitionRegistry? _presenters;
    private readonly MeshAssetRegistry? _meshAssets;
    private readonly MassNavigationConfig _config;

    private MassNavigationAuthoringContract(
        Dictionary<string, EntityTemplate> templates,
        EntityTemplateKeyRegistry? templateKeys,
        PresenterDefinitionRegistry? presenters,
        MeshAssetRegistry? meshAssets,
        MassNavigationConfig config)
    {
        _templates = templates;
        _templateKeys = templateKeys;
        _presenters = presenters;
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

        if (!config.ScenarioRuntime.AutoSpawnConfiguredScenario)
        {
            return new MassNavigationAuthoringContract(
                new Dictionary<string, EntityTemplate>(StringComparer.Ordinal),
                templateKeys: null,
                presenters: null,
                meshAssets: null,
                config);
        }

        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("MassNavigation runtime auto-spawn requires EntityTemplateKeyRegistry.");
        PresenterDefinitionRegistry presenters = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("MassNavigation runtime auto-spawn requires PresenterDefinitionRegistry.");
        MeshAssetRegistry meshAssets = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
            ?? throw new InvalidOperationException("MassNavigation runtime auto-spawn requires PresentationMeshAssetRegistry.");
        IContinuousHeightmap continuousHeightmap = engine.GetService(CoreServiceKeys.ContinuousHeightmap)
            ?? throw new InvalidOperationException("MassNavigation runtime auto-spawn requires a map-owned ContinuousHeightmapAsset bound through CoreServiceKeys.ContinuousHeightmap.");
        if (continuousHeightmap is not IContinuousHeightmapRenderSource)
        {
            throw new InvalidOperationException("MassNavigation runtime auto-spawn requires ContinuousHeightmap to implement IContinuousHeightmapRenderSource so the large-world terrain is visible.");
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

        var contract = new MassNavigationAuthoringContract(templates, templateKeys, presenters, meshAssets, config);
        contract.ValidateAll();
        return contract;
    }

    public int RequireTemplateKey(string templateId)
    {
        EntityTemplateKeyRegistry templateKeys = _templateKeys
            ?? throw new InvalidOperationException("MassNavigation runtime requires EntityTemplateKeyRegistry before validating auto-spawn templates.");
        if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
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

        ValidatePresenter(_config.Presentation.BlockerPresenterId);
        ValidatePresenter(_config.Presentation.HotspotPresenterId);
        ValidateTemplate(_config.Presentation.BlockerTemplateId);
        ValidateTemplate(_config.Presentation.HotspotTemplateId);

        for (int i = 0; i < _config.Presentation.Teams.Length; i++)
        {
            MassNavigationTeamPresentationConfig team = _config.Presentation.Teams[i];
            ValidateTemplate(team.LightTemplateId);
            ValidateTemplate(team.HeavyTemplateId);
            MassNavigationTemplateLayerResolver.RequireAgentLayer(_templates[team.LightTemplateId], team.LightTemplateId);
            MassNavigationTemplateLayerResolver.RequireAgentLayer(_templates[team.HeavyTemplateId], team.HeavyTemplateId);
            ValidatePresenter(team.LightPresenterId);
            ValidatePresenter(team.HeavyPresenterId);
        }
    }

    private void ValidatePresenter(string presenterId)
    {
        if (string.IsNullOrWhiteSpace(presenterId))
        {
            throw new InvalidOperationException("MassNavigation runtime presenter id must be non-empty.");
        }

        PresenterDefinitionRegistry presenters = _presenters
            ?? throw new InvalidOperationException("MassNavigation runtime requires PresenterDefinitionRegistry before validating auto-spawn presenters.");
        int presenterDefinitionId = presenters.GetId(presenterId);
        if (presenterDefinitionId <= 0 || !presenters.TryGet(presenterDefinitionId, out _))
        {
            throw new InvalidOperationException($"MassNavigation runtime requires configured presenter definition '{presenterId}'.");
        }
    }

    private void ValidateRequiredMeshAssets()
    {
        for (int i = 0; i < _config.Presentation.RequiredMeshAssetIds.Length; i++)
        {
            string meshAssetId = _config.Presentation.RequiredMeshAssetIds[i];
            MeshAssetRegistry meshAssets = _meshAssets
                ?? throw new InvalidOperationException("MassNavigation runtime requires PresentationMeshAssetRegistry before validating auto-spawn mesh assets.");
            int runtimeId = meshAssets.GetId(meshAssetId);
            if (runtimeId <= 0 ||
                !meshAssets.TryGetDescriptor(runtimeId, out MeshAssetDescriptor descriptor))
            {
                throw new InvalidOperationException($"MassNavigation runtime requires configured mesh asset '{meshAssetId}'.");
            }

            if (descriptor.Type != MeshAssetType.Model && descriptor.Type != MeshAssetType.Billboard)
            {
                throw new InvalidOperationException(
                    $"MassNavigation runtime mesh asset '{meshAssetId}' must be a configured Model or Billboard asset, actual={descriptor.Type}.");
            }
        }
    }
}
