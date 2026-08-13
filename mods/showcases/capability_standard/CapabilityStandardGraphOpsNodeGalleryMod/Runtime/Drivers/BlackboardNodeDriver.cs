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
    public const string ConfigPowerKey = "showcase.config.power";
    public const string ConfigTierKey = "showcase.config.tier";
    public const string ConfigChainKey = "showcase.config.chainEffect";
    public const string StrikeEffectId = "Effect.GraphOps.Strike";
    public const float SeedPower = 35f;
    public const int SeedStacks = 4;
    public const float ConfigPower = 40f;
    public const int ConfigTier = 2;
    public const float StrikeDamage = 18f;
    public const float MarkHealth = 40f;

    private BlackboardLifecycleGraphApi? _lifecycleApi;
    private Entity _markEffect;
    private int _powerKey;
    private int _stacksKey;
    private int _namedKey;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        if (ctx.SimActors.Length == 0)
        {
            ctx.SimActors = new Entity[actors.Length];
            ctx.ActorHealth = new float[actors.Length];
            for (int i = 0; i < actors.Length; i++)
            {
                Entity entity = ctx.SimWorld.Create(
                    new BlackboardFloatBuffer(),
                    new BlackboardIntBuffer(),
                    new BlackboardEntityBuffer());
                ctx.SimActors[i] = entity;
                ctx.ActorHealth[i] = actors[i].Health;
                BindRole(ctx, actors[i].Role, entity);
            }

            if (ctx.Caster == Entity.Null)
            {
                throw new InvalidOperationException($"Blackboard vignette {ctx.Vignette.Op} requires a caster actor.");
            }

            _powerKey = ConfigKeyRegistry.Register(PowerKey);
            _stacksKey = ConfigKeyRegistry.Register(StacksKey);
            _namedKey = ConfigKeyRegistry.Register(NamedKey);
            SeedBlackboard(ctx);
            SeedLifecycle(ctx);
            BindConfig(ctx);
            ctx.Metrics.AgentCount = actors.Length;
            ctx.Metrics.Detail = ctx.Vignette.Beat;
        }

        SpawnStage(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        BindConfig(ctx);
        _lifecycleApi?.ResetWave();

        int targetIndex = FindRole(ctx, "target");
        float healthBefore = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        ApplyVisibleResult(ctx, result, targetIndex);

        float healthAfter = targetIndex >= 0 ? ctx.ActorHealth[targetIndex] : 0f;
        ctx.CaptionValues["result"] = FormatFeaturedResult(ctx, result);
        ctx.CaptionValues["healthBefore"] = healthBefore.ToString("0");
        ctx.CaptionValues["healthAfter"] = healthAfter.ToString("0");
        ctx.CaptionValues["named"] = ResolveNamed(ctx, result);
        ctx.Metrics.Detail = FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
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

    private static void BindConfig(GraphOpsNodeDriverContext ctx)
    {
        var config = new EffectConfigParams();
        if (!config.TryAddFloat(ConfigKeyRegistry.Register(ConfigPowerKey), ConfigPower) ||
            !config.TryAddInt(ConfigKeyRegistry.Register(ConfigTierKey), ConfigTier) ||
            !config.TryAddEffectTemplateId(
                ConfigKeyRegistry.Register(ConfigChainKey),
                EffectTemplateIdRegistry.Register(StrikeEffectId)))
        {
            throw new InvalidOperationException("Blackboard gallery failed to bind effect-template config params.");
        }

        ctx.Api.SetConfigContext(in config);
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

                int markIndex = FindRole(ctx, "mark");
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

        int index = IndexOfEntity(ctx, named);
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
        ctx.ActorHealth[targetIndex] = next <= 0f ? opening : next;
    }

    private static void SetTargetHealth(GraphOpsNodeDriverContext ctx, int targetIndex, float value)
    {
        if (targetIndex < 0)
        {
            throw new InvalidOperationException($"Blackboard vignette {ctx.Vignette.Op} requires a target actor for health visibility.");
        }

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

    private static int IndexOfEntity(GraphOpsNodeDriverContext ctx, Entity entity)
    {
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            if (ctx.SimActors[i] == entity)
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
        return false;
    }

    public bool HasTag(Entity entity, int tagId) => false;

    public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value)
    {
        value = 0f;
        return false;
    }

    public SpatialQueryResult QueryRadius(IntVector2 centerCm, float radiusCm, Span<Entity> buffer) => new(0, 0);
    public SpatialQueryResult QueryCone(IntVector2 originCm, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer) => new(0, 0);
    public SpatialQueryResult QueryRectangle(IntVector2 centerCm, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => new(0, 0);
    public SpatialQueryResult QueryLine(IntVector2 originCm, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => new(0, 0);
    public SpatialQueryResult QueryHexRange(IntVector2 centerCm, int hexRadius, Span<Entity> buffer) => new(0, 0);
    public SpatialQueryResult QueryHexRing(IntVector2 centerCm, int hexRadius, Span<Entity> buffer) => new(0, 0);
    public SpatialQueryResult QueryHexNeighbors(IntVector2 centerCm, Span<Entity> buffer) => new(0, 0);
    public int GetTeamId(Entity entity) => 0;
    public uint GetEntityLayerCategory(Entity entity) => 0u;
    public int GetRelationship(int teamA, int teamB) => GraphRelationship.Neutral;
    public void ApplyEffectTemplate(Entity caster, Entity target, int templateId) { }
    public void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args) { }
    public void RemoveEffectTemplate(Entity target, int templateId) { }
    public void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta) { }
    public void ModifyAttributeSet(Entity caster, Entity target, int attributeId, float value) { }
    public void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude) { }

    public bool TryReadBlackboardFloat(Entity entity, int keyId, out float value)
    {
        value = 0f;
        return false;
    }

    public bool TryReadBlackboardInt(Entity entity, int keyId, out int value)
    {
        value = 0;
        return false;
    }

    public bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value)
    {
        value = default;
        return false;
    }

    public void WriteBlackboardFloat(Entity entity, int keyId, float value) { }
    public void WriteBlackboardInt(Entity entity, int keyId, int value) { }
    public void WriteBlackboardEntity(Entity entity, int keyId, Entity value) { }

    public bool TryLoadConfigFloat(int keyId, out float value)
    {
        value = 0f;
        return false;
    }

    public bool TryLoadConfigInt(int keyId, out int value)
    {
        value = 0;
        return false;
    }

    public bool TrySnapTargetToNearestInCollection(
        Entity owner,
        int collectionKeyId,
        ref IntVector2 targetPosCm,
        float maxDistanceCm,
        out Entity snappedEntity)
    {
        snappedEntity = Entity.Null;
        return false;
    }

    public bool TrySnapTargetToNearestGraphEdge(
        ref IntVector2 targetPosCm,
        float searchRadiusCm,
        out GraphEdgeProjection projection)
    {
        projection = default;
        return false;
    }
}
