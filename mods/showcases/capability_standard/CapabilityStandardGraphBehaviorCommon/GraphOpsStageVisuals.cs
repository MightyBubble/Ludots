using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphBehaviorCommon;

public static class GraphOpsVisualTemplates
{
    public const string Caster = "GraphOps.Caster";
    public const string Ally = "GraphOps.Ally";
    public const string Target = "GraphOps.Target";
    public const string Soldier = "Unit.Soldier";
    public const string Scout = "Unit.Scout";
}

public sealed class GraphOpsStageVisuals
{
    public const float HealthCeiling = 150f;
    public const int LocalViewerPlayerId = 1;

    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly EntityTemplateKeyRegistry _templates;
    private readonly PresentationStableIdAllocator _stableIds;
    private readonly PerformerEntitySpawnBootstrap _bootstrap;
    private readonly KnowledgeProjectionStore _knowledge;
    private readonly TagOps _tagOps;
    private readonly int _healthAttrId;
    private Entity _viewer;

    private GraphOpsStageVisuals(
        GameEngine engine,
        World world,
        EntityTemplateKeyRegistry templates,
        PresentationStableIdAllocator stableIds,
        PerformerEntitySpawnBootstrap bootstrap,
        KnowledgeProjectionStore knowledge,
        TagOps tagOps,
        int healthAttrId)
    {
        _engine = engine;
        _world = world;
        _templates = templates;
        _stableIds = stableIds;
        _bootstrap = bootstrap;
        _knowledge = knowledge;
        _tagOps = tagOps;
        _healthAttrId = healthAttrId;
    }

    public static GraphOpsStageVisuals FromEngine(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        EntityTemplateKeyRegistry templates = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("GraphOps HUD requires EntityTemplateKeyRegistry.");
        PresentationStableIdAllocator stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator)
            ?? throw new InvalidOperationException("GraphOps HUD requires PresentationStableIdAllocator.");
        PerformerEntityRuntime performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
            ?? throw new InvalidOperationException("GraphOps HUD requires PerformerEntityRuntime.");
        PerformerDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
            ?? throw new InvalidOperationException("GraphOps HUD requires PerformerDefinitionRegistry.");
        KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException("GraphOps HUD requires KnowledgeProjectionStore.");
        if (engine.GetService(CoreServiceKeys.KnowledgeProjectionResolver) == null)
        {
            throw new InvalidOperationException("GraphOps HUD requires KnowledgeProjectionResolver.");
        }

        RenderDebugState renderDebug = engine.GetService(CoreServiceKeys.RenderDebugState)
            ?? throw new InvalidOperationException("GraphOps HUD requires RenderDebugState.");
        renderDebug.DrawWorldHudBars = true;
        renderDebug.DrawWorldHudText = true;
        TagOps tagOps = engine.GetService(CoreServiceKeys.TagOps)
            ?? throw new InvalidOperationException("GraphOps HUD requires TagOps.");

        int healthId = AttributeRegistry.GetId("Health");
        if (healthId < 0)
        {
            throw new InvalidOperationException("GraphOps HUD requires the Health attribute.");
        }

