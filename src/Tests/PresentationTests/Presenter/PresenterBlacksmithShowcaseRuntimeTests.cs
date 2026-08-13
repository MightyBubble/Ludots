using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using NUnit.Framework;
using PresenterBlacksmithShowcaseMod;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PresenterBlacksmithShowcaseRuntimeTests
    {
        private static int ResolvePresenterDefId(Ludots.Core.Engine.GameEngine engine, string key)
        {
            var defs = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
            int id = defs.GetId(key);
            Assert.That(id, Is.GreaterThan(0), $"Presenter definition '{key}' not registered.");
            return id;
        }

        private static int ResolveMeshAssetId(Ludots.Core.Engine.GameEngine engine, string key)
        {
            var meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
                ?? throw new InvalidOperationException("MeshAssetRegistry missing.");
            int id = meshes.GetId(key);
            Assert.That(id, Is.GreaterThan(0), $"Mesh asset '{key}' not registered.");
            return id;
        }

        private static int CountPresentersByDef(Ludots.Core.Engine.GameEngine engine, int defId)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<PresenterState>();
            engine.World.Query(in query, (Entity entity, ref PresenterState state) =>
            {
                if (state.DefId == defId)
                {
                    count++;
                }
            });
            return count;
        }

        private static Entity FindEntityByDef(Ludots.Core.Engine.GameEngine engine, int defId)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<PresenterState>();
            engine.World.Query(in query, (Entity entity, ref PresenterState state) =>
            {
                if (state.DefId == defId && found == Entity.Null)
                {
                    found = entity;
                }
            });
            return found;
        }

        private static int CountVisiblePrimitivesByMesh(Ludots.Core.Engine.GameEngine engine, int meshAssetId)
        {
            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PrimitiveDrawBuffer missing.");
            int count = 0;
            foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
            {
                if (item.Visibility == VisualVisibility.Visible && item.MeshAssetId == meshAssetId)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountVisibleSkinnedByMesh(Ludots.Core.Engine.GameEngine engine, int meshAssetId)
        {
            var skinned = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)
                ?? throw new InvalidOperationException("SkinnedVisualBatchBuffer missing.");
            int count = 0;
            foreach (ref readonly SkinnedVisualBatchItem item in skinned.GetSpan())
            {
                if (item.MeshAssetId == meshAssetId)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountWorldHudItems(Ludots.Core.Engine.GameEngine engine, WorldHudItemKind kind)
        {
            var hud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer missing.");
            int count = 0;
            foreach (ref readonly WorldHudItem item in hud.GetSpan())
            {
                if (item.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static Entity FindBlacksmithEntity(Ludots.Core.Engine.GameEngine engine)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, PresenterBlacksmithShowcaseIds.EntityName, StringComparison.Ordinal))
                {
                    found = entity;
                }
            });
            return found;
        }

        private static void SetWorkingTag(Ludots.Core.Engine.GameEngine engine, Entity building, bool active)
        {
            int workingTagId = TagRegistry.Register("working");
            var tagOps = engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("TagOps missing.");

            if (!engine.World.Has<DirtyFlags>(building))
            {
                engine.World.Add(building, default(DirtyFlags));
            }

            if (active)
            {
                tagOps.AddTag(engine.World, building, workingTagId);
            }
            else
            {
                tagOps.RemoveTag(engine.World, building, workingTagId);
            }
        }

        [Test]
        public void BlacksmithShowcase_MapLoad_BootstrapsCanonicalPresenterTree()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 12);

            int rootId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.RootDefinitionId);
            int leftId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.WorkshopLeftDefinitionId);
            int rightId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.WorkshopRightDefinitionId);
            int chimneyId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.ChimneyDefinitionId);
            int smokeId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.SmokeDefinitionId);
            int routeId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.RouteSplineDefinitionId);
            int decalId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.DecalDefinitionId);
            int workerId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.WorkerDefinitionId);
            int barId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.DurabilityBarDefinitionId);
            int textId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.DurabilityTextDefinitionId);

            Assert.That(CountPresentersByDef(engine, rootId), Is.EqualTo(1));
            Assert.That(CountPresentersByDef(engine, leftId), Is.EqualTo(1));
            Assert.That(CountPresentersByDef(engine, rightId), Is.EqualTo(1));
            Assert.That(CountPresentersByDef(engine, chimneyId), Is.EqualTo(1));
            Assert.That(CountPresentersByDef(engine, smokeId), Is.EqualTo(1), "Smoke presenter should exist as a steady child.");
            Assert.That(CountPresentersByDef(engine, routeId), Is.EqualTo(1));
            Assert.That(CountPresentersByDef(engine, decalId), Is.EqualTo(1));
            Assert.That(CountPresentersByDef(engine, workerId), Is.EqualTo(1), "Worker presenter should exist as a steady child.");
            Assert.That(CountPresentersByDef(engine, barId), Is.EqualTo(1));
            Assert.That(CountPresentersByDef(engine, textId), Is.EqualTo(1));
            Assert.That(
                CountPresentersByDef(engine, ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.FieldMarkerDefinitionId)),
                Is.EqualTo(2),
                "The same field-marker definition should spawn twice with different instance overrides.");
        }

        [Test]
        public void BlacksmithShowcase_ChildTransformOverride_SameDefinitionTwoPoses()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 12);

            var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
            PresenterDefinition root = definitions.Get(definitions.GetId(PresenterBlacksmithShowcaseIds.RootDefinitionId));
            int markerDefId = definitions.GetId(PresenterBlacksmithShowcaseIds.FieldMarkerDefinitionId);
            var authored = new List<ChildPresenterRef>();
            for (int i = 0; i < root.Children.Length; i++)
            {
                if (root.Children[i].DefinitionId == markerDefId)
                {
                    authored.Add(root.Children[i]);
                }
            }

            Assert.That(authored.Count, Is.EqualTo(2));
            Assert.That(authored[0].TransformOverride.HasOverride, Is.True);
            Assert.That(authored[1].TransformOverride.HasOverride, Is.True);
            Assert.That(authored[0].TransformOverride.LocalScale.X, Is.Not.EqualTo(authored[1].TransformOverride.LocalScale.X).Within(0.01f));

            var markers = new List<Entity>();
            var query = new QueryDescription().WithAll<PresenterState>();
            engine.World.Query(in query, (Entity entity, ref PresenterState state) =>
            {
                if (state.DefId == markerDefId)
                {
                    markers.Add(entity);
                }
            });

            Assert.That(markers.Count, Is.EqualTo(2));
            Vector3 left = engine.World.Get<PresenterWorldPosition>(markers[0]).Value;
            Vector3 right = engine.World.Get<PresenterWorldPosition>(markers[1]).Value;
            Vector3 leftScale = engine.World.Get<PresenterWorldScale>(markers[0]).Value;
            Vector3 rightScale = engine.World.Get<PresenterWorldScale>(markers[1]).Value;
            if (left.X > right.X)
            {
                (left, right) = (right, left);
                (leftScale, rightScale) = (rightScale, leftScale);
            }

            Assert.That(right.X - left.X, Is.GreaterThan(6f), "The two field markers should stand on opposite sides of the smithy.");
            Assert.That(rightScale.X, Is.GreaterThan(leftScale.X * 1.5f), "The right marker should read larger than the left marker.");
        }

        [Test]
        public void BlacksmithShowcase_MapLoad_BindsDeclaredVisualHeightmapTruth()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 4);

            IVisualHeightmap heightmap = engine.GetService(CoreServiceKeys.VisualHeightmap);
            Assert.That(heightmap, Is.TypeOf<VisualHeightmapRuntime>());
            Assert.That(heightmap.TrySampleHeightCm(0f, 0f, out float centerHeightCm), Is.True);
            Assert.That(centerHeightCm, Is.EqualTo(0f).Within(0.001f));
            Assert.That(heightmap.TrySampleHeightCm(240000f, 0f, out float edgeHeightCm), Is.True);
            Assert.That(edgeHeightCm, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void BlacksmithShowcase_MapLoad_EmitsHudSplineAndDecal()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 12);

            var splines = engine.GetService(CoreServiceKeys.RoadSplineBuffer)
                ?? throw new InvalidOperationException("RoadSplineBuffer missing.");
            var overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer missing.");
            var hud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer missing.");

            Assert.That(splines.Count, Is.GreaterThanOrEqualTo(1), "Worker route spline should be emitted.");
            Assert.That(overlays.Count, Is.GreaterThanOrEqualTo(1), "Forge decal ground overlay should be emitted.");
            Assert.That(CountWorldHudItems(engine, WorldHudItemKind.Bar), Is.GreaterThanOrEqualTo(1), "Durability HUD bar should be emitted.");
            Assert.That(CountWorldHudItems(engine, WorldHudItemKind.Text), Is.GreaterThanOrEqualTo(1), "Durability HUD text should be emitted.");

            float maxBar = 0f;
            float maxTextCurrent = 0f;
            float maxTextBase = 0f;
            foreach (ref readonly WorldHudItem item in hud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    maxBar = MathF.Max(maxBar, item.Value0);
                }
                else if (item.Kind == WorldHudItemKind.Text)
                {
                    maxTextCurrent = MathF.Max(maxTextCurrent, item.Value0);
                    maxTextBase = MathF.Max(maxTextBase, item.Value1);
                }
            }

            Assert.That(maxBar, Is.GreaterThan(0f));
            Assert.That(maxTextCurrent, Is.GreaterThan(0f));
            Assert.That(maxTextBase, Is.GreaterThan(0f));
        }

        [Test]
        public void BlacksmithShowcase_WorkingTag_TogglesSmokeWorkerAndSoundBehavior()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 12);

            Entity building = FindBlacksmithEntity(engine);
            Assert.That(building, Is.Not.EqualTo(Entity.Null));

            int smokeMeshId = ResolveMeshAssetId(engine, "blacksmith.smoke.billboard");
            int workerMeshId = ResolveMeshAssetId(engine, "blacksmith.worker.knight");
            int smokeDefId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.SmokeDefinitionId);
            int workerDefId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.WorkerDefinitionId);
            var sounds = engine.GetService(CoreServiceKeys.SoundRequestBuffer)
                ?? throw new InvalidOperationException("SoundRequestBuffer missing.");
            var presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
                ?? throw new InvalidOperationException("PresenterEntityRuntime missing.");

            Entity smokeEntity = FindEntityByDef(engine, smokeDefId);
            Entity workerEntity = FindEntityByDef(engine, workerDefId);
            Assert.That(smokeEntity, Is.Not.EqualTo(Entity.Null));
            Assert.That(workerEntity, Is.Not.EqualTo(Entity.Null));

            Assert.That(CountVisiblePrimitivesByMesh(engine, smokeMeshId), Is.EqualTo(0), "Smoke should start hidden.");
            Assert.That(CountVisibleSkinnedByMesh(engine, workerMeshId), Is.EqualTo(0), "Worker should start hidden.");
            Assert.That(sounds.Count, Is.EqualTo(0), "Worker loop sound should start inactive.");

            SetWorkingTag(engine, building, active: true);
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 8);

            Assert.That(CountVisiblePrimitivesByMesh(engine, smokeMeshId), Is.GreaterThanOrEqualTo(1), "Smoke should become visible when working.");
            Assert.That(CountVisibleSkinnedByMesh(engine, workerMeshId), Is.GreaterThanOrEqualTo(1), "Worker should become visible when working.");
            Assert.That(sounds.Count, Is.GreaterThanOrEqualTo(1), "Worker sound loop should emit when working.");
            Assert.That((engine.World.Get<PresenterState>(smokeEntity).BehaviorActiveMask & (1u << 0)) != 0u, Is.True, "Smoke asset binding slot should be active.");
            Assert.That((engine.World.Get<PresenterState>(workerEntity).BehaviorActiveMask & (1u << 0)) != 0u, Is.True, "Worker skinned mesh slot should be active.");
            Assert.That((engine.World.Get<PresenterState>(workerEntity).BehaviorActiveMask & (1u << 4)) != 0u, Is.True, "Worker animator slot should be active.");
            Assert.That((engine.World.Get<PresenterState>(workerEntity).BehaviorActiveMask & (1u << 6)) != 0u, Is.True, "Worker sound slot should be active.");
            Assert.That((engine.World.Get<PresenterState>(workerEntity).BehaviorActiveMask & (1u << 7)) != 0u, Is.True, "Worker spline slot should be active.");

            SetWorkingTag(engine, building, active: false);
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 8);

            Assert.That(CountVisiblePrimitivesByMesh(engine, smokeMeshId), Is.EqualTo(0), "Smoke should hide when working tag is removed.");
            Assert.That(CountVisibleSkinnedByMesh(engine, workerMeshId), Is.EqualTo(0), "Worker should hide when working tag is removed.");
            Assert.That((engine.World.Get<PresenterState>(smokeEntity).BehaviorActiveMask & (1u << 0)) == 0u, Is.True);
            Assert.That((engine.World.Get<PresenterState>(workerEntity).BehaviorActiveMask & ((1u << 0) | (1u << 4) | (1u << 6) | (1u << 7))) == 0u, Is.True, "Worker behavior slots should all deactivate.");
        }

        [Test]
        public void BlacksmithShowcase_GlobalDayNight_UpdatesRootParam()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            var globalEvents = engine.GetService(CoreServiceKeys.GlobalPresentationEventBuffer)
                ?? throw new InvalidOperationException("GlobalPresentationEventBuffer missing.");
            globalEvents.AddDayNight(PresenterBlacksmithShowcaseIds.ParamDayNight, 1f);
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 4);

            var presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
                ?? throw new InvalidOperationException("PresenterEntityRuntime missing.");
            Entity rootEntity = FindEntityByDef(engine, ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.RootDefinitionId));
            Assert.That(rootEntity, Is.Not.EqualTo(Entity.Null));

            float param = presenters.ResolveFloat(rootEntity, PresenterBlacksmithShowcaseIds.ParamDayNight, -1f);
            Assert.That(param, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void BlacksmithShowcase_GlobalRegionChanged_SwapsWorkshopMeshes()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            int northAssetId = ResolveMeshAssetId(engine, "blacksmith.building.north.intact");
            int southAssetId = ResolveMeshAssetId(engine, "blacksmith.building.south.intact");
            Assert.That(CountVisiblePrimitivesByMesh(engine, northAssetId), Is.EqualTo(1), "Precondition: left workshop should start as north.");
            Assert.That(CountVisiblePrimitivesByMesh(engine, southAssetId), Is.EqualTo(1), "Precondition: right workshop should start as south.");

            var globalEvents = engine.GetService(CoreServiceKeys.GlobalPresentationEventBuffer)
                ?? throw new InvalidOperationException("GlobalPresentationEventBuffer missing.");
            globalEvents.AddRegionChanged(1, 0);
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 4);

            Assert.That(CountVisiblePrimitivesByMesh(engine, northAssetId), Is.EqualTo(0), "Both workshops should leave north after region 1.");
            Assert.That(CountVisiblePrimitivesByMesh(engine, southAssetId), Is.EqualTo(2), "Both workshops should resolve to south after region 1.");
        }

        [Test]
        public void BlacksmithShowcase_DurabilityEffects_SwapWorkshopMeshesAndHudValues()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            Entity building = FindBlacksmithEntity(engine);
            Assert.That(building, Is.Not.EqualTo(Entity.Null));
            var effectQueue = engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue missing.");
            var hud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer missing.");
            var timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
                ?? throw new InvalidOperationException("PresentationTimingDiagnostics missing.");

            int damagedAssetId = ResolveMeshAssetId(engine, "blacksmith.building.damaged");
            int ruinedAssetId = ResolveMeshAssetId(engine, "blacksmith.building.ruined");

            int damagedEffectId = EffectTemplateIdRegistry.GetId(PresenterBlacksmithShowcaseIds.EffectSetDurabilityDamaged);
            int ruinedEffectId = EffectTemplateIdRegistry.GetId(PresenterBlacksmithShowcaseIds.EffectSetDurabilityRuined);
            Assert.That(damagedEffectId, Is.GreaterThan(0));
            Assert.That(ruinedEffectId, Is.GreaterThan(0));

            effectQueue.Publish(new EffectRequest
            {
                Source = building,
                Target = building,
                TargetContext = building,
                TemplateId = damagedEffectId,
            });
            int observedOwnerAttributeChanges = 0;
            for (int i = 0; i < 12; i++)
            {
                engine.Tick(1f / 60f);
                observedOwnerAttributeChanges += timings.PresenterOwnerAttributeChangesLastFrame;
            }
            Assert.That(observedOwnerAttributeChanges, Is.GreaterThan(0), "Durability effect must reach PresenterBehaviorSystem as an owner attribute change.");

            int durabilityId = AttributeRegistry.GetId("Durability");
            ref AttributeBuffer damagedAttributes = ref engine.World.Get<AttributeBuffer>(building);
            Assert.That(damagedAttributes.GetCurrent(durabilityId), Is.EqualTo(50f).Within(0.001f));
            Assert.That(damagedAttributes.GetBase(durabilityId), Is.EqualTo(100f).Within(0.001f));
            var defs = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
            var runtime = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)!;
            int leftDefId = defs.GetId(PresenterBlacksmithShowcaseIds.WorkshopLeftDefinitionId);
            int rightDefId = defs.GetId(PresenterBlacksmithShowcaseIds.WorkshopRightDefinitionId);
            Assert.That(defs.TryGet(leftDefId, out PresenterDefinition leftDefinition), Is.True);
            Assert.That(defs.TryGet(rightDefId, out PresenterDefinition rightDefinition), Is.True);
            Assert.That(HasDurabilityAttributeBinding(leftDefinition, durabilityId), Is.True, "Left workshop must keep inherited Durability AttributeBinding behavior.");
            Assert.That(HasDurabilityAttributeBinding(rightDefinition, durabilityId), Is.True, "Right workshop must keep inherited Durability AttributeBinding behavior.");
            Assert.That(runtime.GetActiveByOwnerDefinition(leftDefId, building).Count, Is.EqualTo(1));
            Assert.That(runtime.GetActiveByOwnerDefinition(rightDefId, building).Count, Is.EqualTo(1));
            var q = new QueryDescription().WithAll<PresenterState>();
            int durabilityParamKey = PresenterBlacksmithShowcaseIds.ParamDurability;
            int workshopAssetStateParamKey = PresenterBlacksmithShowcaseIds.ParamWorkshopAssetState;
            int damagedWorkshopParamCount = 0;
            string workshopParamSummary = string.Empty;
            engine.World.Query(in q, (Entity entity, ref PresenterState state) =>
            {
                if (state.DefId == leftDefId || state.DefId == rightDefId)
                {
                    var ints = engine.World.Get<PresenterIntParams>(entity);
                    var floats = engine.World.Get<PresenterFloatParams>(entity);
                    ints.TryGet(workshopAssetStateParamKey, out int assetState);
                    floats.TryGet(durabilityParamKey, out float durabilityRatio);
                    workshopParamSummary += $"def={state.DefId} durability={durabilityRatio:0.###} assetState={assetState};";
                    if (assetState == 2 && MathF.Abs(durabilityRatio - 0.5f) <= 0.001f)
                    {
                        damagedWorkshopParamCount++;
                    }
                }
            });
            Assert.That(damagedWorkshopParamCount, Is.EqualTo(2), workshopParamSummary);
            Assert.That(CountVisiblePrimitivesByMesh(engine, damagedAssetId), Is.EqualTo(2));
            Assert.That(CaptureHudBarValue(hud), Is.EqualTo(0.5f).Within(0.05f));

            effectQueue.Publish(new EffectRequest
            {
                Source = building,
                Target = building,
                TargetContext = building,
                TemplateId = ruinedEffectId,
            });
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 12);

            Assert.That(CountVisiblePrimitivesByMesh(engine, ruinedAssetId), Is.EqualTo(2));
            Assert.That(CaptureHudBarValue(hud), Is.EqualTo(0f).Within(0.05f));
        }

        [Test]
        public void BlacksmithShowcase_SmokeIsParentedUnderChimney()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            int smokeDefId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.SmokeDefinitionId);
            int chimneyDefId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.ChimneyDefinitionId);
            Entity smokeEntity = FindEntityByDef(engine, smokeDefId);
            Assert.That(smokeEntity, Is.Not.EqualTo(Entity.Null));

            ref readonly PresenterParent smokeParent = ref engine.World.Get<PresenterParent>(smokeEntity);
            Assert.That(smokeParent.Parent, Is.Not.EqualTo(Entity.Null), "Smoke should have a parent presenter.");
            Assert.That(engine.World.IsAlive(smokeParent.Parent), Is.True);
            Assert.That(engine.World.Get<PresenterState>(smokeParent.Parent).DefId, Is.EqualTo(chimneyDefId), "Smoke should be parented under the chimney presenter.");
        }

        [Test]
        public void BlacksmithShowcase_DestroyAndRespawn_RebuildsEntireCanonicalTree()
        {
            using var engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            Entity building = FindBlacksmithEntity(engine);
            Assert.That(building, Is.Not.EqualTo(Entity.Null));
            int rootId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.RootDefinitionId);
            int leftId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.WorkshopLeftDefinitionId);
            int rightId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.WorkshopRightDefinitionId);
            int chimneyId = ResolvePresenterDefId(engine, PresenterBlacksmithShowcaseIds.ChimneyDefinitionId);

            engine.World.Destroy(building);
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 8);

            Assert.That(CountPresentersByDef(engine, rootId), Is.EqualTo(0));
            Assert.That(CountPresentersByDef(engine, leftId), Is.EqualTo(0));
            Assert.That(CountPresentersByDef(engine, rightId), Is.EqualTo(0));
            Assert.That(CountPresentersByDef(engine, chimneyId), Is.EqualTo(0));

            var spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = PresenterBlacksmithShowcaseIds.TemplateId,
                MapId = engine.CurrentMapSession?.MapId ?? default,
                WorldPositionCm = default,
                HasFacing = 1,
                FacingAngleRad = 0f,
            };

            Assert.That(spawnQueue.TryEnqueue(in request), Is.True);
            PresenterBlacksmithShowcaseTestHarness.Tick(engine, 12);

            Assert.That(CountPresentersByDef(engine, rootId), Is.EqualTo(1));
            Assert.That(CountPresentersByDef(engine, leftId), Is.EqualTo(1));
            Assert.That(CountPresentersByDef(engine, rightId), Is.EqualTo(1));
            Assert.That(CountPresentersByDef(engine, chimneyId), Is.EqualTo(1));
            Assert.That(CountWorldHudItems(engine, WorldHudItemKind.Bar), Is.GreaterThanOrEqualTo(1));
            Assert.That(CountWorldHudItems(engine, WorldHudItemKind.Text), Is.GreaterThanOrEqualTo(1));
        }

        private static float CaptureHudBarValue(WorldHudBatchBuffer hud)
        {
            foreach (ref readonly WorldHudItem item in hud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    return item.Value0;
                }
            }

            return 0f;
        }

        private static bool HasDurabilityAttributeBinding(PresenterDefinition definition, int durabilityId)
        {
            for (int i = 0; i < definition.Behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref definition.Behaviors[i];
                if (slot.Kind == BehaviorKind.AttributeBinding &&
                    slot.AttributeBinding.AttributeId == durabilityId &&
                    slot.AttributeBinding.TargetParamKey == PresenterBlacksmithShowcaseIds.ParamDurability)
                {
                    return true;
                }
            }

            return false;
        }

    }
}
