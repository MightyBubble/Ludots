using System;
using System.IO;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[Category("ci-gate")]
[Category("acceptance")]
public sealed class FogOfWarShowcaseAcceptanceTests
{
    [Test]
    public void FogOfWarShowcase_CoreScenarioCoversWarFogEpic()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "fog-of-war-showcase");
        Directory.CreateDirectory(artifactDir);
        foreach (string file in Directory.GetFiles(artifactDir))
        {
            File.Delete(file);
        }

        using World world = World.Create();
        Entity viewer = world.Create();
        Entity source = world.Create();
        Entity visibleTarget = world.Create();
        Entity memoryTarget = world.Create();
        Entity concealedTarget = world.Create();
        Entity stealthTarget = world.Create();
        Entity sharingSourceHost = world.Create();
        Entity sharingTargetHost = world.Create();
        RelationshipRuntime relationships = CreateRelationshipRuntime(world, out RelationshipTypeRegistry relationshipTypes);
        int sharedVisionTypeId = relationshipTypes.Register("SharedVision");

        var registry = new FogLayerRegistry();
        FogLayerId groundId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
        FogLayerId airId = registry.Register("air", cellSizeCm: 250, updateHz: 5);
        FogLayerId detectionId = registry.Register("detection", cellSizeCm: 100, updateHz: 10);
        uint groundMask = registry.ToMask(groundId);
        uint airMask = registry.ToMask(airId);
        uint detectionMask = registry.ToMask(detectionId);
        var fields = new FogFieldStore();
        var map = new FogCellMap();
        map.SetHeightTier(new FogCell(2, 0), 2);
        map.SetOpaque(new FogCell(1, 1), true);
        map.SetConcealed(new FogCell(2, 2), true);
        var resolver = new VisionResolver(registry, fields, elevation: map, occlusion: map);
        FogLayerId[] layers = { groundId, airId, detectionId };

        resolver.Resolve(
            new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, groundMask | airMask, VisionPolarity.Reveal, VisionAperture.Cone(400, 45), altitudeBand: 0),
            layers,
            FogRulesPolicy.Default);
        resolver.Resolve(
            new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 45, groundMask, VisionPolarity.Reveal, VisionAperture.Line(450, 80), altitudeBand: 0),
            layers,
            FogRulesPolicy.Default);
        resolver.Resolve(
            new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, detectionMask, VisionPolarity.Reveal, VisionAperture.Disk(450), altitudeBand: 2, detectionStrength: 2, trueSightStrength: 2),
            layers,
            new FogRulesPolicy(verticalEnabled: true, lineOfSightEnabled: false, upTolerance: 2));

        Assert.That(fields.TryGet(1, groundId, out FogField ground), Is.True);
        Assert.That(fields.TryGet(1, airId, out FogField air), Is.True);
        Assert.That(fields.TryGet(1, detectionId, out FogField detection), Is.True);
        Assert.That(ground.GetVisibility(new FogCell(2, 0)), Is.EqualTo(CellVisibility.Unseen), "Low elevation reveal must not see high elevation without tolerance.");
        Assert.That(ground.GetVisibility(new FogCell(2, 2)), Is.EqualTo(CellVisibility.Unseen), "Default LoS must block cells behind an opaque blocker.");
        Assert.That(air.CellSizeCm, Is.EqualTo(250));
        Assert.That(detection.GetVisibility(new FogCell(2, 2)), Is.EqualTo(CellVisibility.Visible));

        var losDisabledComparison = new FogField(1, registry.Get(groundId));
        resolver.RasterizeIntoField(
            new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 45, groundMask, VisionPolarity.Reveal, VisionAperture.Line(450, 80), altitudeBand: 0),
            losDisabledComparison,
            new FogRulesPolicy(lineOfSightEnabled: false));
        Assert.That(losDisabledComparison.GetVisibility(new FogCell(2, 2)), Is.EqualTo(CellVisibility.Visible), "Disabling LoS should fall back to aperture plus vertical rules.");

        ground.SetExplored(new FogCell(3, 0));
        ground.SetVisible(new FogCell(2, 2));
        resolver.RasterizeIntoField(
            new VisionEmitter(1, new WorldCmInt2(0, 0), facingDeg: 0, groundMask, VisionPolarity.Deny, VisionAperture.Disk(150)),
            ground,
            new FogRulesPolicy(denyMode: FogDenyMode.DenyDominates));
        Assert.That(ground.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Denied));
        Assert.That(air.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Visible), "Ground-only deny must not erase the independent air layer.");
        ground.SetVisible(new FogCell(1, 0));

        var knowledge = new KnowledgeProjectionStore();
        var projector = new FogKnowledgeProjector(knowledge, map);
        var disclosure = new FogDisclosurePolicy(
            KnowledgeIdMask256.Empty.WithId(3),
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            ttlTicks: 0,
            trueSightRevealsConcealment: true);
        var policy = new FogProjectionPolicy(disclosure, memoryTtlTicks: 6);
        FogOccupant[] groundOccupants =
        {
            new(visibleTarget, new WorldCmInt2(150, 50), groundMask),
            new(memoryTarget, new WorldCmInt2(350, 50), groundMask),
            new(concealedTarget, new WorldCmInt2(250, 250), groundMask),
        };
        FogOccupant[] detectionOccupants =
        {
            new(stealthTarget, new WorldCmInt2(250, 250), detectionMask, stealthLevel: 2)
        };

        projector.Project(viewer, source, WorldCmInt2.Zero, ground, groundOccupants, policy, currentTick: 20);
        projector.Project(viewer, source, WorldCmInt2.Zero, detection, detectionOccupants, policy, currentTick: 20, detectionStrength: 2);

        Assert.That(knowledge.TryGet(viewer, visibleTarget, 20, out KnowledgeDisclosureRecord visible), Is.True);
        Assert.That(knowledge.TryGet(viewer, memoryTarget, 20, out KnowledgeDisclosureRecord memory), Is.True);
        Assert.That(knowledge.TryGet(viewer, concealedTarget, 20, out KnowledgeDisclosureRecord concealed), Is.True);
        Assert.That(knowledge.TryGet(viewer, stealthTarget, 20, out KnowledgeDisclosureRecord detected), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(visible.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(visible.Position, Is.EqualTo(KnowledgePositionAccess.Live));
            Assert.That(visible.AttributeMask.ContainsId(3), Is.True);
            Assert.That(memory.Presence, Is.EqualTo(KnowledgePresence.Known));
            Assert.That(memory.Position, Is.EqualTo(KnowledgePositionAccess.LastKnown));
            Assert.That(memory.ExpiryTick, Is.EqualTo(26));
            Assert.That(concealed.Presence, Is.EqualTo(KnowledgePresence.HiddenWithSource));
            Assert.That(detected.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
        });

        var snapshots = new FogSnapshotStore(relationships: relationships);
        FogSnapshotHandle before = snapshots.Capture(ground, tick: 20);
        ground.SetVisible(new FogCell(4, 0));
        FogSnapshotHandle after = snapshots.Capture(ground, tick: 21);
        Span<FogCell> changed = stackalloc FogCell[8];
        int changedCount = snapshots.Diff(before, after, changed);
        Assert.That(changedCount, Is.EqualTo(1));
        Assert.That(changed[0], Is.EqualTo(new FogCell(4, 0)));

        var sharedField = new FogField(2, registry.Get(groundId));
        sharedField.SetExplored(new FogCell(5, 0));
        FogSnapshotHandle shared = snapshots.Capture(sharedField, tick: 21);
        var mergedWithoutRelationship = new FogField(1, registry.Get(groundId));
        Assert.That(
            snapshots.TryMergeSharedExplored(before, shared, mergedWithoutRelationship, sharingSourceHost, sharingTargetHost, sharedVisionTypeId),
            Is.False);
        relationships.EnsureLink(sharingSourceHost, sharingTargetHost, sharedVisionTypeId);
        var mergedWithRelationship = new FogField(1, registry.Get(groundId));
        Assert.That(
            snapshots.TryMergeSharedExplored(before, shared, mergedWithRelationship, sharingSourceHost, sharingTargetHost, sharedVisionTypeId),
            Is.True);
        Assert.That(mergedWithRelationship.GetVisibility(new FogCell(5, 0)), Is.EqualTo(CellVisibility.Explored));

        WriteEvidence(
            artifactDir,
            ground,
            air,
            detection,
            visible,
            memory,
            concealed,
            detected,
            changed[0],
            new FogCell(5, 0));
    }

    private static void WriteEvidence(
        string artifactDir,
        FogField ground,
        FogField air,
        FogField detection,
        KnowledgeDisclosureRecord visible,
        KnowledgeDisclosureRecord memory,
        KnowledgeDisclosureRecord concealed,
        KnowledgeDisclosureRecord detected,
        FogCell changedCell,
        FogCell sharedCell)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        File.WriteAllLines(
            Path.Combine(artifactDir, "trace.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new { step = "layers", groundCellSize = ground.CellSizeCm, airCellSize = air.CellSizeCm, detectionCellSize = detection.CellSizeCm }, options),
                JsonSerializer.Serialize(new { step = "knowledge", visible = visible.Presence.ToString(), memory = memory.Presence.ToString(), concealed = concealed.Presence.ToString(), detected = detected.Presence.ToString() }, options),
                JsonSerializer.Serialize(new { step = "snapshot", changedCell = new { changedCell.X, changedCell.Y } }, options),
                JsonSerializer.Serialize(new { step = "shared_explored", sharedCell = new { sharedCell.X, sharedCell.Y } }, options),
            });
        File.WriteAllLines(
            Path.Combine(artifactDir, "battle-report.md"),
            new[]
            {
                "# Fog Of War Showcase Acceptance",
                string.Empty,
                "- layers: ground/air/detection with independent resolution",
                "- apertures: cone and line rasterization with vertical and line-of-sight rules",
                "- projection: LiveVisible, Known/LastKnown, HiddenWithSource, and aspect mask",
                "- generator: DenyDominates writes Denied",
                "- detection: true sight reveals detection-layer occupant",
                "- snapshot: capture/diff reports changed cell",
                "- sharing: relationship-gated merge contributes allied explored cells",
            });
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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root.");
    }
}
