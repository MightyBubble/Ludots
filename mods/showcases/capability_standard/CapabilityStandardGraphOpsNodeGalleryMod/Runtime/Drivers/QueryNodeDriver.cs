using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class QueryNodeDriver : IGraphOpsNodeDriver
{
    public const int SeededMapEntityCount = 12;
    public const int EnemyTeamId = 2;
    public const int AllyTeamId = 1;
    public const string SquadCollectionKey = "squad.members";
    public const float ResidualHealthMax = 40f;

    private GasGraphRuntimeApi? _queryApi;
    private EntitySetQueryRuntime? _entityQueries;
    private EntityCollectionStore? _collections;
    private Entity _caster;
    private Entity[] _units = Array.Empty<Entity>();
    private Entity[] _stageUnitProxies = Array.Empty<Entity>();
    private Entity _stageCasterProxy;
    private float[] _unitX = Array.Empty<float>();
    private float[] _unitY = Array.Empty<float>();
    private float[] _unitHp = Array.Empty<float>();
    private byte[] _unitInRange = Array.Empty<byte>();
    private string[] _unitLabels = Array.Empty<string>();
    private readonly Entity[] _lastTargets = new Entity[GraphVmLimits.MaxTargets];
    private bool _seeded;
    private bool _visualsSpawned;
    private int _healthAttrId;
    private int _soldierTemplateId;
    private int _scoutTemplateId;
    private int _enemyTagId;
    private int _deadTagId;

    public int LastTargetCount { get; private set; }
    public int StrongestIndex { get; private set; } = -1;
    public int WeakestIndex { get; private set; } = -1;
    public int UnitCount => _units.Length;
    public float CasterX => 0f;
    public float CasterY => -2.2f;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded)
        {
            BindQueryRuntime(ctx);
            SeedMap(ctx);
            ctx.RuntimeApiOverride = _queryApi;
            ctx.Caster = _caster;
            ctx.SimActors = _units;
            ctx.ActorHealth = _unitHp;
            ctx.Metrics.AgentCount = SeededMapEntityCount;
            ctx.Metrics.Detail = ctx.Vignette.Beat;
            _seeded = true;
        }

        SpawnStage(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded || _queryApi == null)
        {
            throw new InvalidOperationException($"Query driver for {ctx.Vignette.Op} must Seed before Tick.");
        }

        GraphOpsNodeExecuteResult result = ExecuteQueryGraph(ctx);
        LastTargetCount = result.TargetCount;
        if (LastTargetCount <= 0)
        {
            throw new InvalidOperationException(
                $"Query gallery '{ctx.Vignette.Op}' returned 0 targets; seed or graph failed closed.");
        }

        MarkInRange(result.TargetCount);
        ResolveExtremes(ctx.Vignette.Op, result);
        FillCaptions(ctx, result);
        ctx.Metrics.Detail = FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        SyncStage(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        for (int i = 0; i < _units.Length; i++)
        {
            if (_unitInRange[i] == 0)
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawActor(
                debugDraw,
                _unitX[i],
                _unitY[i],
                radius: 0.85f,
                GraphShowcaseStagePresenter.SentryAlert,
                thickness: 0.16f);
        }

        if (StrongestIndex >= 0 && StrongestIndex < _units.Length)
        {
            GraphShowcaseStagePresenter.DrawAggroLine(
                debugDraw,
                CasterX,
                CasterY,
                _unitX[StrongestIndex],
                _unitY[StrongestIndex]);
        }

        if (WeakestIndex >= 0 && WeakestIndex < _units.Length && WeakestIndex != StrongestIndex)
        {
            GraphShowcaseStagePresenter.DrawAggroLine(
                debugDraw,
                CasterX,
                CasterY,
                _unitX[WeakestIndex],
                _unitY[WeakestIndex]);
        }
    }

    private void BindQueryRuntime(GraphOpsNodeDriverContext ctx)
    {
        _soldierTemplateId = GraphOpsNodeGallerySymbolResolver.SimTemplates.Register(GraphOpsVisualTemplates.Soldier);
        _scoutTemplateId = GraphOpsNodeGallerySymbolResolver.SimTemplates.Register(GraphOpsVisualTemplates.Scout);
        _healthAttrId = AttributeRegistry.GetId("Health");
        if (_healthAttrId < 0)
        {
            _healthAttrId = AttributeRegistry.Register("Health");
        }

        _enemyTagId = TagRegistry.GetId("Enemy");
        if (_enemyTagId <= 0)
        {
            _enemyTagId = TagRegistry.Register("Enemy");
        }

        _deadTagId = TagRegistry.GetId("Dead");
        if (_deadTagId <= 0)
        {
            _deadTagId = TagRegistry.Register("Dead");
        }

        _collections = GraphOpsNodeGallerySymbolResolver.Collections;

        var tagOps = new TagOps(
            new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
            new TagRuleRegistry(),
            new GasBudget());
        var relationships = new RelationshipRuntime(
            ctx.SimWorld,
            new RelationshipTypeRegistry(),
            new RelationshipMetricRegistry(),
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(ctx.SimWorld));
        _entityQueries = new EntitySetQueryRuntime(ctx.SimWorld, tagOps, relationships);
        _queryApi = new GasGraphRuntimeApi(
            ctx.SimWorld,
            tagOps: tagOps,
            relationshipRuntime: relationships,
            entityQueries: _entityQueries,
            entityCollections: _collections);
    }

    private void SeedMap(GraphOpsNodeDriverContext ctx)
    {
        _caster = ctx.SimWorld.Create();
        _units = new Entity[SeededMapEntityCount];
        _unitX = new float[SeededMapEntityCount];
        _unitY = new float[SeededMapEntityCount];
        _unitHp = new float[SeededMapEntityCount];
        _unitInRange = new byte[SeededMapEntityCount];
        _unitLabels = new string[SeededMapEntityCount];

        SeedUnit(ctx, 0, EnemyTeamId, _soldierTemplateId, 90f, enemy: true, dead: false);
        SeedUnit(ctx, 1, EnemyTeamId, _soldierTemplateId, 70f, enemy: true, dead: false);
        SeedUnit(ctx, 2, EnemyTeamId, _soldierTemplateId, 50f, enemy: true, dead: false);
        SeedUnit(ctx, 3, EnemyTeamId, _soldierTemplateId, 30f, enemy: true, dead: false);
        SeedUnit(ctx, 4, EnemyTeamId, _soldierTemplateId, 10f, enemy: true, dead: false);
        SeedUnit(ctx, 5, EnemyTeamId, _soldierTemplateId, 100f, enemy: true, dead: false);
        SeedUnit(ctx, 6, EnemyTeamId, _soldierTemplateId, 0f, enemy: true, dead: false);
        SeedUnit(ctx, 7, EnemyTeamId, _soldierTemplateId, 150f, enemy: true, dead: false);
        SeedUnit(ctx, 8, EnemyTeamId, _soldierTemplateId, 40f, enemy: true, dead: true);
        SeedUnit(ctx, 9, EnemyTeamId, _scoutTemplateId, 80f, enemy: false, dead: false);
        SeedUnit(ctx, 10, AllyTeamId, _soldierTemplateId, 60f, enemy: false, dead: false);
        SeedUnit(ctx, 11, AllyTeamId, _scoutTemplateId, 20f, enemy: false, dead: false);

        var roster = new Entity[] { _units[0], _units[1], _units[2], _units[9], _units[10], _units[11] };
        var descriptor = EntityCollectionDescriptor.Create(
            SquadCollectionKey,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.Display,
            contextEntity: _caster,
            primaryEntity: _caster,
            title: "花名册",
            summary: "小队成员");
        _collections!.Replace(_caster, descriptor, roster, _caster);
    }

    private void SeedUnit(
        GraphOpsNodeDriverContext ctx,
        int index,
        int teamId,
        int templateId,
        float health,
        bool enemy,
        bool dead)
    {
        int col = index % 6;
        int row = index / 6;
        float x = (col - 2.5f) * 2.2f;
        float y = 1.2f + row * 2.8f;
        int xCm = (int)MathF.Round(x * 100f);
        int yCm = (int)MathF.Round(y * 100f);
        Entity entity = ctx.SimWorld.Create(
            new MapEntity(),
            new Team { Id = teamId },
            new EntityTemplateKeyRef { TemplateKeyId = templateId },
            new AttributeBuffer(),
            new GameplayTagContainer(),
            WorldPositionCm.FromCm(xCm, yCm));
        ref AttributeBuffer attrs = ref ctx.SimWorld.Get<AttributeBuffer>(entity);
        attrs.SetBase(_healthAttrId, health);
        ref GameplayTagContainer tags = ref ctx.SimWorld.Get<GameplayTagContainer>(entity);
        if (enemy)
        {
            tags.AddTag(_enemyTagId);
        }

        if (dead)
        {
            tags.AddTag(_deadTagId);
        }

        _units[index] = entity;
        _unitX[index] = x;
        _unitY[index] = y;
        _unitHp[index] = health;
        _unitLabels[index] = teamId == AllyTeamId ? $"友军{index + 1}" : $"敌军{index + 1}";
    }

    private GraphOpsNodeExecuteResult ExecuteQueryGraph(GraphOpsNodeDriverContext ctx)
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
            Caster = _caster,
            Api = _queryApi!,
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

        int count = state.TargetList.Count;
        ReadOnlySpan<Entity> found = state.TargetList.Span;
        for (int i = 0; i < count; i++)
        {
            _lastTargets[i] = found[i];
        }

        return new GraphOpsNodeExecuteResult(
            floats[ctx.FeaturedDest],
            ints[ctx.FeaturedDest],
            bools[ctx.FeaturedDest] != 0,
            entities[ctx.FeaturedDest],
            state.ReturnInt,
            count);
    }

    private void MarkInRange(int count)
    {
        for (int i = 0; i < _units.Length; i++)
        {
            _unitInRange[i] = 0;
        }

        for (int i = 0; i < count; i++)
        {
            int idx = IndexOf(_lastTargets[i]);
            if (idx >= 0)
            {
                _unitInRange[idx] = 1;
            }
        }
    }

    private void ResolveExtremes(string op, GraphOpsNodeExecuteResult result)
    {
        StrongestIndex = -1;
        WeakestIndex = -1;
        if (string.Equals(op, nameof(GraphNodeOp.AggMaxEntityByAttribute), StringComparison.Ordinal))
        {
            if (result.EntityValue == Entity.Null)
            {
                throw new InvalidOperationException("AggMaxEntityByAttribute did not name an entity.");
            }

            StrongestIndex = RequireIndex(result.EntityValue, op);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.AggMinEntityByAttribute), StringComparison.Ordinal))
        {
            if (result.EntityValue == Entity.Null)
            {
                throw new InvalidOperationException("AggMinEntityByAttribute did not name an entity.");
            }

            WeakestIndex = RequireIndex(result.EntityValue, op);
            return;
        }

        if (string.Equals(op, nameof(GraphNodeOp.QuerySortByAttribute), StringComparison.Ordinal))
        {
            StrongestIndex = RequireIndex(_lastTargets[0], op);
            return;
        }

        ScanInRangeExtremes();
        if (string.Equals(op, nameof(GraphNodeOp.AggMinAttribute), StringComparison.Ordinal))
        {
            StrongestIndex = -1;
        }
        else if (string.Equals(op, nameof(GraphNodeOp.AggMaxAttribute), StringComparison.Ordinal))
        {
            WeakestIndex = -1;
        }
        else if (!IsAggregateValueOp(op))
        {
            StrongestIndex = -1;
            WeakestIndex = -1;
        }
    }

    private void ScanInRangeExtremes()
    {
        float maxHp = float.MinValue;
        float minHp = float.MaxValue;
        for (int i = 0; i < _units.Length; i++)
        {
            if (_unitInRange[i] == 0)
            {
                continue;
            }

            if (_unitHp[i] > maxHp)
            {
                maxHp = _unitHp[i];
                StrongestIndex = i;
            }

            if (_unitHp[i] < minHp)
            {
                minHp = _unitHp[i];
                WeakestIndex = i;
            }
        }
    }

    private void FillCaptions(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        ctx.CaptionValues["count"] = LastTargetCount.ToString();
        ctx.CaptionValues["threshold"] = ResidualHealthMax.ToString("0");
        ctx.CaptionValues["sum"] = result.FloatValue.ToString("0");
        ctx.CaptionValues["avg"] = result.FloatValue.ToString("0");
        ctx.CaptionValues["max"] = result.FloatValue.ToString("0");
        ctx.CaptionValues["min"] = result.FloatValue.ToString("0");

        int named = StrongestIndex >= 0 ? StrongestIndex : WeakestIndex;
        if (named < 0 && LastTargetCount > 0)
        {
            named = IndexOf(_lastTargets[0]);
        }

        ctx.CaptionValues["label"] = named >= 0 ? _unitLabels[named] : "无人";
        ctx.CaptionValues["hp"] = named >= 0 ? _unitHp[named].ToString("0") : "0";
    }

    private void SpawnStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || _visualsSpawned)
        {
            return;
        }

        _stageCasterProxy = ctx.Stage.Spawn(
            GraphOpsVisualTemplates.Caster,
            "指挥",
            CasterX,
            CasterY,
            100f,
            100f);
        _stageUnitProxies = new Entity[_units.Length];
        ctx.StageProxies = new Entity[_units.Length + 1];
        ctx.StageProxies[0] = _stageCasterProxy;
        for (int i = 0; i < _units.Length; i++)
        {
            _stageUnitProxies[i] = ctx.Stage.Spawn(
                VisualTemplateForUnit(i),
                _unitLabels[i],
                _unitX[i],
                _unitY[i],
                _unitHp[i]);
            ctx.StageProxies[i + 1] = _stageUnitProxies[i];
        }

        _visualsSpawned = true;
    }

    private void SyncStage(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || !_visualsSpawned)
        {
            return;
        }

        ctx.Stage.SetHealth(_stageCasterProxy, 100f, 100f);
        for (int i = 0; i < _units.Length; i++)
        {
            float shown = _unitInRange[i] != 0 ? _unitHp[i] : 0f;
            ctx.Stage.SetPosition(_stageUnitProxies[i], _unitX[i], _unitY[i]);
            ctx.Stage.SetHealth(_stageUnitProxies[i], shown);
        }
    }

    private static string VisualTemplateForUnit(int index)
    {
        bool scout = index == 9 || index == 11;
        bool ally = index == 10 || index == 11;
        if (scout)
        {
            return GraphOpsVisualTemplates.Scout;
        }

        return ally ? GraphOpsVisualTemplates.Ally : GraphOpsVisualTemplates.Soldier;
    }

    private int RequireIndex(Entity entity, string op)
    {
        int idx = IndexOf(entity);
        if (idx < 0)
        {
            throw new InvalidOperationException($"Query gallery '{op}' named an entity that is not on the seeded map.");
        }

        return idx;
    }

    private int IndexOf(Entity entity)
    {
        for (int i = 0; i < _units.Length; i++)
        {
            if (_units[i] == entity)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsAggregateValueOp(string op)
        => op is nameof(GraphNodeOp.AggSumAttribute)
            or nameof(GraphNodeOp.AggAverageAttribute)
            or nameof(GraphNodeOp.AggMaxAttribute)
            or nameof(GraphNodeOp.AggMinAttribute);

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
}
