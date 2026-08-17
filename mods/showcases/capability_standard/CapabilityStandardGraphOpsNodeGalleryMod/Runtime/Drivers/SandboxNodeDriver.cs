using System.Numerics;
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
    private const float HeadPipsYOffset = 1.2f;
    private const float BadgeYOffset = 1.7f;
    private const float UnitRingRadius = 0.55f;
    private const float BondThickness = 0.12f;
    private const float FlagLiftY = 0.7f;
    private const float MetricBoardDx = 3.4f;
    private const float MetricBoardDy = 1.1f;
    private const float MetricBoardWidth = 2.6f;
    private const float MetricBoardHeight = 1.6f;
    private const float MetricBarSpan = 1.9f;
    private const float LoyaltyBase = 40f;
    private const float LoyaltyCeiling = 100f;
    private const float DrawerDx = -2.6f;
    private const float DrawerDy = 2.6f;
    private const float PipGhostOffset = 0.15f;

    // The gallery host reuses one engine world across exclusively loaded op maps, so
    // driver-staged comparison props must be destroyed on the next Seed instead of
    // leaking into a later op's spatial queries.
    private static readonly List<Entity> StagedProps = new();

    private bool _seeded;
    private GraphOpsNodeGallerySandboxCatalog _catalog = new();
    private int _markedTagId;
    private int _buffTemplateId;
    private int _buffKeyId;
    private int _socialBondTypeId;
    private int _loyaltyMetricId;
    private int _trustedFlagId;
    private float _queryRadiusMeters = QueryRadiusMeters;
    private bool[] _inRange = Array.Empty<bool>();
    private Entity[] _prevHits = new Entity[GraphVmLimits.MaxTargets];
    private int _prevHitCount;
    private Entity _unmarkedScout = Entity.Null;
    private Entity _passerby = Entity.Null;

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
        StageComparisonProps(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded || ctx.EffectRequests == null || ctx.Relationships == null)
        {
            throw new InvalidOperationException($"Sandbox driver for {ctx.Vignette.Op} is not seeded.");
        }

        if (UsesStrikeSettlement(ctx.Vignette.Op))
        {
            GraphOpsNodeActorBinding.RestoreVignetteHealth(ctx);
        }

        if (string.Equals(ctx.Vignette.Op, "QuerySortStable", StringComparison.Ordinal))
        {
            _prevHitCount = ctx.HitTargetCount;
            Array.Copy(ctx.HitTargets, _prevHits, ctx.HitTargetCount);
        }

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
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        if (caster < 0)
        {
            return;
        }

        switch (ctx.Vignette.Op)
        {
            case "RelationshipEnsureLink":
                DrawBondOverlay(ctx, debugDraw, caster);
                break;
            case "RelationshipAddMetric":
                DrawMetricBoardOverlay(ctx, debugDraw, caster, addSegment: true);
                break;
            case "RelationshipSetMetric":
                DrawMetricBoardOverlay(ctx, debugDraw, caster, addSegment: false);
                break;
            case "RelationshipHasFlag":
                DrawHasFlagOverlay(ctx, debugDraw, caster);
                break;
            case "HasTag":
                DrawHasTagOverlay(ctx, debugDraw, caster);
                break;
            case "QueryRadius":
                DrawQueryRingOverlay(ctx, debugDraw, caster);
                break;
            case "QuerySortStable":
                DrawQuerySortOverlay(ctx, debugDraw, caster);
                break;
            case "QueryLimit":
                DrawQueryLimitOverlay(ctx, debugDraw, caster);
                break;
            case "FanOutApplyEffect":
                DrawFanOutStrikeOverlay(ctx, debugDraw, caster, withDrawer: false);
                break;
            case "FanOutApplyEffectDynamic":
                DrawFanOutStrikeOverlay(ctx, debugDraw, caster, withDrawer: true);
                break;
            case "ApplyEffectDynamic":
                DrawDynamicStrikeOverlay(ctx, debugDraw, caster);
                break;
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

        RequireText(catalog.MarkedTag, "markedTag", path);
        RequireText(catalog.MarkEffect, "markEffect", path);
        RequireText(catalog.BuffEffect, "buffEffect", path);
        RequireText(catalog.BuffBlackboardKey, "buffBlackboardKey", path);
        RequireText(catalog.RelationshipType, "relationshipType", path);
        RequireText(catalog.LoyaltyMetric, "loyaltyMetric", path);
        RequireText(catalog.TrustedFlag, "trustedFlag", path);

        _catalog = catalog;
    }

    private void BindCatalogIds(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.TagOps == null || ctx.RelationshipTypes == null || ctx.RelationshipMetrics == null || ctx.RelationshipFlags == null)
        {
            throw new InvalidOperationException($"Sandbox gallery '{ctx.Vignette.Op}' requires host tag and relationship services.");
        }

        _markedTagId = TagRegistry.Register(_catalog.MarkedTag);
        _buffTemplateId = EffectTemplateIdRegistry.GetId(_catalog.BuffEffect);        if (_buffTemplateId <= 0)
        {
            throw new InvalidOperationException(
                $"Sandbox gallery requires effect '{_catalog.BuffEffect}' loaded through EffectTemplateLoader.");
        }

        _buffKeyId = ConfigKeyRegistry.Register(_catalog.BuffBlackboardKey);
        _socialBondTypeId = ctx.RelationshipTypes.Register(_catalog.RelationshipType);
        _loyaltyMetricId = ctx.RelationshipMetrics.Register(_catalog.LoyaltyMetric, -100, 100, 0);
        _trustedFlagId = ctx.RelationshipFlags.Register(_catalog.TrustedFlag);
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

    private void StageComparisonProps(GraphOpsNodeDriverContext ctx)
    {
        DestroyStaleProps(ctx);
        switch (ctx.Vignette.Op)
        {
            case "HasTag":
                _unmarkedScout = SpawnStageProp(ctx, GraphOpsVisualTemplates.Scout, "侦察兵乙", 0f, -12f);
                break;
            case "RelationshipHasFlag":
                _passerby = SpawnStageProp(ctx, GraphOpsVisualTemplates.Soldier, "路人", 2f, -12f);
                break;
        }
    }

    private static void DestroyStaleProps(GraphOpsNodeDriverContext ctx)
    {
        for (int i = StagedProps.Count - 1; i >= 0; i--)
        {
            if (ctx.SimWorld.IsAlive(StagedProps[i]))
            {
                ctx.SimWorld.Destroy(StagedProps[i]);
            }
        }

        StagedProps.Clear();
    }

    private static Entity SpawnStageProp(GraphOpsNodeDriverContext ctx, string template, string name, float xMeters, float yMeters)
    {
        Entity prop = ctx.Stage != null
            ? ctx.Stage.Spawn(template, name, xMeters, yMeters, 100f, 100f)
            : ctx.SimWorld.Create(WorldPositionCm.FromCmFloat(xMeters * 100f, yMeters * 100f));
        TagStateInstaller.EnsureInstalled(ctx.SimWorld, prop);
        StagedProps.Add(prop);
        ctx.Stage?.SetHealthBarVisible(prop, false);
        return prop;
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
                    throw new InvalidOperationException("HasTag expected the marked scout to carry the mark.");
                }

                if (_unmarkedScout == Entity.Null || ctx.Api.HasTag(_unmarkedScout, _markedTagId))
                {
                    throw new InvalidOperationException("HasTag expected the unmarked scout to carry no mark.");
                }

                ctx.CaptionValues["result"] = "有";
                LightTarget(ctx);
                break;
            case "QueryRadius":
                LightHitsOnly(ctx);
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                break;
            case "QuerySortStable":
                LightHitsOnly(ctx);
                ctx.CaptionValues["order"] = JoinHitNames(ctx);
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                break;
            case "QueryLimit":
                LightHitsOnly(ctx);
                ctx.CaptionValues["count"] = result.TargetCount.ToString();
                break;
            case "FanOutApplyEffect":
            case "FanOutApplyEffectDynamic":
                PresentFanOut(ctx, result);
                break;
            case "ApplyEffectDynamic":
                if (ctx.EffectRequests!.Count <= 0)
                {
                    throw new InvalidOperationException("ApplyEffectDynamic applied no effect to the named target.");
                }

                GraphOpsNodeActorBinding.SyncActorHealthFromWorld(ctx);
                LightTarget(ctx);
                ctx.CaptionValues["applied"] = ctx.EffectRequests.Count.ToString();
                break;
            case "RelationshipEnsureLink":
                RequireLink(ctx);
                LightAlly(ctx);
                break;
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

    private static void LightHitsOnly(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.EnsureHudLitBuffer(ctx);
        Array.Fill(ctx.ActorHudLit, false);
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            if (index >= 0 && index != caster)
            {
                ctx.ActorHudLit[index] = true;
            }
        }
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

    private static string JoinHitNames(GraphOpsNodeDriverContext ctx)
    {
        var names = new List<string>(ctx.HitTargetCount);
        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            if (index >= 0)
            {
                names.Add(ctx.Vignette.Actors[index].Name);
            }
        }

        return string.Join("、", names);
    }

    private void DrawBondOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        int ally = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (ally < 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        float ax = actors[caster].X;
        float ay = actors[caster].Y;
        float bx = actors[ally].X;
        float by = actors[ally].Y;
        if (!ctx.Relationships!.HasLink(ctx.Caster, ctx.SimActors[ally], _socialBondTypeId))
        {
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw,
                ax,
                ay,
                bx,
                by,
                0.1f,
                GraphShowcaseStagePresenter.GhostColor,
                arrowStart: false,
                arrowEnd: false);
            return;
        }

        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            ax,
            ay,
            bx,
            by,
            BondThickness,
            GraphShowcaseStagePresenter.SentryIdle,
            arrowStart: true,
            arrowEnd: true);
        GraphShowcaseStagePresenter.DrawBadge(
            debugDraw,
            (ax + bx) * 0.5f,
            (ay + by) * 0.5f,
            GraphShowcaseStagePresenter.BadgeKind.Ring,
            GraphShowcaseStagePresenter.GateColor,
            scale: 1.2f);
    }

    private void DrawMetricBoardOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, bool addSegment)
    {
        DrawBondOverlay(ctx, debugDraw, caster);
        int ally = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (ally < 0 || !ctx.Relationships!.HasLink(ctx.Caster, ctx.SimActors[ally], _socialBondTypeId))
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        int loyalty = ctx.Relationships.GetMetric(ctx.Caster, ctx.SimActors[ally], _socialBondTypeId, _loyaltyMetricId);
        float boardX = actors[ally].X + MetricBoardDx;
        float boardY = actors[ally].Y + MetricBoardDy;
        GraphShowcaseStagePresenter.DrawPanelBox(
            debugDraw,
            boardX,
            boardY,
            MetricBoardWidth,
            MetricBoardHeight,
            1,
            GraphShowcaseStagePresenter.GateColor);

        float barLeft = boardX - MetricBarSpan * 0.5f;
        float barY = boardY + 0.35f;
        float baseEnd = barLeft + MetricBarSpan * LoyaltyBase / LoyaltyCeiling;
        float valueEnd = barLeft + MetricBarSpan * loyalty / LoyaltyCeiling;
        if (addSegment)
        {
            RawLine(debugDraw, barLeft, barY, baseEnd, barY, 0.2f, GraphShowcaseStagePresenter.GhostColor);
            RawLine(debugDraw, baseEnd, barY, valueEnd, barY, 0.22f, GraphShowcaseStagePresenter.GuardColor);
        }
        else
        {
            GraphShowcaseStagePresenter.DrawDashedDirectedLine(
                debugDraw,
                barLeft,
                barY,
                baseEnd,
                barY,
                0.2f,
                GraphShowcaseStagePresenter.GhostColor,
                arrowStart: false,
                arrowEnd: false);
            RawLine(debugDraw, barLeft, barY, valueEnd, barY, 0.22f, GraphShowcaseStagePresenter.GuardColor);
        }

        RawLine(debugDraw, valueEnd, barY - 0.18f, valueEnd - 0.16f, barY - 0.42f, 0.07f, GraphShowcaseStagePresenter.SentryAlert);
        RawLine(debugDraw, valueEnd, barY - 0.18f, valueEnd + 0.16f, barY - 0.42f, 0.07f, GraphShowcaseStagePresenter.SentryAlert);
        GraphShowcaseStagePresenter.DrawNumber(
            debugDraw,
            barLeft + MetricBarSpan + 0.45f,
            barY,
            loyalty,
            0.5f,
            GraphShowcaseStagePresenter.SentryAlert);

        if (addSegment)
        {
            DrawLoyaltySquares(debugDraw, boardX, boardY - 0.45f, (loyalty - (int)LoyaltyBase) / 10);
        }
    }

    private static void DrawLoyaltySquares(DebugDrawCommandBuffer debugDraw, float x, float y, int litSquares)
    {
        int greySquares = (int)(LoyaltyBase / 10f);
        for (int i = 0; i < greySquares + litSquares; i++)
        {
            bool lit = i >= greySquares;
            debugDraw.Boxes.Add(new DebugDrawBox2D
            {
                Center = new Vector2(x - 0.5f + i * 0.3f, y),
                HalfWidth = 0.12f,
                HalfHeight = 0.12f,
                Thickness = lit ? 0.06f : 0.04f,
                Color = lit ? GraphShowcaseStagePresenter.GuardColor : GraphShowcaseStagePresenter.GhostColor
            });
        }
    }

    private void DrawHasFlagOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        DrawBondOverlay(ctx, debugDraw, caster);
        int ally = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (ally < 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        if (ctx.Relationships!.HasFlag(ctx.Caster, ctx.SimActors[ally], _socialBondTypeId, _trustedFlagId))
        {
            float flagX = (actors[caster].X + actors[ally].X) * 0.5f;
            float flagY = (actors[caster].Y + actors[ally].Y) * 0.5f + FlagLiftY;
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                flagX,
                flagY,
                GraphShowcaseStagePresenter.BadgeKind.Flag,
                GraphShowcaseStagePresenter.GuardColor,
                scale: 1.3f);
            if (ctx.Wave is 1 or 2)
            {
                GraphShowcaseStagePresenter.DrawBadge(
                    debugDraw,
                    flagX,
                    flagY,
                    GraphShowcaseStagePresenter.BadgeKind.Ring,
                    GraphShowcaseStagePresenter.GateColor,
                    scale: 1.9f);
            }

            GraphShowcaseStagePresenter.DrawThickOutlineCircle(
                debugDraw,
                actors[ally].X,
                actors[ally].Y,
                0.75f,
                GraphShowcaseStagePresenter.OutlineDark,
                GraphShowcaseStagePresenter.GuardColor);
        }

        DrawPasserbyOverlay(ctx, debugDraw, caster);
    }

    private void DrawPasserbyOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        if (_passerby == Entity.Null ||
            !ctx.SimWorld.IsAlive(_passerby) ||
            !ctx.SimWorld.Has<WorldPositionCm>(_passerby) ||
            ctx.Relationships!.HasLink(ctx.Caster, _passerby, _socialBondTypeId))
        {
            return;
        }

        WorldCmInt2 pos = ctx.SimWorld.Get<WorldPositionCm>(_passerby).ToWorldCmInt2();
        float px = pos.X / 100f;
        float py = pos.Y / 100f;
        GraphOpsNodeActor casterActor = ctx.Vignette.Actors[caster];
        GraphShowcaseStagePresenter.DrawDashedDirectedLine(
            debugDraw,
            casterActor.X,
            casterActor.Y,
            px,
            py,
            0.08f,
            GraphShowcaseStagePresenter.GhostColor,
            arrowStart: false,
            arrowEnd: false);
        GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, px, py, UnitRingRadius, GraphShowcaseStagePresenter.GhostColor);
    }

    private void DrawHasTagOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        int scout = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (scout >= 0 && ctx.Api.HasTag(ctx.SimActors[scout], _markedTagId))
        {
            GraphShowcaseStagePresenter.DrawThickOutlineCircle(
                debugDraw,
                actors[scout].X,
                actors[scout].Y,
                0.75f,
                GraphShowcaseStagePresenter.OutlineDark,
                GraphShowcaseStagePresenter.GuardColor);
            if (ctx.Wave % 2 == 1)
            {
                GraphShowcaseStagePresenter.DrawBadge(
                    debugDraw,
                    actors[scout].X,
                    actors[scout].Y + BadgeYOffset,
                    GraphShowcaseStagePresenter.BadgeKind.Ring,
                    GraphShowcaseStagePresenter.GateColor,
                    scale: 1.7f);
            }

            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                actors[scout].X,
                actors[scout].Y + BadgeYOffset,
                GraphShowcaseStagePresenter.BadgeKind.Diamond,
                GraphShowcaseStagePresenter.GateColor,
                scale: 1.2f);
        }

        if (_unmarkedScout != Entity.Null &&
            ctx.SimWorld.IsAlive(_unmarkedScout) &&
            ctx.SimWorld.Has<WorldPositionCm>(_unmarkedScout))
        {
            WorldCmInt2 pos = ctx.SimWorld.Get<WorldPositionCm>(_unmarkedScout).ToWorldCmInt2();
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                pos.X / 100f,
                pos.Y / 100f + BadgeYOffset,
                GraphShowcaseStagePresenter.BadgeKind.Check,
                GraphShowcaseStagePresenter.GhostColor);
        }
    }

    private void DrawQueryRingOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        GraphShowcaseStagePresenter.DrawTriggerRing(
            debugDraw,
            actors[caster].X,
            actors[caster].Y,
            _queryRadiusMeters,
            armed: true);
        for (int i = 0; i < actors.Length && i < _inRange.Length; i++)
        {
            if (i == caster)
            {
                continue;
            }

            if (_inRange[i])
            {
                GraphShowcaseStagePresenter.DrawActor(
                    debugDraw,
                    actors[i].X,
                    actors[i].Y,
                    UnitRingRadius,
                    GraphShowcaseStagePresenter.SentryAlert,
                    0.16f);
            }
            else
            {
                GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, actors[i].X, actors[i].Y, UnitRingRadius, GraphShowcaseStagePresenter.GhostColor);
            }
        }
    }

    private void DrawQuerySortOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        GraphShowcaseStagePresenter.DrawTriggerRing(
            debugDraw,
            actors[caster].X,
            actors[caster].Y,
            _queryRadiusMeters,
            armed: true);
        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, ctx.HitTargets[i]);
            if (index < 0 || index == caster)
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawActor(
                debugDraw,
                actors[index].X,
                actors[index].Y,
                UnitRingRadius,
                GraphShowcaseStagePresenter.SentryAlert,
                0.16f);
            GraphShowcaseStagePresenter.DrawRankPips(
                debugDraw,
                actors[index].X,
                actors[index].Y + HeadPipsYOffset,
                i + 1,
                GraphShowcaseStagePresenter.SentryAlert);
        }

        for (int i = 0; i < _prevHitCount; i++)
        {
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, _prevHits[i]);
            if (index < 0 || index == caster)
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawRankPips(
                debugDraw,
                actors[index].X + PipGhostOffset,
                actors[index].Y + HeadPipsYOffset - PipGhostOffset,
                i + 1,
                GraphShowcaseStagePresenter.GhostColor);
        }

        for (int i = 0; i < actors.Length && i < _inRange.Length; i++)
        {
            if (i == caster || _inRange[i])
            {
                continue;
            }

            GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, actors[i].X, actors[i].Y, UnitRingRadius, GraphShowcaseStagePresenter.GhostColor);
        }
    }

    private void DrawQueryLimitOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        GraphShowcaseStagePresenter.DrawTriggerRing(
            debugDraw,
            actors[caster].X,
            actors[caster].Y,
            _queryRadiusMeters,
            armed: true);
        var ranked = new List<int>();
        for (int i = 0; i < actors.Length && i < _inRange.Length; i++)
        {
            if (i == caster)
            {
                continue;
            }

            float dx = actors[i].X - actors[caster].X;
            float dy = actors[i].Y - actors[caster].Y;
            if (MathF.Sqrt(dx * dx + dy * dy) <= _queryRadiusMeters + 0.01f)
            {
                ranked.Add(i);
            }
            else
            {
                GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, actors[i].X, actors[i].Y, UnitRingRadius, GraphShowcaseStagePresenter.GhostColor);
            }
        }

        ranked.Sort((a, b) => ctx.SimActors[a].Id.CompareTo(ctx.SimActors[b].Id));
        for (int r = 0; r < ranked.Count; r++)
        {
            int i = ranked[r];
            bool hit = _inRange[i];
            if (hit)
            {
                GraphShowcaseStagePresenter.DrawActor(
                    debugDraw,
                    actors[i].X,
                    actors[i].Y,
                    UnitRingRadius,
                    GraphShowcaseStagePresenter.SentryAlert,
                    0.16f);
            }
            else
            {
                GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, actors[i].X, actors[i].Y, UnitRingRadius, GraphShowcaseStagePresenter.GhostColor);
            }

            GraphShowcaseStagePresenter.DrawRankPips(
                debugDraw,
                actors[i].X,
                actors[i].Y + HeadPipsYOffset,
                r + 1,
                hit ? GraphShowcaseStagePresenter.SentryAlert : GraphShowcaseStagePresenter.GhostColor);
        }
    }

    private void DrawFanOutStrikeOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, bool withDrawer)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        GraphShowcaseStagePresenter.DrawTriggerRing(
            debugDraw,
            actors[caster].X,
            actors[caster].Y,
            _queryRadiusMeters,
            armed: true);
        if (withDrawer)
        {
            DrawDrawerCard(ctx, debugDraw, caster);
        }

        for (int i = 0; i < actors.Length && i < _inRange.Length; i++)
        {
            if (i == caster)
            {
                continue;
            }

            if (_inRange[i])
            {
                GraphShowcaseStagePresenter.DrawActor(
                    debugDraw,
                    actors[i].X,
                    actors[i].Y,
                    UnitRingRadius,
                    GraphShowcaseStagePresenter.SentryAlert,
                    0.16f);
                DrawDamageNumbers(debugDraw, actors[i], ctx.ActorHealth[i]);
            }
            else
            {
                GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, actors[i].X, actors[i].Y, UnitRingRadius, GraphShowcaseStagePresenter.GhostColor);
            }
        }
    }

    private void DrawDynamicStrikeOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        DrawDrawerCard(ctx, debugDraw, caster);
        int stake = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (stake < 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        if (ctx.ActorHealth[stake] >= actors[stake].HealthMax - 0.5f)
        {
            return;
        }

        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            actors[caster].X,
            actors[caster].Y,
            actors[stake].X,
            actors[stake].Y,
            0.14f,
            GraphShowcaseStagePresenter.SentryAlert);
        DrawDamageNumbers(debugDraw, actors[stake], ctx.ActorHealth[stake]);
    }

    private static void DrawDrawerCard(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        GraphOpsNodeActor actor = ctx.Vignette.Actors[caster];
        float x = actor.X + DrawerDx;
        float y = actor.Y + DrawerDy;
        GraphShowcaseStagePresenter.DrawPanelBox(debugDraw, x, y, 1.8f, 1.1f, 1, GraphShowcaseStagePresenter.GateColor);
        bool drawn = ctx.Wave >= 1;
        debugDraw.Boxes.Add(new DebugDrawBox2D
        {
            Center = new Vector2(x, y),
            HalfWidth = 0.34f,
            HalfHeight = 0.44f,
            Thickness = drawn ? 0.12f : 0.04f,
            Color = drawn ? GraphShowcaseStagePresenter.SentryAlert : GraphShowcaseStagePresenter.GhostColor
        });
        if (drawn)
        {
            GraphShowcaseStagePresenter.DrawBadge(
                debugDraw,
                x,
                y,
                GraphShowcaseStagePresenter.BadgeKind.Cross,
                GraphShowcaseStagePresenter.OutlineDark);
        }
    }

    private static void DrawDamageNumbers(DebugDrawCommandBuffer debugDraw, GraphOpsNodeActor actor, float health)
    {
        float healthMax = actor.HealthMax > 0f ? actor.HealthMax : actor.Health;
        if (health >= healthMax - 0.5f)
        {
            return;
        }

        GraphShowcaseStagePresenter.DrawNumber(debugDraw, actor.X + 0.3f, actor.Y + 1.35f, (int)health, 0.4f, GraphShowcaseStagePresenter.GateColor);
        GraphShowcaseStagePresenter.DrawNumber(debugDraw, actor.X + 1.0f, actor.Y + 1.35f, (int)healthMax, 0.32f, GraphShowcaseStagePresenter.GhostColor);
    }

    private static void RawLine(DebugDrawCommandBuffer debugDraw, float ax, float ay, float bx, float by, float thickness, DebugDrawColor color)
    {
        debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = new Vector2(ax, ay),
            B = new Vector2(bx, by),
            Thickness = thickness,
            Color = color
        });
    }

    private static bool UsesStrikeSettlement(string op)
    {
        return op is "FanOutApplyEffect" or "FanOutApplyEffectDynamic" or "ApplyEffectDynamic";
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
