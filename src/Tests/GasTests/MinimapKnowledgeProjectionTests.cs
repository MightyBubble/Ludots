using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class MinimapKnowledgeProjectionTests
{
    private static readonly string[] EngineMods =
    {
        "LudotsCoreMod",
    };

    [Test]
    public void Issue198_RefreshProjectsKnowledgeStatesAcrossPlayerAndTeamViewerSwitches()
    {
        using GameEngine engine = CreateEngine();
        MinimapRuntime runtime = CreateRuntime();
        var markers = new MinimapMarkerBuffer(16);
        var screenMarkers = new MinimapScreenMarkerBuffer(16);

        Entity playerViewer = engine.World.Create();
        Entity teamViewer = engine.World.Create();
        Entity allyIntelSource = engine.World.Create();
        Entity liveTarget = engine.World.Create();
        Entity lastKnownTarget = engine.World.Create();
        Entity disclosedTarget = engine.World.Create();
        Entity hiddenTarget = engine.World.Create();

        var store = new KnowledgeProjectionStore(initialCapacity: 16);
        store.Upsert(playerViewer, liveTarget, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, playerViewer));
        store.Upsert(playerViewer, lastKnownTarget, CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, playerViewer));
        store.Upsert(teamViewer, hiddenTarget, CreateRecord(KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, teamViewer));
        store.Upsert(teamViewer, liveTarget, CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, teamViewer));

        RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("RelationshipRuntime missing.");
        RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
            ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");
        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore missing.");
        int intelTypeId = relationshipTypes.Register("Minimap.KnowledgeIntel");
        int collectionKeyId = collections.KeyRegistry.Register("minimap.knowledge.disclosed");
        relationships.EnsureLink(playerViewer, allyIntelSource, intelTypeId);
        collections.Replace(
            allyIntelSource,
            EntityCollectionDescriptor.Create("minimap.knowledge.disclosed", EntityCollectionSourceKind.RelationDerived, EntityCollectionRoleKind.Display),
            new[] { disclosedTarget });
        RelationshipCatalogRuntime catalogRuntime = CreateCatalogRuntime(
            relationshipTypes,
            collections,
            "Minimap.KnowledgeIntel",
            "minimap.knowledge.disclosed",
            CreateRecord(KnowledgePresence.HiddenWithSource, KnowledgePositionAccess.LastKnown, allyIntelSource),
            attributeId: 1,
            relationshipTypeId: 2);
        var projector = new KnowledgeRelationCollectionProjector(relationships, collections, catalogRuntime, store);
        InstallKnowledgeServices(engine, store, projector);

        SeedMarkers(markers, liveTarget, lastKnownTarget, disclosedTarget, hiddenTarget);
        runtime.Visible = true;
        runtime.UseRtsFullMapPreset();

        engine.SetService(CoreServiceKeys.LocalPlayerEntity, playerViewer);
        runtime.Refresh(engine, markers, screenMarkers);
        MinimapDebugSnapshot playerSnapshot = runtime.CaptureDebugSnapshot();
        Assert.That(playerSnapshot.VisibleMarkerCount, Is.EqualTo(3));
        Assert.That(CountState(playerSnapshot, MinimapKnowledgeState.LiveVisible), Is.EqualTo(1));
        Assert.That(CountState(playerSnapshot, MinimapKnowledgeState.LastKnown), Is.EqualTo(1));
        Assert.That(CountState(playerSnapshot, MinimapKnowledgeState.Disclosed), Is.EqualTo(1));
        Assert.That(playerSnapshot.VisibleMarkers.Any(marker => MathF.Abs(marker.WorldXcm - 4000f) <= 0.001f), Is.False);

        engine.SetService(CoreServiceKeys.LocalPlayerEntity, teamViewer);
        runtime.Refresh(engine, markers, screenMarkers);
        MinimapDebugSnapshot teamSnapshot = runtime.CaptureDebugSnapshot();
        Assert.That(teamSnapshot.VisibleMarkerCount, Is.EqualTo(2));
        Assert.That(FindMarkerByWorldX(teamSnapshot, 1000f).KnowledgeState, Is.EqualTo(MinimapKnowledgeState.LastKnown));
        Assert.That(FindMarkerByWorldX(teamSnapshot, 4000f).KnowledgeState, Is.EqualTo(MinimapKnowledgeState.LiveVisible));
    }

    [Test]
    public void Issue198_RefreshDropsExpiredLastKnownMarkers()
    {
        using GameEngine engine = CreateEngine();
        MinimapRuntime runtime = CreateRuntime();
        var markers = new MinimapMarkerBuffer(4);
        var screenMarkers = new MinimapScreenMarkerBuffer(4);
        var clock = new DiscreteClock();
        engine.SetService(CoreServiceKeys.Clock, clock);

        Entity viewer = engine.World.Create();
        Entity expiringTarget = engine.World.Create();
        var store = new KnowledgeProjectionStore();
        store.Upsert(
            viewer,
            expiringTarget,
            CreateRecord(KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, viewer, expiryTick: 2));
        InstallKnowledgeServices(engine, store, null);
        engine.SetService(CoreServiceKeys.LocalPlayerEntity, viewer);

        markers.BeginFrame();
        var color = new Vector4(0.2f, 0.8f, 1f, 1f);
        Assert.That(markers.TryAdd(9001, expiringTarget, 1200f, 0f, in color, 8f), Is.True);
        runtime.Visible = true;
        runtime.UseRtsFullMapPreset();

        runtime.Refresh(engine, markers, screenMarkers);
        MinimapDebugSnapshot beforeExpiry = runtime.CaptureDebugSnapshot();
        Assert.That(beforeExpiry.VisibleMarkerCount, Is.EqualTo(1));
        Assert.That(beforeExpiry.VisibleMarkers[0].KnowledgeState, Is.EqualTo(MinimapKnowledgeState.LastKnown));

        clock.Advance(ClockDomainId.Step, 2);
        runtime.Refresh(engine, markers, screenMarkers);
        MinimapDebugSnapshot afterExpiry = runtime.CaptureDebugSnapshot();
        Assert.That(afterExpiry.VisibleMarkerCount, Is.EqualTo(0));
    }

    [Test]
    public void Issue198_RefreshKnowledgeFilteringAllocatesZeroAfterWarmup()
    {
        using GameEngine engine = CreateEngine();
        MinimapRuntime runtime = CreateRuntime();
        var markers = new MinimapMarkerBuffer(96);
        var screenMarkers = new MinimapScreenMarkerBuffer(96);
        Entity viewer = engine.World.Create();
        var store = new KnowledgeProjectionStore(initialCapacity: 128);

        markers.BeginFrame();
        for (int i = 0; i < 64; i++)
        {
            Entity owner = engine.World.Create();
            KnowledgePresence presence = (i % 3) == 0 ? KnowledgePresence.LiveVisible : KnowledgePresence.Known;
            KnowledgePositionAccess position = (i % 3) == 0 ? KnowledgePositionAccess.Live : KnowledgePositionAccess.LastKnown;
            store.Upsert(viewer, owner, CreateRecord(presence, position, viewer));
            var color = new Vector4(0.2f, 0.8f, 1f, 1f);
            Assert.That(markers.TryAdd(10_000 + i, owner, -3000f + (i * 90f), (i % 8) * 500f, in color, 7f), Is.True);
        }

        InstallKnowledgeServices(engine, store, null);
        engine.SetService(CoreServiceKeys.LocalPlayerEntity, viewer);
        runtime.Visible = true;
        runtime.UseRtsFullMapPreset();
        for (int i = 0; i < 32; i++)
        {
            runtime.Refresh(engine, markers, screenMarkers);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++)
        {
            runtime.Refresh(engine, markers, screenMarkers);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0), $"Issue #198 expects warmed minimap knowledge filtering to allocate zero bytes, got {allocated}.");
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, EngineMods),
            Path.Combine(repoRoot, "assets"));
        return engine;
    }

    private static MinimapRuntime CreateRuntime()
    {
        return new MinimapRuntime(new MinimapRuntimeConfig
        {
            InitialZoomNormalized = 1f,
            WheelZoomNormalizedStep = 0.08f,
            ButtonZoomNormalizedStep = 0.18f,
            ZoomSliderEnabled = true,
            ModeToggleEnabled = true,
            RotateToggleEnabled = true,
            DebugMarkerSampleCapacity = 128,
            MinZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
            MinZoomExplicitHalfExtentCm = 750f,
            MaxZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
            MaxZoomExplicitHalfExtentCm = 5000f,
        });
    }

    private static void InstallKnowledgeServices(
        GameEngine engine,
        KnowledgeProjectionStore store,
        KnowledgeRelationCollectionProjector? projector)
    {
        engine.SetService(CoreServiceKeys.KnowledgeProjectionStore, store);

        if (projector != null)
        {
            engine.SetService(CoreServiceKeys.KnowledgeRelationCollectionProjector, projector);
        }

        engine.SetService(CoreServiceKeys.KnowledgeProjectionResolver, new KnowledgeProjectionResolver(store, projector));
    }

    private static RelationshipCatalogRuntime CreateCatalogRuntime(
        RelationshipTypeRegistry relationshipTypes,
        EntityCollectionStore collections,
        string typeId,
        string collectionKey,
        in KnowledgeDisclosureRecord profile,
        int attributeId,
        int relationshipTypeId)
    {
        return RelationshipCatalogRuntime.Compile(
            new RelationshipCatalogConfig
            {
                KnowledgeGrants =
                {
                    new RelationshipKnowledgeGrantConfig
                    {
                        Id = $"{typeId}.{collectionKey}",
                        TypeId = typeId,
                        CollectionKey = collectionKey,
                        Presence = profile.Presence,
                        Position = profile.Position,
                        AttributeIds = { attributeId },
                        RelationshipTypeIds = { relationshipTypeId },
                        ObservedTick = profile.ObservedTick,
                        ExpiryTick = profile.ExpiryTick,
                        ConfidencePermille = profile.ConfidencePermille
                    }
                }
            },
            relationshipTypes,
            new RelationshipMetricRegistry(),
            collections);
    }

    private static void SeedMarkers(MinimapMarkerBuffer markers, params Entity[] owners)
    {
        markers.BeginFrame();
        var color = new Vector4(0.12f, 0.82f, 1f, 1f);
        for (int i = 0; i < owners.Length; i++)
        {
            Assert.That(markers.TryAdd(
                8001 + i,
                owners[i],
                1000f * (i + 1),
                0f,
                in color,
                8f), Is.True);
        }
    }

    private static KnowledgeDisclosureRecord CreateRecord(
        KnowledgePresence presence,
        KnowledgePositionAccess position,
        Entity source,
        int expiryTick = 0)
    {
        return new KnowledgeDisclosureRecord(
            presence,
            position,
            KnowledgeIdMask256.Empty.WithId(1),
            KnowledgeIdMask256.Empty.WithId(2),
            KnowledgeIdMask256.Empty,
            source,
            observedTick: 1,
            expiryTick,
            confidencePermille: 900,
            revision: 0);
    }

    private static int CountState(MinimapDebugSnapshot snapshot, MinimapKnowledgeState state)
    {
        int count = 0;
        for (int i = 0; i < snapshot.VisibleMarkers.Count; i++)
        {
            if (snapshot.VisibleMarkers[i].KnowledgeState == state)
            {
                count++;
            }
        }

        return count;
    }

    private static MinimapDebugMarker FindMarkerByWorldX(MinimapDebugSnapshot snapshot, float worldXcm)
    {
        for (int i = 0; i < snapshot.VisibleMarkers.Count; i++)
        {
            if (MathF.Abs(snapshot.VisibleMarkers[i].WorldXcm - worldXcm) <= 0.001f)
            {
                return snapshot.VisibleMarkers[i];
            }
        }

        throw new InvalidOperationException($"Marker at worldX={worldXcm} was not visible.");
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
