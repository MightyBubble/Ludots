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
    public void Host_StaticContract_DeclaresCapabilityAndDrawsCoreBufferAfterTerrain()
    {
        string repoRoot = FindRepoRoot();
        string hostSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs"));
        string composerSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostComposer.cs"));
        string rendererSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src/Client/Ludots.Client.Raylib/Rendering/RaylibNavMeshPresentationRenderer.cs"));

        Assert.That(composerSource, Does.Contain("PresentationVisualCapabilities.NavMeshTileGeometry"));
        Assert.That(hostSource, Does.Contain("GetService(CoreServiceKeys.NavMeshPresentationBuffer)"));
        Assert.That(hostSource, Does.Contain("navMeshPresentationRenderer.BindVisualHeightmap"));
        Assert.That(hostSource, Does.Contain("navMeshPresentationRenderer.Draw(navMeshPresentationBuffer)"));
        Assert.That(rendererSource, Does.Not.Contain("NavQueryServiceRegistry"));
        Assert.That(rendererSource, Does.Not.Contain("RuntimeIncrementalNavMeshRebuildQueue"));
        Assert.That(rendererSource, Does.Not.Contain("NavTileStore"));
        Assert.That(rendererSource, Does.Contain("BindVisualHeightmap"));
        Assert.That(rendererSource, Does.Contain("style.ResolveTileStateColor(tileStates[i])"));

        int terrainDraw = hostSource.IndexOf("terrainRenderer.Render(engine.VertexMap, activeCamera)", StringComparison.Ordinal);
        int navMeshDraw = hostSource.IndexOf("navMeshPresentationRenderer.Draw(navMeshPresentationBuffer)", StringComparison.Ordinal);
        int entityDraw = hostSource.IndexOf("primitiveRenderer.Draw", navMeshDraw, StringComparison.Ordinal);
        Assert.That(terrainDraw, Is.GreaterThanOrEqualTo(0));
        Assert.That(navMeshDraw, Is.GreaterThan(terrainDraw));
        Assert.That(entityDraw, Is.GreaterThan(navMeshDraw));
    }

    [Test]
    public void Renderer_TileLifecycleState_UsesAuthoredCoreStyleColors()
    {
        var pending = new NavMeshPresentationColor(0.9f, 0.8f, 0.1f, 0.7f);
        var rebuilding = new NavMeshPresentationColor(0.9f, 0.4f, 0.1f, 0.8f);
        var committed = new NavMeshPresentationColor(0.1f, 0.9f, 0.4f, 0.7f);
        var style = new NavMeshPresentationStyle(
            new NavMeshPresentationColor(0.1f, 0.2f, 0.3f, 0.4f),
            new NavMeshPresentationColor(0.2f, 0.3f, 0.4f, 0.5f),
            new NavMeshPresentationColor(0.3f, 0.4f, 0.5f, 0.6f),
            pending,
            rebuilding,
            committed,
            heightOffsetMeters: 0.04f,
            drawFill: true,
            drawEdges: true,
            drawTileBounds: true,
            drawTileStateIndication: true);

        Assert.That(
            style.ResolveTileStateColor(NavMeshPresentationTileState.Pending),
            Is.EqualTo(pending));
        Assert.That(
            style.ResolveTileStateColor(NavMeshPresentationTileState.Rebuilding),
            Is.EqualTo(rebuilding));
        Assert.That(
            style.ResolveTileStateColor(NavMeshPresentationTileState.Committed),
            Is.EqualTo(committed));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            style.ResolveTileStateColor((NavMeshPresentationTileState)byte.MaxValue));
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
