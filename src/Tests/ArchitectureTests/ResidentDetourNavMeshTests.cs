using System.Diagnostics;
using DotRecast.Detour;
using Ludots.Core.Navigation.NavMesh;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class ResidentDetourNavMeshTests
{
    private const int CellSizeCm = 250;
    private const int ChunkSizeCells = 64;
    private const int TileSizeCm = CellSizeCm * ChunkSizeCells;

    [Test]
    public void RepeatedFindPath_UnchangedStore_ReusesOneMesh()
    {
        NavQueryService service = CreateQuery(CreateFlat(0, 0));

        NavPathResult first = service.TryFindPath(1_000, 1_000, 14_000, 1_000);
        object? mesh = service.ResidentMesh;
        int builds = service.ResidentMeshBuildCount;

        NavPathResult second = service.TryFindPath(2_000, 2_000, 12_000, 8_000);
        Assert.That(service.TrySnapToSurface(14_500, 14_500, out int snapX, out int snapZ), Is.True);

        Assert.That(first.Status, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(second.Status, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(mesh, Is.Not.Null);
        Assert.That(service.ResidentMesh, Is.SameAs(mesh));
        Assert.That(builds, Is.EqualTo(1));
        Assert.That(service.ResidentMeshBuildCount, Is.EqualTo(1));
        Assert.That(snapX, Is.EqualTo(14_500));
        Assert.That(snapZ, Is.EqualTo(14_500));
    }

    [Test]
    public void Replace_NextQuerySeesNewTileWithoutPerCallRebuild()
    {
        var blobs = new Dictionary<NavTileId, byte[]>();
        NavTile open = CreateFlat(0, 0);
        Remember(blobs, open);
        NavTileStore store = CreateStore(blobs);
        NavQueryService service = CreateQuery(store);

        NavPathResult openPath = service.TryFindPath(1_000, 1_000, 14_000, 1_000);
        object? mesh = service.ResidentMesh;
        int builds = service.ResidentMeshBuildCount;

        NavTile blocked = CreateWestStrip(0, 0);
        Remember(blobs, blocked);
        uint revision = store.Replace(blocked);

        NavPathResult blockedPath = service.TryFindPath(1_000, 1_000, 14_000, 1_000);
        NavPathResult again = service.TryFindPath(1_000, 1_000, 14_000, 1_000);

        Assert.That(openPath.Status, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(blockedPath.Status, Is.Not.EqualTo(NavPathStatus.Ok));
        Assert.That(again.Status, Is.EqualTo(blockedPath.Status));
        Assert.That(revision, Is.EqualTo(1u));
        Assert.That(mesh, Is.Not.Null);
        Assert.That(service.ResidentMesh, Is.SameAs(mesh));
        Assert.That(builds, Is.EqualTo(1));
        Assert.That(service.ResidentMeshBuildCount, Is.EqualTo(1));
    }

    [Test]
    public void LazyLoadedSecondTile_ReusesMesh()
    {
        var blobs = new Dictionary<NavTileId, byte[]>();
        Remember(blobs, CreateFlat(0, 0));
        Remember(blobs, CreateFlat(1, 0));
        NavQueryService service = CreateQuery(CreateStore(blobs));

        NavPathResult inside = service.TryFindPath(1_000, 1_000, 14_000, 1_000);
        object? mesh = service.ResidentMesh;

        NavPathResult across = service.TryFindPath(1_000, 1_000, 17_000, 1_000);

        Assert.That(inside.Status, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(across.Status, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(across.PathXcm[^1], Is.EqualTo(17_000));
        Assert.That(service.ResidentMesh, Is.SameAs(mesh));
        Assert.That(service.ResidentMeshBuildCount, Is.EqualTo(1));
    }

    [Test]
    public void SteadyFindPath_AllocatesFarBelowPerCallMesh()
    {
        NavQueryService service = CreateQuery(CreateFlat(7, 5));
        NavPathResult warmup = service.TryFindPath(113_000, 81_000, 127_000, 95_000);
        Assert.That(warmup.Status, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(service.ResidentMeshBuildCount, Is.EqualTo(1));

        Measure(service, 20);
        long[] samples = new long[5];
        long[] ticks = new long[5];
        for (int i = 0; i < samples.Length; i++)
        {
            var clock = Stopwatch.StartNew();
            samples[i] = Measure(service, 1000);
            clock.Stop();
            ticks[i] = clock.ElapsedTicks;
        }

        Array.Sort(samples);
        Array.Sort(ticks);
        long medianBytes = samples[2];
        double medianMs = ticks[2] * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"steady FindPath median bytes/1000={medianBytes} medianMs/1000={medianMs:F3}");
        TestContext.Out.WriteLine($"steady FindPath median bytes/1000={medianBytes} medianMs/1000={medianMs:F3}");

        Assert.That(service.ResidentMeshBuildCount, Is.EqualTo(1));
        Assert.That(medianBytes, Is.LessThan(8L * 1024 * 1024));
    }

    [Test]
    public void UnbuildableTile_ThrowsAndDoesNotPublishSync()
    {
        const int chunkX = 3;
        const int chunkY = 4;
        NavTile tile = CreateOversizedVertexTile(chunkX, chunkY);
        NavQueryService service = CreateQuery(tile);
        NavTile loaded = service.Store.GetOrLoad(tile.TileId);
        int originX = chunkX * TileSizeCm;
        int originZ = chunkY * TileSizeCm;
        IDtQueryFilter filter = DetourNavQueryEngine.BuildDefaultFilter(null!);

        InvalidOperationException first = Assert.Throws<InvalidOperationException>(() =>
            ResidentDetourNavMesh.FindPath(
                service.Store, 0, filter, TileSizeCm, TileSizeCm,
                originX + 1_000, originZ + 1_000, originX + 2_000, originZ + 2_000, 256))!;
        Assert.Throws<InvalidOperationException>(() =>
            ResidentDetourNavMesh.FindPath(
                service.Store, 0, filter, TileSizeCm, TileSizeCm,
                originX + 1_000, originZ + 1_000, originX + 2_000, originZ + 2_000, 256));

        InvalidOperationException query = Assert.Throws<InvalidOperationException>(() =>
            service.TryFindPath(originX + 1_000, originZ + 1_000, originX + 2_000, originZ + 2_000))!;
        Assert.Throws<InvalidOperationException>(() =>
            service.TryFindPath(originX + 1_000, originZ + 1_000, originX + 2_000, originZ + 2_000));

        Assert.That(loaded.TileId, Is.EqualTo(tile.TileId));
        Assert.That(first.Message, Does.Contain("3,4,0"));
        Assert.That(first.Message, Does.Contain(originX.ToString()));
        Assert.That(first.Message, Does.Contain(originZ.ToString()));
        Assert.That(first.Message, Does.Contain("65535"));
        Assert.That(query.Message, Does.Contain("65535"));
        Assert.That(service.ResidentMesh, Is.Null);
        Assert.That(service.ResidentMeshBuildCount, Is.EqualTo(0));
    }

    [Test]
    public void AddTileRejected_ThrowsWithTileId_AndDoesNotStickOnNotReady()
    {
        const int chunkX = 2;
        const int chunkY = 3;
        NavTile tile = CreateFlat(chunkX, chunkY);
        NavQueryService service = CreateQuery(tile);
        int originX = chunkX * TileSizeCm;
        int originZ = chunkY * TileSizeCm;
        ResidentDetourNavMesh.MaxTilesOverride = 0;
        try
        {
            InvalidOperationException first = Assert.Throws<InvalidOperationException>(() =>
                service.TryFindPath(originX + 1_000, originZ + 1_000, originX + 2_000, originZ + 2_000))!;
            Assert.Throws<InvalidOperationException>(() =>
                service.TryFindPath(originX + 1_000, originZ + 1_000, originX + 2_000, originZ + 2_000));

            Assert.That(first.Message, Does.Contain($"{chunkX},{chunkY},0"));
            Assert.That(service.ResidentMesh, Is.Null);
            Assert.That(service.ResidentMeshBuildCount, Is.EqualTo(0));
        }
        finally
        {
            ResidentDetourNavMesh.MaxTilesOverride = null;
        }
    }

    private static long Measure(NavQueryService service, int count)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
        {
            service.TryFindPath(113_000, 81_000, 127_000, 95_000);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static NavQueryService CreateQuery(NavTile tile)
    {
        var blobs = new Dictionary<NavTileId, byte[]>();
        Remember(blobs, tile);
        return CreateQuery(CreateStore(blobs));
    }

    private static NavQueryService CreateQuery(NavTileStore store)
    {
        var registry = new NavQueryServiceRegistry(
            new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
            TileSizeCm,
            TileSizeCm);
        Assert.That(registry.TryCreateQuery(0, 0, null!, out NavQueryService service), Is.True);
        return service;
    }

    private static NavTileStore CreateStore(Dictionary<NavTileId, byte[]> blobs)
    {
        return new NavTileStore(id => new MemoryStream(blobs[id], writable: false));
    }

    private static void Remember(Dictionary<NavTileId, byte[]> blobs, NavTile tile)
    {
        using var ms = new MemoryStream();
        NavTileBinary.Write(ms, tile);
        blobs[tile.TileId] = ms.ToArray();
    }

    private static NavTile CreateFlat(int chunkX, int chunkY)
        => DefaultGridNavTileFactory.CreateFlatTile(chunkX, chunkY, layer: 0, tileVersion: 1, chunkSizeCells: ChunkSizeCells, cellSizeCm: CellSizeCm);

    private static NavTile CreateWestStrip(int chunkX, int chunkY)
    {
        const int stripCm = 3_000;
        int originX = chunkX * TileSizeCm;
        int originZ = chunkY * TileSizeCm;
        return new NavTile(
            new NavTileId(chunkX, chunkY, 0),
            tileVersion: 2,
            buildConfigHash: 0UL,
            checksum: 0UL,
            originX,
            originZ,
            vertexXcm: new[] { 0, stripCm, stripCm, 0 },
            vertexYcm: new[] { 0, 0, 0, 0 },
            vertexZcm: new[] { 0, 0, TileSizeCm, TileSizeCm },
            triA: new[] { 0, 1 },
            triB: new[] { 1, 2 },
            triC: new[] { 3, 3 },
            n0: new[] { -1, -1 },
            n1: new[] { 1, -1 },
            n2: new[] { -1, 0 },
            triAreaIds: new byte[] { 0, 0 },
            portals: Array.Empty<NavBorderPortal>());
    }

    private static NavTile CreateOversizedVertexTile(int chunkX, int chunkY)
    {
        const int vertexCount = 65535;
        int originX = chunkX * TileSizeCm;
        int originZ = chunkY * TileSizeCm;
        return new NavTile(
            new NavTileId(chunkX, chunkY, 0),
            tileVersion: 1,
            buildConfigHash: 0UL,
            checksum: 0UL,
            originX,
            originZ,
            vertexXcm: new int[vertexCount],
            vertexYcm: new int[vertexCount],
            vertexZcm: new int[vertexCount],
            triA: new[] { 0 },
            triB: new[] { 1 },
            triC: new[] { 2 },
            n0: new[] { -1 },
            n1: new[] { -1 },
            n2: new[] { -1 },
            triAreaIds: new byte[] { 0 },
            portals: Array.Empty<NavBorderPortal>());
    }
}
