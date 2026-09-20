using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Production
{
    /// <summary>
    /// Regression: graph-written WorldPositionCm must reach the presentation layer —
    /// the hero's VisualTransform has to follow a SetWorldPosition teleport within a
    /// couple of ticks, otherwise the presenter freezes at spawn while logic moves
    /// (the "left click does nothing" night raid bug).
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    public sealed class NightRaidPresenterSyncRegressionTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string MapId = "night_raid";

        private static readonly string[] Mods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "MapTriggerNightRaidMod",
        };

        [Test]
        public void HeroVisualTransform_FollowsGraphWrittenWorldPosition()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, Mods),
                Path.Combine(repoRoot, "assets"));
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Start();
            engine.LoadMap(MapId);
            Tick(engine, 2);

            Entity hero = FindHero(engine.World);
            bool hasVisual = engine.World.Has<VisualTransform>(hero);
            bool hasPrevious = engine.World.Has<PreviousWorldPositionCm>(hero);
            TestContext.Out.WriteLine($"hero components: VisualTransform={hasVisual} PreviousWorldPositionCm={hasPrevious}");

            var before = engine.World.Get<WorldPositionCm>(hero);
            float visualXBefore = hasVisual ? engine.World.Get<VisualTransform>(hero).Position.X : float.NaN;
            TestContext.Out.WriteLine($"before: world=({before.Value.X},{before.Value.Y}) visualX={visualXBefore}");

            engine.World.Set(hero, new WorldPositionCm { Value = Fix64Vec2.FromInt(900, 700) });
            Tick(engine, 3);

            var after = engine.World.Get<WorldPositionCm>(hero);
            float visualXAfter = engine.World.Has<VisualTransform>(hero)
                ? engine.World.Get<VisualTransform>(hero).Position.X
                : float.NaN;
            TestContext.Out.WriteLine($"after: world=({after.Value.X},{after.Value.Y}) visualX={visualXAfter}");

            Entity presenter = FindPresenterForOwner(engine.World, hero);
            if (presenter != Entity.Null)
            {
                bool hasWorldPos = engine.World.Has<Ludots.Core.Presentation.Presenters.PresenterWorldPosition>(presenter);
                var presenterPos = hasWorldPos
                    ? engine.World.Get<Ludots.Core.Presentation.Presenters.PresenterWorldPosition>(presenter).Value
                    : default;
                bool bootstrapPending = engine.World.Has<Ludots.Core.Presentation.Components.PresenterBootstrapPending>(presenter);
                bool staticStable = engine.World.Has<Ludots.Core.Presentation.Presenters.PerfStaticStableVisual>(presenter);
                var emitCache = engine.World.Has<Ludots.Core.Presentation.Presenters.PresenterEmitCache>(presenter)
                    ? engine.World.Get<Ludots.Core.Presentation.Presenters.PresenterEmitCache>(presenter)
                    : default;
                TestContext.Out.WriteLine(
                    $"presenter={presenter} worldPos={presenterPos} bootstrapPending={bootstrapPending} staticStable={staticStable} staticDirty={emitCache.StaticDirty}");
                Assert.That(presenterPos.X, Is.EqualTo(9f).Within(0.05f),
                    "presenter world position must follow the teleport (visual render source)");
            }
            else
            {
                TestContext.Out.WriteLine("no presenter entity found for hero owner");
            }

            Assert.That(after.Value.X.RawValue, Is.EqualTo(Fix64.FromInt(900).RawValue), "sanity: logic position must move");
            Assert.That(engine.World.Has<VisualTransform>(hero), Is.True,
                "hero must carry VisualTransform for the presenter to follow");
            Assert.That(visualXAfter, Is.Not.EqualTo(visualXBefore).Within(0.001f),
                "VisualTransform must follow a graph-written WorldPositionCm teleport");
        }

        private static Entity FindPresenterForOwner(World world, Entity owner)
        {
            Entity found = Entity.Null;
            world.Query(
                new QueryDescription().WithAll<Ludots.Core.Presentation.Presenters.PresenterState>(),
                (Entity entity, ref Ludots.Core.Presentation.Presenters.PresenterState state) =>
                {
                    if (state.OwnerEntity == owner && state.AnchorKind == Ludots.Core.Presentation.Commands.PresentationAnchorKind.Entity)
                    {
                        bool tick = world.Has<Ludots.Core.Presentation.Presenters.PerfTransformSyncTick>(entity);
                        bool payloadTick = world.Has<Ludots.Core.Presentation.Presenters.PerfOwnerPayloadTransformSync>(entity);
                        bool attachedTick = world.Has<Ludots.Core.Presentation.Presenters.PerfOwnerPayloadAttachedTransformSync>(entity);
                        bool hasPos = world.Has<Ludots.Core.Presentation.Presenters.PresenterWorldPosition>(entity);
                        var pos = hasPos ? world.Get<Ludots.Core.Presentation.Presenters.PresenterWorldPosition>(entity).Value : default;
                        bool hasSource = world.Has<Ludots.Core.Presentation.Presenters.PresenterTransformSource>(entity);
                        var source = hasSource ? world.Get<Ludots.Core.Presentation.Presenters.PresenterTransformSource>(entity).Value : default;
                        bool hasParent = world.Has<Ludots.Core.Presentation.Presenters.PresenterParent>(entity);
                        TestContext.Out.WriteLine(
                            $"  presenter {entity} def={state.DefId} tick={tick} payloadTick={payloadTick} attachedTick={attachedTick} hasParent={hasParent} pos={pos} source={source}");
                        if (found == Entity.Null)
                        {
                            found = entity;
                        }
                    }
                });
            return found;
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
            }
        }

        private static Entity FindHero(World world)
        {
            Entity found = Entity.Null;
            world.Query(new QueryDescription().WithAll<Name>(), (Entity entity, ref Name name) =>
            {
                if (found == Entity.Null && string.Equals(name.Value, "NightRaidHero", StringComparison.Ordinal))
                {
                    found = entity;
                }
            });
            return found != Entity.Null ? found : throw new InvalidOperationException("NightRaidHero missing.");
        }

        private static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AGENTS.md")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            return dir ?? throw new InvalidOperationException("Repo root not found.");
        }
    }
}
