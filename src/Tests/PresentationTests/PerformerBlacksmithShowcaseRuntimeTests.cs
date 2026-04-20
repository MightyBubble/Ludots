using System;
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
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using NUnit.Framework;
using PerformerBlacksmithShowcaseMod;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PerformerBlacksmithShowcaseRuntimeTests
    {
        private static int ResolvePerformerDefId(Ludots.Core.Engine.GameEngine engine, string key)
        {
            var defs = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
            int id = defs.GetId(key);
            Assert.That(id, Is.GreaterThan(0), $"Performer definition '{key}' not registered.");
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

        private static int CountPerformersByDef(Ludots.Core.Engine.GameEngine engine, int defId)
        {
            var performers = engine.GetService(CoreServiceKeys.PerformerInstanceBuffer)
                ?? throw new InvalidOperationException("PerformerInstanceBuffer missing.");
            int count = 0;
            for (int handle = 0; handle < performers.Capacity; handle++)
            {
                if (performers.IsActive(handle) && performers.Get(handle).DefId == defId)
                {
                    count++;
                }
            }

            return count;
        }

        private static int FindHandleByDef(Ludots.Core.Engine.GameEngine engine, int defId)
        {
            var performers = engine.GetService(CoreServiceKeys.PerformerInstanceBuffer)
                ?? throw new InvalidOperationException("PerformerInstanceBuffer missing.");
            for (int handle = 0; handle < performers.Capacity; handle++)
            {
                if (performers.IsActive(handle) && performers.Get(handle).DefId == defId)
                {
                    return handle;
                }
            }

            return -1;
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
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.EntityName, StringComparison.OrdinalIgnoreCase))
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

            ref GameplayTagContainer tags = ref engine.World.Get<GameplayTagContainer>(building);
            ref TagCountContainer counts = ref engine.World.Get<TagCountContainer>(building);
            ref DirtyFlags dirty = ref engine.World.Get<DirtyFlags>(building);
            if (active)
            {
                tagOps.AddTag(ref tags, ref counts, workingTagId, ref dirty);
            }
            else
            {
                tagOps.RemoveTag(ref tags, ref counts, workingTagId, ref dirty);
            }
        }

        [Test]
        public void BlacksmithShowcase_MapLoad_BootstrapsCanonicalPerformerTree()
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 12);

            int rootId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.RootDefinitionId);
            int leftId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.WorkshopLeftDefinitionId);
            int rightId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.WorkshopRightDefinitionId);
            int chimneyId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.ChimneyDefinitionId);
            int smokeId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.SmokeDefinitionId);
            int routeId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.RouteSplineDefinitionId);
            int decalId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.DecalDefinitionId);
            int workerId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.WorkerDefinitionId);
            int barId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.DurabilityBarDefinitionId);
            int textId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.DurabilityTextDefinitionId);

            Assert.That(CountPerformersByDef(engine, rootId), Is.EqualTo(1));
            Assert.That(CountPerformersByDef(engine, leftId), Is.EqualTo(1));
            Assert.That(CountPerformersByDef(engine, rightId), Is.EqualTo(1));
            Assert.That(CountPerformersByDef(engine, chimneyId), Is.EqualTo(1));
            Assert.That(CountPerformersByDef(engine, smokeId), Is.EqualTo(1), "Smoke performer should exist as a steady child.");
            Assert.That(CountPerformersByDef(engine, routeId), Is.EqualTo(1));
            Assert.That(CountPerformersByDef(engine, decalId), Is.EqualTo(1));
            Assert.That(CountPerformersByDef(engine, workerId), Is.EqualTo(1), "Worker performer should exist as a steady child.");
            Assert.That(CountPerformersByDef(engine, barId), Is.EqualTo(1));
            Assert.That(CountPerformersByDef(engine, textId), Is.EqualTo(1));
        }

        [Test]
        public void BlacksmithShowcase_MapLoad_BindsDeclaredVisualHeightmapTruth()
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 4);

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
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 12);

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
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 12);

            Entity building = FindBlacksmithEntity(engine);
            Assert.That(building, Is.Not.EqualTo(Entity.Null));

            int smokeMeshId = ResolveMeshAssetId(engine, "blacksmith.smoke.billboard");
            int workerMeshId = ResolveMeshAssetId(engine, "blacksmith.worker.knight");
            int smokeDefId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.SmokeDefinitionId);
            int workerDefId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.WorkerDefinitionId);
            var sounds = engine.GetService(CoreServiceKeys.SoundRequestBuffer)
                ?? throw new InvalidOperationException("SoundRequestBuffer missing.");
            var performers = engine.GetService(CoreServiceKeys.PerformerInstanceBuffer)
                ?? throw new InvalidOperationException("PerformerInstanceBuffer missing.");

            int smokeHandle = FindHandleByDef(engine, smokeDefId);
            int workerHandle = FindHandleByDef(engine, workerDefId);
            Assert.That(smokeHandle, Is.GreaterThanOrEqualTo(0));
            Assert.That(workerHandle, Is.GreaterThanOrEqualTo(0));

            Assert.That(CountVisiblePrimitivesByMesh(engine, smokeMeshId), Is.EqualTo(0), "Smoke should start hidden.");
            Assert.That(CountVisibleSkinnedByMesh(engine, workerMeshId), Is.EqualTo(0), "Worker should start hidden.");
            Assert.That(sounds.Count, Is.EqualTo(0), "Worker loop sound should start inactive.");

            SetWorkingTag(engine, building, active: true);
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 8);

            Assert.That(CountVisiblePrimitivesByMesh(engine, smokeMeshId), Is.GreaterThanOrEqualTo(1), "Smoke should become visible when working.");
            Assert.That(CountVisibleSkinnedByMesh(engine, workerMeshId), Is.GreaterThanOrEqualTo(1), "Worker should become visible when working.");
            Assert.That(sounds.Count, Is.GreaterThanOrEqualTo(1), "Worker sound loop should emit when working.");
            Assert.That((performers.Get(smokeHandle).BehaviorActiveMask & (1u << 0)) != 0u, Is.True, "Smoke asset binding slot should be active.");
            Assert.That((performers.Get(workerHandle).BehaviorActiveMask & (1u << 0)) != 0u, Is.True, "Worker skinned mesh slot should be active.");
            Assert.That((performers.Get(workerHandle).BehaviorActiveMask & (1u << 1)) != 0u, Is.True, "Worker animator slot should be active.");
            Assert.That((performers.Get(workerHandle).BehaviorActiveMask & (1u << 2)) != 0u, Is.True, "Worker sound slot should be active.");
            Assert.That((performers.Get(workerHandle).BehaviorActiveMask & (1u << 3)) != 0u, Is.True, "Worker spline slot should be active.");

            SetWorkingTag(engine, building, active: false);
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 8);

            Assert.That(CountVisiblePrimitivesByMesh(engine, smokeMeshId), Is.EqualTo(0), "Smoke should hide when working tag is removed.");
            Assert.That(CountVisibleSkinnedByMesh(engine, workerMeshId), Is.EqualTo(0), "Worker should hide when working tag is removed.");
            Assert.That((performers.Get(smokeHandle).BehaviorActiveMask & (1u << 0)) == 0u, Is.True);
            Assert.That((performers.Get(workerHandle).BehaviorActiveMask & 0b1111u) == 0u, Is.True, "Worker behavior slots should all deactivate.");
        }

        [Test]
        public void BlacksmithShowcase_GlobalDayNight_UpdatesRootParam()
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            var globalEvents = engine.GetService(CoreServiceKeys.GlobalPresentationEventBuffer)
                ?? throw new InvalidOperationException("GlobalPresentationEventBuffer missing.");
            globalEvents.AddDayNight(PerformerBlacksmithShowcaseIds.ParamDayNight, 1f);
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 4);

            var performers = engine.GetService(CoreServiceKeys.PerformerInstanceBuffer)
                ?? throw new InvalidOperationException("PerformerInstanceBuffer missing.");
            int rootHandle = FindHandleByDef(engine, ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.RootDefinitionId));
            Assert.That(rootHandle, Is.GreaterThanOrEqualTo(0));

            float param = performers.Blackboard.ResolveFloat(rootHandle, PerformerBlacksmithShowcaseIds.ParamDayNight, -1f);
            Assert.That(param, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void BlacksmithShowcase_GlobalRegionChanged_SwapsWorkshopMeshes()
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            int northAssetId = ResolveMeshAssetId(engine, "blacksmith.building.north.intact");
            int southAssetId = ResolveMeshAssetId(engine, "blacksmith.building.south.intact");
            Assert.That(CountVisiblePrimitivesByMesh(engine, northAssetId), Is.EqualTo(1), "Precondition: left workshop should start as north.");
            Assert.That(CountVisiblePrimitivesByMesh(engine, southAssetId), Is.EqualTo(1), "Precondition: right workshop should start as south.");

            var globalEvents = engine.GetService(CoreServiceKeys.GlobalPresentationEventBuffer)
                ?? throw new InvalidOperationException("GlobalPresentationEventBuffer missing.");
            globalEvents.AddRegionChanged(1, 0);
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 4);

            Assert.That(CountVisiblePrimitivesByMesh(engine, northAssetId), Is.EqualTo(0), "Both workshops should leave north after region 1.");
            Assert.That(CountVisiblePrimitivesByMesh(engine, southAssetId), Is.EqualTo(2), "Both workshops should resolve to south after region 1.");
        }

        [Test]
        public void BlacksmithShowcase_DurabilityEffects_SwapWorkshopMeshesAndHudValues()
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            Entity building = FindBlacksmithEntity(engine);
            Assert.That(building, Is.Not.EqualTo(Entity.Null));
            var effectQueue = engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue missing.");
            var hud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer missing.");

            int damagedAssetId = ResolveMeshAssetId(engine, "blacksmith.building.damaged");
            int ruinedAssetId = ResolveMeshAssetId(engine, "blacksmith.building.ruined");

            int damagedEffectId = EffectTemplateIdRegistry.GetId(PerformerBlacksmithShowcaseIds.EffectSetDurabilityDamaged);
            int ruinedEffectId = EffectTemplateIdRegistry.GetId(PerformerBlacksmithShowcaseIds.EffectSetDurabilityRuined);
            Assert.That(damagedEffectId, Is.GreaterThan(0));
            Assert.That(ruinedEffectId, Is.GreaterThan(0));

            effectQueue.Publish(new EffectRequest
            {
                Source = building,
                Target = building,
                TargetContext = building,
                TemplateId = damagedEffectId,
            });
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 12);

            Assert.That(CountVisiblePrimitivesByMesh(engine, damagedAssetId), Is.EqualTo(2));
            Assert.That(CaptureHudBarValue(hud), Is.EqualTo(0.5f).Within(0.05f));

            effectQueue.Publish(new EffectRequest
            {
                Source = building,
                Target = building,
                TargetContext = building,
                TemplateId = ruinedEffectId,
            });
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 12);

            Assert.That(CountVisiblePrimitivesByMesh(engine, ruinedAssetId), Is.EqualTo(2));
            Assert.That(CaptureHudBarValue(hud), Is.EqualTo(0f).Within(0.05f));
        }

        [Test]
        public void BlacksmithShowcase_SmokeIsParentedUnderChimney()
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            int smokeDefId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.SmokeDefinitionId);
            int chimneyDefId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.ChimneyDefinitionId);
            var performers = engine.GetService(CoreServiceKeys.PerformerInstanceBuffer)
                ?? throw new InvalidOperationException("PerformerInstanceBuffer missing.");
            int smokeHandle = FindHandleByDef(engine, smokeDefId);
            Assert.That(smokeHandle, Is.GreaterThanOrEqualTo(0));

            PerformerInstance smoke = performers.Get(smokeHandle);
            Assert.That(smoke.ParentHandle, Is.GreaterThanOrEqualTo(0), "Smoke should have a parent performer handle.");
            Assert.That(performers.IsActive(smoke.ParentHandle), Is.True);
            Assert.That(performers.Get(smoke.ParentHandle).DefId, Is.EqualTo(chimneyDefId), "Smoke should be parented under the chimney performer.");
        }

        [Test]
        public void BlacksmithShowcase_DestroyAndRespawn_RebuildsEntireCanonicalTree()
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            Entity building = FindBlacksmithEntity(engine);
            Assert.That(building, Is.Not.EqualTo(Entity.Null));
            int rootId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.RootDefinitionId);
            int leftId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.WorkshopLeftDefinitionId);
            int rightId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.WorkshopRightDefinitionId);
            int chimneyId = ResolvePerformerDefId(engine, PerformerBlacksmithShowcaseIds.ChimneyDefinitionId);

            engine.World.Destroy(building);
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 8);

            Assert.That(CountPerformersByDef(engine, rootId), Is.EqualTo(0));
            Assert.That(CountPerformersByDef(engine, leftId), Is.EqualTo(0));
            Assert.That(CountPerformersByDef(engine, rightId), Is.EqualTo(0));
            Assert.That(CountPerformersByDef(engine, chimneyId), Is.EqualTo(0));

            var spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = PerformerBlacksmithShowcaseIds.TemplateId,
                MapId = engine.CurrentMapSession?.MapId ?? default,
                WorldPositionCm = default,
                HasFacing = 1,
                FacingAngleRad = 0f,
            };

            Assert.That(spawnQueue.TryEnqueue(in request), Is.True);
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 12);

            Assert.That(CountPerformersByDef(engine, rootId), Is.EqualTo(1));
            Assert.That(CountPerformersByDef(engine, leftId), Is.EqualTo(1));
            Assert.That(CountPerformersByDef(engine, rightId), Is.EqualTo(1));
            Assert.That(CountPerformersByDef(engine, chimneyId), Is.EqualTo(1));
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
    }
}
