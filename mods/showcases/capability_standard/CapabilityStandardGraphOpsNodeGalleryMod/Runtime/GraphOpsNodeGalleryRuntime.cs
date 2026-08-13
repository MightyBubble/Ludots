using System.Diagnostics;
using System.IO;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public sealed class GraphOpsNodeGalleryRuntime : IDisposable
{
    private readonly GraphShowcaseConfig _config = new() { ThinkPeriodSeconds = 0.35f };
    private string? _assetsRoot;
    private string? _op;
    private GraphOpsNodeVignette? _vignette;
    private GraphControlFlowCompileResult _compiled;
    private IGraphOpsNodeDriver? _driver;
    private GraphOpsNodeDriverContext? _ctx;
    private GraphOpsStageVisuals? _stage;
    private World? _world;
    private GasGraphRuntimeApi? _api;
    private float _accum;
    private bool _visualsSpawned;

    public GraphShowcaseMetrics Metrics { get; } = new();
    public bool IsBound => _op != null;
    public string Title => _vignette?.Title ?? "";
    public string Op => _op ?? "";
    public GraphOpsNodeVignette Vignette =>
        _vignette ?? throw new InvalidOperationException("BindOp required before reading vignette.");
    public IGraphOpsNodeDriver Driver =>
        _driver ?? throw new InvalidOperationException("BindOp required before reading driver.");
    public GraphOpsNodeDriverContext Context =>
        _ctx ?? throw new InvalidOperationException("EnsureWorld required before reading driver context.");

    public void BindStageVisuals(GraphOpsStageVisuals stage)
    {
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
    }

    public void BindFromStartupMapId(string? mapId)
    {
        if (!GraphOpsNodeIds.TryParseOpFromMapId(mapId, out string op))
        {
            throw new InvalidOperationException(
                $"Node gallery requires startupMapId '{GraphOpsNodeIds.ShowcaseIdPrefix}{{Op}}', got '{mapId}'.");
        }

        BindOp(op);
    }

    public void BindOp(string op)
    {
        if (_op != null)
        {
            if (!string.Equals(_op, GraphOpsNodeIds.RequireOpName(op), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Node gallery already bound to '{_op}', cannot bind '{op}'.");
            }

            return;
        }

        _op = GraphOpsNodeIds.RequireOpName(op);
        _assetsRoot = ResolveAssetsRoot();
        _vignette = GraphOpsNodeVignetteLoader.Load(_assetsRoot, _op);
        _compiled = GraphOpsNodeGraphCompiler.Compile(_assetsRoot, _vignette);
        _driver = GraphOpsNodeDriverCatalog.Create(_vignette.Driver);
        Metrics.ShowcaseId = GraphOpsNodeIds.ShowcaseId(_op);
        Metrics.Detail = _vignette.Beat;
    }

    public void EnsureWorld()
    {
        if (_world != null)
        {
            return;
        }

        if (_vignette == null || _driver == null || _assetsRoot == null || _op == null)
        {
            throw new InvalidOperationException("BindOp required before EnsureWorld.");
        }

        if (!GraphKindParser.TryParse(_vignette.GraphKind, out GraphKind kind))
        {
            throw new InvalidOperationException($"Unsupported graphKind '{_vignette.GraphKind}'.");
        }

        _world = World.Create();
        _api = new GasGraphRuntimeApi(_world, spatialQueries: null, eventBus: null, effectRequests: null);
        _ctx = new GraphOpsNodeDriverContext
        {
            AssetsRoot = _assetsRoot,
            Vignette = _vignette,
            Compiled = _compiled,
            Kind = kind,
            FeaturedDest = GraphOpsNodeGraphCompiler.RequireFeaturedDest(_compiled, _vignette),
            SimWorld = _world,
            Api = _api,
            Metrics = Metrics,
            Stage = _stage
        };
        _driver.Seed(_ctx);
        _visualsSpawned = _stage != null;
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        if (_stage != null && !_visualsSpawned)
        {
            _ctx!.Stage = _stage;
            _driver!.Seed(_ctx);
            _visualsSpawned = true;
        }

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds)
        {
            return;
        }

        _accum = 0f;
        _ctx!.Wave++;
        var sw = Stopwatch.StartNew();
        _driver!.Tick(_ctx);
        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs)
        {
            Metrics.MaxThinkMs = Metrics.LastThinkMs;
        }

        Metrics.ThinkWaves++;
    }

    public void DrawOverlay(DebugDrawCommandBuffer debugDraw)
    {
        if (_driver == null || _ctx == null)
        {
            return;
        }

        _driver.DrawOverlay(_ctx, debugDraw);
    }

    public void Dispose()
    {
        _world?.Dispose();
        _world = null;
    }

    public static string ResolveAssetsRoot()
    {
        string? env = Environment.GetEnvironmentVariable("LUDOTS_GRAPHOPS_NODE_ASSETS");
        if (!string.IsNullOrWhiteSpace(env))
        {
            if (!Directory.Exists(env))
            {
                throw new DirectoryNotFoundException($"LUDOTS_GRAPHOPS_NODE_ASSETS does not exist: {env}");
            }

            return env;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string assets = Path.Combine(dir.FullName, "assets");
            if (File.Exists(Path.Combine(dir.FullName, "mod.json")) &&
                Directory.Exists(Path.Combine(assets, "Vignettes")))
            {
                return assets;
            }

            dir = dir.Parent;
        }

        dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new InvalidOperationException("Repository root not found for GraphOps node gallery assets.");
        }

        string repoAssets = Path.Combine(dir.FullName, GraphOpsNodeIds.ModAssetsRelative);
        if (!Directory.Exists(repoAssets))
        {
            throw new DirectoryNotFoundException($"Node gallery assets missing: {repoAssets}");
        }

        return repoAssets;
    }
}
