using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphQuery;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Spatial;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class BlackboardNodeDriver : IGraphOpsNodeDriver
{
    public const string PowerKey = "showcase.bb.power";
    public const string StacksKey = "showcase.bb.stacks";
    public const string NamedKey = "showcase.bb.named";
    public const string StrikeEffectId = "Effect.GraphOps.Strike";
    public const float SeedPower = 35f;
    public const int SeedStacks = 4;
    public const float StrikeDamage = 18f;
    public const float MarkHealth = 40f;

    private BlackboardLifecycleGraphApi? _lifecycleApi;
    private Entity _markEffect;
    private int _powerKey;
    private int _stacksKey;
    private int _namedKey;
    private bool _seeded;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        if (!_seeded)
        {
            GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
            for (int i = 0; i < actors.Length; i++)
            {
                BindRole(ctx, actors[i].Role, ctx.SimActors[i]);
            }

            _powerKey = ConfigKeyRegistry.Register(PowerKey);
            _stacksKey = ConfigKeyRegistry.Register(StacksKey);
            _namedKey = ConfigKeyRegistry.Register(NamedKey);
            SeedBlackboard(ctx);
            SeedLifecycle(ctx);
            _seeded = true;
        }

        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        _lifecycleApi?.ResetWave();

        int targetIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        float healthBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        ApplyVisibleResult(ctx, result, targetIndex);

        float healthAfter = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        ctx.CaptionValues["result"] = FormatFeaturedResult(ctx, result);
        ctx.CaptionValues["healthBefore"] = healthBefore.ToString("0");
        ctx.CaptionValues["healthAfter"] = healthAfter.ToString("0");
        ctx.CaptionValues["named"] = ResolveNamed(ctx, result);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        int target = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
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

    private void SeedBlackboard(GraphOpsNodeDriverContext ctx)
    {
        ref BlackboardFloatBuffer floats = ref ctx.SimWorld.Get<BlackboardFloatBuffer>(ctx.Caster);
        ref BlackboardIntBuffer ints = ref ctx.SimWorld.Get<BlackboardIntBuffer>(ctx.Caster);
        ref BlackboardEntityBuffer entities = ref ctx.SimWorld.Get<BlackboardEntityBuffer>(ctx.Caster);

        if (ctx.Vignette.Op is "ReadBlackboardFloat")
        {
            floats.Set(_powerKey, SeedPower);
        }
        else if (ctx.Vignette.Op is "ReadBlackboardInt")
        {
            ints.Set(_stacksKey, SeedStacks);
        }
        else if (ctx.Vignette.Op is "ReadBlackboardEntity")
        {
            if (ctx.Target == Entity.Null)
            {
                throw new InvalidOperationException("ReadBlackboardEntity requires a target actor to seed onto the board.");
            }

            entities.Set(_namedKey, ctx.Target);
        }
    }

    private void SeedLifecycle(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Vignette.Op is not ("BeginLifecycleTransaction" or "InvokeBuiltin"))
        {
            return;
        }

        Entity cleanup = ctx.Target != Entity.Null ? ctx.Target : ctx.Caster;
        _lifecycleApi = new BlackboardLifecycleGraphApi(ctx.SimWorld, cleanup);
        ctx.RuntimeApiOverride = _lifecycleApi;

        if (ctx.Vignette.Op is not "InvokeBuiltin")
        {
            return;
        }

        if (!ctx.SimWorld.Has<ActiveEffectContainer>(cleanup))
        {
            ctx.SimWorld.Add(cleanup, new ActiveEffectContainer());
        }

        _markEffect = ctx.SimWorld.Create(
            new GameplayEffect(),
            new EffectTemplateRef { TemplateId = EffectTemplateIdRegistry.Register("Effect.GraphOps.Mark") });
        ref ActiveEffectContainer container = ref ctx.SimWorld.Get<ActiveEffectContainer>(cleanup);
        if (!container.Add(_markEffect))
        {
            throw new InvalidOperationException("InvokeBuiltin seed failed to attach the mark effect.");
        }
    }

    private void ApplyVisibleResult(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result, int targetIndex)
    {
        switch (ctx.Vignette.Op)
        {
            case "ReadBlackboardFloat":
                RequireNonZeroFloat(ctx, result.FloatValue, "威力");
                SubtractTargetHealth(ctx, targetIndex, result.FloatValue);
                break;
            case "ReadBlackboardInt":
                if (result.IntValue != SeedStacks)
                {
                    throw new InvalidOperationException($"ReadBlackboardInt expected {SeedStacks} stacks, got {result.IntValue}.");
                }

                break;
            case "ReadBlackboardEntity":
                if (result.EntityValue != ctx.Target)
                {
                    throw new InvalidOperationException("ReadBlackboardEntity did not lock the seeded target.");
                }

                break;
            case "WriteBlackboardFloat":
                float writtenPower = ReadFloat(ctx, ctx.Caster, _powerKey);
                RequireNonZeroFloat(ctx, writtenPower, "记下威力");
                SetTargetHealth(ctx, targetIndex, writtenPower);
                break;
            case "WriteBlackboardInt":
                int writtenStacks = ReadInt(ctx, ctx.Caster, _stacksKey);
                if (writtenStacks != SeedStacks)
                {
                    throw new InvalidOperationException($"WriteBlackboardInt expected {SeedStacks}, got {writtenStacks}.");
                }

                break;
            case "WriteBlackboardEntity":
                if (!ctx.Api.TryReadBlackboardEntity(ctx.Caster, _namedKey, out Entity named) || named != ctx.Target)
                {
                    throw new InvalidOperationException("WriteBlackboardEntity did not store the target on the board.");
                }

                break;
            case "LoadConfigFloat":
                RequireNonZeroFloat(ctx, result.FloatValue, "配置威力");
                SubtractTargetHealth(ctx, targetIndex, result.FloatValue);
                break;
            case "LoadConfigInt":
                if (result.IntValue == 0)
                {
                    throw new InvalidOperationException("LoadConfigInt returned 0; effect template config must bind a non-zero 阶位.");
                }

                break;
            case "LoadConfigEffectId":
                if (result.IntValue == 0)
                {
                    throw new InvalidOperationException("LoadConfigEffectId returned 0; effect template must point at a real effect.");
                }

                SubtractTargetHealth(ctx, targetIndex, StrikeDamage);
                break;
            case "LoadContextSource":
                if (result.EntityValue != ctx.Caster)
                {
                    throw new InvalidOperationException("LoadContextSource did not return the caster.");
                }

                break;
            case "LoadContextTargetContext":
                if (ctx.TargetContext == Entity.Null || result.EntityValue != ctx.TargetContext)
                {
                    throw new InvalidOperationException("LoadContextTargetContext did not return the seeded context actor.");
                }

                break;
            case "BeginLifecycleTransaction":
                if (_lifecycleApi == null || !_lifecycleApi.TransactionOpen)
                {
                    throw new InvalidOperationException("BeginLifecycleTransaction did not open a transaction.");
                }

                break;
            case "InvokeBuiltin":
                if (_lifecycleApi == null || !_lifecycleApi.TransactionOpen || _lifecycleApi.BuiltinInvocations <= 0)
                {
                    throw new InvalidOperationException("InvokeBuiltin did not run inside an open lifecycle transaction.");
                }

                if (ctx.SimWorld.IsAlive(_markEffect))
                {
                    throw new InvalidOperationException("InvokeBuiltin ClearActiveEffects left the mark effect alive.");
                }

                int markIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "mark");
                if (markIndex >= 0)
                {
                    ctx.ActorHealth[markIndex] = 0f;
                }

                break;
        }
    }

    private string FormatFeaturedResult(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        return ctx.Vignette.Op switch
        {
            "ReadBlackboardFloat" or "LoadConfigFloat" => result.FloatValue.ToString("0.#"),
            "WriteBlackboardFloat" => ReadFloat(ctx, ctx.Caster, _powerKey).ToString("0.#"),
            "ReadBlackboardInt" or "LoadConfigInt" or "LoadConfigEffectId" => result.IntValue.ToString(),
            "WriteBlackboardInt" => ReadInt(ctx, ctx.Caster, _stacksKey).ToString(),
            _ => "1"
        };
    }

    private static string ResolveNamed(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        Entity named = result.EntityValue;
        if (named == Entity.Null)
        {
            named = ctx.TargetContext != Entity.Null ? ctx.TargetContext : ctx.Target;
        }

        int index = GraphOpsNodeActorBinding.IndexOf(ctx, named);
        return index >= 0 ? ctx.Vignette.Actors[index].Name : "木桩";
    }

    private static void BindRole(GraphOpsNodeDriverContext ctx, string role, Entity entity)
    {
        if (string.Equals(role, "caster", StringComparison.Ordinal))
        {
            ctx.Caster = entity;
        }
        else if (string.Equals(role, "target", StringComparison.Ordinal))
        {
            ctx.Target = entity;
        }
        else if (string.Equals(role, "context", StringComparison.Ordinal))
        {
            ctx.TargetContext = entity;
        }
    }

    private static void SubtractTargetHealth(GraphOpsNodeDriverContext ctx, int targetIndex, float amount)
    {
        if (targetIndex < 0)
        {
            throw new InvalidOperationException($"Blackboard vignette {ctx.Vignette.Op} requires a target actor for health visibility.");
        }

        float opening = ctx.Vignette.Actors[targetIndex].Health;
        float next = ctx.ActorHealth[targetIndex] - amount;
        if (next <= 0f)
        {
            next = opening;
        }

        GraphOpsNodeActorBinding.WriteHealth(
            ctx.SimWorld,
            ctx.SimActors[targetIndex],
            next,
            ctx.Vignette.Actors[targetIndex].HealthMax);
        ctx.ActorHealth[targetIndex] = next;
    }

    private static void SetTargetHealth(GraphOpsNodeDriverContext ctx, int targetIndex, float value)
    {
        if (targetIndex < 0)
        {
            throw new InvalidOperationException($"Blackboard vignette {ctx.Vignette.Op} requires a target actor for health visibility.");
        }

        GraphOpsNodeActorBinding.WriteHealth(
            ctx.SimWorld,
            ctx.SimActors[targetIndex],
            value,
            ctx.Vignette.Actors[targetIndex].HealthMax);
        ctx.ActorHealth[targetIndex] = value;
    }

    private static float ReadFloat(GraphOpsNodeDriverContext ctx, Entity entity, int key)
    {
        return ctx.Api.TryReadBlackboardFloat(entity, key, out float value) ? value : 0f;
    }

    private static int ReadInt(GraphOpsNodeDriverContext ctx, Entity entity, int key)
    {
        return ctx.Api.TryReadBlackboardInt(entity, key, out int value) ? value : 0;
    }

    private static void RequireNonZeroFloat(GraphOpsNodeDriverContext ctx, float value, string label)
    {
        if (value == 0f)
        {
            throw new InvalidOperationException($"{ctx.Vignette.Op} returned 0 for {label}; missing config or unpatched blackboard key.");
        }
    }
}

