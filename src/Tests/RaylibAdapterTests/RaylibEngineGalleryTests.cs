using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ludots.Content.EngineGallery;
using Ludots.Raylib.SceneKit;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    [Category("raylib-field")]
    public sealed class RaylibEngineGalleryTests
    {
        private static string RepoRoot => FindRepoRoot();

        private static string EngineProjectRoot => Path.Combine(RepoRoot, "projects", "engine_gallery");

        private static EngineProject OpenEngineProject()
        {
            return EngineProject.Open(EngineProjectRoot);
        }

        [Test]
        public void CatalogAsset_MatchesPortalRegistry_ExactlyOnce()
        {
            string catalogPath = Path.Combine(EngineProjectRoot, "catalog.json");

            GalleryRow[] manifestRows = ReadManifestRows(catalogPath);
            GalleryRow[] registryRows = ReadRegistryRows(Path.Combine(RepoRoot, "showcase.registry.json"));

            Assert.That(manifestRows.Select(row => row.Id), Is.EqualTo(registryRows.Select(row => row.Id)));
            Assert.That(manifestRows.Select(row => row.Title), Is.EqualTo(registryRows.Select(row => row.Title)));
            Assert.That(manifestRows.Select(row => row.Summary), Is.EqualTo(registryRows.Select(row => row.Summary)));
            Assert.That(manifestRows.Select(row => row.Id).Distinct().Count(), Is.EqualTo(manifestRows.Length));
            EngineProject project = OpenEngineProjectForIds();
            Assert.That(project.Ids, Is.EqualTo(manifestRows.Select(row => row.Id)));
        }

        [Test]
        public void SceneAssets_AreContainers_NotRuntimeTypePointers()
        {
            string catalogPath = Path.Combine(EngineProjectRoot, "catalog.json");
            using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
            foreach (JsonElement entry in catalog.RootElement.GetProperty("scenes").EnumerateArray())
            {
                Assert.That(entry.TryGetProperty("sceneType", out _), Is.False);
                string asset = entry.GetProperty("asset").GetString() ?? string.Empty;
                string assetPath = Path.Combine(EngineProjectRoot, asset);
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
            EngineProject project = OpenEngineProjectForIds();
            GalleryRow[] registryRows = ReadRegistryRows(Path.Combine(RepoRoot, "showcase.registry.json"));

            Assert.That(project.Descriptors.Count, Is.EqualTo(registryRows.Length));
            foreach (SceneDescriptor descriptor in project.Descriptors)
            {
                Assert.That(descriptor.Id, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.Title, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.Summary, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.AssetPath, Does.EndWith(".scene.json"));
                Assert.That(File.Exists(Path.Combine(EngineProjectRoot, descriptor.AssetPath)), Is.True, descriptor.AssetPath);
            }
        }

        [Test]
        public void EngineProject_FactoryConstructsEveryScene()
        {
            EngineProject project = OpenEngineProjectForIds();
            foreach (string id in project.Ids)
            {
                IEngineScene scene = project.Create(id);
                Assert.That(scene, Is.Not.Null, id);
                Assert.That(scene.Id, Is.EqualTo(id));
                Assert.That(scene.CameraDefaults.Distance, Is.GreaterThan(0f), id);
                Assert.That(scene.CameraDefaults.FovyDegrees, Is.InRange(0f, 180f), id);
                scene.Dispose();
            }
        }

        [Test]
        public void SceneManifest_IsLoadingTruth_InBothDirections()
        {
            string catalogPath = Path.Combine(EngineProjectRoot, "catalog.json");
            foreach ((string SceneId, JsonElement Document) row in ReadSceneDocuments(catalogPath))
            {
                JsonElement manifest = row.Document.GetProperty("assets");
                var declared = new HashSet<string>();
                foreach (JsonElement asset in manifest.EnumerateArray())
                {
                    declared.Add(asset.GetProperty("id").GetString() ?? string.Empty);
                    string kind = asset.GetProperty("kind").GetString() ?? string.Empty;
                    Assert.That(kind, Is.Not.Empty, $"{row.SceneId} asset kind");
                    string source = asset.GetProperty("source").GetString() ?? string.Empty;
                    Assert.That(source, Does.Not.StartWith("/"), $"{row.SceneId} source must be project-relative");
                    Assert.That(source.Replace('\\', '/'), Does.Not.Contain("../"), $"{row.SceneId} source must not escape");
                }

                var referenced = new HashSet<string>();
                foreach (JsonElement node in row.Document.GetProperty("nodes").EnumerateArray())
                {
                    foreach (JsonElement component in node.GetProperty("components").EnumerateArray())
                    {
                        Type? componentType = FindComponentType(component.GetProperty("type").GetString() ?? string.Empty);
                        Assert.That(componentType, Is.Not.Null, $"{row.SceneId} unknown component kind");
                        bool declaresAssets = component.TryGetProperty("assets", out JsonElement list) &&
                            list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0;
                        bool consumesAssets = typeof(IEngineSceneComponentAssets).IsAssignableFrom(componentType!);
                        Assert.That(declaresAssets, Is.EqualTo(consumesAssets),
                            $"{row.SceneId} component '{componentType!.Name}' assets declaration must match its consumption contract");
                        if (!declaresAssets)
                        {
                            continue;
                        }

                        foreach (JsonElement reference in list.EnumerateArray())
                        {
                            string id = reference.GetString() ?? string.Empty;
                            Assert.That(declared.Contains(id), Is.True, $"{row.SceneId} references undeclared asset '{id}'");
                            referenced.Add(id);
                        }
                    }
                }

                foreach (string id in declared)
                {
                    Assert.That(referenced.Contains(id), Is.True, $"{row.SceneId} declares asset '{id}' that no component references");
                }
            }
        }

        [Test]
        public void SceneComponentSources_HaveNoHardcodedAssetUris()
        {
            foreach (string file in Directory.EnumerateFiles(
                Path.Combine(RepoRoot, "src", "Content", "Ludots.Content.EngineGallery", "Scenes"), "*.cs"))
            {
                foreach (string line in File.ReadAllLines(file))
                {
                    Assert.That(line, Does.Not.Contain(".glb\""), $"{file}: {line.Trim()}");
                    Assert.That(line, Does.Not.Contain(".height\""), $"{file}: {line.Trim()}");
                    Assert.That(line, Does.Not.Contain(".grid\""), $"{file}: {line.Trim()}");
                }
            }
        }

        [Test]
        public void WorldSide_ProductionCode_DoesNotReachIntoEngineProject()
        {
            foreach (string directory in new[] { Path.Combine(RepoRoot, "src", "Core"), Path.Combine(RepoRoot, "src", "Adapters") })
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    Assert.That(File.ReadAllText(file), Does.Not.Contain("engine_gallery/"),
                        $"{file} must bind engine assets through host_assets URIs, not reach into the engine project");
                }
            }
        }

        [Test]
        public void SceneDocument_UnknownComponentKind_FailsLoud()
        {
            EngineProject project = OpenEngineProjectForIds();
            EngineSceneDocument document = ParseMinimalScene(kind: "no_such_component");
            Assert.That(
                () => project.ComposeScene(document, "minimal.scene.json"),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("unknown component kind 'no_such_component'"));
        }

        [Test]
        public void SceneDocument_ComponentWithoutConsumptionContract_FailsLoud()
        {
            EngineProject project = OpenEngineProjectForIds();
            EngineSceneDocument document = ParseMinimalScene(kind: "skybox", assets: ["skybox.mannequin"], manifest:
            [
                new EngineSceneAssetDocument { Id = "skybox.mannequin", Kind = "model", Source = "Models/mannequin_large_walk.glb" },
            ]);
            Assert.That(
                () => project.ComposeScene(document, "minimal.scene.json"),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("declares assets but does not consume"));
        }

        [Test]
        public void SceneDocument_MissingAssetFile_FailsLoud()
        {
            EngineProject project = OpenEngineProjectForIds();
            EngineSceneDocument document = ParseMinimalScene(kind: "crowd_anim", assets: ["crowd_anim.mannequin"], manifest:
            [
                new EngineSceneAssetDocument { Id = "crowd_anim.mannequin", Kind = "model", Source = "Models/__missing.glb" },
            ]);
            Assert.That(
                () => project.ComposeScene(document, "minimal.scene.json"),
                Throws.TypeOf<FileNotFoundException>());
        }

        [Test]
        public void SceneDocument_UnreferencedManifestEntry_FailsLoud()
        {
            Assert.That(
                () => ParseMinimalScene(manifest:
                [
                    new EngineSceneAssetDocument { Id = "skybox.orphan", Kind = "model", Source = "Models/mannequin_large_walk.glb" },
                ]),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("no component references"));
        }

        [Test]
        public void SceneDocument_ReferenceToUndeclaredAsset_FailsLoud()
        {
            Assert.That(
                () => ParseMinimalScene(assets: ["skybox.ghost"]),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("unknown asset 'skybox.ghost'"));
        }

        [Test]
        public void SceneDocument_WrongSchemaVersion_FailsLoud()
        {
            string json = MinimalSceneJson().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
            Assert.That(
                () => EngineProject.ParseSceneDocument(json, "minimal.scene.json"),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("unsupported schema version"));
        }

        [Test]
        public void SceneDocument_InvalidCamera_FailsLoud()
        {
            string json = MinimalSceneJson().Replace("\"fovyDegrees\": 45", "\"fovyDegrees\": 0");
            Assert.That(
                () => EngineProject.ParseSceneDocument(json, "minimal.scene.json"),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("invalid camera values"));
        }

        [Test]
        public void SceneDocument_ParentCycle_FailsLoud()
        {
            string json = MinimalSceneJson()
                .Replace("\"nodes\": [", "\"nodes\": [\n    { \"id\": \"a\", \"parent\": \"b\", \"transform\": { \"position\": [0,0,0], \"rotation\": [0,0,0,1], \"scale\": [1,1,1] }, \"components\": [] },\n    { \"id\": \"b\", \"parent\": \"a\", \"transform\": { \"position\": [0,0,0], \"rotation\": [0,0,0,1], \"scale\": [1,1,1] }, \"components\": [] },");
            Assert.That(
                () => EngineProject.ParseSceneDocument(json, "minimal.scene.json"),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("parent cycle"));
        }

        [Test]
        public void SceneDocument_RootWithParent_FailsLoud()
        {
            string json = MinimalSceneJson()
                .Replace("\"nodes\": [", "\"nodes\": [\n    { \"id\": \"child\", \"parent\": \"root\", \"transform\": { \"position\": [0,0,0], \"rotation\": [0,0,0,1], \"scale\": [1,1,1] }, \"components\": [] },")
                .Replace("\"rootNode\": \"root\"", "\"rootNode\": \"child\"");
            Assert.That(
                () => EngineProject.ParseSceneDocument(json, "minimal.scene.json"),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("cannot declare a parent"));
        }

        [Test]
        public void SceneDocument_EscapingAssetSource_FailsLoud()
        {
            EngineSceneAssetDocument escaping = new() { Id = "skybox.escape", Kind = "model", Source = "../outside.glb" };
            Assert.That(
                () => ParseMinimalScene(manifest: [escaping], assets: ["skybox.escape"]),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("without escaping"));
        }

        private static EngineProject OpenEngineProjectForIds()
        {
            // EngineProject 没有 IDisposable 语义时 using 会失效；这里返回实例由调用方按需持有。
            return OpenEngineProject();
        }

        private static EngineSceneDocument ParseMinimalScene(
            string kind = "skybox",
            string[]? assets = null,
            List<EngineSceneAssetDocument>? manifest = null)
        {
            return EngineProject.ParseSceneDocument(MinimalSceneJson(kind, assets, manifest), "minimal.scene.json");
        }

        private static string MinimalSceneJson(
            string kind = "skybox",
            string[]? assets = null,
            List<EngineSceneAssetDocument>? manifest = null)
        {
            manifest ??= [];
            assets ??= [];
            var manifestJson = string.Join(",\n", manifest.Select(a =>
                $"{{ \"id\": \"{a.Id}\", \"kind\": \"{a.Kind}\", \"source\": \"{a.Source}\" }}"));
            var refsJson = assets.Length > 0 ? $", \"assets\": [{string.Join(", ", assets.Select(a => $"\"{a}\""))}]" : "";
            return $$"""
            {
              "schemaVersion": 1,
              "id": "minimal",
              "title": "最小关卡",
              "summary": "fail-fast 合同测试用最小文档",
              "world": { "units": "meters", "upAxis": "Y", "bounds": { "min": [-8, -2, -8], "max": [8, 12, 8] } },
              "camera": { "mode": "orbit", "target": [0, 0, 0], "distance": 12, "pitchDegrees": 25, "yawDegrees": 45, "fovyDegrees": 45 },
              "assets": [{{manifestJson}}],
              "rootNode": "root",
              "nodes": [
                { "id": "root", "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] },
                  "components": [ { "type": "{{kind}}"{{refsJson}} } ] }
              ]
            }
            """;
        }

        private static Type? FindComponentType(string kind)
        {
            return typeof(Ludots.Content.EngineGallery.Scenes.SkyboxScene).Assembly.GetTypes()
                .FirstOrDefault(t => t.GetCustomAttributes(typeof(EngineSceneComponentAttribute), false)
                    .OfType<EngineSceneComponentAttribute>()
                    .Any(a => a.Kind == kind));
        }

        private static IEnumerable<(string SceneId, JsonElement Document)> ReadSceneDocuments(string catalogPath)
        {
            using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
            foreach (JsonElement entry in catalog.RootElement.GetProperty("scenes").EnumerateArray())
            {
                string asset = entry.GetProperty("asset").GetString() ?? string.Empty;
                string assetPath = Path.Combine(EngineProjectRoot, asset);
                yield return (entry.GetProperty("id").GetString() ?? string.Empty, JsonDocument.Parse(File.ReadAllText(assetPath)).RootElement.Clone());
            }
        }

        private static GalleryRow[] ReadManifestRows(string path)
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(path));
            var rows = new List<GalleryRow>();
            foreach (JsonElement entry in manifest.RootElement.GetProperty("scenes").EnumerateArray())
            {
                string asset = entry.GetProperty("asset").GetString() ?? string.Empty;
                string assetPath = Path.Combine(EngineProjectRoot, asset);
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

                if (string.Equals(GetOptionalString(entry, "status"), "retired", System.StringComparison.Ordinal))
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
