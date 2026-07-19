using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class VisionResolverTests
    {
        [Test]
        public void VisionResolver_RasterizesDiskConeBoxAndLineApertures()
        {
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerDefinition layer = registry.Get(layerId);

            var disk = new FogField(1, in layer);
            var cone = new FogField(1, in layer);
            var box = new FogField(1, in layer);
            var line = new FogField(1, in layer);
            var resolver = new VisionResolver(registry, new FogFieldStore());
            uint mask = registry.ToMask(layerId);

            resolver.RasterizeIntoField(
                new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, mask, VisionPolarity.Reveal, VisionAperture.Disk(150)),
                disk,
                FogRulesPolicy.Default);
            resolver.RasterizeIntoField(
                new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, mask, VisionPolarity.Reveal, VisionAperture.Cone(250, 45)),
                cone,
                FogRulesPolicy.Default);
            resolver.RasterizeIntoField(
                new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, mask, VisionPolarity.Reveal, VisionAperture.Box(50, 150)),
                box,
                FogRulesPolicy.Default);
            resolver.RasterizeIntoField(
                new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, mask, VisionPolarity.Reveal, VisionAperture.Line(250, 50)),
                line,
                FogRulesPolicy.Default);

            Assert.Multiple(() =>
            {
                Assert.That(disk.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Visible));
                Assert.That(disk.GetVisibility(new FogCell(2, 0)), Is.EqualTo(CellVisibility.Unseen));
                Assert.That(cone.GetVisibility(new FogCell(1, 0)), Is.EqualTo(CellVisibility.Visible));
                Assert.That(cone.GetVisibility(new FogCell(-1, 0)), Is.EqualTo(CellVisibility.Unseen));
                Assert.That(box.GetVisibility(new FogCell(1, 0)), Is.EqualTo(CellVisibility.Visible));
                Assert.That(box.GetVisibility(new FogCell(0, 1)), Is.EqualTo(CellVisibility.Unseen));
                Assert.That(line.GetVisibility(new FogCell(1, 0)), Is.EqualTo(CellVisibility.Visible));
                Assert.That(line.GetVisibility(new FogCell(0, 1)), Is.EqualTo(CellVisibility.Unseen));
            });
        }

        [Test]
        public void VisionResolver_AppliesVerticalRuleIndependentlyFromLineOfSight()
        {
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerDefinition layer = registry.Get(layerId);
            var map = new FogCellMap();
            map.SetHeightTier(new FogCell(1, 0), 2);

            var lowField = new FogField(1, in layer);
            var tolerantField = new FogField(1, in layer);
            var resolver = new VisionResolver(registry, new FogFieldStore(), elevation: map);
            uint mask = registry.ToMask(layerId);

            resolver.RasterizeIntoField(
                new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, mask, VisionPolarity.Reveal, VisionAperture.Disk(200), altitudeBand: 0),
                lowField,
                new FogRulesPolicy(verticalEnabled: true, lineOfSightEnabled: false, upTolerance: 0));
            resolver.RasterizeIntoField(
                new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, mask, VisionPolarity.Reveal, VisionAperture.Disk(200), altitudeBand: 0),
                tolerantField,
                new FogRulesPolicy(verticalEnabled: true, lineOfSightEnabled: false, upTolerance: 2));

            Assert.That(lowField.GetVisibility(new FogCell(1, 0)), Is.EqualTo(CellVisibility.Unseen));
            Assert.That(tolerantField.GetVisibility(new FogCell(1, 0)), Is.EqualTo(CellVisibility.Visible));
        }

        [Test]
        public void VisionResolver_LineOfSightBlocksCellsAndCanBeDisabled()
        {
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerDefinition layer = registry.Get(layerId);
            var map = new FogCellMap();
            map.SetOpaque(new FogCell(1, 0), true);

            var blocked = new FogField(1, in layer);
            var disabled = new FogField(1, in layer);
            var resolver = new VisionResolver(registry, new FogFieldStore(), occlusion: map);
            uint mask = registry.ToMask(layerId);
            var emitter = new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, mask, VisionPolarity.Reveal, VisionAperture.Line(350, 50));

            resolver.RasterizeIntoField(emitter, blocked, FogRulesPolicy.Default);
            resolver.RasterizeIntoField(emitter, disabled, new FogRulesPolicy(lineOfSightEnabled: false));

            Assert.That(blocked.GetVisibility(new FogCell(2, 0)), Is.EqualTo(CellVisibility.Unseen));
            Assert.That(disabled.GetVisibility(new FogCell(2, 0)), Is.EqualTo(CellVisibility.Visible));
        }

        [Test]
        public void VisionResolver_DenyPolarityHonorsDenyModeAndLayerMasks()
        {
            var registry = new FogLayerRegistry();
            FogLayerId groundId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerId airId = registry.Register("air", cellSizeCm: 100, updateHz: 5);
            var fields = new FogFieldStore();
            var resolver = new VisionResolver(registry, fields);
            uint groundMask = registry.ToMask(groundId);
            uint airMask = registry.ToMask(airId);
            FogLayerId[] layers = { groundId, airId };

            resolver.Resolve(
                new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, groundMask | airMask, VisionPolarity.Reveal, VisionAperture.Disk(150)),
                layers,
                new FogRulesPolicy(denyMode: FogDenyMode.DenyDominates));
            resolver.Resolve(
                new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, groundMask, VisionPolarity.Deny, VisionAperture.Disk(150)),
                layers,
                new FogRulesPolicy(denyMode: FogDenyMode.DenyDominates));

            Assert.That(fields.TryGet(1, groundId, out FogField ground), Is.True);
            Assert.That(fields.TryGet(1, airId, out FogField air), Is.True);
            Assert.That(ground.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Denied));
            Assert.That(air.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Visible));

            var revealDominates = new FogField(1, registry.Get(groundId));
            revealDominates.SetVisible(new FogCell(0, 0));
            resolver.RasterizeIntoField(
                new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, groundMask, VisionPolarity.Deny, VisionAperture.Disk(100)),
                revealDominates,
                new FogRulesPolicy(denyMode: FogDenyMode.RevealDominates));
            Assert.That(revealDominates.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Visible));
        }

        [Test]
        public void VisionResolver_RelationshipGatedScopesOnlyApplyToLinkedTargets()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out RelationshipTypeRegistry types);
            int sharedVisionTypeId = types.Register("SharedVision");
            Entity sourceHost = world.Create();
            Entity linkedHost = world.Create();
            Entity unlinkedHost = world.Create();
            relationships.EnsureLink(sourceHost, linkedHost, sharedVisionTypeId);

            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            var fields = new FogFieldStore();
            var resolver = new VisionResolver(registry, fields, relationships: relationships);
            uint layerMask = registry.ToMask(layerId);
            FogLayerId[] layers = { layerId };
            FogScopeTarget[] targets =
            {
                new(scopeKeyId: 10, linkedHost),
                new(scopeKeyId: 11, unlinkedHost)
            };

            int changed = resolver.ResolveToScopes(
                new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, layerMask, VisionPolarity.Deny, VisionAperture.Disk(100)),
                targets,
                layers,
                new FogRulesPolicy(denyMode: FogDenyMode.DenyDominates),
                new FogRelationshipRule(sourceHost, sharedVisionTypeId));

            Assert.That(changed, Is.GreaterThan(0));
            Assert.That(fields.TryGet(10, layerId, out FogField linkedField), Is.True);
            Assert.That(linkedField.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Denied));
            Assert.That(fields.TryGet(11, layerId, out _), Is.False);
        }

        [Test]
        public void VisionResolver_RelationshipGatedScopesRequireRelationshipRuntime()
        {
            using World world = World.Create();
            Entity sourceHost = world.Create();
            Entity targetHost = world.Create();
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            var resolver = new VisionResolver(registry, new FogFieldStore());
            uint layerMask = registry.ToMask(layerId);
            FogScopeTarget[] targets = { new(scopeKeyId: 10, targetHost) };
            FogLayerId[] layers = { layerId };

            Assert.That(
                () => resolver.ResolveToScopes(
                    new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, layerMask, VisionPolarity.Deny, VisionAperture.Disk(100)),
                    targets,
                    layers,
                    FogRulesPolicy.Default,
                    new FogRelationshipRule(sourceHost, relationshipTypeId: 0)),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("RelationshipRuntime"));
        }

        [Test]
        public void VisionSystem_DrivesFogResolveAndKnowledgeProjectionFromEcsComponents()
        {
            using World world = World.Create();
            var session = new GameSession();
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            uint layerMask = registry.ToMask(layerId);
            var fields = new FogFieldStore();
            var knowledge = new KnowledgeProjectionStore();
            var cellMap = new FogCellMap();
            var resolver = new VisionResolver(registry, fields, elevation: cellMap, occlusion: cellMap);
            var projector = new FogKnowledgeProjector(knowledge, cellMap);
            var system = new VisionSystem(world, session, registry, fields, resolver, projector, knowledge);

            Entity viewer = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new VisionEmitterCm
                {
                    ScopeKeyId = 1,
                    LayerMask = layerMask,
                    Polarity = VisionPolarity.Reveal,
                    Aperture = VisionAperture.Disk(150)
                });
            Entity target = world.Create(
                WorldPositionCm.FromCm(50, 50),
                new FogOccupantCm { ExposeLayerMask = layerMask });

            system.Update(1f / 60f);

            Assert.That(fields.TryGet(1, layerId, out FogField field), Is.True);
            Assert.That(field.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Visible));
            Assert.That(knowledge.TryGet(viewer, target, session.CurrentTick, out KnowledgeDisclosureRecord record), Is.True);
            Assert.That(record.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
        }

        private static RelationshipRuntime CreateRelationshipRuntime(World world, out RelationshipTypeRegistry types)
        {
            types = new RelationshipTypeRegistry();
            return new RelationshipRuntime(
                world,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 4),
                new RelationshipReverseIndex(world));
        }
    }
}