        var bootstrap = new PerformerEntitySpawnBootstrap(
            engine.World,
            templates,
            stableIds,
            performers,
            definitions,
            definitions.BootstrapRegistry);
        return new GraphOpsStageVisuals(engine, engine.World, templates, stableIds, bootstrap, knowledge, tagOps, healthId);
    }

    public Entity Spawn(string templateId, string displayName, float xMeters, float yMeters, float health, float healthMax = HealthCeiling)
    {
        int templateKeyId = _templates.GetId(templateId);
        if (templateKeyId <= 0)
        {
            throw new InvalidOperationException($"GraphOps HUD template '{templateId}' is not in EntityTemplateKeyRegistry.");
        }

        if (healthMax <= 0f)
        {
            throw new InvalidOperationException($"GraphOps HUD '{displayName}' health max must be positive.");
        }

        Vector3 visual = WorldPlane2D.LogicCmToVisualMeters(xMeters * 100f, yMeters * 100f, heightMeters: 0.9f);
        bool isViewer = _viewer == Entity.Null || !_world.IsAlive(_viewer);
        Entity entity = isViewer
            ? _world.Create(
                new EntityTemplateKeyRef { TemplateKeyId = templateKeyId },
                new PresentationStableId { Value = _stableIds.Allocate() },
                new Name { Value = displayName },
                new PlayerOwner { PlayerId = LocalViewerPlayerId },
                new AttributeBuffer(),
                WorldPositionCm.FromCmFloat(xMeters * 100f, yMeters * 100f),
                new VisualTransform
                {
                    Position = visual,
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One
                },
                new CullState { IsVisible = true, LOD = LODLevel.High })
            : _world.Create(
                new EntityTemplateKeyRef { TemplateKeyId = templateKeyId },
                new PresentationStableId { Value = _stableIds.Allocate() },
                new Name { Value = displayName },
                new AttributeBuffer(),
                WorldPositionCm.FromCmFloat(xMeters * 100f, yMeters * 100f),
                new VisualTransform
                {
                    Position = visual,
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });

        WriteHealth(entity, health, healthMax);
        _bootstrap.TryBootstrap(entity, templateId);
        if (!_world.Has<PerformerRootBootstrapHandled>(entity))
        {
            throw new InvalidOperationException(
                $"GraphOps HUD performer did not bind for template '{templateId}' ({displayName}).");
        }

        if (isViewer)
        {
            BindViewer(entity);
        }

        DiscloseHealth(entity);
        return entity;
    }

    public Entity BindMapEntity(
        Entity entity,
        string templateId,
        string displayName,
        float xMeters,
        float yMeters,
        float health,
        float healthMax,
        bool bindAsViewer)
    {
        if (!_world.IsAlive(entity))
        {
            throw new InvalidOperationException($"GraphOps HUD cannot bind dead map actor '{displayName}'.");
        }

        int templateKeyId = _templates.GetId(templateId);
        if (templateKeyId <= 0)
        {
            throw new InvalidOperationException($"GraphOps HUD template '{templateId}' is not in EntityTemplateKeyRegistry.");
        }

        if (healthMax <= 0f)
        {
            throw new InvalidOperationException($"GraphOps HUD '{displayName}' health max must be positive.");
        }

        if (!_world.Has<EntityTemplateKeyRef>(entity))
        {
            _world.Add(entity, new EntityTemplateKeyRef { TemplateKeyId = templateKeyId });
        }

        if (!_world.Has<PresentationStableId>(entity))
        {
            _world.Add(entity, new PresentationStableId { Value = _stableIds.Allocate() });
        }

        if (!_world.Has<Name>(entity))
        {
            _world.Add(entity, new Name { Value = displayName });
        }
        else
        {
            _world.Get<Name>(entity).Value = displayName;
        }

        Vector3 visual = WorldPlane2D.LogicCmToVisualMeters(xMeters * 100f, yMeters * 100f, heightMeters: 0.9f);
        if (!_world.Has<WorldPositionCm>(entity))
        {
            _world.Add(entity, WorldPositionCm.FromCmFloat(xMeters * 100f, yMeters * 100f));
        }

        if (!_world.Has<VisualTransform>(entity))
        {
            _world.Add(entity, new VisualTransform
            {
                Position = visual,
                Rotation = Quaternion.Identity,
                Scale = Vector3.One
            });
        }

        if (!_world.Has<CullState>(entity))
        {
            _world.Add(entity, new CullState { IsVisible = true, LOD = LODLevel.High });
        }

        if (!_world.Has<AttributeBuffer>(entity))
        {
            _world.Add(entity, new AttributeBuffer());
        }

        WriteHealth(entity, health, healthMax);

        bool isViewer = bindAsViewer && (_viewer == Entity.Null || !_world.IsAlive(_viewer));
        if (isViewer)
        {
            if (!_world.Has<PlayerOwner>(entity))
            {
                _world.Add(entity, new PlayerOwner { PlayerId = LocalViewerPlayerId });
            }

            BindViewer(entity);
        }

        _bootstrap.TryBootstrap(entity, templateId);
        if (!_world.Has<PerformerRootBootstrapHandled>(entity))
        {
            throw new InvalidOperationException(
                $"GraphOps HUD performer did not bind for template '{templateId}' ({displayName}).");
        }

        DiscloseHealth(entity);
        return entity;
    }

    public void SetHealth(Entity entity, float health, float healthMax = HealthCeiling)
    {
        if (!_world.IsAlive(entity))
        {
            throw new InvalidOperationException("GraphOps HUD SetHealth target is not alive.");
        }

        WriteHealth(entity, health, healthMax);
        DiscloseHealth(entity);
    }

    private void WriteHealth(Entity entity, float health, float healthMax)
    {
        if (healthMax <= 0f)
        {
            throw new InvalidOperationException("GraphOps HUD health max must be positive.");
        }

        if (!_world.Has<AttributeBuffer>(entity))
        {
            _world.Add(entity, new AttributeBuffer());
        }

        if (!_world.Has<DirtyFlags>(entity))
        {
            _world.Add(entity, new DirtyFlags());
        }

        AttributeMutationOps.SetBase(_world, entity, _healthAttrId, healthMax, _tagOps);
        AttributeMutationOps.SetCurrent(_world, entity, _healthAttrId, Math.Clamp(health, 0f, healthMax), _tagOps);
    }

    public void SetPosition(Entity entity, float xMeters, float yMeters)
    {
        if (!_world.IsAlive(entity))
        {
            throw new InvalidOperationException("GraphOps HUD SetPosition target is not alive.");
        }

        ref WorldPositionCm pos = ref _world.Get<WorldPositionCm>(entity);
        pos = WorldPositionCm.FromCmFloat(xMeters * 100f, yMeters * 100f);
        ref VisualTransform visual = ref _world.Get<VisualTransform>(entity);
        visual.Position = WorldPlane2D.LogicCmToVisualMeters(xMeters * 100f, yMeters * 100f, heightMeters: 0.9f);
    }

    private void BindViewer(Entity viewer)
    {
        _viewer = viewer;
        _engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = viewer;
        _engine.GlobalContext[CoreServiceKeys.LocalPlayerId.Name] = LocalViewerPlayerId;
        _engine.GlobalContext[CoreServiceKeys.PresentationAudienceRevealHidden.Name] = true;
        _engine.SetService(CoreServiceKeys.LocalPlayerEntity, viewer);
        _engine.SetService(CoreServiceKeys.LocalPlayerId, LocalViewerPlayerId);
        if (_engine.CurrentMapSession != null)
        {
            _engine.CurrentMapSession.LocalPlayerEntity = viewer;
            _engine.CurrentMapSession.LocalPlayerId = LocalViewerPlayerId;
        }
    }

    private void DiscloseHealth(Entity subject)
    {
        if (_viewer == Entity.Null || !_world.IsAlive(_viewer) || !_world.IsAlive(subject))
        {
            throw new InvalidOperationException("GraphOps HUD cannot disclose Health without a live viewer and subject.");
        }

        KnowledgeIdMask256 attributeMask = KnowledgeIdMask256.Empty.WithId(_healthAttrId);
        KnowledgeIdMask256 empty = KnowledgeIdMask256.Empty;
        int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(_engine.GlobalContext);
        _knowledge.Upsert(
            _viewer,
            subject,
            new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                in attributeMask,
                in empty,
                in empty,
                _viewer,
                observedTick,
                expiryTick: 0,
                confidencePermille: 1000,
                revision: 0));
    }
}