internal sealed class BlackboardLifecycleGraphApi : IGraphRuntimeApi
{
    private readonly World _world;
    private readonly Entity _cleanupTarget;

    public BlackboardLifecycleGraphApi(World world, Entity cleanupTarget)
    {
        _world = world;
        _cleanupTarget = cleanupTarget;
    }

    public bool TransactionOpen { get; private set; }
    public int TransactionStarts { get; private set; }
    public int BuiltinInvocations { get; private set; }
    public int LastBuiltinHandlerId { get; private set; }

    public void ResetWave()
    {
        TransactionOpen = false;
    }

    public void BeginLifecycleTransaction()
    {
        TransactionOpen = true;
        TransactionStarts++;
    }

    public void InvokeBuiltin(int builtinHandlerId)
    {
        if (!TransactionOpen)
        {
            throw new InvalidOperationException("InvokeBuiltin requires an open lifecycle transaction.");
        }

        BuiltinInvocations++;
        LastBuiltinHandlerId = builtinHandlerId;
        if (builtinHandlerId == (int)BuiltinHandlerId.ClearActiveEffects)
        {
            EntityLifecycleAtomicOps.ClearActiveEffects(_world, _cleanupTarget);
            return;
        }

        if (builtinHandlerId == (int)BuiltinHandlerId.TransferStableId)
        {
            return;
        }

        throw new InvalidOperationException($"Blackboard gallery InvokeBuiltin does not handle handler id {builtinHandlerId}.");
    }

