using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;

namespace MassNavWebParityMod.Runtime;

internal sealed class MassNavAuthoringContract
{
    private readonly Dictionary<string, EntityTemplate> _templates;
    private readonly EntityTemplateKeyRegistry _templateKeys;
    private readonly PerformerDefinitionRegistry _performers;
    private readonly MassNavWebParityConfig _config;

    private MassNavAuthoringContract(
        Dictionary<string, EntityTemplate> templates,
        EntityTemplateKeyRegistry templateKeys,
        PerformerDefinitionRegistry performers,
        MassNavWebParityConfig config)
    {
        _templates = templates;
        _templateKeys = templateKeys;
        _performers = performers;
        _config = config;
    }

    public IReadOnlyDictionary<string, EntityTemplate> Templates => _templates;

    public static MassNavAuthoringContract Require(GameEngine engine, MassNavWebParityConfig config)
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
            ?? throw new InvalidOperationException("MassNavWebParityMod requires EntityTemplateKeyRegistry.");
        PerformerDefinitionRegistry performers = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
            ?? throw new InvalidOperationException("MassNavWebParityMod requires PerformerDefinitionRegistry.");

        var templates = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
        foreach (EntityTemplate template in engine.MapLoader.TemplateRegistry.GetAll())
        {
            if (template == null || string.IsNullOrWhiteSpace(template.Id))
            {
                continue;
            }

            templates[template.Id] = template;
        }

        var contract = new MassNavAuthoringContract(templates, templateKeys, performers, config);
        contract.ValidateAll();
        return contract;
    }

    public int RequireTemplateKey(string templateId)
    {
        if (!_templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            throw new InvalidOperationException($"MassNavWebParityMod template '{templateId}' was not registered in EntityTemplateKeyRegistry.");
        }

        return templateKeyId;
    }

    public void ValidateTemplate(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException("MassNavWebParityMod template id must be non-empty.");
        }

        if (!_templates.ContainsKey(templateId))
        {
            throw new InvalidOperationException($"MassNavWebParityMod requires configured entity template '{templateId}'.");
        }

        RequireTemplateKey(templateId);
    }

    private void ValidateAll()
    {
        ValidatePerformer(_config.Presentation.BlockerPerformerId);
        ValidatePerformer(_config.Presentation.HotspotPerformerId);
        ValidateTemplate(_config.Presentation.BlockerTemplateId);
        ValidateTemplate(_config.Presentation.HotspotTemplateId);

        for (int i = 0; i < _config.Presentation.Teams.Length; i++)
        {
            MassNavTeamPresentationConfig team = _config.Presentation.Teams[i];
            ValidateTemplate(team.LightTemplateId);
            ValidateTemplate(team.HeavyTemplateId);
            ValidatePerformer(team.LightPerformerId);
            ValidatePerformer(team.HeavyPerformerId);
        }
    }

    private void ValidatePerformer(string performerId)
    {
        if (string.IsNullOrWhiteSpace(performerId))
        {
            throw new InvalidOperationException("MassNavWebParityMod performer id must be non-empty.");
        }

        int performerDefinitionId = _performers.GetId(performerId);
        if (performerDefinitionId <= 0 || !_performers.TryGet(performerDefinitionId, out _))
        {
            throw new InvalidOperationException($"MassNavWebParityMod requires configured performer definition '{performerId}'.");
        }
    }
}
