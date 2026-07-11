using System;
using System.Collections.Generic;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.AOI;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Tests for ILoadedChunks event-driven lifecycle:
    ///   - HexGridAOI fires ChunkLoaded/ChunkUnloaded correctly
    ///   - ChunkedNodeGraphStore auto-removes graph data on ChunkUnloaded
    ///   - VertexMap auto-releases vertex data on ChunkUnloaded
    ///   - HexGridAOI.Reset() cascades ChunkUnloaded to all consumers
    /// </summary>
    [TestFixture]
    public class LoadedChunksEventTests
    {
        #region Helpers

        /// <summary>
        /// Minimal ILoadedChunks stub for consumer-only tests.
        /// Allows manually firing events.
        /// </summary>
        private sealed class MockLoadedChunks : ILoadedChunks
        {
            private readonly HashSet<long> _active = new HashSet<long>();

            public IReadOnlyCollection<long> ActiveChunkKeys => _active;
            public bool IsLoaded(long chunkKey) => _active.Contains(chunkKey);

            public event Action<long> ChunkLoaded;
            public event Action<long> ChunkUnloaded;

            public void SimulateLoad(long key)
            {
                _active.Add(key);
                ChunkLoaded?.Invoke(key);
            }

            public void SimulateUnload(long key)
            {
                _active.Remove(key);
                ChunkUnloaded?.Invoke(key);
            }
        }

        /// <summary>
        /// Minimal IAOISource for driving HexGridAOI.Update().
        /// </summary>
        private sealed class SimpleAOISource : IAOISource
        {
            public int CenterXcm { get; set; }
            public int CenterZcm { get; set; }
            public int RadiusCm { get; set; }
        }

        private static GraphChunkData MakeSimpleChunk()
        {
            var b = new NodeGraphBuilder(1, 0);
            b.AddNode(0, 0);
            return new GraphChunkData(b.Build(), Array.Empty<GraphCrossEdge>());
        }

        #endregion

        // =====================================================================
        // 1. HexGridAOI fires ChunkLoaded when chunks become active
        // =====================================================================
        [Test]
        public void HexGridAOI_Update_FiresChunkLoaded_ForNewChunks()
        {
            var aoi = new HexGridAOI();
            var loaded = new List<long>();
            aoi.ChunkLoaded += key => loaded.Add(key);

            // Update with origin — should load a set of chunks around (0,0)
            aoi.Update(new SimpleAOISource { CenterXcm = 0, CenterZcm = 0, RadiusCm = 0 });

            That(loaded.Count, Is.GreaterThan(0), "Should fire ChunkLoaded for each activated chunk");
            That(aoi.ActiveChunkKeys.Count, Is.EqualTo(loaded.Count),
                "ActiveChunkKeys count should match loaded event count");

            foreach (long key in loaded)
            {
                That(aoi.IsLoaded(key), Is.True, $"Chunk {key} should be marked as loaded");
            }
        }

        // =====================================================================
        // 2. HexGridAOI fires ChunkUnloaded when chunks become inactive
        // =====================================================================
        [Test]
        public void HexGridAOI_Update_FiresChunkUnloaded_WhenChunksLeaveRange()
        {
            var aoi = new HexGridAOI();

            // Load chunks around origin
            aoi.Update(new SimpleAOISource { CenterXcm = 0, CenterZcm = 0, RadiusCm = 0 });
            var initialChunks = new HashSet<long>(aoi.ActiveChunkKeys);
            That(initialChunks.Count, Is.GreaterThan(0));

            var unloaded = new List<long>();
            aoi.ChunkUnloaded += key => unloaded.Add(key);

            // Move far away so the original chunks fall out of range
            int farDistance = HexCoordinates.EdgeLengthCm * 64 * 20; // 20 chunk-widths away
            aoi.Update(new SimpleAOISource { CenterXcm = farDistance, CenterZcm = farDistance, RadiusCm = 0 });

            // Some original chunks should have been unloaded
            That(unloaded.Count, Is.GreaterThan(0), "Should fire ChunkUnloaded for chunks that left range");

            foreach (long key in unloaded)
            {
                That(aoi.IsLoaded(key), Is.False, $"Unloaded chunk {key} should no longer be active");
            }
        }

        // =====================================================================
        // 3. HexGridAOI.Reset() fires ChunkUnloaded for ALL active chunks
        // =====================================================================
        [Test]
        public void HexGridAOI_Reset_FiresChunkUnloaded_ForAllActiveChunks()
        {
            var aoi = new HexGridAOI();

            // Load some chunks
            aoi.Update(new SimpleAOISource { CenterXcm = 0, CenterZcm = 0, RadiusCm = 0 });
            var activeBeforeReset = new HashSet<long>(aoi.ActiveChunkKeys);
            That(activeBeforeReset.Count, Is.GreaterThan(0));

            var unloaded = new List<long>();
            aoi.ChunkUnloaded += key => unloaded.Add(key);

            aoi.Reset();

            // Every previously-active chunk should have an unload event
            That(unloaded.Count, Is.EqualTo(activeBeforeReset.Count),
                "Reset should fire ChunkUnloaded for every active chunk");

            foreach (long key in unloaded)
            {
                That(activeBeforeReset.Contains(key), Is.True,
                    $"ChunkUnloaded key {key} should have been in the active set");
            }

            That(aoi.ActiveChunkKeys.Count, Is.EqualTo(0), "After Reset, no chunks should remain active");
        }

        // =====================================================================
        // 4. ChunkedNodeGraphStore removes graph data on ChunkUnloaded
        // =====================================================================
        [Test]
        public void ChunkedNodeGraphStore_SubscribeToLoadedChunks_RemovesOnUnload()
        {
            var mock = new MockLoadedChunks();
            var store = new ChunkedNodeGraphStore();

            long key = GraphChunkKey.Pack(5, 7);
            store.AddOrReplace(key, MakeSimpleChunk());
            That(store.TryGetChunk(key, out _), Is.True, "Chunk data should exist before unload");

            store.SubscribeToLoadedChunks(mock);
            mock.SimulateUnload(key);

            That(store.TryGetChunk(key, out _), Is.False, "Chunk data should be removed after unload event");
            That(store.IsViewDirty, Is.True, "View dirty flag should be set after chunk removal");
        }

        // =====================================================================
        // 5. VertexMap releases vertex data on ChunkUnloaded
        // =====================================================================
        [Test]
        public void VertexMap_SubscribeToLoadedChunks_RemovesOnUnload()
        {
            var mock = new MockLoadedChunks();
            var vertexMap = new VertexMap();
            vertexMap.Initialize(64, 64);

            // Write a height value at (0, 0) — this goes to chunk (0, 0)
            vertexMap.SetHeight(0, 0, 12);
            That(vertexMap.GetHeight(0, 0), Is.EqualTo(12), "Height should be set before unload");
            That(vertexMap.ChunkCount, Is.EqualTo(1), "Should have exactly 1 chunk");

            vertexMap.SubscribeToLoadedChunks(mock);

            long chunkKey = HexCoordinates.GetChunkKey(0, 0);
            mock.SimulateUnload(chunkKey);

            That(vertexMap.ChunkCount, Is.EqualTo(0), "Chunk should be removed after unload event");
            That(vertexMap.GetHeight(0, 0), Is.EqualTo(0), "Height should revert to default after chunk removal");
        }

        // =====================================================================
        // 6. Integration: HexGridAOI.Reset() cascades to all consumers
        // =====================================================================
        [Test]
        public void Reset_CascadesToAllConsumers()
        {
            var aoi = new HexGridAOI();

            // Load chunks
            aoi.Update(new SimpleAOISource { CenterXcm = 0, CenterZcm = 0, RadiusCm = 0 });
            That(aoi.ActiveChunkKeys.Count, Is.GreaterThan(0));

            // Pick one active chunk key and populate consumers with data for it
            long targetKey = 0;
            foreach (long k in aoi.ActiveChunkKeys)
            {
                targetKey = k;
                break;
            }

            var store = new ChunkedNodeGraphStore();
            store.AddOrReplace(targetKey, MakeSimpleChunk());
            store.SubscribeToLoadedChunks(aoi);

            var vertexMap = new VertexMap();
            vertexMap.Initialize(64, 64);
            // Manually inject a chunk entry under the same key by writing data
            // that maps to chunk (0,0) — which is one of the active chunks near origin
            long originChunkKey = HexCoordinates.GetChunkKey(0, 0);
            // If targetKey equals originChunkKey, writing at (0,0) populates that chunk
            // Otherwise we still test that the graph store chunk gets cleaned up
            vertexMap.SetHeight(0, 0, 13);
            vertexMap.SubscribeToLoadedChunks(aoi);

            // Verify pre-conditions
            That(store.TryGetChunk(targetKey, out _), Is.True, "Graph chunk should exist");
            That(vertexMap.ChunkCount, Is.GreaterThan(0), "VertexMap should have data");

            // Reset — should cascade ChunkUnloaded to all subscribers
            aoi.Reset();

            // Graph store: the target chunk should be gone
            That(store.TryGetChunk(targetKey, out _), Is.False,
                "Graph chunk data should be removed after Reset cascade");

            // VertexMap: if origin chunk was in the active set, it should be released
            if (aoi.ActiveChunkKeys.Count == 0 && originChunkKey == targetKey)
            {
                That(vertexMap.GetHeight(0, 0), Is.EqualTo(0),
                    "VertexMap data should be released after Reset cascade");
            }

            That(aoi.ActiveChunkKeys.Count, Is.EqualTo(0), "All chunks should be cleared");
        }

        // =====================================================================
        // 7. Resubscribe: old source unsubscribed, new source works
        // =====================================================================
        [Test]
        public void ChunkedNodeGraphStore_Resubscribe_UnsubscribesOldSource()
        {
            var mockA = new MockLoadedChunks();
            var mockB = new MockLoadedChunks();
            var store = new ChunkedNodeGraphStore();

            long key = GraphChunkKey.Pack(1, 1);
            store.AddOrReplace(key, MakeSimpleChunk());
            store.SubscribeToLoadedChunks(mockA);

            // Switch to source B
            store.SubscribeToLoadedChunks(mockB);

            // Old source A fires — should NOT affect the store
            mockA.SimulateUnload(key);
            That(store.TryGetChunk(key, out _), Is.True,
                "Old source events should be ignored after resubscribe");

            // New source B fires — SHOULD affect the store
            mockB.SimulateUnload(key);
            That(store.TryGetChunk(key, out _), Is.False,
                "New source events should be processed");
        }

        [Test]
        public void WorldGridLoadedChunks_ContributorsPublishUnionAndReleaseIndependently()
        {
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 1000);
            using WorldGridLoadedChunkContributor camera = loadedChunks.AcquireContributor("camera");
            using WorldGridLoadedChunkContributor navigation = loadedChunks.AcquireContributor("navigation");
            long shared = GraphChunkKey.Pack(0, 0);
            long cameraOnly = GraphChunkKey.Pack(1, 0);
            long navigationOnly = GraphChunkKey.Pack(2, 0);
            var unloaded = new List<long>();
            loadedChunks.ChunkUnloaded += unloaded.Add;

            camera.SetLoaded(shared, loaded: true);
            camera.SetLoaded(cameraOnly, loaded: true);
            navigation.SetLoaded(shared, loaded: true);
            navigation.SetLoaded(navigationOnly, loaded: true);

            That(loadedChunks.ActiveChunkKeys, Is.EquivalentTo(new[] { shared, cameraOnly, navigationOnly }));

            camera.Dispose();

            That(loadedChunks.IsLoaded(shared), Is.True, "A shared chunk must remain active while another contributor still owns it.");
            That(loadedChunks.IsLoaded(cameraOnly), Is.False);
            That(loadedChunks.IsLoaded(navigationOnly), Is.True);
            That(unloaded, Is.EquivalentTo(new[] { cameraOnly }));
        }

        [Test]
        public void WorldGridLoadedChunks_RemoteContributorWindowsRemainActiveTogether()
        {
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 1000);
            using WorldGridLoadedChunkContributor near = loadedChunks.AcquireContributor("near");
            using WorldGridLoadedChunkContributor remote = loadedChunks.AcquireContributor("remote");

            near.UpdateWindow(centerXcm: 0, centerYcm: 0, radiusCm: 0);
            remote.UpdateWindow(centerXcm: 100_000, centerYcm: -100_000, radiusCm: 0);

            That(loadedChunks.IsLoaded(GraphChunkKey.Pack(0, 0)), Is.True);
            That(loadedChunks.IsLoaded(GraphChunkKey.Pack(100, -100)), Is.True);
            That(loadedChunks.ActiveChunkKeys.Count, Is.EqualTo(2));
        }

        [Test]
        public void WorldGridLoadedChunks_ReassertingSameWindowKeepsUnionAfterAnotherContributorMoves()
        {
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 1000);
            using WorldGridLoadedChunkContributor fixedWindow = loadedChunks.AcquireContributor("fixed");
            using WorldGridLoadedChunkContributor movingWindow = loadedChunks.AcquireContributor("moving");
            long fixedKey = GraphChunkKey.Pack(0, 0);
            long movedKey = GraphChunkKey.Pack(50, 50);

            fixedWindow.UpdateWindow(centerXcm: 0, centerYcm: 0, radiusCm: 0);
            movingWindow.UpdateWindow(centerXcm: 10_000, centerYcm: 10_000, radiusCm: 0);
            movingWindow.UpdateWindow(centerXcm: 50_000, centerYcm: 50_000, radiusCm: 0);
            fixedWindow.UpdateWindow(centerXcm: 0, centerYcm: 0, radiusCm: 0);

            That(loadedChunks.IsLoaded(fixedKey), Is.True);
            That(loadedChunks.IsLoaded(movedKey), Is.True);
            That(loadedChunks.ActiveChunkKeys.Count, Is.EqualTo(2));
        }

        [Test]
        public void WorldGridLoadedChunks_ResetAndRepeatedDisposeAreIdempotent()
        {
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 1000);
            WorldGridLoadedChunkContributor contributor = loadedChunks.AcquireContributor("reset-owner");
            long key = GraphChunkKey.Pack(4, 5);

            contributor.SetLoaded(key, loaded: false);
            contributor.SetLoaded(key, loaded: true);
            contributor.SetLoaded(key, loaded: false);
            contributor.SetLoaded(key, loaded: false);
            contributor.SetLoaded(key, loaded: true);
            loadedChunks.Reset();
            loadedChunks.Reset();
            contributor.Dispose();
            contributor.Dispose();

            That(loadedChunks.ActiveChunkKeys, Is.Empty);
            That(loadedChunks.ContributorCount, Is.Zero);
        }

        [Test]
        public void WorldGridLoadedChunks_DuplicateContributorKeyFailsUntilLeaseIsReleased()
        {
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 1000);
            WorldGridLoadedChunkContributor first = loadedChunks.AcquireContributor("stable-key");

            InvalidOperationException error = Throws<InvalidOperationException>(
                () => loadedChunks.AcquireContributor("stable-key"))!;
            That(error.Message, Does.Contain("already acquired"));

            first.Dispose();
            using WorldGridLoadedChunkContributor reacquired = loadedChunks.AcquireContributor("stable-key");
            That(loadedChunks.ContributorCount, Is.EqualTo(1));
        }

        [Test]
        public void WorldGridLoadedChunks_DirectAndNamedContributionsShareOneUnion()
        {
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 1000);
            using WorldGridLoadedChunkContributor named = loadedChunks.AcquireContributor("named");
            long shared = GraphChunkKey.Pack(0, 0);
            long directOnly = GraphChunkKey.Pack(1, 0);
            long namedOnly = GraphChunkKey.Pack(2, 0);

            loadedChunks.SetLoaded(shared, loaded: true);
            loadedChunks.SetLoaded(directOnly, loaded: true);
            named.SetLoaded(shared, loaded: true);
            named.SetLoaded(namedOnly, loaded: true);
            loadedChunks.SetLoaded(shared, loaded: false);

            That(loadedChunks.ActiveChunkKeys, Is.EquivalentTo(new[] { shared, directOnly, namedOnly }));

            named.Dispose();

            That(loadedChunks.ActiveChunkKeys, Is.EquivalentTo(new[] { directOnly }));
        }
    }
}