    public bool TryGetGridPos(Entity entity, out IntVector2 gridPos)
    {
        gridPos = default;
        throw new InvalidOperationException("Lifecycle gallery adapter does not answer grid queries.");
    }

    public bool HasTag(Entity entity, int tagId)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not answer tags.");

    public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value)
    {
        value = 0f;
        throw new InvalidOperationException("Lifecycle gallery adapter does not read attributes.");
    }

    public SpatialQueryResult QueryRadius(IntVector2 centerCm, float radiusCm, Span<Entity> buffer)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not run spatial queries.");

    public SpatialQueryResult QueryCone(IntVector2 originCm, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not run spatial queries.");

    public SpatialQueryResult QueryRectangle(IntVector2 centerCm, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not run spatial queries.");

    public SpatialQueryResult QueryLine(IntVector2 originCm, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not run spatial queries.");

    public SpatialQueryResult QueryHexRange(IntVector2 centerCm, int hexRadius, Span<Entity> buffer)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not run spatial queries.");

    public SpatialQueryResult QueryHexRing(IntVector2 centerCm, int hexRadius, Span<Entity> buffer)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not run spatial queries.");

    public SpatialQueryResult QueryHexNeighbors(IntVector2 centerCm, Span<Entity> buffer)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not run spatial queries.");

    public int GetTeamId(Entity entity)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not answer teams.");

    public uint GetEntityLayerCategory(Entity entity)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not answer layers.");

    public int GetRelationship(int teamA, int teamB)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not answer team relationships.");

    public void ApplyEffectTemplate(Entity caster, Entity target, int templateId)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not apply effects.");

    public void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not apply effects.");

    public void RemoveEffectTemplate(Entity target, int templateId)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not remove effects.");

    public void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not modify attributes.");

    public void ModifyAttributeSet(Entity caster, Entity target, int attributeId, float value)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not modify attributes.");

    public void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not send events.");

    public bool TryReadBlackboardFloat(Entity entity, int keyId, out float value)
    {
        value = 0f;
        throw new InvalidOperationException("Lifecycle gallery adapter does not read blackboard.");
    }

    public bool TryReadBlackboardInt(Entity entity, int keyId, out int value)
    {
        value = 0;
        throw new InvalidOperationException("Lifecycle gallery adapter does not read blackboard.");
    }

    public bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value)
    {
        value = default;
        throw new InvalidOperationException("Lifecycle gallery adapter does not read blackboard.");
    }

    public void WriteBlackboardFloat(Entity entity, int keyId, float value)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not write blackboard.");

    public void WriteBlackboardInt(Entity entity, int keyId, int value)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not write blackboard.");

    public void WriteBlackboardEntity(Entity entity, int keyId, Entity value)
        => throw new InvalidOperationException("Lifecycle gallery adapter does not write blackboard.");

    public bool TryLoadConfigFloat(int keyId, out float value)
    {
        value = 0f;
        throw new InvalidOperationException("Lifecycle gallery adapter does not load config.");
    }

    public bool TryLoadConfigInt(int keyId, out int value)
    {
        value = 0;
        throw new InvalidOperationException("Lifecycle gallery adapter does not load config.");
    }

    public bool TrySnapTargetToNearestInCollection(
        Entity owner,
        int collectionKeyId,
        ref IntVector2 targetPosCm,
        float maxDistanceCm,
        out Entity snappedEntity)
    {
        snappedEntity = Entity.Null;
        throw new InvalidOperationException("Lifecycle gallery adapter does not snap.");
    }

    public bool TrySnapTargetToNearestGraphEdge(
        ref IntVector2 targetPosCm,
        float searchRadiusCm,
        out GraphEdgeProjection projection)
    {
        projection = default;
        throw new InvalidOperationException("Lifecycle gallery adapter does not snap.");
    }
}
