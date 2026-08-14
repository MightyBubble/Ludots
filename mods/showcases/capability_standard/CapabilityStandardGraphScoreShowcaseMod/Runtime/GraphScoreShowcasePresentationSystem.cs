using Arch.Core;
using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Hud;

namespace CapabilityStandardGraphScoreShowcaseMod.Runtime;

internal sealed class GraphScoreShowcasePresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly ScreenOverlayBuffer _overlay;
    private readonly QueryDescription _named = new QueryDescription().WithAll<Name>();

    public GraphScoreShowcasePresentationSystem(GameEngine engine, ScreenOverlayBuffer overlay)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!string.Equals(
                _engine.CurrentMapSession?.MapConfig?.Id,
                GraphScoreShowcaseContract.MapId,
                StringComparison.Ordinal))
        {
            return;
        }

        World world = _engine.World;
        Entity caster = FindNamed(world, GraphScoreShowcaseContract.CasterName);
        Entity fullDummy = FindNamed(world, GraphScoreShowcaseContract.FullDummyName);
        Entity woundedDummy = FindNamed(world, GraphScoreShowcaseContract.WoundedDummyName);
        if (caster == Entity.Null || fullDummy == Entity.Null || woundedDummy == Entity.Null)
        {
            GraphShowcaseStagePresenter.DrawPlayerCaption(
                _overlay,
                GraphScoreShowcaseContract.PlayerTitle,
                "场上还没站齐三个角色，短剧开不了。");
            return;
        }

        float fullHealth = ReadHealth(world, fullDummy);
        float woundedHealth = ReadHealth(world, woundedDummy);
        string detail =
            $"{GraphScoreShowcaseContract.FullDummyName} 血 {fullHealth:0}；" +
            $"{GraphScoreShowcaseContract.WoundedDummyName} 血 {woundedHealth:0}；" +
            ReadDecisionCaption(world, caster, fullDummy, woundedDummy);

        GraphShowcaseStagePresenter.DrawPlayerCaption(
            _overlay,
            GraphScoreShowcaseContract.PlayerTitle,
            detail);
    }

    private static string ReadDecisionCaption(World world, Entity caster, Entity fullDummy, Entity woundedDummy)
    {
        if (!world.Has<UtilityAiDecisionTrace>(caster))
        {
            return "还在打分";
        }

        UtilityAiDecisionTrace trace = world.Get<UtilityAiDecisionTrace>(caster);
        if (trace.BestTarget == woundedDummy)
        {
            return $"这一刀打向{GraphScoreShowcaseContract.WoundedDummyName}（分 {trace.BestScore:0}）";
        }

        if (trace.BestTarget == fullDummy)
        {
            return $"这一刀打向{GraphScoreShowcaseContract.FullDummyName}（分 {trace.BestScore:0}）";
        }

        return "还在打分";
    }

    private static float ReadHealth(World world, Entity entity)
    {
        int healthId = AttributeRegistry.GetId("Health");
        if (healthId < 0 || !world.Has<AttributeBuffer>(entity))
        {
            throw new InvalidOperationException("短剧角色缺少生命值，不能假装打过分。");
        }

        return world.Get<AttributeBuffer>(entity).GetCurrent(healthId);
    }

    private Entity FindNamed(World world, string entityName)
    {
        Entity result = Entity.Null;
        world.Query(in _named, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.Ordinal))
            {
                result = entity;
            }
        });
        return result;
    }
}
