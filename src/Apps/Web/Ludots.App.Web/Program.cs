using System.Text.Json;
using Ludots.Adapter.Web;
using Ludots.Adapter.Web.Streaming;
using Ludots.Core.Hosting;

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
var configFile = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "launcher.runtime.json";
var launchOptions = WebServerLaunchOptions.Resolve(baseDir, configFile);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(launchOptions.ListenUrl);

var app = builder.Build();

var gameHost = new WebGameHost(baseDir, configFile);

var cts = new CancellationTokenSource();
var setupReady = new TaskCompletionSource<WebHostSetup>(TaskCreationOptions.RunContinuationsAsynchronously);
var gameLoopTask = Task.Factory.StartNew(
    () =>
    {
        try
        {
            WebHostSetup loopSetup = gameHost.Setup;
            setupReady.TrySetResult(loopSetup);
            gameHost.Run(cts.Token);
        }
        catch (Exception ex)
        {
            setupReady.TrySetException(ex);
            throw;
        }
    },
    CancellationToken.None,
    TaskCreationOptions.LongRunning,
    TaskScheduler.Default);
var setup = setupReady.Task.GetAwaiter().GetResult();
_ = gameLoopTask.ContinueWith(
    t =>
    {
        Exception? taskFault = t.Exception?.GetBaseException() ?? t.Exception;
        Exception fault = taskFault ?? new InvalidOperationException("Web game loop faulted without exception details.");
        setup.LoopStatus.MarkFaulted(fault);
        Console.Error.WriteLine($"[GameLoop FAULTED] {t.Exception}");
    },
    CancellationToken.None,
    TaskContinuationOptions.OnlyOnFaulted,
    TaskScheduler.Default);

app.UseWebSockets();

var clientPath = launchOptions.ClientDistributionDirectory;
if (Directory.Exists(clientPath))
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(clientPath) });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(clientPath) });
}

app.MapGet("/health", () =>
{
    var sessions = setup.Transport.GetSessionInfo();
    WebHostLoopHealthSnapshot loop = setup.LoopStatus.CaptureHealthSnapshot();
    var payload = new
    {
        status = loop.Status,
        loop = new
        {
            loop.Healthy,
            loop.Running,
            loop.Faulted,
            loop.FaultType,
            loop.FaultMessage,
        },
        clients = sessions.Count,
        tick = setup.Engine.GameSession?.CurrentTick ?? 0,
        sessions = sessions.Select(s => new { s.Id, s.FramesSent, s.BytesSent, s.FramesDropped }),
    };

    return Results.Json(
        payload,
        statusCode: loop.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.Map("/ws", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    await setup.Transport.HandleClientAsync(ws, cts.Token);
});

Console.WriteLine($"Web server starting on {launchOptions.ListenUrl} ...");
Console.WriteLine($"Browser launch URL: {launchOptions.LaunchUrl}");
Console.WriteLine($"Static files: {(Directory.Exists(clientPath) ? clientPath : $"NOT FOUND: {clientPath}")}");

app.Lifetime.ApplicationStopping.Register(() =>
{
    cts.Cancel();
    gameLoopTask.Wait(TimeSpan.FromSeconds(5));
});

app.Run();

internal sealed record WebServerLaunchOptions(
    string ListenUrl,
    string LaunchUrl,
    string ClientDistributionDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static WebServerLaunchOptions Resolve(string baseDir, string configFile)
    {
        string defaultClientPath = Path.GetFullPath(Path.Combine(
            baseDir,
            "..",
            "..",
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "Client",
            "Web",
            "dist"));
        var defaultOptions = new WebServerLaunchOptions(
            "http://0.0.0.0:5200",
            "http://localhost:5200",
            defaultClientPath);

        string bootstrapPath = Path.IsPathRooted(configFile)
            ? Path.GetFullPath(configFile)
            : Path.GetFullPath(Path.Combine(baseDir, configFile));
        if (!File.Exists(bootstrapPath))
        {
            return defaultOptions;
        }

        AppBootstrapConfig bootstrap = ReadJson<AppBootstrapConfig>(bootstrapPath, "launcher bootstrap");
        string? graphPath = ResolveGraphPath(baseDir, bootstrapPath, bootstrap);
        if (string.IsNullOrWhiteSpace(graphPath))
        {
            return defaultOptions;
        }

        LauncherGraphDocument graph = ReadJson<LauncherGraphDocument>(graphPath, "launcher graph");
        bool isWebGpu = string.Equals(graph.Adapter.Id, "webgpu", StringComparison.OrdinalIgnoreCase);
        string launchUrl = graph.Adapter.LaunchUrl;
        string clientPath = graph.Adapter.ClientDistributionDirectory;

        if (isWebGpu)
        {
            if (string.IsNullOrWhiteSpace(launchUrl))
            {
                throw new InvalidOperationException("WebGPU launch graph is missing adapter.launchUrl; browser play cannot start.");
            }

            if (string.IsNullOrWhiteSpace(clientPath))
            {
                throw new InvalidOperationException("WebGPU launch graph is missing adapter.clientDistributionDirectory; browser WebGPU client cannot be served.");
            }
        }

        if (string.IsNullOrWhiteSpace(launchUrl) || string.IsNullOrWhiteSpace(clientPath))
        {
            return defaultOptions;
        }

        clientPath = Path.IsPathRooted(clientPath)
            ? Path.GetFullPath(clientPath)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(graphPath) ?? baseDir, clientPath));

        return new WebServerLaunchOptions(
            ToListenUrl(launchUrl),
            launchUrl,
            clientPath);
    }

    private static string? ResolveGraphPath(string baseDir, string bootstrapPath, AppBootstrapConfig bootstrap)
    {
        string? graphPath = ResolveBootstrapRelativePath(baseDir, bootstrapPath, bootstrap.LaunchGraphPath);
        string? fullGraphPath = ResolveBootstrapRelativePath(baseDir, bootstrapPath, bootstrap.LaunchGraphFullPath);
        if (graphPath != null &&
            fullGraphPath != null &&
            !string.Equals(Path.GetFullPath(graphPath), Path.GetFullPath(fullGraphPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Launcher bootstrap has conflicting launch graph pointers: '{graphPath}' and '{fullGraphPath}'.");
        }

        string? resolved = fullGraphPath ?? graphPath;
        if (!string.IsNullOrWhiteSpace(resolved) && !File.Exists(resolved))
        {
            throw new FileNotFoundException($"Launch graph not found: {resolved}");
        }

        return resolved;
    }

    private static string? ResolveBootstrapRelativePath(string baseDir, string bootstrapPath, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        return Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(bootstrapPath) ?? baseDir, candidate));
    }

    private static T ReadJson<T>(string path, string label)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException($"Parsed {label} is null: {path}");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to parse {label}: {path}: {ex.Message}", ex);
        }
    }

    private static string ToListenUrl(string launchUrl)
    {
        if (!Uri.TryCreate(launchUrl, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException($"Invalid browser launch URL: {launchUrl}");
        }

        return $"{uri.Scheme}://0.0.0.0:{uri.Port}";
    }
}
