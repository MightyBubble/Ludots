using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.TagDisplay;
using Ludots.Core.Spatial;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class SandboxNodeDriver : IGraphOpsNodeDriver
{
    private const int CasterTeamId = 1;
    private const int UnitTeamId = 2;
    private const uint CasterLayer = 0b0001;
    private const uint UnitLayer = 0b0010;
    private const float QueryRadiusMeters = 8f;

    private bool _seeded;
    private GraphInstruction[] _program = Array.Empty<GraphInstruction>();
    private SandboxGalleryCatalog _catalog = new();
    private GasGraphRuntimeApi? _api;
    private SpatialCoordinateConverter? _coords;
    private GridSpatialPartitionWorld? _grid;
    private SpatialQueryService? _spatial;
    private TagOps? _tagOps;
    private RelationshipRuntime? _relationships;
    private EffectRequestQueue? _requests;
    private TagDisplayTableRegistry? _tagDisplay;
    private RelationshipTypeRegistry? _types;
    private RelationshipMetricRegistry? _metrics;
    private RelationshipFlagRegistry? _flags;
    private RelationshipReasonRegistry? _reasons;
    private int _burningTagId;
    private int _markedTagId;
    private int _burningTokenId;
    private int _markedTokenId;
    private int _markTemplateId;
    private int _buffTemplateId;
    private int _buffKeyId;
    private int _socialBondTypeId;
    private int _loyaltyMetricId;
    private int _trustedFlagId;
    private float _queryRadiusMeters = QueryRadiusMeters;
    private bool[] _inRange = Array.Empty<bool>();

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded)
        {
            LoadCatalog(ctx.AssetsRoot);
            BindServices(ctx);
            CompileExecutionProgram(ctx);
            SpawnField(ctx);
            _seeded = true;
        }

        SpawnStage(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (_api == null || _requests == null || _relationships == null || _tagDisplay == null)
        {
            throw new InvalidOperationException($"Sandbox driver for {ctx.Vignette.Op} is not seeded.");
        }

        _requests.Clear();
        GraphOpsNodeExecuteResult result = ExecuteFeatured(ctx);
        if (ProgramHasQueryRadius(_program) && result.TargetCount <= 0)
        {
            throw new InvalidOperationException(
                $"Sandbox {ctx.Vignette.Op} QueryRadius produced an empty TargetList.");
        }

        ApplyPresentation(ctx, result);
        ctx.Metrics.Detail = FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        SyncStage(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        if (!ProgramHasQueryRadius(_program))
        {
            return;
        }

        int caster = FindRole(ctx, "caster");
        if (caster < 0)
        {
            return;
        }

        GraphShowcaseStagePresenter.DrawTriggerRing(
            debugDraw,
            ctx.Vignette.Actors[caster].X,
            ctx.Vignette.Actors[caster].Y,
            _queryRadiusMeters,
            armed: true);

        for (int i = 0; i < ctx.Vignette.Actors.Length && i < _inRange.Length; i++)
        {
            if (!_inRange[i])
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawActor(
                debugDraw,
                ctx.Vignette.Actors[i].X,
                ctx.Vignette.Actors[i].Y,
                0.55f,
                GraphShowcaseStagePresenter.SentryAlert,
                thickness: 0.1f);
        }
    }

    private void LoadCatalog(string assetsRoot)
    {
        string path = Path.Combine(assetsRoot, "GAS", "sandbox", "catalog.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Sandbox gallery requires assets/GAS/sandbox/catalog.json (not engine ConfigPipeline graphs.json).",
                path);
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        SandboxGalleryCatalog? catalog = JsonSerializer.Deserialize<SandboxGalleryCatalog>(
            File.ReadAllText(path),
            options);
        if (catalog == null)
        {
            throw new InvalidOperationException($"Sandbox catalog '{path}' deserialized to null.");
        }

        RequireText(catalog.DisplayTable, "displayTable", path);
        RequireText(catalog.BurningTag, "burningTag", path);
        RequireText(catalog.MarkedTag, "markedTag", path);
        RequireText(catalog.BurningCaption, "burningCaption", path);
        RequireText(catalog.MarkedCaption, "markedCaption", path);
        RequireText(catalog.MarkEffect, "markEffect", path);
        RequireText(catalog.BuffEffect, "buffEffect", path);
        RequireText(catalog.BuffBlackboardKey, "buffBlackboardKey", path);
        RequireText(catalog.RelationshipType, "relationshipType", path);
        RequireText(catalog.LoyaltyMetric, "loyaltyMetric", path);
        RequireText(catalog.TrustedFlag, "trustedFlag", path);
        if (catalog.BurningTokenId <= 0 || catalog.MarkedTokenId <= 0)
        {
            throw new InvalidOperationException($"Sandbox catalog '{path}' token ids must be positive.");
        }

        _catalog = catalog;
    }

    private void BindServices(GraphOpsNodeDriverContext ctx)
    {
        _coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
        _grid = new GridSpatialPartitionWorld(cellSize: 4);
        _spatial = new SpatialQueryService(new GridSpatialPartitionBackend(_grid, _coords));
        _spatial.SetCoordinateConverter(_coords);
        World world = ctx.SimWorld;
        _spatial.SetPositionProvider(entity =>
        {
            if (!world.IsAlive(entity) || !world.Has<WorldPositionCm>(entity))
            {
                throw new InvalidOperationException("Sandbox spatial query entity is missing WorldPositionCm.");
            }

            return world.Get<WorldPositionCm>(entity).ToWorldCmInt2();
        });

        _tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
        _types = new RelationshipTypeRegistry();
        _metrics = new RelationshipMetricRegistry();
        _flags = new RelationshipFlagRegistry();
        _reasons = new RelationshipReasonRegistry();
        _socialBondTypeId = _types.Register(_catalog.RelationshipType);
        _loyaltyMetricId = _metrics.Register(_catalog.LoyaltyMetric, -100, 100, 0);
        _trustedFlagId = _flags.Register(_catalog.TrustedFlag);
        _reasons.Register("Scenario.Setup");
        _relationships = new RelationshipRuntime(
            world,
            _types,
            _metrics,
            _flags,
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(world));

        _burningTagId = RequireTag(_catalog.BurningTag);
        _markedTagId = RequireTag(_catalog.MarkedTag);
        _burningTokenId = _catalog.BurningTokenId;
        _markedTokenId = _catalog.MarkedTokenId;
        var mask = new GameplayTagContainer();
        mask.AddTag(_burningTagId);
        mask.AddTag(_markedTagId);
        _tagDisplay = new TagDisplayTableRegistry();
        _tagDisplay.RegisterTable(
            _catalog.DisplayTable,
            in mask,
            new (int, int)[]
            {
                (_burningTagId, _burningTokenId),
                (_markedTagId, _markedTokenId)
            });
        _tagDisplay.Freeze();

        _markTemplateId = RequireEffectTemplate(_catalog.MarkEffect);
        _buffTemplateId = RequireEffectTemplate(_catalog.BuffEffect);
        if (_markTemplateId <= 0 || _buffTemplateId <= 0)
        {
            throw new InvalidOperationException("Sandbox gallery effect templates failed to register.");
        }
        _buffKeyId = ConfigKeyRegistry.Register(_catalog.BuffBlackboardKey);
        _requests = new EffectRequestQueue();
        var entityQueries = new EntitySetQueryRuntime(world, _tagOps, _relationships);
        _api = new GasGraphRuntimeApi(
            world,
            _spatial,
            _coords,
            eventBus: null,
            effectRequests: _requests,
            tagOps: _tagOps,
            relationshipRuntime: _relationships,
            typeRegistry: _types,
            metricRegistry: _metrics,
            flagRegistry: _flags,
            reasonRegistry: _reasons,
            entityQueries: entityQueries,
            tagDisplayTables: _tagDisplay);
        ctx.RuntimeApiOverride = _api;

        _queryRadiusMeters = QueryRadiusMeters;
    }

    private void CompileExecutionProgram(GraphOpsNodeDriverContext ctx)
    {
        string path = Path.Combine(ctx.AssetsRoot, "GAS", "graphs", ctx.Vignette.Op + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing sandbox execution graph: {path}", path);
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        JsonObject obj = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        string graphId = GraphOpsNodeIds.GraphId(ctx.Vignette.Op);
        GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
        if (!compiled.Succeeded || compiled.Package is not { } package)
        {
            string message = string.Join("; ", compiled.Diagnostics.Select(d => d.Message));
            throw new InvalidOperationException($"Sandbox execution compile failed for '{graphId}': {message}");
        }

        var resolver = new SandboxGallerySymbolResolver(_tagDisplay!, _types!, _metrics!, _flags!, _reasons!);
        GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, resolver);
        _program = package.Program;
        _queryRadiusMeters = ReadQueryRadiusMeters(_program);
    }

    private void SpawnField(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        ctx.SimActors = new Entity[actors.Length];
        ctx.ActorHealth = new float[actors.Length];
        _inRange = new bool[actors.Length];
        for (int i = 0; i < actors.Length; i++)
        {
            GraphOpsNodeActor actor = actors[i];
            bool caster = string.Equals(actor.Role, "caster", StringComparison.Ordinal);
            int xCm = (int)MathF.Round(actor.X * 100f);
            int yCm = (int)MathF.Round(actor.Y * 100f);
            Entity entity = ctx.SimWorld.Create(
                new MapEntity(),
                new Team { Id = caster ? CasterTeamId : UnitTeamId },
                WorldPositionCm.FromCm(xCm, yCm),
                new EntityLayer(category: caster ? CasterLayer : UnitLayer, mask: uint.MaxValue),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new DirtyFlags());
            ctx.SimActors[i] = entity;
            ctx.ActorHealth[i] = actor.Health;
            if (caster)
            {
                ctx.Caster = entity;
            }
            else if (string.Equals(actor.Role, "target", StringComparison.Ordinal))
            {
                ctx.Target = entity;
            }

            AddToGrid(entity, xCm, yCm);
        }

        ProbeRadiusOrFail(ctx);

        if (ctx.Caster == Entity.Null)
        {
            throw new InvalidOperationException($"Sandbox vignette {ctx.Vignette.Op} requires a caster actor.");
        }

        SeedTags(ctx);
        ref BlackboardIntBuffer casterBb = ref ctx.SimWorld.Get<BlackboardIntBuffer>(ctx.Caster);
        casterBb.Set(_buffKeyId, _buffTemplateId);
        ctx.Metrics.AgentCount = actors.Length;
        ctx.Metrics.Detail = ctx.Vignette.Beat;
    }

    private void SeedTags(GraphOpsNodeDriverContext ctx)
    {
        int tagId = ctx.Vignette.Op switch
        {
            "HasTag" => _markedTagId,
            "SelectTagInMask" or "LookupTagDisplayToken" => _burningTagId,
            _ => 0
        };
        if (tagId <= 0)
        {
            return;
        }

        Entity tagged = ctx.Target != Entity.Null ? ctx.Target : ctx.Caster;
        if (!_tagOps!.AddTag(ctx.SimWorld, tagged, tagId))
        {
            throw new InvalidOperationException($"Sandbox {ctx.Vignette.Op} failed to seed status tag {tagId}.");
        }
    }

    private void AddToGrid(Entity entity, int xCm, int yCm)
    {
        IntVector2 grid = _coords!.WorldToGrid(new WorldCmInt2(xCm, yCm));
        _grid!.Add(entity, new IntRect(grid.X, grid.Y, grid.X + 1, grid.Y + 1));
    }

    private void ProbeRadiusOrFail(GraphOpsNodeDriverContext ctx)
    {
        if (_spatial == null || ctx.Caster == Entity.Null)
        {
            throw new InvalidOperationException("Sandbox spatial query service was not constructed.");
        }

        Span<Entity> buffer = stackalloc Entity[GraphVmLimits.MaxTargets];
        WorldCmInt2 origin = ctx.SimWorld.Get<WorldPositionCm>(ctx.Caster).ToWorldCmInt2();
        int radiusCm = (int)MathF.Round(_queryRadiusMeters * 100f);
        if (radiusCm <= 0)
        {
            radiusCm = 800;
        }

        SpatialQueryResult probe = _spatial.QueryRadius(origin, radiusCm, buffer);
        if (probe.Count <= 0)
        {
            throw new InvalidOperationException(
                $"Sandbox spatial probe found no units. origin=({origin.X},{origin.Y}) radiusCm={radiusCm} actors={ctx.SimActors.Length}.");
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
        entities[0] = ctx.Caster;
        entities[1] = ctx.Target;
        var targetList = new GraphTargetList(targets);
        WorldCmInt2 origin = ctx.SimWorld.Get<WorldPositionCm>(ctx.Caster).ToWorldCmInt2();

        var state = new GraphExecutionState
        {
            World = ctx.SimWorld,
            Caster = ctx.Caster,
            ExplicitTarget = ctx.Target,
            TargetPosCm = new IntVector2(origin.X, origin.Y),
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

        GasGraphOpHandlerTable.Execute(ref state, _program, GasGraphOpHandlerTable.Instance);
        if (state.Status != GraphExecutionStatus.Halted)
        {
            throw new InvalidOperationException(
                $"Featured sandbox graph for {ctx.Vignette.Op} ended with status {state.Status}.");
        }

        MarkHits(ctx, state.TargetList);
        byte dest = ctx.FeaturedDest;
        return new GraphOpsNodeExecuteResult(
            dest < floats.Length ? floats[dest] : 0f,
            dest < ints.Length ? ints[dest] : 0,
            dest < bools.Length && bools[dest] != 0,
            dest < entities.Length ? entities[dest] : default,
            state.ReturnInt,
            state.TargetList.Count);
    }

    private void MarkHits(GraphOpsNodeDriverContext ctx, GraphTargetList targetList)
    {
        Array.Fill(_inRange, false);
        ReadOnlySpan<Entity> hits = targetList.Span;
        for (int h = 0; h < hits.Length; h++)
        {
            int index = IndexOf(ctx, hits[h]);
            if (index >= 0)
            {
                _inRange[index] = true;
            }
        }
    }

    private void ApplyPresentation(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        switch (ctx.Vignette.Op)
        {
            case "HasTag":
                PresentHasTag(ctx, result);
                break;
            case "SelectTagInMask":
                PresentSelectTag(ctx, result);
                break;
            case "LookupTagDisplayToken":
                PresentLookup(ctx, result);
                break;
            case "QueryRadius":
                PresentQueryRadius(ctx, result);
                break;
            case "QuerySortStable":
                PresentQuerySort(ctx, result);
                break;
            case "QueryLimit":
                PresentQueryLimit(ctx, result);
                break;
            case "FanOutApplyEffect":
                PresentFanOut(ctx, result, "圈里每人挂上一层，血条显示挂上了。");
                break;
            case "ApplyEffectDynamic":
                PresentApplyDynamic(ctx);
                break;
            case "FanOutApplyEffectDynamic":
                PresentFanOut(ctx, result, "按读到的模板打圈里所有人，状态已经挂上了。");
                break;
            case "RelationshipEnsureLink":
                PresentEnsure(ctx);
                break;
            case "RelationshipSetMetric":
                PresentSetMetric(ctx);
                break;
            case "RelationshipAddMetric":
                PresentAddMetric(ctx);
                break;
            case "RelationshipHasFlag":
                PresentHasFlag(ctx, result);
                break;
            default:
                throw new InvalidOperationException($"Sandbox driver does not host op '{ctx.Vignette.Op}'.");
        }
    }

    private void PresentHasTag(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        if (!result.BoolValue)
        {
            throw new InvalidOperationException("HasTag expected the scout to carry the enemy mark.");
        }

        ctx.CaptionValues["result"] = "有";
        HighlightTargetHealth(ctx, 80f);
    }

    private void PresentSelectTag(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        ctx.CaptionValues["card"] = CaptionForTagId(result.IntValue);
        HighlightTargetHealth(ctx, 80f);
    }

    private void PresentLookup(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        ctx.CaptionValues["token"] = CaptionForTokenId(result.IntValue);
        HighlightTargetHealth(ctx, 80f);
    }

    private void PresentQueryRadius(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        HighlightRangeHealth(ctx, litHealth: 80f);
        ctx.CaptionValues["count"] = result.TargetCount.ToString();
    }

    private void PresentQuerySort(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        HighlightRangeHealth(ctx, litHealth: 80f);
        ctx.CaptionValues["order"] = JoinHitNames(ctx);
        ctx.CaptionValues["count"] = result.TargetCount.ToString();
    }

    private void PresentQueryLimit(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        HighlightRangeHealth(ctx, litHealth: 80f);
        ctx.CaptionValues["count"] = result.TargetCount.ToString();
    }

    private void PresentFanOut(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result, string caption)
    {
        if (_requests!.Count <= 0)
        {
            throw new InvalidOperationException($"Sandbox {ctx.Vignette.Op} applied no effects.");
        }

        HighlightEffectTargets(ctx, 55f);
        ctx.CaptionValues["count"] = result.TargetCount.ToString();
        ctx.CaptionValues["applied"] = _requests.Count.ToString();
        _ = caption;
    }

    private void PresentApplyDynamic(GraphOpsNodeDriverContext ctx)
    {
        if (_requests!.Count <= 0)
        {
            throw new InvalidOperationException("ApplyEffectDynamic applied no effect to the named target.");
        }

        HighlightTargetHealth(ctx, 55f);
        ctx.CaptionValues["applied"] = _requests.Count.ToString();
    }

    private void PresentEnsure(GraphOpsNodeDriverContext ctx)
    {
        RequireLink(ctx);
        int loyalty = ReadLoyalty(ctx);
        WriteAllyHealth(ctx, loyalty);
        ctx.CaptionValues["loyalty"] = loyalty.ToString();
    }

    private void PresentSetMetric(GraphOpsNodeDriverContext ctx)
    {
        RequireLink(ctx);
        int loyalty = ReadLoyalty(ctx);
        WriteAllyHealth(ctx, loyalty);
        ctx.CaptionValues["loyalty"] = loyalty.ToString();
    }

    private void PresentAddMetric(GraphOpsNodeDriverContext ctx)
    {
        RequireLink(ctx);
        int loyalty = ReadLoyalty(ctx);
        WriteAllyHealth(ctx, loyalty);
        ctx.CaptionValues["loyalty"] = loyalty.ToString();
    }

    private void PresentHasFlag(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        RequireLink(ctx);
        if (!result.BoolValue)
        {
            throw new InvalidOperationException("RelationshipHasFlag expected Trusted after the flag was set.");
        }

        WriteAllyHealth(ctx, 100f);
        ctx.CaptionValues["result"] = "信得过";
    }

    private void HighlightRangeHealth(GraphOpsNodeDriverContext ctx, float litHealth)
    {
        for (int i = 0; i < ctx.ActorHealth.Length; i++)
        {
            if (string.Equals(ctx.Vignette.Actors[i].Role, "caster", StringComparison.Ordinal))
            {
                continue;
            }

            if (_inRange[i])
            {
                ctx.ActorHealth[i] = litHealth;
            }
        }
    }

    private void HighlightEffectTargets(GraphOpsNodeDriverContext ctx, float health)
    {
        for (int i = 0; i < _requests!.Count; i++)
        {
            int index = IndexOf(ctx, _requests[i].Target);
            if (index >= 0)
            {
                ctx.ActorHealth[index] = health;
                _inRange[index] = true;
            }
        }
    }

    private static void HighlightTargetHealth(GraphOpsNodeDriverContext ctx, float health)
    {
        int target = FindRole(ctx, "target");
        if (target >= 0)
        {
            ctx.ActorHealth[target] = health;
        }
    }

    private void WriteAllyHealth(GraphOpsNodeDriverContext ctx, float loyalty)
    {
        int ally = FindRole(ctx, "target");
        if (ally < 0)
        {
            throw new InvalidOperationException($"Sandbox {ctx.Vignette.Op} requires an ally target actor.");
        }

        float clamped = Math.Clamp(loyalty, 0f, ctx.Vignette.Actors[ally].HealthMax);
        ctx.ActorHealth[ally] = clamped;
    }

    private void RequireLink(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Target == Entity.Null ||
            !_relationships!.HasLink(ctx.Caster, ctx.Target, _socialBondTypeId))
        {
            throw new InvalidOperationException($"Sandbox {ctx.Vignette.Op} did not ensure a SocialBond link.");
        }
    }

    private int ReadLoyalty(GraphOpsNodeDriverContext ctx)
        => _relationships!.GetMetric(ctx.Caster, ctx.Target, _socialBondTypeId, _loyaltyMetricId);

    private string CaptionForTagId(int tagId)
    {
        if (tagId == _burningTagId)
        {
            return _catalog.BurningCaption;
        }

        if (tagId == _markedTagId)
        {
            return _catalog.MarkedCaption;
        }

        throw new InvalidOperationException($"SelectTagInMask returned unmapped tag {tagId}.");
    }

    private string CaptionForTokenId(int tokenId)
    {
        if (tokenId == _burningTokenId)
        {
            return _catalog.BurningCaption;
        }

        if (tokenId == _markedTokenId)
        {
            return _catalog.MarkedCaption;
        }

        throw new InvalidOperationException($"LookupTagDisplayToken returned unmapped token {tokenId}.");
    }

    private string JoinHitNames(GraphOpsNodeDriverContext ctx)
    {
        var names = new List<string>();
        for (int i = 0; i < ctx.Vignette.Actors.Length && i < _inRange.Length; i++)
        {
            if (_inRange[i])
            {
                names.Add(ctx.Vignette.Actors[i].Name);
            }
        }

        return string.Join("、", names);
    }

    private static bool ProgramHasQueryRadius(GraphInstruction[] program)
    {
        for (int i = 0; i < program.Length; i++)
        {
            if (program[i].Op == (ushort)GraphNodeOp.QueryRadius)
            {
                return true;
            }
        }

        return false;
    }

    private static float ReadQueryRadiusMeters(GraphInstruction[] program)
    {
        for (int i = 0; i < program.Length; i++)
        {
            if (program[i].Op == (ushort)GraphNodeOp.QueryRadius)
            {
                return program[i].ImmF / 100f;
            }
        }

        return QueryRadiusMeters;
    }

    private static int IndexOf(GraphOpsNodeDriverContext ctx, Entity entity)
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

    private static int RequireTag(string name)
    {
        int id = TagRegistry.GetId(name);
        return id > 0 ? id : TagRegistry.Register(name);
    }

    private static int RequireEffectTemplate(string name)
    {
        int id = EffectTemplateIdRegistry.GetId(name);
        return id > 0 ? id : EffectTemplateIdRegistry.Register(name);
    }

    private static void RequireText(string? value, string field, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Sandbox catalog '{path}' missing {field}.");
        }
    }

    private sealed class SandboxGalleryCatalog
    {
        public string DisplayTable { get; set; } = "";
        public string BurningTag { get; set; } = "";
        public string MarkedTag { get; set; } = "";
        public int BurningTokenId { get; set; }
        public int MarkedTokenId { get; set; }
        public string BurningCaption { get; set; } = "";
        public string MarkedCaption { get; set; } = "";
        public string MarkEffect { get; set; } = "";
        public string BuffEffect { get; set; } = "";
        public string BuffBlackboardKey { get; set; } = "";
        public string RelationshipType { get; set; } = "";
        public string LoyaltyMetric { get; set; } = "";
        public string TrustedFlag { get; set; } = "";
    }

    private sealed class SandboxGallerySymbolResolver : IGraphSymbolResolver
    {
        private readonly TagDisplayTableRegistry _tables;
        private readonly RelationshipTypeRegistry _types;
        private readonly RelationshipMetricRegistry _metrics;
        private readonly RelationshipFlagRegistry _flags;
        private readonly RelationshipReasonRegistry _reasons;

        public SandboxGallerySymbolResolver(
            TagDisplayTableRegistry tables,
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry metrics,
            RelationshipFlagRegistry flags,
            RelationshipReasonRegistry reasons)
        {
            _tables = tables;
            _types = types;
            _metrics = metrics;
            _flags = flags;
            _reasons = reasons;
        }

        public int ResolveTag(string name) => RequireTag(name);
        public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
        public int ResolveEffectTemplate(string name) => RequireEffectTemplate(name);
        public int ResolveRelationshipType(string name) => _types.Register(name);
        public int ResolveRelationshipMetric(string name) => _metrics.Register(name, -100, 100, 0);
        public int ResolveRelationshipFlag(string name) => _flags.Register(name);
        public int ResolveRelationshipReason(string name) => _reasons.Register(name);
        public int ResolveTargetDispatchPreset(string name) => ConfigKeyRegistry.Register($"targetDispatch.{name}");
        public int ResolveEntityTemplate(string name) => ConfigKeyRegistry.Register($"entityTemplate.{name}");
        public int ResolveTagDisplayTable(string name) => _tables.GetTableId(name);
    }
}
