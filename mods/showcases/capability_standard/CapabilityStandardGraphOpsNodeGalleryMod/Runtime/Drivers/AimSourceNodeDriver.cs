using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Input.AimSource;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

/// <summary>
/// Hosts the aimsource pure-helper vignettes: screen point to ground, pointer pick,
/// region filter, and the two direction helpers. The featured graphs run against the
/// production aimsource kernel over a deterministic gallery binding (screen px ↔
/// world cm 1:1 projector, flat heightmap ground), so every caption quotes a value the
/// real kernel chain computed.
/// </summary>
public sealed class AimSourceNodeDriver : IGraphOpsNodeDriver
{
    private const float UnitRadius = 0.85f;
    private const float UnitRingThickness = 0.16f;
    private const int AimTargetXCm = 330;
    private const int AimTargetYCm = 120;

    private Entity[] _units = Array.Empty<Entity>();
    private GraphOpsNodeExecuteResult _last;
    private bool _seeded;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "caster");
        ctx.Api.BindAimSource(new GraphAimSourceRuntime(ctx.SimWorld, BuildGalleryGlobals()));
        CollectUnits(ctx);

        if (string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.ScreenPointToEntity), StringComparison.Ordinal))
        {
            SeedSelectableTags(ctx);
            ctx.PrefillTargets = _units;
            ctx.PrefillTargetCount = _units.Length;
        }
        else if (string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.ScreenRegionToEntities), StringComparison.Ordinal))
        {
            ctx.PrefillTargets = _units;
            ctx.PrefillTargetCount = _units.Length;
        }
        else if (string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.PointToDirection), StringComparison.Ordinal))
        {
            ctx.TargetPosCm = new IntVector2(AimTargetXCm, AimTargetYCm);
            ctx.HasTargetPosCm = true;
        }

        ctx.Metrics.AgentCount = ctx.SimActors.Length;
        ctx.Metrics.Detail = ctx.Vignette.Beat;
        _seeded = true;
        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (!_seeded)
        {
            throw new InvalidOperationException($"Aimsource gallery '{ctx.Vignette.Op}' must Seed before Tick.");
        }

        _last = ctx.ExecuteFeaturedGraph();
        MarkHits(ctx);
        FillCaptions(ctx);
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

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        GraphShowcaseStagePresenter.DrawActor(
            debugDraw, actors[caster].X, actors[caster].Y, 0.5f, GraphShowcaseStagePresenter.GhostColor, 0.1f);
        GraphShowcaseStagePresenter.DrawActor(
            debugDraw, actors[caster].X, actors[caster].Y, 0.3f, GraphShowcaseStagePresenter.SentryAlert, 0.12f);

        bool resultWave = ctx.Wave % 2 == 1;
        for (int i = 0; i < _units.Length; i++)
        {
            int actorIndex = GraphOpsNodeActorBinding.IndexOf(ctx, _units[i]);
            if (actorIndex < 0)
            {
                continue;
            }

            float x = actors[actorIndex].X;
            float y = actors[actorIndex].Y;
            if (resultWave && ctx.ActorHudLit.Length > actorIndex && ctx.ActorHudLit[actorIndex])
            {
                GraphShowcaseStagePresenter.DrawActor(debugDraw, x, y, UnitRadius, GraphShowcaseStagePresenter.SentryAlert, UnitRingThickness);
            }
            else
            {
                GraphShowcaseStagePresenter.DrawGhostCircle(debugDraw, x, y, UnitRadius, GraphShowcaseStagePresenter.GhostColor);
            }
        }

        if (!resultWave)
        {
            return;
        }

        switch (ctx.Vignette.Op)
        {
            case nameof(GraphNodeOp.PointToDirection):
            case nameof(GraphNodeOp.StickToDirection):
                DrawAimArrow(ctx, debugDraw, caster, _last.FloatValue);
                break;
            case nameof(GraphNodeOp.ScreenPointToEntity):
                if (_last.EntityValue != Entity.Null)
                {
                    DrawPickLine(ctx, debugDraw, caster, _last.EntityValue);
                }

                break;
            case nameof(GraphNodeOp.ScreenPointToGround):
                DrawGroundMark(ctx, debugDraw, caster);
                break;
        }
    }

    private static Dictionary<string, object> BuildGalleryGlobals()
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [CoreServiceKeys.ScreenProjector.Name] = new GalleryScreenProjector(),
            [CoreServiceKeys.ScreenRayProvider.Name] = new GalleryScreenRayProvider(),
            [CoreServiceKeys.ContinuousHeightmap.Name] = new ContinuousHeightmapRuntime(
                ContinuousHeightmapAsset.CreateSingleLayer(
                    new WorldAabbCm(-10_000, -10_000, 20_000, 20_000),
                    sampleColumns: 2,
                    sampleRows: 2,
                    new short[] { 0, 0, 0, 0 })),
            [CoreServiceKeys.WorldSizeSpec.Name] = new WorldSizeSpec(new WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100),
        };
    }

    private void CollectUnits(GraphOpsNodeDriverContext ctx)
    {
        var units = new List<Entity>();
        for (int i = 0; i < ctx.Vignette.Actors.Length; i++)
        {
            if (string.Equals(ctx.Vignette.Actors[i].Role, "caster", StringComparison.Ordinal))
            {
                continue;
            }

            units.Add(ctx.SimActors[i]);
        }

        _units = units.ToArray();
    }

    private static void SeedSelectableTags(GraphOpsNodeDriverContext ctx)
    {
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            if (!ctx.SimWorld.Has<CommandSourceSelectableTag>(ctx.SimActors[i]))
            {
                ctx.SimWorld.Add(ctx.SimActors[i], new CommandSourceSelectableTag());
            }
        }
    }

    private void MarkHits(GraphOpsNodeDriverContext ctx)
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

        if (string.Equals(ctx.Vignette.Op, nameof(GraphNodeOp.ScreenPointToEntity), StringComparison.Ordinal) &&
            _last.EntityValue != Entity.Null)
        {
            int index = GraphOpsNodeActorBinding.IndexOf(ctx, _last.EntityValue);
            if (index >= 0)
            {
                ctx.ActorHudLit[index] = true;
            }
        }
    }

    private void FillCaptions(GraphOpsNodeDriverContext ctx)
    {
        var values = ctx.CaptionValues;
        switch (ctx.Vignette.Op)
        {
            case nameof(GraphNodeOp.ScreenPointToGround):
                if (!_last.BoolValue ||
                    !ctx.Api.TryScreenPointToGround(400f, 150f, out IntVector2 groundCm))
                {
                    throw new InvalidOperationException(
                        $"Aimsource gallery '{ctx.Vignette.Op}' ground resolution failed; caption cannot quote a value.");
                }

                values["x"] = FormatMeters(groundCm.X);
                values["y"] = FormatMeters(groundCm.Y);
                break;
            case nameof(GraphNodeOp.ScreenPointToEntity):
                values["name"] = ActorNameOf(ctx, _last.EntityValue);
                break;
            case nameof(GraphNodeOp.ScreenRegionToEntities):
                values["count"] = _last.TargetCount.ToString(CultureInfo.InvariantCulture);
                if (_last.TargetCount <= 0)
                {
                    throw new InvalidOperationException(
                        "ScreenRegionToEntities gallery returned 0 members; the rect must cover part of the cast.");
                }

                break;
            case nameof(GraphNodeOp.PointToDirection):
                values["deg"] = _last.FloatValue.ToString("0", CultureInfo.InvariantCulture);
                break;
            case nameof(GraphNodeOp.StickToDirection):
                values["deg"] = _last.FloatValue.ToString("0", CultureInfo.InvariantCulture);
                break;
            default:
                throw new InvalidOperationException($"Aimsource driver does not host op '{ctx.Vignette.Op}'.");
        }
    }

    private static string FormatMeters(int centimeters)
    {
        return (centimeters / 100f).ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static string ActorNameOf(GraphOpsNodeDriverContext ctx, Entity entity)
    {
        if (entity == Entity.Null)
        {
            throw new InvalidOperationException("ScreenPointToEntity gallery picked no one; the pointer must land on a cast member.");
        }

        int index = GraphOpsNodeActorBinding.IndexOf(ctx, entity);
        return index >= 0 ? ctx.Vignette.Actors[index].Name : "无名者";
    }

    private static void DrawAimArrow(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, float directionDeg)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        double radians = directionDeg * Math.PI / 180.0;
        float dx = (float)Math.Cos(radians);
        float dy = (float)Math.Sin(radians);
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            actors[caster].X,
            actors[caster].Y,
            actors[caster].X + dx * 2.6f,
            actors[caster].Y + dy * 2.6f,
            0.16f,
            GraphShowcaseStagePresenter.SentryAlert);
    }

    private static void DrawPickLine(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster, Entity picked)
    {
        int index = GraphOpsNodeActorBinding.IndexOf(ctx, picked);
        if (index < 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            actors[caster].X,
            actors[caster].Y,
            actors[index].X,
            actors[index].Y,
            0.12f,
            GraphShowcaseStagePresenter.SentryAlert);
    }

    private static void DrawGroundMark(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw, int caster)
    {
        if (!ctx.Api.TryScreenPointToGround(400f, 150f, out IntVector2 groundCm))
        {
            return;
        }

        debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = new Vector2(groundCm.X / 100f, groundCm.Y / 100f),
            Radius = 0.5f,
            Thickness = 2f,
            Color = GraphShowcaseStagePresenter.SentryAlert,
        });
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        GraphShowcaseStagePresenter.DrawDirectedLine(
            debugDraw,
            actors[caster].X,
            actors[caster].Y,
            groundCm.X / 100f,
            groundCm.Y / 100f,
            0.08f,
            GraphShowcaseStagePresenter.GhostColor);
    }

    /// <summary>Gallery binding-local projector: world meters project to screen px 1:100 (cm).</summary>
    private sealed class GalleryScreenProjector : IScreenProjector
    {
        public Vector2 WorldToScreen(Vector3 worldPosition)
        {
            return new Vector2(worldPosition.X * 100f, worldPosition.Z * 100f);
        }
    }

    private sealed class GalleryScreenRayProvider : IScreenRayProvider
    {
        public ScreenRay GetRay(Vector2 screenPosition)
        {
            return new ScreenRay(new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f), -Vector3.UnitY);
        }
    }
}
