using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ludots.App.RaylibEngineGallery;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    [Category("raylib-field")]
    public sealed class RaylibEngineGalleryTests
    {
        [Test]
        public void CatalogAsset_MatchesPortalRegistry_ExactlyOnce()
        {
            string repoRoot = FindRepoRoot();
            string catalogPath = Path.Combine(
                repoRoot,
                "src",
                "Apps",
                "Raylib",
                "Ludots.App.RaylibEngineGallery",
                "assets",
                "engine_gallery",
                "catalog.json");

            GalleryRow[] manifestRows = ReadManifestRows(catalogPath);
            GalleryRow[] registryRows = ReadRegistryRows(Path.Combine(repoRoot, "showcase.registry.json"));

            Assert.That(manifestRows.Select(row => row.Id), Is.EqualTo(registryRows.Select(row => row.Id)));
            Assert.That(manifestRows.Select(row => row.Title), Is.EqualTo(registryRows.Select(row => row.Title)));
            Assert.That(manifestRows.Select(row => row.Summary), Is.EqualTo(registryRows.Select(row => row.Summary)));
            Assert.That(manifestRows.Select(row => row.Id).Distinct().Count(), Is.EqualTo(manifestRows.Length));
            Assert.That(SceneCatalog.Ids, Is.EqualTo(manifestRows.Select(row => row.Id)));
        }

        [Test]
        public void SceneAssets_AreContainers_NotRuntimeTypePointers()
        {
            string repoRoot = FindRepoRoot();
            string catalogPath = Path.Combine(
                repoRoot,
                "src",
                "Apps",
                "Raylib",
                "Ludots.App.RaylibEngineGallery",
                "assets",
                "engine_gallery",
                "catalog.json");

            using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
            foreach (JsonElement entry in catalog.RootElement.GetProperty("scenes").EnumerateArray())
            {
                Assert.That(entry.TryGetProperty("sceneType", out _), Is.False);
                string asset = entry.GetProperty("asset").GetString() ?? string.Empty;
                string assetPath = Path.Combine(Path.GetDirectoryName(catalogPath)!, asset);
                using JsonDocument scene = JsonDocument.Parse(File.ReadAllText(assetPath));
                JsonElement root = scene.RootElement;
                Assert.That(root.TryGetProperty("sceneType", out _), Is.False);
                Assert.That(root.GetProperty("rootNode").GetString(), Is.Not.Null.And.Not.Empty);
                Assert.That(root.GetProperty("nodes").GetArrayLength(), Is.GreaterThan(0));
                JsonElement[] components = root
                    .GetProperty("nodes")
                    .EnumerateArray()
                    .SelectMany(node => node.GetProperty("components").EnumerateArray())
                    .ToArray();
                Assert.That(components, Is.Not.Empty);
                foreach (JsonElement component in components)
                {
                    Assert.That(component.TryGetProperty("sceneType", out _), Is.False);
                    Assert.That(component.TryGetProperty("type", out JsonElement type), Is.True);
                    Assert.That(type.GetString(), Does.Not.Contain("."));
                }
            }
        }

        [Test]
        public void CatalogDescriptors_AreReadable()
        {
            string repoRoot = FindRepoRoot();
            IReadOnlyList<SceneDescriptor> descriptors = SceneCatalog.Descriptors;
            Assert.That(descriptors.Count, Is.GreaterThan(0));

            foreach (SceneDescriptor descriptor in descriptors)
            {
                Assert.That(descriptor.Id, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.Title, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.Summary, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.AssetPath, Does.EndWith(".scene.json"));
                Assert.That(File.Exists(Path.Combine(
                    repoRoot,
                    "src",
                    "Apps",
                    "Raylib",
                    "Ludots.App.RaylibEngineGallery",
                    "assets",
                    "engine_gallery",
                    descriptor.AssetPath)), Is.True);
            }
        }

        [Test]
        public void SceneCatalog_FactoryConstructsEveryScene()
        {
            foreach (string id in SceneCatalog.Ids)
            {
                IEngineScene scene = SceneCatalog.Create(id);
                Assert.That(scene, Is.Not.Null, id);
                Assert.That(scene.Id, Is.EqualTo(id));
                scene.Dispose();
            }
        }

        private static GalleryRow[] ReadManifestRows(string path)
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement scenes = manifest.RootElement.GetProperty("scenes");
            var rows = new List<GalleryRow>();
            foreach (JsonElement entry in scenes.EnumerateArray())
            {
                string asset = entry.GetProperty("asset").GetString() ?? string.Empty;
                string assetPath = Path.Combine(Path.GetDirectoryName(path)!, asset);
                using JsonDocument scene = JsonDocument.Parse(File.ReadAllText(assetPath));
                JsonElement root = scene.RootElement;
                rows.Add(new GalleryRow(
                    root.GetProperty("id").GetString() ?? string.Empty,
                    root.GetProperty("title").GetString() ?? string.Empty,
                    root.GetProperty("summary").GetString() ?? string.Empty));
            }

            return rows.ToArray();
        }

        private static GalleryRow[] ReadRegistryRows(string path)
        {
            using JsonDocument registry = JsonDocument.Parse(File.ReadAllText(path));
            var rows = new List<GalleryRow>();
            foreach (JsonElement entry in registry.RootElement.GetProperty("showcases").EnumerateArray())
            {
                if (!string.Equals(GetOptionalString(entry, "binding"), "engine_gallery", System.StringComparison.Ordinal))
                {
                    continue;
                }

                string? status = GetOptionalString(entry, "status");
                if (string.Equals(status, "retired", System.StringComparison.Ordinal))
                {
                    continue;
                }

                string showcaseId = entry.GetProperty("id").GetString() ?? string.Empty;
                rows.Add(new GalleryRow(
                    StripEngineGalleryPrefix(showcaseId),
                    entry.GetProperty("title").GetString() ?? string.Empty,
                    entry.GetProperty("summary").GetString() ?? string.Empty));
            }

            return rows.ToArray();
        }

        private static string StripEngineGalleryPrefix(string showcaseId)
        {
            const string prefix = "engine_raylib_";
            if (!showcaseId.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Engine gallery registry id '{showcaseId}' does not use the expected '{prefix}' prefix.");
            }

            return showcaseId[prefix.Length..];
        }

        private static string? GetOptionalString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement value) ? value.GetString() : null;
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

            throw new InvalidOperationException("Could not locate repository root.");
        }

        private sealed record GalleryRow(string Id, string Title, string Summary);
    }
}
