using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Raylib.SceneKit
{
    /// <summary>
    /// 引擎工程：一个目录即一个工程（project.json → 场景目录 → 各关卡容器）。
    /// 播放器/测试用 Open(root) 打开任意工程；任何文档不一致都 fail-fast。
    /// </summary>
    public sealed class EngineProject
    {
        private const int CurrentSchemaVersion = 1;
        internal static readonly string[] FileAssetKinds = ["model", "mesh", "material", "texture", "terrain"];

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        private readonly Dictionary<string, SceneEntry> _entries;

        private EngineProject(
            string root,
            string name,
            IReadOnlyList<string> ids,
            IReadOnlyList<SceneDescriptor> descriptors,
            Dictionary<string, SceneEntry> entries,
            Assembly contentAssembly)
        {
            Root = root;
            Name = name;
            Ids = ids;
            Descriptors = descriptors;
            _entries = entries;
            Components = new EngineSceneComponentRegistry(contentAssembly);
        }

        public string Root { get; }

        public string Name { get; }

        public IReadOnlyList<string> Ids { get; }

        public IReadOnlyList<SceneDescriptor> Descriptors { get; }

        internal EngineSceneComponentRegistry Components { get; }

        /// <summary>最近打开的工程根；工程内相对 URI 的运行时解析以此为第一搜索根。</summary>
        public static string? CurrentRoot => EngineProjectEnvironment.CurrentRoot;

        public static EngineProject Open(string projectRootPath)
        {
            string root = ResolveProjectRoot(projectRootPath);

            EngineProjectDocument project = DeserializeFile<EngineProjectDocument>(Path.Combine(root, "project.json"));
            if (project.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Engine project '{root}' uses unsupported schema version {project.SchemaVersion}; expected {CurrentSchemaVersion}.");
            }

            RequireText(project.Name, root, "name");
            RequireText(project.Scenes, root, "scenes");
            string contentAssemblyName = RequireText(project.ContentAssembly, root, "contentAssembly");

            Assembly contentAssembly;
            try
            {
                contentAssembly = Assembly.Load(contentAssemblyName);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Engine project '{root}' declares content assembly '{contentAssemblyName}' which failed to load; the runtime host must reference it.",
                    exception);
            }

            string catalogRelative = NormalizeProjectRelative(project.Scenes, root, "project.json scenes");
            string catalogPath = Path.Combine(root, catalogRelative);
            SceneCatalogManifest manifest = DeserializeFile<SceneCatalogManifest>(catalogPath);
            if (manifest.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Engine project catalog '{catalogPath}' uses unsupported schema version {manifest.SchemaVersion}; expected {CurrentSchemaVersion}.");
            }

            if (manifest.Scenes.Count == 0)
            {
                throw new InvalidDataException($"Engine project catalog '{catalogPath}' does not declare any scenes.");
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
                    throw new InvalidDataException($"Engine project catalog '{catalogPath}' contains duplicate scene id '{id}'.");
                }

                string sceneRelative = NormalizeProjectRelative(asset, catalogPath, $"scene '{id}' asset");
                string sceneAssetPath = Path.GetFullPath(Path.Combine(root, sceneRelative));
                if (!File.Exists(sceneAssetPath))
                {
                    throw new FileNotFoundException($"Engine project scene asset '{asset}' was not found.", sceneAssetPath);
                }

                EngineSceneDocument document = ParseSceneDocument(File.ReadAllText(sceneAssetPath), sceneAssetPath);
                if (!string.Equals(document.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Engine project catalog entry '{id}' points to scene asset '{sceneAssetPath}' whose id is '{document.Id}'.");
                }

                ids.Add(id);
                descriptors.Add(new SceneDescriptor(document.Id, document.Title, document.Summary, asset));
                entries.Add(id, new SceneEntry(document, sceneAssetPath));
            }

            EngineProject opened = new EngineProject(root, project.Name, ids.ToArray(), descriptors.ToArray(), entries, contentAssembly);
            EngineProjectEnvironment.CurrentRoot = root;
            return opened;
        }

        public bool TryCreate(string id, out IEngineScene? scene)
        {
            if (!_entries.TryGetValue(id, out SceneEntry? entry))
            {
                scene = null;
                return false;
            }

            scene = entry.Create(this);
            return true;
        }

        public IEngineScene Create(string id)
        {
            if (!TryCreate(id, out IEngineScene? scene) || scene == null)
            {
                throw new InvalidOperationException($"Unknown scene '{id}' in project '{Name}'. Available: {string.Join(", ", Ids)}");
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
                throw new InvalidDataException($"Engine scene '{sourceName}' is not valid JSON: {exception.Message}", exception);
            }

            if (document == null)
            {
                throw new InvalidDataException($"Engine scene '{sourceName}' is empty.");
            }

            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Engine scene '{sourceName}' uses unsupported schema version {document.SchemaVersion}; expected {CurrentSchemaVersion}.");
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

        internal IEngineScene ComposeScene(EngineSceneDocument document, string sourceName)
        {
            var assetsById = new Dictionary<string, EngineSceneAssetDocument>(StringComparer.Ordinal);
            foreach (EngineSceneAssetDocument asset in document.Assets)
            {
                assetsById.Add(asset.Id, asset);
            }

            var nodes = new List<EngineSceneNodeRuntime>(document.Nodes.Count);
            foreach (EngineSceneNodeDocument node in document.Nodes)
            {
                var components = new List<IEngineSceneComponent>(node.Components.Count);
                foreach (EngineSceneComponentDocument component in node.Components)
                {
                    IEngineSceneComponent instance = Components.Create(component.Type, sourceName);
                    if (component.Config.HasValue)
                    {
                        if (instance is not IEngineSceneComponentConfigurable configurable)
                        {
                            throw new InvalidDataException(
                                $"Engine scene '{sourceName}' component '{component.Type}' declares config but does not consume component config.");
                        }

                        configurable.Configure(component.Config.Value);
                    }

                    if (component.Assets.Count > 0)
                    {
                        if (instance is not IEngineSceneComponentAssets consumer)
                        {
                            throw new InvalidDataException(
                                $"Engine scene '{sourceName}' component '{component.Type}' declares assets but does not consume manifest assets.");
                        }

                        var resolved = new Dictionary<string, EngineSceneAsset>(StringComparer.Ordinal);
                        foreach (string reference in component.Assets)
                        {
                            EngineSceneAssetDocument declared = assetsById[reference];
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

        private string ResolveAssetSource(EngineSceneAssetDocument asset, string sourceName)
        {
            string normalized = asset.Source.Replace('\\', '/');
            string path = Path.GetFullPath(Path.Combine(Root, normalized));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Engine scene '{sourceName}' asset '{asset.Id}' source '{asset.Source}' was not found in project '{Name}'.", path);
            }

            return path;
        }

        private static string ResolveProjectRoot(string projectRootPath)
        {
            if (string.IsNullOrWhiteSpace(projectRootPath))
            {
                throw new InvalidDataException("Engine project path is required.");
            }

            if (Path.IsPathRooted(projectRootPath))
            {
                return Path.GetFullPath(projectRootPath);
            }

            string cwdRelative = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), projectRootPath));
            if (File.Exists(Path.Combine(cwdRelative, "project.json")))
            {
                return cwdRelative;
            }

            string? directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                string candidate = Path.Combine(directory, projectRootPath);
                if (File.Exists(Path.Combine(candidate, "project.json")))
                {
                    return Path.GetFullPath(candidate);
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new DirectoryNotFoundException($"Engine project '{projectRootPath}' was not found from '{Directory.GetCurrentDirectory()}' or above '{AppContext.BaseDirectory}'.");
        }

        private static void ValidateWorld(EngineSceneWorldDocument? world, string sourceName)
        {
            if (world == null ||
                !string.Equals(world.Units, "meters", StringComparison.Ordinal) ||
                !string.Equals(world.UpAxis, "Y", StringComparison.Ordinal) ||
                world.Bounds == null)
            {
                throw new InvalidDataException(
                    $"Engine scene '{sourceName}' must declare world.units='meters', world.upAxis='Y', and world.bounds.");
            }

            ValidateVector3(world.Bounds.Min, sourceName, "world.bounds.min");
            ValidateVector3(world.Bounds.Max, sourceName, "world.bounds.max");
            if (world.Bounds.Min![0] >= world.Bounds.Max![0] ||
                world.Bounds.Min[1] >= world.Bounds.Max[1] ||
                world.Bounds.Min[2] >= world.Bounds.Max![2])
            {
                throw new InvalidDataException($"Engine scene '{sourceName}' must declare increasing world bounds.");
            }
        }

        private static void ValidateCamera(EngineSceneCameraDocument? camera, string sourceName)
        {
            if (camera == null || !string.Equals(camera.Mode, "orbit", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Engine scene '{sourceName}' must declare camera.mode='orbit'.");
            }

            ValidateVector3(camera.Target, sourceName, "camera.target");
            if (camera.Distance <= 0f || camera.PitchDegrees is < 0f or >= 90f ||
                camera.FovyDegrees is <= 0f or >= 180f)
            {
                throw new InvalidDataException($"Engine scene '{sourceName}' declares invalid camera values.");
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
                if (Array.IndexOf(FileAssetKinds, kind) < 0)
                {
                    throw new InvalidDataException(
                        $"Engine scene '{sourceName}' asset '{id}' uses unknown kind '{kind}'; expected one of {string.Join('/', FileAssetKinds)}.");
                }

                if (!seen.Add(id))
                {
                    throw new InvalidDataException($"Engine scene '{sourceName}' contains duplicate asset id '{id}'.");
                }

                string normalized = asset.Source.Replace('\\', '/');
                if (Path.IsPathRooted(normalized) || normalized.Contains("../", StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Engine scene '{sourceName}' asset '{id}' source must be a project-relative URI without escaping.");
                }
            }
        }

        private static void ValidateNodes(EngineSceneDocument document, string sourceName)
        {
            RequireText(document.RootNode, sourceName, "rootNode");
            if (document.Nodes.Count == 0)
            {
                throw new InvalidDataException($"Engine scene '{sourceName}' must declare at least one node.");
            }

            var nodesById = new Dictionary<string, EngineSceneNodeDocument>(StringComparer.Ordinal);
            bool rootFound = false;
            int componentCount = 0;
            foreach (EngineSceneNodeDocument node in document.Nodes)
            {
                string nodeId = RequireText(node.Id, sourceName, "node id");
                if (nodesById.ContainsKey(nodeId))
                {
                    throw new InvalidDataException($"Engine scene '{sourceName}' contains duplicate node id '{nodeId}'.");
                }

                ValidateTransform(node.Transform, sourceName, nodeId);
                nodesById.Add(nodeId, node);
                rootFound |= string.Equals(nodeId, document.RootNode, StringComparison.Ordinal);
                componentCount += node.Components.Count;
            }

            if (!rootFound)
            {
                throw new InvalidDataException($"Engine scene '{sourceName}' rootNode '{document.RootNode}' is not declared.");
            }

            if (componentCount == 0)
            {
                throw new InvalidDataException($"Engine scene '{sourceName}' must declare at least one component.");
            }

            var referencedAssets = new HashSet<string>(StringComparer.Ordinal);
            var declaredAssets = new HashSet<string>(StringComparer.Ordinal);
            foreach (EngineSceneAssetDocument asset in document.Assets)
            {
                declaredAssets.Add(asset.Id);
            }

            foreach (EngineSceneNodeDocument node in document.Nodes)
            {
                bool isRoot = string.Equals(node.Id, document.RootNode, StringComparison.Ordinal);
                if (isRoot && node.Parent != null)
                {
                    throw new InvalidDataException($"Engine scene '{sourceName}' rootNode '{node.Id}' cannot declare a parent.");
                }

                if (!isRoot && string.IsNullOrWhiteSpace(node.Parent))
                {
                    throw new InvalidDataException($"Engine scene '{sourceName}' non-root node '{node.Id}' must declare a parent.");
                }

                if (node.Parent != null && !nodesById.ContainsKey(node.Parent))
                {
                    throw new InvalidDataException(
                        $"Engine scene '{sourceName}' node '{node.Id}' references unknown parent '{node.Parent}'.");
                }

                var ancestry = new HashSet<string>(StringComparer.Ordinal);
                string current = node.Id;
                while (!string.Equals(current, document.RootNode, StringComparison.Ordinal))
                {
                    if (!ancestry.Add(current))
                    {
                        throw new InvalidDataException($"Engine scene '{sourceName}' contains a parent cycle at node '{current}'.");
                    }

                    current = nodesById[current].Parent!;
                }

                foreach (EngineSceneComponentDocument component in node.Components)
                {
                    RequireText(component.Type, sourceName, $"node '{node.Id}' component type");
                    if (component.Config is { ValueKind: not JsonValueKind.Object })
                    {
                        throw new InvalidDataException(
                            $"Engine scene '{sourceName}' component '{component.Type}' config must be a JSON object.");
                    }

                    foreach (string reference in component.Assets)
                    {
                        if (!declaredAssets.Contains(reference))
                        {
                            throw new InvalidDataException(
                                $"Engine scene '{sourceName}' component '{component.Type}' references unknown asset '{reference}'.");
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
                        $"Engine scene '{sourceName}' declares asset '{asset.Id}' that no component references.");
                }
            }
        }

        private static void ValidateTransform(EngineSceneTransformDocument? transform, string sourceName, string nodeId)
        {
            if (transform == null)
            {
                throw new InvalidDataException($"Engine scene '{sourceName}' node '{nodeId}' must declare a transform.");
            }

            ValidateVector3(transform.Position, sourceName, $"node '{nodeId}' transform.position");
            ValidateVector4(transform.Rotation, sourceName, $"node '{nodeId}' transform.rotation");
            ValidateVector3(transform.Scale, sourceName, $"node '{nodeId}' transform.scale");
        }

        private static void ValidateVector3(List<float>? value, string sourceName, string fieldLabel)
        {
            if (value == null || value.Count != 3 || value.Any(float.IsNaN) || value.Any(float.IsInfinity))
            {
                throw new InvalidDataException($"Engine scene '{sourceName}' field {fieldLabel} must be a finite 3D vector.");
            }
        }

        private static void ValidateVector4(List<float>? value, string sourceName, string fieldLabel)
        {
            if (value == null || value.Count != 4 || value.Any(float.IsNaN) || value.Any(float.IsInfinity))
            {
                throw new InvalidDataException($"Engine scene '{sourceName}' field {fieldLabel} must be a finite 4D vector.");
            }
        }

        private static T DeserializeFile<T>(string path)
        {
            string json = File.ReadAllText(path);
            T? value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return value ?? throw new InvalidDataException($"Engine project asset '{path}' is empty.");
        }

        private static string NormalizeProjectRelative(string relativePath, string sourcePath, string fieldLabel)
        {
            string normalized = relativePath.Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized.Contains("../", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Engine project '{sourcePath}' field {fieldLabel} ('{relativePath}') must stay inside the project root.");
            }

            return normalized;
        }

        private static string RequireText(string? value, string sourceName, string fieldLabel)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            throw new InvalidDataException($"Engine project asset '{sourceName}' is missing required field {fieldLabel}.");
        }

        private sealed record SceneEntry(EngineSceneDocument Document, string AssetPath)
        {
            public IEngineScene Create(EngineProject project)
            {
                return project.ComposeScene(Document, AssetPath);
            }
        }
    }

    /// <summary>当前工程根的环境槽；运行时相对 URI 解析（渲染器 vfs 等）以此为第一搜索根。</summary>
    internal static class EngineProjectEnvironment
    {
        public static string? CurrentRoot { get; set; }
    }

    public sealed record SceneDescriptor(string Id, string Title, string Summary, string AssetPath);

    internal sealed class EngineProjectDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("scenes")]
        public string Scenes { get; set; } = string.Empty;

        [JsonPropertyName("contentAssembly")]
        public string ContentAssembly { get; set; } = string.Empty;
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

        [JsonPropertyName("config")]
        public JsonElement? Config { get; set; }
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

    internal sealed class EngineSceneComponentRegistry
    {
        private readonly Lazy<IReadOnlyDictionary<string, Type>> _components;

        public EngineSceneComponentRegistry(Assembly contentAssembly)
        {
            _components = new Lazy<IReadOnlyDictionary<string, Type>>(
                () => BuildComponents(contentAssembly),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public IEngineSceneComponent Create(string kind, string sceneAssetPath)
        {
            if (!_components.Value.TryGetValue(kind, out Type? type))
            {
                throw new InvalidDataException(
                    $"Engine scene '{sceneAssetPath}' references unknown component kind '{kind}'.");
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
                    $"Failed to instantiate engine component kind '{kind}' from scene '{sceneAssetPath}'.",
                    exception);
            }
        }

        public bool TryGetKind(Type type, out string kind)
        {
            EngineSceneComponentAttribute? attribute = type.GetCustomAttribute<EngineSceneComponentAttribute>();
            kind = attribute?.Kind ?? string.Empty;
            return attribute != null;
        }

        private static IReadOnlyDictionary<string, Type> BuildComponents(Assembly assembly)
        {
Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                string reasons = string.Join("; ", exception.LoaderExceptions.Select(e => e?.Message ?? "unknown").Take(3));
                throw new InvalidDataException(
                    $"Engine content assembly '{assembly.FullName}' failed to load component types: {reasons}", exception);
            }

            var result = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (Type type in types)
            {
                EngineSceneComponentAttribute? attribute = type.GetCustomAttribute<EngineSceneComponentAttribute>();
                if (attribute == null || !typeof(IEngineSceneComponent).IsAssignableFrom(type) || type.IsAbstract)
                {
                    continue;
                }

                if (!result.TryAdd(attribute.Kind, type))
                {
                    throw new InvalidDataException(
                        $"Engine component kind '{attribute.Kind}' is registered by both '{result[attribute.Kind].FullName}' and '{type.FullName}'.");
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
            for (int i = 0; i < _nodes.Count; i++)
            {
                IReadOnlyList<IEngineSceneComponent> components = _nodes[i].Components;
                for (int j = 0; j < components.Count; j++)
                {
                    components[j].Load();
                }
            }
        }

        public void Draw(float deltaSeconds, double totalTimeSeconds, ref Raylib_cs.Camera3D camera)
        {
            ThrowIfDisposed();
            for (int i = 0; i < _nodes.Count; i++)
            {
                IReadOnlyList<IEngineSceneComponent> components = _nodes[i].Components;
                for (int j = 0; j < components.Count; j++)
                {
                    components[j].Draw(deltaSeconds, totalTimeSeconds, ref camera);
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
                for (int j = components.Count - 1; j >= 0; j--)
                {
                    components[j].Dispose();
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
