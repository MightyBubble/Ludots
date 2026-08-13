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
    private GraphOpsNodeGallerySandboxCatalog _catalog = new();
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
        _inRange = new bool[ctx.SimActors.Length];
        if (ProgramHasQueryRadius(ctx.Compiled.Program))
        {
            _queryRadiusMeters = ReadQueryRadiusMeters(ctx.Compiled.Program);
            ProbeRadiusOrFail(ctx);
        }
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
        GraphOpsNodeGallerySandboxCatalog? catalog = JsonSerializer.Deserialize<GraphOpsNodeGallerySandboxCatalog>(
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
        _buffTemplateId = EffectTemplateIdRegistry.GetId(_catalog.BuffEffect);
        if (_buffTemplateId <= 0)
        {
            throw new InvalidOperationException(
                $"Sandbox gallery requires effect '{_catalog.BuffEffect}' loaded through EffectTemplateLoader.");
        }

        _buffKeyId = ConfigKeyRegistry.Register(_catalog.BuffBlackboardKey);
        _socialBondTypeId = ctx.RelationshipTypes.Register(_catalog.RelationshipType);
        _loyaltyMetricId = ctx.RelationshipMetrics.Register(_catalog.LoyaltyMetric, -100, 100, 0);
        _ = ctx.RelationshipFlags.Register(_catalog.TrustedFlag);
        if (EffectTemplateIdRegistry.GetId(_catalog.MarkEffect) <= 0)
        {
            throw new InvalidOperationException(
                $"Sandbox gallery requires effect '{_catalog.MarkEffect}' loaded through EffectTemplateLoader.");
        }
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

        int targetIndex = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        Entity tagged = targetIndex >= 0
            ? GraphOpsNodeActorBinding.RequireRole(ctx, "target")
            : ctx.Caster;
        TagStateInstaller.EnsureInstalled(ctx.SimWorld, tagged);
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
                LightTarget(ctx);
                break;
            case "SelectTagInMask":
                ctx.CaptionValues["card"] = CaptionForTagId(result.IntValue);
                LightTarget(ctx);
                break;
            case "LookupTagDisplayToken":
                ctx.CaptionValues["token"] = CaptionForTokenId(result.IntValue);
                LightTarget(ctx);
                break;
            case "QueryRadius":
                LightInRange(ctx);
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                break;
            case "QuerySortStable":
                LightInRange(ctx);
                ctx.CaptionValues["order"] = JoinHitNames(ctx);
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                break;
            case "QueryLimit":
                LightInRange(ctx);
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

                LightTarget(ctx);
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
                LightAlly(ctx);
                ctx.CaptionValues["loyalty"] = loyalty.ToString();
                break;
            case "RelationshipHasFlag":
                RequireLink(ctx);
                if (!result.BoolValue)
                {
                    throw new InvalidOperationException("RelationshipHasFlag expected Trusted after the flag was set.");
                }

                LightAlly(ctx);
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

        GraphOpsNodeActorBinding.SyncActorHealthFromWorld(ctx);
        var lit = new List<int>();
        for (int i = 0; i < ctx.EffectRequests.Count; i++)
        {
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.EffectRequests[i].Target);
            if (index >= 0)
            {
                _inRange[index] = true;
                lit.Add(index);
            }
        }

        GraphOpsNodeActorBinding.LightCasterAndIndices(ctx, lit.ToArray());

        ctx.CaptionValues["count"] = result.TargetCount.ToString();
        ctx.CaptionValues["applied"] = ctx.EffectRequests.Count.ToString();
    }

    private static void LightInRange(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.LightCasterAndHits(ctx);
    }

    private static void LightTarget(GraphOpsNodeDriverContext ctx)
    {
        int target = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        GraphOpsNodeActorBinding.LightCasterAndIndices(ctx, target >= 0 ? [target] : []);
    }

    private static void LightAlly(GraphOpsNodeDriverContext ctx)
    {
        int ally = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (ally < 0)
        {
            throw new InvalidOperationException($"Sandbox {ctx.Vignette.Op} requires an ally target actor.");
        }

        GraphOpsNodeActorBinding.LightCasterAndIndices(ctx, [ally]);
    }

    private void RequireLink(GraphOpsNodeDriverContext ctx)
    {
        Entity ally = GraphOpsNodeActorBinding.RequireRole(ctx, "target");
        ctx.Target = ally;
        if (!ctx.Relationships!.HasLink(ctx.Caster, ally, _socialBondTypeId))
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

        throw new InvalidOperationException("Sandbox QueryRadius radius is missing from the compiled program.");
    }

    private static void RequireText(string? value, string field, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Sandbox catalog '{path}' missing {field}.");
        }
    }
}
