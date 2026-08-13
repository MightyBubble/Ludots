using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class AttrNodeDriver : IGraphOpsNodeDriver
{
    public const string MarkEffectId = "Effect.GraphOpsAttr.Mark";

    private GasGraphRuntimeApi? _api;
    private EffectRequestQueue? _requests;
    private int _healthAttrId;
    private int _markTemplateId;
    private Entity _seededMark;

    public int PendingEffectRequests => _requests?.Count ?? 0;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        EnsureRuntime(ctx);
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        if (ctx.SimActors.Length == 0)
        {
            ctx.SimActors = new Entity[actors.Length];
            ctx.ActorHealth = new float[actors.Length];
            for (int i = 0; i < actors.Length; i++)
            {
                Entity entity = CreateSimActor(ctx, actors[i]);
                ctx.SimActors[i] = entity;
                ctx.ActorHealth[i] = actors[i].Health;
                if (string.Equals(actors[i].Role, "caster", StringComparison.Ordinal))
                {
                    ctx.Caster = entity;
                }
                else if (string.Equals(actors[i].Role, "target", StringComparison.Ordinal))
                {
                    ctx.Target = entity;
                }
            }

            if (ctx.Caster == Entity.Null)
            {
                throw new InvalidOperationException($"Attr vignette {ctx.Vignette.Op} requires a caster actor.");
            }

            ctx.Metrics.AgentCount = actors.Length;
            ctx.Metrics.Detail = ctx.Vignette.Beat;
        }

        if (string.Equals(ctx.Vignette.Op, "RemoveEffectTemplate", StringComparison.Ordinal))
        {
            EnsureMarkOnTarget(ctx);
        }

        SpawnStage(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (_api == null || _requests == null)
        {
            throw new InvalidOperationException(
                $"AttrNodeDriver requires GasGraphRuntimeApi with EffectRequestQueue before Tick. Op={ctx.Vignette.Op}");
        }

        WriteVignetteHealth(ctx);
        if (string.Equals(ctx.Vignette.Op, "RemoveEffectTemplate", StringComparison.Ordinal))
        {
            EnsureMarkOnTarget(ctx);
        }

        _requests.Clear();
        int casterIndex = FindRole(ctx, "caster");
        int targetIndex = FindRole(ctx, "target");
        float targetBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        float casterBefore = casterIndex >= 0 ? ctx.ActorHealth[casterIndex] : 0f;
        GraphOpsNodeExecuteResult result = ExecuteFeatured(ctx);
        SyncActorHealth(ctx);

        float targetAfter = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        float casterAfter = casterIndex >= 0 ? ctx.ActorHealth[casterIndex] : 0f;
        FillCaptions(ctx, result, targetBefore, targetAfter, casterBefore, casterAfter);
        ctx.Metrics.Detail = FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        FailClose(ctx, result, targetBefore, targetAfter, casterBefore, casterAfter);
        SyncStage(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = FindRole(ctx, "caster");
        int target = FindRole(ctx, "target");
        if (caster < 0 || target < 0)
        {
            return;
        }

        GraphShowcaseStagePresenter.DrawAggroLine(
            debugDraw,
            ctx.Vignette.Actors[caster].X,
            ctx.Vignette.Actors[caster].Y,
            ctx.Vignette.Actors[target].X,
            ctx.Vignette.Actors[target].Y);
    }

    private void EnsureRuntime(GraphOpsNodeDriverContext ctx)
    {
        _healthAttrId = AttributeRegistry.Register("Health");
        _markTemplateId = EffectTemplateIdRegistry.Register(MarkEffectId);
        if (_api != null)
        {
            return;
        }

        _requests = new EffectRequestQueue();
        var tagOps = new TagOps(
            new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
            new TagRuleRegistry());
        _api = new GasGraphRuntimeApi(
            ctx.SimWorld,
            spatialQueries: null,
            coords: null,
            eventBus: null,
            effectRequests: _requests,
            tagOps: tagOps);
    }

    private Entity CreateSimActor(GraphOpsNodeDriverContext ctx, GraphOpsNodeActor actor)
    {
        Entity entity = string.Equals(actor.Role, "target", StringComparison.Ordinal)
            ? ctx.SimWorld.Create(new AttributeBuffer(), new DirtyFlags(), new ActiveEffectContainer())
            : ctx.SimWorld.Create(new AttributeBuffer(), new DirtyFlags());
        WriteHealth(ctx.SimWorld, entity, actor.Health, actor.HealthMax);
        return entity;
    }

    private void WriteVignetteHealth(GraphOpsNodeDriverContext ctx)
    {
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            GraphOpsNodeActor actor = ctx.Vignette.Actors[i];
            WriteHealth(ctx.SimWorld, ctx.SimActors[i], actor.Health, actor.HealthMax);
            ctx.ActorHealth[i] = actor.Health;
        }
    }

    private void WriteHealth(World world, Entity entity, float health, float healthMax)
    {
        ref AttributeBuffer attrs = ref world.Get<AttributeBuffer>(entity);
        attrs.SetBase(_healthAttrId, healthMax);
        attrs.SetCurrent(_healthAttrId, health);
    }

    private void SyncActorHealth(GraphOpsNodeDriverContext ctx)
    {
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            ctx.ActorHealth[i] = ctx.SimWorld.Get<AttributeBuffer>(ctx.SimActors[i]).GetCurrent(_healthAttrId);
        }
    }

    private void EnsureMarkOnTarget(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Target == Entity.Null)
        {
            throw new InvalidOperationException("RemoveEffectTemplate requires a target actor.");
        }

        if (_seededMark != Entity.Null &&
            ctx.SimWorld.IsAlive(_seededMark) &&
            ctx.SimWorld.Has<GameplayEffect>(_seededMark) &&
            !ctx.SimWorld.Get<GameplayEffect>(_seededMark).CancelRequested)
        {
            return;
        }

        _seededMark = ctx.SimWorld.Create(
            new GameplayEffect
            {
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.FixedFrame,
                AggregatesModifiers = true
            },
            new EffectTemplateRef { TemplateId = _markTemplateId });
        ref ActiveEffectContainer container = ref ctx.SimWorld.Get<ActiveEffectContainer>(ctx.Target);
        if (!container.Add(_seededMark))
        {
            throw new InvalidOperationException("Failed to attach gallery mark effect for RemoveEffectTemplate.");
        }
    }

    private GraphOpsNodeExecuteResult ExecuteFeatured(GraphOpsNodeDriverContext ctx)
    {
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        var targetList = new GraphTargetList(targets);

        var state = new GraphExecutionState
        {
            World = ctx.SimWorld,
            Caster = ctx.Caster,
            ExplicitTarget = ctx.Target,
            Api = _api!,
            F = floats,
            I = ints,
            B = bools,
            E = entities,
            Targets = targets,
            TargetList = targetList,
            CallStack = callStack,
            RandomSeed = (uint)(0xA5A5A5A5u ^ (uint)ctx.Wave),
            Status = GraphExecutionStatus.Running
        };

        GasGraphOpHandlerTable.Execute(ref state, ctx.Compiled.Program, GasGraphOpHandlerTable.Instance);
        if (state.Status != GraphExecutionStatus.Halted)
        {
            throw new InvalidOperationException(
                $"Featured graph for {ctx.Vignette.Op} ended with status {state.Status}.");
        }

        return new GraphOpsNodeExecuteResult(
            floats[ctx.FeaturedDest],
            ints[ctx.FeaturedDest],
            bools[ctx.FeaturedDest] != 0,
            entities[ctx.FeaturedDest],
            state.ReturnInt,
            targetList.Count);
    }

    private static void FillCaptions(
        GraphOpsNodeDriverContext ctx,
        GraphOpsNodeExecuteResult result,
        float targetBefore,
        float targetAfter,
        float casterBefore,
        float casterAfter)
    {
        ctx.CaptionValues["layers"] = result.IntValue.ToString();
        ctx.CaptionValues["combo"] = result.IntValue.ToString();
        ctx.CaptionValues["hp"] = result.FloatValue.ToString("0");
        ctx.CaptionValues["style"] = result.BoolValue ? "全力" : "轻击";
        ctx.CaptionValues["healthBefore"] = targetBefore.ToString("0");
        ctx.CaptionValues["healthAfter"] = targetAfter.ToString("0");
        ctx.CaptionValues["casterBefore"] = casterBefore.ToString("0");
        ctx.CaptionValues["casterAfter"] = casterAfter.ToString("0");
    }

    private void FailClose(
        GraphOpsNodeDriverContext ctx,
        GraphOpsNodeExecuteResult result,
        float targetBefore,
        float targetAfter,
        float casterBefore,
        float casterAfter)
    {
        string op = ctx.Vignette.Op;
        if (string.Equals(op, "ConstInt", StringComparison.Ordinal) && result.IntValue != 3)
        {
            throw new InvalidOperationException($"ConstInt gallery expected 层数 3, got {result.IntValue}.");
        }

        if (string.Equals(op, "AddInt", StringComparison.Ordinal) && result.IntValue != 3)
        {
            throw new InvalidOperationException($"AddInt gallery expected 连击 3, got {result.IntValue}.");
        }

        if (string.Equals(op, "LoadCaster", StringComparison.Ordinal) && result.EntityValue != ctx.Caster)
        {
            throw new InvalidOperationException("LoadCaster gallery did not resolve the caster.");
        }

        if (string.Equals(op, "LoadExplicitTarget", StringComparison.Ordinal) &&
            (result.EntityValue != ctx.Target || targetAfter >= targetBefore))
        {
            throw new InvalidOperationException("LoadExplicitTarget gallery must point at 木桩 and drop its health.");
        }

        if (string.Equals(op, "LoadContextTarget", StringComparison.Ordinal) &&
            (result.EntityValue != ctx.Target || targetAfter >= targetBefore))
        {
            throw new InvalidOperationException("LoadContextTarget gallery must take 木桩 from the strike context and drop health.");
        }

        if (string.Equals(op, "LoadAttribute", StringComparison.Ordinal) && result.FloatValue <= 0f)
        {
            throw new InvalidOperationException("LoadAttribute gallery read no health.");
        }

        if (string.Equals(op, "LoadSelfAttribute", StringComparison.Ordinal) && result.FloatValue <= 0f)
        {
            throw new InvalidOperationException("LoadSelfAttribute gallery read no caster health.");
        }

        if (string.Equals(op, "CompareLtInt", StringComparison.Ordinal) && !result.BoolValue)
        {
            throw new InvalidOperationException("CompareLtInt gallery seeded below the line must 全力.");
        }

        if (string.Equals(op, "CompareEqInt", StringComparison.Ordinal) && !result.BoolValue)
        {
            throw new InvalidOperationException("CompareEqInt gallery expected 叠满.");
        }

        if (string.Equals(op, "CompareEqEntity", StringComparison.Ordinal) && result.BoolValue)
        {
            throw new InvalidOperationException("CompareEqEntity gallery expected 打的不是自己.");
        }

        if (string.Equals(op, "SelectEntity", StringComparison.Ordinal) && result.EntityValue != ctx.Target)
        {
            throw new InvalidOperationException("SelectEntity gallery expected 打向木桩.");
        }

        if (string.Equals(op, "ModifyAttributeAdd", StringComparison.Ordinal) && targetAfter >= targetBefore)
        {
            throw new InvalidOperationException("ModifyAttributeAdd gallery must actually 扣血.");
        }

        if (string.Equals(op, "WriteSelfAttribute", StringComparison.Ordinal) && casterAfter <= casterBefore)
        {
            throw new InvalidOperationException("WriteSelfAttribute gallery must 回 health on the caster.");
        }

        if (string.Equals(op, "ApplyEffectTemplate", StringComparison.Ordinal) && PendingEffectRequests <= 0)
        {
            throw new InvalidOperationException("ApplyEffectTemplate gallery enqueued zero effect requests.");
        }

        if (string.Equals(op, "RemoveEffectTemplate", StringComparison.Ordinal))
        {
            if (_seededMark == Entity.Null ||
                !ctx.SimWorld.IsAlive(_seededMark) ||
                !ctx.SimWorld.Get<GameplayEffect>(_seededMark).CancelRequested)
            {
                throw new InvalidOperationException("RemoveEffectTemplate gallery did not 卸 the seeded mark.");
            }
        }
    }

    private static string FormatDetail(string template, Dictionary<string, string> values)
    {
        string text = template;
        foreach (KeyValuePair<string, string> pair in values)
        {
            text = text.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        if (text.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Detail template still has unsubstituted placeholders: {text}");
        }

        return text;
    }

    private static int FindRole(GraphOpsNodeDriverContext ctx, string role)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, role, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static void SpawnStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || ctx.StageProxies.Length > 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        ctx.StageProxies = new Entity[actors.Length];
        for (int i = 0; i < actors.Length; i++)
        {
            GraphOpsNodeActor actor = actors[i];
            ctx.StageProxies[i] = ctx.Stage.Spawn(
                actor.Template,
                actor.Name,
                actor.X,
                actor.Y,
                ctx.ActorHealth[i],
                actor.HealthMax);
        }
    }

    private static void SyncStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || ctx.StageProxies.Length == 0)
        {
            return;
        }

        for (int i = 0; i < ctx.StageProxies.Length; i++)
        {
            GraphOpsNodeActor actor = ctx.Vignette.Actors[i];
            ctx.Stage.SetPosition(ctx.StageProxies[i], actor.X, actor.Y);
            ctx.Stage.SetHealth(ctx.StageProxies[i], ctx.ActorHealth[i], actor.HealthMax);
        }
    }

}
