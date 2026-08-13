using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphBehaviorCommon;

public static class GraphOpsHeadlessGameEngine
{
    public const string CoreModId = "LudotsCoreMod";
    public const string InputModId = "CoreInputMod";
    public const string CameraModId = "CameraProfilesMod";
    public const string GalleryModId = "CapabilityStandardGraphOpsNodeGalleryMod";

    private static readonly object GalleryGate = new();
    private static GameEngine? _galleryEngine;
    private static string? _galleryRepoRoot;

    public static GameEngine Create(string repoRoot, params string[] extraModIds)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new ArgumentException("Repository root is required.", nameof(repoRoot));
        }

        var modIds = new List<string> { CoreModId, InputModId, CameraModId };
        if (extraModIds != null)
        {
            for (int i = 0; i < extraModIds.Length; i++)
            {
                string id = extraModIds[i];
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new ArgumentException("Extra mod id must not be empty.", nameof(extraModIds));
                }

                if (!modIds.Exists(existing => string.Equals(existing, id, StringComparison.Ordinal)))
                {
                    modIds.Add(id);
                }
            }
        }

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            ResolveModPaths(repoRoot, modIds),
            Path.Combine(repoRoot, "assets"));
        InstallNullInput(engine);
        return engine;
    }

    public static GameEngine SharedGallery(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        lock (GalleryGate)
        {
            if (_galleryEngine != null)
            {
                if (!string.Equals(_galleryRepoRoot, repoRoot, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Gallery headless engine is already bound to '{_galleryRepoRoot}', cannot reuse for '{repoRoot}'.");
                }

                return _galleryEngine;
            }

            _galleryEngine = Create(repoRoot, GalleryModId);
            _galleryRepoRoot = repoRoot;
            return _galleryEngine;
        }
    }

    public static void LoadExclusiveMap(GameEngine engine, string mapId)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        if (engine.CurrentMapSession != null)
        {
            engine.UnloadMap(engine.CurrentMapSession.MapId.Value);
        }

        engine.LoadMap(mapId);
        if (engine.CurrentMapSession == null)
        {
            throw new InvalidOperationException($"GameEngine.LoadMap('{mapId}') left CurrentMapSession null.");
        }
    }

    public static string FindRepoRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            throw new ArgumentException("Start path is required.", nameof(startPath));
        }

        var dir = new DirectoryInfo(Path.GetFullPath(startPath));
        if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.Directory) == false)
        {
            dir = dir.Parent;
        }

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Repository root not found from '{startPath}'.");
    }

    public static List<string> ResolveModPaths(string repoRoot, IReadOnlyList<string> modIds)
    {
        List<DiscoveredMod> discovered = ModDiscovery.DiscoverMods(new[] { Path.Combine(repoRoot, "mods") });
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < discovered.Count; i++)
        {
            DiscoveredMod mod = discovered[i];
            byName[mod.Manifest.Name] = mod.DirectoryPath;
        }

        var result = new List<string>(modIds.Count);
        for (int i = 0; i < modIds.Count; i++)
        {
            string id = modIds[i];
            if (!byName.TryGetValue(id, out string? path))
            {
                throw new DirectoryNotFoundException($"Mod not found in repo: {id}");
            }

            result.Add(path);
        }

        return result;
    }

    private static void InstallNullInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        if (engine.MergedConfig?.StartupInputContexts != null)
        {
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, new NullInputBackend());
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}

public sealed class GraphOpsEngineWorld : IDisposable
{
    public GameEngine Engine { get; }
    public World World => Engine.World;
    public bool OwnsEngine { get; }

    private GraphOpsEngineWorld(GameEngine engine, bool ownsEngine)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        OwnsEngine = ownsEngine;
    }

    public static GraphOpsEngineWorld AttachOrCreate(GameEngine? boundEngine, string startPath)
    {
        if (boundEngine != null)
        {
            return new GraphOpsEngineWorld(boundEngine, ownsEngine: false);
        }

        string repoRoot = GraphOpsHeadlessGameEngine.FindRepoRoot(startPath);
        GameEngine engine = GraphOpsHeadlessGameEngine.Create(repoRoot);
        return new GraphOpsEngineWorld(engine, ownsEngine: true);
    }

    public void StartOwnedAndTick()
    {
        if (!OwnsEngine)
        {
            return;
        }

        Engine.Start();
        Engine.Tick(1f / 60f);
    }

    public void Dispose()
    {
        if (OwnsEngine)
        {
            Engine.Dispose();
        }
    }
}
