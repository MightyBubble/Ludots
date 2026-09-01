using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.App.RaylibEngineGallery
{
    public sealed record SceneDescriptor(string Id, string Title, string Summary, string AssetPath);

    public static class SceneCatalog
    {
        private const string CatalogAssetPath = "engine_gallery/catalog.json";
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
                throw new InvalidOperationException($"Unknown gallery scene '{id}'.");
            }

            return scene;
        }

        private static CatalogState LoadState()
        {
            string catalogPath = ResolveAssetPath(CatalogAssetPath);
            SceneCatalogManifest manifest = Deserialize<SceneCatalogManifest>(catalogPath);
            if (manifest.SchemaVersion != 2)
            {
                throw new InvalidDataException($"Engine gallery catalog '{catalogPath}' uses unsupported schema version {manifest.SchemaVersion}.");
            }

            if (manifest.Scenes.Count == 0)
            {
                throw new InvalidDataException($"Engine gallery catalog '{catalogPath}' does not declare any scenes.");
            }

            var ids = new List<string>(manifest.Scenes.Count);
            var descriptors = new List<SceneDescriptor>(manifest.Scenes.Count);
            var entries = new Dictionary<string, SceneEntry>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SceneCatalogManifestEntry item in manifest.Scenes)
            {
                string id = RequireText(item.Id, catalogPath, "id");
                string assetPath = RequireText(item.Asset, catalogPath, $"scene '{id}' asset");
                if (!seen.Add(id))
                {
                    throw new InvalidDataException($"Engine gallery catalog '{catalogPath}' contains duplicate scene id '{id}'.");
                }

                string sceneAssetPath = ResolveAssetPath(assetPath, catalogPath);
                EngineSceneDocument document = LoadSceneDocument(sceneAssetPath, id);
                if (!string.Equals(document.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Engine gallery catalog '{catalogPath}' entry '{id}' points to scene asset '{sceneAssetPath}' whose id is '{document.Id}'.");
                }

                ids.Add(id);
                descriptors.Add(new SceneDescriptor(document.Id, document.Title, document.Summary, assetPath));
                entries.Add(id, new SceneEntry(document, sceneAssetPath));
            }

            return new CatalogState(ids.ToArray(), descriptors.ToArray(), entries);
        }

        private static EngineSceneDocument LoadSceneDocument(string sceneAssetPath, string catalogId)
        {
            EngineSceneDocument document = Deserialize<EngineSceneDocument>(sceneAssetPath);
            if (document.SchemaVersion != 2)
            {
                throw new InvalidDataException($"Engine gallery scene '{catalogId}' asset '{sceneAssetPath}' uses unsupported schema version {document.SchemaVersion}.");
            }

            RequireText(document.Id, sceneAssetPath, "id");
            RequireText(document.Title, sceneAssetPath, "title");
            RequireText(document.Summary, sceneAssetPath, "summary");
            ValidateWorld(document.World, sceneAssetPath);
            ValidateCamera(document.Camera, sceneAssetPath);
            ValidateAssets(document.Assets, sceneAssetPath);
            RequireText(document.RootNode, sceneAssetPath, "rootNode");
            if (document.Nodes.Count == 0)
            {
                throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' must declare at least one node.");
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            var nodesById = new Dictionary<string, EngineSceneNodeDocument>(StringComparer.Ordinal);
            bool rootFound = false;
            int componentCount = 0;
            foreach (EngineSceneNodeDocument node in document.Nodes)
            {
                string nodeId = RequireText(node.Id, sceneAssetPath, "node id");
                if (!nodeIds.Add(nodeId))
                {
                    throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' contains duplicate node id '{nodeId}'.");
                }

                ValidateTransform(node.Transform, sceneAssetPath, nodeId);
                nodesById.Add(nodeId, node);
                if (string.Equals(nodeId, document.RootNode, StringComparison.Ordinal))
                {
                    rootFound = true;
                }

                foreach (EngineSceneComponentDocument component in node.Components)
                {
                    RequireText(component.Type, sceneAssetPath, $"node '{nodeId}' component type");
                    componentCount++;
                }
            }

            if (!rootFound)
            {
                throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' rootNode '{document.RootNode}' is not declared.");
            }

            foreach (EngineSceneNodeDocument node in document.Nodes)
            {
                bool isRoot = string.Equals(node.Id, document.RootNode, StringComparison.Ordinal);
                if (isRoot && node.Parent != null)
                {
                    throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' rootNode '{node.Id}' cannot declare a parent.");
                }

                if (!isRoot && string.IsNullOrWhiteSpace(node.Parent))
                {
                    throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' non-root node '{node.Id}' must declare a parent.");
                }

                if (node.Parent != null && !nodesById.ContainsKey(node.Parent))
                {
                    throw new InvalidDataException(
                        $"Engine gallery scene asset '{sceneAssetPath}' node '{node.Id}' references unknown parent '{node.Parent}'.");
                }

                var ancestry = new HashSet<string>(StringComparer.Ordinal);
                string current = node.Id;
                while (!string.Equals(current, document.RootNode, StringComparison.Ordinal))
                {
                    if (!ancestry.Add(current))
                    {
                        throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' contains a parent cycle at node '{current}'.");
                    }

                    EngineSceneNodeDocument currentNode = nodesById[current];
                    current = currentNode.Parent!;
                }
            }

            if (componentCount == 0)
            {
                throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' must declare at least one component.");
            }

            return document;
        }

        private static void ValidateWorld(EngineSceneWorldDocument? world, string sceneAssetPath)
        {
            if (world == null ||
                !string.Equals(world.Units, "meters", StringComparison.Ordinal) ||
                !string.Equals(world.UpAxis, "Y", StringComparison.Ordinal) ||
                world.Bounds == null)
            {
                throw new InvalidDataException(
                    $"Engine gallery scene asset '{sceneAssetPath}' must declare world.units='meters', world.upAxis='Y', and world.bounds.");
            }

            ValidateVector3(world.Bounds.Min, sceneAssetPath, "world.bounds.min");
            ValidateVector3(world.Bounds.Max, sceneAssetPath, "world.bounds.max");
            if (world.Bounds.Min![0] >= world.Bounds.Max![0] ||
                world.Bounds.Min[1] >= world.Bounds.Max[1] ||
                world.Bounds.Min[2] >= world.Bounds.Max[2])
            {
                throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' must declare increasing world bounds.");
            }
        }

        private static void ValidateCamera(EngineSceneCameraDocument? camera, string sceneAssetPath)
        {
            if (camera == null || !string.Equals(camera.Mode, "orbit", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' must declare camera.mode='orbit'.");
            }

            ValidateVector3(camera.Target, sceneAssetPath, "camera.target");
            if (camera.Distance <= 0f || camera.PitchDegrees is < 0f or > 90f ||
                camera.FovyDegrees is <= 0f or >= 180f)
            {
                throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' declares invalid camera values.");
            }
        }

        private static void ValidateAssets(List<EngineSceneAssetDocument>? assets, string sceneAssetPath)
        {
            if (assets == null || assets.Count == 0)
            {
                throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' must declare at least one asset reference.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (EngineSceneAssetDocument asset in assets)
            {
                string assetId = RequireText(asset.Id, sceneAssetPath, "asset id");
                RequireText(asset.Type, sceneAssetPath, $"asset '{assetId}' type");
                RequireText(asset.Source, sceneAssetPath, $"asset '{assetId}' source");
                if (!ids.Add(assetId))
                {
                    throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' contains duplicate asset id '{assetId}'.");
                }
            }
        }

        private static void ValidateTransform(EngineSceneTransformDocument? transform, string sceneAssetPath, string nodeId)
        {
            if (transform == null)
            {
                throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' node '{nodeId}' must declare a transform.");
            }

            ValidateVector3(transform.Position, sceneAssetPath, $"node '{nodeId}' transform.position");
            ValidateVector4(transform.Rotation, sceneAssetPath, $"node '{nodeId}' transform.rotation");
            ValidateVector3(transform.Scale, sceneAssetPath, $"node '{nodeId}' transform.scale");
        }

        private static void ValidateVector3(List<float>? value, string sceneAssetPath, string fieldLabel)
        {
            if (value == null || value.Count != 3 || value.Any(float.IsNaN) || value.Any(float.IsInfinity))
            {
                throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' field {fieldLabel} must be a finite 3D vector.");
            }
        }

        private static void ValidateVector4(List<float>? value, string sceneAssetPath, string fieldLabel)
        {
            if (value == null || value.Count != 4 || value.Any(float.IsNaN) || value.Any(float.IsInfinity))
            {
                throw new InvalidDataException($"Engine gallery scene asset '{sceneAssetPath}' field {fieldLabel} must be a finite 4D vector.");
            }
        }

        private static T Deserialize<T>(string path)
        {
            string json = File.ReadAllText(path);
            T? value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return value ?? throw new InvalidDataException($"Engine gallery asset '{path}' is empty.");
        }

        private static string ResolveAssetPath(string relativePath, string? sourcePath = null)
        {
            if (Path.IsPathRooted(relativePath))
            {
                throw new InvalidDataException($"Engine gallery asset path '{relativePath}' must be relative.");
            }

            string normalized = relativePath.Replace('\\', '/');
            if (normalized.Contains("../", StringComparison.Ordinal) ||
                normalized.StartsWith("../", StringComparison.Ordinal) ||
                normalized.Contains("..\\", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Engine gallery asset path '{relativePath}' escapes the asset root.");
            }

            string rootRelative = normalized.StartsWith("engine_gallery/", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : $"engine_gallery/{normalized}";
            if (!GalleryAssetPaths.Instance.TryResolveFullPath(rootRelative, out string path) || !File.Exists(path))
            {
                string origin = sourcePath == null ? CatalogAssetPath : sourcePath;
                throw new FileNotFoundException($"Engine gallery asset '{relativePath}' referenced by '{origin}' was not found.", path);
            }

            return path;
        }

        private static string RequireText(string? value, string sourcePath, string fieldLabel)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            throw new InvalidDataException($"Engine gallery asset '{sourcePath}' is missing required field {fieldLabel}.");
        }

        private sealed record SceneEntry(EngineSceneDocument Document, string AssetPath)
        {
            public IEngineScene Create()
            {
                var nodes = new List<EngineSceneNodeRuntime>(Document.Nodes.Count);
                foreach (EngineSceneNodeDocument node in Document.Nodes)
                {
                    var components = new List<IEngineSceneComponent>(node.Components.Count);
                    foreach (EngineSceneComponentDocument component in node.Components)
                    {
                        IEngineSceneComponent instance = EngineSceneComponentRegistry.Create(component.Type, AssetPath);
                        if (instance is IEngineSceneComponentConfigurable consumer && component.Config.HasValue)
                        {
                            consumer.Configure(component.Config.Value);
                        }

                        components.Add(instance);
                    }

                    EngineSceneTransformDocument transform = node.Transform!;
                    nodes.Add(new EngineSceneNodeRuntime(
                        node.Id,
                        node.Parent,
                        new EngineSceneNodeTransform(
                            new Vector3(transform.Position![0], transform.Position[1], transform.Position[2]),
                            new Quaternion(transform.Rotation![0], transform.Rotation[1], transform.Rotation[2], transform.Rotation[3]),
                            new Vector3(transform.Scale![0], transform.Scale[1], transform.Scale[2])),
                        components));
                }

                return new CompositeEngineScene(Document, nodes);
            }
        }

        private sealed record CatalogState(
            IReadOnlyList<string> Ids,
            IReadOnlyList<SceneDescriptor> Descriptors,
            IReadOnlyDictionary<string, SceneEntry> Entries);

        private sealed class SceneCatalogManifest
        {
            [JsonPropertyName("schemaVersion")]
            public int SchemaVersion { get; set; }

            [JsonPropertyName("scenes")]
            public List<SceneCatalogManifestEntry> Scenes { get; set; } = [];
        }

        private sealed class SceneCatalogManifestEntry
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("asset")]
            public string? Asset { get; set; }
        }
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
        public List<EngineSceneAssetDocument>? Assets { get; set; }

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

    internal sealed class EngineSceneAssetDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;
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

    internal sealed class EngineSceneComponentDocument
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("config")]
        public JsonElement? Config { get; set; }
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
                    if (component is IEngineSceneNodeAware nodeAware)
                    {
                        nodeAware.SetNodeTransform(node.Transform);
                    }

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

    public readonly record struct EngineSceneCameraDefaults(
        float Distance,
        float PitchDegrees,
        float YawDegrees,
        Vector3 Target,
        float FovyDegrees);

    internal sealed record EngineSceneNodeRuntime(
        string Id,
        string? Parent,
        EngineSceneNodeTransform Transform,
        IReadOnlyList<IEngineSceneComponent> Components);

    internal static class EngineSceneComponentRegistry
    {
        private static readonly Lazy<IReadOnlyDictionary<string, Type>> Components = new(BuildComponents, true);

            public static IEngineSceneComponent Create(string kind, string sceneAssetPath)
        {
            if (!Components.Value.TryGetValue(kind, out Type? type))
            {
                throw new InvalidDataException(
                    $"Engine gallery scene asset '{sceneAssetPath}' references unknown component kind '{kind}'.");
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
                    $"Failed to instantiate engine gallery component kind '{kind}' from scene asset '{sceneAssetPath}'.",
                    exception);
            }
        }

        private static IReadOnlyDictionary<string, Type> BuildComponents()
        {
            var result = new Dictionary<string, Type>(StringComparer.Ordinal);
            Assembly assembly = typeof(SceneCatalog).Assembly;
            foreach (Type type in assembly.GetTypes())
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
}
