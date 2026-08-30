using System.Text.Json.Nodes;
using Ludots.Adapter.Raylib;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class NavWalkabilityOverlayDescriptorTests
{
    private string _tempDirectory = null!;
    private string _texturePath = null!;
    private const string TextureUri = "TestMod:assets/nav/east_asia_nav_walkability.png";

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "ludots-nav-walkability-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _texturePath = Path.Combine(_tempDirectory, "east_asia_nav_walkability.png");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Test]
    public void ResolveOrThrow_PrefersSiblingSidecarBounds()
    {
        File.WriteAllText(
            _texturePath + ".json",
            """
            {
              "boundsCm": {
                "minX": -40,
                "minZ": -30,
                "maxX": 60,
                "maxZ": 70
              }
            }
            """);
        MapConfig map = CreateMapConfig(-10, -20, 30, 40);

        NavWalkabilityOverlayDescriptor descriptor =
            NavWalkabilityOverlayDescriptorResolver.ResolveOrThrow(
                map,
                new FixedAssetPathResolver(TextureUri, _texturePath));

        Assert.That(descriptor.TextureUri, Is.EqualTo(TextureUri));
        Assert.That(descriptor.BoundsCm, Is.EqualTo(new WorldAabbCm(-40, -30, 100, 100)));
        Assert.That(descriptor.SidecarPath, Is.EqualTo(_texturePath + ".json"));
    }

    [Test]
    public void ResolveOrThrow_UsesMetadataBoundsWhenSidecarIsAbsent()
    {
        MapConfig map = CreateMapConfig(-10, -20, 30, 40);

        NavWalkabilityOverlayDescriptor descriptor =
            NavWalkabilityOverlayDescriptorResolver.ResolveOrThrow(
                map,
                new FixedAssetPathResolver(TextureUri, _texturePath));

        Assert.That(descriptor.BoundsCm, Is.EqualTo(new WorldAabbCm(-10, -20, 40, 60)));
        Assert.That(descriptor.SidecarPath, Is.Null);
    }

    [Test]
    public void SetNavWalkabilityOverlay_MissingTextureFailsBeforeGraphicsInitialization()
    {
        using var renderer = new RaylibContinuousHeightmapRenderer(
            new FixedAssetPathResolver(TextureUri, _texturePath));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            renderer.SetNavWalkabilityOverlay(
                TextureUri,
                new WorldAabbCm(-10, -20, 40, 60),
                enabled: true))!;

        Assert.That(exception.Message, Does.Contain("nav walkability texture file missing"));
        Assert.That(exception.Message, Does.Contain(TextureUri));
        Assert.That(renderer.NavWalkabilityOverlayActive, Is.False);
    }

    private static MapConfig CreateMapConfig(int minX, int minY, int maxX, int maxY)
    {
        return new MapConfig
        {
            Id = "east_asia_visual_heightmap",
            Metadata =
            {
                [NavWalkabilityOverlayDescriptorResolver.MetadataKey] = new JsonObject
                {
                    ["textureUri"] = TextureUri,
                    ["boundsCm"] = new JsonObject
                    {
                        ["minX"] = minX,
                        ["minZ"] = minY,
                        ["maxX"] = maxX,
                        ["maxZ"] = maxY,
                    },
                },
            },
        };
    }

    private sealed class FixedAssetPathResolver : IRenderAssetPathResolver
    {
        private readonly string _uri;
        private readonly string _path;

        public FixedAssetPathResolver(string uri, string path)
        {
            _uri = uri;
            _path = path;
        }

        public bool TryResolveFullPath(string uri, out string fullPath)
        {
            fullPath = string.Equals(uri, _uri, StringComparison.Ordinal)
                ? _path
                : string.Empty;
            return fullPath.Length > 0;
        }
    }
}
