using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.App.RaylibEngineGallery
{
    public sealed record SceneDescriptor(string Id, string Title, string Summary, string AssetPath);

    /// <summary>
    /// 引擎工程运行时目录：装载 project.json → catalog.json → 各关卡容器，
    /// 严格校验后按组件特性注册组合出 IEngineScene。任何文档不一致都 fail-fast。
    /// </summary>
    public static class SceneCatalog
    {
        private const string ProjectAssetPath = "engine_gallery/project.json";
        private const int CurrentSchemaVersion = 1;
        private static readonly string[] FileAssetKinds = ["model", "mesh", "material", "texture", "terrain"];

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        private static readonly Lazy<CatalogState> State = new(LoadState, true);

        public static IReadOnlyList<string> Ids => State.Value.Ids;

        public static IReadOnlyList<SceneDescriptor> Descriptors => State.Value.Descriptors;

        public static bool TryCreate(string id, out IEngineScene? scene)
        {
            if (!State.Value.Entries.TryGetValue(id, out SceneEntry? entry))
            {
                scene = null;
                return false;
            }

            scene = entry.Create();
            return true;
        }

        public static IEngineScene Create(string id)
        {
            if (!TryCreate(id, out IEngineScene? scene) || scene == null)
            {
                throw new InvalidOperationException($"Unknown gallery scene '{id}'. Available: {string.Join(", ", Ids)}");
            }

            return scene;
        }

        internal static EngineSceneDocument ParseSceneDocument(string json, string sourceName)
        {
            EngineSceneDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<EngineSceneDocument>(json, JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Engine gallery scene '{sourceName}' is not valid JSON: {exception.Message}", exception);
            }

            if (document == null)
            {
                throw new InvalidDataException($"Engine gallery scene '{sourceName}' is empty.");
            }

            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Engine gallery scene '{sourceName}' uses unsupported schema version {document.SchemaVersion}; expected {CurrentSchemaVersion}.");
            }

            RequireText(document.Id, sourceName, "id");
            RequireText(document.Title, sourceName, "title");
            RequireText(document.Summary, sourceName, "summary");
            ValidateWorld(document.World, sourceName);
            ValidateCamera(document.Camera, sourceName);
            ValidateManifest(document.Assets, sourceName);
            ValidateNodes(document, sourceName);
            return document;
        }

        internal static IEngineScene ComposeScene(EngineSceneDocument document, string sourceName)
        {
            var nodes = new List<EngineSceneNodeRuntime>(document.Nodes.Count);
            foreach (EngineSceneNodeDocument node in document.Nodes)
            {
                var components = new List<IEngineSceneComponent>(node.Components.Count);
                foreach (EngineSceneComponentDocument component in node.Components)
                {
                    IEngineSceneComponent instance = EngineSceneComponentRegistry.Create(component.Type, sourceName);
                    if (component.Assets.Count > 0)
                    {
                        if (instance is not IEngineSceneComponentAssets consumer)
                        {
                            throw new InvalidDataException(
                                $"Engine gallery scene '{sourceName}' component '{component.Type}' declares assets but does not consume manifest assets.");
                        }

                        var resolved = new Dictionary<string, EngineSceneAsset>(StringComparer.Ordinal);
                        foreach (string reference in component.Assets)
                        {
                            EngineSceneAssetDocument declared = document.Assets.First(a => a.Id == reference);
                            resolved.Add(declared.Id, new EngineSceneAsset(declared.Id, declared.Kind, declared.Source, ResolveAssetSource(declared, sourceName)));
                        }

                        consumer.SetAssets(resolved);
                    }

                    components.Add(instance);
                }

                nodes.Add(new EngineSceneNodeRuntime(node.Id, node.Parent, components));
            }

            return new CompositeEngineScene(document, nodes);
        }

        private static CatalogState LoadState()
        {
            string projectPath = ResolveEngineProjectAsset(ProjectAssetPath);
            EngineProjectDocument project = DeserializeFile<EngineProjectDocument>(projectPath);
            if (project.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Engine gallery project '{projectPath}' uses unsupported schema version {project.SchemaVersion}; expected {CurrentSchemaVersion}.");
            }

            RequireText(project.Name, projectPath, "name");
            RequireText(project.Scenes, projectPath, "scenes");

            string catalogPath = ResolveEngineProjectAsset(project.Scenes);
            SceneCatalogManifest manifest = DeserializeFile<SceneCatalogManifest>(catalogPath);
            if (manifest.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Engine gallery catalog '{catalogPath}' uses unsupported schema version {manifest.SchemaVersion}; expected {CurrentSchemaVersion}.");
            }

            if (manifest.Scenes.Count == 0)
            {
                throw new InvalidDataException($"Engine gallery catalog '{catalogPath}' does not declare any scenes.");
            }

            var ids = new List<string>(manifest.Scenes.Count);
            var descriptors = new List<SceneDescriptor>(manifest.Scenes.Count);
            var entries = new Dictionary<string, SceneEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (SceneCatalogManifestEntry item in manifest.Scenes)
            {
                string id = RequireText(item.Id, catalogPath, "id");
                string asset = RequireText(item.Asset, catalogPath, $"scene '{id}' asset");
                if (entries.ContainsKey(id))
                {
                    throw new InvalidDataException($"Engine gallery catalog '{catalogPath}' contains duplicate scene id '{id}'.");
                }

                string sceneAssetPath = ResolveEngineProjectAsset(asset, catalogPath);
                EngineSceneDocument document = LoadSceneDocument(sceneAssetPath, id);
                if (!string.Equals(document.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Engine gallery catalog '{catalogPath}' entry '{id}' points to scene asset '{sceneAssetPath}' whose id is '{document.Id}'.");
                }

                ids.Add(id);
                descriptors.Add(new SceneDescriptor(document.Id, document.Title, document.Summary, asset));
                entries.Add(id, new SceneEntry(document, sceneAssetPath));
            }

            return new CatalogState(ids.ToArray(), descriptors.ToArray(), entries);
        }

        private static EngineSceneDocument LoadSceneDocument(string sceneAssetPath, string catalogId)
        {
            EngineSceneDocument document = ParseSceneDocument(File.ReadAllText(sceneAssetPath), sceneAssetPath);
            if (!string.Equals(document.Id, catalogId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Engine gallery catalog entry '{catalogId}' points to scene asset '{sceneAssetPath}' whose id is '{document.Id}'.");
            }

            return document;
        }

        private static void ValidateWorld(EngineSceneWorldDocument? world, string sourceName)
        {
            if (world == null ||
                !string.Equals(world.Units, "meters", StringComparison.Ordinal) ||
                !string.Equals(world.UpAxis, "Y", StringComparison.Ordinal) ||
                world.Bounds == null)
            {
                throw new InvalidDataException(
                    $"Engine gallery scene '{sourceName}' must declare world.units='meters', world.upAxis='Y', and world.bounds.");
            }

            ValidateVector3(world.Bounds.Min, sourceName, "world.bounds.min");
            ValidateVector3(world.Bounds.Max, sourceName, "world.bounds.max");
            if (world.Bounds.Min![0] >= world.Bounds.Max![0] ||
                world.Bounds.Min[1] >= world.Bounds.Max[1] ||
                world.Bounds.Min[2] >= world.Bounds.Max![2])
            {
                throw new InvalidDataException($"Engine gallery scene '{sourceName}' must declare increasing world bounds.");
            }
        }

        private static void ValidateCamera(EngineSceneCameraDocument? camera, string sourceName)
        {
            if (camera == null || !string.Equals(camera.Mode, "orbit", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Engine gallery scene '{sourceName}' must declare camera.mode='orbit'.");
            }

            ValidateVector3(camera.Target, sourceName, "camera.target");
            if (camera.Distance <= 0f || camera.PitchDegrees is < 0f or > 90f ||
                camera.FovyDegrees is <= 0f or >= 180f)
            {
                throw new InvalidDataException($"Engine gallery scene '{sourceName}' declares invalid camera values.");
            }
        }

        private static void ValidateManifest(List<EngineSceneAssetDocument> assets, string sourceName)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (EngineSceneAssetDocument asset in assets)
            {
                string id = RequireText(asset.Id, sourceName, "asset id");
                string kind = RequireText(asset.Kind, sourceName, $"asset '{id}' kind");
                RequireText(asset.Source, sourceName, $"asset '{id}' source");
                if (!FileAssetKinds.Contains(kind, StringComparer.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Engine gallery scene '{sourceName}' asset '{id}' uses unknown kind '{kind}'; expected one of {string.Join('/', FileAssetKinds)}.");
                }

                if (!seen.Add(id))
                {
                    throw new InvalidDataException($"Engine gallery scene '{sourceName}' contains duplicate asset id '{id}'.");
                }

                string normalized = asset.Source.Replace('\\', '/');
                if (Path.IsPathRooted(normalized) || normalized.Contains("../", StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Engine gallery scene '{sourceName}' asset '{id}' source must be a project-relative URI without escaping.");
                }
            }
        }

        private static void ValidateNodes(EngineSceneDocument document, string sourceName)
        {
            RequireText(document.RootNode, sourceName, "rootNode");
            if (document.Nodes.Count == 0)
            {
                throw new InvalidDataException($"Engine gallery scene '{sourceName}' must declare at least one node.");
            }

            var nodesById = new Dictionary<string, EngineSceneNodeDocument>(StringComparer.Ordinal);
            bool rootFound = false;
            int componentCount = 0;
            foreach (EngineSceneNodeDocument node in document.Nodes)
            {
                string nodeId = RequireText(node.Id, sourceName, "node id");
                if (nodesById.ContainsKey(nodeId))
                {
                    throw new InvalidDataException($"Engine gallery scene '{sourceName}' contains duplicate node id '{nodeId}'.");
                }

                ValidateTransform(node.Transform, sourceName, nodeId);
                nodesById.Add(nodeId, node);
                rootFound |= string.Equals(nodeId, document.RootNode, StringComparison.Ordinal);
                componentCount += node.Components.Count;
            }

            if (!rootFound)
            {
                throw new InvalidDataException($"Engine gallery scene '{sourceName}' rootNode '{document.RootNode}' is not declared.");
            }

            if (componentCount == 0)
            {
                throw new InvalidDataException($"Engine gallery scene '{sourceName}' must declare at least one component.");
            }

            var referencedAssets = new HashSet<string>(StringComparer.Ordinal);
            foreach (EngineSceneNodeDocument node in document.Nodes)
            {
                bool isRoot = string.Equals(node.Id, document.RootNode, StringComparison.Ordinal);
                if (isRoot && node.Parent != null)
                {
                    throw new InvalidDataException($"Engine gallery scene '{sourceName}' rootNode '{node.Id}' cannot declare a parent.");
                }

                if (!isRoot && string.IsNullOrWhiteSpace(node.Parent))
                {
                    throw new InvalidDataException($"Engine gallery scene '{sourceName}' non-root node '{node.Id}' must declare a parent.");
                }

                if (node.Parent != null && !nodesById.ContainsKey(node.Parent))
                {
                    throw new InvalidDataException(
                        $"Engine gallery scene '{sourceName}' node '{node.Id}' references unknown parent '{node.Parent}'.");
                }

                var ancestry = new HashSet<string>(StringComparer.Ordinal);
                string current = node.Id;
                while (!string.Equals(current, document.RootNode, StringComparison.Ordinal))
                {
                    if (!ancestry.Add(current))
                    {
                        throw new InvalidDataException($"Engine gallery scene '{sourceName}' contains a parent cycle at node '{current}'.");
                    }

                    current = nodesById[current].Parent!;
                }

                foreach (EngineSceneComponentDocument component in node.Components)
                {
                    RequireText(component.Type, sourceName, $"node '{node.Id}' component type");
                    foreach (string reference in component.Assets)
                    {
                        if (document.Assets.All(a => a.Id != reference))
                        {
                            throw new InvalidDataException(
                                $"Engine gallery scene '{sourceName}' component '{component.Type}' references unknown asset '{reference}'.");
                        }

                        referencedAssets.Add(reference);
                    }
                }
            }

            foreach (EngineSceneAssetDocument asset in document.Assets)
            {
                if (!referencedAssets.Contains(asset.Id))
                {
                    throw new InvalidDataException(
                        $"Engine gallery scene '{sourceName}' declares asset '{asset.Id}' that no component references.");
                }
            }
        }

        private static void ValidateTransform(EngineSceneTransformDocument? transform, string sourceName, string nodeId)
        {
            if (transform == null)
            {
                throw new InvalidDataException($"Engine gallery scene '{sourceName}' node '{nodeId}' must declare a transform.");
            }

            ValidateVector3(transform.Position, sourceName, $"node '{nodeId}' transform.position");
            ValidateVector4(transform.Rotation, sourceName, $"node '{nodeId}' transform.rotation");
            ValidateVector3(transform.Scale, sourceName, $"node '{nodeId}' transform.scale");
        }

        private static void ValidateVector3(List<float>? value, string sourceName, string fieldLabel)
        {
            if (value == null || value.Count != 3 || value.Any(float.IsNaN) || value.Any(float.IsInfinity))
            {
                throw new InvalidDataException($"Engine gallery scene '{sourceName}' field {fieldLabel} must be a finite 3D vector.");
            }
        }

        private static void ValidateVector4(List<float>? value, string sourceName, string fieldLabel)
        {
            if (value == null || value.Count != 4 || value.Any(float.IsNaN) || value.Any(float.IsInfinity))
            {
                throw new InvalidDataException($"Engine gallery scene '{sourceName}' field {fieldLabel} must be a finite 4D vector.");
            }
        }

        private static T DeserializeFile<T>(string path)
        {
            string json = File.ReadAllText(path);
            T? value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return value ?? throw new InvalidDataException($"Engine gallery asset '{path}' is empty.");
        }

        private static string ResolveEngineProjectAsset(string relativePath, string? sourcePath = null)
        {
            string normalized = relativePath.Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized.Contains("../", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Engine gallery project asset path '{relativePath}' must stay inside the project root.");
            }

            string rootRelative = normalized.StartsWith("engine_gallery/", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : $"engine_gallery/{normalized}";
            if (!GalleryAssetPaths.Instance.TryResolveFullPath(rootRelative, out string path) || !File.Exists(path))
            {
                string origin = sourcePath == null ? ProjectAssetPath : sourcePath;
                throw new FileNotFoundException($"Engine gallery asset '{relativePath}' referenced by '{origin}' was not found.", path);
            }

            return path;
        }

        private static string ResolveAssetSource(EngineSceneAssetDocument asset, string sourceName)
        {
            string normalized = asset.Source.Replace('\\', '/');
            if (!GalleryAssetPaths.Instance.TryResolveFullPath(normalized, out string path) || !File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Engine gallery scene '{sourceName}' asset '{asset.Id}' source '{asset.Source}' was not found.", path);
            }

            return path;
        }

        private static string RequireText(string? value, string sourceName, string fieldLabel)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            throw new InvalidDataException($"Engine gallery asset '{sourceName}' is missing required field {fieldLabel}.");
        }

        private sealed record SceneEntry(EngineSceneDocument Document, string AssetPath)
        {
            public IEngineScene Create()
            {
                return ComposeScene(Document, AssetPath);
            }
        }

        private sealed record CatalogState(
            IReadOnlyList<string> Ids,
            IReadOnlyList<SceneDescriptor> Descriptors,
            IReadOnlyDictionary<string, SceneEntry> Entries);
    }

    internal sealed class EngineProjectDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("scenes")]
        public string Scenes { get; set; } = string.Empty;
    }

    internal sealed class SceneCatalogManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("scenes")]
        public List<SceneCatalogManifestEntry> Scenes { get; set; } = [];
    }

    internal sealed class SceneCatalogManifestEntry
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("asset")]
        public string? Asset { get; set; }
    }

    internal sealed class EngineSceneDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("world")]
        public EngineSceneWorldDocument? World { get; set; }

        [JsonPropertyName("camera")]
        public EngineSceneCameraDocument? Camera { get; set; }

        [JsonPropertyName("assets")]
        public List<EngineSceneAssetDocument> Assets { get; set; } = [];

        [JsonPropertyName("rootNode")]
        public string RootNode { get; set; } = string.Empty;

        [JsonPropertyName("nodes")]
        public List<EngineSceneNodeDocument> Nodes { get; set; } = [];
    }

    internal sealed class EngineSceneNodeDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("parent")]
        public string? Parent { get; set; }

        [JsonPropertyName("transform")]
        public EngineSceneTransformDocument? Transform { get; set; }

        [JsonPropertyName("components")]
        public List<EngineSceneComponentDocument> Components { get; set; } = [];
    }

    internal sealed class EngineSceneComponentDocument
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<string> Assets { get; set; } = [];
    }

    internal sealed class EngineSceneWorldDocument
    {
        [JsonPropertyName("units")]
        public string Units { get; set; } = string.Empty;

        [JsonPropertyName("upAxis")]
        public string UpAxis { get; set; } = string.Empty;

        [JsonPropertyName("bounds")]
        public EngineSceneBoundsDocument? Bounds { get; set; }
    }

    internal sealed class EngineSceneBoundsDocument
    {
        [JsonPropertyName("min")]
        public List<float>? Min { get; set; }

        [JsonPropertyName("max")]
        public List<float>? Max { get; set; }
    }

    internal sealed class EngineSceneCameraDocument
    {
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = string.Empty;

        [JsonPropertyName("target")]
        public List<float>? Target { get; set; }

        [JsonPropertyName("distance")]
        public float Distance { get; set; }

        [JsonPropertyName("pitchDegrees")]
        public float PitchDegrees { get; set; }

        [JsonPropertyName("yawDegrees")]
        public float YawDegrees { get; set; }

        [JsonPropertyName("fovyDegrees")]
        public float FovyDegrees { get; set; } = 45f;
    }

    internal sealed class EngineSceneTransformDocument
    {
        [JsonPropertyName("position")]
        public List<float>? Position { get; set; }

        [JsonPropertyName("rotation")]
        public List<float>? Rotation { get; set; }

        [JsonPropertyName("scale")]
        public List<float>? Scale { get; set; }
    }

    internal sealed class EngineSceneAssetDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;
    }

    internal sealed record EngineSceneNodeRuntime(
        string Id,
        string? Parent,
        IReadOnlyList<IEngineSceneComponent> Components);

    internal static class EngineSceneComponentRegistry
    {
        private static readonly Lazy<IReadOnlyDictionary<string, Type>> Components = new(BuildComponents, true);

        public static IEngineSceneComponent Create(string kind, string sceneAssetPath)
        {
            if (!Components.Value.TryGetValue(kind, out Type? type))
            {
                throw new InvalidDataException(
                    $"Engine gallery scene '{sceneAssetPath}' references unknown component kind '{kind}'.");
            }

            try
            {
                object? instance = Activator.CreateInstance(type);
                if (instance is not IEngineSceneComponent component)
                {
                    throw new InvalidOperationException($"Type '{type.FullName}' does not implement IEngineSceneComponent.");
                }

                return component;
            }
            catch (Exception exception) when (exception is not InvalidDataException)
            {
                throw new InvalidOperationException(
                    $"Failed to instantiate engine gallery component kind '{kind}' from scene '{sceneAssetPath}'.",
                    exception);
            }
        }

        public static bool TryGetKind(Type type, out string kind)
        {
            EngineSceneComponentAttribute? attribute = type.GetCustomAttribute<EngineSceneComponentAttribute>();
            kind = attribute?.Kind ?? string.Empty;
            return attribute != null;
        }

        private static IReadOnlyDictionary<string, Type> BuildComponents()
        {
            var result = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (Type type in typeof(SceneCatalog).Assembly.GetTypes())
            {
                EngineSceneComponentAttribute? attribute = type.GetCustomAttribute<EngineSceneComponentAttribute>();
                if (attribute == null || !typeof(IEngineSceneComponent).IsAssignableFrom(type) || type.IsAbstract)
                {
                    continue;
                }

                if (!result.TryAdd(attribute.Kind, type))
                {
                    throw new InvalidDataException(
                        $"Engine gallery component kind '{attribute.Kind}' is registered by both '{result[attribute.Kind].FullName}' and '{type.FullName}'.");
                }
            }

            return result;
        }
    }

    internal sealed class CompositeEngineScene : IEngineScene
    {
        private readonly IReadOnlyList<EngineSceneNodeRuntime> _nodes;
        private bool _disposed;

        public CompositeEngineScene(EngineSceneDocument document, IReadOnlyList<EngineSceneNodeRuntime> nodes)
        {
            Id = document.Id;
            Title = document.Title;
            Summary = document.Summary;
            CameraDefaults = new EngineSceneCameraDefaults(
                document.Camera!.Distance,
                document.Camera.PitchDegrees,
                document.Camera.YawDegrees,
                new Vector3(document.Camera.Target![0], document.Camera.Target[1], document.Camera.Target[2]),
                document.Camera.FovyDegrees);
            _nodes = nodes;
        }

        public string Id { get; }

        public string Title { get; }

        public string Summary { get; }

        public EngineSceneCameraDefaults CameraDefaults { get; }

        public void Load()
        {
            ThrowIfDisposed();
            foreach (EngineSceneNodeRuntime node in _nodes)
            {
                foreach (IEngineSceneComponent component in node.Components)
                {
                    component.Load();
                }
            }
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Raylib_cs.Camera3D camera)
        {
            ThrowIfDisposed();
            foreach (EngineSceneNodeRuntime node in _nodes)
            {
                foreach (IEngineSceneComponent component in node.Components)
                {
                    component.Draw(deltaSeconds, totalTimeSeconds, ref camera);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                IReadOnlyList<IEngineSceneComponent> components = _nodes[i].Components;
                for (int componentIndex = components.Count - 1; componentIndex >= 0; componentIndex--)
                {
                    components[componentIndex].Dispose();
                }
            }

            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
