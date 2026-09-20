using System.Reflection;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Presentation.Navigation;
using NUnit.Framework;

namespace Ludots.Adapter.Raylib.Tests;

[TestFixture]
public sealed class RaylibNavMeshPresentationContractTests
{
    [Test]
    public void Renderer_PublicDrawContract_ConsumesOnlyCorePresentationBuffer()
    {
        Type rendererType = typeof(RaylibNavMeshPresentationRenderer);
        MethodInfo[] drawMethods = rendererType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => string.Equals(method.Name, nameof(RaylibNavMeshPresentationRenderer.Draw), StringComparison.Ordinal))
            .ToArray();

        Assert.That(drawMethods, Has.Length.EqualTo(1));
        ParameterInfo[] parameters = drawMethods[0].GetParameters();
        Assert.That(parameters, Has.Length.EqualTo(1));
        Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(NavMeshPresentationBuffer)));

        Type[] forbiddenOwnershipTypes =
        {
            typeof(NavQueryServiceRegistry),
            typeof(RuntimeIncrementalNavMeshRebuildQueue),
            typeof(NavTileStore)
        };
        FieldInfo[] fields = rendererType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        for (int i = 0; i < forbiddenOwnershipTypes.Length; i++)
        {
            Assert.That(
                fields.Any(field => field.FieldType == forbiddenOwnershipTypes[i]),
                Is.False,
                $"Raylib NavMesh renderer must not own {forbiddenOwnershipTypes[i].Name}; Core projects it into NavMeshPresentationBuffer.");
        }
    }

    [Test]
    public void Host_StaticContract_DeclaresCapabilityAndDrawsCoreBufferAfterTerrainBeforePrimitives()
    {
        string repoRoot = FindRepoRoot();
        string hostSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs"));
        string composerSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostComposer.cs"));
        string frameRendererSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src/Adapters/Raylib/Ludots.Adapter.Raylib/Rendering/RaylibFrameRenderer.cs"));
        string rendererSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src/Client/Ludots.Client.Raylib/Rendering/RaylibNavMeshPresentationRenderer.cs"));

        Assert.That(composerSource, Does.Contain("PresentationVisualCapabilities.NavMeshTileGeometry"));
        Assert.That(composerSource, Does.Contain("PresentationVisualCapabilities.Decal"));
        Assert.That(composerSource, Does.Contain("PresentationVisualCapabilities.Vfx"));
        Assert.That(composerSource, Does.Contain("PresentationVisualCapabilities.Surface"));
        // typed InstancedBatch lane 由 RaylibInstancedBatchLaneStore 消费，flat 位在组装期与
        // lane source 绑定原子声明；Hierarchical 在 raylib 无真分层，保持不声明以 fail-loud。
        Assert.That(composerSource, Does.Contain("PresentationVisualCapabilities.InstancedStaticMeshBatch"));
        Assert.That(composerSource, Does.Not.Contain("PresentationVisualCapabilities.HierarchicalInstancedStaticMeshBatch"));
        Assert.That(hostSource, Does.Contain("GetService(CoreServiceKeys.NavMeshPresentationBuffer)"));
        Assert.That(frameRendererSource, Does.Contain("_navMeshPresentationRenderer.Draw(_navMeshPresentationBuffer)"));
        Assert.That(rendererSource, Does.Not.Contain("NavQueryServiceRegistry"));
        Assert.That(rendererSource, Does.Not.Contain("RuntimeIncrementalNavMeshRebuildQueue"));
        Assert.That(rendererSource, Does.Not.Contain("NavTileStore"));

        int terrainDraw = frameRendererSource.IndexOf("_terrainRenderer.Render(TerrainSource(), frame.ActiveCamera)", StringComparison.Ordinal);
        int navMeshDraw = frameRendererSource.IndexOf("_navMeshPresentationRenderer.Draw(_navMeshPresentationBuffer)", StringComparison.Ordinal);
        int entityDraw = frameRendererSource.IndexOf("_primitiveRenderer.Draw", navMeshDraw, StringComparison.Ordinal);
        Assert.That(terrainDraw, Is.GreaterThanOrEqualTo(0));
        Assert.That(navMeshDraw, Is.GreaterThan(terrainDraw));
        Assert.That(entityDraw, Is.GreaterThan(navMeshDraw));
    }

    [Test]
    public void Renderer_Source_ConsumesAuthoritativeTileGeometry_NoFakeConstantPlane()
    {
        string repoRoot = FindRepoRoot();
        string rendererSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src/Client/Ludots.Client.Raylib/Rendering/RaylibNavMeshPresentationRenderer.cs"));

        Assert.That(rendererSource, Does.Contain("tile.TriA"));
        Assert.That(rendererSource, Does.Contain("tile.TriB"));
        Assert.That(rendererSource, Does.Contain("tile.TriC"));
        Assert.That(rendererSource, Does.Contain("VertexXcm"));
        Assert.That(rendererSource, Does.Contain("VertexYcm"));
        Assert.That(rendererSource, Does.Contain("VertexZcm"));
        Assert.That(rendererSource, Does.Not.Contain("ActiveTriA"));
        Assert.That(rendererSource, Does.Not.Contain("ActiveVertexYcm"));
        Assert.That(rendererSource, Does.Not.Contain("NavQueryTileSpace"));
        Assert.That(rendererSource, Does.Not.Contain("DrawRectangle"));
    }

    [Test]
    public void ProjectVertex_PureProjection_ConsumesAuthoritativeNavTileGeometry_NoConstantPlane()
    {
        var tile = DefaultGridNavTileFactory.CreateFlatTile(
            chunkX: 3,
            chunkY: 5,
            layer: 0,
            tileVersion: 1,
            tileWidthCm: 400,
            tileHeightCm: 400,
            tileWidthCells: 4,
            tileHeightCells: 4);
        // Vary authored heights so a constant-plane regression becomes measurable.
        tile.VertexYcm[0] = 100;
        tile.VertexYcm[1] = 250;
        tile.VertexYcm[2] = 400;

        const float offsetMeters = 0.05f;
        var a = RaylibNavMeshPresentationRenderer.ProjectVertex(tile, 0, offsetMeters);
        var b = RaylibNavMeshPresentationRenderer.ProjectVertex(tile, 1, offsetMeters);
        var c = RaylibNavMeshPresentationRenderer.ProjectVertex(tile, 2, offsetMeters);

        Assert.That(a.X, Is.EqualTo(3 * 400 * 0.01f));
        Assert.That(a.Z, Is.EqualTo(5 * 400 * 0.01f));
        Assert.That(a.Y, Is.EqualTo(100 * 0.01f + offsetMeters));
        Assert.That(b.Y, Is.EqualTo(250 * 0.01f + offsetMeters));
        Assert.That(c.Y, Is.EqualTo(400 * 0.01f + offsetMeters));
        Assert.That(a.Y, Is.Not.EqualTo(b.Y));
        Assert.That(b.Y, Is.Not.EqualTo(c.Y));

        InvalidOperationException? outOfRange = Assert.Throws<InvalidOperationException>(
            () => RaylibNavMeshPresentationRenderer.ProjectVertex(tile, tile.VertexCount, offsetMeters));
        Assert.That(outOfRange!.Message, Does.Contain("outside VertexCount"));
    }

    [Test]
    public void Draw_TwoPassReconciliation_NativeFreeStyle_ReplacesFullCacheAndEmptyFrameEvictsAll()
    {
        // drawFill=false and drawEdges=false keep Draw native-free: no material, mesh upload,
        // blend mode, or edge line is touched, so the cache lifecycle is testable headlessly.
        var style = new NavMeshPresentationStyle(
            new NavMeshPresentationColor(0.1f, 0.2f, 0.3f, 0.4f),
            new NavMeshPresentationColor(0.2f, 0.3f, 0.4f, 0.5f),
            heightOffsetMeters: 0f,
            drawFill: false,
            drawEdges: false);

        using var renderer = new RaylibNavMeshPresentationRenderer(tileCapacity: 1);
        var buffer = new NavMeshPresentationBuffer(tileCapacity: 1);
        NavTile tileA = DefaultGridNavTileFactory.CreateFlatTile(
            chunkX: 0, chunkY: 0, layer: 0, tileVersion: 1, chunkSizeCells: 4, cellSizeCm: 100);
        NavTile tileB = DefaultGridNavTileFactory.CreateFlatTile(
            chunkX: 7, chunkY: 7, layer: 0, tileVersion: 1, chunkSizeCells: 4, cellSizeCm: 100);

        // Frame A caches tile A in the single slot.
        PublishFrame(buffer, tileA, in style);
        renderer.Draw(buffer);
        Assert.That(renderer.CachedTileCount, Is.EqualTo(1));
        Assert.That(renderer.DrawnTileCountLastFrame, Is.EqualTo(1));
        Assert.That(renderer.DrawnTriangleCountLastFrame, Is.EqualTo(2));
        Assert.That(renderer.RebuiltTileCountLastFrame, Is.EqualTo(1));

        // Frame B replaces the full cache with a different tile: stale slot A must be evicted
        // before slot B is created instead of throwing on capacity, and the count stays 1.
        PublishFrame(buffer, tileB, in style);
        Assert.DoesNotThrow(() => renderer.Draw(buffer));
        Assert.That(renderer.CachedTileCount, Is.EqualTo(1));
        Assert.That(renderer.DrawnTileCountLastFrame, Is.EqualTo(1));
        Assert.That(renderer.DrawnTriangleCountLastFrame, Is.EqualTo(2));
        Assert.That(renderer.RebuiltTileCountLastFrame, Is.EqualTo(1));

        // Empty frame: reconcile evicts every cached slot and draws nothing.
        PublishFrame(buffer, tile: null, in style);
        renderer.Draw(buffer);
        Assert.That(renderer.CachedTileCount, Is.EqualTo(0));
        Assert.That(renderer.DrawnTileCountLastFrame, Is.EqualTo(0));
        Assert.That(renderer.DrawnTriangleCountLastFrame, Is.EqualTo(0));
        Assert.That(renderer.RebuiltTileCountLastFrame, Is.EqualTo(0));
    }

    private static void PublishFrame(NavMeshPresentationBuffer buffer, NavTile? tile, in NavMeshPresentationStyle style)
    {
        // BeginFrame/AddTile are Core-internal; the Raylib adapter test assembly is not a Core
        // friend, so publish through reflection without broadening the Core public API.
        MethodInfo beginFrame = typeof(NavMeshPresentationBuffer).GetMethod(
            "BeginFrame", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NavMeshPresentationBuffer.BeginFrame internal method not found.");
        beginFrame.Invoke(buffer, new object?[]
        {
            0, // layer
            0, // profile
            "Small",
            NavBakeMode.Offline,
            NavBakeAlgorithmKind.Recast,
            1u, // storeRevision
            1u, // stateRevision
            new NavBuildConfig(1f, 0.6f, 1),
            style
        });

        if (tile == null)
        {
            return;
        }

        MethodInfo addTile = typeof(NavMeshPresentationBuffer).GetMethod(
            "AddTile", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NavMeshPresentationBuffer.AddTile internal method not found.");
        addTile.Invoke(buffer, new object?[] { tile });
    }

    private static string FindRepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "showcase.registry.json")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
