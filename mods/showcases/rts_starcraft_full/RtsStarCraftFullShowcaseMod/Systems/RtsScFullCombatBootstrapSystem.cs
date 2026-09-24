using Arch.Core;
using Arch.System;
using System.Collections.Generic;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Scripting;

namespace RtsStarCraftFullShowcaseMod.Systems;

/// <summary>
/// Applies auto-attack PeriodicSearch buffs to combat units based on entity template id.
/// </summary>
internal sealed class RtsScFullCombatBootstrapSystem : ISystem<float>
{
    private static readonly QueryDescription CombatUnitQuery = new QueryDescription()
        .WithAll<AttributeBuffer, EntityTemplateKeyRef>();

    private readonly GameEngine _engine;
    private readonly EffectRequestQueue _requests;
    private readonly EntityTemplateKeyRegistry _templateKeys;
    private readonly Dictionary<int, int> _autoAttackEffectByTemplateKeyId = new();

    public RtsScFullCombatBootstrapSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _requests = engine.GetService(CoreServiceKeys.EffectRequestQueue) as EffectRequestQueue
            ?? throw new InvalidOperationException("EffectRequestQueue is required.");
        _templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry) as EntityTemplateKeyRegistry
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry is required.");
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!IsStarCraftFullMapActive())
        {
            return;
        }

        World world = _engine.World;
        world.Query(in CombatUnitQuery, (Entity entity, ref AttributeBuffer attributes, ref EntityTemplateKeyRef templateRef) =>
        {
            int effectTemplateId = ResolveAutoAttackEffectTemplateId(templateRef.TemplateKeyId);
            if (effectTemplateId <= 0)
            {
                return;
            }

            if (HasActiveAutoAttack(world, entity, effectTemplateId))
            {
                return;
            }

            _requests.Publish(new EffectRequest
            {
                TemplateId = effectTemplateId,
                Source = entity,
                Target = entity,
                TargetContext = entity,
            });
        });
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    private bool IsStarCraftFullMapActive()
    {
        var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
        return tags != null && tags.Any(static t => t.Contains("starcraft_full", StringComparison.OrdinalIgnoreCase));
    }

    private int ResolveAutoAttackEffectTemplateId(int templateKeyId)
    {
        if (templateKeyId <= 0)
        {
            return 0;
        }

        if (_autoAttackEffectByTemplateKeyId.TryGetValue(templateKeyId, out int cachedEffectTemplateId))
        {
            return cachedEffectTemplateId;
        }

        int effectTemplateId = 0;
        string templateId = _templateKeys.GetName(templateKeyId);
        if (!string.IsNullOrWhiteSpace(templateId) && templateId.StartsWith("scf_", StringComparison.Ordinal))
        {
            string slug = templateId["scf_".Length..].Replace('_', '.');
            int resolved = EffectTemplateIdRegistry.GetId($"Effect.Scf.AutoAttack.{slug}");
            if (resolved != EffectTemplateIdRegistry.InvalidId)
            {
                effectTemplateId = resolved;
            }
        }

        _autoAttackEffectByTemplateKeyId[templateKeyId] = effectTemplateId;
        return effectTemplateId;
    }

    private static bool HasActiveAutoAttack(World world, Entity entity, int effectTemplateId)
    {
        if (!world.IsAlive(entity) || !world.Has<ActiveEffectContainer>(entity))
        {
            return false;
        }

        ref ActiveEffectContainer active = ref world.Get<ActiveEffectContainer>(entity);
        for (int i = 0; i < active.Count; i++)
        {
            Entity effectEntity = active.GetEntity(i);
            if (!world.IsAlive(effectEntity) || !world.Has<EffectContext>(effectEntity))
            {
                continue;
            }

            if (world.Has<EffectTemplateRef>(effectEntity) &&
                world.Get<EffectTemplateRef>(effectEntity).TemplateId == effectTemplateId)
            {
                return true;
            }
        }

        return false;
    }
}
