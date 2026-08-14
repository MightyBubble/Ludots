using Arch.Core;
using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
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
        float fullScore = ExecuteOfficialScore(world, caster, fullDummy);
        float woundedScore = ExecuteOfficialScore(world, caster, woundedDummy);
        string chosen = world.Has<UtilityAiDecisionTrace>(caster)
            && world.Get<UtilityAiDecisionTrace>(caster).BestTarget == woundedDummy
            ? GraphScoreShowcaseContract.WoundedDummyName
            : world.Has<UtilityAiDecisionTrace>(caster)
                && world.Get<UtilityAiDecisionTrace>(caster).BestTarget == fullDummy
                ? GraphScoreShowcaseContract.FullDummyName
                : "还在打分";

        GraphShowcaseStagePresenter.DrawPlayerCaption(
            _overlay,
            GraphScoreShowcaseContract.PlayerTitle,
            $"{GraphScoreShowcaseContract.FullDummyName} 血 {fullHealth:0} 分 {fullScore:0}；" +
            $"{GraphScoreShowcaseContract.WoundedDummyName} 血 {woundedHealth:0} 分 {woundedScore:0}；" +
            $"这一刀打向{chosen}");
    }

    private float ExecuteOfficialScore(World world, Entity caster, Entity target)
    {
        GraphProgramRegistry graphs = _engine.GetService(CoreServiceKeys.GraphProgramRegistry)
            ?? throw new InvalidOperationException("残血打分短剧需要已登记的打分图。");
        GasGraphRuntimeApi api = _engine.GetService(CoreServiceKeys.GasGraphRuntimeApi)
            ?? throw new InvalidOperationException("残血打分短剧需要正式图执行接口。");
        int graphId = GraphIdRegistry.GetId(GraphScoreShowcaseContract.GraphKey);
        if (graphId <= 0)
        {
            throw new InvalidOperationException(
                $"打分图 '{GraphScoreShowcaseContract.GraphKey}' 没有登记，不能用空分糊弄过去。");
        }

        if (!graphs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program))
        {
            throw new InvalidOperationException(
                $"打分图 '{GraphScoreShowcaseContract.GraphKey}' 没有可执行程序。");
        }

        GraphKind kind = graphs.RequireKind(graphId, GraphKind.Score);
        return GraphExecutor.ExecuteScore(world, caster, target, default, program, api, kind);
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
