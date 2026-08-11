using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;
using Ludots.NavBake.Recast;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Stage B contracts: banked NavTile output, allocation-free checksum serialization, and
    /// NavTileStore generation/resident-capacity behavior. No LayeredSpan kernel dependency.
    /// </summary>
    [TestFixture]
    public sealed class NavTileBankAndChecksumContractTests
    {
        [Test]
        public void NavTile_SetCounts_CapacityExhaustionHardFailsAndNamesRequiredAmount()
        {
            NavTile tile = NavTile.CreateBanked(vertexCapacity: 16, triangleCapacity: 32, portalCapacity: 8);

            InvalidOperationException vertices = Assert.Throws<InvalidOperationException>(
                () => tile.SetCounts(vertexCount: 17, triangleCount: 1, portalCount: 1))!;
            Assert.That(vertices.Message, Does.Contain("vertexCount"));
            Assert.That(vertices.Message, Does.Contain("16"));
            Assert.That(vertices.Message, Does.Contain("17"));

            InvalidOperationException triangles = Assert.Throws<InvalidOperationException>(
                () => tile.SetCounts(vertexCount: 1, triangleCount: 33, portalCount: 1))!;
            Assert.That(triangles.Message, Does.Contain("triangleCount"));
            Assert.That(triangles.Message, Does.Contain("32"));

            InvalidOperationException portals = Assert.Throws<InvalidOperationException>(
                () => tile.SetCounts(vertexCount: 1, triangleCount: 1, portalCount: 9))!;
            Assert.That(portals.Message, Does.Contain("portalCount"));
            Assert.That(portals.Message, Does.Contain("8"));

            Assert.DoesNotThrow(() => tile.SetCounts(vertexCount: 16, triangleCount: 32, portalCount: 8));
            Assert.That(tile.VertexCount, Is.EqualTo(16));
            Assert.That(tile.TriangleCount, Is.EqualTo(32));
            Assert.That(tile.PortalCount, Is.EqualTo(8));
        }

        [Test]
        public void NavTile_CopyGeometryFrom_CapacityExhaustionHardFailsBeforeMutation()
        {
            NavTile source = CreateFlat(chunkX: 1, chunkY: 2, version: 5);
            NavTile destination = NavTile.CreateBanked(
                vertexCapacity: 3,
                triangleCapacity: 32,
                portalCapacity: 8);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => destination.CopyGeometryFrom(source))!;
            Assert.That(ex.Message, Does.Contain("outputVertexCapacity"));
            Assert.That(ex.Message, Does.Contain("3"));

            // No partial mutation: header/counts/checksum remain untouched.
            Assert.That(destination.VertexCount, Is.EqualTo(0));
            Assert.That(destination.TriangleCount, Is.EqualTo(0));
            Assert.That(destination.PortalCount, Is.EqualTo(0));
            Assert.That(destination.Checksum, Is.EqualTo(0UL));
        }

        [Test]
        public void NavTile_CopyGeometryFrom_FillsBankedChannelsAndCopiesChecksum()
        {
            NavTile source = CreateFlat(chunkX: 1, chunkY: 2, version: 5);
            Span<byte> scratch = stackalloc byte[NavTileBinary.GetSerializedSize(source)];
            NavTileBinary.AssignChecksum(source, scratch);

            NavTile destination = NavTile.CreateBanked(
                vertexCapacity: 64,
                triangleCapacity: 64,
                portalCapacity: 16);
            destination.CopyGeometryFrom(source);

            Assert.That(destination.TileId, Is.EqualTo(source.TileId));
            Assert.That(destination.TileVersion, Is.EqualTo(source.TileVersion));
            Assert.That(destination.Checksum, Is.EqualTo(source.Checksum));
            Assert.That(destination.VertexCount, Is.EqualTo(source.VertexCount));
            Assert.That(destination.TriangleCount, Is.EqualTo(source.TriangleCount));
            Assert.That(destination.PortalCount, Is.EqualTo(source.PortalCount));
            Assert.That(destination.VertexCapacity, Is.GreaterThan(destination.VertexCount));
            for (int i = 0; i < source.VertexCount; i++)
            {
                Assert.That(destination.VertexXcm[i], Is.EqualTo(source.VertexXcm[i]));
                Assert.That(destination.VertexYcm[i], Is.EqualTo(source.VertexYcm[i]));
                Assert.That(destination.VertexZcm[i], Is.EqualTo(source.VertexZcm[i]));
            }

            for (int i = 0; i < source.TriangleCount; i++)
            {
                Assert.That(destination.TriA[i], Is.EqualTo(source.TriA[i]));
                Assert.That(destination.TriB[i], Is.EqualTo(source.TriB[i]));
                Assert.That(destination.TriC[i], Is.EqualTo(source.TriC[i]));
                Assert.That(destination.TriAreaIds[i], Is.EqualTo(source.TriAreaIds[i]));
            }

            Assert.That(destination.ActivePortals.Length, Is.EqualTo(source.PortalCount));
        }

        [Test]
        public void NavTileOutputBank_RentSlot_ExhaustionHardFailsAndResetReusesSameSlots()
        {
            var bank = new NavTileOutputBank(CreateRuntimeConfig(
                stagedEntryCapacity: 2,
                vertexCapacity: 16,
                triangleCapacity: 32,
                portalCapacity: 8));

            Assert.That(bank.Capacity, Is.EqualTo(2));
            NavTile first = bank.RentSlot();
            NavTile second = bank.RentSlot();
            Assert.That(bank.Count, Is.EqualTo(2));
            Assert.That(ReferenceEquals(first, second), Is.False);
            Assert.That(first.VertexCount, Is.EqualTo(0));

            InvalidOperationException exhausted = Assert.Throws<InvalidOperationException>(() => bank.RentSlot())!;
            Assert.That(exhausted.Message, Does.Contain("stagedEntryCapacity"));
            Assert.That(exhausted.Message, Does.Contain("2"));

            bank.Reset();
            Assert.That(bank.Count, Is.EqualTo(0));

            // Output slots are reused: Reset returns the same preallocated instances.
            Assert.That(ReferenceEquals(bank.RentSlot(), first), Is.True);
            Assert.That(ReferenceEquals(bank.RentSlot(), second), Is.True);
        }

        [Test]
        public void NavTileOutputBank_RentedSlot_IsClearedBeforeReuse()
        {
            var bank = new NavTileOutputBank(CreateRuntimeConfig(
                stagedEntryCapacity: 1,
                vertexCapacity: 16,
                triangleCapacity: 32,
                portalCapacity: 8));

            NavTile slot = bank.RentSlot();
            slot.AssignHeader(new NavTileId(7, 8, 9), tileVersion: 3, buildConfigHash: 4UL, originXcm: 5, originZcm: 6);
            slot.SetCounts(vertexCount: 4, triangleCount: 2, portalCount: 4);
            slot.SetChecksum(1234UL);
            Assert.That(slot.VertexCount, Is.EqualTo(4));

            bank.Reset();
            NavTile reused = bank.RentSlot();
            Assert.That(ReferenceEquals(reused, slot), Is.True);
            Assert.That(reused.VertexCount, Is.EqualTo(0));
            Assert.That(reused.TriangleCount, Is.EqualTo(0));
            Assert.That(reused.PortalCount, Is.EqualTo(0));
            Assert.That(reused.Checksum, Is.EqualTo(0UL));
        }

        [Test]
        public void NavTileBinary_SpanAndStreamPaths_ProduceByteIdenticalOutput()
        {
            Assert.That(NavTileBinary.FormatVersion, Is.EqualTo((ushort)3));
            NavTile tile = CreateFlat(chunkX: 1, chunkY: 2, version: 9);

            Span<byte> scratch = stackalloc byte[NavTileBinary.GetSerializedSize(tile)];
            NavTileBinary.AssignChecksum(tile, scratch);
            Assert.That(tile.Checksum, Is.Not.EqualTo(0UL));

            byte[] viaSpan = new byte[NavTileBinary.GetSerializedSize(tile)];
            int written = NavTileBinary.Write(viaSpan.AsSpan(), tile);
            Assert.That(written, Is.EqualTo(viaSpan.Length));

            byte[] viaStream;
            using (var ms = new MemoryStream())
            {
                NavTileBinary.Write(ms, tile);
                viaStream = ms.ToArray();
            }

            Assert.That(viaSpan, Is.EqualTo(viaStream));
            Assert.That(viaStream.Length, Is.EqualTo(NavTileBinary.GetSerializedSize(tile)));
            Assert.That(NavTileBinary.ComputeChecksum(tile, scratch), Is.EqualTo(tile.Checksum));

            // Stream layout contract: little-endian magic "NTIL" + FormatVersion 3.
            Assert.That(viaStream[0], Is.EqualTo(0x4E));
            Assert.That(viaStream[1], Is.EqualTo(0x54));
            Assert.That(viaStream[2], Is.EqualTo(0x49));
            Assert.That(viaStream[3], Is.EqualTo(0x4C));
            Assert.That(viaStream[4], Is.EqualTo((byte)NavTileBinary.FormatVersion));
            Assert.That(viaStream[5], Is.EqualTo(0));

            using var readMs = new MemoryStream(viaStream);
            NavTile roundTrip = NavTileBinary.Read(readMs);
            Assert.That(roundTrip.TileId, Is.EqualTo(tile.TileId));
            Assert.That(roundTrip.TileVersion, Is.EqualTo(tile.TileVersion));
            Assert.That(roundTrip.Checksum, Is.EqualTo(tile.Checksum));
            Assert.That(roundTrip.VertexCount, Is.EqualTo(tile.VertexCount));
            Assert.That(roundTrip.TriangleCount, Is.EqualTo(tile.TriangleCount));
            Assert.That(roundTrip.PortalCount, Is.EqualTo(tile.PortalCount));
            for (int i = 0; i < tile.PortalCount; i++)
            {
                Assert.That(roundTrip.Portals[i].LeftYcm, Is.EqualTo(tile.Portals[i].LeftYcm));
                Assert.That(roundTrip.Portals[i].RightYcm, Is.EqualTo(tile.Portals[i].RightYcm));
            }

            for (int i = 0; i < tile.TriangleCount; i++)
            {
                Assert.That(roundTrip.TriA[i], Is.EqualTo(tile.TriA[i]));
                Assert.That(roundTrip.TriB[i], Is.EqualTo(tile.TriB[i]));
                Assert.That(roundTrip.TriC[i], Is.EqualTo(tile.TriC[i]));
                Assert.That(roundTrip.N0[i], Is.EqualTo(tile.N0[i]));
            }
        }

        [Test]
        public void NavTileBinary_ShortDestinationSpan_HardFailsBeforeWriting()
        {
            NavTile tile = CreateFlat(chunkX: 0, chunkY: 0, version: 1);
            int size = NavTileBinary.GetSerializedSize(tile);

            byte[] destination = new byte[size - 1];
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => NavTileBinary.Write(destination.AsSpan(), tile))!;
            Assert.That(ex.Message, Does.Contain("below required"));
        }

        [Test]
        public void NavTileBinary_ShortChecksumScratch_HardFails()
        {
            NavTile tile = CreateFlat(chunkX: 0, chunkY: 0, version: 1);
            int size = NavTileBinary.GetSerializedSize(tile);

            byte[] scratch = new byte[size - 1];
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => NavTileBinary.ComputeChecksum(tile, scratch.AsSpan()))!;
            Assert.That(ex.Message, Does.Contain("below required"));
        }

        [Test]
        public void NavTileBinary_VersionMismatch_HardFailsRead()
        {
            NavTile tile = CreateFlat(chunkX: 0, chunkY: 0, version: 1);
            Span<byte> scratch = stackalloc byte[NavTileBinary.GetSerializedSize(tile)];
            NavTileBinary.AssignChecksum(tile, scratch);
            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, tile);
            byte[] bytes = ms.ToArray();

            // FormatVersion 2 bytes must hard-fail the v3 reader (no backward-compatible v2 reader).
            bytes[4] = 2;
            bytes[5] = 0;
            using var bad = new MemoryStream(bytes);
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => NavTileBinary.Read(bad))!;
            Assert.That(ex.Message, Does.Contain("version"));
        }

        [Test]
        public void BankedUnusedCapacity_PoisonDoesNotAffectChecksumOrValidEmpty()
        {
            NavTile tile = CreateFlat(chunkX: 0, chunkY: 0, version: 1);
            Span<byte> scratch = stackalloc byte[NavTileBinary.GetSerializedSize(tile)];
            NavTileBinary.AssignChecksum(tile, scratch);
            ulong checksumBefore = tile.Checksum;
            byte[] bytesBefore;
            using (var ms = new MemoryStream())
            {
                NavTileBinary.Write(ms, tile);
                bytesBefore = ms.ToArray();
            }

            NavTile banked = NavTile.CreateBanked(vertexCapacity: 32, triangleCapacity: 32, portalCapacity: 32);
            banked.CopyGeometryFrom(tile);
            Assert.That(banked.VertexCapacity, Is.GreaterThan(banked.VertexCount));
            Assert.That(banked.TriangleCapacity, Is.GreaterThan(banked.TriangleCount));
            Assert.That(banked.PortalCapacity, Is.GreaterThan(banked.PortalCount));

            var poisonPortal = new NavBorderPortal(
                NavPortalSide.West,
                1, 2, 3, 4,
                leftXcm: 999, leftYcm: 0, leftZcm: 777,
                rightXcm: 666, rightYcm: 0, rightZcm: 444,
                clearanceCm: 1);
            for (int i = banked.PortalCount; i < banked.PortalCapacity; i++)
            {
                banked.Portals[i] = poisonPortal;
            }

            for (int i = banked.VertexCount; i < banked.VertexCapacity; i++)
            {
                banked.VertexXcm[i] = 123456;
                banked.VertexYcm[i] = -98765;
                banked.VertexZcm[i] = 444444;
            }

            for (int i = banked.TriangleCount; i < banked.TriangleCapacity; i++)
            {
                banked.TriA[i] = 7;
                banked.TriB[i] = 8;
                banked.TriC[i] = 9;
                banked.N0[i] = 10;
                banked.N1[i] = 11;
                banked.N2[i] = 12;
                banked.TriAreaIds[i] = 255;
            }

            Assert.That(banked.ActivePortals.Length, Is.EqualTo(banked.PortalCount));
            Assert.That(banked.ActiveVertexXcm.Length, Is.EqualTo(banked.VertexCount));
            Assert.That(banked.ActiveTriA.Length, Is.EqualTo(banked.TriangleCount));

            Span<byte> bankScratch = stackalloc byte[NavTileBinary.GetSerializedSize(banked)];
            NavTileBinary.AssignChecksum(banked, bankScratch);
            Assert.That(banked.Checksum, Is.EqualTo(checksumBefore));

            using (var ms = new MemoryStream())
            {
                NavTileBinary.Write(ms, banked);
                Assert.That(ms.ToArray(), Is.EqualTo(bytesBefore));
            }

            NavTile empty = NavTile.CreateBanked(vertexCapacity: 32, triangleCapacity: 32, portalCapacity: 32);
            NavValidEmptyTile.Fill(
                empty,
                new NavTileId(0, 0, 0),
                tileVersion: 1,
                buildConfigHash: 1UL,
                originXcm: 0,
                originZcm: 0,
                bankScratch);
            for (int i = 0; i < empty.PortalCapacity; i++)
            {
                empty.Portals[i] = poisonPortal;
            }

            Assert.That(empty.PortalCount, Is.EqualTo(0));
            Assert.That(empty.ActivePortals.Length, Is.EqualTo(0));
            Assert.That(empty.VertexCount, Is.EqualTo(0));
            Assert.That(empty.TriangleCount, Is.EqualTo(0));
            Assert.That(empty.Checksum, Is.Not.EqualTo(0UL));
            using var emptyMs = new MemoryStream();
            NavTileBinary.Write(emptyMs, empty);
            emptyMs.Position = 0;
            NavTile emptyRoundTrip = NavTileBinary.Read(emptyMs);
            Assert.That(emptyRoundTrip.PortalCount, Is.EqualTo(0));
            Assert.That(emptyRoundTrip.Checksum, Is.EqualTo(empty.Checksum));
        }

        [Test]
        public void FlatGridBaseline_BankedUnusedAreaPoison_DoesNotAffectDetourArea()
        {
            NavTile tile = DefaultGridNavTileFactory.CreateFlatTile(
                chunkX: 0,
                chunkY: 0,
                layer: 0,
                tileVersion: 1,
                chunkSizeCells: 4,
                cellSizeCm: SpatialScaleDefaults.CellCm,
                areaId: 3);
            Assert.That(tile.TriangleCount, Is.EqualTo(2));
            Assert.That(tile.TriangleCapacity, Is.EqualTo(2));

            NavTile banked = NavTile.CreateBanked(
                Math.Max(32, tile.VertexCount),
                Math.Max(32, tile.TriangleCount),
                Math.Max(32, tile.PortalCount));
            banked.CopyGeometryFrom(tile);
            Assert.That(banked.TriangleCount, Is.EqualTo(2));
            Assert.That(banked.TriangleCapacity, Is.GreaterThan(banked.TriangleCount));
            Assert.That(banked.ActiveTriAreaIds[0], Is.EqualTo((byte)3));
            Assert.That(banked.ActiveTriAreaIds[1], Is.EqualTo((byte)3));

            for (int i = banked.TriangleCount; i < banked.TriangleCapacity; i++)
            {
                banked.TriAreaIds[i] = 255;
            }

            Assert.That(banked.TriAreaIds.Length, Is.GreaterThan(banked.TriangleCount));
            Assert.That(banked.TriAreaIds[0], Is.EqualTo((byte)3));

            byte[] clean = DetourNavQueryEngine.BuildFlatGridBaselineDetourTileBytes(tile, 400, 400);
            byte[] poisoned = DetourNavQueryEngine.BuildFlatGridBaselineDetourTileBytes(banked, 400, 400);
            Assert.That(poisoned, Is.EqualTo(clean));

            NavPathResult path = DetourNavQueryEngine.FindPathFromDetourTileBytes(
                new[] { poisoned },
                layer: 0,
                areaCosts: NavAreaCostTable.CreateDefault(),
                startXcm: 50,
                startZcm: 50,
                goalXcm: 350,
                goalZcm: 350,
                maxPortals: 64);
            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok));
        }

        [Test]
        public void Store_ReplaceGenerationBatch_CommitsAtomicallyAndTracksGeneration()
        {
            NavTileStore store = CreateStore(
                residentTileCapacity: 8,
                outputVertexCapacity: 16,
                outputTriangleCapacity: 32,
                outputPortalCapacity: 8);
            Assert.That(store.Generation, Is.EqualTo(0UL));
            Assert.That(store.ResidentCount, Is.EqualTo(0));

            var batch = new List<NavTile>
            {
                CreateFlat(chunkX: 0, chunkY: 0, version: 1),
                CreateFlat(chunkX: 1, chunkY: 0, version: 2),
                CreateFlat(chunkX: 0, chunkY: 1, version: 3)
            };

            uint revision = store.ReplaceGenerationBatch(generation: 1UL, batch);
            Assert.That(revision, Is.EqualTo(1u));
            Assert.That(store.Generation, Is.EqualTo(1UL));
            Assert.That(store.ResidentCount, Is.EqualTo(3));
            Assert.That(store.TryGet(new NavTileId(1, 0, 0), out NavTile published), Is.True);
            Assert.That(published.TileVersion, Is.EqualTo(2u));
            Assert.That(published.VertexCount, Is.EqualTo(4));

            uint second = store.ReplaceGenerationBatch(generation: 2UL, new[] { CreateFlat(chunkX: 2, chunkY: 0, version: 4) });
            Assert.That(second, Is.EqualTo(2u));
            Assert.That(store.Generation, Is.EqualTo(2UL));
            Assert.That(store.ResidentCount, Is.EqualTo(4));

            // Existing residents are overwritten in place by the same banked slot.
            uint overwrite = store.ReplaceGenerationBatch(generation: 3UL, new[] { CreateFlat(chunkX: 1, chunkY: 0, version: 9) });
            Assert.That(overwrite, Is.EqualTo(3u));
            Assert.That(store.ResidentCount, Is.EqualTo(4));
            Assert.That(store.TryGet(new NavTileId(1, 0, 0), out NavTile updated), Is.True);
            Assert.That(updated.TileVersion, Is.EqualTo(9u));
        }

        [Test]
        public void Store_ReplaceGenerationBatch_RejectsNonIncreasingGeneration_WithoutMutation()
        {
            NavTileStore store = CreateStore(8, 16, 32, 8);
            store.Replace(CreateFlat(chunkX: 0, chunkY: 0, version: 1));
            uint revisionBefore = store.Revision;
            ulong generationBefore = store.Generation;
            int residentCountBefore = store.ResidentCount;

            InvalidOperationException same = Assert.Throws<InvalidOperationException>(
                () => store.ReplaceGenerationBatch(generation: generationBefore, new[] { CreateFlat(chunkX: 1, chunkY: 0, version: 2) }))!;
            Assert.That(same.Message, Does.Contain("strictly greater"));
            Assert.That(store.Revision, Is.EqualTo(revisionBefore));
            Assert.That(store.Generation, Is.EqualTo(generationBefore));
            Assert.That(store.ResidentCount, Is.EqualTo(residentCountBefore));
            Assert.That(store.TryGet(new NavTileId(1, 0, 0), out _), Is.False);

            InvalidOperationException zero = Assert.Throws<InvalidOperationException>(
                () => store.ReplaceGenerationBatch(generation: 0UL, new[] { CreateFlat(chunkX: 1, chunkY: 0, version: 2) }))!;
            Assert.That(zero.Message, Does.Contain("non-zero"));
            Assert.That(store.ResidentCount, Is.EqualTo(residentCountBefore));
        }

        [Test]
        public void Store_ReplaceGenerationBatch_RejectsDuplicateIdsAndNullTiles_WithoutMutation()
        {
            NavTileStore store = CreateStore(8, 16, 32, 8);

            InvalidOperationException duplicates = Assert.Throws<InvalidOperationException>(
                () => store.ReplaceGenerationBatch(
                    generation: 1UL,
                    new List<NavTile>
                    {
                        CreateFlat(chunkX: 0, chunkY: 0, version: 1),
                        CreateFlat(chunkX: 0, chunkY: 0, version: 2)
                    }))!;
            Assert.That(duplicates.Message, Does.Contain("duplicate"));
            Assert.That(store.ResidentCount, Is.EqualTo(0));
            Assert.That(store.Generation, Is.EqualTo(0UL));
            Assert.That(store.Revision, Is.EqualTo(0u));

            InvalidOperationException nullTile = Assert.Throws<InvalidOperationException>(
                () => store.ReplaceGenerationBatch(
                    generation: 1UL,
                    new List<NavTile> { CreateFlat(chunkX: 0, chunkY: 0, version: 1), null! }))!;
            Assert.That(nullTile.Message, Does.Contain("null"));
            Assert.That(store.ResidentCount, Is.EqualTo(0));
        }

        [Test]
        public void Store_ReplaceGenerationBatch_ResidentCapacityExhaustion_HardFailsWithoutMutation()
        {
            NavTileStore store = CreateStore(
                residentTileCapacity: 2,
                outputVertexCapacity: 16,
                outputTriangleCapacity: 32,
                outputPortalCapacity: 8);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => store.ReplaceGenerationBatch(
                    generation: 1UL,
                    new List<NavTile>
                    {
                        CreateFlat(chunkX: 0, chunkY: 0, version: 1),
                        CreateFlat(chunkX: 1, chunkY: 0, version: 2),
                        CreateFlat(chunkX: 2, chunkY: 0, version: 3)
                    }))!;
            Assert.That(ex.Message, Does.Contain("residentTileCapacity"));
            Assert.That(store.ResidentCount, Is.EqualTo(0));
            Assert.That(store.Generation, Is.EqualTo(0UL));
            Assert.That(store.Revision, Is.EqualTo(0u));
        }

        [Test]
        public void Store_Replace_IncomingGeometryCapacityExhaustion_HardFails()
        {
            NavTileStore store = CreateStore(
                residentTileCapacity: 4,
                outputVertexCapacity: 4,
                outputTriangleCapacity: 2,
                outputPortalCapacity: 4);

            store.Replace(CreateFlat(chunkX: 0, chunkY: 0, version: 1));
            Assert.That(store.ResidentCount, Is.EqualTo(1));

            var tooManyVertices = new NavTile(
                new NavTileId(1, 0, 0),
                tileVersion: 1,
                buildConfigHash: 0UL,
                checksum: 0UL,
                originXcm: 0,
                originZcm: 0,
                vertexXcm: new int[5],
                vertexYcm: new int[5],
                vertexZcm: new int[5],
                triA: new[] { 0, 1 },
                triB: new[] { 1, 2 },
                triC: new[] { 2, 2 },
                n0: new[] { -1, -1 },
                n1: new[] { -1, -1 },
                n2: new[] { -1, -1 },
                triAreaIds: new byte[] { 0, 0 },
                portals: new NavBorderPortal[4]);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => store.Replace(tooManyVertices))!;
            Assert.That(ex.Message, Does.Contain("outputVertexCapacity"));
            Assert.That(store.TryGet(new NavTileId(1, 0, 0), out _), Is.False);
        }

        [Test]
        public void Store_CopyResidentTileIds_OrdersDeterministicallyByLayerChunkYChunkX()
        {
            NavTileStore store = CreateStore(8, 16, 32, 8);
            store.Replace(CreateFlat(chunkX: 1, chunkY: 0, version: 1, layer: 0));
            store.Replace(CreateFlat(chunkX: 0, chunkY: 0, version: 2, layer: 1));
            store.Replace(CreateFlat(chunkX: 0, chunkY: 1, version: 3, layer: 0));
            store.Replace(CreateFlat(chunkX: 0, chunkY: 0, version: 4, layer: 0));

            var ids = new NavTileId[8];
            int count = store.CopyResidentTileIds(ids.AsSpan());
            Assert.That(count, Is.EqualTo(4));
            Assert.That(ids[0], Is.EqualTo(new NavTileId(0, 0, 0)));
            Assert.That(ids[1], Is.EqualTo(new NavTileId(1, 0, 0)));
            Assert.That(ids[2], Is.EqualTo(new NavTileId(0, 1, 0)));
            Assert.That(ids[3], Is.EqualTo(new NavTileId(0, 0, 1)));

            int again = store.CopyResidentTileIds(ids.AsSpan());
            Assert.That(again, Is.EqualTo(4));
            Assert.That(ids[0], Is.EqualTo(new NavTileId(0, 0, 0)));
            Assert.That(ids[3], Is.EqualTo(new NavTileId(0, 0, 1)));
        }

        [Test]
        public void Store_CopyResidentTilesSpan_OrdersDeterministicallyAndReportsRevisionAndGeneration()
        {
            NavTileStore store = CreateStore(8, 16, 32, 8);
            store.ReplaceGenerationBatch(
                generation: 1UL,
                new List<NavTile>
                {
                    CreateFlat(chunkX: 1, chunkY: 0, version: 1),
                    CreateFlat(chunkX: 0, chunkY: 1, version: 2),
                    CreateFlat(chunkX: 0, chunkY: 0, version: 3)
                });

            var scratch = new NavTile[8];
            int count = store.CopyResidentTiles(scratch.AsSpan(), out uint revision, out ulong generation);
            Assert.That(count, Is.EqualTo(3));
            Assert.That(revision, Is.EqualTo(store.Revision));
            Assert.That(generation, Is.EqualTo(1UL));
            Assert.That(scratch[0].TileId, Is.EqualTo(new NavTileId(0, 0, 0)));
            Assert.That(scratch[1].TileId, Is.EqualTo(new NavTileId(1, 0, 0)));
            Assert.That(scratch[2].TileId, Is.EqualTo(new NavTileId(0, 1, 0)));
            Assert.That(scratch[0].TileVersion, Is.EqualTo(3u));
            Assert.That(scratch[1].TileVersion, Is.EqualTo(1u));
            Assert.That(scratch[2].TileVersion, Is.EqualTo(2u));

            int again = store.CopyResidentTiles(scratch.AsSpan(), out _, out _);
            Assert.That(again, Is.EqualTo(3));
            Assert.That(scratch[0].TileId, Is.EqualTo(new NavTileId(0, 0, 0)));
        }

        [Test]
        public void Store_CopyResidentTiles_AllocatesZeroManagedBytesAfterWarmup()
        {
            NavTileStore store = CreateStore(8, 16, 32, 8);
            store.Replace(CreateFlat(chunkX: 0, chunkY: 0, version: 1));
            store.Replace(CreateFlat(chunkX: 1, chunkY: 0, version: 2));
            store.Replace(CreateFlat(chunkX: 0, chunkY: 1, version: 3));
            var scratch = new NavTile[8];
            var ids = new NavTileId[8];

            // Warmup: JIT, lock plumbing, and sort paths must be fully warm before measuring.
            for (int i = 0; i < 8; i++)
            {
                store.CopyResidentTiles(scratch, out _);
                store.CopyResidentTiles(scratch.AsSpan(), out _, out _);
                store.CopyResidentTileIds(ids.AsSpan());
            }

            GC.Collect();
            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                store.CopyResidentTiles(scratch, out _);
                store.CopyResidentTiles(scratch.AsSpan(), out _, out _);
                store.CopyResidentTileIds(ids.AsSpan());
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0L), $"Steady-state resident copies allocated {allocated} managed bytes.");
        }

        [Test]
        public void NavBakeService_BakeInto_DefaultBridgeWritesBankedDestination()
        {
            NavBakeContext context = CreateExactCdtContext();
            var service = new NavBakeService(new ExactCdtNavBakeAlgorithm());
            NavBakeResult baseline = service.Bake(context);
            Assert.That(baseline.FailureCount, Is.EqualTo(0), baseline.Entries[0].Artifact.Message);
            NavTile expected = baseline.Entries[0].Tile;

            NavTile destination = NavTile.CreateBanked(
                vertexCapacity: 64,
                triangleCapacity: 128,
                portalCapacity: 16);
            NavMeshAgentProfileConfig navProfile = context.Config.Profiles[0];
            AgentProfileConfig agentProfile = context.AgentProfiles.Require(navProfile.Id, "profiles[0]");

            bool success = service.BakeInto(
                context,
                new NavBakeTileCoord(0, 0),
                context.Config.Layers[0],
                navProfile,
                agentProfile,
                destination,
                checksumScratch: default,
                out NavBakeArtifact artifact);

            Assert.That(success, Is.True);
            Assert.That(artifact.ErrorCode, Is.EqualTo(NavBakeErrorCode.None));
            Assert.That(destination.TileId, Is.EqualTo(expected.TileId));
            Assert.That(destination.TileVersion, Is.EqualTo(expected.TileVersion));
            Assert.That(destination.VertexCount, Is.EqualTo(expected.VertexCount));
            Assert.That(destination.TriangleCount, Is.EqualTo(expected.TriangleCount));
            Assert.That(destination.PortalCount, Is.EqualTo(expected.PortalCount));
            Assert.That(destination.VertexCount, Is.LessThanOrEqualTo(destination.VertexCapacity));
            for (int i = 0; i < expected.TriangleCount; i++)
            {
                Assert.That(destination.TriA[i], Is.EqualTo(expected.TriA[i]));
                Assert.That(destination.TriB[i], Is.EqualTo(expected.TriB[i]));
                Assert.That(destination.TriC[i], Is.EqualTo(expected.TriC[i]));
            }
        }

        [Test]
        public void NavBakeService_BakeInto_UnsupportedModeInput_StillFailsDiagnostically()
        {
            // Recast declares RuntimeIncrementalTriangleSurface after the triangle-surface convergence, so
            // the unsupported-mode diagnostic is now proven with an adapter that declares offline only.
            NavBakeContext context = CreateExactCdtContext();
            var unsupported = new NavBakeContext
            {
                MapId = context.MapId,
                SourceUri = context.SourceUri,
                TriangleSurface = context.TriangleSurface,
                Obstacles = context.Obstacles,
                Config = context.Config,
                AgentProfiles = context.AgentProfiles,
                Targets = context.Targets,
                BuildConfig = context.BuildConfig,
                TileVersion = context.TileVersion,
                Mode = NavBakeMode.RuntimeIncremental,
                Algorithm = NavBakeAlgorithmKind.Recast,
                Execution = context.Execution
            };
            var service = new NavBakeService(new OfflineOnlyRecastAdapter());
            NavTile destination = NavTile.CreateBanked(64, 128, 16);
            NavMeshAgentProfileConfig navProfile = context.Config.Profiles[0];
            AgentProfileConfig agentProfile = context.AgentProfiles.Require(navProfile.Id, "profiles[0]");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => service.BakeInto(
                    unsupported,
                    new NavBakeTileCoord(0, 0),
                    context.Config.Layers[0],
                    navProfile,
                    agentProfile,
                    destination,
                    checksumScratch: default,
                    out _))!;
            Assert.That(ex.Message, Does.Contain("runtime-incremental"));
        }

        private sealed class OfflineOnlyRecastAdapter : INavBakeAlgorithm
        {
            public NavBakeAlgorithmKind Kind => NavBakeAlgorithmKind.Recast;

            public NavBakeAdapterCapabilities Capabilities => NavBakeAdapterCapabilities.OfflineTriangleSurface;

            public bool SupportsMode(NavBakeMode mode)
            {
                return mode switch
                {
                    NavBakeMode.Offline => true,
                    NavBakeMode.RuntimeIncremental => false,
                    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, $"Unknown nav bake mode '{mode}'.")
                };
            }

            public bool GuaranteesBitwiseDeterminism => false;

            public bool Supports3DMultiLayer => true;

            public bool IsZeroAllocationHotPath => false;

            public bool TryBake(
                NavBakeContext context,
                NavBakeTileCoord target,
                NavLayerConfig layer,
                NavMeshAgentProfileConfig navProfile,
                AgentProfileConfig agentProfile,
                out NavTile tile,
                out byte[] detourTileBytes,
                out NavBakeArtifact artifact)
            {
                throw new InvalidOperationException("Offline-only adapter must be rejected before any bake attempt.");
            }
        }

        private static NavTileStore CreateStore(
            int residentTileCapacity,
            int outputVertexCapacity,
            int outputTriangleCapacity,
            int outputPortalCapacity)
        {
            return new NavTileStore(
                _ => throw new InvalidOperationException("Stage B bank tests do not load tiles from disk."),
                residentTileCapacity,
                outputVertexCapacity,
                outputTriangleCapacity,
                outputPortalCapacity);
        }

        private static NavRuntimeIncrementalConfig CreateRuntimeConfig(
            int stagedEntryCapacity,
            int vertexCapacity,
            int triangleCapacity,
            int portalCapacity)
        {
            return new NavRuntimeIncrementalConfig
            {
                StagedEntryCapacity = stagedEntryCapacity,
                OutputVertexCapacity = vertexCapacity,
                OutputTriangleCapacity = triangleCapacity,
                OutputPortalCapacity = portalCapacity
            };
        }

        private static NavTile CreateFlat(int chunkX, int chunkY, uint version, int layer = 0)
            => DefaultGridNavTileFactory.CreateFlatTile(
                chunkX,
                chunkY,
                layer,
                version,
                chunkSizeCells: 4,
                cellSizeCm: 100);

        private static NavBakeContext CreateExactCdtContext()
        {
            NavBuildConfig build = new NavBuildConfig(1f, 0.6f, 1);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(
                new FlatGridLogicTerrainField(4, 4, chunkSizeCells: 4),
                build,
                haloPaddingCm: 0);
            return new NavBakeContext
            {
                MapId = "stage_b_bake_into",
                SourceUri = "Core:Maps/stage_b_bake_into.tris",
                TriangleSurface = surface,
                Obstacles = new NavObstacleSet(),
                Config = CreateBakeConfig(),
                AgentProfiles = CreateAgentProfiles(),
                Targets = new[] { new NavBakeTileCoord(0, 0) },
                BuildConfig = build,
                TileVersion = 1,
                Mode = NavBakeMode.Offline,
                Algorithm = NavBakeAlgorithmKind.ExactCdt,
                Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
            };
        }

        private static NavMeshBakeConfig CreateBakeConfig()
        {
            return new NavMeshBakeConfig
            {
                Mode = NavBakeNames.ModeOffline,
                Algorithm = NavBakeNames.AlgorithmExactCdt,
                Profiles = new List<NavMeshAgentProfileConfig>
                {
                    new NavMeshAgentProfileConfig { Id = "Small", MaxClimbCm = 40, MaxSlopeDeg = 45 }
                },
                Layers = new List<NavLayerConfig>
                {
                    new NavLayerConfig { Id = "Ground", Layer = 0 }
                },
                Areas = new List<NavAreaCostConfig>(),
                TriangleSurface = new NavTriangleSurfaceConfig { HaloPaddingCm = 0 },
                RuntimeIncremental = new NavRuntimeIncrementalConfig
                {
                    TileBudgetPerFixedTick = 1,
                    IncludeNeighborTiles = true,
                    HeightScaleMeters = 1f,
                    MinWalkableUpDot = 0.6f,
                    CliffHeightThreshold = 1
                }
            };
        }

        private static AgentProfileRegistry CreateAgentProfiles()
        {
            return new AgentProfileRegistry(new[]
            {
                new AgentProfileConfig
                {
                    Id = "Small",
                    RadiusCm = 30,
                    HeightCm = 180,
                    ClearanceCm = 40,
                    Mass = 1,
                    Layer = 0
                }
            });
        }
    }
}
