using System.Text.Json;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Spatial;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class SandboxNodeDriver : IGraphOpsNodeDriver
{
    private const float QueryRadiusMeters = 8f;

    private bool _seeded;
    private SandboxGalleryCatalog _catalog = new();
    private int _burningTagId;
    private int _markedTagId;
    private int _burningTokenId;
    private int _markedTokenId;
    private int _buffTemplateId;
    private int _buffKeyId;
    private int _socialBondTypeId;
    private int _loyaltyMetricId;
    private float _queryRadiusMeters = QueryRadiusMeters;
    private bool[] _inRange = Array.Empty<bool>();

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        LoadCatalog(ctx.AssetsRoot);
        BindCatalogIds(ctx);
        SeedTags(ctx);
        ref BlackboardIntBuffer casterBb = ref ctx.SimWorld.Get<BlackboardIntBuffer>(ctx.Caster);
        casterBb.Set(_buffKeyId, _buffTemplateId);
        WorldCmInt2 origin = ctx.SimWorld.Get<WorldPositionCm>(ctx.Caster).ToWorldCmInt2();
        ctx.TargetPosCm = new IntVector2(origin.X, origin.Y);
        ctx.HasTargetPosCm = true;
        _queryRadiusMeters = ReadQueryRadiusMeters(ctx.Compiled.Program);
        _inRange = new bool[ctx.SimActors.Length];
        ProbeRadiusOrFail(ctx);
        ctx.Metrics.AgentCount = ctx.SimActors.Length;
        ctx.Metrics.Detail = ctx.Vignette.Beat;
        _seeded = true;
        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded || ctx.EffectRequests == null || ctx.Relationships == null || ctx.TagDisplay == null)
        {
            throw new InvalidOperationException($"Sandbox driver for {ctx.Vignette.Op} is not seeded.");
        }

        ctx.EffectRequests.Clear();
        GraphOpsNodeExecuteResult result = ctx.ExecuteFeaturedGraph();
        if (ProgramHasQueryRadius(ctx.Compiled.Program) && result.TargetCount <= 0)
        {
            throw new InvalidOperationException(
                $"Sandbox {ctx.Vignette.Op} QueryRadius produced an empty TargetList.");
        }

        MarkHits(ctx);
        ApplyPresentation(ctx, result);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
        if (!ProgramHasQueryRadius(ctx.Compiled.Program))
        {
            return;
        }

        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
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
                "Sandbox gallery requires assets/GAS/sandbox/catalog.json.",
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

    private void BindCatalogIds(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.TagOps == null || ctx.RelationshipTypes == null || ctx.RelationshipMetrics == null || ctx.RelationshipFlags == null)
        {
            throw new InvalidOperationException($"Sandbox gallery '{ctx.Vignette.Op}' requires host tag and relationship services.");
        }

        _burningTagId = TagRegistry.Register(_catalog.BurningTag);
        _markedTagId = TagRegistry.Register(_catalog.MarkedTag);
        _burningTokenId = _catalog.BurningTokenId;
        _markedTokenId = _catalog.MarkedTokenId;
        _buffTemplateId = EffectTemplateIdRegistry.Register(_catalog.BuffEffect);
        _buffKeyId = ConfigKeyRegistry.Register(_catalog.BuffBlackboardKey);
        _socialBondTypeId = ctx.RelationshipTypes.Register(_catalog.RelationshipType);
        _loyaltyMetricId = ctx.RelationshipMetrics.Register(_catalog.LoyaltyMetric, -100, 100, 0);
        _ = ctx.RelationshipFlags.Register(_catalog.TrustedFlag);
        _ = EffectTemplateIdRegistry.Register(_catalog.MarkEffect);
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
        if (!ctx.TagOps!.AddTag(ctx.SimWorld, tagged, tagId))
        {
            throw new InvalidOperationException($"Sandbox {ctx.Vignette.Op} failed to seed status tag {tagId}.");
        }
    }

    private void ProbeRadiusOrFail(GraphOpsNodeDriverContext ctx)
    {
        Span<Entity> buffer = stackalloc Entity[GraphVmLimits.MaxTargets];
        WorldCmInt2 origin = ctx.SimWorld.Get<WorldPositionCm>(ctx.Caster).ToWorldCmInt2();
        int radiusCm = (int)MathF.Round(_queryRadiusMeters * 100f);
        if (radiusCm <= 0)
        {
            throw new InvalidOperationException($"Sandbox {ctx.Vignette.Op} QueryRadius radius must be positive.");
        }

        SpatialQueryResult probe = ctx.Api.QueryRadius(new IntVector2(origin.X, origin.Y), radiusCm, buffer);
        if (probe.Count <= 0)
        {
            throw new InvalidOperationException(
                $"Sandbox spatial probe found no units. origin=({origin.X},{origin.Y}) radiusCm={radiusCm} actors={ctx.SimActors.Length}.");
        }
    }

    private void MarkHits(GraphOpsNodeDriverContext ctx)
    {
        Array.Fill(_inRange, false);
        for (int h = 0; h < ctx.HitTargetCount; h++)
        {
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[h]);
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
                if (!result.BoolValue)
                {
                    throw new InvalidOperationException("HasTag expected the scout to carry the enemy mark.");
                }

                ctx.CaptionValues["result"] = "有";
                HighlightTargetHealth(ctx, 80f);
                break;
            case "SelectTagInMask":
                ctx.CaptionValues["card"] = CaptionForTagId(result.IntValue);
                HighlightTargetHealth(ctx, 80f);
                break;
            case "LookupTagDisplayToken":
                ctx.CaptionValues["token"] = CaptionForTokenId(result.IntValue);
                HighlightTargetHealth(ctx, 80f);
                break;
            case "QueryRadius":
                HighlightRangeHealth(ctx, litHealth: 80f);
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                break;
            case "QuerySortStable":
                HighlightRangeHealth(ctx, litHealth: 80f);
                ctx.CaptionValues["order"] = JoinHitNames(ctx);
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                break;
            case "QueryLimit":
                HighlightRangeHealth(ctx, litHealth: 80f);
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                break;
            case "FanOutApplyEffect":
                PresentFanOut(ctx, result);
                break;
            case "ApplyEffectDynamic":
                if (ctx.EffectRequests!.Count <= 0)
                {
                    throw new InvalidOperationException("ApplyEffectDynamic applied no effect to the named target.");
                }

                HighlightTargetHealth(ctx, 55f);
                ctx.CaptionValues["applied"] = ctx.EffectRequests.Count.ToString();
                break;
            case "FanOutApplyEffectDynamic":
                PresentFanOut(ctx, result);
                break;
            case "RelationshipEnsureLink":
            case "RelationshipSetMetric":
            case "RelationshipAddMetric":
                RequireLink(ctx);
                int loyalty = ctx.Relationships!.GetMetric(ctx.Caster, ctx.Target, _socialBondTypeId, _loyaltyMetricId);
                WriteAllyHealth(ctx, loyalty);
                ctx.CaptionValues["loyalty"] = loyalty.ToString();
                break;
            case "RelationshipHasFlag":
                RequireLink(ctx);
                if (!result.BoolValue)
                {
                    throw new InvalidOperationException("RelationshipHasFlag expected Trusted after the flag was set.");
                }

                WriteAllyHealth(ctx, 100f);
                ctx.CaptionValues["result"] = "信得过";
                break;
            default:
                throw new InvalidOperationException($"Sandbox driver does not host op '{ctx.Vignette.Op}'.");
        }
    }

    private void PresentFanOut(GraphOpsNodeDriverContext ctx, GraphOpsNodeExecuteResult result)
    {
        if (ctx.EffectRequests!.Count <= 0)
        {
            throw new InvalidOperationException($"Sandbox {ctx.Vignette.Op} applied no effects.");
        }

        for (int i = 0; i < ctx.EffectRequests.Count; i++)
        {
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.EffectRequests[i].Target);
            if (index >= 0)
            {
                ctx.ActorHealth[index] = 55f;
                _inRange[index] = true;
            }
        }

        ctx.CaptionValues["count"] = result.TargetCount.ToString();
        ctx.CaptionValues["applied"] = ctx.EffectRequests.Count.ToString();
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

    private static void HighlightTargetHealth(GraphOpsNodeDriverContext ctx, float health)
    {
        int target = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (target >= 0)
        {
            ctx.ActorHealth[target] = health;
        }
    }

    private void WriteAllyHealth(GraphOpsNodeDriverContext ctx, float loyalty)
    {
        int ally = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (ally < 0)
        {
            throw new InvalidOperationException($"Sandbox {ctx.Vignette.Op} requires an ally target actor.");
        }

        ctx.ActorHealth[ally] = Math.Clamp(loyalty, 0f, ctx.Vignette.Actors[ally].HealthMax);
    }

    private void RequireLink(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Target == Entity.Null ||
            !ctx.Relationships!.HasLink(ctx.Caster, ctx.Target, _socialBondTypeId))
        {
            throw new InvalidOperationException($"Sandbox {ctx.Vignette.Op} did not ensure a SocialBond link.");
        }
    }

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
}
