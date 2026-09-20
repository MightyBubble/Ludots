using System.Globalization;
using System.Numerics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Registry;
using Ludots.Core.Input.AimSource;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class DerivedQueryNodeDriver : IGraphOpsNodeDriver
{
    private int _key;
    private int _originalTeam;

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        var programs = ctx.Programs ?? throw new InvalidOperationException("Gallery query registry is required.");
        var collections = ctx.Collections ?? throw new InvalidOperationException("Gallery collection store is required.");
        var vignette = GraphOpsNodeVignetteLoader.Load(ctx.AssetsRoot, nameof(GraphNodeOp.QueryFilterTeam));
        var compiled = GraphOpsNodeGraphCompiler.Compile(ctx.AssetsRoot, vignette, collections: collections);
        int id = GraphIdRegistry.GetId(GraphOpsNodeIds.GraphId(nameof(GraphNodeOp.QueryFilterTeam)));
        programs.Register(id, compiled.Program, GraphKind.Query);
        _key = collections.KeyRegistry.Register(GraphOpsNodeGalleryHost.SnapCollectionKey);
        _originalTeam = ctx.SimWorld.Get<Team>(ctx.SimActors[1]).Id;
        if (ctx.Vignette.Op == nameof(GraphNodeOp.QueryScreenRegionCollection))
        {
            ctx.Api.BindQueryCollection(ctx.Caster, _key, id, programs);
            var spatial = ctx.SpatialQueries as SpatialQueryService
                ?? throw new InvalidOperationException("Gallery shared spatial service is required.");
            spatial.BindBoundsWorld(ctx.SimWorld);
            var projection = new GalleryProjection();
            ctx.Api.BindAimSource(new GraphAimSourceRuntime(ctx.SimWorld, new Dictionary<string, object>
            {
                [CoreServiceKeys.SpatialQueryService.Name] = spatial,
                [CoreServiceKeys.WorldSizeSpec.Name] = new WorldSizeSpec(new WorldAabbCm(-10000, -10000, 20000, 20000), 100),
                [CoreServiceKeys.ScreenRayProvider.Name] = projection,
                [CoreServiceKeys.ScreenProjector.Name] = projection
            }));
        }
        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        int team = ctx.Wave % 2 == 0 ? ctx.SimWorld.Get<Team>(ctx.Caster).Id : _originalTeam;
        ctx.SimWorld.Set(ctx.SimActors[1], new Team { Id = team });
        ctx.ExecuteFeaturedGraph();
        if (ctx.Vignette.Op == nameof(GraphNodeOp.BindQueryCollection))
        {
            ReadOnlySpan<Entity> members = ctx.Collections!.RequireSource(ctx.Caster, _key).Read();
            if (ctx.HitTargets.Length < members.Length) ctx.HitTargets = new Entity[members.Length];
            members.CopyTo(ctx.HitTargets);
            ctx.HitTargetCount = members.Length;
        }
        Array.Fill(ctx.ActorHudLit, false);
        foreach (Entity hit in ctx.HitTargets.AsSpan(0, ctx.HitTargetCount))
        {
            int actor = GraphOpsNodeActorBinding.IndexOf(ctx, hit);
            if (actor >= 0) ctx.ActorHudLit[actor] = true;
        }
        ctx.CaptionValues["count"] = ctx.HitTargetCount.ToString(CultureInfo.InvariantCulture);
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer draw)
    {
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            var actor = ctx.Vignette.Actors[i];
            GraphShowcaseStagePresenter.DrawActor(draw, actor.X, actor.Y, 0.85f,
                ctx.ActorHudLit[i] ? GraphShowcaseStagePresenter.SentryAlert : GraphShowcaseStagePresenter.GhostColor, 0.16f);
        }
    }

    private sealed class GalleryProjection : IScreenProjector, IScreenRayProvider
    {
        public Vector2 WorldToScreen(Vector3 world) => new(world.X * 100, world.Z * 100);
        public ScreenRay GetRay(Vector2 screen) => new(new Vector3(screen.X / 100, 20, screen.Y / 100), -Vector3.UnitY);
    }
}
